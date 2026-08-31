using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Escaping of spec-derived text into the C# string literals TemplateRenderer emits. Split out
/// from TemplateRendererTests (which covers everything else about rendering) so this concern —
/// and its own doc comment explaining what it does and doesn't prove — stays self-contained
/// rather than wedged between that file's plan factories and its shared <see cref="Render"/>
/// helper.
/// <para>
/// Why a hostile operationId ever reaches TemplateRenderer unvalidated — the needsFixture gate,
/// and why it stays that narrow — is explained once, canonically, at TestPlanBuilder.cs's
/// <c>if (needsFixture &amp;&amp; !FixtureDocument.TryValidateOperationKey(...))</c> check; this
/// file does not re-derive that reasoning. What is locally relevant here: the plans below all
/// default NeedsFixture to true (TestCasePlan's own record default), which TemplateRenderer
/// never actually consults — rendering only ever looks at Role — so hostile text is exercised
/// directly against <see cref="Render"/> without needing to fake the real gate at all. That a
/// hostile operation actually reaches the renderer *through* the real gate is proven separately
/// by <c>CompileVerificationTests.GeneratedProjectWithHostileSpecTextCompiles</c>, which compiles
/// a real generated project from a real spec and asserts on the generated file's content — a
/// string assertion here can only prove escaping happened, not that the result is valid C#.
/// </para>
/// </summary>
[TestClass]
public class TemplateRendererEscapingTests
{
    private static TestClassPlan OrdinaryPlan() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, "GET", "/orders/{id}", ["id"], 200, "Order", "Contract")]);

    private static TestClassPlan PlanAuthWithRequiredScopes(IReadOnlyList<string> requiredScopes) => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "DeleteOrder_Forbidden",
            DisplayName: "Given Orders, when deleteOrder, then 403",
            OperationKey: "deleteOrder",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 403,
            SchemaKey: null,
            Category: "Contract",
            Role: CaseRole.Auth,
            NeedsFixture: false,
            Slot: IdentitySlot.Secondary,
            RequiredScopes: requiredScopes)]);

    private static TestClassPlan PlanWithHostileDisplayName() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when get\"Order\\Id, then 200",
            "getOrderById", false, "GET", "/orders/{id}", ["id"], 200, "Order", "Contract")]);

    private static TestClassPlan PlanWithHostileOperationKey() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when x, then 200",
            "get\"Order\\Id", false, "GET", "/orders/{id}", ["id"], 200, "Order", "Contract")]);

    private static TestClassPlan PlanWithHostileOperationKeyAndBody() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("CreateOrder_Contract", "Given Orders, when x, then 201",
            "create\"Order\\Thing", false, "POST", "/orders", [], 201, "Order", "Contract",
            HasRequestBody: true)]);

    private static TestClassPlan PlanWithHostileOperationKeyAndQueryParameters() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan(
            MethodName: "GetOrderById_Contract",
            DisplayName: "Given Orders, when x, then 200",
            OperationKey: "get\"Order\\Id",
            OperationKeySynthesized: false,
            HttpMethod: "GET",
            PathTemplate: "/orders/{id}",
            PathParameterNames: ["id"],
            ExpectedStatus: 200,
            SchemaKey: "Order",
            Category: "Contract",
            QueryParameterNames: ["page"])]);

    private static TestClassPlan PlanWithHostilePathTemplate() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, "GET", "/orders/{id}\"weird\\path", ["id"], 200, "Order", "Contract")]);

    private static TestClassPlan PlanWithHostileSchemaKey() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, "GET", "/orders/{id}", ["id"], 200,
            "op:get\"Order\\Id:200:application/json", "Contract")]);

    private static TestClassPlan PlanWithHostilePathParameterName() => new(
        "OrdersTests", "Orders",
        [new TestCasePlan("GetOrderById_Contract", "Given Orders, when getOrderById, then 200",
            "getOrderById", false, "GET", "/orders/{id}", ["weird\"id\\name"], 200, "Order", "Contract")]);

    private static TestClassPlan PlanWithHostileQueryParameterName() => new(
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
            QueryParameterNames: ["sort\"by\\field"])]);

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheDisplayName()
    {
        var rendered = Render(PlanWithHostileDisplayName());
        rendered.ShouldContain("[Description(\"Given Orders, when get\\\"Order\\\\Id, then 200\")]");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheOperationKeyForRequireFixture()
    {
        var rendered = Render(PlanWithHostileOperationKey());
        rendered.ShouldContain("RequireFixture(\"get\\\"Order\\\\Id\");");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheOperationKeyForFixtureBody()
    {
        var rendered = Render(PlanWithHostileOperationKeyAndBody());
        rendered.ShouldContain("FixtureBody(\"create\\\"Order\\\\Thing\")");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheOperationKeyForFixtureParameter()
    {
        var rendered = Render(PlanWithHostileOperationKey());
        rendered.ShouldContain("FixtureParameter(\"get\\\"Order\\\\Id\", \"id\")");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheOperationKeyForFixtureQueryParameters()
    {
        var rendered = Render(PlanWithHostileOperationKeyAndQueryParameters());
        rendered.ShouldContain("FixtureQueryParameters(\"get\\\"Order\\\\Id\", \"page\")");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInThePathTemplate()
    {
        var rendered = Render(PlanWithHostilePathTemplate());
        rendered.ShouldContain("InTestUrl.Build(\"/orders/{id}\\\"weird\\\\path\"");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheSchemaKey()
    {
        var rendered = Render(PlanWithHostileSchemaKey());
        rendered.ShouldContain("\"op:get\\\"Order\\\\Id:200:application/json\"");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInThePathParameterName()
    {
        var rendered = Render(PlanWithHostilePathParameterName());
        rendered.ShouldContain("FixtureParameter(\"getOrderById\", \"weird\\\"id\\\\name\")");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInTheQueryParameterName()
    {
        var rendered = Render(PlanWithHostileQueryParameterName());
        rendered.ShouldContain("FixtureQueryParameters(\"getOrderById\", \"sort\\\"by\\\\field\")");
    }

    [TestMethod]
    public void EscapesQuotesAndBackslashesInARequiredScope()
    {
        var rendered = Render(PlanAuthWithRequiredScopes(["orders.write\"x\\y"]));
        rendered.ShouldContain("RequireSecondaryIdentityLacks(\"orders.write\\\"x\\\\y\");");
    }

    [TestMethod]
    public void OrdinaryTextIsRenderedUnescaped()
    {
        // A fine, cheap, local sanity check that escaping is a no-op for ordinary text — not
        // the authoritative proof of that property. GoldenFileTests.OutputMatchesTheGoldenFile
        // is the byte-exact, whole-file version of this same guarantee, and is what actually
        // catches an escaping helper that silently alters ordinary output: this test only
        // spot-checks four substrings, so it could stay green while other ordinary text moved.
        var rendered = Render(OrdinaryPlan());

        rendered.ShouldContain("[Description(\"Given Orders, when getOrderById, then 200\")]");
        rendered.ShouldContain("RequireFixture(\"getOrderById\");");
        rendered.ShouldContain("FixtureParameter(\"getOrderById\", \"id\")");
        rendered.ShouldContain("InTestUrl.Build(\"/orders/{id}\"");
    }

    private static string Render(TestClassPlan plan)
        => new TemplateRenderer("mstest").RenderClass(plan, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");
}
