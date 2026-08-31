using System.Text;

namespace InTest.Golden.Tests;

/// <summary>
/// [harness-port-comes-first]: builds the command line to execute a generated suite — it does not
/// run anything itself. Returning a command for the caller to hand to <see cref="ProcessRunner"/>,
/// rather than shelling out from here, was a deliberate choice over the alternative of just calling
/// <c>ProcessRunner.RunAsync</c> directly from this type: it keeps <see cref="ProcessRunner"/> as
/// the sole owner of its pipe-drain ordering and <c>MSBUILDDISABLENODEREUSE</c> handling (see its
/// own remarks for why both are load-bearing), and it means the argument-construction logic below
/// — which framework gets which flags, in what shape — is unit-testable without spawning a process
/// at all. <c>MsTestCommandUsesDotnetTestWithAllFlags</c> and its neighbors in
/// <c>GeneratedSuiteExecutionTests.cs</c> exercise this type directly for exactly that reason.
/// <para>
/// The two frameworks have exactly one working invocation each, and they are not the same shape.
/// </para>
/// <para>
/// <b>MSTest — `dotnet test`.</b> The scaffold sets no <c>OutputType</c> and no
/// <c>EnableMSTestRunner</c>, so it builds a plain dll. There is no executable to invoke, and
/// `dotnet test` is the only thing here that can run a dll's tests.
/// </para>
/// <para>
/// <b>xUnit v3 — the built assembly, invoked as `dotnet &lt;dll&gt;`, never through an apphost
/// executable.</b> `dotnet test` uses the VSTest target, which the .NET 10 SDK refuses for a
/// Microsoft.Testing.Platform project: "Testing with VSTest target is no longer supported by
/// Microsoft.Testing.Platform on .NET 10 SDK and later." An apphost (<c>ProjectName.exe</c>) was
/// tried first and works on Windows, but the `golden` CI job also runs on <c>ubuntu-latest</c>
/// (`.github/workflows/build-and-test.yml`), where `dotnet build` emits an <i>extensionless</i>
/// apphost — a hardcoded <c>.exe</c> would silently never exist on that leg, and the first xUnit
/// caller would die inside <c>Process.Start</c> rather than fail a test. `dotnet &lt;dll&gt;` is
/// identical on both operating systems and was confirmed working directly: `dotnet p.dll
/// -result-trx out.trx -filterVSTest "FullyQualifiedName~Ok"` ran the test and wrote the trx.
/// </para>
/// <para>
/// The Microsoft.Testing.Platform opt-in path was also tried, as a candidate for making `dotnet
/// test` work for xUnit after all — `dotnet test -- --report-trx`, with a `global.json` runner
/// entry — and produced <c>Zero tests ran</c>, exit 5, measured on SDK 10.0.400, 10.0.303 and
/// 10.0.111. An MSTest control run under the same `global.json` failed identically, which is why
/// the conclusion is that `dotnet test`'s MTP handshake is broken <i>on this machine</i> rather than
/// anything xUnit-specific — that recipe may well work elsewhere, and this is not a claim that it
/// is broken everywhere. The direct-assembly invocation needs no opt-in at all and worked
/// unconditionally in every case tried here, which is why it is the one used below. <b>Do not spend
/// time trying to make `dotnet test` work for xUnit.</b>
/// </para>
/// <para>
/// <c>-result-trx</c> and <c>-filterVSTest</c> are real xunit.v3 in-process-runner flags — confirmed
/// against xunit.v3 <b>4.0.0</b> by running them and inspecting the resulting trx. An earlier probe
/// found them spelled <c>-trx</c>/<c>-filter</c> instead and concluded (wrongly) that the names
/// below were fabricated; that probe had resolved xunit.v3 <b>1.1.0</b>, because a throwaway
/// scratchpad project outside this repo does not inherit <c>Directory.Packages.props</c>'s version
/// pin and NuGet picked its own. Pin the version explicitly in any future probe built outside the
/// repo, or it will silently test the wrong package version's CLI surface.
/// </para>
/// <para>
/// <c>"Debug"</c> and <c>"net10.0"</c> in the xUnit branch below must track the sibling `dotnet
/// build` call's configuration (Debug by default, here and at every call site) and
/// <c>Directory.Build.props</c>' target framework. Nothing fails loudly if either drifts — the
/// computed path simply does not exist, and <see cref="ProcessRunner.RunAsync"/> throws inside
/// <c>Process.Start</c> rather than reporting a clear "wrong path" error.
/// </para>
/// <para>
/// <c>--results-directory</c> is a `dotnet test` option; the direct xUnit runner has no equivalent
/// flag. The mstest arm appends it as its own flag. The xunit arm instead folds it into the trx
/// path via <see cref="Path.Combine(string, string)"/>, because <c>-result-trx</c> takes a path, not
/// a bare filename plus a separate directory option. Modeling this once here — rather than letting
/// every MSTest-specific call site concatenate its own <c>--results-directory "…"</c> fragment, as
/// an earlier version of this conversion did across 18 call sites — keeps all MSTest-specific text
/// inside the one type whose job is holding it, and means the xUnit caller never has to touch those
/// 18 sites again when it starts passing a results directory of its own.
/// </para>
/// </summary>
internal sealed record GeneratedSuiteCommand(string FileName, string Arguments)
{
    internal static GeneratedSuiteCommand For(
        string framework,
        string projectRoot,
        string projectName,
        string? trxPath = null,
        string? filter = null,
        string? resultsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(projectName);

        return framework switch
        {
            // [nunit-is-vstest] (Task 6 of the NUnit framework pack plan): NUnit joins MSTest's
            // arm here rather than getting a duplicate branch — measured, both run under classic
            // VSTest (`dotnet test <csproj>` exits 0, `--logger "trx;LogFileName=…"` produces a
            // trx), unlike xunit.v3's Microsoft.Testing.Platform invocation below. No new
            // invocation shape, no direct-exe path, no -filterVSTest translation for NUnit.
            "mstest" or "nunit" => new GeneratedSuiteCommand(
                "dotnet", MsTestArguments(projectRoot, trxPath, filter, resultsDirectory)),
            "xunit" => new GeneratedSuiteCommand(
                "dotnet",
                XunitCommandArguments(
                    Path.Combine(projectRoot, "bin", "Debug", "net10.0", projectName + ".dll"),
                    trxPath,
                    filter,
                    resultsDirectory)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(framework), framework, "expected \"mstest\", \"nunit\", or \"xunit\"."),
        };
    }

