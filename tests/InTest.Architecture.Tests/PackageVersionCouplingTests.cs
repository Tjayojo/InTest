using System.Text.RegularExpressions;
using InTest.Cli;
using InTest.Cli.Commands;
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
/// <b>What is deliberately excluded, and why — updated by
/// docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's Task 1
/// (<c>[scaffold-reads-itself]</c>):</b> InTest.Runtime's version in InitCommand.cs's scaffold is
/// not a third-party dependency version — it is InTest's own packed version — so it has no
/// PackageVersion entry in Directory.Packages.props to compare against, confirmed by reading the
/// file, not assumed. Before Task 1, InitCommand.cs hardcoded a literal ("0.1.0") there, and this
/// guard compared that literal against Directory.Build.props' own <c>&lt;Version&gt;</c> — its
/// source of truth at the time. Task 1 removed the literal: a CLI built as a prerelease (say
/// 0.1.0-preview.3) now scaffolds <c>Version="{CliVersion.Current}"</c>, an interpolation rather
/// than a fixed number, so the scaffolded restore can never ask for a version that has not shipped
/// yet. There is nothing left in Directory.Build.props for that position to be compared against —
/// comparing it there would (again) be comparing against nothing, and worse, against a value that
/// no longer has anything to do with what gets scaffolded.
/// <para>
/// This class now checks InTest.Runtime two different ways, matching how CLAUDE.md's own
/// "one canonical explanation" section treats mechanism vs. proof: a fast, in-process, source-text
/// check inside <see cref="AssertScaffoldMatchesCentral"/> below, that InitCommand.cs's scaffold
/// interpolates <see cref="RuntimeVersionExpression"/> at this exact position rather than any
/// literal (catches a regression back to a hardcoded version, including a plausible-looking
/// "0.1.0", without needing to run anything); and a behavioral check,
/// <see cref="AssertInitCommandScaffoldsTheRunningRuntimeVersion"/>, that actually runs
/// <c>InitCommand.Run</c> and reads the InTest.Runtime version InTest genuinely wrote back,
/// comparing it against <see cref="CliVersion.Current"/> — the assertion Task 1 Step 2 asks for
/// verbatim ("the scaffold emits the running version"), and the only one of the two that was
/// measured to discriminate a hardcoded literal from the fix (see that method's own doc comment
/// for the experiment).
/// <c>ReadRuntimeSelfVersion</c> is still called by both test methods below, unchanged in role —
/// but Task 2 of the same plan (<c>[version-from-git]</c>) removed Directory.Build.props'
/// <c>&lt;Version&gt;</c> element entirely, which is what that call used to read, and that removal
/// was this class's own predicted acceptance signal: both test methods below failed at exactly
/// that call, for exactly that reason, the moment the element was gone — confirmed, not assumed,
/// by running this class before <see cref="ReadRuntimeSelfVersion"/> below was rewritten.
/// <see cref="ReadRuntimeSelfVersion"/> now reads <see cref="CliVersion.Current"/> instead — the
/// running assembly's own resolved version, populated at build time from the
/// <c>AssemblyInformationalVersionAttribute</c> MinVer writes, rather than a static XML value that
/// no longer exists. Its return value is still unused by <see cref="AssertScaffoldMatchesCentral"/>
/// (see that method's own parameter doc comment) — that has not changed; what changed is only
/// where the call gets its value from.
/// </para>
/// </para>
/// </summary>
[TestClass]
public class PackageVersionCouplingTests
{
    private static readonly Regex PackageReferencePattern =
        new(@"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>", RegexOptions.Compiled);

    private static readonly Regex PackageVersionPattern =
        new(@"<PackageVersion\s+Include=""([^""]+)""\s+Version=""([^""]+)""\s*/>", RegexOptions.Compiled);

