using InTest.Cli.Clients;
using InTest.Cli.Fixtures;
using InTest.Cli.Naming;
using InTest.Cli.Spec;
using Microsoft.OpenApi;

namespace InTest.Cli.Planning;

public static class TestPlanBuilder
{
    private const string JsonMediaType = "application/json";
    private const string DefaultTag = "Default";
    private const string ContractCategory = "Contract";

    // Decision 5: v1-c generates a declared-error case for 404 only. 400 has no deterministic
    // fixture-free trigger; 401/403 are the auth cases' territory (Task 5); everything else needs
    // conflicting state or input this plan does not create. Widening this set is a scope
    // decision for a later plan, not a constant to extend casually.
    private const int NotFoundStatus = 404;

    // Task 6 review finding: this text used to live twice — once here, once hand-copied into
    // CoverageReport.cs's Contains() match, with a third copy hand-copied again into
    // CoverageReportTests.cs. Reword any of the three independently and the other two silently
    // stop agreeing. Hoisted to a single constant that both the note text below and
    // CoverageReport reference directly, so a reword here is the only place it can happen —
    // there is no second copy left to drift out of sync.
    internal const string NoPathParameterNoteReason =
        "no path parameter to target with an unmatchable value";

    // Decision 3's fixed pair — never read off the operation's declared `responses`, unlike
    // NotFoundStatus above. An auth case exists because the operation declares `security`, so
    // these are the only two statuses it can ever assert.
    private const int UnauthorizedStatus = 401;
    private const int ForbiddenStatus = 403;

    /// <summary>Statuses that carry no body by definition, so a missing schema is correct
    /// rather than a gap.</summary>
    private static readonly HashSet<int> BodilessStatuses = [204, 205, 304];

