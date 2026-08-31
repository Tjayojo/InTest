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
                                   "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                                "framework": "mstest" } }
                                 """;

    [TestMethod]
    public void LoadsAValidConfig()
    {
        WriteConfig(Valid);

        var config = ConfigLoader.Load(_root);

        config.SpecSource.ShouldBe("orders.json");
        config.RootNamespace.ShouldBe("Orders.ApiTests");
        config.TestBaseClass.ShouldBe("Orders.ApiTests.OrdersTestBase");
        config.Framework.ShouldBe("mstest");
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

        message.ShouldContain("intest.json", Case.Sensitive);
        message.ShouldContain(_root);
        message.ShouldContain("intest init", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsJsonThatDoesNotParseRatherThanThrowingJsonException()
    {
        var reason = ReasonFor("""{ "schemaVersion": 1, "spec": { "source": "orders.json" } """);

        reason.ShouldContain("intest.json", Case.Sensitive);
        reason.ShouldContain("not valid JSON");
    }

    [TestMethod]
    public void ExplainsATopLevelThatIsNotAnObject()
    {
        var reason = ReasonFor("""[ { "spec": { "source": "orders.json" } } ]""");

        reason.ShouldContain("intest.json", Case.Sensitive);
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

        reason.ShouldContain("spec", Case.Sensitive);
        reason.ShouldContain("source", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsASpecSectionThatIsNotAnObject()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": "orders.json",
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                               """);

        reason.ShouldContain("spec", Case.Sensitive);
        reason.ShouldContain("object");
    }

    [TestMethod]
    public void ExplainsAMissingSpecSource()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "producer": "auto" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                               """);

        reason.ShouldContain("spec.source", Case.Sensitive);
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

        reason.ShouldContain("spec.source", Case.Sensitive);
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

        reason.ShouldContain("spec.source", Case.Sensitive);
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

        reason.ShouldContain("spec.source", Case.Sensitive);
        reason.ShouldContain("empty");
        reason.ShouldNotContain("Spec file not found");
    }

    /// <summary>
    /// A URL <c>spec.source</c> loads, and says so: §9's snapshot shipped, so a URL is a
    /// supported kind of source rather than a refusal with a roadmap attached.
    /// <para>
    /// This test replaces a refusal test rather than deleting one, because the defect that
    /// refusal was built for is still the reason this value is judged here at all.
    /// <c>Path.Combine(projectRoot, "https://example.com/openapi.json")</c> treats the URL as a
    /// relative segment, so <c>SpecLoader</c> reported
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c> — a path
    /// the adopter never wrote, a Windows separator spliced onto a URL, phrased as though the
    /// file were merely missing. A malformed URL reaches exactly that outcome today if it gets
    /// past <see cref="Spec.SpecFetcher.TryValidateUrl"/>, which is what
    /// <see cref="RefusesAMalformedUrlSpecSource"/> pins.
    /// </para>
    /// <para>
    /// <see cref="LoadedConfig.SpecSourceIsUrl"/> is asserted, not just the absence of a throw:
    /// the flag is what decides whether <c>generate</c> fetches and whether
    /// <c>fixtures repair</c> reads <c>spec.json</c>, so "it loaded" is only half the contract.
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("https://example.com/openapi.json", DisplayName = "https")]
    [DataRow("http://example.com/openapi.json", DisplayName = "http")]
    [DataRow("HTTPS://EXAMPLE.COM/openapi.json", DisplayName = "uppercase scheme")]
    [DataRow("https://orders-staging.example.com/swagger/v1/swagger.json", DisplayName = "a real swagger endpoint shape")]
    public void LoadsAUrlSpecSourceAndMarksItAsOne(string source)
    {
        WriteConfig($$"""
                      { "schemaVersion": 1, "spec": { "source": "{{source}}" },
                        "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                     "framework": "mstest" } }
                      """);

        var config = ConfigLoader.Load(_root);

        config.SpecSource.ShouldBe(source, "the URL reaches the loader exactly as it was written");
        config.SpecSourceIsUrl.ShouldBeTrue(
        "this flag is what routes generate to SpecFetcher and repair to the snapshot");
    }

    /// <summary>
    /// The value that clears <see cref="Spec.SpecLoader.IsUrl"/>'s prefix test and is still not a
    /// URL anyone can fetch. Without this guard it would be handed to
    /// <c>Path.Combine(projectRoot, …)</c> and resurface as the mangled-path defect described on
    /// <see cref="LoadsAUrlSpecSourceAndMarksItAsOne"/> — the refusal that was deleted when URLs
    /// became supported, minus the one case that still needs it.
    /// </summary>
    [TestMethod]
    [DataRow("https://", DisplayName = "scheme only")]
    [DataRow("http://", DisplayName = "scheme only, http")]
    public void RefusesAMalformedUrlSpecSource(string source)
    {
        var reason = ReasonFor($$"""
                                 { "schemaVersion": 1, "spec": { "source": "{{source}}" },
                                   "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                                 """);

        reason.ShouldContain("spec.source", Case.Sensitive);
        reason.ShouldContain(source, Case.Sensitive,
        customMessage: "a refusal quotes what the adopter actually wrote");
        reason.ShouldNotContain("Spec file not found",
        customMessage: "the defect this guard inherited was an accurate sentence about the " +
                       "wrong thing — a malformed URL is not a file that is missing");
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
                        "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                     "framework": "mstest" } }
                      """);

        var config = ConfigLoader.Load(_root);

        config.SpecSource.ShouldBe(source);

        // The assertion that matters more since §9 landed. While a URL was refused, a false
        // positive here produced a confusing error message; now it produces a `generate` that
        // tries to fetch "C:/specs/orders.json" over HTTP and a `fixtures repair` that reads
        // spec.json instead of the adopter's actual spec. Same predicate, far worse failure.
        config.SpecSourceIsUrl.ShouldBeFalse(
        "a path must never be routed down the fetch-and-snapshot path");
    }

    // ---- project -------------------------------------------------------------------------

    /// <summary>The brief's second named defect: <c>KeyNotFoundException</c> through the catch-all.</summary>
    [TestMethod]
    public void ExplainsAMissingProjectSection()
    {
        var reason = ReasonFor("""{ "schemaVersion": 1, "spec": { "source": "orders.json" } }""");

        reason.ShouldContain("project", Case.Sensitive);
        reason.ShouldContain("rootNamespace", Case.Sensitive);
        reason.ShouldContain("testBaseClass", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsAProjectSectionThatIsNotAnObject()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" }, "project": ["Orders.ApiTests"] }
                               """);

        reason.ShouldContain("project", Case.Sensitive);
        reason.ShouldContain("object");
    }

    [TestMethod]
    public void ExplainsAMissingRootNamespace()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                               """);

        reason.ShouldContain("project.rootNamespace", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsAMissingTestBaseClass()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests" } }
                               """);

        reason.ShouldContain("project.testBaseClass", Case.Sensitive);
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

        reason.ShouldContain("project.rootNamespace", Case.Sensitive);
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

        reason.ShouldContain("project.testBaseClass", Case.Sensitive);
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

        reason.ShouldContain("project.rootNamespace", Case.Sensitive);
        reason.ShouldContain("Change project.rootNamespace in intest.json", Case.Sensitive);
        reason.ShouldContain("Orders.ApiTests", Case.Sensitive);
    }

    [TestMethod]
    public void StillRefusesATestBaseClassThatIsNotAValidCSharpName()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.class" } }
                               """);

        reason.ShouldContain("project.testBaseClass", Case.Sensitive);
        reason.ShouldContain("Change project.testBaseClass in intest.json", Case.Sensitive);
    }

    [TestMethod]
    public void StillRefusesARootNamespaceThatIsJsonNull()
    {
        ReasonFor("""
                  { "schemaVersion": 1, "spec": { "source": "orders.json" },
                    "project": { "rootNamespace": null, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                  """).ShouldContain("project.rootNamespace", Case.Sensitive);
    }

    // ---- project.framework -------------------------------------------------------------------
    // Task 9: `project.framework` was always written by `intest init` but never read — decorative
    // JSON. These pin the point where it stops being decorative: required (see
    // ConfigLoader.RequireSupportedFramework's own doc comment for why, unlike intestVersion, it
    // does not get to be optional), and refused with a "not supported yet" message — naming the
    // roadmap (§3), not just rejecting the value — for anything other than the one framework this
    // build actually ships.

    [TestMethod]
    public void ExplainsAMissingFramework()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                               """);

        reason.ShouldContain("project.framework", Case.Sensitive);
    }

    /// <summary>
    /// project.framework's twin of <see cref="ExplainsARootNamespaceThatIsNotAString"/>: the same
    /// <c>RequireString</c> type-check message, reused rather than re-derived, on a setting that
    /// used to be read by nothing at all.
    /// </summary>
    [TestMethod]
    public void ExplainsAFrameworkThatIsNotAString()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": 7 } }
                               """);

        reason.ShouldContain("project.framework", Case.Sensitive);
        reason.ShouldContain("7");
        reason.ShouldContain("string");
    }

    [TestMethod]
    public void ExplainsAFrameworkThatIsJsonNull()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": null } }
                               """);

        reason.ShouldContain("project.framework", Case.Sensitive);
        reason.ShouldContain("null");
    }

    /// <summary>
    /// The message a team hand-editing intest.json toward a real, roadmapped framework should
    /// see: it names what they wrote, names what is actually supported, and reads as "not
    /// supported yet" rather than "invalid" — §3 designs InTest for three frameworks and ships
    /// two (mstest and xunit) so far, and this is the one place that fact has to reach the
    /// adopter directly.
    /// <para>
    /// The exemplar is "nunit", not "xunit" — this test used "xunit" as its unsupported-framework
    /// counter-example until intest actually started accepting it (Task 4 of the xUnit framework
    /// pack plan), at which point "xunit" stopped being an unsupported value and this test would
    /// have gone red for a reason that has nothing to do with what it is pinning. "nunit" is the
    /// one remaining framework §3 designs for but does not yet ship, so it is the exemplar now.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ExplainsAnUnsupportedFrameworkAsNotYetSupportedRatherThanInvalid()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "nunit" } }
                               """);

        reason.ShouldContain("nunit", Case.Sensitive);
        reason.ShouldContain("mstest", Case.Sensitive);
        reason.ShouldContain("not", Case.Sensitive);
        reason.ShouldContain("yet", Case.Sensitive,
        customMessage: "nunit is a real, roadmapped framework (§3) — the message must read as " +
                       "\"not supported yet\", not as a bare validation failure");
    }

    /// <summary>
    /// The same refusal path for a value that names nothing InTest has ever planned to support —
    /// pinned separately from <see cref="ExplainsAnUnsupportedFrameworkAsNotYetSupportedRatherThanInvalid"/>
    /// so that test's "yet" assertion can never be satisfied by accident through a message that
    /// only special-cases the two real framework names.
    /// </summary>
    [TestMethod]
    public void ExplainsAnUnknownFrameworkValue()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "banana" } }
                               """);

        reason.ShouldContain("banana", Case.Sensitive);
        reason.ShouldContain("mstest", Case.Sensitive);
    }

    /// <summary>
    /// Case sensitivity, decided and pinned rather than left implicit: only the exact lowercase
    /// spelling <c>intest init</c> writes is accepted. <c>project.framework</c> is adopter-facing
    /// JSON, not a C# identifier with case-insensitive lookup — <c>rootNamespace</c> and
    /// <c>testBaseClass</c> are both compared exactly as written, and treating "MSTest" as
    /// equivalent to "mstest" would make this the one setting on this surface that tolerates a
    /// spelling <c>init</c> itself never produces.
    /// </summary>
    [TestMethod]
    public void RefusesAnUppercaseSpellingOfTheSupportedFramework()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "MSTest" } }
                               """);

        reason.ShouldContain("MSTest", Case.Sensitive);
        reason.ShouldContain("mstest", Case.Sensitive);
    }

    /// <summary>
    /// [config-opens-by-one-value]: xunit is the second framework value ConfigLoader accepts,
    /// mirroring the InTest.Runtime.xUnit adapter package another task in this plan adds.
    /// </summary>
    [TestMethod]
    public void AcceptsXunitAsAFrameworkValue()
    {
        WriteConfig("""
                    { "schemaVersion": 1, "spec": { "source": "orders.json" },
                      "project": { "rootNamespace": "Orders.ApiTests",
                                   "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                   "framework": "xunit" } }
                    """);

        ConfigLoader.Load(_root).Framework.ShouldBe("xunit");
    }

    /// <summary>
    /// Ordinal-exact lowercase, the same discipline the mstest value has always had: this is
    /// adopter-facing JSON, not a C# identifier with case-insensitive lookup.
    /// </summary>
    [TestMethod]
    public void RefusesAFrameworkValueThatOnlyDiffersInCase()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests",
                                              "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "xUnit" } }
                               """);

        reason.ShouldContain("xUnit", Case.Sensitive);
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

        reason.ShouldContain("schemaVersion", Case.Sensitive);
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

        reason.ShouldContain("schemaVersion", Case.Sensitive);
        reason.ShouldNotContain("upgrade");
    }

    [TestMethod]
    public void ExplainsASchemaVersionThatIsNotAnInteger()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": "1", "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
                               """);

        reason.ShouldContain("schemaVersion", Case.Sensitive);
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

        reason.ShouldContain("schemaVersion", Case.Sensitive);
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

        reason.ShouldContain("schemaVersion", Case.Sensitive);
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
                      "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                   "framework": "mstest" } }
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
                      "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                   "framework": "mstest" } }
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
                      "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                   "framework": "mstest" } }
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

        reason.ShouldContain("intestVersion", Case.Sensitive);
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

        reason.ShouldContain("intestVersion", Case.Sensitive);
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

        reason.ShouldContain("intestVersion", Case.Sensitive);
        reason.ShouldContain("empty");
        reason.ShouldNotContain("not a string");
    }

    // ---- client (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md) --------
    // Optional, absent by default — every fixture above this section declares no `client` at all
    // and must keep loading exactly as before. Once declared, `kind` and `typeName` are both
    // required together, the same "half-finished edit is not a smaller valid config" rule
    // project.framework already established for a required setting on this surface.

    [TestMethod]
    public void LoadsAConfigWithNoClientSectionAsNullClient()
    {
        WriteConfig(Valid);

        ConfigLoader.Load(_root).Client.ShouldBeNull();
    }

    [TestMethod]
    public void LoadsAValidClientSection()
    {
        WriteConfig("""
                    { "schemaVersion": 1, "spec": { "source": "orders.json" },
                      "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                   "framework": "mstest" },
                      "client": { "kind": "kiota", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                    """);

        var client = ConfigLoader.Load(_root).Client;

        client.ShouldNotBeNull();
        client.Kind.ShouldBe("kiota");
        client.TypeName.ShouldBe("Orders.ApiClient.OrdersApiClient");
    }

    [TestMethod]
    [DataRow("nswag")]
    [DataRow("refit")]
    public void LoadsEveryOtherSupportedClientKind(string kind)
    {
        WriteConfig($$"""
                      { "schemaVersion": 1, "spec": { "source": "orders.json" },
                        "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                     "framework": "mstest" },
                        "client": { "kind": "{{kind}}", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                      """);

        ConfigLoader.Load(_root).Client!.Kind.ShouldBe(kind);
    }

    [TestMethod]
    public void ExplainsAClientSectionThatIsNotAnObject()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": "kiota" }
                               """);

        reason.ShouldContain("client", Case.Sensitive);
        reason.ShouldContain("object");
    }

    [TestMethod]
    public void ExplainsAClientSectionMissingKind()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "typeName": "Orders.ApiClient.OrdersApiClient" } }
                               """);

        reason.ShouldContain("client.kind", Case.Sensitive);
        reason.ShouldNotContain("typeName");
    }

    [TestMethod]
    public void ExplainsAClientSectionMissingTypeName()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "kind": "kiota" } }
                               """);

        reason.ShouldContain("client.typeName", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsAnUnsupportedClientKind()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "kind": "swagger-codegen", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                               """);

        reason.ShouldContain("swagger-codegen", Case.Sensitive);
        reason.ShouldContain("kiota", Case.Sensitive);
    }

    /// <summary>
    /// Ordinal-exact, matching <see cref="RefusesAnUppercaseSpellingOfTheSupportedFramework"/>:
    /// adopter-facing JSON, not a case-insensitive C# identifier lookup.
    /// </summary>
    [TestMethod]
    public void RefusesAnUppercaseSpellingOfAClientKind()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "kind": "Kiota", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                               """);

        reason.ShouldContain("Kiota", Case.Sensitive);
        reason.ShouldContain("kiota", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsAClientTypeNameThatIsNotAValidCSharpName()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "kind": "kiota", "typeName": "Orders.ApiClient; public class Injected { } //" } }
                               """);

        reason.ShouldContain("client.typeName", Case.Sensitive);
        reason.ShouldContain("Change client.typeName in intest.json", Case.Sensitive);
    }

    [TestMethod]
    public void ExplainsAClientKindThatIsNotAString()
    {
        var reason = ReasonFor("""
                               { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                 "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase",
                                              "framework": "mstest" },
                                 "client": { "kind": 7, "typeName": "Orders.ApiClient.OrdersApiClient" } }
                               """);

        reason.ShouldContain("client.kind", Case.Sensitive);
        reason.ShouldContain("string");
    }
}
