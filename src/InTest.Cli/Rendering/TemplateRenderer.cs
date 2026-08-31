using System.Reflection;
using InTest.Cli.Naming;
using InTest.Cli.Planning;
using Scriban;

namespace InTest.Cli.Rendering;

public sealed class TemplateRenderer
{
    private readonly Template _classTemplate;

    /// <summary>
    /// [v3-only]'s own cancellation-token accessor, per framework — <c>TestContext.CancellationToken</c>
    /// for MSTest, <c>TestContext.Current.CancellationToken</c> for xUnit v3 (there is no per-test
    /// constructor-injected <c>TestContext</c> under xUnit v3; <c>TestContext.Current</c> is its
    /// ambient accessor instead), and <c>TestContext.CurrentContext.CancellationToken</c> for NUnit
    /// (verified live via <c>[CancelAfter(200)]</c> cancelling an awaited 30s delay at 249ms — see
    /// the NUnit framework pack plan's <c>[error-is-the-sink]</c> section neighbours). All three
    /// templates already spell their own raw-HTTP branch's token sites literally, per framework, in
    /// the <c>.scriban</c> source itself — this field exists only for
    /// <see cref="BuildClientCallExpression"/>, whose token argument is not template text at all but
    /// a string this class computes once and splices into <c>TestCasePlan.ClientCallExpression</c>'s
    /// placeholder-substituted result.
    /// <para>
    /// <b>Task 8's own finding, fixed in the same change:</b> before this field existed,
    /// <see cref="BuildClientCallExpression"/> appended the literal text
    /// <c>"(cancellationToken: TestContext.CancellationToken)"</c> unconditionally, regardless of
    /// which template <see cref="_classTemplate"/> had selected — correct for MSTest, but CS0120
    /// ("An object reference is required for the non-static field, method, or property
    /// 'TestContext.CancellationToken'") under xUnit, since xUnit's own <c>TestContext</c> type has
    /// no static <c>CancellationToken</c> member. Nothing before Task 8 ever rendered a
    /// client-routed case through an xUnit <see cref="TemplateRenderer"/> and then compiled the
    /// result — <c>TemplateRendererClientTests.cs</c> constructs every one of its
    /// <see cref="TemplateRenderer"/> instances with <c>"mstest"</c>, and Task 7's own xUnit golden
    /// file (<c>docs/superpowers/plans/2026-08-30-intest-xunit-framework-pack.md</c>) configures no
    /// <c>client</c> section — so the gap was invisible until
    /// <c>GeneratedSuiteExecutionTests.XunitGeneratedClientRoutedSuccessCaseReceivesAConformingBody</c>
    /// actually built a generated xUnit project with one, which is exactly the kind of gap Task 8
    /// exists to close (measured directly: that test failed with the CS0120 above before this fix).
    /// </para>
    /// </summary>
    private readonly string _cancellationTokenExpression;