    /// <param name="client">
    /// The typed-client-invocation opt-in — kind, typeName and the adopter's override map — or
    /// <see langword="null"/> when the project declares no <c>client</c> section, the default and
    /// today's only exercised path. One optional parameter, per
    /// <see cref="ClientPlanningConfig"/>'s own doc comment, not three loose ones; every existing
    /// call site (<c>GenerateCommand</c>, <c>FixturesRepairCommand</c>, every test that calls
    /// <c>Build(document)</c>) compiles unchanged because it defaults to null.
    /// </param>
    public static TestPlan Build(OpenApiDocument document, ClientPlanningConfig? client = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var skipped = new List<SkippedOperation>();
        var notes = new List<CoverageNote>();
        var draft = new List<(string Tag, TestCasePlan Case)>();
        var proposedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        // Every operation key seen in this document, skipped or not — the ground truth
        // StaleClientOverrideNotes checks client.Overrides against once the main loop finishes.
        // Populated regardless of skip: an override naming an operation this document still
        // declares is never stale even if that operation happens to generate no case today, and
        // conflating "skipped" with "does not exist" would misreport the former as the latter.
        var allOperationKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, pathItem) in document.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var (method, operation) in (pathItem.Operations ?? []).OrderBy(o => o.Key.Method, StringComparer.Ordinal))
            {
                var key = OperationKey.Resolve(operation.OperationId, method.Method, path);
                allOperationKeys.Add(key.Value);

                // Delegated to the composer rather than reproduced here: it alone knows which
                // parameters it actually emits a value for (an optional query parameter with an
                // example or default still produces one), so it is the only place that can answer
                // this without risking drift from what a fixture write would really do.
                var needsFixture = FixtureComposer.NeedsFixture(operation);

                // This is a filename check, not a general "is this operationId OK?" validator —
                // narrow on purpose, and worth spelling out because two separate investigations
                // have already had to re-derive this reasoning from scratch. TryValidateOperationKey's
                // authority is the filesystem: it refuses a key that cannot become a fixture
                // filename (see its doc comment and reason strings in FixtureDocument.cs). Gating
                // on `needsFixture` follows directly from that authority — a key only becomes a
                // filename when a fixture is written for it, so an operation that writes none has
                // nothing here for this check to protect.
                //
                // The consequence is real and worth stating plainly rather than leaving implicit:
                // for a parameterless, body-free operation (needsFixture == false — see
                // FixtureComposer.NeedsFixture: false only when there is no JSON body to compose
                // AND no path/query parameter carrying a value, so a parameterless operation with
                // a JSON body is NOT this case) the key is never checked by this line at all —
                // including TryValidateOperationKey's char.IsControl check, so an operationId
                // with an embedded newline passes straight through here.
                //
                // That is safe only because a different, separately-scoped mechanism owns that
                // hazard: CSharpLiteral.Escape, applied to every operation key TemplateRenderer
                // emits regardless of NeedsFixture (TemplateRenderer.cs), whose authority is the
                // C# grammar rather than the filesystem — it neutralizes what would otherwise be a
                // compile-breaking or line-splitting literal. The two rules are deliberately kept
                // separate and must not be merged into one check run unconditionally: refuse here
                // when the text would have to name a file nothing can write; escape there when the
                // text is only ever going to be a C# string literal. Running the filename check
                // unconditionally would skip a perfectly testable operation over a character that
                // causes no problem once escaped — trading a working test for no test at all, to
                // guard against a failure mode (a broken compile) that no longer exists once the
                // escaping side of this pair is in place.
                //
                // This comment is the canonical explanation of the needsFixture gate and why it
                // stays narrow; TemplateRendererEscapingTests.cs and CompileVerificationTests.cs
                // each restate only what they locally need and point back here rather than
                // re-deriving the mechanism — keep it that way rather than letting a future edit
                // re-explain the gate a fourth time somewhere else.
                if (needsFixture && !FixtureDocument.TryValidateOperationKey(key.Value, out var reason))
                {
                    skipped.Add(new SkippedOperation(key.Value, reason));
                    continue;
                }

                if (operation.RequestBody?.Content is { Count: > 0 } requestContent &&
                    !requestContent.ContainsKey(JsonMediaType))
                {
                    skipped.Add(new SkippedOperation(key.Value,
                        $"request body media type(s) {string.Join(", ", requestContent.Keys.Order(StringComparer.Ordinal))} not supported in v0"));
                    continue;
                }

                var success = SelectSuccessResponse(operation);
                if (success is null)
                {
                    skipped.Add(new SkippedOperation(key.Value, "no 2xx or 3xx response declared"));
                    continue;
                }

                var (status, response) = success.Value;
                var schemaKey = ResolveSchemaKey(response, status, key.Value);

                var tag = operation.Tags?.FirstOrDefault()?.Name is { Length: > 0 } t
                    ? CSharpIdentifier.ToPascalCase(t)
                    : DefaultTag;

                var pathParameterNames = PathParameters(path);
                var httpMethod = method.Method.ToUpperInvariant();
                var methodName = CSharpIdentifier.ToPascalCase(key.Value) + "_Contract";
                proposedNames[CaseIdentity(key.Value, CaseRole.Success, status)] = methodName;

                var queryParameterNames = QueryParameters(operation);
                var hasRequestBody = FixtureComposer.HasJsonBodyToCompose(operation);

                // [typed-path-parameters]/[nswag-path-parameter-order]: computed once here, ahead
                // of both ResolveClientCall (which needs them to decide the client-invocation
                // verdict) and the TestCasePlan constructed below (which carries them) — the same
                // "compute once, carry everywhere" discipline QueryParameterNames/HasRequestBody
                // above already follow. pathParameterKinds's elements are nullable
                // (PathParameterKind?) per the corrected [typed-path-parameters] finding: not every
                // schema shape a real client generator types is one of this enum's four members.
                var pathParameterKinds = ResolvePathParameterKinds(operation, pathParameterNames);
                var declaredPathParameterOrder = DeclaredPathParameterOrder(operation);

                // [success-only]: this is the only site in Build that ever resolves a client call
                // — DeclaredError and Auth cases (TryPlanDeclaredNotFound, PlanAuthCases below)
                // never call ClientCallPlanner at all, regardless of `client`, because they exist
                // to exercise the API's own behaviour against an unmatchable id, not the client's.
                var clientCallExpression = ResolveClientCall(
                    client, key, httpMethod, path, declaredPathParameterOrder, queryParameterNames.Count > 0,
                    hasRequestBody, pathParameterKinds.Any(k => k is null), notes);

                draft.Add((tag, new TestCasePlan(
                    MethodName: methodName,
                    DisplayName: $"Given {tag}, when {key.Value}, then {status}",
                    OperationKey: key.Value,
                    OperationKeySynthesized: key.Synthesized,
                    HttpMethod: httpMethod,
                    PathTemplate: path,
                    PathParameterNames: pathParameterNames,
                    ExpectedStatus: status,
                    SchemaKey: schemaKey,
                    Category: ContractCategory,
                    Role: CaseRole.Success,
                    NeedsFixture: needsFixture,
                    QueryParameterNames: queryParameterNames,
                    HasRequestBody: hasRequestBody,
                    // [typed-path-parameters]: the Success case never used to carry a kind per
                    // path parameter — the raw-HTTP branch's PathArguments always splices a bare
                    // FixtureParameter(...) for Success regardless of declared type (decision 1:
                    // every Success path parameter is required, so it always comes from the
                    // fixture, and InTestUrl.Build takes strings either way). TemplateRenderer's
                    // client-routed branch is the new reason this is needed here: converting a
                    // fixture's string value to the declared type before splicing it into Kiota's
                    // item-builder indexer needs to know that type per parameter, and this is the
                    // single source of truth for it (ResolvePathParameterKinds), not a second
                    // re-derivation in the renderer. Reused from the local computed above rather
                    // than calling ResolvePathParameterKinds a second time here.
                    PathParameterKinds: pathParameterKinds,
                    // [nswag-path-parameter-order]: carried for the same reason ClientCallExpression
                    // itself is — a later reader of this plan (or a future consumer beyond
                    // ResolveClientCall) should not have to re-derive the spec's declared parameter
                    // order from the operation a second time.
                    DeclaredPathParameterOrder: declaredPathParameterOrder,
                    ClientCallExpression: clientCallExpression)));

                // Declared-error and auth cases only exist below this point — a call-site fact,
                // not only a comment (Task 10 item 5): both helpers run once the success case
                // above is already confirmed generated, so neither can outlive an operation this
                // method already skipped (the `continue`s above it), and the two can never
                // disagree about the operation.
                if (TryPlanDeclaredNotFound(operation, key, httpMethod, path, tag, pathParameterNames, proposedNames, notes) is { } notFoundCase)
                {
                    draft.Add((tag, notFoundCase));
                }

                foreach (var authCase in PlanAuthCases(operation, document, key, httpMethod, path, tag, pathParameterNames, proposedNames, notes))
                {
                    draft.Add((tag, authCase));
                }
            }
        }

        // Deliberately unlike fixture drift: FixtureComposer can independently reconstruct ground
        // truth from the spec and diff a fixture against it, but an override exists *because*
        // convention already failed — there is no second derivable answer for a stale key to be
        // compared against. The only check available is "does the key even exist in this document
        // any more", so a stale entry gets a note (softer than fixture drift's generate-blocking
        // refusal), not a skip and not an exit-1 gate.
        if (client is not null)
        {
            foreach (var staleKey in client.Overrides.Keys
                         .Where(k => !allOperationKeys.Contains(k))
                         .OrderBy(k => k, StringComparer.Ordinal))
            {
                notes.Add(new CoverageNote(staleKey,
                    $"{ClientCallMap.FileName} overrides this operation key, but no operation in " +
                    "the current spec has it — the entry is stale and covers nothing"));
            }
        }

        var deduped = CSharpIdentifier.Dedupe(proposedNames);

        var classes = draft
            .GroupBy(d => d.Tag, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TestClassPlan(
                ClassName: g.Key + "Tests",
                Tag: g.Key,
                Cases: g.Select(d => d.Case with { MethodName = deduped[CaseIdentity(d.Case.OperationKey, d.Case.Role, d.Case.ExpectedStatus)] })
                        .OrderBy(c => c.MethodName, StringComparer.Ordinal)
                        .ToList()))
            .ToList();

        return new TestPlan(
            document.Info?.Title ?? "Api",
            classes,
            skipped.OrderBy(s => s.OperationKey, StringComparer.Ordinal).ToList(),
            notes.OrderBy(n => n.OperationKey, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Decision 5: a declared-error 404 case, or <c>null</c> plus a <see cref="CoverageNote"/>
    /// explaining why one was withheld. Extracted from <see cref="Build"/> (Task 10 item 5) —
    /// four of the seven branches this plan's declared-error/auth logic needed were withholds
    /// distinguished only by their guard condition, and pulling them into one method with an
    /// early return per guard replaces "physical statement order plus a comment" with a value
    /// the caller cannot get without this method already having cleared every guard.
    /// </summary>
    private static TestCasePlan? TryPlanDeclaredNotFound(
        OpenApiOperation operation, OperationKey key, string httpMethod, string path, string tag,
        IReadOnlyList<string> pathParameterNames, Dictionary<string, string> proposedNames, List<CoverageNote> notes)
    {
        if (FindDeclaredResponse(operation, NotFoundStatus) is not { } notFoundResponse)
        {
            return null;
        }

        var requiredQueryParameters = RequiredQueryParameterNames(operation);

        if (pathParameterNames.Count == 0)
        {
            // Nowhere to put an unmatchable value — telling a lookup query parameter from a
            // filter is itself a guess. The operation's success case still generated and runs,
            // so this is a *note*, not a skip (§12): adding it to `skipped` would make
            // GenerateCommand report a live, passing operation as skipped, and put it in
            // coverage-report.json's `skipped` array instead of the artefact `--check` would
            // actually expect it in.
            notes.Add(new CoverageNote(key.Value, $"declares {NotFoundStatus} but has {NoPathParameterNoteReason}"));
            return null;
        }

        if (requiredQueryParameters.Count > 0)
        {
            // Decision 5's postscript: whether a missing *required* query parameter is answered
            // with 400 or 404 depends on binding and route configuration — a measurement to
            // take, not an assumption to ship. Sending only the unmatchable path id and omitting
            // a required query parameter risks asserting 404 against what a compliant,
            // correctly-routed API actually answers with 400 — the same hazard the
            // no-path-parameter branch above exists to avoid, so it gets the same treatment: a
            // note, not a guess shipped as a test.
            notes.Add(new CoverageNote(key.Value,
                $"declares {NotFoundStatus} but has required query parameter(s) " +
                $"({string.Join(", ", requiredQueryParameters)}) that an unmatchable-id-only request would omit"));
            return null;
        }

        if (operation.RequestBody?.Required == true)
        {
            // The strictly stronger case of the required-query-parameter branch above: against
            // an ASP.NET Core [ApiController] with a non-nullable [FromBody] parameter, a
            // bodyless request (decision 6: send no body) is rejected by model binding with 400
            // ("A non-empty request body is required.") before the action's NotFound() path ever
            // runs — confirmed by building plans from the shipped samples: PUT
            // /api/products/{id} and POST /api/stock/{sku}/adjustments both declare a required
            // body and both controllers bind it with a non-nullable [FromBody] parameter under
            // [ApiController]. Sending only the unmatchable path id and omitting a required body
            // would assert 404 against a guaranteed 400 on every run, so this gets the same
            // note-not-guess treatment as the branches above.
            notes.Add(new CoverageNote(key.Value,
                $"declares {NotFoundStatus} but has a required request body that an unmatchable-id-only, bodyless request would omit"));
            return null;
        }

        var methodName = CSharpIdentifier.ToPascalCase(key.Value) + "_NotFound";
        proposedNames[CaseIdentity(key.Value, CaseRole.DeclaredError, NotFoundStatus)] = methodName;

        return FixtureFreeCase(key, httpMethod, path, tag, pathParameterNames,
            methodName, NotFoundStatus, ResolveSchemaKey(notFoundResponse, NotFoundStatus, key.Value),
            CaseRole.DeclaredError,
            // Review finding on Task 4: which flavour of unmatchable value is safe depends on
            // the parameter's declared type — an integer path parameter needs a
            // well-typed-but-unmatchable integer, not a GUID string a route-constraint-free
            // binder rejects with 400 before the 404 path ever runs. TemplateRenderer is the
            // only consumer.
            ResolvePathParameterKinds(operation, pathParameterNames));
    }

    /// <summary>
    /// Decision 3: the no-token 401 case and the wrong-scope 403 case, both generated together
    /// once an operation declares `security` — or, when it omits `security` but the document
    /// declares it at the top level, neither case plus a <see cref="CoverageNote"/> naming the
    /// unresolved inheritance. Extracted from <see cref="Build"/> for the same reason
    /// <see cref="TryPlanDeclaredNotFound"/> is (Task 10 item 5).
    /// <para>
    /// Unlike the declared-error 404 case, an auth case has nowhere it *must* point an
    /// unmatchable value: sending no token, or the wrong scope, needs no target resource, so the
    /// no-path-parameter restriction that guards 404 does not apply here.
    /// </para>
    /// </summary>
    private static IReadOnlyList<TestCasePlan> PlanAuthCases(
        OpenApiOperation operation, OpenApiDocument document, OperationKey key, string httpMethod, string path, string tag,
        IReadOnlyList<string> pathParameterNames, Dictionary<string, string> proposedNames, List<CoverageNote> notes)
    {
        // Operation-level `security` only — an empty array here explicitly overrides a
        // document-level default to "no auth", and v1-c does not attempt to resolve
        // document-level inheritance.
        if (operation.Security is { Count: > 0 })
        {
            var kinds = ResolvePathParameterKinds(operation, pathParameterNames);

            var unauthorizedMethodName = CSharpIdentifier.ToPascalCase(key.Value) + "_Unauthorized";
            proposedNames[CaseIdentity(key.Value, CaseRole.Auth, UnauthorizedStatus)] = unauthorizedMethodName;

            var forbiddenMethodName = CSharpIdentifier.ToPascalCase(key.Value) + "_Forbidden";
            proposedNames[CaseIdentity(key.Value, CaseRole.Auth, ForbiddenStatus)] = forbiddenMethodName;

            return
            [
                // Status-only, deliberately: decision 3 never asks a spec's declared 401
                // response schema for anything, and an operation need not declare one at all
                // for this case to exist. No scopes either — the 401 case sends no token at all,
                // so a scope requirement is never meaningful there (FixtureFreeCase's default).
                FixtureFreeCase(key, httpMethod, path, tag, pathParameterNames,
                    unauthorizedMethodName, UnauthorizedStatus, schemaKey: null, CaseRole.Auth, kinds, IdentitySlot.None),
                FixtureFreeCase(key, httpMethod, path, tag, pathParameterNames,
                    forbiddenMethodName, ForbiddenStatus, schemaKey: null, CaseRole.Auth, kinds, IdentitySlot.Secondary,
                    RequiredScopes(operation))
            ];
        }

        // Review finding on Task 5: an operation that omits `security` entirely inherits the
        // document-level block per the OpenAPI spec — valid, and routine when a whole API shares
        // one scheme. The branch above only ever sees the operation's own declaration, so an
        // operation secured purely by inheritance got no auth cases and, unlike the three
        // note-not-guess branches guarding the 404 case, no CoverageNote either — an invisible
        // gap in coverage-report.json. Resolving the inheritance is deferred (same "measurement,
        // not an assumption" reasoning as decision 5's postscript); this branch only makes the
        // omission visible. `operation.Security is null` — not `{ Count: 0 }` — is deliberate:
        // an explicit empty array is the spec's own way of overriding the document default to
        // "no auth" for this operation, which is not a gap to report (Task 10 item 8(b) pins
        // that Microsoft.OpenApi actually materializes `"security": []` as a non-null empty
        // list, which is what makes this distinction meaningful rather than dead code).
        if (operation.Security is null && document.Security is { Count: > 0 })
        {
            notes.Add(new CoverageNote(key.Value,
                "document declares `security` but this operation does not; v1-c does not " +
                "resolve document-level inheritance, so no auth cases were generated for it"));
        }

        return [];
    }

    /// <summary>
    /// The Success case's client-invocation verdict — <see cref="ClientCallPlanner.Resolve"/>'s
    /// result, turned into the pair this call site needs: the expression itself (or
    /// <see langword="null"/>), with any withheld reason already folded into <paramref name="notes"/>
    /// as a <see cref="CoverageNote"/> rather than handed back for the caller to decide what to do
    /// with. <paramref name="client"/> being <see langword="null"/> (no `client` section declared)
    /// short-circuits before ever touching <see cref="ClientCallPlanner"/> — the common path today,
    /// and the one every existing golden fixture and test exercises, so it must add zero overhead
    /// and zero notes.
    /// <para>
    /// Takes the whole <see cref="OperationKey"/>, not just its <c>Value</c>, so
    /// <c>[nswag-needs-operationid]</c>'s presence gate can read <c>!key.Synthesized</c> — the
    /// fact <c>OperationKey.Resolve</c> already computed about whether the spec declared an
    /// <c>operationId</c> — rather than this method (or <see cref="ClientCallPlanner"/>)
    /// re-deriving it from the operation a second time.
    /// </para>
    /// <para>
    /// <paramref name="declaredPathParameterOrder"/> and <paramref name="hasUntypablePathParameter"/>
    /// are the same "caller computes once, callee never re-derives" idiom already applied to
    /// <paramref name="hasQueryParameters"/>/<paramref name="hasRequestBody"/> above, extended to
    /// two corrected findings: <c>[nswag-path-parameter-order]</c> (NSwag binds a generated
    /// method's positional arguments in the spec's declared <c>parameters</c>-array order, not
    /// path-template order — <see cref="ClientCallPlanner.BuildNSwagConvention"/>'s own doc comment
    /// has the measured evidence) and the corrected <c>[typed-path-parameters]</c> finding (some
    /// path-parameter schema shapes have no client-side conversion InTest can produce at all —
    /// <see cref="PathParameterKind"/>'s own doc comment has the measured evidence). Both are
    /// computed once in <see cref="Build"/> from the same <c>operation.Parameters</c> read
    /// <c>PathParameterKinds</c> already performs, not re-read here.
    /// </para>
    /// </summary>
    private static string? ResolveClientCall(
        ClientPlanningConfig? client, OperationKey key, string httpMethod, string path,
        IReadOnlyList<string> declaredPathParameterOrder, bool hasQueryParameters, bool hasRequestBody,
        bool hasUntypablePathParameter, List<CoverageNote> notes)
    {
        if (client is null)
        {
            return null;
        }

        var resolution = ClientCallPlanner.Resolve(
            client.Kind, key.Value, !key.Synthesized, httpMethod, path, declaredPathParameterOrder,
            hasQueryParameters, hasRequestBody, hasUntypablePathParameter, client.Overrides);

        if (resolution.Expression is null && resolution.UnresolvedReason is not null)
        {
            notes.Add(new CoverageNote(key.Value, resolution.UnresolvedReason));
        }

        return resolution.Expression;
    }

    /// <summary>
    /// The distinct union of OAuth scopes an operation's `security` declares, across every
    /// requirement in the list and every scheme within each requirement — feeds
    /// <see cref="TestCasePlan.RequiredScopes"/> on the 403 case only (see that member's comment
    /// for why the plan carries this rather than a later phase re-deriving it). Measured against
    /// the installed Microsoft.OpenApi 3.10.0: <c>OpenApiOperation.Security</c> is an
    /// <c>IList&lt;OpenApiSecurityRequirement&gt;</c>, and <c>OpenApiSecurityRequirement</c> is
    /// itself a <c>Dictionary&lt;OpenApiSecuritySchemeReference, List&lt;string&gt;&gt;</c> — the
    /// scopes are the dictionary's *values*, not its keys, and a requirement can name more than
    /// one scheme (each contributing its own scope list) just as the security array can hold more
    /// than one requirement.
    /// <para>
    /// [containment]: OpenAPI's <c>security</c> array is a logical OR across requirements — an
    /// identity satisfying any single requirement in full is authorized. The dictionary *within*
    /// one requirement is an AND across its schemes. Flattening every requirement's every scheme
    /// into one set enlarges the required set beyond what any single requirement alone demands.
    /// </para>
    /// <para>
    /// That enlargement is not a safe approximation in general, only against one failure mode: it
    /// cannot make a case skip when it should have run. It does nothing to prevent the opposite —
    /// for a multi-requirement spec, an identity that fully satisfies one alternative requirement
    /// gets measured, by the runtime guard this union feeds, against the union of *every*
    /// requirement's scopes rather than just the one it satisfies. A case that should skip as
    /// unable to produce a 403 instead runs, and fails, against a status the API is correct to
    /// return — F11 itself, one level down: the exact bug this plan exists to fix, recurring
    /// inside the plan's own scope-union logic. Every sample spec today declares exactly one
    /// security requirement, so this failure mode has not fired yet — that is a fact about
    /// today's samples, not a property of this method. It is a real latent gap, not something
    /// this flattening makes safe.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> RequiredScopes(OpenApiOperation operation) =>
        // Unreachable given this method's sole call site: PlanAuthCases only ever calls this
        // inside `if (operation.Security is { Count: > 0 })`, so the null/empty branch below
        // never actually executes today. Kept anyway — defensively, and so this method stays
        // correct on its own terms for any future caller that doesn't already guard the same
        // way.
        operation.Security is not { Count: > 0 } securityRequirements
            ? []
            : securityRequirements
                .SelectMany(requirement => requirement.Values)
                .SelectMany(scopes => scopes)
                // RFC 6749 scope tokens are case-sensitive: "orders.read" and "ORDERS.READ" are
                // two distinct scopes, so an ignore-case comparer here would silently collapse
                // them and drop a requirement the 403 guard should be comparing against. This is
                // the same explicit-Ordinal discipline ApiTestBase.RequireSecondaryIdentityLacks
                // applies to its own scope comparison, via the three-argument
                // scopes.Contains(s, StringComparer.Ordinal) overload — not the two-argument
                // scopes.Contains, which would silently defer to whatever comparer the
                // secondary identity's own Scopes collection happens to have been built with.
                .Distinct(StringComparer.Ordinal)
                // Dictionary<,> enumeration order is unspecified, and this is the only collection
                // this file builds that isn't explicitly ordered. It matters here because Task 4
                // renders this union into RequireSecondaryIdentityLacks("...", "...") calls in a
                // golden file compared byte-exact, and the existing determinism test only renders
                // twice in the same process — it cannot catch an order shift that only shows up
                // across processes or runs from unspecified Dictionary enumeration. Ordering pins
                // it the same way every other collection here already is.
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

    /// <summary>
    /// The shared constructor for every declared-error and auth case (Task 10 item 5) — the
    /// eleven-argument prefix <see cref="TryPlanDeclaredNotFound"/> and <see cref="PlanAuthCases"/>
    /// otherwise each repeat verbatim. <see cref="TestCasePlan.Category"/> is always
    /// <see cref="ContractCategory"/> and <see cref="TestCasePlan.NeedsFixture"/> is always
    /// <c>false</c> here — decision 6: every fixture-free case (declared-error and auth alike)
    /// uses an unmatchable id and sends no body, so an unfilled fixture can never block one of
    /// these cases and a broken generator can never delete or mutate real state through them.
    /// </summary>
    private static TestCasePlan FixtureFreeCase(
        OperationKey key, string httpMethod, string path, string tag, IReadOnlyList<string> pathParameterNames,
        string methodName, int expectedStatus, string? schemaKey, CaseRole role,
        IReadOnlyList<PathParameterKind?> pathParameterKinds, IdentitySlot slot = IdentitySlot.Default,
        IReadOnlyList<string>? requiredScopes = null) => new(
            MethodName: methodName,
            DisplayName: $"Given {tag}, when {key.Value}, then {expectedStatus}",
            OperationKey: key.Value,
            OperationKeySynthesized: key.Synthesized,
            HttpMethod: httpMethod,
            PathTemplate: path,
            PathParameterNames: pathParameterNames,
            ExpectedStatus: expectedStatus,
            SchemaKey: schemaKey,
            Category: ContractCategory,
            Role: role,
            NeedsFixture: false,
            PathParameterKinds: pathParameterKinds,
            Slot: slot,
            // The null-to-empty normalization that actually matters lives on
            // TestCasePlan.RequiredScopes's init property; the `?? []` here is redundant
            // belt-and-braces defense, not the load-bearing normalization.
            RequiredScopes: requiredScopes ?? []);

    /// <summary>
    /// The dedupe dictionary's key (decision 4): operation key alone collapses a success case and
    /// its declared-error sibling onto the same entry, so every case for that operation is
    /// reassigned the same final <c>MethodName</c> — CS0111 the moment an operation has more than
    /// one case. Combining role into the key keeps each case's proposed name — and, when a real
    /// collision with another operation's name exists, its hash suffix — independent of its
    /// siblings.
    /// <para>
    /// Role alone is still not unique once <see cref="CaseRole.Auth"/> exists: the no-token 401
    /// case and the wrong-scope 403 case are <em>both</em> <see cref="CaseRole.Auth"/> on the same
    /// operation, so <c>operationKey#Auth</c> would collide between them — the second
    /// <c>proposedNames</c> write would silently overwrite the first, and both cases would be
    /// reassigned the very same <c>MethodName</c> from <c>deduped</c>, the identical CS0111 failure
    /// decision 4 already describes for role-less keys. <see cref="TestCasePlan.ExpectedStatus"/>
    /// is what actually distinguishes them (and is already what every role's case count is capped
    /// by — one case per operation per status), so it joins the key as a third component.
    /// </para>
    /// </summary>
    private static string CaseIdentity(string operationKey, CaseRole role, int expectedStatus) =>
        $"{operationKey}#{role}#{expectedStatus}";

    private static IOpenApiResponse? FindDeclaredResponse(OpenApiOperation operation, int status)
    {
        if (operation.Responses is null)
        {
            return null;
        }

        foreach (var (code, response) in operation.Responses)
        {
            if (int.TryParse(code, out var parsed) && parsed == status)
            {
                return response;
            }
        }

        return null;
    }

    private static (int Status, IOpenApiResponse Response)? SelectSuccessResponse(OpenApiOperation operation)
    {
        if (operation.Responses is null)
        {
            return null;
        }

        foreach (var (code, response) in operation.Responses.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (int.TryParse(code, out var status) && status is >= 200 and < 400)
            {
                return (status, response);
            }
        }

        return null;
    }

    private static string? ResolveSchemaKey(IOpenApiResponse response, int status, string operationKey)
    {
        if (BodilessStatuses.Contains(status))
        {
            return null;
        }

        if (response.Content is null || !response.Content.TryGetValue(JsonMediaType, out var media) || media.Schema is null)
        {
            return null;
        }

        // A reference resolves to its component name; anything inline gets a synthesized key
        // so that contract tests never silently degrade to a status-code check.
        return media.Schema is OpenApiSchemaReference reference && reference.Reference?.Id is { Length: > 0 } id
            ? id
            : $"op:{operationKey}:{status}:{JsonMediaType}";
    }

    /// <summary>
    /// All declared <c>in: query</c> parameter names, required or not. This is a presence check
    /// only — it must not replicate <see cref="FixtureComposer"/>'s tiered precedence for which
    /// of them actually get a fixture entry (decision 1); the template only needs to know whether
    /// to look any query parameters up at runtime at all.
    /// </summary>
    private static IReadOnlyList<string> QueryParameters(OpenApiOperation operation)
        => (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Query)
            .Select(p => p.Name!)
            .ToList();

    /// <summary>
    /// The subset of <see cref="QueryParameters"/> the spec marks <c>required: true</c> — the
    /// ones a declared-error case cannot simply omit without risking a 400-vs-404 mismatch (see
    /// the required-query-parameter branch above).
    /// </summary>
    private static IReadOnlyList<string> RequiredQueryParameterNames(OpenApiOperation operation)
        => (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Query && p.Required)
            .Select(p => p.Name!)
            .ToList();

    /// <summary>
    /// One <see cref="PathParameterKind"/>? per entry in <paramref name="pathParameterNames"/>,
    /// same order — originally the spec data <see cref="TemplateRenderer"/> needed to render a
    /// well-typed unmatchable value for a declared-error/auth case (decision 6, and the review
    /// finding above <see cref="TryPlanDeclaredNotFound"/>'s call site); <c>[typed-path-parameters]</c>
    /// widened this method's own call sites to include the Success case too (see <see cref="Build"/>),
    /// because the client-routed branch needs the same per-parameter kind to convert a fixture's
    /// <c>string</c> value to the type Kiota's item-builder indexer actually declares.
    /// <para>
    /// <b>Corrected finding — element type is <c>PathParameterKind?</c>, not <c>PathParameterKind</c>.</b>
    /// See <see cref="ResolvePathParameterKind"/>'s own doc comment for the measured evidence and
    /// <see cref="PathParameterKind"/>'s own doc comment for the fuller correction: this method used
    /// to fall every unrecognized shape through to <see cref="PathParameterKind.String"/> (or, via
    /// the old <c>IsNumericType</c> helper, collapse <c>type: number</c> into
    /// <see cref="PathParameterKind.Integer"/> alongside genuine <c>type: integer</c>), which reads
    /// as "exhaustive" but was silently wrong for both — a real kiota 1.34.1 client types a
    /// <c>date-time</c>-formatted string parameter as <c>this[DateTimeOffset]</c>, not
    /// <c>this[string]</c>, and a <c>number</c>-typed parameter as <c>this[double]</c>, not
    /// <c>this[int]</c>. <see langword="null"/> now means exactly that: this shape has no
    /// client-side conversion InTest can produce, and <see cref="ClientCallPlanner.Resolve"/>
    /// withholds convention for the whole operation when any element here is
    /// <see langword="null"/>.
    /// </para>
    /// </summary>
    private static IReadOnlyList<PathParameterKind?> ResolvePathParameterKinds(
        OpenApiOperation operation, IReadOnlyList<string> pathParameterNames)
    {
        var declared = (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Path)
            .ToDictionary(p => p.Name!, p => p.Schema, StringComparer.Ordinal);

        return pathParameterNames
            .Select(name => declared.TryGetValue(name, out var schema) ? ResolvePathParameterKind(schema) : PathParameterKind.String)
            .ToList();
    }

    /// <summary>
    /// The per-parameter classification <see cref="ResolvePathParameterKinds"/> maps each declared
    /// path parameter schema through. Only four shapes are typable, matching
    /// <see cref="PathParameterKind"/>'s own four members exactly: <c>type: string</c> with no
    /// <c>format</c> declared at all (<see cref="PathParameterKind.String"/>); <c>type: string,
    /// format: uuid</c> (<see cref="PathParameterKind.Guid"/>); <c>type: integer, format: int64</c>
    /// (<see cref="PathParameterKind.Long"/>); and <c>type: integer</c> with any other or absent
    /// format, <c>int32</c> included (<see cref="PathParameterKind.Integer"/>). No schema declared
    /// at all for a path parameter also resolves to <see cref="PathParameterKind.String"/> — there
    /// is nothing to classify, and a fresh GUID has always been well-typed for an untyped
    /// parameter.
    /// <para>
    /// Everything else returns <see langword="null"/>, not a same-looking fallback member —
    /// <c>type: number</c> (any format, <c>double</c> included), a <c>type: string</c> with a
    /// format other than <c>uuid</c> (<c>date-time</c>, <c>date</c>, <c>byte</c>, or any
    /// unrecognized value), <c>type: boolean</c>, and any other declared type. Measured directly
    /// against real kiota 1.34.1 output: <c>type: string, format: date-time</c> generates a
    /// <c>this[DateTimeOffset]</c> item-builder indexer, and <c>type: number, format: double</c>
    /// generates <c>this[double]</c> — neither is a <see cref="PathParameterKind.String"/> or
    /// <see cref="PathParameterKind.Integer"/> in disguise, and treating either as one used to
    /// silently bind the wrong (in the first case, deprecated) overload or produce a runtime
    /// <see cref="FormatException"/> (in the second, via <c>int.Parse("1.5")</c>). This method
    /// previously used an <c>IsNumericType</c> helper that matched <c>integer</c> and
    /// <c>number</c> together for "numeric at all" before picking a sub-kind from <c>format</c> —
    /// that helper is exactly what conflated the two; it has been removed rather than kept and
    /// worked around, since nothing else in this file needs "integer or number" as a single
    /// question any more.
    /// </para>
    /// </summary>
    private static PathParameterKind? ResolvePathParameterKind(IOpenApiSchema? schema)
    {
        if (schema?.Type is not { } type)
        {
            return PathParameterKind.String;
        }

        if (type.HasFlag(JsonSchemaType.String))
        {
            if (string.IsNullOrEmpty(schema.Format))
            {
                return PathParameterKind.String;
            }

            return string.Equals(schema.Format, "uuid", StringComparison.Ordinal)
                ? PathParameterKind.Guid
                : null;
        }

        if (type.HasFlag(JsonSchemaType.Integer))
        {
            return string.Equals(schema.Format, "int64", StringComparison.Ordinal)
                ? PathParameterKind.Long
                : PathParameterKind.Integer;
        }

        // type: number (double/float/no format) and every other declared type (boolean, array,
        // object, ...) — no member of PathParameterKind fits any of them.
        return null;
    }

    /// <summary>
    /// <c>[nswag-path-parameter-order]</c>: every <c>in: path</c> parameter's name, in the order
    /// the spec's own <c>operation.Parameters</c> declares them — deliberately not re-sorted to
    /// the path template's own order the way <see cref="PathParameters"/> (path-template order) or
    /// <see cref="ResolvePathParameterKinds"/>
    /// (path-template order, keyed by name) both are. <see cref="ClientCallPlanner.BuildNSwagConvention"/>
    /// is the sole consumer: NSwag binds a generated method's positional path-parameter arguments
    /// in this declared order, not path-template order, and the two are only guaranteed to agree
    /// when an operation has at most one path parameter — every piece of evidence
    /// <c>BuildNSwagConvention</c> originally shipped on. Measured directly (nswag 14.7.1): a path
    /// <c>/customers/{customerId}/orders/{orderId}</c> whose <c>parameters</c> array declares
    /// <c>orderId</c> before <c>customerId</c> generates
    /// <c>GetCustomerOrderAsync(System.Guid orderId, System.Guid customerId, ...)</c> — the two
    /// orders disagree, and because both parameters share a type the wrong-order call still
    /// compiles, silently asserting against the wrong resource.
    /// </summary>
    private static IReadOnlyList<string> DeclaredPathParameterOrder(OpenApiOperation operation)
        => (operation.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Path)
            .Select(p => p.Name!)
            .ToList();

    private static IReadOnlyList<string> PathParameters(string path)
    {
        var names = new List<string>();
        var i = 0;

        while (i < path.Length)
        {
            var open = path.IndexOf('{', i);
            if (open < 0)
            {
                break;
            }
            var close = path.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }
            names.Add(path[(open + 1)..close]);
            i = close + 1;
        }

        return names;
    }
}
