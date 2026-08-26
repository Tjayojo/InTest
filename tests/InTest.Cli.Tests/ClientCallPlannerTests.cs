using InTest.Cli.Planning;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Asserts <see cref="ClientCallPlanner.BuildKiotaConvention"/> and
/// <see cref="ClientCallPlanner.BuildNSwagConvention"/> against captured real generator output: a
/// real `kiota` 1.34.1 client and a real `nswag` 14.7.1 client, both built from
/// <c>samples/Orders.Api/Orders.Api.json</c>-shaped specs — see <see cref="ClientCallPlanner"/>'s
/// own doc comment for the file-by-file evidence. Every expected string below is the exact call
/// shape read directly from generated source, not guessed from either generator's documentation.
/// </summary>
[TestClass]
public class ClientCallPlannerTests
{
    private static readonly Dictionary<string, string> NoOverrides = new(StringComparer.Ordinal);

    // ---- BuildKiotaConvention: the four Orders.Api operations that qualify for a convention
    // guess at all (CaseRole.Success, no query parameters, no request body) — GET /api/orders and
    // POST /api/customers and POST /api/orders are excluded by construction (query params / body)
    // and covered instead by TestPlanBuilderTests, which owns that gating. ---------------------

    [TestMethod]
    public void DerivesTheExpressionForGetOneOrder()
    {
        // OrdersItemRequestBuilder.GetAsync() — client.Api.Orders[id].GetAsync().
        ClientCallPlanner.BuildKiotaConvention("GET", "/api/orders/{id}").ShouldBe("Api.Orders[{id}].GetAsync");
    }

    [TestMethod]
    public void DerivesTheExpressionForCancelOrder()
    {
        // OrdersItemRequestBuilder.DeleteAsync().
        ClientCallPlanner.BuildKiotaConvention("DELETE", "/api/orders/{id}").ShouldBe("Api.Orders[{id}].DeleteAsync");
    }

    [TestMethod]
    public void DerivesTheExpressionForListCustomers()
    {
        // CustomersRequestBuilder.GetAsync() — no indexer, since the operation carries no path
        // parameter: client.Api.Customers.GetAsync().
        ClientCallPlanner.BuildKiotaConvention("GET", "/api/customers").ShouldBe("Api.Customers.GetAsync");
    }

    [TestMethod]
    public void DerivesTheExpressionForGetOneCustomer()
    {
        // CustomersItemRequestBuilder.GetAsync().
        ClientCallPlanner.BuildKiotaConvention("GET", "/api/customers/{id}").ShouldBe("Api.Customers[{id}].GetAsync");
    }

    [TestMethod]
    public void LeavesThePlaceholderIntactRatherThanResolvingIt()
    {
        // Stage 3's renderer substitutes FixtureParameter("opKey","param") into `{id}` — this
        // planner must never resolve it itself, or splice a receiver or trailing parentheses,
        // both of which the renderer also owns.
        var expression = ClientCallPlanner.BuildKiotaConvention("GET", "/api/orders/{id}");

        expression.ShouldContain("{id}", Case.Sensitive);
        expression.ShouldNotContain("(");
        expression.ShouldNotContain(")");
    }

    [TestMethod]
    public void ThrowsForAnHttpMethodWithNoKnownKiotaVerbMethod()
    {
        Should.Throw<ArgumentException>(() => ClientCallPlanner.BuildKiotaConvention("TRACE", "/api/orders"));
    }

    // ---- BuildNSwagConvention: measured directly against nswag 14.7.1's openapi2csclient output
    // for [nswag-needs-operationid] — see ClientCallPlanner's own doc comment for the full
    // generated-source evidence (GetOrderByIdAsync(System.Guid id) plus a sibling overload
    // GetOrderByIdAsync(System.Guid id, System.Threading.CancellationToken cancellationToken) on
    // a single configured client class, from operationId "getOrderById"). ---------------------

    [TestMethod]
    public void DerivesTheExpressionForGetOrderByIdWithNSwag()
    {
        // {PascalCase(operationId)}Async — no builder chain, no verb-derived naming: the
        // configured client type IS the receiver, so the method sits directly on it.
        ClientCallPlanner.BuildNSwagConvention("/api/orders/{id}", "getOrderById")
            .ShouldBe("GetOrderByIdAsync({id}, cancellationToken: TestContext.CancellationToken)");
    }

    [TestMethod]
    public void DerivesTheExpressionForListCustomersWithNSwagAndNoPathParameter()
    {
        ClientCallPlanner.BuildNSwagConvention("/api/customers", "listCustomers")
            .ShouldBe("ListCustomersAsync(cancellationToken: TestContext.CancellationToken)");
    }

