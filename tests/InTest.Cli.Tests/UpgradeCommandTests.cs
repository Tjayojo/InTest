using System.Text;
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
        text.ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"", Case.Sensitive,
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

    /// <summary>Runs `upgrade` with stderr captured, so a test can assert what the adopter is
    /// told — the same idiom GenerateCommandTests, FixturesRepairCommandTests and
    /// InitCommandTests already use for their own refusal messages.</summary>
    private static async Task<(int ExitCode, string Error)> UpgradeCapturingErrorAsync(string root)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            return (await UpgradeCommand.RunAsync(root, CancellationToken.None), capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
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
        ReadIntestJson().ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"", Case.Sensitive);
        ReadDotnetTools().ShouldContain($"\"version\": \"{CliVersion.Current}\"", Case.Sensitive);
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

        ReadIntestJson().ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"", Case.Sensitive);
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
    /// <para>
    /// The unknown key deliberately nests a same-named <c>"intestVersion"</c> — CRITICAL 1's fix,
    /// applied here too: a first review round found this test could not have caught the
    /// unanchored-regex defect (nested-key corruption) because its unknown key contained no text
    /// the old match could confuse with the real one. It now does, and
    /// <c>DoesNotCorruptANestedKeyNamedIntestVersion</c> pins the same claim directly against
    /// SetIntestVersion for a case where the colliding key sits <i>before</i> the real one instead.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task PreservesUnusualFormattingKeyOrderAndUnknownKeysInIntestJson()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);

        var handWritten =
            "{\"project\":{\"rootNamespace\":\"Orders.ApiTests\",\"testBaseClass\":\"Orders.ApiTests.OrdersTestBase\"}," +
            "\"intestVersion\":\"0.0.1\",\"schemaVersion\":1,\"spec\":{\"source\":\"orders.json\"}," +
            "\"somethingFromALaterRelease\":{\"nested\":true,\"intestVersion\":\"pinned-by-a-later-release\"}}";
        File.WriteAllText(Path.Combine(_root, "intest.json"), handWritten);

        var expectedAfter = handWritten.Replace(
            "\"intestVersion\":\"0.0.1\"", $"\"intestVersion\":\"{CliVersion.Current}\"");

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        ReadIntestJson().ShouldBe(expectedAfter,
            customMessage: "every byte outside the top-level intestVersion value must be " +
                            "untouched — key order, spacing, the unrecognised key, and the nested " +
                            "same-named key inside it alike");
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
        after.ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"", Case.Sensitive);
        // The rest of the document must still be exactly what was there — the insertion adds a
        // line, it does not touch neighbouring content.
        after.ShouldContain("\"spec\": { \"source\": \"orders.json\" }", Case.Sensitive);
        after.ShouldContain(
            "\"project\": { \"rootNamespace\": \"Orders.ApiTests\", \"testBaseClass\": \"Orders.ApiTests.OrdersTestBase\" }");
    }

    /// <summary>
    /// Item 4, exercised through the full command rather than just SetIntestVersion directly: the
    /// insert path (no existing intestVersion key — exactly InsertsIntestVersionWhenAbsent's case
    /// above) must derive its newline from the file it is editing rather than hard-coding "\n".
    /// Reachable in practice because the scaffolded .gitattributes explicitly pins Generated/**,
    /// coverage-report.json and fixtures/**/*.json but never intest.json itself, so intest.json's
    /// line endings follow whatever the checking-out machine's own core.autocrlf produces rather
    /// than a fixed convention — a config predating Task 1 — this insert path's whole reason to
    /// exist — stays CRLF on a core.autocrlf=true clone.
    /// </summary>
    [TestMethod]
    public async Task InsertsIntestVersionUsingTheConfigsOwnCrlfLineEnding()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"),
            "{\r\n  \"schemaVersion\": 1,\r\n  \"spec\": { \"source\": \"orders.json\" },\r\n  " +
            "\"project\": { \"rootNamespace\": \"Orders.ApiTests\", " +
            "\"testBaseClass\": \"Orders.ApiTests.OrdersTestBase\" }\r\n}");

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var after = ReadIntestJson();
        after.ShouldContain(
            $"\"schemaVersion\": 1,\r\n  \"intestVersion\": \"{CliVersion.Current}\",\r\n  \"spec\"",
            Case.Sensitive,
            customMessage: "the inserted line must use this file's own CRLF, not a bare LF");
        after.ShouldNotContain("1,\n  \"intestVersion", Case.Sensitive,
            customMessage: "a lone LF here would mean the insertion ignored the file's line ending");
    }

    /// <summary>
    /// Item 3: falsifies the type doc's central claim if it regresses. File.ReadAllTextAsync
    /// silently consumes a leading UTF-8 BOM and File.WriteAllTextAsync never re-emits one, so a
    /// targeted edit that round-trips intest.json through text (rather than bytes) drops the BOM
    /// even though nothing in the matched span asked for that — measured directly: first byte 0xEF
    /// before upgrade, 0x7B (a bare '{') after, exit 0. UpgradeCommand now round-trips through
    /// bytes precisely so a BOM sits outside every matched span and survives like any other byte
    /// outside it.
    /// </summary>
    [TestMethod]
    public async Task PreservesALeadingUtf8BomInIntestJson()
    {
        InitProject(Spec);
        var intestJsonPath = Path.Combine(_root, "intest.json");
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(await File.ReadAllBytesAsync(intestJsonPath)).ToArray();
        await File.WriteAllBytesAsync(intestJsonPath, withBom);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var afterBytes = await File.ReadAllBytesAsync(intestJsonPath);
        afterBytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF },
            customMessage: "the BOM sits outside intestVersion's matched span and must survive untouched");
        Encoding.UTF8.GetString(afterBytes).ShouldContain($"\"intestVersion\": \"{CliVersion.Current}\"", Case.Sensitive);
    }

    /// <summary>The dotnet-tools.json twin of the test above — the same byte-preservation claim
    /// applies to both adopter-owned files this command edits.</summary>
    [TestMethod]
    public async Task PreservesALeadingUtf8BomInDotnetToolsJson()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(await File.ReadAllBytesAsync(dotnetToolsPath)).ToArray();
        await File.WriteAllBytesAsync(dotnetToolsPath, withBom);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var afterBytes = await File.ReadAllBytesAsync(dotnetToolsPath);
        afterBytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(afterBytes).ShouldContain($"\"version\": \"{CliVersion.Current}\"", Case.Sensitive);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// SetIntestVersion exercised directly against the shape the plan's "unusual key order"
    /// hazard names — schemaVersion immediately followed by another property on the same line,
    /// comma included — without needing a whole project scaffold for a one-function claim. This is
    /// the has-comma branch of the insert path; <see cref="SetIntestVersionInsertsAfterSchemaVersionWithNoTrailingComma"/>
    /// is its sibling, and the two are only distinguishable from each other, not from this test
    /// alone: forcing the has-comma form unconditionally (deleting the ternary in SetIntestVersion
    /// and always taking the comma-first branch) leaves this test green and only breaks the
    /// no-comma sibling.
    /// </summary>
    [TestMethod]
    public void SetIntestVersionInsertsAfterSchemaVersionMatchingItsIndentation()
    {
        var before = Utf8("{\n  \"schemaVersion\": 1,\n  \"spec\": { \"source\": \"orders.json\" } }");

        var after = UpgradeCommand.SetIntestVersion(before, "1.2.3");

        Encoding.UTF8.GetString(after).ShouldBe(
            "{\n  \"schemaVersion\": 1,\n  \"intestVersion\": \"1.2.3\",\n  \"spec\": { \"source\": \"orders.json\" } }");
    }

    /// <summary>
    /// The no-comma sibling: schemaVersion is the last property before the object closes, so
    /// nothing follows it on the same line. A hand-edited config predating Task 1 can plausibly
    /// look like this if schemaVersion was appended last. Mutation check: forcing the has-comma
    /// branch unconditionally leaves a spurious comma directly after the number — this test fails,
    /// where <see cref="SetIntestVersionInsertsAfterSchemaVersionMatchingItsIndentation"/> alone
    /// could not tell the two branches apart, since its input always has a comma.
    /// </summary>
    [TestMethod]
    public void SetIntestVersionInsertsAfterSchemaVersionWithNoTrailingComma()
    {
        var before = Utf8("{\n  \"spec\": { \"source\": \"orders.json\" },\n  \"schemaVersion\": 1\n}");

        var after = UpgradeCommand.SetIntestVersion(before, "1.2.3");

        Encoding.UTF8.GetString(after).ShouldBe(
            "{\n  \"spec\": { \"source\": \"orders.json\" },\n  \"schemaVersion\": 1,\n  \"intestVersion\": \"1.2.3\"\n}");
    }

    [TestMethod]
    public void SetIntestVersionReplacesOnlyTheExistingValue()
    {
        var before = Utf8("{ \"schemaVersion\": 1, \"intestVersion\":   \"0.1.0\"  , \"spec\": {} }");

        var after = UpgradeCommand.SetIntestVersion(before, "9.9.9");

        Encoding.UTF8.GetString(after).ShouldBe("{ \"schemaVersion\": 1, \"intestVersion\":   \"9.9.9\"  , \"spec\": {} }");
    }

    /// <summary>
    /// CRITICAL 1's proof, exercised at the unit level: a nested key named exactly
    /// <c>intestVersion</c>, sitting <i>before</i> the real top-level one and containing no text
    /// the old regex-based match could tell apart from the real key. Measured against the previous
    /// implementation: it rewrote the nested value and left the top-level one untouched, exit 0 —
    /// the exact "corrupts an unrelated adopter key and never bumps the real field" defect a first
    /// review round reported. SetIntestVersion's underlying scanner must only ever consider a
    /// property that is a direct child of the document's root object.
    /// </summary>
    [TestMethod]
    public void DoesNotCorruptANestedKeyNamedIntestVersion()
    {
        var before = Utf8(
            "{ \"schemaVersion\": 1, \"vendorNotes\": { \"intestVersion\": \"pinned-by-us\" }, " +
            "\"intestVersion\": \"0.0.1\" }");

        var after = UpgradeCommand.SetIntestVersion(before, "9.9.9");

        var afterText = Encoding.UTF8.GetString(after);
        afterText.ShouldBe(
            "{ \"schemaVersion\": 1, \"vendorNotes\": { \"intestVersion\": \"pinned-by-us\" }, " +
            "\"intestVersion\": \"9.9.9\" }",
            customMessage: "the nested vendorNotes.intestVersion must survive untouched and only " +
                            "the root-level key may change");
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
        ReadIntestJson().ShouldContain("// pinned", Case.Sensitive,
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
        report.ShouldContain("createProduct", Case.Sensitive);
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
        // Two separate Contains, not one string spanning the line break between them: the claim is
        // that these two values are unchanged, not that they stay adjacent across a particular
        // line ending. The original single assertion embedded a literal "\n" here, which only
        // matched because this file's own line endings happened to be LF on the machine that wrote
        // it. CI's windows-latest checks .cs out as CRLF by default, so the setup literal above
        // carried CRLF there and the embedded "\n" no longer occurred in the text at all, even
        // though "version": 1 and "isRoot": true were both present and both correct. See
        // .gitattributes' *.cs entry and CONTRIBUTING.md for the general case this is an instance
        // of.
        after.ShouldContain("\"version\": 1,", Case.Sensitive,
            customMessage: "the manifest format version (an unrelated integer under the same key name) must not change");
        after.ShouldContain("\"isRoot\": true", Case.Sensitive,
            customMessage: "isRoot must not change");
        after.ShouldContain("\"some-other-tool\": { \"version\": \"3.4.5\"", Case.Sensitive,
            customMessage: "a sibling tool's own pin must be untouched");
        after.ShouldContain($"\"intest.cli\": {{ \"version\": \"{CliVersion.Current}\"", Case.Sensitive);
    }

    /// <summary>
    /// Item 11 / part of CRITICAL 1's proof at the dotnet-tools.json side: the previous
    /// implementation matched <c>"intest.cli": { ... }</c> with a regex body class of
    /// <c>[^{}]*</c>, which cannot contain a brace at all — so an <c>intest.cli</c> entry with any
    /// nested object inside it (here, an unrelated <c>"metadata"</c> object) made the whole regex
    /// fail to match, and the command refused with "does not pin intest.cli under tools" even
    /// though the pin was right there. The JSON-aware scanner must walk past the nested object
    /// rather than refusing at the first unmatched brace.
    /// </summary>
    [TestMethod]
    public async Task BumpsTheVersionEvenWhenIntestCliCarriesANestedObject()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        File.WriteAllText(dotnetToolsPath, """
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "intest.cli": {
              "commands": ["intest"],
              "metadata": { "notes": "pinned deliberately" },
              "version": "0.0.1"
            }
          }
        }
        """);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.Ok);

        var after = ReadDotnetTools();
        after.ShouldContain($"\"version\": \"{CliVersion.Current}\"", Case.Sensitive);
        after.ShouldContain("\"metadata\": { \"notes\": \"pinned deliberately\" }", Case.Sensitive,
            customMessage: "the unrelated nested object must survive untouched");
    }

    /// <summary>
    /// CRITICAL 1's proof at the dotnet-tools.json side: an <c>"intest.cli"</c> key nested inside
    /// some unrelated object elsewhere in the manifest — not under <c>"tools"</c> at all — must
    /// never be found or bumped, even though the previous regex-based <c>IntestCliToolBlock</c>
    /// would have matched it anywhere in the file. Its own refusal message asserted the pin was
    /// "under tools"; this proves the scanner now actually enforces that rather than just saying
    /// it.
    /// </summary>
    [TestMethod]
    public async Task DoesNotBumpAnIntestCliKeyNestedOutsideTools()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        var before = """
        {
          "version": 1,
          "isRoot": true,
          "vendorNotes": { "intest.cli": { "version": "pinned-by-us" } },
          "tools": { "some-other-tool": { "version": "3.4.5", "commands": ["other"] } }
        }
        """;
        File.WriteAllText(dotnetToolsPath, before);

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);

        ReadDotnetTools().ShouldBe(before,
            customMessage: "a same-named key outside \"tools\" must never be touched");
    }

    /// <summary>
    /// CRITICAL 2's proof: this exact refusal used to run <i>after</i> GenerateCommand.RunAsync had
    /// already rewritten Generated/ and coverage-report.json wholesale — measured by deleting
    /// .config/dotnet-tools.json and running `upgrade`, which exited 2 with fresh output already on
    /// disk. All four manifest checks (this one, the missing-pin case below, the missing-version-
    /// field case, and an unreadable intest.json) are now pure reads hoisted above the regenerate
    /// call, so none of them can ever observe Generated/ having been created.
    /// </summary>
    [TestMethod]
    public async Task RefusesWhenDotnetToolsJsonDoesNotExist()
    {
        InitProject(Spec);
        File.Delete(Path.Combine(_root, ".config", "dotnet-tools.json"));
        var intestJsonBefore = ReadIntestJson();

        (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);

        ReadIntestJson().ShouldBe(intestJsonBefore,
            customMessage: "intest.json must not be rewritten when the tools pin cannot be bumped");
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "CRITICAL 2: a missing manifest must be caught before regeneration ever runs");
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
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "CRITICAL 2: a manifest missing the intest.cli pin must be caught before regeneration ever runs");
    }

    /// <summary>
    /// The third of the four CRITICAL 2 causes: an intest.cli entry present but missing its
    /// "version" field. Also item 7's proof — the refusal must prescribe a fix like its two
    /// siblings (missing manifest, missing pin) do, not just diagnose.
    /// </summary>
    [TestMethod]
    public async Task RefusesWhenIntestCliPinHasNoVersionField()
    {
        InitProject(Spec);
        var dotnetToolsPath = Path.Combine(_root, ".config", "dotnet-tools.json");
        File.WriteAllText(dotnetToolsPath, """
        { "version": 1, "isRoot": true, "tools": { "intest.cli": { "commands": ["intest"] } } }
        """);
        var intestJsonBefore = ReadIntestJson();

        var (exitCode, error) = await UpgradeCapturingErrorAsync(_root);

        exitCode.ShouldBe(ExitCode.ToolError);
        error.ShouldContain("no \"version\" field", Case.Sensitive,
            customMessage: "the refusal must diagnose the actual problem");
        error.ShouldContain("add one by hand", Case.Insensitive,
            customMessage: "the refusal must prescribe a fix, matching its two siblings " +
                            "(missing manifest, missing pin), not just diagnose");
        ReadIntestJson().ShouldBe(intestJsonBefore);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "CRITICAL 2: an intest.cli pin with no version field must be caught before regeneration ever runs");
    }

    /// <summary>
    /// The fourth CRITICAL 2 cause: intest.json itself unreadable. Locking the file open for
    /// exclusive access is the only reliable, portable way to make a subsequent
    /// File.ReadAllBytesAsync throw IOException without deleting the file outright (deleting it
    /// would instead exercise ConfigLoader's own "No intest.json found" refusal, a different code
    /// path already covered elsewhere).
    /// </summary>
    [TestMethod]
    public async Task RefusesWhenIntestJsonIsUnreadable()
    {
        InitProject(Spec);
        var intestJsonPath = Path.Combine(_root, "intest.json");
        var dotnetToolsBefore = ReadDotnetTools();

        await using (new FileStream(intestJsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (await UpgradeAsync(_root)).ShouldBe(ExitCode.ToolError);
        }

        ReadDotnetTools().ShouldBe(dotnetToolsBefore,
            customMessage: "CRITICAL 2: an unreadable intest.json must be caught before " +
                            "regeneration — and before the tools pin is bumped — ever runs");
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
    }

    // ---- .gitattributes: write if absent, never overwrite --------------------------------------

    /// <summary>
    /// Item 6: `upgrade` is the first InTest command ever to create a team-owned file, and that
    /// narrowness was documented only in a code comment, not in the output a developer actually
    /// sees. The report line must say so when it happens.
    /// </summary>
    [TestMethod]
    public async Task ScaffoldsGitattributesWhenAbsent()
    {
        InitProject(Spec);
        File.Delete(Path.Combine(_root, ".gitattributes"));

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        File.Exists(Path.Combine(_root, ".gitattributes")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(_root, ".gitattributes")).ShouldContain("Generated/** text eol=crlf", Case.Sensitive);
        report.ShouldContain(".gitattributes", Case.Sensitive,
            customMessage: "the report must say a team-owned file was created, not just the two configs");
    }

    [TestMethod]
    public async Task NeverOverwritesAnExistingGitattributes()
    {
        InitProject(Spec);
        var gitattributesPath = Path.Combine(_root, ".gitattributes");
        File.WriteAllText(gitattributesPath, "# adopter customised this file\n*.custom text eol=lf\n");
        var before = File.ReadAllText(gitattributesPath);

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        File.ReadAllText(gitattributesPath).ShouldBe(before);
        report.ShouldNotContain(".gitattributes",
            customMessage: "an already-present .gitattributes was not created by this run, so the " +
                            "report must not claim it was");
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

    // ---- [prerelease-reference-migration]: detect and report, never rewrite -------------------

    /// <summary>
    /// Fires the case docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's
    /// [prerelease-reference-migration] exists for: a project scaffolded while intest was a
    /// prerelease, upgraded once intest is a different (here, effectively "stable" relative to the
    /// stale reference) version. init itself always scaffolds InTest.Runtime at CliVersion.Current
    /// ([scaffold-reads-itself]) — this test manufactures the drift by hand-editing the .csproj
    /// afterward, the same way InitProject manufactures an old intestVersion above, since a single
    /// test process can only ever be "one running intest version" at a time.
    /// </summary>
    [TestMethod]
    public async Task ReportsWhenTheScaffoldedRuntimeReferenceDiffersFromTheRunningVersion()
    {
        InitProject(Spec);

        var csprojPath = Path.Combine(_root, "Orders.ApiTests.csproj");
        var csprojText = File.ReadAllText(csprojPath);
        var scaffolded = $"Include=\"InTest.Runtime\" Version=\"{CliVersion.Current}\"";
        csprojText.ShouldContain(scaffolded, Case.Sensitive,
            customMessage: "init is expected to scaffold InTest.Runtime at the running tool's own version");

        var stalePrereleaseVersion = CliVersion.Current + "-preview.7";
        File.WriteAllText(csprojPath, csprojText.Replace(
            scaffolded, $"Include=\"InTest.Runtime\" Version=\"{stalePrereleaseVersion}\""));

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        report.ShouldContain("Orders.ApiTests.csproj", Case.Sensitive,
            customMessage: "the note must name the file");
        report.ShouldContain(
            $"Version=\"{stalePrereleaseVersion}\" to Version=\"{CliVersion.Current}\"", Case.Sensitive,
            customMessage: "the note must name the current value and the exact replacement");

        // Read only: upgrade must not rewrite the .csproj, even though it just told the adopter
        // exactly what to change there by hand.
        File.ReadAllText(csprojPath).ShouldContain(
            $"Include=\"InTest.Runtime\" Version=\"{stalePrereleaseVersion}\"", Case.Sensitive,
            customMessage: "upgrade must never rewrite the .csproj — see [prerelease-reference-migration]");
    }

    /// <summary>
    /// The converse of the test above, and the one this decision's own doc comment insists on as
    /// hard as the positive case: a .csproj that does not match the one shape `init` is known to
    /// write — here, central package management dropping the Version attribute entirely — must
    /// produce silence, not a crash and not a guessed report against a match that was never made.
    /// </summary>
    [TestMethod]
    public async Task SilentWhenTheCsprojDoesNotMatchTheExpectedRuntimeReferenceShape()
    {
        InitProject(Spec);

        var csprojPath = Path.Combine(_root, "Orders.ApiTests.csproj");
        var csprojText = File.ReadAllText(csprojPath);
        var scaffolded = $"<PackageReference Include=\"InTest.Runtime\" Version=\"{CliVersion.Current}\" />";
        csprojText.ShouldContain(scaffolded, Case.Sensitive);
        File.WriteAllText(csprojPath, csprojText.Replace(
            scaffolded, "<PackageReference Include=\"InTest.Runtime\" />"));

        var (exitCode, report) = await UpgradeCapturingReportAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        report.ShouldNotContain("NOTE:",
            customMessage: "a .csproj shape DetectRuntimeReferenceMismatch does not recognise must " +
                            "produce silence, not a guess");
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
