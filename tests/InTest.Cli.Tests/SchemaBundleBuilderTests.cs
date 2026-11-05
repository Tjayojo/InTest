using System.Text.Json;
using InTest.Cli.Planning;
using InTest.Cli.Schemas;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class SchemaBundleBuilderTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders": { "get": { "operationId": "listOrders", "responses": { "200": { "description": "ok",
          "content": { "application/json": { "schema": { "type": "array",
            "items": { "$ref": "#/components/schemas/Order" } } } } } } } },
        "/orders/{id}": { "get": { "operationId": "getOrderById", "responses": { "200": { "description": "ok",
          "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } } } } }
      },
      "components": { "schemas": {
        "Order": { "type": "object", "required": ["id"], "properties": {
          "id": { "type": "string" },
          "notes": { "type": "string", "nullable": true },
          "parent": { "$ref": "#/components/schemas/Order" } } } } }
    }
    """;

    private static async Task<string> BuildAsync()
    {
        var spec = await SpecLoader.LoadFromTextAsync(Spec);
        return SchemaBundleBuilder.Build(spec.Document, TestPlanBuilder.Build(spec.Document));
    }

    [TestMethod]
    public async Task IncludesEveryComponentSchemaUnderDefinitions()
    {
        using var doc = JsonDocument.Parse(await BuildAsync());
        doc.RootElement.GetProperty("definitions").TryGetProperty("Order", out _).ShouldBeTrue();
    }

    [TestMethod]
    public async Task RewritesComponentReferencesToDefinitions()
    {
        (await BuildAsync()).ShouldNotContain("#/components/schemas/");
    }

    /// <summary>
    /// <c>enum</c> and <c>pattern</c> are enforced at runtime, so rewriting a component path that
    /// appears as schema <em>data</em> rather than as a <c>$ref</c> makes a contract test give the
    /// wrong verdict: a response carrying the real literal is rejected, one carrying the rewritten
    /// text is accepted. The whole-string <c>.Replace</c> this builder used to end with did
    /// exactly that — measured, including on <c>description</c>.
    /// </summary>
    [TestMethod]
    public async Task DoesNotRewriteAComponentPathThatIsSchemaContentRatherThanAReference()
    {
        const string spec = """
                            {
                              "openapi": "3.0.3",
                              "info": { "title": "Orders", "version": "1.0" },
                              "paths": {
                                "/orders": { "get": { "operationId": "listOrders", "responses": { "200": { "description": "ok",
                                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } } } } }
                              },
                              "components": { "schemas": {
                                "Order": { "type": "object", "description": "See #/components/schemas/Legacy.", "properties": {
                                  "kind": { "type": "string", "enum": ["#/components/schemas/Literal", "ok"] },
                                  "pointer": { "type": "string", "pattern": "^#/components/schemas/.+$" },
                                  "legacy": { "$ref": "#/components/schemas/Legacy" } } },
                                "Legacy": { "type": "object" } } }
                            }
                            """;

        var loaded = await SpecLoader.LoadFromTextAsync(spec);
        var bundle = SchemaBundleBuilder.Build(loaded.Document, TestPlanBuilder.Build(loaded.Document));

        using var doc = JsonDocument.Parse(bundle);
        var order = doc.RootElement.GetProperty("definitions").GetProperty("Order");

        order.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString()
            .ShouldBe("#/components/schemas/Literal");
        order.GetProperty("properties").GetProperty("pointer").GetProperty("pattern").GetString()
            .ShouldBe("^#/components/schemas/.+$");
        order.GetProperty("description").GetString()
            .ShouldBe("See #/components/schemas/Legacy.");

        order.GetProperty("properties").GetProperty("legacy").GetProperty("$ref").GetString()
            .ShouldBe("#/definitions/Legacy", "a real $ref must still be rewritten");
    }

    [TestMethod]
    public async Task GivesInlineResponseSchemasASynthesizedKey()
    {
        using var doc = JsonDocument.Parse(await BuildAsync());
        doc.RootElement.GetProperty("definitions")
           .TryGetProperty("op:listOrders:200:application/json", out _).ShouldBeTrue();
    }

    [TestMethod]
    public async Task NormalizesOpenApi30NullableIntoATypeUnion()
    {
        var bundle = await BuildAsync();
        bundle.ShouldNotContain("\"nullable\"");
        bundle.ShouldContain("null");
    }

    [TestMethod]
    public async Task SelfReferentialSchemasDoNotHang()
    {
        // Order.parent references Order. Bundling under definitions must terminate;
        // inlining would not. Circular-reference resolution is the defect class that
        // deprecated the whole Microsoft.OpenApi 2.x line.
        var task = BuildAsync();
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBe(task);
    }

    [TestMethod]
    public async Task ProducedBundleValidatesRealPayloads()
    {
        var bundle = Runtime.SchemaBundle.FromJson(await BuildAsync());
        bundle.Validate("Order", """{"id":"a","notes":null}""").ShouldBeEmpty();
        bundle.Validate("Order", "{}").ShouldNotBeEmpty();
    }

    /// <summary>
    /// Pins the trailing "\r\n" the v1-e line-endings task added to Build() (LF at the time;
    /// [crlf-everywhere] flips the direction, not the reasoning): before that fix it appended
    /// nothing at all, so spec-schemas.json was the one file in Generated/ with no final newline.
    /// GitattributesSurvivesAnAutocrlfInputCheckout's before/after byte comparison cannot catch a
    /// regression back to that — both sides of that round trip are written by the same call to
    /// Build(), so an unconditional absence would compare equal to itself. This test is the only
    /// thing asserting the newline is there at all, and also that it is exactly one CRLF, not a
    /// bare LF and not doubled.
    /// </summary>
    [TestMethod]
    public async Task EndsWithASingleTrailingCarriageReturnLineFeed()
    {
        var bundle = await BuildAsync();
        bundle.ShouldEndWith("\r\n");
        bundle.ShouldNotEndWith("\r\n\r\n");
    }
}
