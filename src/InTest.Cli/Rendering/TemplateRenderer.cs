using System.Reflection;
using InTest.Cli.Naming;
using InTest.Cli.Planning;
using Scriban;

namespace InTest.Cli.Rendering;

public sealed class TemplateRenderer
{
    private readonly Template _classTemplate = Template.Parse(LoadEmbedded("mstest-class.scriban"));

    /// <param name="clientTypeName">
    /// The adopter's typed-client dotted type name — <c>LoadedConfig.Client?.TypeName</c> — or
    /// <see langword="null"/> when the project declares no <c>client</c> section, the default and
    /// every call site that predates stage 3 (GoldenFileTests' own <c>RenderAsync</c> included).
    /// Optional so every existing caller keeps compiling unchanged; see
    /// <see cref="BuildClientCallExpression"/> for why a null here forces every case's
    /// <c>client_call_expression</c> to null regardless of what <see cref="TestCasePlan.ClientCallExpression"/>
    /// carries.
    /// </param>
    public string RenderClass(TestClassPlan plan, string @namespace, string baseClass, string? clientTypeName = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var model = new
        {
            @namespace,
            class_name = plan.ClassName,
            base_class = baseClass,
            cases = plan.Cases.Select(c => new
            {
                method_name = c.MethodName,
                // '_literal' names every field below whose value is only valid pasted directly
                // between a pair of '"' in the emitted source — CSharpLiteral.Escape's own doc
                // comment says the same thing, but a model field is the use site a future
                // template edit will actually look at. Two different misuses, two different
                // guards: TemplateEscapingGuardTests enforces the direction the original defect
                // actually took — every field mstest-class.scriban quotes must be named
                // '_literal' (or be on that test's small explicit allow-list, e.g. 'category'
                // below). Nothing enforces the opposite direction — an already-escaped
                // '_literal' value read back out of literal position and recomposed into
                // something else — beyond the name itself.
                display_name_literal = CSharpLiteral.Escape(c.DisplayName),
                // NOT escaped, and deliberately not named '_literal': always the constant
                // TestPlanBuilder.ContractCategory = "Contract" (TestPlanBuilder.cs:12), never
                // spec-derived, so there is nothing here for CSharpLiteral.Escape to do. Allow-
                // listed by name in TemplateEscapingGuardTests rather than escaped, so a reader
                // of that test (or of this comment) sees why it's the one exception rather than
                // having to infer it from an absent suffix.
                category = c.Category,
                operation_key_literal = CSharpLiteral.Escape(c.OperationKey),
                http_method_pascal = ToPascalMethod(c.HttpMethod),
                path_template_literal = CSharpLiteral.Escape(c.PathTemplate),
                path_argument_list = PathArguments(c),
                query_expression = QueryExpression(c),
                has_body = c.HasRequestBody,
                expected_status = c.ExpectedStatus,
                schema_key_literal = c.SchemaKey is null ? null : CSharpLiteral.Escape(c.SchemaKey),
                // [DoNotParallelize]'s source. Method alone was Task 4's implementation and, on
                // review, its own bug: a declared-error or auth case always sends a generated,
                // unmatchable id and no body (decision 6) — it mutates nothing real, regardless
                // of HTTP method, so serializing it against other tests bought nothing but
                // slower runs. Only a Success case's mutating method is real mutation, since only
                // Success sends fixture-backed, real data. Tested for Success rather than
                // "!= DeclaredError && != Auth" for the same fail-safe reason as
                // emits_fixture_lookup and PathArguments below: a role this code has not been
                // told about yet must fail toward "does not need serializing", the same direction
                // decision 6 already requires of it.
                mutates = c.Role == CaseRole.Success && c.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE",
                // Decision 3: only the wrong-scope 403 case needs a second identity to exist at
                // all, so only it carries the runtime guard call. Slot.Secondary is, today, the
                // only slot that ever needs it — computed from the slot itself rather than the
                // role so the condition stays meaningful if a future role ever reused Secondary.
                identity_needs_guard = c.Slot == IdentitySlot.Secondary,
                // Task 4: the second guard for the wrong-scope 403 case — a second identity
                // existing (identity_needs_guard, above) is not enough to make a 403 provable;
                // the secondary identity must also lack at least one scope the operation
                // requires. Null (not empty string) when RequiredScopes is empty so the template
                // can render nothing at all for a scope-free secured operation (e.g. bearerAuth
                // with no scopes) rather than a bare RequireSecondaryIdentityLacks() call, which
                // would read as an assertion that the identity holds zero scopes rather than as
                // "there is nothing to check here". TestCasePlan.RequiredScopes is empty-never-
                // null and already ordered with StringComparer.Ordinal (Task 3), so this only
                // joins and quotes — it does not resort.
                //
                // Escaped despite RFC 6749 §3.3 defining scope-token as
                // 1*( %x21 / %x23-5B / %x5D-7E ) — a grammar that already excludes space, '"',
                // '\' and every control character, which would make a hostile scope name
                // unreachable if every string here had to be a compliant token. It does not:
                // that grammar bounds what a real OAuth2 exchange treats as a valid scope, not
                // what an OpenAPI document is allowed to declare. The OpenAPI Specification's
                // OAuthFlow.scopes is Map[string, string] with no format constraint on the key,
                // and RequiredScopes (TestPlanBuilder.RequiredScopes) reads those keys straight
                // out of the document's `security` requirement — an invalid-but-parseable spec
                // can put any text there, so this runs unconditionally rather than trusting the
                // RFC to have already ruled the hostile case out.
                required_scopes_args = c.RequiredScopes.Count == 0
                    ? null
                    : string.Join(", ", c.RequiredScopes.Select(s => $"\"{CSharpLiteral.Escape(s)}\"")),
                // Decision 7: Default (every case that predates Task 5, and every non-auth case)
                // renders as null, which the template reads as "emit no override line at all" —
                // the reason every existing Success case stays byte-identical in the golden file.
                identity_override = c.Slot switch
                {
                    IdentitySlot.None => "IdentitySlot.None",
                    IdentitySlot.Secondary => "IdentitySlot.Secondary",
                    _ => null
                },
                // Stage 3 ([template-and-render], typed-client invocation): both fields sit bare,
                // never inside a quoted string — TemplateEscapingGuardTests.AllowedInBarePosition
                // carries both by name below rather than a '_literal' suffix. Carried per case
                // (not once at the model's root) purely so that guard's tc.<name> scan — which
                // only inspects case-scoped references — accounts for them; client_type_name's
                // value is identical across every case in the class.
                //
                // client_type_name reaches mstest-class.scriban in reference position
                // (ApiClient<{{ tc.client_type_name }}>()), the same reference-position rule
                // base_class/@namespace above already follow: ConfigLoader.ReadOptionalClientConfig
                // validates client.typeName with CSharpIdentifier.TryValidateDottedName before this
                // ever runs, not CSharpLiteral.Escape, so there is nothing here for that escaper to
                // do.
                client_type_name = clientTypeName,
                // See BuildClientCallExpression's own doc comment for the two reasons a non-null
                // TestCasePlan.ClientCallExpression can still render as null here. Null is what
                // turns the template's `{{ if tc.client_call_expression }}` branch off — the same
                // idiom identity_override above already uses for its own if-condition — so a case
                // this renders null for takes today's raw-HTTP branch exactly as it always has.
                client_call_expression = BuildClientCallExpression(c, clientTypeName),
                // Decision 6: a declared-error case shares its operation key with the success
                // case beside it, so calling RequireFixture here would let that sibling's unfilled
                // or unresolved fixture block a case that needs no data at all — the exact failure
                // mode decision 6 exists to prevent.
                //
                // Deliberately phrased as "== Success" rather than "!= DeclaredError": Task 5 adds
                // CaseRole.Auth (see that enum's own doc comment), and decision 6 applies to auth
                // cases too — a wrong-scope 403 pointed at a real id via FixtureParameter succeeds
                // when auth is broken, deleting real data at exactly the moment something is
                // already wrong. Testing positively for the one safe role means any role this code
                // has not been told about yet — Auth included — takes the fixture-free arm by
                // default, rather than the destructive one. Not TestCasePlan.NeedsFixture, which
                // answers a different question: whether the operation gets a fixture *file* at all
                // (FixtureComposer's verdict). That is already false for parameterless success
                // cases like listOrders, which must still emit RequireFixture — using it here would
                // silently change success-case output.
                emits_fixture_lookup = c.Role == CaseRole.Success
            }).ToList()
        };

        var rendered = _classTemplate.Render(model, member => member.Name);
        return Normalize(rendered);
    }