    [TestMethod]
    public void OrdersMultiplePathParametersInPathTemplateOrder()
    {
        ClientCallPlanner.BuildNSwagConvention("/api/customers/{customerId}/orders/{orderId}", "getCustomerOrder")
            .ShouldBe("GetCustomerOrderAsync({customerId}, {orderId}, cancellationToken: TestContext.CancellationToken)");
    }

    [TestMethod]
    public void LeavesThePathParameterPlaceholderIntactForNSwagToo()
    {
        // TemplateRenderer.BuildClientCallExpression substitutes {id} the same way for both
        // conventions — this planner must never resolve it itself.
        var expression = ClientCallPlanner.BuildNSwagConvention("/api/orders/{id}", "getOrderById");

        expression.ShouldContain("{id}", Case.Sensitive);
    }

    [TestMethod]
    public void IgnoresTheHttpMethodEntirely()
    {
        // Measured: unlike Kiota, NSwag's method name comes from operationId alone, never the
        // verb — DELETE and GET on the same operationId-less-of-verb-info shape derive identically.
        ClientCallPlanner.BuildNSwagConvention("/api/orders/{id}", "cancelOrder")
            .ShouldBe("CancelOrderAsync({id}, cancellationToken: TestContext.CancellationToken)");
    }

    // ---- Resolve: override wins outright, unconditionally ------------------------------------

    [TestMethod]
    public void ResolveReturnsTheOverrideVerbatimWhenOneExists()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getOrderById"] = "Orders[{id}].GetAsync"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "getOrderById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldBe("Orders[{id}].GetAsync");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    [TestMethod]
    public void ResolveReturnsTheOverrideEvenWhenTheOperationHasQueryParametersOrABody()
    {
        // The gate that stops convention from guessing at a query-bound or typed-body call does
        // not apply to an override — the adopter wrote real C# and owns it.
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["listOrders"] = "Orders.GetAsync(rc => rc.QueryParameters.Status = OrderStatus.Placed)"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "listOrders", hasOperationId: true, "GET", "/api/orders",
            hasQueryParameters: true, hasRequestBody: false, overrides);

