using InTest.Cli.Fixtures;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixtureComposerTests
{
    private static async Task<FixtureDocument> ComposeAsync(string spec, string path, string method)
    {
        var loaded = await SpecLoader.LoadFromTextAsync(spec);
        return FixtureComposer.Compose(loaded.Document, path, method, "op_key", "intest 0.2.0");
    }

    private const string TierOne = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/p":{"post":{
        "requestBody":{"content":{"application/json":{
          "schema":{"type":"object","properties":{"sku":{"type":"string"}}},
          "example":{"sku":"REAL-0001"}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    [TestMethod]
    public async Task Tier1UsesTheMediaTypeExampleVerbatim()
    {
        var fixture = await ComposeAsync(TierOne, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(1);
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("REAL-0001");
    }

    [TestMethod]
    public async Task Tier2ComposesFromPerPropertyExamples()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"sku":{"type":"string","example":"EX-1"},"qty":{"type":"integer","example":5}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(2);
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("EX-1");
        fixture.Body["qty"]!.GetValue<int>().ShouldBe(5);
    }

    [TestMethod]
    public async Task Tier3UsesDefaults()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"currency":{"type":"string","default":"GBP"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");
        fixture.Meta.Tier.ShouldBe(3);
        fixture.Body!["currency"]!.GetValue<string>().ShouldBe("GBP");
    }

    [TestMethod]
    public async Task Tier4EmitsObviousSentinelsNeverPlausibleValues()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        // "string" or 0 would be schema-valid, so a permissive endpoint would accept them and
        // the suite would assert nothing while looking healthy. The sentinel must be obvious.
        fixture.Body!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task ComposesNestedObjectsAndArrays()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{
                "dims":{"type":"object","properties":{"w":{"type":"number"}}},
                "lines":{"type":"array","items":{"type":"object","properties":{"sku":{"type":"string"}}}}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Body!["dims"]!["w"].ShouldNotBeNull();
        fixture.Body["lines"]!.AsArray().Count.ShouldBe(1, "one element is enough to show the shape");
        fixture.Body["lines"]![0]!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task OnlySentinelsRequiredParameters()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p/{id}":{"get":{
            "parameters":[
              {"name":"id","in":"path","required":true,"schema":{"type":"string"}},
              {"name":"page","in":"query","schema":{"type":"integer","example":2}},
              {"name":"sort","in":"query","schema":{"type":"string","default":"name"}},
              {"name":"filter","in":"query","schema":{"type":"string"}},
              {"name":"X-Trace","in":"header","schema":{"type":"string"}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p/{id}", "GET");

        fixture.Parameters["id"].ShouldBe("TODO:id", "required parameters must be supplied");
        fixture.Parameters["page"].ShouldBe("2", "an example is a real value, not a sentinel");
        fixture.Parameters["sort"].ShouldBe("name", "a default is a real value too");

        // The regression this prevents: Catalog's GET /api/products declares five optional
        // query parameters and passes today. Sentinelling them would block a working operation
        // and leave Task 10 below the 6 passing tests v0 already achieved.
        fixture.Parameters.ShouldNotContainKey("filter", "an optional parameter with no value is omitted");
        fixture.Parameters.ShouldNotContainKey("X-Trace", "headers are not path or query parameters");
    }

    [TestMethod]
    public async Task SentinelsAreStringsRegardlessOfDeclaredType()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "required":["price","active"],"properties":{
                "price":{"type":"number"},"active":{"type":"boolean"}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        // A zero would be schema-valid and indistinguishable from a deliberate value, leaving
        // repair no way to know it was never filled in. See decision 3.
        fixture.Body!["price"]!.GetValue<string>().ShouldBe("TODO:price");
        fixture.Body["active"]!.GetValue<string>().ShouldBe("TODO:active");
    }

    [TestMethod]
    public async Task OmitsBodyWhenTheOperationTakesNone()
    {
        const string spec = """
        {"openapi":"3.0.3","info":{"title":"T","version":"1"},
         "paths":{"/p":{"get":{"responses":{"200":{"description":"ok"}}}}}}
        """;

        (await ComposeAsync(spec, "/p", "GET")).Body.ShouldBeNull();
    }

    [TestMethod]
    public async Task StopsAtARepeatedSchemaReference()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Node"}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Node":{"type":"object","properties":{
            "name":{"type":"string"},"child":{"$ref":"#/components/schemas/Node"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        // Asserted on observable output, not by racing a timeout. Compose is synchronous, so
        // non-termination stack-overflows or hangs the test host before any timeout could be
        // observed — a timeout guard here passes only when the bug is absent, which is not the
        // case it exists for.
        fixture.Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
        fixture.Body["child"].ShouldBeNull("a repeated reference emits null and stops");
    }

    // F6: an un-navigated oneOf/anyOf/allOf fell through every check in ComposeFromSchema and
    // sentinelled the whole property as a string, hiding that it was really an object with its
    // own required fields. The tests below pin the fix and the invariants it depends on.

    [TestMethod]
    [DataRow("oneOf")]
    [DataRow("anyOf")]
    public async Task AUnionWithANullBranchComposesTheRemainingSchemaSkeleton(string keyword)
    {
        // OpenAPI 3.1's idiom for a nullable reference, and exactly what the built-in
        // Microsoft.AspNetCore.OpenApi producer emits for a nullable request-body property. oneOf
        // and anyOf are both real 3.1 documents for this shape — a $ref alongside `type: null` —
        // and SoleUnionBranch filters and counts them identically, so one parameterized spec
        // covers both rather than restating the same shape per keyword. allOf gets its own test
        // below: `allOf: [{type: null}, {$ref: ...}]` would mean "satisfies both null and this
        // object" — a document no producer emits and no schema can satisfy — so it is deliberately
        // not a third row here. A plain token substitution, not string interpolation, sidesteps
        // the raw string literal's brace-counting rules against this much literal JSON.
        const string specTemplate = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"dimensions":{"__KEYWORD__":[{"type":"null"},{"$ref":"#/components/schemas/Dimensions"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Dimensions":{"type":"object","properties":{
            "length":{"type":"number"},"width":{"type":"number"}}}}}
        }
        """;
        var spec = specTemplate.Replace("__KEYWORD__", keyword);

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        fixture.Body!["dimensions"]!["length"]!.GetValue<string>().ShouldBe("TODO:length");
        fixture.Body["dimensions"]!["width"]!.GetValue<string>().ShouldBe("TODO:width");
    }

    [TestMethod]
    public async Task AllOfWithASingleRefBranchComposesThatSchema()
    {
        // allOf's realistic single-branch form: no null branch, since allOf means every branch's
        // constraints must hold simultaneously and a null branch alongside a $ref would be
        // unsatisfiable (see the comment above). One branch, already non-null, so SoleUnionBranch
        // finds it without needing to discard anything.
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"dimensions":{"allOf":[{"$ref":"#/components/schemas/Dimensions"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Dimensions":{"type":"object","properties":{
            "length":{"type":"number"},"width":{"type":"number"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        fixture.Body!["dimensions"]!["length"]!.GetValue<string>().ShouldBe("TODO:length");
        fixture.Body["dimensions"]!["width"]!.GetValue<string>().ShouldBe("TODO:width");
    }

    [TestMethod]
    public async Task AUnionBranchWithAnExampleRecordsTier2NotASentinel()
    {
        // Every other union test here bottoms out in a sentinel. Recursing into the surviving
        // branch can just as well find a real example or default first — that value, and its
        // tier, must come through unchanged; the union check must not force everything under it
        // down to tier 4.
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"currency":{"oneOf":[{"type":"null"},{"type":"string","example":"GBP"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(2);
        fixture.Body!["currency"]!.GetValue<string>().ShouldBe("GBP");
    }

    [TestMethod]
    public async Task AGenuinelyAmbiguousUnionStillEmitsASentinel()
    {
        // Two non-null branches: guessing which one applies would produce a value that looks
        // deliberate but is not. A sentinel is the honest answer.
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"value":{"oneOf":[{"type":"string"},{"type":"integer"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        fixture.Body!["value"]!.GetValue<string>().ShouldBe("TODO:value");
    }

    [TestMethod]
    public async Task AUnionOfOnlyNullBranchesStillEmitsASentinel()
    {
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"value":{"oneOf":[{"type":"null"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        fixture.Body!["value"]!.GetValue<string>().ShouldBe("TODO:value");
    }

    [TestMethod]
    public async Task ASelfReferencingUnionTerminatesInsteadOfRecursingForever()
    {
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Node"}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Node":{"type":"object","properties":{
            "name":{"type":"string"},
            "next":{"oneOf":[{"type":"null"},{"$ref":"#/components/schemas/Node"}]}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        // Asserted on observable output, not by racing a timeout — see
        // StopsAtARepeatedSchemaReference above for why a timeout guard would be unsound here.
        fixture.Meta.Tier.ShouldBe(4);
        fixture.Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
        fixture.Body["next"].ShouldBeNull("a self-referencing union branch emits null and stops");
    }

    [TestMethod]
    public async Task ComposesItsOwnPropertiesEvenWhenAnAllOfIsAlsoPresent()
    {
        // Pins the placement invariant on the union check in ComposeFromSchema: it runs only
        // after the object check, so a schema declaring both `type: object` (with its own
        // properties) and an allOf still composes those declared properties instead of diverting
        // into the allOf branch. Moving the union check earlier would pass every other test in
        // this file while silently dropping "sku" here.
        const string spec = """
        {
          "openapi":"3.1.0","info":{"title":"T","version":"1"},
          "paths":{"/p":{"post":{
            "requestBody":{"content":{"application/json":{"schema":{"type":"object",
              "properties":{"item":{"type":"object","properties":{"sku":{"type":"string"}},
                "allOf":[{"$ref":"#/components/schemas/Base"}]}}}}}},
            "responses":{"201":{"description":"ok"}}}}},
          "components":{"schemas":{"Base":{"type":"object","properties":{
            "id":{"type":"string"}}}}}
        }
        """;

        var fixture = await ComposeAsync(spec, "/p", "POST");

        fixture.Meta.Tier.ShouldBe(4);
        // Split so a regression here names the invariant instead of throwing a bare
        // NullReferenceException: under the broken (hoisted) ordering, "item" composes from
        // "Base" instead of its own properties, "sku" is absent, and a combined `!.GetValue<>()`
        // would fail with only a line number to go on.
        fixture.Body!["item"]!["sku"].ShouldNotBeNull(
            "the union check must stay after the object check — a schema with both type: object "
            + "and an allOf composes its own declared properties (F6)");
        fixture.Body["item"]!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    private const string NeedsFixtureSpec = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{
        "/body":{"post":{
          "requestBody":{"content":{"application/json":{"schema":{"type":"object"}}}},
          "responses":{"201":{"description":"ok"}}}},
        "/path/{id}":{"get":{
          "parameters":[{"name":"id","in":"path","required":true,"schema":{"type":"string"}}],
          "responses":{"200":{"description":"ok"}}}},
        "/query":{"get":{
          "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","example":2}}],
          "responses":{"200":{"description":"ok"}}}},
        "/nothing":{"get":{"responses":{"200":{"description":"ok"}}}},
        "/body-no-schema":{"post":{
          "requestBody":{"content":{"application/json":{}}},
          "responses":{"201":{"description":"ok"}}}}
      }
    }
    """;

    [TestMethod]
    public async Task NeedsFixtureAgreesExactlyWithWhatComposeActuallyProduces()
    {
        // Pins the two sides together: NeedsFixture must say yes precisely when Compose would
        // write something a caller could observe (a body, or a non-empty $parameters block) —
        // never a hardcoded true/false per case, since the point is the equivalence itself.
        var loaded = await SpecLoader.LoadFromTextAsync(NeedsFixtureSpec);
        var document = loaded.Document;

        var cases = new (string Path, string Method)[]
        {
            ("/body", "POST"),
            ("/path/{id}", "GET"),
            ("/query", "GET"),
            ("/nothing", "GET"),
            ("/body-no-schema", "POST"),
        };

        foreach (var (path, method) in cases)
        {
            var pathItem = document.Paths[path];
            var operation = pathItem.Operations![new HttpMethod(method)];
            var effectiveParameters = InTest.Cli.Spec.EffectiveParameters.Resolve(pathItem, operation);
            var fixture = FixtureComposer.Compose(document, path, method, "op_key", "intest 0.2.0");
            var composeProducesSomething = fixture.Body is not null || fixture.Parameters.Count > 0;

            FixtureComposer.NeedsFixture(effectiveParameters, operation).ShouldBe(composeProducesSomething, $"{method} {path}");
        }
    }

    // Issue #7: nothing in FixtureComposer used to read pathItem.Parameters, so an "id" declared
    // only at the path-item level (rather than repeated on the operation) never became a fixture
    // entry — the operation's own success case still generated, and the raw-HTTP call it emitted
    // referenced a FixtureParameter that was never written. Reproduced against getWidget below,
    // structurally identical to getGadget except for where "id" is declared.
    private const string PathItemParameterSpec = """
    {
      "openapi": "3.0.3", "info": { "title": "T", "version": "1" },
      "paths": {
        "/gadgets/{id}": { "get": {
          "operationId": "getGadget",
          "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
          "responses": { "200": { "description": "ok" } } } },
        "/widgets/{id}": {
          "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
          "get": {
            "operationId": "getWidget",
            "responses": { "200": { "description": "ok" } } } }
      }
    }
    """;

    [TestMethod]
    public async Task ComposesAFixtureEntryForAParameterDeclaredOnlyOnThePathItem()
    {
        var fixture = await ComposeAsync(PathItemParameterSpec, "/widgets/{id}", "GET");

        // Same sentinel a required path parameter always gets (decision 1), proving the
        // path-item-declared "id" reached Compose exactly the way the operation-declared twin's
        // does — see the next test.
        fixture.Parameters.ShouldContainKey("id");
        fixture.Parameters["id"].ShouldBe("TODO:id");
    }

    [TestMethod]
    public async Task AnOperationLevelAndAPathItemLevelTwinComposeIdenticalFixtures()
    {
        var fromOperationLevel = await ComposeAsync(PathItemParameterSpec, "/gadgets/{id}", "GET");
        var fromPathItemLevel = await ComposeAsync(PathItemParameterSpec, "/widgets/{id}", "GET");

        fromPathItemLevel.Parameters.ShouldBe(fromOperationLevel.Parameters,
            "getGadget (id on the operation) and getWidget (id on the path item) are structurally " +
            "identical and must compose the same fixture — the bug was that only one of them did");
    }

    [TestMethod]
    public async Task NeedsFixtureIsTrueForAParameterDeclaredOnlyOnThePathItem()
    {
        var loaded = await SpecLoader.LoadFromTextAsync(PathItemParameterSpec);
        var pathItem = loaded.Document.Paths["/widgets/{id}"];
        var operation = pathItem.Operations![HttpMethod.Get];
        var effectiveParameters = InTest.Cli.Spec.EffectiveParameters.Resolve(pathItem, operation);

        FixtureComposer.NeedsFixture(effectiveParameters, operation).ShouldBeTrue(
            "a path-item-declared required path parameter must be seen just like an operation-declared one");
    }
}
