using InTest.Cli.Configuration;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// The adopter-config rule, tested at its source. Every case here is a hand-edited
/// <c>intest.json</c> — the likeliest way any of them occurs — so the bar for each message is the
/// one <see cref="Naming.CSharpIdentifier.TryValidateDottedName"/> and
/// <see cref="Fixtures.FixtureDocument.TryValidateOperationKey"/> already meet: name the setting,
/// quote what was actually written, narrow to the offending part, state the rule, and end with a
/// remedy and an example.
/// <para>
/// Every test asserts the message does NOT contain "unexpected failure". Before this loader
/// existed each of these reached <c>GenerateCommand</c>'s catch-all and surfaced as
/// <c>intest: unexpected failure: KeyNotFoundException: …</c> — already exit 2, so an exit-code
/// assertion alone would have passed against the defect. The absent-substring assertion is what
/// makes these tests fail for the reason their names state.
/// </para>
/// </summary>
[TestClass]
public class ConfigLoaderTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-cfg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteConfig(string json) => File.WriteAllText(Path.Combine(_root, "intest.json"), json);

    /// <summary>The message for a config that cannot load, with the stack-trace shape ruled out.</summary>
    private string ReasonFor(string json)
    {
        WriteConfig(json);
        var message = Should.Throw<ConfigLoadException>(() => ConfigLoader.Load(_root)).Message;
        message.ShouldNotContain("unexpected failure");
        message.ShouldNotContain("Exception");
        return message;
    }

    private const string Valid = """
    { "schemaVersion": 1, "spec": { "source": "orders.json" },
      "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
    """;

    [TestMethod]
    public void LoadsAValidConfig()
    {
        WriteConfig(Valid);

        var config = ConfigLoader.Load(_root);

        config.SpecSource.ShouldBe("orders.json");
        config.RootNamespace.ShouldBe("Orders.ApiTests");
        config.TestBaseClass.ShouldBe("Orders.ApiTests.OrdersTestBase");
    }

    /// <summary>
    /// `producer` and `name` are settings `intest init` writes but no command reads, and
    /// `intestVersion` is a setting `init` writes that <see cref="ConfigLoader"/> does read
    /// (surfaced on <see cref="LoadedConfig.IntestVersion"/>) but does not require — all three,
    /// plus any wholly unknown key, must not be rejected: §5's config grows by addition, and a
    /// config written by a newer patch release still has to load.
    /// </summary>
    [TestMethod]
    public void IgnoresSettingsItDoesNotRead()
    {
        WriteConfig("""
        { "schemaVersion": 1, "intestVersion": "9.9.9",
          "spec": { "source": "orders.json", "producer": "swashbuckle" },
          "project": { "name": "Orders.ApiTests", "framework": "mstest", "assertions": ["shouldly"],
                       "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" },
          "somethingFromALaterRelease": { "nested": true } }
        """);

        ConfigLoader.Load(_root).SpecSource.ShouldBe("orders.json");
    }

    [TestMethod]
    public void NamesTheProjectDirectoryWhenThereIsNoConfigAtAll()
    {
        var message = Should.Throw<ConfigLoadException>(() => ConfigLoader.Load(_root)).Message;

        message.ShouldContain("intest.json");
        message.ShouldContain(_root);
        message.ShouldContain("intest init");
    }

    [TestMethod]
    public void ExplainsJsonThatDoesNotParseRatherThanThrowingJsonException()
    {
        var reason = ReasonFor("""{ "schemaVersion": 1, "spec": { "source": "orders.json" } """);

        reason.ShouldContain("intest.json");
        reason.ShouldContain("not valid JSON");
    }

    [TestMethod]
    public void ExplainsATopLevelThatIsNotAnObject()
    {
        var reason = ReasonFor("""[ { "spec": { "source": "orders.json" } } ]""");

        reason.ShouldContain("intest.json");
        reason.ShouldContain("array");
        reason.ShouldContain("object");
    }

    // ---- spec ----------------------------------------------------------------------------

    [TestMethod]
    public void ExplainsAMissingSpecSection()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1,
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec");
        reason.ShouldContain("source");
    }

    [TestMethod]
    public void ExplainsASpecSectionThatIsNotAnObject()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": "orders.json",
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec");
        reason.ShouldContain("object");
    }

    [TestMethod]
    public void ExplainsAMissingSpecSource()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "producer": "auto" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec.source");
    }

    /// <summary>
    /// The brief's first named defect. <c>.GetString()!</c> on a number threw
    /// <see cref="InvalidOperationException"/> from deep inside <c>System.Text.Json</c>, naming
    /// neither the file nor the setting.
    /// </summary>
    [TestMethod]
    public void ExplainsASpecSourceThatIsNotAStringAndQuotesWhatWasWritten()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": 42 },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec.source");
        reason.ShouldContain("42");
        reason.ShouldContain("string");
    }

    /// <summary>
    /// The other half of the same defect, and the more dangerous half: <c>.GetString()!</c>
    /// returned null rather than throwing, so the failure surfaced later still — an
    /// <see cref="ArgumentNullException"/> from <c>Path.Combine</c> naming its own <c>path2</c>
    /// parameter, which points at a framework method rather than at the config.
    /// </summary>
    [TestMethod]
    public void ExplainsASpecSourceThatIsJsonNull()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": null },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec.source");
        reason.ShouldContain("null");
    }

    /// <summary>
    /// An empty source is the one case that never threw at all: <c>Path.Combine(root, "")</c>
    /// is <c>root</c>, so <c>SpecLoader</c> reported "Spec file not found:" against the project
    /// directory — an accurate sentence about the wrong thing, which is worse than a crash
    /// because it sends the adopter looking for a missing file that was never named.
    /// </summary>
    [TestMethod]
    public void ExplainsAnEmptySpecSourceRatherThanReportingTheProjectDirectoryAsAMissingSpec()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "   " },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec.source");
        reason.ShouldContain("empty");
        reason.ShouldNotContain("Spec file not found");
    }

    /// <summary>
    /// A URL <c>spec.source</c> is the empty source's twin, and it fails the same way: not on its
    /// own terms. <c>Path.Combine(projectRoot, "https://example.com/openapi.json")</c> treats the
    /// URL as a relative segment, so <c>SpecLoader</c> reported
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c> — a path
    /// the adopter never wrote, a Windows separator spliced onto a URL, phrased as though the
    /// file were merely missing rather than as though the kind of source were unsupported.
    /// <para>
    /// This is the documented path, not a typo: the <c>--spec</c> help text promised "Path or
    /// URL" and getting started's Phase 1 instructed adopters to "Point <c>spec.source</c> at the
    /// URL". Refusing here rather than at <c>init</c> alone is what reaches an adopter who
    /// followed that instruction by hand-editing the config, and it covers <c>fixtures repair</c>
    /// as well as <c>generate</c> — one loader, one answer.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ExplainsAUrlSpecSourceRatherThanReportingAMangledPathAsAMissingSpec()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "https://example.com/openapi.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("spec.source");
        reason.ShouldContain("https://example.com/openapi.json",
            customMessage: "a refusal quotes what the adopter actually wrote");
        reason.ShouldContain("URL",
            customMessage: "a refusal names the kind of value it is refusing, not just that it failed");
        reason.ShouldNotContain("Spec file not found",
            customMessage: "the defect was an accurate sentence about the wrong thing — the spec " +
                           "is not a file that is missing, it is a kind of source InTest cannot read");
    }

    /// <summary>
    /// The false positive the narrow predicate exists to avoid. <c>Uri.TryCreate</c> parses
    /// <c>C:/specs/orders.json</c> as an <i>absolute</i> URI with scheme <c>file</c>, so a
    /// general "is this an absolute URI" check would refuse ordinary Windows paths — the single
    /// most common shape of <c>spec.source</c> on the platform this is developed on. The rule is
    /// therefore an <c>http://</c>/<c>https://</c> prefix and nothing broader.
    /// </summary>
    [TestMethod]
    [DataRow("C:/specs/orders.json", DisplayName = "rooted Windows path — an absolute file: URI to Uri.TryCreate")]
    [DataRow("//fileserver/specs/orders.json", DisplayName = "UNC path")]
    [DataRow("specs/http/orders.json", DisplayName = "path with a url-ish segment")]
    [DataRow("../Orders/bin/Debug/net10.0/orders.json", DisplayName = "the documented relative path")]
    public void LoadsASpecSourceThatIsNotAUrl(string source)
    {
        WriteConfig($$"""
        { "schemaVersion": 1, "spec": { "source": "{{source}}" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        ConfigLoader.Load(_root).SpecSource.ShouldBe(source);
    }

    // ---- project -------------------------------------------------------------------------

    /// <summary>The brief's second named defect: <c>KeyNotFoundException</c> through the catch-all.</summary>
    [TestMethod]
    public void ExplainsAMissingProjectSection()
    {
        var reason = ReasonFor("""{ "schemaVersion": 1, "spec": { "source": "orders.json" } }""");

        reason.ShouldContain("project");
        reason.ShouldContain("rootNamespace");
        reason.ShouldContain("testBaseClass");
    }

    [TestMethod]
    public void ExplainsAProjectSectionThatIsNotAnObject()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" }, "project": ["Orders.ApiTests"] }
        """);

        reason.ShouldContain("project");
        reason.ShouldContain("object");
    }

    [TestMethod]
    public void ExplainsAMissingRootNamespace()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("project.rootNamespace");
    }

    [TestMethod]
    public void ExplainsAMissingTestBaseClass()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests" } }
        """);

        reason.ShouldContain("project.testBaseClass");
    }

    /// <summary>
    /// The hole directly beneath 0f42984. <c>TryValidateDottedName</c> takes a <c>string?</c> and
    /// handles null, so JSON null was already refused — but a number never reached it, because
    /// <c>.GetString()</c> threw first. Validating the type here is what closes that.
    /// </summary>
    [TestMethod]
    public void ExplainsARootNamespaceThatIsNotAString()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": 7, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("project.rootNamespace");
        reason.ShouldContain("7");
        reason.ShouldContain("string");
    }

    [TestMethod]
    public void ExplainsATestBaseClassThatIsNotAString()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": true } }
        """);

        reason.ShouldContain("project.testBaseClass");
        reason.ShouldContain("string");
    }

    // ---- the rule 0f42984 established, still enforced from its new home -------------------

    /// <summary>
    /// Moving validation into the loader must not weaken it. These two pin the message text
    /// <c>GenerateCommand</c> used to emit — including the remedy clause — so the injection fix
    /// cannot regress into a bare type check.
    /// </summary>
    [TestMethod]
    public void StillRefusesARootNamespaceThatIsNotAValidCSharpName()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests; public class Injected { } //",
                       "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("project.rootNamespace");
        reason.ShouldContain("Change project.rootNamespace in intest.json");
        reason.ShouldContain("Orders.ApiTests");
    }

    [TestMethod]
    public void StillRefusesATestBaseClassThatIsNotAValidCSharpName()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.class" } }
        """);

        reason.ShouldContain("project.testBaseClass");
        reason.ShouldContain("Change project.testBaseClass in intest.json");
    }

    [TestMethod]
    public void StillRefusesARootNamespaceThatIsJsonNull()
    {
        ReasonFor("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": null, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """).ShouldContain("project.rootNamespace");
    }

    // ---- schemaVersion ---------------------------------------------------------------------
    // §5: "schemaVersion in intest.json moves only on a major — it is how the CLI detects a
    // config it must not silently reinterpret." Nothing read it until now, so that sentence
    // described a capability the tool did not have: a schemaVersion 2 config was reinterpreted
    // by a schemaVersion 1 CLI, producing wrong output and no error. It is the only failure on
    // this surface that was silent rather than merely badly reported.

    /// <summary>
    /// The message must name the version the config declares and the version this CLI
    /// implements, and must NOT point at `intest upgrade`. `intest upgrade` exists as of v1-e,
    /// but that is not why this assertion still holds — it holds because `upgrade` still cannot
    /// act on this refusal. `UpgradeCommand` calls straight into `GenerateCommand.RunAsync`,
    /// which calls <see cref="ConfigLoader.Load"/> the same way plain `generate` always has, so
    /// this exact check refuses the config before any of `upgrade`'s own edits ever run —
    /// confirmed by building the case directly: republish the CLI with a higher
    /// <see cref="ConfigLoader.SupportedSchemaVersion"/> and run that build's `upgrade` against
    /// an ordinary, older-schema config. Exit 2, config untouched. Naming `intest upgrade` in the
    /// message would point at a command that, for this exact input, cannot act — the
    /// documented-but-unreachable remedy shape this project calls `[paired]` and has closed six
    /// times before. See <see cref="ConfigLoader.RequireSupportedSchemaVersion"/>'s own doc
    /// comment for the fuller version of this reasoning.
    /// </summary>
    [TestMethod]
    public void RefusesASchemaVersionThisCliDoesNotImplement()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 2, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("schemaVersion");
        reason.ShouldContain("2");
        reason.ShouldContain("1");
        reason.ShouldNotContain("upgrade");
    }

    /// <summary>
    /// Same reasoning as <see cref="RefusesASchemaVersionThisCliDoesNotImplement"/> above, for
    /// the sibling case: a config missing <c>schemaVersion</c> entirely is refused by the same
    /// check, before `upgrade`'s regenerate-first call would ever see it, so the message must
    /// not name a remedy that cannot run against this input either.
    /// </summary>
    [TestMethod]
    public void ExplainsAMissingSchemaVersion()
    {
        var reason = ReasonFor("""
        { "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("schemaVersion");
        reason.ShouldNotContain("upgrade");
    }

    [TestMethod]
    public void ExplainsASchemaVersionThatIsNotAnInteger()
    {
        var reason = ReasonFor("""
        { "schemaVersion": "1", "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("schemaVersion");
        reason.ShouldContain("string");
    }

    /// <summary>
    /// A fractional schemaVersion is a JSON number, so a ValueKind check alone would let it
    /// through to an integer conversion that throws.
    /// </summary>
    [TestMethod]
    public void ExplainsASchemaVersionThatIsNotAWholeNumber()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1.5, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("schemaVersion");
        reason.ShouldContain("1.5");
    }

    /// <summary>
    /// schemaVersion governs how every other setting is interpreted, so it is checked before
    /// them: a config from a schema this CLI does not implement must not be reported as having
    /// a bad rootNamespace, when the truth is that its rootNamespace may not mean what this
    /// version thinks it means.
    /// </summary>
    [TestMethod]
    public void ChecksSchemaVersionBeforeTheSettingsItGoverns()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 2, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "not a valid name", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("schemaVersion");
        reason.ShouldNotContain("rootNamespace");
    }

    // ---- intestVersion ---------------------------------------------------------------------
    // [read-what-init-wrote]: intestVersion joins ConfigLoader because that is where the whole
    // document is available, but it stays optional — unlike schemaVersion, which governs how
    // every other setting is interpreted and so must always be declared. Deciding what a
    // version *means* (comparing it against the running CLI) is `generate --check`'s job, not
    // this loader's; this only reads and validates the shape of what is written.

    [TestMethod]
    public void SurfacesAPresentAndWellFormedIntestVersion()
    {
        WriteConfig("""
        { "schemaVersion": 1, "intestVersion": "0.1.0", "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        ConfigLoader.Load(_root).IntestVersion.ShouldBe("0.1.0");
    }

    /// <summary>
    /// The field `init` writes but a config predating it — or one hand-edited without it — does
    /// not have. It must load, and the absence must be visible to the caller as null rather than
    /// silently defaulted to some version string, which would let `--check` compare the running
    /// CLI against a value nobody actually declared.
    /// </summary>
    [TestMethod]
    public void SurfacesNullIntestVersionWhenTheSettingIsMissing()
    {
        WriteConfig(Valid);

        ConfigLoader.Load(_root).IntestVersion.ShouldBeNull();
    }

    /// <summary>
    /// SemVer 2 informational versions put the prerelease label before build metadata
    /// (<c>1.0.0-rc.1+&lt;sha&gt;</c>), so <see cref="CliVersion"/>'s strip-after-first-'+' leaves
    /// the "-rc.1" intact — <c>CliVersion.Current</c> does NOT always take the shape of three
    /// dot-separated whole numbers. A config <c>intest init</c> writes while built from such a
    /// version must still load: rejecting it here is strictly worse than the field being unread
    /// at all, since the same binary both wrote the file and refuses it.
    /// </summary>
    [TestMethod]
    public void SurfacesAPrereleaseIntestVersion()
    {
        WriteConfig("""
        { "schemaVersion": 1, "intestVersion": "1.0.0-rc.1", "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        ConfigLoader.Load(_root).IntestVersion.ShouldBe("1.0.0-rc.1");
    }

    /// <summary>
    /// Under [exact-match], `--check` compares `intestVersion` against the running CLI by string
    /// equality. Any non-empty string that isn't the running version becomes a mismatch whose
    /// message names both sides and points at `upgrade` — a better outcome than a shape-rejection
    /// exit 2, because it tells the adopter something actionable. So a value like "banana" must
    /// load here; only emptiness is refused, since "" is a mistake rather than a version claim
    /// (and under [exact-match] it would otherwise render as "generated by intest ; running tool
    /// is 0.1.0", the hole-in-the-message problem the plan names for the absent case).
    /// </summary>
    [TestMethod]
    public void SurfacesAnyNonEmptyIntestVersionRegardlessOfShape()
    {
        WriteConfig("""
        { "schemaVersion": 1, "intestVersion": "banana", "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        ConfigLoader.Load(_root).IntestVersion.ShouldBe("banana");
    }

    /// <summary>
    /// intestVersion's twin of <see cref="ExplainsASpecSourceThatIsNotAStringAndQuotesWhatWasWritten"/>:
    /// the same <c>ValueKind != JsonValueKind.String</c> shape, on a different setting, and
    /// nothing else in this file pins it. Confirmed by mutation — replacing the throw this guards
    /// with <c>return null</c> and running the suite left it green, because
    /// <see cref="SurfacesAnyNonEmptyIntestVersionRegardlessOfShape"/> and its neighbours only
    /// ever write intestVersion as a string.
    /// </summary>
    [TestMethod]
    public void ExplainsAnIntestVersionThatIsNotAStringAndQuotesWhatWasWritten()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "intestVersion": 42, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("intestVersion");
        reason.ShouldContain("42");
        reason.ShouldContain("string");
    }

    /// <summary>
    /// The other half of the same branch, and the one <c>RequireString</c> already treats
    /// as worth naming specially: <c>declared.ValueKind == JsonValueKind.Null ? "null" :
    /// Quote(declared)</c> exists because JSON null is what a half-finished hand edit leaves
    /// behind — the same reasoning <see cref="ExplainsASpecSourceThatIsJsonNull"/> pins for
    /// spec.source. Losing that ternary in a later refactor is exactly the kind of change a test
    /// asserting only "intestVersion" would not notice; this asserts "null" too.
    /// </summary>
    [TestMethod]
    public void ExplainsAnIntestVersionThatIsJsonNull()
    {
        var reason = ReasonFor("""
        { "schemaVersion": 1, "intestVersion": null, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("intestVersion");
        reason.ShouldContain("null");
    }

    /// <summary>
    /// Empty and whitespace-only get the same refusal, for the same reason: both are "a mistake
    /// rather than a version claim" (<c>ReadOptionalIntestVersion</c>'s own doc comment),
    /// and both would otherwise render §8's mismatch message with a hole where the declared
    /// version belongs. <c>spec.source</c>, thirty lines above in <see cref="ConfigLoader"/>,
    /// draws the identical line with <c>string.IsNullOrWhiteSpace</c> — this mirrors it rather
    /// than re-deriving it. Asserting only "intestVersion" (as this test used to) is too weak to
    /// pin the branch: mutation shows it is the sole guard here, and a message that merely
    /// mentions the setting without saying what is wrong with it would still pass. Asserting
    /// "empty" too, and ruling out the non-string branch's wording, closes that gap.
    /// </summary>
    [TestMethod]
    [DataRow("", DisplayName = "empty string")]
    [DataRow("   ", DisplayName = "whitespace only")]
    public void ExplainsAnEmptyIntestVersion(string value)
    {
        var reason = ReasonFor($$"""
        { "schemaVersion": 1, "intestVersion": "{{value}}", "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        reason.ShouldContain("intestVersion");
        reason.ShouldContain("empty");
        reason.ShouldNotContain("not a string");
    }
}