    /// <summary>
    /// Normalizes line endings so golden files compare identically on every OS. [crlf-everywhere]:
    /// collapse to LF first so any already-CRLF input (e.g. a template file checked out CRLF) does
    /// not double up, then re-expand to CRLF — the direction this project standardizes on for
    /// every generated artifact. See TemplateRenderer's own callers and CommittedJsonOptions for
    /// the JSON half of the same decision.
    /// </summary>
    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()
                .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";

    private static string ToPascalMethod(string httpMethod)
        => httpMethod.Length == 0 ? "Get" : char.ToUpperInvariant(httpMethod[0]) + httpMethod[1..].ToLowerInvariant();

    /// <summary>
    /// Every path parameter on a success case is unconditionally required (decision 1), so its
    /// value always comes from the fixture via <c>FixtureParameter</c> — never a sentinel
    /// constant, never TestData. Every other role is the deliberate exception (decision 6): it
    /// sends a fresh, generated id no seeded row can match, precisely so an unfilled fixture can
    /// never block it and a broken generator can never point a mutating non-success case at real
    /// data.
    ///
    /// The condition tests for Success, not "!= DeclaredError", for the same fail-safe reason as
    /// <c>emits_fixture_lookup</c> above: Task 5's Auth role must default to this same
    /// fixture-free arm the instant it exists, without anyone having to remember to add it here.
    ///
    /// Review finding on Task 4: "unmatchable" is not the same as "any old value" — a
    /// <c>type: integer</c> path parameter needs a well-typed-but-unmatchable value, or an
    /// ASP.NET Core binder without a route constraint answers 400 from model binding before the
    /// action's declared-error path ever runs, and the generated test asserts 404 against a
    /// guaranteed 400 on every run. <see cref="UnmatchableValueFor"/> picks per
    /// <see cref="TestCasePlan.PathParameterKinds"/>; a missing or short kinds list (every call
    /// site that predates this field) reads as "unknown", which renders the same GUID this
    /// method always rendered.
    /// </summary>
    private static string PathArguments(TestCasePlan plan)
    {
        if (plan.PathParameterNames.Count == 0)
        {
            return string.Empty;
        }

        if (plan.Role == CaseRole.Success)
        {
            var operationKeyLiteral = CSharpLiteral.Escape(plan.OperationKey);
            return ", " + string.Join(", ",
                plan.PathParameterNames.Select(n => FixtureParameterCall(operationKeyLiteral, n)));
        }

        var kinds = plan.PathParameterKinds;
        var values = plan.PathParameterNames.Select((_, i) =>
            UnmatchableValueFor(kinds is not null && i < kinds.Count ? kinds[i] : PathParameterKind.String));

        return ", " + string.Join(", ", values);
    }

