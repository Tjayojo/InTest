using System.Text.RegularExpressions;
using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// CLAUDE.md's Build configuration section states plainly that package versions are duplicated
/// by design in three places and must be changed together: <c>Directory.Packages.props</c> (the
/// central version list), the scaffolded <c>.csproj</c> string in <c>InitCommand.cs</c> (what
/// <c>init</c> writes for an adopter), and the hand-written test project in
/// <c>CompileVerificationTests.cs</c> (the only suite that proves a scaffolded project actually
/// compiles). Confirmed by reading all three: nothing under <c>tests/</c> reads
/// <c>Directory.Packages.props</c>, so a version bump in one site alone left the other two
/// silently stale, and every test still passed — <c>CompileVerificationTests</c> builds its own
/// project against its own hardcoded pins, and the scaffold just emits whatever literal
/// <c>InitCommand.cs</c> holds, regardless of what <c>Directory.Packages.props</c> says. That
/// becomes urgent the moment a bot (Dependabot or similar) starts bumping
/// <c>Directory.Packages.props</c> on a schedule: it knows nothing about the other two sites, and
/// both the old and new versions resolve fine from nuget.org, so the drift would be invisible
/// until an adopter's scaffold quietly fell behind.
/// <para>
/// This guard makes the coupling mechanical. It reads all three sites as text — mirroring
/// <see cref="NeutralityTests"/> and <c>InTest.Cli.Tests.JsonWritingOptionsGuardTests</c>'s own
/// approach, since the rule is about what the source says, not about anything observable after
/// compilation — and fails, by package name with both versions and both files, wherever a
/// hardcoded version disagrees with its central counterpart.
/// </para>
/// <para>
/// <b>What is genuinely coupled, established by reading all three files rather than assumed:</b>
/// Directory.Packages.props' first ItemGroup (the one InTest.Cli/InTest.Runtime actually
/// restore) lists MSTest.TestFramework, MSTest.TestAdapter, MSTest.Analyzers,
/// Microsoft.NET.Test.Sdk and Shouldly, all at explicit versions. InitCommand.cs's scaffold
/// hardcodes all five of those at matching versions, plus a sixth PackageReference —
/// InTest.Runtime — that has no counterpart in Directory.Packages.props at all.
/// CompileVerificationTests.cs's hand-written project hardcodes only three of the five —
/// MSTest.TestFramework, MSTest.TestAdapter, Microsoft.NET.Test.Sdk — because that project never
/// asserts with Shouldly or runs analyzers against the generated code it compiles; it references
/// InTest.Runtime too, but via <c>ProjectReference</c> to the local build, not
/// <c>PackageReference</c>, so no version literal exists there to drift. This guard does not
/// force a false three-way requirement that every site declare every package — it walks whatever
/// PackageReference each scaffold site actually declares and checks it against the center.
/// </para>
/// <para>
/// <b>What is deliberately excluded, and why:</b> InTest.Runtime's version in InitCommand.cs's
/// scaffold (currently 0.1.0) is not a third-party dependency version — it is InTest's own packed
/// <c>&lt;Version&gt;</c>, pinned in Directory.Build.props precisely so a scaffolded restore can
/// resolve <c>InTest.Runtime</c> at all (Directory.Build.props' own comment: "the scaffolded
/// project pins InTest.Runtime 0.1.0, so the packages must pack as 0.1.0"). It has no
/// PackageVersion entry in Directory.Packages.props to compare against — confirmed by reading the
/// file, not assumed — so comparing it there would be comparing against nothing. This guard
/// instead compares InTest.Runtime's scaffolded version against Directory.Build.props' own
/// <c>&lt;Version&gt;</c>, which is its actual source of truth, so InTest.Runtime stays coupled
/// too rather than silently unchecked. Getting this wrong (routing InTest.Runtime through the
/// Directory.Packages.props comparison instead) would make the guard fail permanently against a
/// file that was never wrong — worse than no guard, because it teaches the next person to ignore
/// this test's red.
/// </para>
/// </summary>
[TestClass]
public class PackageVersionCouplingTests
{
    private static readonly Regex PackageReferencePattern =
        new(@"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>", RegexOptions.Compiled);

    private static readonly Regex PackageVersionPattern =
        new(@"<PackageVersion\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>", RegexOptions.Compiled);

    private static readonly Regex VersionElementPattern =
        new(@"<Version>([^<]+)</Version>", RegexOptions.Compiled);

