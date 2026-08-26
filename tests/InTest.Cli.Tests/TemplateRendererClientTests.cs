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

    private static string Render(TestClassPlan plan, string? clientTypeName = ClientTypeName)
        => new TemplateRenderer().RenderClass(plan, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase", clientTypeName);

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
    public void WrapsTheClientCallInThePinnedTryExceptionFilterCatchShape()
    {
        // [captured-response-is-the-verdict]'s pinned shape, verbatim: two '?.'s in the filter
        // (the first for "no slot, no test scope active", the second for "slot exists but nothing
        // captured yet" — InTestAmbient.LastCapturedResponse's own doc explains why a single
        // '?.' cannot work), and a second, unconditional catch that does nothing but let the
        // captured-response assertion below run instead.
        var rendered = Render(PlanWithClientCall());

        rendered.ShouldContain("catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null) { throw; }");
        rendered.ShouldContain("catch (Exception) { /* the captured response is the verdict */ }");
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