    /// <summary>
    /// A large in-range integer literal for <see cref="PathParameterKind.Integer"/> — well-typed
    /// (a compliant binder accepts it and reaches the action) but unmatchable by any seeded row
    /// using ordinary small or sequential ids. Every other kind keeps the fresh GUID this
    /// renderer always emitted, which was already a well-typed unmatchable value for a string
    /// (uuid-formatted or not).
    /// </summary>
    private static string UnmatchableValueFor(PathParameterKind kind)
        => kind == PathParameterKind.Integer ? "\"2147483647\"" : "Guid.NewGuid().ToString()";

    /// <summary>
    /// The one place a <c>FixtureParameter("opKey", "param")</c> call is spelled out —
    /// <see cref="PathArguments"/> (raw-HTTP path arguments) and
    /// <see cref="BuildClientCallExpression"/> (stage 3's typed-client indexer substitution) both
    /// call this rather than each formatting the string itself, per the typed-client-invocation
    /// plan's explicit instruction to reuse <see cref="PathArguments"/>'s existing
    /// path-parameter-fixture-resolution logic rather than reimplementing it. <paramref
    /// name="operationKeyLiteral"/> is taken pre-escaped (both callers already hold one, built
    /// once per case) rather than re-escaping the same operation key twice per parameter.
    /// </summary>
    private static string FixtureParameterCall(string operationKeyLiteral, string paramName)
        => $"FixtureParameter(\"{operationKeyLiteral}\", \"{CSharpLiteral.Escape(paramName)}\")";