    /// <summary>
    /// The one package InitCommand.cs's scaffold hardcodes a version for that is not a
    /// Directory.Packages.props entry — see this class's own doc comment for why. Anything else
    /// found in a scaffold with no matching central entry is a real gap (Directory.Packages.props
    /// missing an entry, or a name that doesn't match), and must fail loudly rather than be
    /// silently skipped — see <see cref="AssertScaffoldMatchesCentral"/>.
    /// </summary>
    private const string RuntimeSelfVersionedPackage = "InTest.Runtime";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InTest.sln")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("Could not locate the repository root (InTest.sln).");
        return dir!.FullName;
    }

    private static Dictionary<string, string> ReadCentralPackageVersions()
    {
        var path = Path.Combine(RepoRoot(), "Directory.Packages.props");
        var text = File.ReadAllText(path);
        var matches = PackageVersionPattern.Matches(text);

        // If this is ever zero, the regex has stopped matching Directory.Packages.props' actual
        // syntax — that is this guard silently going blind, not a clean bill of health (mirrors
        // TemplateEscapingGuardTests' and JsonWritingOptionsGuardTests' own zero-match checks).
        matches.Count.ShouldBeGreaterThan(0,
            "no <PackageVersion Include=\"...\" Version=\"...\" /> entries were found in " +
            "Directory.Packages.props. Either its format changed and " +
            "PackageVersionCouplingTests.PackageVersionPattern no longer matches it, or this " +
            "guard is passing vacuously — do not leave it silently disabled.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in matches)
        {
            // Directory.Packages.props lists each package once; a duplicate Include would already
            // be a NuGet restore error (NU1009) before this test ever runs, so last-write-wins
            // here is fine — it can never actually happen against a repo that builds.
            result[match.Groups[1].Value] = match.Groups[2].Value;
        }
        return result;
    }

    private static string ReadRuntimeSelfVersion()
    {
        var path = Path.Combine(RepoRoot(), "Directory.Build.props");
        var text = File.ReadAllText(path);
        var match = VersionElementPattern.Match(text);
        match.Success.ShouldBeTrue(
            "no <Version>...</Version> element was found in Directory.Build.props. Either its " +
            "format changed and PackageVersionCouplingTests.VersionElementPattern no longer " +
            "matches it, or the pin InitCommand.cs's scaffold relies on for InTest.Runtime has " +
            "been removed.");
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Extracts every <c>&lt;PackageReference Include="..." Version="..." /&gt;</c> from
    /// <paramref name="relativePath"/> (relative to the repo root) and checks each one against
    /// <paramref name="central"/> — Directory.Packages.props for everything except
    /// <see cref="RuntimeSelfVersionedPackage"/>, which is checked against
    /// <paramref name="runtimeSelfVersion"/> (Directory.Build.props' own Version) instead. See
    /// this class's doc comment for why that one package takes a different path.
    /// </summary>
    private static void AssertScaffoldMatchesCentral(
        string relativePath,
        Dictionary<string, string> central,
        string runtimeSelfVersion)
    {
        var fileLabel = Path.GetFileName(relativePath);
        var path = Path.Combine(RepoRoot(), relativePath);
        var text = File.ReadAllText(path);
        var matches = PackageReferencePattern.Matches(text);

        // Same anti-vacuity reasoning as ReadCentralPackageVersions above: a scaffold string that
        // stops matching this pattern (reformatted, attributes reordered, etc.) must fail loudly,
        // not silently stop checking anything.
        matches.Count.ShouldBeGreaterThan(0,
            $"no <PackageReference Include=\"...\" Version=\"...\" /> entries were found in " +
            $"{fileLabel}. Either its scaffold string's format changed and " +
            $"PackageVersionCouplingTests.PackageReferencePattern no longer matches it, or this " +
            $"guard is passing vacuously — do not leave it silently disabled.");

        var offenders = new List<string>();

        foreach (Match match in matches)
        {
            var package = match.Groups[1].Value;
            var scaffoldVersion = match.Groups[2].Value;

            if (package == RuntimeSelfVersionedPackage)
            {
                if (scaffoldVersion != runtimeSelfVersion)
                {
                    offenders.Add(
                        $"{package}: {fileLabel} pins {scaffoldVersion}, but Directory.Build.props' " +
                        $"<Version> — InTest's own packed version, which is what the scaffolded " +
                        $"restore actually needs to resolve — is {runtimeSelfVersion}.");
                }
                continue;
            }

            if (!central.TryGetValue(package, out var centralVersion))
            {
                offenders.Add(
                    $"{package}: {fileLabel} hardcodes version {scaffoldVersion}, but " +
                    $"Directory.Packages.props has no PackageVersion entry for it at all. Either " +
                    $"add one, or — if this package is deliberately not centrally versioned, the " +
                    $"way InTest.Runtime is — add it to " +
                    $"PackageVersionCouplingTests.RuntimeSelfVersionedPackage's reasoning and give " +
                    $"it the same treatment.");
                continue;
            }

            if (scaffoldVersion != centralVersion)
            {
                offenders.Add(
                    $"{package}: Directory.Packages.props pins {centralVersion}, but {fileLabel} " +
                    $"pins {scaffoldVersion}.");
            }
        }

        offenders.ShouldBeEmpty(
            $"{fileLabel} and Directory.Packages.props disagree on package versions that CLAUDE.md's " +
            "Build configuration section says must be changed together:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void InitCommandScaffoldVersionsMatchTheCenter()
    {
        var central = ReadCentralPackageVersions();
        var runtimeSelfVersion = ReadRuntimeSelfVersion();
        AssertScaffoldMatchesCentral(
            Path.Combine("src", "InTest.Cli", "Commands", "InitCommand.cs"), central, runtimeSelfVersion);
    }

    [TestMethod]
    public void CompileVerificationTestsScaffoldVersionsMatchTheCenter()
    {
        var central = ReadCentralPackageVersions();
        var runtimeSelfVersion = ReadRuntimeSelfVersion();
        AssertScaffoldMatchesCentral(
            Path.Combine("tests", "InTest.Golden.Tests", "CompileVerificationTests.cs"), central, runtimeSelfVersion);
    }
}
