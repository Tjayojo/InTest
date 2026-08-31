using System.Diagnostics;
using InTest.Runtime;
using NUnit.Framework;

namespace InTest.Runtime.NUnit.Tests;

/// <summary>
/// [error-is-the-sink]: Warn must reach the operator on a passing run. Every sink that fails under
/// NUnit fails *silently* -- nothing throws -- so a test that merely called Warn and asserted
/// nothing about output would pass against every wrong implementation.
/// <para>
/// The plan's prescribed shape -- redirect <see cref="Console.Error"/> around a direct, in-process
/// call to <see cref="TestHost.NUnitDiagnostics.Warn"/> -- does not hold up: measured directly,
/// <c>TestContext.Error</c> is NUnit's own per-test capture buffer, not a wrapper over
/// <see cref="Console.Error"/>, so <see cref="Console.SetError(TextWriter)"/> around the call sees
/// nothing (confirmed: that assertion failed against an empty captured string, while the marker
/// still reached the real console afterwards, under NUnit's own "Standard Error Messages"
/// reporting). That is exactly the plan's own escape hatch ("if TestContext.Error cannot be
/// redirected this way, the assertion shape is wrong, not the sink"), so this test uses the
/// prescribed fallback instead: run the suite out-of-process and grep its captured console output,
/// the same way the design probe established the finding in the first place.
/// </para>
/// <para>
/// <see cref="EmitsWarnMarker"/> is the leaf: it only calls <c>Warn</c> and always passes -- it
/// exists solely as a filterable, isolated subprocess target whose own passing-run console output
/// is what gets asserted on below. The <c>--filter</c> selects only that one test by name, so the
/// outer test's subprocess never re-enters itself.
/// </para>
/// </summary>
[TestFixture]
public class NUnitDiagnosticsTests
{
    private const string Marker = "WARN_MARKER";

    [Test]
    public void EmitsWarnMarker()
    {
        new TestHost.NUnitDiagnostics().Warn(Marker);
        Assert.Pass();
    }

    [Test]
    public async Task WarnWritesToTheErrorSinkWhichSurvivesAPassingRun()
    {
        var (exitCode, output) = await RunFilteredSubprocessAsync(nameof(EmitsWarnMarker));

        Assert.That(exitCode, Is.EqualTo(0), $"subprocess run should have passed; output was:\n{output}");
        Assert.That(output, Does.Contain(Marker));
    }

    /// <summary>
    /// Shells out to <c>dotnet test</c> against this very project, filtered to <paramref name="testName"/>
    /// only, and returns the exit code plus the combined stdout/stderr the operator would actually
    /// see. This project intentionally has no dependency on InTest.Cli's <c>ProcessRunner</c> --
    /// InTest.Runtime.NUnit.Tests exists to test the NUnit adapter's own internals in isolation, not
    /// to pull in the CLI's process-invocation helper for one call site.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunFilteredSubprocessAsync(string testName)
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var csproj = Path.Combine(projectDirectory, "InTest.Runtime.NUnit.Tests.csproj");

        // AppContext.BaseDirectory is <project>/bin/<Configuration>/<tfm>/, so the configuration is
        // the parent of the tfm directory. Derived rather than hardcoded to "Debug" because
        // --no-build below makes the subprocess look for an existing build: hardcoding Debug would
        // send a Release run hunting for outputs that were never produced.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Required for the same reason `ProcessRunner.RunAsync` (tests/InTest.Golden.Tests/ProcessRunner.cs)
        // sets it before every redirected `dotnet build`/`dotnet test` -- see that method's comment
        // for the full mechanism and the measured stall it prevents. Not delegating to ProcessRunner
        // itself: it lives in a different assembly, and its own doc comment already argues against
        // sharing it across assemblies for a single call site.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(csproj);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add($"FullyQualifiedName~{testName}");
        startInfo.ArgumentList.Add("--nologo");

        // --no-build is load-bearing, not an optimisation. Without it the subprocess builds this
        // very project, and that build copies InTest.Runtime.dll and InTest.Runtime.NUnit.dll into
        // <project>/bin/<config>/<tfm>/ -- the directory the test host running THIS method has both
        // assemblies loaded and locked from. The copy then fails with MSB3027/MSB3021 ("locked by:
        // testhost"), the subprocess exits 1, and the assertion below reports a sink failure that
        // is really a build failure.
        //
        // Measured, not theorised. The copy is attempted only when the source assemblies are newer
        // than the copies, so an incremental no-op build never trips it -- which is exactly why this
        // shipped green and stayed green: every run that passed had nothing to copy. Reproduced
        // deterministically by rebuilding src/InTest.Runtime and src/InTest.Runtime.NUnit (making
        // the copies genuinely stale) and then running this suite with --no-build so nothing could
        // refresh them: 1 failed, 1 passed, "error MSB3027 ... locked by: testhost". In the wild it
        // fires whenever something rebuilds those projects between this suite's own build and the
        // subprocess -- a concurrent build in the same tree, or an edit to Directory.Build.props,
        // which invalidates every project's incremental state at once.
        //
        // --no-build is also the semantically correct choice independent of the lock: this test
        // asserts what the assemblies under test do, and those are the assemblies the outer host
        // already loaded. Rebuilding mid-run would test something else.
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);

        using var process = Process.Start(startInfo)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut + stdErr);
    }
}
