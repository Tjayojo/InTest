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
        var startInfo = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Without this, every `dotnet build`/`dotnet test` below hangs this method for the full
        // MSBuild node-reuse idle timeout — 15 minutes — even though the child itself exits in
        // seconds. MSBuild spawns persistent worker nodes with /nodeReuse:true; those nodes
        // INHERIT the redirected stdout/stderr handles this ProcessStartInfo creates, and they
        // outlive the `dotnet` process that spawned them. The write end of the pipe therefore
        // stays open after the child exits, ReadToEndAsync never sees EOF, and this method
        // blocks until the orphaned nodes finally time out and close their inherited handles.
        //
        // Measured here, not inferred: GeneratedProjectCompiles took 15m20s against a 13s
        // standalone build, and GeneratedSuiteBuildsAndPassesAgainstALiveService — which shells
        // out twice — took 40m51s. Every test in this assembly that does NOT shell out finished
        // in milliseconds in the same run. During a stall the only child of testhost was a
        // conhost.exe, confirming the `dotnet` child had already exited while the pipe stayed
        // open.
        //
        // Set per-child rather than for the whole test assembly: this is a fact about how these
        // specific redirected child builds must run, and scoping it here means it cannot be lost
        // by someone editing a runsettings file or a CI workflow that looks unrelated.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)!;

        // Both pipes are drained CONCURRENTLY, and awaiting them sequentially is a deadlock, not
        // a style preference. `await ReadToEndAsync()` on stdout does not return until the child
        // closes stdout — normally at exit — so a child that first fills stderr's OS pipe buffer
        // (~4KB on Windows) blocks forever on its own stderr write, never exits, never closes
        // stdout, and this method never returns. `dotnet build` and `dotnet test` on a scaffolded
        // project clear 4KB of stderr easily once a build emits warnings, so the bound is real
        // rather than theoretical: this was measured here as a run that sat at zero CPU with no
        // child process for over an hour before it was diagnosed.
        //
        // WaitForExitAsync is awaited last, after both reads have completed, because on .NET it
        // additionally waits for the redirected streams to reach EOF — awaiting it first would
        // reintroduce the same hazard from the other direction.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout + stderr);
    }
}
