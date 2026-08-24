using InTest.Cli;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// §5's exit-code convention at the one layer no command owns: the command line itself.
/// <para>
/// These invoke the built <c>InTest.Cli</c> assembly as a real process and assert on the code it
/// hands the shell. Nothing shorter would do. Two defects live here, both above every command and
/// below every test that calls a <c>Command.Run</c> directly: the parse layer answering exit 1
/// where §5 says 2, and the crash floor doing the same. <c>InTest.Cli.Tests</c> could observe
/// neither — a test that starts at <c>InitCommand.Run</c> has already skipped the parse, and now
/// that the floor is <c>Program</c>'s rather than each command's, an in-process call cannot reach
/// it at all: the exception escapes before there is any exit code to assert on.
/// </para>
/// <para>
/// They live in this assembly and not in <c>InTest.Cli.Tests</c> because
/// <see cref="ProcessRunner"/> does — item 6 of Task 10 gave out-of-process invocation a single
/// home here precisely so a second copy would not appear in the other assembly.
/// </para>
/// </summary>
[TestClass]
public class CliExitCodeTests
{
    private static string Cli => Path.Combine(AppContext.BaseDirectory, "InTest.Cli.dll");

    private static Task<(int ExitCode, string Output)> RunCliAsync(string arguments) =>
        ProcessRunner.RunAsync("dotnet", $"\"{Cli}\" {arguments}".TrimEnd());