        resolution.Expression.ShouldBe("Orders.GetAsync(rc => rc.QueryParameters.Status = OrderStatus.Placed)");
    }

    [TestMethod]
    public void ResolveReturnsTheOverrideEvenForANonKiotaKind()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getOrderById"] = "OrdersGETAsync(Guid.Parse(FixtureParameter(\"getOrderById\", \"id\")))"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "getOrderById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldNotBeNull();
    }

    [TestMethod]
    public void ResolveReturnsTheOverrideEvenWhenNoOperationIdIsDeclared()
    {
        // [nswag-needs-operationid]'s presence gate must not apply to an override — the adopter
        // wrote real C# and owns it, exactly as ResolveReturnsTheOverrideEvenWhenTheOperationHasQueryParametersOrABody
        // already pins for the query-parameter/request-body gates.
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["get_root"] = "PingAsync(cancellationToken: TestContext.CancellationToken)"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "get_root", hasOperationId: false, "GET", "/",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldBe("PingAsync(cancellationToken: TestContext.CancellationToken)");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    // ---- Resolve: no override, Kiota convention applies when the shape qualifies -------------

    [TestMethod]
    public void ResolveAppliesTheKiotaConventionWhenNothingBlocksIt()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "getOrderById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBe("Api.Orders[{id}].GetAsync");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    [TestMethod]
    public void ResolveAppliesTheKiotaConventionRegardlessOfOperationIdPresence()
    {
        // hasOperationId only gates NSwag ([nswag-needs-operationid]) — Kiota's own convention is
        // derived purely from the path template and verb, exactly as it always has been, whether
        // or not the spec happens to declare an operationId.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "get_api_orders_id", hasOperationId: false, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBe("Api.Orders[{id}].GetAsync");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    // ---- Resolve: no override, NSwag convention applies once operationId makes it deterministic

    [TestMethod]
    public void ResolveAppliesTheNSwagConventionWhenAnOperationIdWithNoUnderscoreIsPresent()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "getOrderById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBe("GetOrderByIdAsync({id}, cancellationToken: TestContext.CancellationToken)");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    [TestMethod]
    public void ResolveWithholdsTheNSwagConventionWhenNoOperationIdIsDeclared()
    {
        // Reproduces the original v1 measurement exactly: no operationId means NSwag synthesizes
        // {Resource}{VERB}Async with an unpredictable collection-vs-item split and strongly-typed
        // parameters with no string overload — see ClientCallPlanner's own doc comment.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "get_api_orders_id", hasOperationId: false, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("operationId");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveWithholdsTheNSwagConventionWhenTheOperationIdContainsAnUnderscore()
    {
        // Measured: nswag's default operationGenerationMode (MultipleClientsFromOperationId)
        // splits "Orders_GetById" into a separate OrdersClient class with method GetByIdAsync —
        // never a method on the single configured client.typeName.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "Orders_GetById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("'_'", Case.Sensitive);
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveWithholdsTheNSwagConventionForAQueryBoundOperationEvenWithAnOperationId()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "listOrders", hasOperationId: true, "GET", "/api/orders",
            hasQueryParameters: true, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("query parameters");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveWithholdsTheNSwagConventionForAnOperationWithARequestBodyEvenWithAnOperationId()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "createOrder", hasOperationId: true, "POST", "/api/orders",
            hasQueryParameters: false, hasRequestBody: true, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("request body");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    // ---- Resolve: no override, Kiota, but query parameters or a body block convention --------

    [TestMethod]
    public void ResolveWithholdsConventionForAQueryBoundOperation()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "listOrders", hasOperationId: true, "GET", "/api/orders",
            hasQueryParameters: true, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("query parameters");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveWithholdsConventionForAnOperationWithARequestBody()
    {
        // Measured finding: Kiota's PostAsync takes a typed model object, not a JSON string, so
        // there is no compiling way to splice a raw fixture body in.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "createOrder", hasOperationId: true, "POST", "/api/orders",
            hasQueryParameters: false, hasRequestBody: true, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("request body");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveNamesBothReasonsWhenBothApply()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "search", hasOperationId: true, "POST", "/api/orders/search",
            hasQueryParameters: true, hasRequestBody: true, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("query parameters");
        resolution.UnresolvedReason.ShouldContain("request body");
    }

    // ---- Resolve: an unsupported HTTP verb withholds convention instead of crashing ----------

    /// <summary>
    /// Reproduced defect: before this gate existed, a HEAD/OPTIONS/TRACE operation with neither a
    /// query parameter nor a request body sailed past both existing gates and reached
    /// <see cref="ClientCallPlanner.BuildKiotaConvention"/>, which throws for any verb outside
    /// GET/POST/PUT/PATCH/DELETE — confirmed by direct reproduction: a spec with
    /// <c>head: { responses: { "200": … } }</c> on <c>/api/ping</c> crashed `generate` with
    /// <c>ArgumentException: 'HEAD' has no known Kiota verb-method convention</c>, exit 2, the
    /// instant a `client` section was configured. <see cref="ClientCallPlanner.Resolve"/> must
    /// absorb that the same way it already absorbs the query-parameter and request-body gates: a
    /// withheld resolution plus a reason naming the verb, never an escaped exception.
    /// </summary>
    [TestMethod]
    public void ResolveWithholdsConventionForAnUnsupportedHttpVerbInsteadOfThrowing()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "ping", hasOperationId: true, "HEAD", "/api/ping",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("HEAD", Case.Sensitive);
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }

    [TestMethod]
    public void ResolveNeverAppliesTheVerbGateToNSwag()
    {
        // NSwag's method name is derived from operationId alone (BuildNSwagConvention never
        // touches httpMethod), so a HEAD/OPTIONS/TRACE operation that would withhold under Kiota
        // still resolves under NSwag as long as an underscore-free operationId is present.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.NSwag, "pingApi", hasOperationId: true, "HEAD", "/api/ping",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBe("PingApiAsync(cancellationToken: TestContext.CancellationToken)");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    /// <summary>An override still wins outright for an unsupported verb — the same
    /// override-runs-first guarantee <see cref="ResolveReturnsTheOverrideEvenWhenTheOperationHasQueryParametersOrABody"/>
    /// already pins for the other two gates, extended to the new one.</summary>
    [TestMethod]
    public void ResolveReturnsTheOverrideEvenForAnUnsupportedHttpVerb()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ping"] = "Api.Ping.CustomAsync()"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "ping", hasOperationId: true, "HEAD", "/api/ping",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldBe("Api.Ping.CustomAsync()");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    // ---- Resolve: Refit gets no convention guess, permanently and unconditionally ------------

    [TestMethod]
    public void ResolveReturnsNullFromConventionForRefit()
    {
        // Unlike NSwag above, nothing about the operation's shape — an operationId included —
        // could ever change this verdict: [refit-override-only] is permanent, not gated.
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Refit, "getOrderById", hasOperationId: true, "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("Refit");
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }
}