    private static string MsTestArguments(
        string projectRoot, string? trxPath, string? filter, string? resultsDirectory)
    {
        var sb = new StringBuilder($"test \"{projectRoot}\" --no-build --nologo");
        if (trxPath is not null)
        {
            sb.Append($" --logger \"trx;LogFileName={trxPath}\"");
        }

        if (filter is not null)
        {
            sb.Append($" --filter \"{filter}\"");
        }

        if (resultsDirectory is not null)
        {
            sb.Append($" --results-directory \"{resultsDirectory}\"");
        }

        return sb.ToString();
    }

    private static string XunitCommandArguments(
        string dllPath, string? trxPath, string? filter, string? resultsDirectory)
    {
        var xunitArgs = XunitArguments(trxPath, filter, resultsDirectory);

        // No trailing space when neither flag was requested — a caller that asserts the exact
        // argument string (as the tests below do) should never have to account for one.
        return xunitArgs.Length > 0 ? $"\"{dllPath}\" {xunitArgs}" : $"\"{dllPath}\"";
    }

    private static string XunitArguments(string? trxPath, string? filter, string? resultsDirectory)
    {
        var sb = new StringBuilder();
        if (trxPath is not null)
        {
            // -result-trx takes a path, not a bare filename plus a separate directory option (the
            // direct runner has nothing equivalent to dotnet test's --results-directory), so a
            // caller-supplied results directory is folded in here instead of being its own flag.
            var effectiveTrxPath = resultsDirectory is not null
                ? Path.Combine(resultsDirectory, trxPath)
                : trxPath;
            sb.Append($"-result-trx \"{effectiveTrxPath}\"");
        }

        // -filterVSTest, not --filter: the direct runner rejects --filter with
        // "error: unknown option: --filter" and takes the identical query string under this name.
        if (filter is not null)
        {
            sb.Append(sb.Length > 0 ? " " : "").Append($"-filterVSTest \"{filter}\"");
        }

        return sb.ToString();
    }
}