    [TestMethod]
    public async Task MissingRequiredOptionExitsToolError()
    {
        // The defect, stated: `--name ""` and `--name` absent are the same mistake one keystroke
        // apart. The first reached CommandArguments and exited 2; the second never reached a
        // command at all and exited 1 — the code §5 reserves for work a human must go and do.
        var (exitCode, output) = await RunCliAsync("init --spec orders.json");

        exitCode.ShouldBe(2, $"a command line that could not be parsed is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task UnrecognisedFlagExitsToolError()
    {
        var (exitCode, output) = await RunCliAsync("init --name Orders.ApiTests --spec orders.json --bogus");

        exitCode.ShouldBe(2, $"an unrecognised flag is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task NoCommandNamedExitsToolError()
    {
        // The root command has subcommands and no action of its own, so bare `intest` is a parse
        // failure like any other. Named here because the fix sits above every command: this one
        // belongs to no command, which is the point.
        var (exitCode, output) = await RunCliAsync(string.Empty);

        exitCode.ShouldBe(2, $"naming no command is a tool error:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task ParseFailureKeepsSystemCommandLineDiagnostics()
    {
        // The exit code was the defect, not the text. Asserting on the interpolated token rather
        // than on System.CommandLine's sentence keeps this from failing under a non-English UI
        // culture, where the sentence around the token is localised and the token is not.
        var (_, output) = await RunCliAsync("init --spec orders.json");

        output.ShouldContain("--name", Case.Sensitive);
    }

    [TestMethod]
    public async Task HelpExitsOk()
    {
        var (exitCode, output) = await RunCliAsync("--help");

        exitCode.ShouldBe(0, $"--help is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task HelpOnACommandWithAnUnsuppliedRequiredOptionExitsOk()
    {
        // The carve-out that makes the fix non-trivial. `init --help` parses with errors present
        // — `--name` and `--spec` are both required and neither was given — and HelpAction
        // declares ClearsParseErrors, so the errors are gone by the time it has run. Read
        // ParseResult.Errors before invoking and this exits 2 and help stops working.
        var (exitCode, output) = await RunCliAsync("init --help");

        exitCode.ShouldBe(0, $"asking a command for help is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task VersionExitsOk()
    {
        var (exitCode, output) = await RunCliAsync("--version");

        exitCode.ShouldBe(0, $"--version is not a failure:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task ACommandsOwnExitCodeSurvives()
    {
        // The override must be reachable only from the parse layer. A command line that parses
        // cleanly and then declines still reports why it declined — 3, not 2 and not 0.
        var root = Path.Combine(Path.GetTempPath(), "intest-exitcode-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var first = await RunCliAsync($"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");
            first.ExitCode.ShouldBe(ExitCode.Ok, first.Output);

            var (exitCode, output) = await RunCliAsync(
                $"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");

            exitCode.ShouldBe(ExitCode.AlreadyInitialised, output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The regression this floor exists to prevent, and the one nothing else guards. §5 puts "an
    /// exception went unhandled" under 2, but <c>EnableDefaultExceptionHandler</c> — on by default
    /// — answers an exception escaping a command's action with 1, the code reserved for work a
    /// human must go and do. That was masked while <c>init</c>, <c>generate</c> and
    /// <c>fixtures repair</c> each carried a catch-all of their own; a fourth command would have
    /// shipped returning 1 for a crash, and no test in <c>InTest.Cli.Tests</c> could have noticed,
    /// because the parse layer is not involved and <c>ParseResult.Errors</c> is empty.
    /// <para>
    /// <c>init</c> now has no catch of its own, so this reaches the floor in <c>Program</c> and
    /// nowhere else. The trigger is a <c>--project</c> naming an existing <b>file</b>: it passes
    /// every rule the command states — non-blank, and <c>intest.json</c> is not present inside it
    /// — and then <c>Directory.CreateDirectory</c> throws <c>IOException</c>. Genuinely
    /// unanticipated rather than a refusal in disguise, which is what makes it a crash.
    /// </para>
    /// <para>
    /// The predecessor of this test passed <c>"\0nul"</c> to <c>InitCommand.Run</c> in-process. A
    /// NUL cannot cross a process-argument boundary, and an in-process call can no longer observe
    /// the floor anyway, so both halves had to change together.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CrashInACommandWithNoCatchOfItsOwnExitsToolError()
    {
        var file = Path.Combine(Path.GetTempPath(), "intest-crash-" + Guid.NewGuid().ToString("N")[..8]);
        await File.WriteAllTextAsync(file, "not a directory");
        try
        {
            var (exitCode, output) = await RunCliAsync(
                $"init --project \"{file}\" --name Orders.ApiTests --spec orders.json");

            exitCode.ShouldBe(ExitCode.ToolError,
                $"§5 puts an unhandled exception under 2; System.CommandLine's default handler " +
                $"would answer 1:{Environment.NewLine}{output}");
            output.ShouldContain("intest: unexpected failure:",
                customMessage: "the sentence the three per-command catch-alls used before it moved up");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// A crash is not a refusal, and the floor must not blur them. The typed catches inside the
    /// commands — <c>ConfigLoadException</c>, <c>SpecLoadException</c>,
    /// <c>FixtureFormatException</c> — print their message bare because it is written for the
    /// adopter; only the floor prefixes. Hoisting those catches up alongside the catch-all would
    /// have relabelled every curated refusal as "unexpected failure", so this pins the boundary
    /// from outside the process, where the distinction is actually visible to CI.
    /// <para>
    /// Both commands that catch typed exceptions are covered, not just one. The property is held
    /// per-command rather than structurally — each command has to remember to catch — so pinning
    /// it for <c>generate</c> alone would leave it unpinned for <c>fixtures repair</c>, which
    /// carries the most typed catches of the two and is therefore the likelier place to lose it.
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("generate", DisplayName = "generate")]
    [DataRow("fixtures repair", DisplayName = "fixtures repair")]
    public async Task ARefusalIsNotReportedThroughTheCrashFloor(string command)
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-refusal-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var (exitCode, output) = await RunCliAsync($"{command} --project \"{root}\"");

            exitCode.ShouldBe(ExitCode.ToolError, output);
            output.ShouldNotContain("unexpected failure",
                customMessage: "a missing intest.json is a stated tool error, not a crash");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The help text is a promise the tool makes in its own voice, and for a long time it was the
    /// one promise the tool could not keep: <c>--spec</c> read "Path or URL of the OpenAPI
    /// document" while both commands that consume the value handed
    /// <c>Path.Combine(projectRoot, source)</c> to <c>SpecLoader.LoadFromFileAsync</c>, which
    /// opens files. §9's snapshot closed that gap, so this test now pins the promise being kept
    /// rather than withdrawn — the sentence is the same, and it finally means something. Pinned
    /// here for the same reason
    /// every other test in this class is: <c>Program</c>'s option definitions are above every
    /// command, so <c>InTest.Cli.Tests</c> — which calls <c>Command.Run</c> methods directly —
    /// never executes them and could not observe this.
    /// <para>
    /// Asserted on the <c>--spec</c> line alone rather than on the whole help output, because
    /// <c>--project</c>'s line carries the current directory as its default and a checkout path
    /// is not something a test gets to make claims about.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SpecHelpPromisesAPathOrAUrl()
    {
        var (_, output) = await RunCliAsync("init --help");

        var specLine = output.Split('\n').SingleOrDefault(line => line.Contains("--spec <spec>"));
        specLine.ShouldNotBeNull($"init --help must document --spec:{Environment.NewLine}{output}");

        specLine.ShouldContain("Path of the OpenAPI document");
        specLine.ShouldContain("URL",
            customMessage: "§9's snapshot shipped, so the promise the help text makes is finally " +
                           "one the tool keeps — a URL is fetched and snapshotted to spec.json");
    }

    /// <summary>
    /// A URL <c>--spec</c> scaffolds, out of process, exit 0 — the capability the help text one
    /// line above has always described.
    /// <para>
    /// This replaces a refusal test, and the history is worth keeping because it is why `init`
    /// judges <c>--spec</c> at all. Measured before any guard existed, this exact command line
    /// printed "Initialised Orders.ApiTests. Next: `intest generate`." and exited <b>0</b>,
    /// writing the whole scaffold; `generate` then failed with
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c>, exit 2.
    /// The tool accepted the value its help had promised, then contradicted itself one command
    /// later in the vocabulary of a missing file. Exit 0 is now the right answer for the first
    /// time — but for a reason (§9's snapshot exists), not by omission.
    /// </para>
    /// <para>
    /// Out of process rather than in <c>InitCommandTests</c>, which pins the same scaffold:
    /// <c>init</c> is the command that <i>takes</i> <c>--spec</c>, so its exit code is what a
    /// pipeline sees. What a malformed URL does is the refusal half, and lives in
    /// <c>InitCommandTests.RefusesAMalformedUrlSpec</c>.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AUrlSpecScaffoldsAProjectPointingTheBuildAtTheSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-urlspec-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var (exitCode, output) = await RunCliAsync(
                $"init --project \"{root}\" --name Orders.ApiTests --spec https://example.com/openapi.json");

            exitCode.ShouldBe(ExitCode.Ok, output);
            output.ShouldContain("Initialised");

            File.ReadAllText(Path.Combine(root, "intest.json"))
                .ShouldContain("https://example.com/openapi.json",
                    customMessage: "the URL is the source, and is what intest.json records");
            File.ReadAllText(Path.Combine(root, "Orders.ApiTests.csproj"))
                .ShouldContain("<InTestSpecSource>spec.json</InTestSpecSource>",
                    customMessage: "MSBuild cannot copy from https:// — the build points at the snapshot");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// v1-e Task 3. Every other <c>--check</c> assertion lives in
    /// <c>InTest.Cli.Tests.GenerateCheckCommandTests</c>, calling <c>GenerateCommand.RunAsync</c>
    /// directly with <c>check: true</c> — which never exercises <c>Program.cs</c>'s option
    /// definitions at all, for the same reason every other test in this class exists: an
    /// in-process call skips the parse layer entirely. <c>--check</c> is a plain
    /// <c>Option&lt;bool&gt;</c> with no custom parser, so nothing here is expected to reach the
    /// unreachable-by-design catches <c>Program.cs</c> documents for that case — this test is
    /// only about whether the flag is wired to <c>generate</c> at all, and whether it survives
    /// the trip through <c>ParseResult.GetValue</c> into the exit code a real invocation reports.
    /// </summary>
    [TestMethod]
    public async Task CheckFlagIsWiredThroughToGenerate()
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-checkflag-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "orders.json"), """
            {
              "openapi": "3.0.3",
              "info": { "title": "Orders", "version": "1.0" },
              "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
                "responses": { "200": { "description": "ok" } } } } }
            }
            """);

            var init = await RunCliAsync(
                $"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");
            init.ExitCode.ShouldBe(ExitCode.Ok, init.Output);

            var firstGenerate = await RunCliAsync($"generate --project \"{root}\"");
            firstGenerate.ExitCode.ShouldBe(ExitCode.Ok, firstGenerate.Output);

            var matchingCheck = await RunCliAsync($"generate --project \"{root}\" --check");
            matchingCheck.ExitCode.ShouldBe(ExitCode.Ok,
                $"a fresh scaffold's committed output must match its own render:{Environment.NewLine}{matchingCheck.Output}");

            // Edits the committed class file directly rather than the spec, so this test does not
            // depend on GenerateCheckCommandTests' more detailed "which artefact changed" cases —
            // it only needs *some* real difference, to prove --check's own exit-1 branch (not
            // just its exit-0 branch) survives the trip through Program.cs.
            var classFile = Path.Combine(root, "Generated", "OrdersTests.g.cs");
            await File.AppendAllTextAsync(classFile, "// hand-edited, no longer matches a fresh render\n");

            var driftedCheck = await RunCliAsync($"generate --project \"{root}\" --check");
            driftedCheck.ExitCode.ShouldBe(ExitCode.WorkOutstanding,
                $"a hand-edited generated file must be reported as drift through the real CLI, " +
                $"not just through GenerateCommand.RunAsync called directly:{Environment.NewLine}{driftedCheck.Output}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// v1-e Task 4, item 5 of a review round on it. `[paired]`'s whole argument is that a refusal
    /// must never name a remedy command that does not exist — <c>generate --check</c>'s exit-4
    /// version-mismatch message names `intest upgrade` on exactly that promise. Nothing had ever
    /// pinned that <c>upgrade</c> is actually reachable from a real command line rather than only
    /// from <c>UpgradeCommand.RunAsync</c> called in-process: every test in
    /// <c>InTest.Cli.Tests.UpgradeCommandTests</c> calls that method directly, which skips
    /// <c>Program.cs</c>'s subcommand wiring entirely, the same gap this class exists to close for
    /// every other command (see the type doc comment). Measured before this test existed: commenting
    /// out <c>root.Subcommands.Add(upgrade)</c> in <c>Program.cs</c> left every test in
    /// <c>InTest.Cli.Tests</c> green, because none of them go through the parser — `intest upgrade
    /// ...` would instead fail System.CommandLine's own "unrecognized command" parse, exit 2, and
    /// never reach <c>UpgradeCommand</c> at all.
    /// </summary>
    [TestMethod]
    public async Task UpgradeIsWiredAsARealSubcommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-upgradewiring-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "orders.json"), """
            {
              "openapi": "3.0.3",
              "info": { "title": "Orders", "version": "1.0" },
              "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
                "responses": { "200": { "description": "ok" } } } } }
            }
            """);

            var init = await RunCliAsync($"init --project \"{root}\" --name Orders.ApiTests --spec orders.json");
            init.ExitCode.ShouldBe(ExitCode.Ok, init.Output);

            var upgrade = await RunCliAsync($"upgrade --project \"{root}\"");

            upgrade.ExitCode.ShouldBe(ExitCode.Ok,
                $"`upgrade` must be reachable as a real subcommand, not just as a method " +
                $"InTest.Cli.Tests can call directly:{Environment.NewLine}{upgrade.Output}");
            upgrade.Output.ShouldNotContain("Unrecognized command or argument",
                customMessage: "if this appears, `upgrade` fell through to the parser's default " +
                                "\"no such command\" refusal instead of running");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
