using InTest.Cli.Fixtures;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixtureDocumentTests
{
    [TestMethod]
    public void RoundTripsThroughJson()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 2, OperationId = "post_api_products", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7" },
            Body = System.Text.Json.Nodes.JsonNode.Parse("""{"sku":"WGT-0001"}""")
        };

        var reloaded = FixtureDocument.Parse(document.ToJson());

        reloaded.Meta.Tier.ShouldBe(2);
        reloaded.Meta.OperationId.ShouldBe("post_api_products");
        reloaded.Parameters["id"].ShouldBe("7");
        reloaded.Body!.ToJsonString().ShouldContain("WGT-0001", Case.Sensitive);
    }

    [TestMethod]
    public void OmitsBodyForOperationsThatTakeNone()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 1, OperationId = "get_api_products_id", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["id"] = "7" }
        };

        document.ToJson().ShouldNotContain("\"body\"");
    }

    [TestMethod]
    public void SerializationIsStableSoDiffsStayReviewable()
    {
        var document = new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = 4, OperationId = "op", GeneratedBy = "intest 0.2.0" },
            Parameters = new() { ["zebra"] = "1", ["alpha"] = "2" }
        };

        document.ToJson().ShouldBe(FixtureDocument.Parse(document.ToJson()).ToJson());
        document.ToJson().IndexOf("alpha", StringComparison.Ordinal)
                .ShouldBeLessThan(document.ToJson().IndexOf("zebra", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RejectsAFixtureWithoutMeta()
    {
        Should.Throw<FixtureFormatException>(() => FixtureDocument.Parse("""{"body":{}}"""));
    }

    [TestMethod]
    [DataRow("""{"$meta":{"tier":"high","operationId":"op","generatedBy":"intest 0.2.0"}}""", "tier",
        DisplayName = "string where $meta.tier must be a number")]
    [DataRow("""{"$meta":{"tier":1,"operationId":"op","generatedBy":"intest 0.2.0"},"$parameters":{"id":7}}""", "parameters.id",
        DisplayName = "number where a $parameters value must be a string")]
    [DataRow("""{"$meta":{"tier":1,"operationId":"op","generatedBy":"intest 0.2.0"},"$parameters":{"id":{"a":1}}}""", "parameters.id",
        DisplayName = "object where a $parameters value must be a string")]
    public void ReportsAMalformedFieldRatherThanCrashingWithAFrameworkException(string json, string fieldFragment)
    {
        // Fixtures are committed and hand-edited: an unquoted number or a stray nested object is
        // a realistic typo, not adversarial input. Left unguarded these throw raw framework
        // exceptions ("An element of type 'String' cannot be converted to a 'System.Int32'",
        // "The node must be of type 'JsonValue'") that would crash FixtureStore at runtime over
        // one malformed fixture — this class's own doc comment says one bad thing must not
        // abandon the document, and an unhandled crash is the worst version of abandoning it.
        var exception = Should.Throw<FixtureFormatException>(() => FixtureDocument.Parse(json));

        exception.Message.ShouldContain(fieldFragment, Case.Sensitive);
        exception.InnerException.ShouldNotBeNull("the framework exception explaining the real cause must not be discarded");
    }

    [TestMethod]
    [DataRow("""{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":["a","b"]}""",
        DisplayName = "array where $parameters must be an object")]
    [DataRow("""{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":"id=7"}""",
        DisplayName = "string where $parameters must be an object")]
    [DataRow("""{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":7}""",
        DisplayName = "number where $parameters must be an object")]
    public void ReportsAMalformedParametersBlockRatherThanSilentlyDroppingIt(string json)
    {
        // The container shape, not just the values inside it. A pattern match with no else branch
        // reads a '$parameters' that is an array or a pasted query string as "this operation takes
        // no parameters" — the fixture then loads clean, every parameter is silently absent, and
        // the request goes out malformed. That is the one outcome this class exists to prevent,
        // and '$meta' three lines above already rejects a wrong container shape.
        Should.Throw<FixtureFormatException>(() => FixtureDocument.Parse(json))
              .Message.ShouldContain("$parameters", Case.Sensitive);
    }

    [TestMethod]
    public void TreatsAnExplicitlyNullParametersBlockAsAbsent()
    {
        // Deliberately not an error, and the same reading 'body' already gets: JSON null is how a
        // hand-editor writes "there are none". Only a wrong *shape* is malformed.
        var document = FixtureDocument.Parse(
            """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":null}""");

        document.Parameters.ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow("""{"$meta":{"tier":1,"generatedBy":"intest 0.2.0"}}""", DisplayName = "operationId absent")]
    [DataRow("""{"$meta":{"tier":1,"operationId":"   ","generatedBy":"intest 0.2.0"}}""", DisplayName = "operationId blank")]
    public void RejectsAFixtureWithNoUsableOperationId(string json)
    {
        // operationId is not a defaultable field like tier or generatedBy: `fixtures repair`
        // needs it to tell "missing, needs fixing" from "present and correct", and it cannot if
        // Parse quietly turns an absent value into "".
        var exception = Should.Throw<FixtureFormatException>(() => FixtureDocument.Parse(json));
        exception.Message.ShouldContain("operationId", Case.Sensitive);
    }

    [TestMethod]
    [DataRow("post_api_products", DisplayName = "synthesized key")]
    [DataRow("Stock_GetBySku", DisplayName = "NSwag {Controller}_{Action} key")]
    [DataRow("getOrderById", DisplayName = "hand-written camelCase operationId")]
    public void AcceptsAnOperationKeyThatIsAlreadyFileNameSafe(string key)
    {
        FixtureDocument.FileNameFor(key).ShouldBe(key + ".json");
    }

    [TestMethod]
    [DataRow("Orders/Create", "/", DisplayName = "path separator")]
    [DataRow("Orders?Create", "?", DisplayName = "wildcard character")]
    [DataRow("orders:create", ":", DisplayName = "stream separator")]
    [DataRow("Orders\\Create", "\\", DisplayName = "backslash — invalid on Windows, legal on Unix")]
    public void ReportsAnOperationKeyThatCannotBeAFileName(string key, string offending)
    {
        // Try-pattern, not an exception: an unusable operationId is one operation InTest cannot
        // serve, not a reason to abandon the other 147 in the document. The caller records a
        // skip and continues — see Task 2a.
        FixtureDocument.TryValidateOperationKey(key, out var reason).ShouldBeFalse();

        reason.ShouldContain(key, Case.Sensitive);
        // No Case.Sensitive on `offending`: every row supplies a punctuation mark, which has no
        // casing for the annotation to pin. It would read as a claim and check nothing.
        reason.ShouldContain(offending);
        reason.ShouldContain("operationId", Case.Sensitive);
    }

    [TestMethod]
    public void ReportsAnOperationKeyThatIsTooLongToBeAFileName()
    {
        // An operationId past this length passes TryValidateOperationKey today and then fails as
        // a raw OS path-length error wherever a caller writes the file — outside this class's
        // error path, for exactly the input this class exists to gate.
        var key = new string('a', 201);

        FixtureDocument.TryValidateOperationKey(key, out var reason).ShouldBeFalse();

        reason.ShouldContain("201");
        reason.ShouldContain("200");
        reason.ShouldContain("exceeds");
    }

    [TestMethod]
    public void ReportsAControlCharacterEvenThoughUnixWouldAllowIt()
    {
        // Path.GetInvalidFileNameChars() returns only NUL and '/' on Unix, so a literal tab
        // would otherwise pass validation there and be written into a fixture filename verbatim
        // — the same gap already hardened for separators, closed the same way.
        FixtureDocument.TryValidateOperationKey("Orders\tCreate", out var reason).ShouldBeFalse();

        reason.ShouldContain("operationId", Case.Sensitive);
    }

    [TestMethod]
    public void RejectsBackslashOnEveryPlatformNotJustWindows()
    {
        // Path.GetInvalidFileNameChars() is platform-specific: 41 characters on Windows
        // (verified), but only NUL and '/' on Unix. Delegating to it would accept
        // Orders\Create on Linux and write a file literally named Orders\Create.json, so the
        // explicit list carries the separators rather than trusting the framework's per-OS answer.
        FixtureDocument.TryValidateOperationKey("Orders\\Create", out _).ShouldBeFalse();
    }

    [TestMethod]
    public void TheExplicitSeparatorListCarriesEveryCharacterUnixWouldOtherwiseAllow()
    {
        // Path.GetInvalidFileNameChars() returns 41 characters on Windows but only NUL and
        // '/' on Unix. Asserting through TryValidateOperationKey would pass on Windows even
        // if this list were empty, because the framework list masks it — so assert the list.
        FixtureDocument.InvalidOperationKeyCharacters.ShouldBe(
            new[] { '/', '\\', '?', '*', ':', '"', '<', '>', '|' }, ignoreOrder: true);
    }

    [TestMethod]
    public void ReportsAWindowsReservedDeviceName()
    {
        FixtureDocument.TryValidateOperationKey("CON", out var reason).ShouldBeFalse();
        reason.ShouldContain("reserved");
    }

    [TestMethod]
    public void FileNameForStillThrowsBecauseCallersMustValidateFirst()
    {
        // FileNameFor is only reached for keys the plan already accepted. Throwing here is an
        // invariant violation, not flow control — the flow-control path is TryValidateOperationKey.
        Should.Throw<FixtureFormatException>(() => FixtureDocument.FileNameFor("Orders/Create"));
    }

}