    /// <summary>
    /// The adapter package ids InitCommand.cs's scaffold hardcodes a version for that are not
    /// Directory.Packages.props entries — see this class's own doc comment for why. Anything else
    /// found in a scaffold with no matching central entry is a real gap (Directory.Packages.props
    /// missing an entry, or a name that doesn't match), and must fail loudly rather than be
    /// silently skipped — see <see cref="AssertScaffoldMatchesCentral"/>.
    /// <para>
    /// <c>InTest.Runtime.MSTest</c> is the MSTest adapter's package id — the runtime split
    /// (<c>src/InTest.Runtime.MSTest/</c>) moved the scaffold's own reference from the neutral
    /// <c>InTest.Runtime</c> package to the adapter, which ProjectReferences the neutral package
    /// transitively. <c>InTest.Runtime.xUnit</c> joins it here for the same reason, added by the
    /// xUnit framework pack task (<c>src/InTest.Runtime.xUnit/</c>), and <c>InTest.Runtime.NUnit</c>
    /// joins it too, added by the NUnit framework pack task (<c>src/InTest.Runtime.NUnit/</c>) —
    /// both are checked the same way as the MSTest adapter, not as a third-party dependency,
    /// because each is InTest's own packed version rather than a Directory.Packages.props entry.
    /// All adapters still declare their types in <c>namespace InTest.Runtime</c>, so nothing else
    /// in the scaffold (e.g. <c>testBaseClass</c>) needs to change alongside this.
    /// </para>
    /// <para>
    /// <b>Not a fourth member of this coupling:</b> unlike <c>InTest.Runtime.MSTest</c> and
    /// <c>InTest.Runtime.xUnit</c>, InTest.Runtime.NUnit's own scaffolded <c>PackageReference</c>s
    /// include two genuinely third-party packages that DO have their own
    /// <c>Directory.Packages.props</c> entries and DO get checked against them by the ordinary
    /// (non-self-versioned) path in <see cref="AssertScaffoldMatchesCentral"/> below: <c>NUnit</c>
    /// and <c>NUnit3TestAdapter</c>. These two version independently of each other (4.x and 6.x)
    /// — unlike the MSTest trio this guard was originally written around, which moves in lockstep
    /// — but that needs no special-casing here: <see cref="AssertScaffoldMatchesCentral"/> already
    /// looks up and compares each matched package id against the center independently, one at a
    /// time, so two packages that happen to share a scaffold site but not a version are already
    /// checked correctly with no change to that method at all.
    /// </para>
    /// <para>
    /// A <see cref="HashSet{T}"/> rather than a single <c>const string</c>: naming exactly one
    /// adapter by scalar const was correct when only InTest.Runtime.MSTest existed, but stopped
    /// being able to express "either shipped adapter" the moment a second one joined — this is a
    /// change of shape, not merely of value, and <see cref="AssertScaffoldMatchesCentral"/> below
    /// tests membership in this set rather than equality against a single string.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> RuntimeSelfVersionedPackages =
        new(StringComparer.Ordinal) { "InTest.Runtime.MSTest", "InTest.Runtime.xUnit", "InTest.Runtime.NUnit" };

    /// <summary>
    /// The exact text InitCommand.cs's scaffold is expected to carry as the MSTest adapter's
    /// <c>Version</c> attribute value, verbatim, since <c>[scaffold-reads-itself]</c>
    /// (docs/superpowers/plans/2026-08-23-trunk-based-versioning.md, Task 1) replaced the
    /// hardcoded "0.1.0" literal with an interpolation of <see cref="CliVersion.Current"/>.
    /// InitCommand.cs's <c>.csproj</c> scaffold is a single-<c>$</c> C# interpolated raw string
    /// literal (<c>$"""..."""</c>, not <c>$$"""..."""</c>), so its interpolation holes use single
    /// braces — matching this constant, not the double-brace holes InitCommand.cs's other two
    /// scaffolded templates (<c>intest.json</c>, <c>.config/dotnet-tools.json</c>) use.
    /// </summary>
    private const string RuntimeVersionExpression = "{CliVersion.Current}";

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

