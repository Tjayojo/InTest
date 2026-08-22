using System.Diagnostics;

namespace InTest.Golden.Tests;

/// <summary>
/// Runs an external process and captures its combined stdout+stderr. Task 9's whole-branch
/// review found this exact block duplicated in three places — <c>InitCommandTests</c> (in
/// <c>InTest.Cli.Tests</c>), <see cref="GeneratedSuiteExecutionTests"/>'s own private
/// <c>RunAsync</c>, and inline in <see cref="CompileVerificationTests"/>. Task 10 item 7 moved
/// the first of those into this assembly (next to <see cref="CompileVerificationTests"/>, which
/// already owns "does the scaffolded project build"), turning what was a cross-assembly
/// duplication into a same-assembly one with an obvious single home — this class (item 6).
/// <para>
/// v1-e's <c>InitCommandTests.RunGit</c> (in <c>InTest.Cli.Tests</c>) is a separate, later,
/// near-identical-looking block — not a regression of the move above. It shells out to `git`,
/// not `dotnet`, needs its own hermetic environment (empty <c>GIT_CONFIG_GLOBAL</c>/
/// <c>GIT_CONFIG_SYSTEM</c>, <c>GIT_TERMINAL_PROMPT=0</c>) that would be meaningless for this
/// class's callers, and lives in the assembly that already owns everything else about `init`'s
/// scaffold. Consolidating it here would trade one obvious cross-assembly duplication (what
/// this comment used to warn about) for a same-assembly coupling between two processes that
/// share only "start it and read its output" — not worth it for one caller.
/// </para>
/// </summary>
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}
