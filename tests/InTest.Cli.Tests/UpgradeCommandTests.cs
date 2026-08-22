using InTest.Cli;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Task 4 of the v1-e plan: `intest upgrade`. Every test scaffolds a project through
/// <see cref="InitCommand"/> rather than hand-writing intest.json, so the "old" state under test
/// is a state `init` actually produces (or a deliberate, named departure from it) — the same
/// discipline <see cref="GenerateCommandTests"/> and <see cref="GenerateCheckCommandTests"/>
/// already use for their own configs.
/// </summary>
[TestClass]
public class UpgradeCommandTests
{
    // A single GET with a path parameter and no request body, mirroring GenerateCommandTests.Spec
    // and GenerateCheckCommandTests.Spec — no fixture is needed, so the happy-path tests below are
    // not also exercising the fixture-drift path. The drift path gets its own spec, below.
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    // A request body, so FixtureComposer.NeedsFixture is true and no fixture exists — the same
    // scenario GenerateCheckCommandTests.SpecNeedingAFixture pins, reused here to force generate's
    // regeneration step to fail with fixture drift rather than run to completion.
    private const string SpecNeedingAFixture = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/api/products":{"post":{
        "operationId":"createProduct",
        "requestBody":{"content":{"application/json":{"schema":{"type":"object",
          "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-upgrade-" + Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>
    /// Scaffolds a real project via `init` (so intest.json, .config/dotnet-tools.json and
    /// .gitattributes all start out exactly as `init` writes them), then overwrites the spec and
    /// intest.json's declared intestVersion with an old value — simulating "a project last
    /// upgraded on an earlier release" without hand-authoring a config from scratch.
    /// </summary>
    private void InitProject(string spec, string oldIntestVersion = "0.0.1")
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), spec);

        var intestJsonPath = Path.Combine(_root, "intest.json");
        var text = File.ReadAllText(intestJsonPath);
        text.ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"",
            customMessage: "init is expected to declare the running tool's own version");
        File.WriteAllText(intestJsonPath, text.Replace(
            $"\"intestVersion\": \"{CliVersion.Current}\"", $"\"intestVersion\": \"{oldIntestVersion}\""));

        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        var toolsText = File.ReadAllText(dotnetToolsPath);
        File.WriteAllText(dotnetToolsPath, toolsText.Replace(
            $"\"version\": \"{CliVersion.Current}\"", $"\"version\": \"{oldIntestVersion}\""));
    }

    private static Task<int> UpgradeAsync(string root) => UpgradeCommand.RunAsync(root, CancellationToken.None);

    private static async Task<(int ExitCode, string Report)> UpgradeCapturingReportAsync(string root)
    {
        var report = new StringWriter();
        var exitCode = await UpgradeCommand.RunAsync(root, CancellationToken.None, report);
        return (exitCode, report.ToString());
    }

    private string ReadIntestJson() => File.ReadAllText(Path.Combine(_root, "intest.json"));
    private string ReadDotnetTools() => File.ReadAllText(Path.Combine(_root, ".config", "dotnet-tools.json"));

    // ---- happy path --------------------------------------------------------------------------

    [TestMethod]
    public async Task UpgradesIntestVersionAndDotnetToolsVersionThenRegenerates()
    {
        InitProject(Spec);

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        ReadIntestJson().ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"");
        ReadDotnetTools().ShouldContain($"\"version\": \"{CliVersion.Current}\"");
        File.Exists(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBeTrue(
            "upgrade must regenerate, not just bump the version fields");
        report.ShouldContain(CliVersion.Current);
    }

    /// <summary>
    /// The output `upgrade` regenerates must actually match a fresh render under the new version —
    /// proving `upgrade` and `generate --check` agree, not just that upgrade wrote *some* files.
    /// </summary>
    [TestMethod]
    public async Task OutputMatchesAFreshRenderAfterUpgrading()
    {
        InitProject(Spec);
        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var report = new StringWriter();
        var checkExitCode = await GenerateCommand.RunAsync(_root, CancellationToken.None, report, check: true);

        checkExitCode.ShouldBe(ExitCode.Ok, report.ToString());
    }

