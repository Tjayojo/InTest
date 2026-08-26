using InTest.Cli.Planning;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Asserts <see cref="ClientCallPlanner.BuildKiotaConvention"/> against captured real generator
/// output: a real `kiota` 1.34.1 client built from <c>samples/Orders.Api/Orders.Api.json</c> — see
/// <see cref="ClientCallPlanner"/>'s own doc comment for the file-by-file evidence. Every expected
/// string below is the exact call shape read directly from the generated
/// <c>OrdersApiClient</c>/<c>ApiRequestBuilder</c>/<c>OrdersRequestBuilder</c>/
/// <c>OrdersItemRequestBuilder</c>/<c>CustomersRequestBuilder</c>/<c>CustomersItemRequestBuilder</c>
/// classes, not guessed from Kiota's documentation.
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

    // ---- Resolve: override wins outright, unconditionally ------------------------------------

    [TestMethod]
    public void ResolveReturnsTheOverrideVerbatimWhenOneExists()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getOrderById"] = "Orders[{id}].GetAsync"
        };

        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "getOrderById", "GET", "/api/orders/{id}",
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
            ClientKind.Kiota, "listOrders", "GET", "/api/orders",
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
            ClientKind.NSwag, "getOrderById", "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldNotBeNull();
    }

    // ---- Resolve: no override, Kiota convention applies when the shape qualifies -------------

    [TestMethod]
    public void ResolveAppliesTheKiotaConventionWhenNothingBlocksIt()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "getOrderById", "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBe("Api.Orders[{id}].GetAsync");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    // ---- Resolve: no override, Kiota, but query parameters or a body block convention --------

    [TestMethod]
    public void ResolveWithholdsConventionForAQueryBoundOperation()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "listOrders", "GET", "/api/orders",
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
            ClientKind.Kiota, "createOrder", "POST", "/api/orders",
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
            ClientKind.Kiota, "search", "POST", "/api/orders/search",
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
    /// instant a `client` section was configured. <see cref="Resolve"/> must absorb that the same
    /// way it already absorbs the query-parameter and request-body gates: a withheld resolution
    /// plus a reason naming the verb, never an escaped exception.
    /// </summary>
    [TestMethod]
    public void ResolveWithholdsConventionForAnUnsupportedHttpVerbInsteadOfThrowing()
    {
        var resolution = ClientCallPlanner.Resolve(
            ClientKind.Kiota, "ping", "HEAD", "/api/ping",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain("HEAD", Case.Sensitive);
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
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
            ClientKind.Kiota, "ping", "HEAD", "/api/ping",
            hasQueryParameters: false, hasRequestBody: false, overrides);

        resolution.Expression.ShouldBe("Api.Ping.CustomAsync()");
        resolution.UnresolvedReason.ShouldBeNull();
    }

    // ---- Resolve: NSwag and Refit get no convention guess -------------------------------------

    [TestMethod]
    [DataRow(ClientKind.NSwag)]
    [DataRow(ClientKind.Refit)]
    public void ResolveReturnsNullFromConventionForNonKiotaKinds(ClientKind kind)
    {
        // Even for a shape that would qualify under Kiota (no query params, no body) — the gap is
        // the generator, not the operation's own shape.
        var resolution = ClientCallPlanner.Resolve(
            kind, "getOrderById", "GET", "/api/orders/{id}",
            hasQueryParameters: false, hasRequestBody: false, NoOverrides);

        resolution.Expression.ShouldBeNull();
        resolution.UnresolvedReason.ShouldNotBeNull();
        resolution.UnresolvedReason.ShouldContain(kind.ToString());
        resolution.UnresolvedReason.ShouldContain("client-map.json", Case.Sensitive);
    }
}
