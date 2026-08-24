using System.CommandLine;
using InTest.Cli;
using InTest.Cli.Commands;

var projectOption = new Option<string>("--project")
{
    Description = "Test project directory containing intest.json.",
    DefaultValueFactory = _ => Directory.GetCurrentDirectory()
};

var checkOption = new Option<bool>("--check")
{
    Description = "Compare committed output against a fresh render instead of writing. " +
                   "Reads only; writes nothing under Generated/ or coverage-report.json."
};

var generate = new Command("generate", "Generate tests from the configured OpenAPI document.");
generate.Options.Add(projectOption);
generate.Options.Add(checkOption);
generate.SetAction((parseResult, cancellationToken) =>
    GenerateCommand.RunAsync(
        parseResult.GetValue(projectOption)!,
        cancellationToken,
        check: parseResult.GetValue(checkOption)));

var nameOption = new Option<string>("--name") { Description = "Test project name.", Required = true };
// "Path or URL" is finally true. This sentence has a history worth one line of comment: it said
// "Path or URL" while `init` and `generate` both refused a URL outright, which is the
// documentation-ahead-of-the-build defect the deleted SpecLoader.UrlReason existed to apologise
// for. §9's snapshot shipped; the promise the help text was making is now kept. A URL is fetched
// by `generate` and snapshotted to spec.json — see SpecSnapshot.
var specOption = new Option<string>("--spec") { Description = "Path of the OpenAPI document relative to the test project directory, or the URL it is served from.", Required = true };

var init = new Command("init", "Scaffold a test project.");
init.Options.Add(projectOption);
init.Options.Add(nameOption);
init.Options.Add(specOption);
init.SetAction(parseResult => InitCommand.Run(
    parseResult.GetValue(projectOption)!,
    parseResult.GetValue(nameOption)!,
    parseResult.GetValue(specOption)!));

var fixtures = new Command("fixtures", "Fixture maintenance.");
var repair = new Command("repair", "Create missing fixtures and add sentinels for new required properties.");
repair.Options.Add(projectOption);
repair.SetAction((parseResult, cancellationToken) =>
    FixturesRepairCommand.RunAsync(parseResult.GetValue(projectOption)!, cancellationToken));
fixtures.Subcommands.Add(repair);

var upgrade = new Command("upgrade",
    "Adopt the running intest version: regenerate against it, then bump intestVersion and the .config/dotnet-tools.json pin.");
upgrade.Options.Add(projectOption);
upgrade.SetAction((parseResult, cancellationToken) =>
    UpgradeCommand.RunAsync(parseResult.GetValue(projectOption)!, cancellationToken));

var root = new RootCommand("InTest — generate API integration tests from an OpenAPI document.");
root.Subcommands.Add(generate);
root.Subcommands.Add(init);
root.Subcommands.Add(fixtures);
root.Subcommands.Add(upgrade);

// §5's exit-code convention, applied at the one layer no command owns. Two rules meet here, both
// of which System.CommandLine answers with exit 1 and §5 answers with exit 2, and neither of
// which any command can reach.
//
// The first is the parse failure. 1 is reserved for real work outstanding that a human must do —
// fixture drift, validation failures, `--check` differences. A command line that could not be
// parsed is none of those: nothing ran. That left `intest init --name ""` exiting 2 and `intest
// init` — the same mistake one keystroke apart — exiting 1, so CI could not tell a mistyped
// invocation from fixture drift, which is the single confusion the 1/2 split exists to prevent.
// It is not a widening: bare `intest` and bare `intest fixtures` are parse failures of the same
// kind and exit 2 too. Exempting them would mean adding a branch that asserts some parse failures
// mean outstanding work, which §5 denies.
//
// The second is the crash floor, and it is here for the same reason rather than by analogy. §5
// puts "an exception went unhandled" under 2, but `EnableDefaultExceptionHandler` — on by default
// — catches anything escaping a command's action and returns 1. That held only because `init`,
// `generate` and `fixtures repair` each carried their own catch-all, so a fourth command would
// have shipped returning 1 for a crash with no test able to notice: the parse layer is not
// involved and `parseResult.Errors` is empty. Disabling the handler and catching here makes the
// floor structural, so it covers commands not yet written. The three per-command catch-alls are
// gone; their typed catches stay, because those are not a floor — `ConfigLoadException`,
// `SpecLoadException` and `FixtureFormatException` carry adopter-facing text and are printed
// bare, and rendering them through the sentence below would relabel a curated refusal as a crash.
//
// Invoke first, then read `Errors`. `InvokeAsync` is what prints System.CommandLine's own
// diagnostics, and those are not the defect — the code is, so the text is left exactly as the
// library wrote it. Reading `Errors` afterwards is safe rather than merely convenient: a
// terminating action that declares `ClearsParseErrors` — `--help`, `--version` — suppresses the
// errors at *parse* time, so they never enter `Errors` to begin with instead of being cleared out
// of it mid-call. Measured against 2.0.11, not inferred from the property names.
//
// The catch covers `Parse` for a reason that is not reachable today and is meant to stay that
// way: an option carrying a `CustomParser` or a validator runs adopter-written code *during*
// parsing, and that code can throw. Every option here is a plain `Option<string>`, so nothing
// currently reaches it — which is precisely why it is worth saying, since the next reader finds
// an unreachable catch with no test behind it and concludes it is dead. It is justified the same
// way the floor below it is: it holds for options not yet added.
//
// `OperationCanceledException` is not special-cased on the way here, which is worth stating
// because frameworks routinely do special-case it: `EnableDefaultExceptionHandler` turns off the
// *exception* handler, leaving open whether cancellation takes a separate path through
// `InvokeAsync`. It does not — measured against 2.0.11 with a command action throwing it
// directly, which lands here and exits 2 exactly like any other escape. No test pins this: no
// command line can cancel a token, so the only in-repo trigger would be a test-only command on
// the shipped CLI, which is a worse trade than the branch it would cover.
ParseResult parseResult;
int exitCode;
try
{
    parseResult = root.Parse(args);
    exitCode = await parseResult.InvokeAsync(
        new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (Exception ex)
{
    // The sentence all three commands used before this moved up, unchanged. Deliberately not
    // phrased as a refusal: nothing here says the adopter broke a stated rule, and it cannot
    // promise that nothing was written, since a scaffold that fails on its sixth file has already
    // written five. That is why every rule a command can state is checked before its first write.
    Console.Error.WriteLine($"intest: unexpected failure: {ex.GetType().Name}: {ex.Message}");
    return ExitCode.ToolError;
}

return parseResult.Errors.Count > 0 ? ExitCode.ToolError : exitCode;