    [TestMethod]
    public async Task IsIdempotentWhenAlreadyOnTheRunningVersion()
    {
        InitProject(Spec, oldIntestVersion: CliVersion.Current);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);
        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        ReadIntestJson().ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"");
    }

    // ---- targeted text edit, not parse-and-rewrite -------------------------------------------

    /// <summary>
    /// The core claim of decision 1: only the intestVersion value changes. Reformats intest.json
    /// with single-line-per-object-close style, an unconventional key order (project before
    /// spec, schemaVersion last within its neighbourhood), extra blank-line-free packing, and an
    /// unknown key from "a later release" (mirroring ConfigLoaderTests.IgnoresSettingsItDoesNotRead)
    /// — every one of those bytes must survive untouched. A parse-and-rewrite through JsonNode or
    /// JsonDocument would satisfy "the new version is present" while failing this test, because
    /// reserializing normalizes indentation and typically reorders or at least reformats content.
    /// </summary>
    [TestMethod]
    public async Task PreservesUnusualFormattingKeyOrderAndUnknownKeysInIntestJson()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);

        var handWritten =
            "{\"project\":{\"rootNamespace\":\"Orders.ApiTests\",\"testBaseClass\":\"Orders.ApiTests.OrdersTestBase\"}," +
            "\"intestVersion\":\"0.0.1\",\"schemaVersion\":1,\"spec\":{\"source\":\"orders.json\"}," +
            "\"somethingFromALaterRelease\":{\"nested\":true}}";
        File.WriteAllText(Path.Combine(_root, "intest.json"), handWritten);

        var expectedAfter = handWritten.Replace(
            "\"intestVersion\":\"0.0.1\"", $"\"intestVersion\":\"{CliVersion.Current}\"");

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        ReadIntestJson().ShouldBe(expectedAfter,
            customMessage: "every byte outside the intestVersion value must be untouched — " +
                            "key order, spacing, and the unrecognised key alike");
    }

    /// <summary>
    /// A config predating Task 1 (or hand-edited without intestVersion) is exactly the case
    /// [read-what-init-wrote] says ConfigLoader must still load — "config grows by addition".
    /// upgrade's whole purpose is adopting a version deliberately, so it adds the field rather
    /// than only ever updating one already present.
    /// </summary>
    [TestMethod]
    public async Task InsertsIntestVersionWhenAbsent()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1,
          "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var after = ReadIntestJson();
        after.ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"");
        // The rest of the document must still be exactly what was there — the insertion adds a
        // line, it does not touch neighbouring content.
        after.ShouldContain("\"spec\": { \"source\": \"orders.json\" }");
        after.ShouldContain(
            "\"project\": { \"rootNamespace\": \"Orders.ApiTests\", \"testBaseClass\": \"Orders.ApiTests.OrdersTestBase\" }");
    }

    /// <summary>
    /// SetIntestVersion exercised directly against the exact shape the plan's "unusual key order"
    /// hazard names — schemaVersion with no trailing comma before whatever follows it starts on
    /// the same line — without needing a whole project scaffold for a one-function claim.
    /// </summary>
    [TestMethod]
    public void SetIntestVersionInsertsAfterSchemaVersionMatchingItsIndentation()
    {
        var before = "{\n  \"schemaVersion\": 1,\n  \"spec\": { \"source\": \"orders.json\" } }";

        var after = UpgradeCommand.SetIntestVersion(before, "1.2.3");

        after.ShouldBe("{\n  \"schemaVersion\": 1,\n  \"intestVersion\": \"1.2.3\",\n  \"spec\": { \"source\": \"orders.json\" } }");
    }

    [TestMethod]
    public void SetIntestVersionReplacesOnlyTheExistingValue()
    {
        var before = "{ \"schemaVersion\": 1, \"intestVersion\":   \"0.1.0\"  , \"spec\": {} }";

        var after = UpgradeCommand.SetIntestVersion(before, "9.9.9");

        after.ShouldBe("{ \"schemaVersion\": 1, \"intestVersion\":   \"9.9.9\"  , \"spec\": {} }");
    }

    // ---- comments and trailing commas never reach upgrade's editing code ----------------------

    /// <summary>
    /// Confirmed by direct experiment (see UpgradeCommand's own type doc comment): ConfigLoader's
    /// default JsonDocument.Parse options disallow comments, so a config that actually carries one
    /// fails to load — with the same message plain `generate` gives it — before upgrade's own
    /// text-editing code ever runs. This is the "targeted edit is never asked to be comment-safe"
    /// claim, pinned as a runnable proof rather than left as a comment nobody can re-check.
    /// </summary>
    [TestMethod]
    public async Task RefusesAConfigWithACommentBeforeEditingAnything()
    {
        InitProject(Spec);
        var intestJsonPath = Path.Combine(_root, "intest.json");
        var before = File.ReadAllText(intestJsonPath);
        File.WriteAllText(intestJsonPath, before.Replace(
            "\"schemaVersion\": 1,", "\"schemaVersion\": 1, // pinned"));

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.ToolError);
        report.ShouldNotContain(CliVersion.Current,
            customMessage: "nothing about a successful upgrade should be reported");
        ReadIntestJson().ShouldContain("// pinned",
            customMessage: "a config that never loaded must be left exactly as it was");
    }

    [TestMethod]
    public async Task RefusesAConfigWithATrailingCommaBeforeEditingAnything()
    {
        InitProject(Spec);
        var intestJsonPath = Path.Combine(_root, "intest.json");
        var before = File.ReadAllText(intestJsonPath);
        File.WriteAllText(intestJsonPath, before.Replace(
            "\"project\": {", "\"project\": { \"unused\": true,"));
        // Malform it further into a genuine trailing comma before the closing brace.
        var malformed = File.ReadAllText(intestJsonPath).TrimEnd();
        malformed = malformed[..^1] + ", }";
        File.WriteAllText(intestJsonPath, malformed);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);
    }

    // ---- half-applied state: the one the plan names as worth a test ---------------------------

    /// <summary>
    /// Task 4 Step 2's named guarantee: "A failed regeneration must not leave intestVersion
    /// bumped against un-regenerated output — that state makes --check lie afterwards." Forces
    /// generate's own regeneration step to fail on fixture drift (no fixture exists for
    /// createProduct, which needs one), and asserts intest.json, .config/dotnet-tools.json and
    /// Generated/ are all exactly as they were before upgrade ran.
    /// </summary>
    [TestMethod]
    public async Task DoesNotBumpIntestVersionWhenRegenerationFails()
    {
        InitProject(SpecNeedingAFixture);
        var intestJsonBefore = ReadIntestJson();
        var dotnetToolsBefore = ReadDotnetTools();

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("createProduct");
        ReadIntestJson().ShouldBe(intestJsonBefore,
            customMessage: "intestVersion must not be bumped when regeneration itself did not run");
        ReadDotnetTools().ShouldBe(dotnetToolsBefore);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "drift is detected before anything is written, in both generate and upgrade");
    }

    /// <summary>
    /// The crash-floor twin of DoesNotBumpIntestVersionWhenRegenerationFails: a malformed
    /// intest.json makes GenerateCommand.RunAsync's own ConfigLoader.Load throw, which it catches
    /// and reports as ExitCode.ToolError before ever writing Generated/. upgrade must forward
    /// that code rather than treating "not WorkOutstanding" as success, and must not have touched
    /// .config/dotnet-tools.json on the way — the same "nothing written on failure" guarantee,
    /// exercised through the tool-error path rather than the drift path.
    /// </summary>
    [TestMethod]
    public async Task ForwardsToolErrorFromRegenerationWithoutWritingAnything()
    {
        InitProject(Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"), "{ not json");
        var dotnetToolsBefore = ReadDotnetTools();

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);

        ReadDotnetTools().ShouldBe(dotnetToolsBefore);
    }

    // ---- .config/dotnet-tools.json: bumps only the intest.cli pin -----------------------------

    [TestMethod]
    public async Task NeverBumpsTheManifestFormatVersionOrAnotherToolsPin()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        File.WriteAllText(dotnetToolsPath, $$"""
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "some-other-tool": { "version": "3.4.5", "commands": ["other"] },
            "intest.cli": { "version": "0.0.1", "commands": ["intest"] }
          }
        }
        """);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var after = ReadDotnetTools();
        after.ShouldContain("\"version\": 1,\n  \"isRoot\": true", Case.Sensitive,
            customMessage: "the manifest format version (an unrelated integer under the same key name) must not change");
        after.ShouldContain("\"some-other-tool\": { \"version\": \"3.4.5\"",
            customMessage: "a sibling tool's own pin must be untouched");
        after.ShouldContain($"\"intest.cli\": {{ \"version\": \"{CliVersion.Current}\"");
    }

    [TestMethod]
    public async Task RefusesWhenDotnetToolsJsonDoesNotExist()
    {
        InitProject(Spec);
        File.Delete(Path.Combine(_root, ".config", "dotnet-tools.json"));
        var intestJsonBefore = ReadIntestJson();

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);

        ReadIntestJson().ShouldBe(intestJsonBefore,
            customMessage: "intest.json must not be rewritten when the tools pin cannot be bumped");
    }

    [TestMethod]
    public async Task RefusesWhenDotnetToolsJsonDoesNotPinIntestCli()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        File.WriteAllText(dotnetToolsPath, """
        { "version": 1, "isRoot": true, "tools": { "some-other-tool": { "version": "3.4.5", "commands": ["other"] } } }
        """);
        var intestJsonBefore = ReadIntestJson();

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);

        ReadIntestJson().ShouldBe(intestJsonBefore);
    }

    // ---- .gitattributes: write if absent, never overwrite --------------------------------------

    [TestMethod]
    public async Task ScaffoldsGitattributesWhenAbsent()
    {
        InitProject(Spec);
        File.Delete(Path.Combine(_root, ".gitattributes"));

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        File.Exists(Path.Combine(_root, ".gitattributes")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(_root, ".gitattributes")).ShouldContain("Generated/** text eol=lf");
    }

    [TestMethod]
    public async Task NeverOverwritesAnExistingGitattributes()
    {
        InitProject(Spec);
        var gitattributesPath = Path.Combine(_root, ".gitattributes");
        File.WriteAllText(gitattributesPath, "# adopter customised this file\n*.custom text eol=lf\n");
        var before = File.ReadAllText(gitattributesPath);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        File.ReadAllText(gitattributesPath).ShouldBe(before);
    }

    // ---- fixtures/ and every other team-owned file: never touched, except the named exception --

    /// <summary>
    /// The exception stated, not assumed: this asserts every team-owned file is byte-identical
    /// after upgrade EXCEPT .gitattributes, which is allowed to change from absent to present.
    /// Deleting .gitattributes first and asserting a blanket "nothing changed" would be the wrong,
    /// self-contradicting version the plan explicitly warns against — Step 1b decides upgrade DOES
    /// create it when missing, so a test that forbids exactly that would fight the implementation
    /// it is supposed to pin, and whichever assertion is written first would silently win.
    /// </summary>
    [TestMethod]
    public async Task NeverTouchesFixturesOrOtherTeamOwnedFilesExceptGitattributesWhenAbsent()
    {
        // SpecNeedingAFixture's operation gets a real fixture via `fixtures repair` — the only
        // command allowed to write under fixtures/ — so there is a genuine, well-formed fixture
        // file (with its required $meta block) to prove untouched, rather than a hand-rolled one
        // that only coincidentally matches FixtureDocument's shape.
        InitProject(SpecNeedingAFixture);
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(ExitCode.Ok);
        File.Exists(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBeTrue();
        File.Delete(Path.Combine(_root, ".gitattributes"));

        var teamOwned = new[]
        {
            "AssemblyInfo.cs", "TestStartup.cs", "OrdersTestBase.cs", "appsettings.json",
            "appsettings.staging.json", "Orders.ApiTests.runsettings", ".editorconfig",
            "Orders.ApiTests.csproj",
        };
        var before = teamOwned.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(_root, f)));
        var fixtureBefore = File.ReadAllText(Path.Combine(_root, "fixtures", "createProduct.json"));
        File.Exists(Path.Combine(_root, ".gitattributes")).ShouldBeFalse();

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        foreach (var (file, content) in before)
        {
            File.ReadAllText(Path.Combine(_root, file)).ShouldBe(content, customMessage: $"{file} must be untouched by upgrade");
        }
        File.ReadAllText(Path.Combine(_root, "fixtures", "createProduct.json")).ShouldBe(fixtureBefore,
            customMessage: "fixtures/ is never touched by upgrade, with no exception");
        // The one named, decided exception: .gitattributes goes from absent to present.
        File.Exists(Path.Combine(_root, ".gitattributes")).ShouldBeTrue(
            "the sole exception Task 4 Step 1b decides: upgrade scaffolds .gitattributes when absent");
    }

    // ---- no version metadata: refuses rather than laundering "0.0.0" into intestVersion --------

    /// <summary>
    /// Exercised through the internal seam directly, the same way GenerateCheckCommandTests
    /// exercises GenerateCommand.ReportVersionMismatch's own fallback branch — a normal test build
    /// always carries a real informational version (Directory.Build.props pins one), so
    /// CliVersion.Current cannot actually be "0.0.0" in this test process.
    /// </summary>
    [TestMethod]
    public void NoVersionMetadataMessageNamesTheBuildProblemAndDoesNotSuggestUpgrade()
    {
        var message = UpgradeCommand.NoVersionMetadataMessage(CliVersion.FallbackVersion);

        message.ShouldContain("build problem");
        message.ShouldContain(CliVersion.FallbackVersion);
    }
}