    /// <summary>
    /// [framework-selects-template]: one template per framework, chosen once at construction.
    /// Separate files rather than one file branching internally — the templates are ~121 lines
    /// each and mostly identical, and a fourth framework branching every block internally would
    /// only get harder to read as more frameworks joined MSTest, xUnit, and now NUnit.
    /// <see cref="_cancellationTokenExpression"/> is selected alongside <see cref="_classTemplate"/>,
    /// from the same switch and the same input, so the two can never disagree about which
    /// framework this instance renders for.
    /// </summary>
    public TemplateRenderer(string framework)
    {
        ArgumentNullException.ThrowIfNull(framework);

        (_classTemplate, _cancellationTokenExpression) = framework switch
        {
            "mstest" => (Template.Parse(LoadEmbedded("mstest-class.scriban")), "TestContext.CancellationToken"),
            "xunit" => (Template.Parse(LoadEmbedded("xunit-class.scriban")), "TestContext.Current.CancellationToken"),
            "nunit" => (Template.Parse(LoadEmbedded("nunit-class.scriban")), "TestContext.CurrentContext.CancellationToken"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(framework), framework, "expected \"mstest\", \"xunit\", or \"nunit\"."),
        };
    }

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
    /// A large in-range integer literal for <see cref="PathParameterKind.Integer"/> and
    /// <see cref="PathParameterKind.Long"/> — well-typed (a compliant binder accepts it and
    /// reaches the action; <c>2147483647</c> fits both <c>int</c> and <c>long</c>, so one literal
    /// serves both) but unmatchable by any seeded row using ordinary small or sequential ids.
    /// Every other kind — <see cref="PathParameterKind.String"/> and <see cref="PathParameterKind.Guid"/>
    /// alike — keeps the fresh GUID this renderer always emitted, which was already a well-typed
    /// unmatchable value for a string (uuid-formatted or not). This method feeds only the
    /// raw-HTTP declared-error/auth branch (<see cref="PathArguments"/>) — <c>[typed-path-parameters]</c>'s
    /// per-kind conversion is a client-routed-branch-only concern
    /// (<see cref="BuildClientCallExpression"/>/<see cref="WrapForClientCall"/>), since
    /// <c>InTestUrl.Build</c> takes strings regardless of a path parameter's declared type.
    /// </summary>
    private static string UnmatchableValueFor(PathParameterKind? kind) => kind switch
    {
        PathParameterKind.Integer or PathParameterKind.Long => "\"2147483647\"",
        // Covers PathParameterKind.String, PathParameterKind.Guid, and null alike. Null is the
        // corrected [typed-path-parameters] finding's "untypable" verdict (see
        // TestPlanBuilder.ResolvePathParameterKind and PathParameterKind's own doc comment) — e.g.
        // a date-time- or number-typed path parameter on a declared-error/auth case. This branch
        // sends no real fixture value regardless (decision 6: an unmatchable id, always generated),
        // so "untypable" has nothing to convert and falls back to the same well-typed-but-
        // unmatchable GUID a plain string already used, exactly as it did before this field could
        // distinguish "String" from "untypable" at all.
        _ => "Guid.NewGuid().ToString()"
    };

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
    /// <para>
    /// <b>Reproduced defect, now fixed: a self-closing override must not get a second argument
    /// list appended.</b> The unconditional <c>$"{expression}(cancellationToken: …)"</c> this
    /// method used to return was correct only for a bare call chain — a Kiota-convention
    /// expression such as <c>Api.Orders[{id}].GetAsync</c>, which names a method with no argument
    /// list of its own. <c>client-map.json</c>'s own documented escape hatch is exactly the
    /// opposite shape: getting-started.md tells an adopter to "wrap it in parentheses with any
    /// additional argument the call needs", i.e. to spell the whole call, arguments included —
    /// <c>"createOrder": "Api.Orders.PostAsync(new CreateOrderRequest())"</c>, confirmed by direct
    /// reproduction to generate
    /// <c>await ApiClient&lt;…&gt;().Api.Orders.PostAsync(new CreateOrderRequest())(cancellationToken:
    /// TestContext.CancellationToken);</c> — CS0149, "method group cannot be invoked twice" —
    /// against exactly the case this map exists to cover (the query-parameter and request-body
    /// operations <see cref="Planning.ClientCallPlanner"/>'s own convention gates withhold). An
    /// override that already closes its own parentheses is spliced verbatim; only a bare call
    /// chain gets the token appended. Distinguished the only way available here — no parser, no
    /// knowledge of what <see cref="TestCasePlan.ClientCallExpression"/>'s author intended, only
    /// the text itself — by whether the expression, after path-parameter substitution, already
    /// ends with a closing <c>)</c>: every shape <see cref="Planning.ClientCallPlanner.BuildKiotaConvention"/>
    /// produces ends in a bare verb-method name (<c>GetAsync</c>, <c>PostAsync</c>, …, per its own
    /// doc comment — "no trailing <c>()</c> … stage 3 owns the call arguments"), and every
    /// documented override shape that supplies its own arguments necessarily ends by closing the
    /// parenthesis it opened. A hand-written override that violates that pattern — ending in
    /// something other than a bare identifier or a closed call, e.g. an indexer with no verb — is
    /// not a shape <c>client-map.json</c>'s own contract describes, and <c>[compiler-is-oracle]</c>
    /// catches it at the adopter's next build regardless of which arm this heuristic takes.
    /// </para>
    /// <para>
    /// <c>[typed-path-parameters]</c>: each <c>{param}</c> placeholder is substituted with
    /// <see cref="FixtureParameterCall"/>'s bare, <see cref="string"/>-returning call wrapped per
    /// <see cref="TestCasePlan.PathParameterKinds"/> (<see cref="WrapForClientCall"/>) — never the
    /// bare call alone, unlike <see cref="PathArguments"/>'s Success arm, which stays bare
    /// unconditionally because <c>InTestUrl.Build</c> takes strings regardless of declared type.
    /// This is the whole fix for the deprecated-indexer risk this plan's own risk section names:
    /// splicing a bare <see cref="string"/> into a non-string-typed item-builder indexer used to
    /// bind Kiota's <c>[Obsolete]</c>-marked <c>this[string]</c> overload every time; converting
    /// first makes the call bind the typed, non-obsolete overload instead — measured directly
    /// against a real kiota 1.34.1 client (see the plan's own measurement table). A missing or
    /// short <see cref="TestCasePlan.PathParameterKinds"/> — every call site that predates this
    /// field, including a hand-built <see cref="TestCasePlan"/> in a test — reads as "kind
    /// unknown", wrapped identically to <see cref="PathParameterKind.String"/>: no wrap at all,
    /// the same bare splice this method always produced before this field existed.
    /// </para>
    /// <para>
    /// <b>Instance method, not static — Task 8's own fix.</b> The cancellation-token argument
    /// appended below used to be the literal text <c>"TestContext.CancellationToken"</c>, correct
    /// for MSTest and a CS0120 under xUnit v3, where <c>TestContext.CancellationToken</c> is not a
    /// static member at all — see <see cref="_cancellationTokenExpression"/>'s own doc comment for
    /// the measured error and why nothing had compiled this path against an xUnit-selected
    /// <see cref="TemplateRenderer"/> before Task 8. Now spliced from that field, which the
    /// constructor sets from the same switch that picks <see cref="_classTemplate"/>, so this
    /// method can no longer disagree with whichever template it is about to feed.
    /// </para>
    /// </summary>
    private string? BuildClientCallExpression(TestCasePlan plan, string? clientTypeName)
    {
        if (clientTypeName is null || plan.ClientCallExpression is null)
        {
            return null;
        }

        var operationKeyLiteral = CSharpLiteral.Escape(plan.OperationKey);
        var expression = plan.ClientCallExpression;
        var kinds = plan.PathParameterKinds;

        for (var i = 0; i < plan.PathParameterNames.Count; i++)
        {
            var name = plan.PathParameterNames[i];
            var kind = kinds is not null && i < kinds.Count ? kinds[i] : PathParameterKind.String;
            var value = WrapForClientCall(FixtureParameterCall(operationKeyLiteral, name), kind);
            expression = expression.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        // A self-closing override (one that already spells its own argument list, e.g. a typed
        // body or a RequestConfiguration lambda — exactly what client-map.json's override map
        // exists to cover) is spliced verbatim; only a bare call chain gets the cancellation token
        // appended. See this method's own doc comment for the reproduced CS0149 this guards
        // against and why "already ends with ')'" is the correct — not merely convenient — test.
        return expression.EndsWith(')') ? expression : $"{expression}(cancellationToken: {_cancellationTokenExpression})";
    }

    /// <summary>
    /// <c>[typed-path-parameters]</c>: converts <see cref="FixtureParameterCall"/>'s bare
    /// <see cref="string"/>-returning splice to the type Kiota's per-parameter item-builder
    /// indexer actually declares for the given <paramref name="kind"/> — the whole reason a
    /// convention-derived path-parameter call now binds the typed, non-obsolete indexer overload
    /// instead of the deprecated <c>this[string]</c> one (see this plan's risk section, "measured,
    /// not assumed"). <see cref="PathParameterKind.String"/> needs no conversion at all — the
    /// fixture value already has the type the string-typed indexer expects — so it is the only
    /// kind that returns <paramref name="fixtureParameterCall"/> unwrapped; every other kind wraps
    /// it in the matching <c>.Parse(...)</c> call.
    /// </summary>
    private static string WrapForClientCall(string fixtureParameterCall, PathParameterKind? kind) => kind switch
    {
        PathParameterKind.Guid => $"Guid.Parse({fixtureParameterCall})",
        PathParameterKind.Integer => $"int.Parse({fixtureParameterCall})",
        PathParameterKind.Long => $"long.Parse({fixtureParameterCall})",
        // PathParameterKind.String falls here unconditionally — always did. So, deliberately, does
        // null: a convention-derived expression can never carry a null-kind {param} placeholder any
        // more (ClientCallPlanner.Resolve withholds the whole convention when any path parameter is
        // untypable — see that method's own doc comment), but a hand-written client-map.json
        // override is the adopter's own C# and is not gated by Resolve at all (overrides win
        // outright, unconditionally); an override that names an untypable-kind parameter gets the
        // same bare, unwrapped splice a plain string always got, which is the adopter's problem to
        // type-correct at their own next build, not this renderer's to guess a conversion for.
        _ => fixtureParameterCall
    };

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
