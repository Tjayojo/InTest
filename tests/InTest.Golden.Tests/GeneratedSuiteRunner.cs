using System.Text;

namespace InTest.Golden.Tests;

/// <summary>
/// [harness-port-comes-first]: chooses how to execute a generated suite, because the two frameworks
/// have exactly one working invocation each and they are not the same shape.
/// <para>
/// <b>MSTest — `dotnet test`.</b> The scaffold sets no <c>OutputType</c>, so it builds a dll. There
/// is no executable to run.
/// </para>
/// <para>
/// <b>xUnit v3 — the built executable, directly.</b> `dotnet test` uses the VSTest target, which the
/// .NET 10 SDK refuses for a Microsoft.Testing.Platform project. This was measured on SDK 10.0.400,
/// 10.0.303 and 10.0.111, with and without a logger argument, so it is not a flag problem. The
/// Microsoft.Testing.Platform opt-in path (`dotnet test -- --report-trx`, with a `global.json`
/// runner entry) was also tried and produced <c>Zero tests ran</c>, exit 5 — and an MSTest control
/// under the same `global.json` failed identically, so what is broken there is `dotnet test`'s MTP
/// handshake rather than anything xUnit-specific. The direct executable needs no opt-in at all and
/// works unconditionally, which is why it is the one used here. <b>Do not spend time trying to make
/// `dotnet test` work for xUnit.</b>
/// </para>
/// </summary>
internal sealed record GeneratedSuiteRunner(string FileName, string Arguments)
{
    internal static GeneratedSuiteRunner For(
        string framework,
        string projectRoot,
        string projectName,
        string? trxPath = null,
        string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(projectName);

        return framework switch
        {
            "mstest" => new GeneratedSuiteRunner("dotnet", MsTestArguments(projectRoot, trxPath, filter)),
            "xunit" => new GeneratedSuiteRunner(
                Path.Combine(projectRoot, "bin", "Debug", "net10.0", projectName + ".exe"),
                XunitArguments(trxPath, filter)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(framework), framework, "expected \"mstest\" or \"xunit\"."),
        };
    }

    private static string MsTestArguments(string projectRoot, string? trxPath, string? filter)
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

        return sb.ToString();
    }

    private static string XunitArguments(string? trxPath, string? filter)
    {
        var sb = new StringBuilder();
        if (trxPath is not null)
        {
            sb.Append($"-result-trx \"{trxPath}\"");
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
