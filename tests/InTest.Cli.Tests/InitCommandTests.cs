using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using InTest.Cli;
using InTest.Cli.Commands;
using InTest.Cli.Configuration;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class InitCommandTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-init-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveDirectory()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void ScaffoldsEveryTeamOwnedFile()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../Orders/bin/Debug/net10.0/orders.json").ShouldBe(0);

        foreach (var file in new[]
        {
            "intest.json", "Orders.ApiTests.csproj", ".editorconfig", ".gitattributes", "AssemblyInfo.cs",
            "TestStartup.cs", "OrdersTestBase.cs", "appsettings.json", "Orders.ApiTests.runsettings",
            ".config/dotnet-tools.json"
        })
        {
            File.Exists(Path.Combine(_root, file)).ShouldBeTrue($"{file} was not scaffolded.");
        }
    }

    // The spec used by GitattributesSurvivesAnAutocrlfTrueCheckout: `getOrderById`'s path
    // parameter needs no fixture to generate successfully (mirrors GenerateCommandTests.Spec —
    // duplicated here rather than shared, matching how each test file in this project already
    // keeps its own local Spec constant), which keeps that test to one `generate` call with no
    // `fixtures repair` step in between.
    private const string SpecNeedingNoFixture = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    /// <summary>
    /// Proves the scaffolded .gitattributes actually does its job, rather than merely existing.
    /// "The file on disk contains LF" would pass on Linux, or with core.autocrlf left at its
    /// non-Windows default, regardless of whether .gitattributes covers the right paths — or
    /// exists at all. This instead reproduces Step 1 of the v1-e line-endings task's manual
    /// measurement as an automated round trip: commit a real `init` + `generate` scaffold with
    /// core.autocrlf=true (the Git-for-Windows default) set on the source, then materialize a
    /// second working copy with the same setting forced on the destination — the two-step path a
    /// Windows adopter's own clone goes through — and diff the bytes. Every one of InTest's own
    /// generated artefacts (Generated/**, coverage-report.json, fixtures/*.json) must come back
    /// byte-identical; without .gitattributes pinning them, git's own autocrlf translation would
    /// rewrite every LF to CRLF on the second checkout, exactly as the manual experiment showed.
    /// </summary>
    [TestMethod]
    public async Task GitattributesSurvivesAnAutocrlfTrueCheckout()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), SpecNeedingNoFixture);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(ExitCode.Ok);

        // `generate` alone never writes fixtures/ (only `fixtures repair` does), so write one by
        // hand — pure LF, matching what FixtureDocument's writer now produces — to exercise the
        // fixtures/*.json pattern too, not just Generated/** and coverage-report.json.
        Directory.CreateDirectory(Path.Combine(_root, "fixtures"));
        File.WriteAllText(Path.Combine(_root, "fixtures", "sample.json"), "{\n  \"sample\": true\n}\n");

        var tracked = new[]
        {
            Path.Combine("Generated", "OrdersTests.g.cs"),
            Path.Combine("Generated", "spec-schemas.json"),
            Path.Combine("Generated", "spec-paths.json"),
            "coverage-report.json",
            Path.Combine("fixtures", "sample.json"),
        };
        var beforeCheckout = tracked.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(_root, f)));

        RunGit(_root, "init -q");
        RunGit(_root, "config core.autocrlf true");
        RunGit(_root, "config user.email test@example.com");
        RunGit(_root, "config user.name Test");
        RunGit(_root, "add -A");
        RunGit(_root, "commit -q -m snapshot");

        var clone = Path.Combine(Path.GetTempPath(), "intest-clone-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // --no-checkout, then set core.autocrlf, then checkout: a plain `clone` applies the
            // destination's config too late for a `-c` override to be trustworthy across git
            // versions (confirmed by direct experiment while measuring Step 1 — a `-c
            // core.autocrlf=true clone` converted the files correctly but did not persist the
            // setting into the clone's own .git/config, which this test does not want to depend
            // on). Splitting the two steps makes the setting unambiguously in effect for the
            // checkout that follows.
            RunGit(Path.GetTempPath(), $"clone -q --no-checkout \"{_root}\" \"{clone}\"");
            RunGit(clone, "config core.autocrlf true");
            RunGit(clone, "checkout -q HEAD -- .");

            foreach (var file in tracked)
            {
                File.ReadAllBytes(Path.Combine(clone, file)).ShouldBe(beforeCheckout[file],
                    $"{file} changed bytes across a core.autocrlf=true checkout — .gitattributes did not pin it to LF.");
            }
        }
        finally
        {
            ForceDeleteDirectory(clone);
        }
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {arguments} failed: {stdout}{stderr}");
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    [TestMethod]
    public void DeclaresParallelizationOnlyInAssemblyInfo()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "AssemblyInfo.cs")).ShouldContain("[assembly: DoNotParallelize]");
        // The element form, not the bare name: the INTEST0001 guard target must *name* both
        // properties in order to detect them, so what matters is that neither is ever *set*.
        var csproj = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        csproj.ShouldNotContain("<MSTestParallelizeScope>");
        csproj.ShouldNotContain("<MSTestParallelizeWorkers>");
    }

    [TestMethod]
    public void GuardsAgainstTheDuplicateAttributeBuildBreak()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj")).ShouldContain("INTEST0001");
    }

    [TestMethod]
    public void LeavesTheProfileParameterCommentedOut()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var runsettings = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.runsettings"));
        runsettings.ShouldContain("<!-- <Parameter name=\"profile\"");
    }

    [TestMethod]
    public void RefusesToOverwriteAnExistingProject()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(0);
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(3);
    }

    [TestMethod]
    public void RefusesAnInvalidNameAndWritesNothing()
    {
        // --name seeds project.rootNamespace, project.testBaseClass, baseClassName, and the
        // `namespace` declaration of two scaffolded files — an invalid value here is invalid
        // regardless of what is (or is not) already on disk, so this must be checked before the
        // intest.json-already-exists check and before anything is written.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "My Project", "orders.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--name");
        message.ShouldContain("My Project");
    }

    [TestMethod]
    public void CsprojCopiesFixturesToTheOutputDirectory()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"))
            .ShouldContain("fixtures/**/*.json",
                customMessage: "FixtureStore loads from AppContext.BaseDirectory — this is the F1 defect repeating");
    }

    [TestMethod]
    public void TestStartupDoesNotReferenceTheDeletedTestDataType()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "TestStartup.cs"))
            .ShouldNotContain("TestData", customMessage: "Task 8 deletes it; a scaffold must not teach a dead API");
    }

    [TestMethod]
    public void RegisterCommentPointsAtImplementingITestTokenProviderNowThatAuthHandlerConsumesIt()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        // Task 2 question (e): AuthHandler now ships attached to InTestClients.Api, so telling
        // an adopter to append their own DelegatingHandler there produces two handlers both
        // setting Authorization, where the last one registered silently wins. The comment must
        // say AuthHandler is already attached and that only ITestTokenProvider needs
        // implementing — the instruction this same comment told people NOT to follow before
        // AuthHandler existed to consume it.
        var scaffold = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        scaffold.ShouldContain("AuthHandler",
            customMessage: "the scaffold must say AuthHandler is already attached, not send an adopter to write their own");
        scaffold.ShouldContain("ITestTokenProvider",
            customMessage: "the scaffold must point at the extension point that now actually works");
        scaffold.ShouldContain("InTestClients.Api",
            customMessage: "the scaffold must still name the client AuthHandler is attached to");
    }

    [TestMethod]
    public void ScaffoldedStartupDrainsFixtureCleanup()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 5: without an [AssemblyCleanup] calling TestHost.CleanupAsync, DrainAsync ships
        // with no caller and a fixture's teardown never runs in a generated project. One regex
        // ties the attribute, the method signature, and the call together as a single unit,
        // rather than two independent ShouldContain checks: independent checks would still pass
        // if the call were moved into AssemblyInit and AssemblyCleanup were left empty, which is
        // exactly the failure mode this test exists to catch. The call is pinned with its
        // parenthesised invocation, "TestHost.CleanupAsync(context)", not the bare
        // "TestHost.CleanupAsync" substring: that bare form also appears in the method's own doc
        // comment, so it would stay present even if the method body were gutted.
        Regex.IsMatch(
                startup,
                @"\[AssemblyCleanup\]\s+public\s+static\s+async\s+Task\s+AssemblyCleanup\(TestContext\s+context\)" +
                @"\s*\{\s*await\s+TestHost\.CleanupAsync\(context\);\s*\}",
                RegexOptions.Singleline)
            .ShouldBeTrue("expected [AssemblyCleanup] to directly wrap a call to TestHost.CleanupAsync(context)");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedFixtureRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Commented, not live: `init` never discovers fixtures by reflection (v1-b decision 2), and a
        // live call here would reference a fixture type that does not exist yet, breaking every
        // fresh scaffold's build before a team has written one.
        startup.ShouldContain("// services.AddSingleton<IAssemblyFixture,");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedTokenProviderRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 6: same precedent as the IAssemblyFixture example above — commented, not live.
        // StaticTokenProvider needs a real token neither Catalog nor Inventory has a source for,
        // so a live registration here would either fail to construct or issue a token that
        // authenticates nothing. AuthHandler already no-ops when no provider is registered (Task
        // 2(b)), which is exactly the state this scaffold must ship in.
        startup.ShouldContain("// services.AddSingleton<ITestTokenProvider",
            customMessage: "the scaffold must show the registration, but only as a comment");
    }

    [TestMethod]
    public void EscapesAmpersandSoTheGeneratedCsprojActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/orders.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        // The real parse, not a string check: an unescaped '&' is not well-formed XML and
        // XDocument.Parse throws on it rather than silently accepting it.
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("../R&D/orders.json");
    }

    [TestMethod]
    public void EscapesDollarParenSoItSurvivesAsLiteralTextNotAnMSBuildExpansion()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders$(Configuration).json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        // %24, not a bare $( — a bare $(Configuration) would expand as an MSBuild property
        // reference rather than surviving as the literal text the adopter typed.
        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%24(Configuration).json");
    }

    [TestMethod]
    public void EscapesQuestionMarkSoTheIncludeGlobCannotResolveToADifferentFile()
    {
        // Confirmed by real `dotnet build` (see MSBuildPropertyValue's doc comment): with
        // specs/orders.json and specs/ordersX.json both on disk, an unescaped
        // Include="$(InTestSpecSource)" for "specs/orders?.json" silently resolved to
        // ordersX.json — the wrong file — instead of failing loudly.
        InitCommand.Run(_root, "Orders.ApiTests", "orders?.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%3F.json");
    }

    [TestMethod]
    public void EscapesQuoteSoTheGeneratedIntestJsonActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders\".json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        // The real parse: an unescaped '"' inside the JSON string value truncates it and leaves
        // the rest of the document malformed, which JsonDocument.Parse throws on.
        using var doc = JsonDocument.Parse(jsonText);

        doc.RootElement.GetProperty("spec").GetProperty("source").GetString().ShouldBe("orders\".json");
    }

    [TestMethod]
    public void WritesAmpersandAndNonAsciiCharactersLiterallyIntoIntestJson()
    {
        // Pins the choice of JavaScriptEncoder.UnsafeRelaxedJsonEscaping over the default
        // encoder — a choice round-tripping cannot prove, since both produce valid JSON encoding
        // the same string. The default encoder would render '&' as \u0026 and 'é' as \u00e9:
        // still correct JSON, but unreadable by an adopter who opens the file by hand.
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/café.json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        jsonText.ShouldContain("R&D");
        jsonText.ShouldContain("café");
    }

    [TestMethod]
    public void RoundTripsAHazardousSpecSourcePastConfigLoad()
    {
        // The strongest test on this surface: proves the value survives write (InitCommand) then
        // read (ConfigLoader) intact, through both escaping layers at once.
        var hazardous = "../R&D/orders?\"$(x).json";
        InitCommand.Run(_root, "Orders.ApiTests", hazardous).ShouldBe(0);

        ConfigLoader.Load(_root).SpecSource.ShouldBe(hazardous.Replace("\\", "/"));
    }

    [TestMethod]
    public void RefusesACharacterXmlCannotRepresentAndWritesNothing()
    {
        // U+0001 is a C0 control character XML 1.0's Char production excludes — no MSBuild or
        // XML escape sequence represents it, so this must refuse rather than escape.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "Orders.ApiTests", "orders\u0001.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--spec");
        // Pins that the diagnosis itself — not just the boilerplate sentence appended in
        // InitCommand — reached the message: MSBuildPropertyValue renders the offending
        // character as U+0001 rather than pasting the raw control character into the terminal.
        message.ShouldContain("U+0001");
    }

    // ---- One refusal surface -----------------------------------------------------------------
    // `init` rejects three arguments, and used to reject them two different ways. --name went
    // through CSharpIdentifier.TryValidateDottedName and came back as one sentence at exit 2;
    // --project and --spec went through ArgumentException.ThrowIfNullOrWhiteSpace and escaped
    // unhandled, which System.CommandLine turns into exit **1**. That is not a cosmetic
    // difference: §5 reserves 1 for "real work is outstanding that a human must do" — fixture
    // drift, validation failures — and separates it from 2 precisely so "CI can tell a crash from
    // fixture drift". A mistyped --spec therefore reported itself to a pipeline as fixture drift.
    // Two spellings of one mistake, `--name "My Project"` and `--name ""`, returned two different
    // exit codes. These tests pin the single surface that replaced it.

    /// <summary>Runs `init` with stderr captured, so a test can assert what the adopter is told.</summary>
    private static (int ExitCode, string Error) RunCapturingError(string projectRoot, string name, string spec)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            return (InitCommand.Run(projectRoot, name, spec), capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// The shape, asserted on the assembled message rather than on the template that produces it.
    /// CSharpIdentifier.EmptyValueReason makes the middle of every refusal one object, but the
    /// setting that leads it and the example it carries are supplied per call site — so a
    /// fourth refusal written freehand would still share the template and still break the shape.
    /// This is the test that would catch that.
    /// </summary>
    [TestMethod]
    [DataRow("--project", "", "Orders.ApiTests", "orders.json", DisplayName = "--project empty")]
    [DataRow("--name", null, "", "orders.json", DisplayName = "--name empty")]
    [DataRow("--name", null, "   ", "orders.json", DisplayName = "--name whitespace")]
    [DataRow("--spec", null, "Orders.ApiTests", "", DisplayName = "--spec empty")]
    [DataRow("--spec", null, "Orders.ApiTests", "  \t ", DisplayName = "--spec whitespace")]
    public void RefusesEveryBlankArgumentInTheSameShape(
        string setting, string? projectRoot, string name, string spec)
    {
        var (exitCode, error) = RunCapturingError(projectRoot ?? _root, name, spec);

        exitCode.ShouldBe(ExitCode.ToolError,
            "§5 gives 2 for a tool error and 1 for outstanding work — an argument the adopter " +
            "mistyped is a tool error, and reporting it as 1 makes it indistinguishable from drift");
        error.ShouldStartWith(setting,
            customMessage: "a refusal leads with the setting the adopter got wrong");
        error.ShouldContain("is empty",
            customMessage: "a refusal says what is wrong with the value, not just that something is");
        // Not ShouldContain("for example"): that phrase is discriminating only if an actual
        // quoted value follows it, and a rule that said "for example" and then trailed off would
        // have satisfied it. "Carries", not "ends with" — --project's example sits mid-sentence,
        // ahead of the sentence telling the adopter they can omit the flag entirely.
        Regex.IsMatch(error, "for example \"[^\"]+\"").ShouldBeTrue(
            "a refusal carries a value the adopter can copy");
    }

    /// <summary>
    /// Separate from the shape test because the shape test cannot prove this row: with --project
    /// blank, `_root` is not where a broken build would write. Path.Combine("", "intest.json") is
    /// "intest.json", so a blank --project does not fail — it silently retargets every write at
    /// the process's current directory. Refusing it is what stops `init` scaffolding nine files
    /// into whatever directory the adopter happened to be standing in.
    /// </summary>
    [TestMethod]
    public void RefusesABlankProjectRatherThanScaffoldingIntoTheCurrentDirectory()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        try
        {
            var (exitCode, error) = RunCapturingError("", "Orders.ApiTests", "orders.json");

            exitCode.ShouldBe(ExitCode.ToolError);
            error.ShouldStartWith("--project");
            Directory.GetFileSystemEntries(_root).ShouldBeEmpty(
                "a blank --project must be refused, not resolved to the current directory");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    /// <summary>
    /// The same reason the blank <c>--spec</c> guard gives: `init` must never write a config it
    /// knows `generate` will reject. Measured before this guard existed —
    /// <c>init --spec https://example.com/openapi.json</c> printed
    /// "Initialised Orders.ApiTests. Next: `intest generate`." and exited <b>0</b>, writing the
    /// whole scaffold, and only then did `generate` fail with
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c> at exit 2.
    /// <para>
    /// So `init` did not merely fail to help: it actively confirmed the belief the help text had
    /// created ("Path or URL"), and displaced the contradiction onto a different command, one
    /// step later, phrased as a missing file. Refusing here is what makes the tool's own voice
    /// agree with itself.
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("https://example.com/openapi.json", DisplayName = "https")]
    [DataRow("http://example.com/openapi.json", DisplayName = "http")]
    [DataRow("HTTPS://EXAMPLE.COM/openapi.json", DisplayName = "uppercase scheme")]
    public void RefusesAUrlSpecRatherThanScaffoldingAProjectGenerateWillReject(string spec)
    {
        var (exitCode, error) = RunCapturingError(_root, "Orders.ApiTests", spec);

        exitCode.ShouldBe(ExitCode.ToolError);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty(
            "§5's exit 2 is \"nothing was written\", and an argument is judged before the first write");
        error.ShouldStartWith("--spec",
            customMessage: "a refusal leads with the setting the adopter got wrong");
        error.ShouldContain(spec,
            customMessage: "a refusal quotes what the adopter actually wrote");
        error.ShouldContain("URL",
            customMessage: "a refusal names the kind of value it is refusing");
        error.ShouldContain("for example \"",
            customMessage: "a refusal carries a value the adopter can copy");
    }

    /// <summary>
    /// The false positive the narrow predicate exists to avoid, pinned at `init` as well as at
    /// <see cref="Configuration.ConfigLoader"/> because the two refuse independently.
    /// <c>Uri.TryCreate</c> calls <c>C:/specs/orders.json</c> an <i>absolute</i> URI with scheme
    /// <c>file</c>, so a general absolute-URI check would refuse the most ordinary
    /// <c>--spec</c> value on Windows. The rule is an <c>http://</c>/<c>https://</c> prefix and
    /// nothing broader.
    /// </summary>
    [TestMethod]
    [DataRow("C:/specs/orders.json", DisplayName = "rooted Windows path — an absolute file: URI to Uri.TryCreate")]
    [DataRow("//fileserver/specs/orders.json", DisplayName = "UNC path")]
    [DataRow("specs/http/orders.json", DisplayName = "path with a url-ish segment")]
    public void ScaffoldsFromAPathThatOnlyLooksLikeAUrl(string spec)
    {
        var (exitCode, error) = RunCapturingError(_root, "Orders.ApiTests", spec);

        exitCode.ShouldBe(ExitCode.Ok, error);
        var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "intest.json")));
        config.RootElement.GetProperty("spec").GetProperty("source").GetString().ShouldBe(spec);
    }

    // ReportsAnUnanticipatedScaffoldFailureAsAToolErrorRatherThanAStackTrace moved to
    // InTest.Golden.Tests/CliExitCodeTests as CrashInACommandWithNoCatchOfItsOwnExitsToolError.
    // It asserted the catch-all inside InitCommand.Run, and that catch-all is now Program's, so a
    // test calling InitCommand.Run directly can no longer reach it — the exception escapes before
    // any exit code exists. Only a real process can observe the floor, which is the point of
    // moving it rather than deleting it.

    // ScaffoldStillBuildsWithNoTokenProviderRegistered moved to InTest.Golden.Tests, next to
    // CompileVerificationTests (Task 10 item 7): it is the only out-of-process build that lived
    // in this assembly, and under a solution-level `dotnet test` this assembly's ~6s run fully
    // overlaps InTest.Golden.Tests' ~1m40s one, so two independent MSBuild invocations could
    // build scaffolded projects that both ProjectReference the same InTest.Runtime.csproj
    // simultaneously — a known source of intermittent obj/ file-lock failures. The assertion
    // itself is unchanged; see ScaffoldCompileVerificationTests there.
}