    /// <summary>
    /// Reads the running InTest.Cli assembly's own resolved version — <see cref="CliVersion.Current"/>,
    /// populated at build time from the <c>AssemblyInformationalVersionAttribute</c> MinVer writes
    /// (docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's Task 2, <c>[version-from-git]</c>).
    /// Before Task 2 this read Directory.Build.props' own <c>&lt;Version&gt;</c> element instead — a
    /// static XML value that Task 2 removed entirely once MinVer took over deriving Version,
    /// PackageVersion, AssemblyVersion and InformationalVersion from git tags and commit height (see
    /// that file's own <c>[version-from-git]</c> comment). That removal was this class's own
    /// predicted acceptance signal for Task 2: both test methods below called this method and both
    /// failed here, for exactly this one reason, the moment the element was gone — confirmed by
    /// running this class before this method was rewritten, not assumed. Still called by both test
    /// methods below, unchanged in role: a live read of what the running build's version actually
    /// resolved to, so this guard cannot go stale relative to whatever mechanism computes that
    /// version, static XML or otherwise.
    /// </summary>
    private static string ReadRuntimeSelfVersion()
    {
        return CliVersion.Current;
    }

    /// <summary>
    /// Extracts every <c>&lt;PackageReference Include="..." Version="..." /&gt;</c> from
    /// <paramref name="relativePath"/> (relative to the repo root) and checks each one against
    /// <paramref name="central"/> — Directory.Packages.props for everything except a member of
    /// <see cref="RuntimeSelfVersionedPackages"/>, which is checked against
    /// <see cref="RuntimeVersionExpression"/> instead (a fixed, expected source-text shape, not a
    /// value read from <paramref name="runtimeSelfVersion"/> — see this class's own doc comment).
    /// <paramref name="runtimeSelfVersion"/> itself is unused inside this method; it stays a
    /// parameter only so both call sites keep computing it via <see cref="ReadRuntimeSelfVersion"/>
    /// — see that method's doc comment for why that call must survive on its own.
    /// </summary>
    private static void AssertScaffoldMatchesCentral(
        string relativePath,
        Dictionary<string, string> central,
        string runtimeSelfVersion)
    {
        _ = runtimeSelfVersion; // see the parameter's own doc comment above

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

            if (RuntimeSelfVersionedPackages.Contains(package))
            {
                // [scaffold-reads-itself]: the fast, in-process half of the InTest.Runtime guard.
                // There is no version literal here any more to compare against a central value —
                // what this checks instead is that the scaffold's source text still interpolates
                // CliVersion.Current at this exact position (the Version attribute
                // PackageReferencePattern just matched) rather than any literal, including a
                // plausible-looking "0.1.0". A source-text match cannot prove the scaffold
                // actually *emits* the running version, only that it is still wired to try —
                // AssertInitCommandScaffoldsTheRunningRuntimeVersion below is the behavioral half
                // that proves the rest, and the one Task 1 Step 2 was measured against.
                if (scaffoldVersion != RuntimeVersionExpression)
                {
                    offenders.Add(
                    $"{package}: {fileLabel} pins \"{scaffoldVersion}\" for its Version " +
                    $"attribute, but this must be exactly {RuntimeVersionExpression} — " +
                    "InitCommand.cs's scaffold is required to reference CliVersion.Current " +
                    "there rather than any literal version, or a CLI built as a prerelease " +
                    "scaffolds a restore that can never succeed (see [scaffold-reads-itself], " +
                    "docs/superpowers/plans/2026-08-23-trunk-based-versioning.md).");
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
                $"PackageVersionCouplingTests.RuntimeSelfVersionedPackages's reasoning and give " +
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

    /// <summary>
    /// The behavioral half of the InTest.Runtime guard, and the one Task 1 Step 2 of
    /// docs/superpowers/plans/2026-08-23-trunk-based-versioning.md actually asks for: "an
    /// assertion that the scaffold emits the running version." Runs the real <c>init</c> scaffold
    /// into a throwaway directory — the same <c>InitCommand.Run</c> call
    /// <c>InTest.Cli.Tests.InitCommandTests</c> exercises — and reads back the adapter
    /// <c>PackageReference</c>'s <c>Version</c> attribute from the <c>.csproj</c> <c>init</c>
    /// actually wrote, rather than InitCommand.cs's source text (which
    /// <see cref="InitCommandScaffoldVersionsMatchTheCenter"/> above already checks, mechanically,
    /// at the source level via <see cref="RuntimeVersionExpression"/>). This call site passes no
    /// framework argument, so it exercises <c>init</c>'s default framework (MSTest) — the
    /// resulting scaffold's adapter reference is matched by membership in
    /// <see cref="RuntimeSelfVersionedPackages"/> rather than a hardcoded literal, so this test
    /// keeps working unchanged regardless of which adapter the default happens to be.
    /// <para>
    /// <b>Proven to discriminate, not merely written and trusted.</b> Under an ordinary build,
    /// this assembly's own <see cref="CliVersion.Current"/> and the scaffold's emitted value are
    /// both "0.1.0" — so this assertion alone would pass just as happily whether InitCommand.cs
    /// called <see cref="CliVersion.Current"/> or still hardcoded "0.1.0", which is exactly the
    /// defect this guard exists to catch. The two are told apart only by building at a version
    /// other than 0.1.0. Measured directly: building InTest.Cli and this project with
    /// <c>-p:Version=9.9.9-test.1</c> and running only this test
    /// (<c>--filter FullyQualifiedName~AssertInitCommandScaffoldsTheRunningRuntimeVersion</c>)
    /// passes, with the scaffolded .csproj's InTest.Runtime reference reading "9.9.9-test.1" —
    /// confirming the assertion moved with the build rather than being trivially true at the
    /// default version. Reverting InitCommand.cs's interpolation to a hardcoded
    /// <c>Version="0.1.0"</c> literal and rebuilding at that same overridden version then fails
    /// this exact test, and only this test, with a message naming "0.1.0" against the expected
    /// "9.9.9-test.1" — proof that this specific assertion, not some other test in the suite,
    /// is what catches the regression (CLAUDE.md's own recorded lesson: a whole-suite failure
    /// does not by itself say which assertion caught it — filtering to this one test name is what
    /// makes the attribution unambiguous). Both halves of that experiment are recorded in this
    /// task's own report rather than kept as a second, permanent build configuration in this
    /// suite — the same practice this repository already follows for
    /// <c>JsonWritingOptionsGuardTests</c> and <c>TemplateEscapingGuardTests</c>: prove a guard
    /// once by mutation, then trust it rather than running the mutant forever.
    /// </para>
    /// </summary>
    [TestMethod]
    public void AssertInitCommandScaffoldsTheRunningRuntimeVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "intest-pkgcoupling-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            InitCommand.Run(root, "Scaffold.ApiTests", "spec.json").ShouldBe(0);

            var csprojPath = Path.Combine(root, "Scaffold.ApiTests.csproj");
            var csprojText = File.ReadAllText(csprojPath);

            Match? runtimeMatch = null;
            foreach (Match candidate in PackageReferencePattern.Matches(csprojText))
            {
                if (RuntimeSelfVersionedPackages.Contains(candidate.Groups[1].Value))
                {
                    runtimeMatch = candidate;
                    break;
                }
            }

            runtimeMatch.ShouldNotBeNull(
            "the scaffolded .csproj has no adapter PackageReference (InTest.Runtime.MSTest, " +
            "InTest.Runtime.xUnit or InTest.Runtime.NUnit) at all — InitCommand.cs's scaffold " +
            "shape has changed; update this test alongside it.");

            runtimeMatch!.Groups[2].Value.ShouldBe(CliVersion.Current,
            $"the scaffolded {runtimeMatch.Groups[1].Value} PackageReference must carry the " +
            "running intest's own version — see [scaffold-reads-itself].");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
