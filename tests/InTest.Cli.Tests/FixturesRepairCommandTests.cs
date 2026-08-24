using InTest.Cli;
using InTest.Cli.Commands;
using InTest.Cli.Fixtures;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class FixturesRepairCommandTests
{
    private string _root = null!;

    private const string Spec = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/api/products":{"post":{
        "operationId":"createProduct",
        "requestBody":{"content":{"application/json":{"schema":{"type":"object",
          "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-fix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
        InitCommand.Run(_root, "T.ApiTests", "spec.json").ShouldBe(0);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FixturePath => Path.Combine(_root, "fixtures", "createProduct.json");

    [TestMethod]
    public async Task CreatesAMissingFixture()
    {
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.Exists(FixturePath).ShouldBeTrue();
        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["sku"]!.GetValue<string>().ShouldBe("TODO:sku");
    }

    [TestMethod]
    public async Task ReturnsZeroWhenThereIsNothingToRepair()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        // A PR script running repair unconditionally must not fail on a clean tree.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
    }

    [TestMethod]
    public async Task NeverOverwritesAHandWrittenValue()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var document = FixtureDocument.Parse(File.ReadAllText(FixturePath));
        document.Body!["sku"] = "WGT-0001";
        File.WriteAllText(FixturePath, document.ToJson());

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["sku"]!.GetValue<string>()
            .ShouldBe("WGT-0001", "repair adds what is absent; it never replaces what a human wrote");
    }

    [TestMethod]
    public async Task AddsAPropertyThatBecameRequired()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec.Replace(
            """"required":["sku"],"properties":{"sku":{"type":"string"}}"""",
            """"required":["sku","name"],"properties":{"sku":{"type":"string"},"name":{"type":"string"}}""""));

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["name"]!.GetValue<string>().ShouldBe("TODO:name");
    }

    [TestMethod]
    public async Task ReportsAPropertyThatLeftTheSchemaWithoutDeletingIt()
    {
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var document = FixtureDocument.Parse(File.ReadAllText(FixturePath));
        document.Body!["legacyRef"] = "kept-by-hand";
        File.WriteAllText(FixturePath, document.ToJson());

        var report = new StringWriter();
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None, report);

        // §10 requires both halves: not deleted, and reported. Silent retention is how a
        // property nobody meant to keep survives three refactors.
        FixtureDocument.Parse(File.ReadAllText(FixturePath)).Body!["legacyRef"].ShouldNotBeNull(
            "never silently deleted — it may be deliberate");
        report.ToString().ShouldContain("legacyRef", Case.Sensitive);
        report.ToString().ShouldContain("no longer in schema");
    }

    [TestMethod]
    public async Task CreatesFixturesOnlyForOperationsTheTestPlanCovers()
    {
        // TestPlanBuilder already owns "which operations exist", including skips for non-JSON
        // request bodies and operations with no 2xx response. If repair iterated the raw
        // document instead, it would create fixtures for operations no generated test uses,
        // and generate's drift check would disagree with it about the operation set.
        const string withSkipped = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/upload":{"post":{"operationId":"upload",
              "requestBody":{"content":{"multipart/form-data":{"schema":{"type":"object"}}}},
              "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), withSkipped);
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        File.Exists(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "fixtures", "upload.json")).ShouldBeFalse(
            "multipart operations are skipped by the plan, so they get no fixture");
    }

    [TestMethod]
    public async Task NeverWritesOutsideFixtures()
    {
        var before = Directory.GetFiles(_root, "*", SearchOption.TopDirectoryOnly)
                              .ToDictionary(f => f, File.GetLastWriteTimeUtc);

        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        foreach (var (file, written) in before)
        {
            File.GetLastWriteTimeUtc(file).ShouldBe(written, $"{Path.GetFileName(file)} must not be touched");
        }
    }

    [TestMethod]
    public async Task DoesNotCreateFixturesForOperationsThatDoNotNeedOne()
    {
        // FixtureComposer.NeedsFixture is the sole authority on whether an operation gets a
        // fixture. A parameterless GET and a GET whose only parameter is optional with no
        // example or default both compose to an empty body/$parameters — repair must not turn
        // that into a junk fixture file just because the test plan covers the operation.
        const string withNoFixtureNeeded = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/health":{"get":{"operationId":"getHealth",
              "responses":{"200":{"description":"ok"}}}},
            "/api/items":{"get":{"operationId":"listItems",
              "parameters":[{"name":"sort","in":"query","required":false,
                "schema":{"type":"string"}}],
              "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), withNoFixtureNeeded);
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.Exists(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBeTrue(
            "this operation needs a fixture and must still get one");
        File.Exists(Path.Combine(_root, "fixtures", "getHealth.json")).ShouldBeFalse(
            "a parameterless GET needs no fixture — NeedsFixture is false");
        File.Exists(Path.Combine(_root, "fixtures", "listItems.json")).ShouldBeFalse(
            "an all-optional query parameter with no example or default needs no fixture");
    }

    [TestMethod]
    public async Task AppliesLegitimateRepairsEvenWhenAnotherFixtureIsMalformed()
    {
        // Alphabetically, createProduct sorts before createWidget — the loop reaches the
        // corrupted fixture first. One bad committed fixture must not stop repair from adding a
        // sentinel to an unrelated operation that legitimately needs one.
        const string twoOperations = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/api/products":{"post":{"operationId":"createProduct",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/api/widgets":{"post":{"operationId":"createWidget",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object",
                "required":["name"],"properties":{"name":{"type":"string"}}}}}},
              "responses":{"201":{"description":"ok"}}}}}
        }
        """;

        File.WriteAllText(Path.Combine(_root, "spec.json"), twoOperations);
        await FixturesRepairCommand.RunAsync(_root, CancellationToken.None);

        var productPath = Path.Combine(_root, "fixtures", "createProduct.json");
        var widgetPath = Path.Combine(_root, "fixtures", "createWidget.json");
        File.WriteAllText(productPath, "{ not valid json");

        File.WriteAllText(Path.Combine(_root, "spec.json"), twoOperations.Replace(
            """"required":["name"],"properties":{"name":{"type":"string"}}"""",
            """"required":["name","color"],"properties":{"name":{"type":"string"},"color":{"type":"string"}}""""));

        var report = new StringWriter();
        var exitCode = await FixturesRepairCommand.RunAsync(_root, CancellationToken.None, report);

        exitCode.ShouldBe(ExitCode.ToolError,
            "a malformed committed fixture is a real tool error and must be reflected in the exit code");
        FixtureDocument.Parse(File.ReadAllText(widgetPath)).Body!["color"]!.GetValue<string>()
            .ShouldBe("TODO:color", "the unrelated, legitimate repair must still be applied");
        // The report should say which operation's fixture could not be read.
        report.ToString().ShouldContain("createProduct", Case.Sensitive);
    }

    // ---- intest.json is one document, so it is valid or it is not ---------------------------
    // repair reads only spec.source, but it validates the whole file through the same loader
    // `generate` uses. "Valid for repair but not for generate" is a state nobody can reason
    // about: repair succeeds, the adopter believes their config is sound, and they lose that
    // belief one command later. §5's exit 2 — "malformed intest.json" — is a property of the
    // document, not of a command's read set.

    /// <summary>Runs repair with stderr captured, so a test can assert what the adopter is told.</summary>
    private async Task<(int ExitCode, string Error)> RunCapturingErrorAsync(string? projectRoot = null)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            return (await FixturesRepairCommand.RunAsync(projectRoot ?? _root, CancellationToken.None),
                    capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private async Task<string> ExpectExplainedConfigErrorAsync(string json)
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), json);

        var (exitCode, error) = await RunCapturingErrorAsync();

        exitCode.ShouldBe(ExitCode.ToolError);
        Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse(
            "§5 reserves exit 2 for a tool error where nothing was written");
        error.ShouldNotContain("unexpected failure");
        return error;
    }

    /// <summary>
    /// Before ConfigLoader this test failed on the exit code, not the message: repair SUCCEEDED
    /// and wrote fixtures against a config <c>generate</c> refuses. Partial validity was not a
    /// hypothetical the shape permitted — it was a state the tool actively produced, on the most
    /// ordinary hand edit there is. That is why the fix is one loader rather than a matching
    /// guard added here.
    /// </summary>
    [TestMethod]
    public async Task ExplainsAMissingProjectSectionInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync(
            """{ "schemaVersion": 1, "spec": { "source": "spec.json" } }""");

        error.ShouldContain("project", Case.Sensitive);
        error.ShouldNotContain("KeyNotFoundException");
    }

    [TestMethod]
    public async Task ExplainsASpecSourceThatIsNotAStringInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 1, "spec": { "source": 42 },
          "project": { "rootNamespace": "T.ApiTests", "testBaseClass": "T.ApiTests.TTestBase" } }
        """);

        error.ShouldContain("spec.source", Case.Sensitive);
        error.ShouldNotContain("InvalidOperationException");
    }

    /// <summary>
    /// The behaviour change this decision buys, stated as its own test so it cannot regress
    /// silently: repair never renders rootNamespace and does not need it, and refuses anyway.
    /// Nothing is lost by doing so — repair cannot repair intest.json — and the adopter learns
    /// one command earlier about a failure they would hit deterministically on the next
    /// `generate`.
    /// </summary>
    [TestMethod]
    public async Task RefusesAnInvalidRootNamespaceEvenThoughItNeverRendersOne()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 1, "spec": { "source": "spec.json" },
          "project": { "rootNamespace": "T.ApiTests; class Injected { } //",
                       "testBaseClass": "T.ApiTests.TTestBase" } }
        """);

        error.ShouldContain("project.rootNamespace", Case.Sensitive);
    }

    [TestMethod]
    public async Task RefusesASchemaVersionThisCliDoesNotImplement()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 2, "spec": { "source": "spec.json" },
          "project": { "rootNamespace": "T.ApiTests", "testBaseClass": "T.ApiTests.TTestBase" } }
        """);

        error.ShouldContain("schemaVersion", Case.Sensitive);
        error.ShouldNotContain("upgrade");
    }

    /// <summary>
    /// The config `intest init` writes must load unchanged. This is the guard that a validation
    /// rule added here can never contradict the scaffold — the two would otherwise drift, and a
    /// fresh project that cannot run `repair` is the worst possible first experience.
    /// </summary>
    [TestMethod]
    public async Task AcceptsTheConfigThatInitWrites()
    {
        // CreateProject already ran `init`; nothing here edits intest.json.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
    }

    /// <summary>
    /// `--project` is an argument the adopter typed, not a crash. It reached ConfigLoader.Load's
    /// ArgumentException.ThrowIfNullOrWhiteSpace and came back as
    /// "intest: unexpected failure: ArgumentException: ... (Parameter 'projectRoot')" — the right
    /// exit code attached to the wrong sentence, naming a C# parameter the adopter never wrote
    /// instead of the flag they did. `init` had the same rule stated a third way; there is one
    /// now, in CommandArguments, and this is fixtures repair's call site of it.
    /// </summary>
    [TestMethod]
    public async Task RefusesABlankProjectRatherThanReportingItAsACrash()
    {
        var (exitCode, error) = await RunCapturingErrorAsync(projectRoot: "");

        exitCode.ShouldBe(ExitCode.ToolError);
        error.ShouldNotContain("unexpected failure",
            customMessage: "an argument the adopter got wrong is refused, not reported as a crash");
        error.ShouldStartWith("--project", Case.Sensitive,
            customMessage: "a refusal names the flag the adopter typed, not the parameter it bound to");
        error.ShouldContain("is empty");
        error.ShouldContain("for example");
    }

    // ---- a URL spec.source ---------------------------------------------------------------------

    /// <summary>
    /// Repoints this project's config at a URL, leaving everything else `init` scaffolded intact.
    /// The snapshot, if the test wants one, is written separately — which is the whole point:
    /// repair never fetches, so the only way it can see a spec is if `generate` already put one
    /// there.
    /// </summary>
    private void UseUrlSpecSource() => File.WriteAllText(Path.Combine(_root, "intest.json"), """
    { "schemaVersion": 1, "spec": { "source": "https://orders-staging.example.com/swagger/v1/swagger.json" },
      "project": { "rootNamespace": "T.ApiTests", "testBaseClass": "T.ApiTests.TTestBase" } }
    """);

    /// <summary>
    /// [no-refetch]: repair reads the committed snapshot `generate` took, and opens no socket to
    /// do it. Deciding what the spec now says is `generate`'s job, deliberately, on a branch,
    /// where the resulting spec.json diff is reviewable — and if repair fetched too, the two
    /// commands could plan against different upstream revisions, producing drift repair cannot
    /// fix.
    /// <para>
    /// This project's snapshot happens to already sit at spec.json, because that is the file name
    /// §9 fixes and <see cref="SpecSnapshot.FileName"/> names. What changes here is only which
    /// setting sends repair to it.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ReadsTheSnapshotForAUrlSpecSource()
    {
        UseUrlSpecSource();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(ExitCode.Ok);

        File.Exists(FixturePath).ShouldBeTrue(
            "the snapshot is where a URL-sourced project's spec lives");
    }

    /// <summary>
    /// The state an adopter reaches by running `fixtures repair` before `generate` on a fresh
    /// URL project. Letting <c>SpecLoader.LoadFromFileAsync</c> report this would say
    /// "Spec file not found: &lt;projectRoot&gt;/spec.json" — naming a file the adopter never
    /// wrote, never chose the name of, and cannot usefully create by hand. That is the same
    /// defect class the pre-§9 URL refusal existed to fix (an accurate sentence about the wrong
    /// thing), and this test is what stops it being reintroduced one file over.
    /// </summary>
    [TestMethod]
    public async Task ExplainsAMissingSnapshotRatherThanReportingAFileTheAdopterNeverNamed()
    {
        UseUrlSpecSource();
        File.Delete(Path.Combine(_root, SpecSnapshot.FileName));

        var (exitCode, error) = await RunCapturingErrorAsync();

        exitCode.ShouldBe(ExitCode.ToolError);
        error.ShouldContain("intest generate",
            customMessage: "the remedy is the command that takes the snapshot");
        error.ShouldNotContain("Spec file not found",
            customMessage: "an accurate sentence about the wrong thing sends the adopter hunting " +
                           "for a file that was never theirs to create");
        error.ShouldNotContain("unexpected failure");
    }
}