    /// <summary>
    /// Turns <see cref="TestCasePlan.ClientCallExpression"/>'s placeholder-intact call chain
    /// (<c>Api.Orders[{id}].GetAsync</c> — <see cref="Planning.ClientCallPlanner.BuildKiotaConvention"/>'s
    /// own doc comment names the shape) into the executable expression the template splices bare
    /// after <c>ApiClient&lt;T&gt;()</c>: every <c>{param}</c> placeholder becomes the same
    /// <see cref="FixtureParameterCall"/> text <see cref="PathArguments"/> already produces for the
    /// raw-HTTP branch (one implementation of path-parameter fixture resolution, not two), and the
    /// trailing call arguments are appended last. Kiota's verb methods take
    /// <c>(Action&lt;RequestConfiguration&lt;...&gt;&gt;? requestConfiguration = default,
    /// CancellationToken cancellationToken = default)</c> — passing only <c>cancellationToken</c>,
    /// and by name, lets <c>requestConfiguration</c> fall back to its own default rather than this
    /// renderer having to name or construct one.
    /// <para>
    /// Returns <see langword="null"/> — which turns the template's whole
    /// <c>{{ if tc.client_call_expression }}</c> branch off, falling back to today's raw-HTTP
    /// shape — only when <paramref name="clientTypeName"/> is null: no <c>client</c> config
    /// reached <see cref="RenderClass"/> at all. In production <c>TestPlanBuilder</c> only ever
    /// sets <see cref="TestCasePlan.ClientCallExpression"/> when <c>GenerateCommand</c> supplied a
    /// <c>ClientPlanningConfig</c>, and <c>GenerateCommand</c> only ever does that from the same
    /// <c>LoadedClientConfig</c> that supplies <paramref name="clientTypeName"/> — so the two are
    /// always both-null or both-set together there. This guard is for the shape, not a reachable
    /// production gap: a test that constructs a <see cref="TestCasePlan"/> directly, bypassing
    /// <c>TestPlanBuilder</c>, must not be able to render a bare <c>ApiClient&lt;null&gt;()</c> by
    /// skipping it.
    /// </para>
    /// <para>
    /// [stage-3b]: <see cref="TestCasePlan.SchemaKey"/> being null — a bodiless Success response
    /// (204/205/304, or any response declaring no schema at all; reachable today via
    /// <c>samples/Orders.Api</c>'s cancel-order <c>DELETE</c>, and via any <c>client-map.json</c>
    /// override, which bypasses every one of <c>ClientCallPlanner.Resolve</c>'s own gates by
    /// design) — used to also fall back to raw HTTP here, because
    /// <c>ApiResponseAssertions</c> had no captured-response counterpart of
    /// <c>ShouldMatchStatusAsync</c> to call instead of the schema-validating
    /// <c>ShouldMatchCapturedContractAsync</c>. That was safe but wrong in intent: an adopter who
    /// opted a case into client routing got silent raw HTTP instead, with no signal. Now that
    /// <c>ShouldMatchCapturedStatusAsync</c> exists, a client-routed case is routed through the
    /// client unconditionally — <see cref="RenderClass"/>'s <c>schema_key_literal</c> is what
    /// decides, per case, which of the two captured-response assertions the template emits,
    /// mirroring the raw-HTTP branch's own long-standing choice between
    /// <c>ShouldMatchContractAsync</c> and <c>ShouldMatchStatusAsync</c>.
    /// </para>
    /// </summary>
    private static string? BuildClientCallExpression(TestCasePlan plan, string? clientTypeName)
    {
        if (clientTypeName is null || plan.ClientCallExpression is null)
        {
            return null;
        }

        var operationKeyLiteral = CSharpLiteral.Escape(plan.OperationKey);
        var expression = plan.ClientCallExpression;

        foreach (var name in plan.PathParameterNames)
        {
            expression = expression.Replace(
                $"{{{name}}}", FixtureParameterCall(operationKeyLiteral, name), StringComparison.Ordinal);
        }

        return $"{expression}(cancellationToken: TestContext.CancellationToken)";
    }

    /// <summary>
    /// Appended to the built path so the query string comes entirely from whichever declared
    /// query parameters the fixture actually supplies (decision 1) — never baked into the
    /// template, since an optional parameter with no example or default is never sent at all and
    /// the template has no way to know at generation time which ones a hand-filled fixture will
    /// end up carrying.
    ///
    /// Gated on Role == Success for the same reason as <see cref="PathArguments"/> and
    /// <c>emits_fixture_lookup</c> above (decision 6): of the three fixture paths — path
    /// arguments, body, query string — this was the one a mutation review found ungated. Adding
    /// QueryParameterNames to the declared-error and both auth branches in TestPlanBuilder and
    /// running the whole suite, Golden included, produced generated 404 and auth tests calling
    /// FixtureQueryParameters(...) with every test still green: a fixture-free case could be
    /// blocked by a sibling's unfilled fixture, and — for a mutating operation — a broken auth
    /// case could carry real, possibly filtering, data into a request decision 6 requires to touch
    /// nothing real. Tested positively for Success, not "!= DeclaredError", so any role this code
    /// has not been told about yet fails toward the fixture-free arm by default.
    /// </summary>
    private static string QueryExpression(TestCasePlan plan)
    {
        if (plan.Role != CaseRole.Success)
        {
            return string.Empty;
        }

        var names = plan.QueryParameterNames ?? [];
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var nameArgs = string.Join(", ", names.Select(n => $"\"{CSharpLiteral.Escape(n)}\""));
        return $" + InTestUrl.BuildQuery(FixtureQueryParameters(\"{CSharpLiteral.Escape(plan.OperationKey)}\", {nameArgs}))";
    }

    private static string LoadEmbedded(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded template '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
