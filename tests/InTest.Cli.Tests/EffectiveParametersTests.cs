using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Pins <c>[effective-parameters]</c>'s merge rule directly, independent of any downstream
/// consumer (<c>TestPlanBuilder</c>, <c>FixtureComposer</c>) — issue #7. No spec under
/// <c>samples/</c>, <c>tests/**/Specs/</c> or <c>examples/</c> declares a path-item-level
/// parameter (confirmed before writing this plan), so every spec here is new, not reused —
/// otherwise this coverage would be vacuous.
/// </summary>
[TestClass]
public class EffectiveParametersTests
{
    private static async Task<(Microsoft.OpenApi.IOpenApiPathItem PathItem, Microsoft.OpenApi.OpenApiOperation Operation)>
        LoadAsync(string spec, string path, string method)
    {
        var loaded = await SpecLoader.LoadFromTextAsync(spec);
        var pathItem = loaded.Document.Paths[path];
        var operation = pathItem.Operations![new HttpMethod(method)];
        return (pathItem, operation);
    }

    [TestMethod]
    public async Task APathItemParameterAloneIsMergedIn()
    {
        // getWidget declares no parameters of its own at all — "id" exists only on the path item.
        // This is exactly the shape the issue reproduces: nothing in a plain operation.Parameters
        // read would ever see it.
        const string spec = """
        {
          "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
          "paths": { "/widgets/{id}": {
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "get": { "operationId": "getWidget", "responses": { "200": { "description": "ok" } } }
          } }
        }
        """;

        var (pathItem, operation) = await LoadAsync(spec, "/widgets/{id}", "GET");
        var effective = EffectiveParameters.Resolve(pathItem, operation);

        effective.Count.ShouldBe(1);
        effective[0].Name.ShouldBe("id");
        effective[0].In.ShouldBe(Microsoft.OpenApi.ParameterLocation.Path);
    }

    [TestMethod]
    public async Task AnOperationLevelParameterOverridesAPathItemOneOfTheSameNameAndIn()
    {
        // The path item declares "id" as an untyped string; the operation redeclares the same
        // name+in as a uuid. OpenAPI's override rule says the operation's version wins outright —
        // the merge must not keep, or blend with, the path-item schema.
        const string spec = """
        {
          "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
          "paths": { "/widgets/{id}": {
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "get": {
              "operationId": "getWidget",
              "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } }],
              "responses": { "200": { "description": "ok" } }
            }
          } }
        }
        """;

        var (pathItem, operation) = await LoadAsync(spec, "/widgets/{id}", "GET");
        var effective = EffectiveParameters.Resolve(pathItem, operation);

        effective.Count.ShouldBe(1, "the operation's entry replaces the path item's, it does not sit alongside it");
        effective[0].Schema!.Format.ShouldBe("uuid", "the operation-level schema must win outright");
    }

    [TestMethod]
    public async Task APathItemPathParameterAndAnOperationQueryParameterOfTheSameNameBothSurvive()
    {
        // The case most likely to be got wrong: matching on name alone would treat the
        // operation's "id" (in: query) as an override of the path item's "id" (in: path) and drop
        // one of them. They are different parameters — both must reach the effective list.
        const string spec = """
        {
          "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
          "paths": { "/widgets/{id}": {
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "get": {
              "operationId": "getWidget",
              "parameters": [{ "name": "id", "in": "query", "required": false, "schema": { "type": "string" } }],
              "responses": { "200": { "description": "ok" } }
            }
          } }
        }
        """;

        var (pathItem, operation) = await LoadAsync(spec, "/widgets/{id}", "GET");
        var effective = EffectiveParameters.Resolve(pathItem, operation);

        effective.Count.ShouldBe(2, "a path 'id' and a query 'id' are different parameters and both must survive");
        effective.ShouldContain(p => p.Name == "id" && p.In == Microsoft.OpenApi.ParameterLocation.Path);
        effective.ShouldContain(p => p.Name == "id" && p.In == Microsoft.OpenApi.ParameterLocation.Query);
    }

    [TestMethod]
    public async Task OrderingIsDeterministicPathItemParametersFirstThenUnmatchedOperationParameters()
    {
        // This codebase pins generated output byte-for-byte (TestPlanBuilderTests.IsDeterministic,
        // the golden suite) — an unstable merge order would churn it unpredictably. The rule
        // pinned here: path-item parameters in their own declared order (with an override
        // substituted in place, never moved), then any operation-only parameters in their own
        // declared order.
        const string spec = """
        {
          "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
          "paths": { "/widgets/{id}/parts/{partId}": {
            "parameters": [
              { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } },
              { "name": "partId", "in": "path", "required": true, "schema": { "type": "string" } }
            ],
            "get": {
              "operationId": "getWidgetPart",
              "parameters": [
                { "name": "partId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } },
                { "name": "verbose", "in": "query", "required": false, "schema": { "type": "boolean" } }
              ],
              "responses": { "200": { "description": "ok" } }
            }
          } }
        }
        """;

        var (pathItem, operation) = await LoadAsync(spec, "/widgets/{id}/parts/{partId}", "GET");
        var effective = EffectiveParameters.Resolve(pathItem, operation);

        // "id" (path-item order, position 0), "partId" (path-item order, position 1, but
        // substituted with the operation's uuid-typed override), then "verbose" (operation-only,
        // appended after every path-item entry).
        effective.Select(p => p.Name).ShouldBe(["id", "partId", "verbose"]);
        effective.Single(p => p.Name == "partId").Schema!.Format.ShouldBe("uuid",
            "the override's schema must come through even though its position is inherited from the path item");
    }

    [TestMethod]
    public async Task APathItemWithNoParametersLeavesTheOperationsListUnchanged()
    {
        // The inert case every existing spec under samples/, tests/**/Specs/ and examples/
        // actually is: no pathItem.Parameters at all, so the merge must reproduce
        // operation.Parameters exactly, in its own order — this is what keeps the fix from
        // changing any committed golden file, fixture or example.
        const string spec = """
        {
          "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
          "paths": { "/orders/{id}": {
            "get": {
              "operationId": "getOrder",
              "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
              "responses": { "200": { "description": "ok" } }
            }
          } }
        }
        """;

        var (pathItem, operation) = await LoadAsync(spec, "/orders/{id}", "GET");
        var effective = EffectiveParameters.Resolve(pathItem, operation);

        effective.Count.ShouldBe(1);
        effective[0].ShouldBeSameAs(operation.Parameters![0]);
    }
}
