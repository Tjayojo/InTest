using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Stage 3 ([template-and-render]) of
/// docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md: <c>TemplateRenderer</c>'s
/// own half of the typed-client-invocation feature — turning a resolved
/// <see cref="TestCasePlan.ClientCallExpression"/> into the pinned
/// <c>try</c>/exception-filter/<c>catch</c> shape <c>[captured-response-is-the-verdict]</c> names,
/// independent of <c>ClientCallPlanner</c> (already covered by <c>ClientCallPlannerTests.cs</c>) and
/// of the live, generated-and-run proof (<c>InTest.Golden.Tests.GeneratedSuiteExecutionTests</c>).
/// Kept separate from <see cref="TemplateRendererTests"/> for the same reason
/// <c>ClientCallMapTests.cs</c> and <c>ClientCallPlannerTests.cs</c> are their own files rather than
/// folded into <c>ConfigLoaderTests.cs</c> and <c>TestPlanBuilderTests.cs</c>: a distinct concern,
/// added in its own change.
/// </summary>
[TestClass]
public class TemplateRendererClientTests
{
    private const string ClientTypeName = "Orders.ApiClient.OrdersApiClient";

    /// <summary>
    /// A client-routed Success case shaped like <c>ClientCallPlanner.BuildKiotaConvention</c>'s own
    /// output for a path-parameter operation: placeholder intact (<c>{id}</c>), no leading receiver,
    /// no trailing <c>()</c> — see that method's own doc comment for why the string arrives at
    /// <c>TemplateRenderer</c> in exactly this shape.
    /// </summary>
    private static TestClassPlan PlanWithClientCall(
        string? schemaKey = "Order", string? clientCallExpression = "Api.Orders[{id}].GetAsync") => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_Contract",
            DisplayName: "Given Orders, when getOrderById, then 200",
            OperationKey: "getOrderById",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 200,
            SchemaKey: schemaKey,
            Category: "Contract",
            ClientCallExpression: clientCallExpression)]);

    /// <summary>No path parameter at all — <c>ClientCallPlanner.BuildKiotaConvention</c>'s shape
    /// for a bare collection GET, e.g. <c>Api.Orders.GetAsync</c>, with nothing for the renderer's
    /// placeholder substitution to do.</summary>
    private static TestClassPlan PlanWithClientCallNoPathParameter() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "ListOrders_Contract",
            DisplayName: "Given Orders, when listOrders, then 200",
            OperationKey: "listOrders",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders",
            PathParameterNames: [],
            ExpectedStatus: 200,
            SchemaKey: "OrderList",
            Category: "Contract",
            ClientCallExpression: "Api.Orders.GetAsync")]);

    /// <summary>
    /// A <c>client-map.json</c> override that already spells its own argument list — exactly the
    /// documented shape getting-started.md's own worked example uses ("wrap it in parentheses with
    /// any additional argument the call needs"), and the reproduced-CS0149 case
    /// <c>BuildClientCallExpression</c>'s own doc comment names: a POST whose typed body
    /// <c>ClientCallPlanner</c>'s convention gate withholds, routed through the client by an
    /// override instead.
    /// </summary>
    private static TestClassPlan PlanWithSelfClosingOverride() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "CreateOrder_Contract",
            DisplayName: "Given Orders, when createOrder, then 201",
            OperationKey: "createOrder",
            OperationKeySynthesized: false,
            HttpMethod: "POST",
            PathTemplate: "/orders",
            PathParameterNames: [],
            ExpectedStatus: 201,
            SchemaKey: "Order",
            Category: "Contract",
            HasRequestBody: true,
            ClientCallExpression: "Api.Orders.PostAsync(new CreateOrderRequest())")]);

    /// <summary>One raw-HTTP Success case alongside one client-routed Success case, in the same
    /// class — the design's own "a class freely mixes client-routed Success cases with raw-HTTP
    /// siblings" claim (the typed-client-invocation plan's Template section), proved directly
    /// rather than only asserted in a comment.</summary>
    private static TestClassPlan PlanMixingRawAndClientRoutedCases() => new(
        "OrdersTests", "Orders",
        [
            new TestCasePlan(
                MethodName: "GetOrderById_Contract",
                DisplayName: "Given Orders, when getOrderById, then 200",
                OperationKey: "getOrderById",
                OperationKeySynthesized: false,
                HttpMethod: "GET",
                PathTemplate: "/orders/{id}",
                PathParameterNames: ["id"],
                ExpectedStatus: 200,
                SchemaKey: "Order",
                Category: "Contract",
                ClientCallExpression: "Api.Orders[{id}].GetAsync"),
            new TestCasePlan(
                MethodName: "ListOrders_Contract",
                DisplayName: "Given Orders, when listOrders, then 200",
                OperationKey: "listOrders",
                OperationKeySynthesized: false,
                HttpMethod: "GET",
                PathTemplate: "/orders",
                PathParameterNames: [],
                ExpectedStatus: 200,
                SchemaKey: "OrderList",
                Category: "Contract")
        ]);

    /// <summary>
    /// <c>[typed-path-parameters]</c>: a client-routed Success case carrying a declared
    /// <see cref="PathParameterKind"/> for its one path parameter, so
    /// <c>BuildClientCallExpression</c>'s per-kind conversion (<c>WrapForClientCall</c>) has
    /// something to convert against. Otherwise identical to <see cref="PlanWithClientCall"/>.
    /// </summary>
    private static TestClassPlan PlanWithClientCallAndKind(PathParameterKind kind) => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_Contract",
            DisplayName: "Given Orders, when getOrderById, then 200",
            OperationKey: "getOrderById",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 200,
            SchemaKey: "Order",
            Category: "Contract",
            PathParameterKinds: [kind],
            ClientCallExpression: "Api.Orders[{id}].GetAsync")]);

    private static string Render(TestClassPlan plan, string? clientTypeName = ClientTypeName, string framework = "mstest")
        => new TemplateRenderer(framework).RenderClass(plan, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase", clientTypeName);

    /// <summary>
    /// An NSwag-convention expression, taken from <see cref="ClientCallPlanner.BuildNSwagConvention"/>
    /// itself rather than written out here. That matters: the planner and the renderer are the two
    /// hops that disagreed about the cancellation token, so a hardcoded expression would test the
    /// renderer's substitution while leaving the planner free to bake an accessor again — verified
    /// by reintroducing the original defect, which a hardcoded version did not catch.
    /// </summary>
    private static TestClassPlan PlanWithNSwagClientCall() =>
        PlanWithClientCall(clientCallExpression:
        ClientCallPlanner.BuildNSwagConvention("/orders/{id}", "getOrderById", ["id"]));

    /// <summary>
    /// The accessor differs per framework and only this renderer knows which one applies. An NSwag
    /// expression closes its own argument list, so the self-closing test splices it verbatim —
    /// which is how the planner's old hardcoded <c>TestContext.CancellationToken</c> reached an
    /// xUnit and an NUnit file unchanged (CS0120 in both), alongside the correct accessor on the
    /// assertion line of the same file.
    /// </summary>
    [TestMethod]
    [DataRow("mstest", "TestContext.CancellationToken", DisplayName = "mstest")]
    [DataRow("xunit", "TestContext.Current.CancellationToken", DisplayName = "xunit")]
    [DataRow("nunit", "TestContext.CurrentContext.CancellationToken", DisplayName = "nunit")]
    public void ResolvesTheNSwagConventionsCancellationTokenPlaceholderToTheSelectedFrameworksAccessor(
        string framework, string expectedAccessor)
    {
        var rendered = Render(PlanWithNSwagClientCall(), framework: framework);

        rendered.ShouldContain(
        $"GetOrderByIdAsync(FixtureParameter(\"getOrderById\", \"id\"), cancellationToken: {expectedAccessor});");
        rendered.ShouldNotContain(ClientCallPlanner.CancellationTokenPlaceholder, Case.Sensitive);
    }

    /// <summary>
    /// The general form of the defect the test above pins one instance of: a framework-specific
    /// value produced at a site that never consulted the framework. Rendering all three and
    /// asserting the output carries its own accessor and neither of the others catches any such
    /// site, including ones that do not exist yet — which is the guarantee an enum was expected to
    /// give and cannot, since a switch expression over one still needs a default arm under this
    /// repo's <c>TreatWarningsAsErrors</c> (CS8524, measured).
    /// <para>
    /// The plan deliberately carries both a client-routed case (whose accessor is spliced into the
    /// call) and a raw-HTTP case (whose accessor the template spells itself), so one pass covers
    /// both routes.
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("mstest", "TestContext.CancellationToken", DisplayName = "mstest")]
    [DataRow("xunit", "TestContext.Current.CancellationToken", DisplayName = "xunit")]
    [DataRow("nunit", "TestContext.CurrentContext.CancellationToken", DisplayName = "nunit")]
    public void NoFrameworksRenderedOutputCarriesAnotherFrameworksCancellationTokenAccessor(
        string framework, string ownAccessor)
    {
        string[] allAccessors =
        [
            "TestContext.CancellationToken",
            "TestContext.Current.CancellationToken",
            "TestContext.CurrentContext.CancellationToken",
        ];

        var rendered = Render(PlanWithNSwagClientCall(), framework: framework) +
                       Render(PlanWithClientCallNoPathParameter(), framework: framework);

        // Comment lines are excluded, and have to be: all three templates carry the same prose
        // block naming MSTest's accessor ("ShouldMatchCapturedStatusAsync both call
        // TestContext.CancellationToken"), so an unfiltered scan reports every xUnit and NUnit
        // render as carrying MSTest's. What this test is about is emitted *code*.
        var code = string.Join('\n', rendered
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // The framework's own accessor is replaced first because the three overlap as substrings:
        // "TestContext.CancellationToken" is not contained in the other two, but leaving xUnit's
        // and NUnit's in place would make the loop below compare them against themselves.
        var foreignOnly = code.Replace(ownAccessor, "<own>", StringComparison.Ordinal);

        code.ShouldContain(ownAccessor, Case.Sensitive,
        customMessage: "the scan is vacuous unless the framework's own accessor is actually emitted");

        foreach (var foreign in allAccessors.Where(a => a != ownAccessor))
        {
            foreignOnly.ShouldNotContain(foreign, Case.Sensitive,
            customMessage: $"{framework} output must not carry {foreign} — some site produced a " +
                           "framework-specific value without consulting the selected framework");
        }
    }

    [TestMethod]
    public void CallsTheClientThroughApiClientOfTheConfiguredType()
    {
        Render(PlanWithClientCall()).ShouldContain($"ApiClient<{ClientTypeName}>()");
    }

    [TestMethod]
    public void SubstitutesEveryPathParameterPlaceholderWithTheSameFixtureParameterCallThePathArgumentsBranchUses()
    {
        // "The same call" is the point, not merely "a call that looks similar" — PathArguments
        // (the raw-HTTP branch) and BuildClientCallExpression (this branch) must share one
        // implementation of path-parameter fixture resolution, per the typed-client-invocation
        // plan's explicit instruction not to reimplement it.
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldContain("Api.Orders[FixtureParameter(\"getOrderById\", \"id\")].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    // --- [typed-path-parameters]: per-kind conversion before the indexer splice. A missing
    // PathParameterKinds (every call site above this point in the file, and every one that
    // predates this field) already exercises the "unknown -> String -> bare splice" default via
    // the test just above; these four cover each PathParameterKind explicitly, proving the
    // conversion WrapForClientCall performs rather than only its absence. ---

    [TestMethod]
    public void SplicesAGuidPathParameterThroughGuidParse()
    {
        // The measured fix itself: a real kiota 1.34.1 item builder's this[Guid] overload is the
        // non-obsolete one for a uuid-formatted path parameter (this plan's measurement table) --
        // Guid.Parse(...) is what makes the call bind that overload instead of the deprecated
        // this[string] one a bare FixtureParameter(...) splice would reach.
        var rendered = Render(PlanWithClientCallAndKind(PathParameterKind.Guid));

        rendered.ShouldContain(
        "Api.Orders[Guid.Parse(FixtureParameter(\"getOrderById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    [TestMethod]
    public void SplicesAnIntegerPathParameterThroughIntParse()
    {
        var rendered = Render(PlanWithClientCallAndKind(PathParameterKind.Integer));

        rendered.ShouldContain(
        "Api.Orders[int.Parse(FixtureParameter(\"getOrderById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    [TestMethod]
    public void SplicesALongPathParameterThroughLongParse()
    {
        var rendered = Render(PlanWithClientCallAndKind(PathParameterKind.Long));

        rendered.ShouldContain(
        "Api.Orders[long.Parse(FixtureParameter(\"getOrderById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    [TestMethod]
    public void LeavesAStringPathParameterBareWithNoConversionWrapper()
    {
        // String needs no conversion at all -- the fixture value already has the type the
        // string-typed indexer expects, so the splice at the call site itself stays exactly the
        // bare shape every kind rendered before this field existed (and that the "unknown kind"
        // default above still produces). Checked as "the exact unwrapped line is present" rather
        // than "the rendered text contains no '.Parse(' anywhere" -- the template's own comment
        // above the client-routed branch names Guid.Parse/int.Parse/long.Parse in prose
        // regardless of which kind a given case actually resolves to, so a whole-text substring
        // search would false-positive on that comment rather than testing the call site.
        var rendered = Render(PlanWithClientCallAndKind(PathParameterKind.String));

        rendered.ShouldContain("Api.Orders[FixtureParameter(\"getOrderById\", \"id\")].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    /// <summary>
    /// <c>[typed-path-parameters]</c>, corrected: a <see langword="null"/> ("untypable") kind can
    /// only reach this branch through a hand-written <c>client-map.json</c> override — production
    /// planning ( <c>TestPlanBuilder</c> / <c>ClientCallPlanner.Resolve</c>) withholds the whole
    /// convention when a path parameter is untypable, so a plan built by <c>TestPlanBuilder</c>
    /// never carries a null-kind <c>{param}</c> placeholder in <c>ClientCallExpression</c>. An
    /// override's own placeholder still needs *some* defined behaviour, though, since this is a
    /// hand-built <see cref="TestCasePlan"/> bypassing that gate entirely (exactly what a
    /// <c>client-map.json</c> override does) — it falls back to the same bare, unwrapped splice
    /// <see cref="PathParameterKind.String"/> already gets, leaving type-correctness to the
    /// adopter's own next build rather than this renderer guessing a conversion.
    /// </summary>
    [TestMethod]
    public void SplicesAnUntypablePathParameterBareWithNoConversionWrapper()
    {
        var plan = new TestClassPlan("OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_Contract",
            DisplayName: "Given Orders, when getOrderById, then 200",
            OperationKey: "getOrderById",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 200,
            SchemaKey: "Order",
            Category: "Contract",
            PathParameterKinds: [null],
            ClientCallExpression: "Api.Orders[{id}].GetAsync")]);

        var rendered = Render(plan);

        rendered.ShouldContain("Api.Orders[FixtureParameter(\"getOrderById\", \"id\")].GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    [TestMethod]
    public void LeavesAPlaceholderFreeExpressionUntouchedApartFromTheTrailingArguments()
    {
        var rendered = Render(PlanWithClientCallNoPathParameter());

        rendered.ShouldContain("Api.Orders.GetAsync(cancellationToken: TestContext.CancellationToken);");
    }

    [TestMethod]
    public void PassesTheCancellationTokenByNameRatherThanPositionally()
    {
        // Kiota's verb methods take (Action<RequestConfiguration<...>>? requestConfiguration =
        // default, CancellationToken cancellationToken = default) — a positional cancellationToken
        // would bind to requestConfiguration instead and fail to compile.
        Render(PlanWithClientCall()).ShouldContain("cancellationToken: TestContext.CancellationToken");
    }

    [TestMethod]
    public void AppendsTheCancellationTokenToABareCallChainButNotToASelfClosingOverride()
    {
        // Reproduced defect: BuildClientCallExpression used to append "(cancellationToken: …)"
        // unconditionally, so a client-map.json override that already spells its own argument
        // list — getting-started.md's own documented shape for the exact operations
        // ClientCallPlanner's convention gates withhold — rendered
        // "PostAsync(new CreateOrderRequest())(cancellationToken: …)", CS0149 ("cannot invoke a
        // non-delegate type twice"). A bare Kiota-convention chain (no override) must still get
        // the token appended, or the generated call never passes one at all.
        var bareChain = Render(PlanWithClientCall());
        var selfClosingOverride = Render(PlanWithSelfClosingOverride());

        bareChain.ShouldContain("Api.Orders[FixtureParameter(\"getOrderById\", \"id\")].GetAsync(cancellationToken: TestContext.CancellationToken);");

        selfClosingOverride.ShouldContain("await ApiClient<Orders.ApiClient.OrdersApiClient>().Api.Orders.PostAsync(new CreateOrderRequest());");
        selfClosingOverride.ShouldNotContain("cancellationToken:",
        customMessage: "a self-closing override already closed its own argument list — appending " +
                       "a second one is the exact CS0149 this test guards against");
        selfClosingOverride.ShouldNotContain("CreateOrderRequest())(",
        customMessage: "the reproduced defect's own shape: a second, appended argument list " +
                       "immediately after the override's own closing ')'");
    }

    [TestMethod]
    public void WrapsTheClientCallInThePinnedTryExceptionFilterCatchShape()
    {
        // [captured-response-is-the-verdict]'s pinned shape, verbatim: two '?.'s in the first
        // filter (the first for "no slot, no test scope active", the second for "slot exists but
        // nothing captured yet" — InTestAmbient.LastCapturedResponse's own doc explains why a
        // single '?.' cannot work), and a second catch — filtered to exclude
        // OperationCanceledException (a review finding: a bare `catch (Exception)` here would
        // swallow a cancellation and report the captured response as the verdict instead of
        // letting it propagate) — whose body reports the discarded exception via
        // WarnSwallowedClientException ([warn-on-swallowed-exception]) rather than doing nothing,
        // but still lets the captured-response assertion below run instead of failing on `ex`.
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldContain("catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null) { throw; }");
        rendered.ShouldContain("catch (Exception ex) when (ex is not OperationCanceledException) { WarnSwallowedClientException(ex); }");
    }

    [TestMethod]
    public void DoesNotSwallowAnOperationCanceledExceptionAsTheCapturedVerdict()
    {
        // The specific defect the filter above exists to close: before this fix, a bare
        // `catch (Exception)` matched OperationCanceledException just like any other exception,
        // so a cancelled client call with a captured response already sitting in the ambient slot
        // (e.g. a second call inside an override expression whose first call already succeeded)
        // would report that captured response as the verdict instead of letting the cancellation
        // propagate. Asserted as "the filter names the type", which is the mechanical guarantee —
        // proving the runtime effect end-to-end needs a live cancellation, out of reach for a
        // string-rendering test.
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldContain("when (ex is not OperationCanceledException)");
    }

    [TestMethod]
    public void StartsTheStopwatchBeforeTheTryBlock()
    {
        // Non-negotiable per [captured-response-is-the-verdict]: ShouldMatchCapturedContractAsync
        // takes elapsed, and the throwing path still needs a real number — so the stopwatch cannot
        // live inside the try it is timing.
        var rendered = Render(PlanWithClientCall());

        var stopwatchIndex = rendered.IndexOf("var stopwatch = Stopwatch.StartNew();", StringComparison.Ordinal);
        var tryIndex = rendered.IndexOf("try\r\n", StringComparison.Ordinal);

        stopwatchIndex.ShouldBeGreaterThanOrEqualTo(0);
        tryIndex.ShouldBeGreaterThanOrEqualTo(0);
        stopwatchIndex.ShouldBeLessThan(tryIndex);
    }

    [TestMethod]
    public void AssertsAgainstTheCapturedContractRatherThanTheRawResponse()
    {
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldContain("ShouldMatchCapturedContractAsync(\r\n            LastCapturedResponse, 200, \"Order\", Schemas, TestId, stopwatch.Elapsed");
        rendered.ShouldNotContain("ShouldMatchContractAsync(\r\n            response,");
    }

    [TestMethod]
    public void EmitsNoRawHttpRequestBuildingForAClientRoutedCase()
    {
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldNotContain("new HttpRequestMessage(");
        rendered.ShouldNotContain("Client.SendAsync(");
    }

    [TestMethod]
    public void ARawCaseIsUnaffectedByTheClientBranchExisting()
    {
        // No ClientCallExpression on the plan (the default, and every case predating stage 3) must
        // render exactly as it always has — the golden regression this file's own golden test
        // (GoldenFileTests.OutputMatchesTheGoldenFile) checks byte-for-byte against a real spec;
        // this is the same claim, narrower and faster.
        var plan = new TestClassPlan("OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "ListOrders_Contract",
            DisplayName: "Given Orders, when listOrders, then 200",
            OperationKey: "listOrders",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders",
            PathParameterNames: [],
            ExpectedStatus: 200,
            SchemaKey: "OrderList",
            Category: "Contract")]);

        var rendered = Render(plan);

        rendered.ShouldContain("new HttpRequestMessage(");
        rendered.ShouldNotContain("ApiClient<");
        rendered.ShouldNotContain("LastCapturedResponse");
    }

    [TestMethod]
    public void RoutesThroughTheClientAndAssertsStatusOnlyWhenTheCaseHasNoSchemaKey()
    {
        // [stage-3b]: a bodiless Success response (204/205/304, or any response declaring no
        // schema — reachable via any client-map.json override too) used to fall back to raw HTTP
        // here, because ApiResponseAssertions had no captured-response counterpart of
        // ShouldMatchStatusAsync to call instead of the schema-validating
        // ShouldMatchCapturedContractAsync. Now that ShouldMatchCapturedStatusAsync exists, a
        // client-routed case is routed through the client unconditionally — this schema-less case
        // must still call the client and assert only status, never fall back to raw HTTP.
        var rendered = Render(PlanWithClientCall(schemaKey: null));

        rendered.ShouldContain($"ApiClient<{ClientTypeName}>()");
        rendered.ShouldContain("LastCapturedResponse");
        rendered.ShouldContain("ShouldMatchCapturedStatusAsync(\r\n            LastCapturedResponse, 200, TestId, stopwatch.Elapsed, TestContext.CancellationToken);");
        rendered.ShouldNotContain("new HttpRequestMessage(");
        rendered.ShouldNotContain("ShouldMatchCapturedContractAsync(");
    }

    [TestMethod]
    public void FallsBackToRawHttpWhenRenderClassReceivesNoClientTypeName()
    {
        // Defensive, not a reachable production gap (TestPlanBuilder only ever sets
        // ClientCallExpression when GenerateCommand also supplies a clientTypeName) — but a test
        // that constructs a TestCasePlan directly, bypassing TestPlanBuilder, must not be able to
        // render a bare ApiClient<null>() by omitting clientTypeName from RenderClass.
        var rendered = Render(PlanWithClientCall(), clientTypeName: null);

        rendered.ShouldNotContain("ApiClient<");
        rendered.ShouldContain("new HttpRequestMessage(");
    }

    [TestMethod]
    public void AClassCanMixARawCaseAndAClientRoutedCaseTogether()
    {
        var rendered = Render(PlanMixingRawAndClientRoutedCases());

        rendered.ShouldContain($"ApiClient<{ClientTypeName}>()", customMessage: "the client-routed case must still render its branch");
        rendered.ShouldContain("new HttpRequestMessage(", customMessage: "the raw-HTTP sibling must still render its own branch");
    }

    [TestMethod]
    public void IsDeterministic()
    {
        Render(PlanWithClientCall()).ShouldBe(Render(PlanWithClientCall()));
    }

    [TestMethod]
    public void EmitsNoStrayBlankLinesForAClientRoutedCase()
    {
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldNotContain("\r\n\r\n\r\n");
        rendered.ShouldNotContain("\r\n\r\n    }");
    }
}
