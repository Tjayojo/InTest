using System.Reflection;

namespace InTest.Cli;

/// <summary>
/// The single source for the CLI's own version. Before this, <c>InitCommand</c>,
/// <c>FixturesRepairCommand</c> and <c>GenerateCommand</c> each hardcoded their own "0.1.0"
/// literal, and nothing kept them in step — a scaffolded project or a fixture's <c>generatedBy</c>
/// could claim a version the tool does not actually have. See "Decisions this plan encodes" §5.
/// </summary>
public static class CliVersion
{
    /// <summary>
    /// What <see cref="Read"/> returns when the running binary carries no
    /// <see cref="AssemblyInformationalVersionAttribute"/> at all — a build problem, not a
    /// version. <c>generate --check</c> (v1-e) names this constant rather than the literal
    /// <c>"0.0.0"</c> so the one place that must recognise "this isn't a real version" cannot
    /// drift from the one place that produces it. See the <c>[exact-match]</c> decision section
    /// of <c>docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md</c> for why the
    /// fallback needed its own message once it became user-visible: under <c>[major-only]</c> a
    /// stray "0.0.0" was masked by the coarser comparison, but exact-match string equality
    /// surfaces it in §8's worked mismatch message, which reads as a version-drift problem it
    /// is not — telling an adopter to run <c>intest upgrade</c> there would only write "0.0.0"
    /// into <c>intestVersion</c> and permanently hide the real defect (a binary built without
    /// version metadata) rather than fixing it.
    /// </summary>
    public const string FallbackVersion = "0.0.0";

    /// <summary>
    /// The assembly's informational version with any source-control suffix
    /// (<c>+&lt;commit-sha&gt;</c>, appended by the SDK's SourceLink integration) trimmed off, so
    /// callers get a plain "0.1.0" rather than "0.1.0+649945bcf0226d5c0c8b90f2bcbee894242a157d".
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return FallbackVersion;
        }

        var plusIndex = informational.IndexOf('+');
        return plusIndex >= 0 ? informational[..plusIndex] : informational;
    }
}
