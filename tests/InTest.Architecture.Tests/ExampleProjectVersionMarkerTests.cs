using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// Guards the defect an adopter hit first, on the published packages, before any automated test
/// in this repo caught it: <c>examples/Catalog.ApiTests</c> and <c>examples/Orders.ApiTests</c>
/// each carry three independent version markers — <c>intest.json</c>'s <c>intestVersion</c>,
/// <c>.config/dotnet-tools.json</c>'s <c>intest.cli</c> pin, and the <c>.csproj</c>'s
/// <c>InTest.Runtime</c> <c>PackageReference</c> — and nothing forced them to agree.
/// <c>PackageVersionCouplingTests</c> guards the scaffold *template* those three markers are
/// generated from (<c>InitCommand.cs</c>'s literal .csproj/.json strings); it reads no file under
/// <c>examples/</c> at all, so a committed example can drift from what a fresh <c>init</c> would
/// produce without either guard noticing. Measured directly (docs/v0-acceptance.md, "adopter
/// dry run against the published packages"): both examples had <c>intestVersion</c> and the
/// <c>intest.cli</c> pin hand-edited to <c>0.1.0</c> after the <c>.csproj</c>'s
/// <c>InTest.Runtime</c> reference was corrected to the real first publish,
/// <c>0.1.0-preview.1</c> — <c>dotnet tool restore</c> failed outright (no such version on
/// nuget.org), and even past that, <c>generate --check</c>'s <c>[exact-match]</c> gate would have
/// exited 4 the moment the CLI pin resolved to anything.
/// <para>
/// <b>Internal consistency, not "matches nuget.org", and that trade is deliberate.</b> The
/// obviously stronger check would confirm all three markers name a version that is actually
/// published — but that means a network call from every CI run of this suite, against a registry
/// this repo does not control, for a fact (what shipped today) that changes independently of any
/// commit. A flaky or offline registry would then fail a test that has nothing to do with the
/// code under test. What actually caused this incident was never "unpublished version" in
/// isolation — <c>InTest.Runtime</c>'s pin was correct the whole time — it was the three markers
/// disagreeing with <i>each other</i>, which is exactly what a hand-edit to only one or two of
/// them produces and what <c>intest upgrade</c> (<c>UpgradeCommand.RunAsync</c>) exists to
/// prevent by moving <c>intestVersion</c> and the <c>intest.cli</c> pin together in one call. This
/// guard checks that invariant — cheap, offline, and deterministic — and leaves "is this version
/// actually on nuget.org" to the adoption dry run itself, which already exercises that question
/// for real (docs/v0-acceptance.md).
/// </para>
/// <para>
/// <b>Proven to fire, not merely written and trusted</b> (mirrors the practice
/// <c>PackageVersionCouplingTests</c>' own doc comment records for
/// <c>AssertInitCommandScaffoldsTheRunningRuntimeVersion</c>): reverting
/// <c>examples/Orders.ApiTests/intest.json</c>'s <c>intestVersion</c> from
/// <c>0.1.0-preview.1</c> back to <c>0.1.0</c> and running only this class
/// (<c>--filter FullyQualifiedName~ExampleProjectVersionMarkerTests</c>) fails
/// <see cref="ThreeVersionMarkersAgreeAcrossEveryExample"/> with a message naming
/// <c>Orders.ApiTests</c>, <c>0.1.0</c> and <c>0.1.0-preview.1</c> by name; reverting the edit
/// makes it pass again. Not left as a permanent second build configuration — proven once by
/// mutation, then trusted, the same practice this repository already follows for
/// <c>TemplateEscapingGuardTests</c> and <c>JsonWritingOptionsGuardTests</c>.
/// </para>
/// </summary>
[TestClass]
public class ExampleProjectVersionMarkerTests
{
    // Deliberately matches either id: examples/ stays pinned to the bare InTest.Runtime at
    // 0.1.0-preview.1 (see each example's own .csproj comment) because that is the *published*
    // version examples actually restore from nuget.org, and InTest.Runtime.MSTest 0.1.0-preview.1
    // does not exist yet — it ships with the next release, alongside the runtime split
    // (src/InTest.Runtime.MSTest/). Repointing examples/ to the adapter id today would break
    // `dotnet restore` for anyone running them. Once the adapter is published, migrating each
    // example is a one-line PackageReference id edit rather than a surprise red test here — do NOT
    // "fix" that migration by touching examples/ preemptively (see CLAUDE.md's Task 8 notes).
    // The regeneration that must accompany that id edit is a release-checklist step, not a test —
    // see CONTRIBUTING.md's publishing checklist for why it cannot be enforced here.
    private static readonly Regex RuntimePackageReferencePattern =
        new(@"<PackageReference\s+Include=""InTest\.Runtime(?:\.MSTest)?""\s+Version=""([^""]+)""\s*/>", RegexOptions.Compiled);

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

    /// <summary>
    /// Every immediate subdirectory of <c>examples/</c> that carries its own <c>intest.json</c> —
    /// discovered rather than hardcoded, so a third committed example added later is picked up
    /// without anyone remembering to extend this list. <c>examples/Directory.Packages.props</c>
    /// sits alongside these directories as a file, not a directory, so it is excluded by
    /// construction rather than needing its own filter.
    /// </summary>
    private static string[] ExampleProjectDirectories()
    {
        var examplesRoot = Path.Combine(RepoRoot(), "examples");
        return Directory.GetDirectories(examplesRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "intest.json")))
            .OrderBy(dir => dir, StringComparer.Ordinal)
            .ToArray();
    }

    [TestMethod]
    public void ThreeVersionMarkersAgreeAcrossEveryExample()
    {
        var exampleDirs = ExampleProjectDirectories();

        // Same anti-vacuity reasoning PackageVersionCouplingTests and NeutralityTests both apply:
        // a guard that silently checks zero examples is passing for the wrong reason, not because
        // nothing is wrong.
        exampleDirs.Length.ShouldBeGreaterThan(0,
        "no example project directories were found under examples/ (a directory containing " +
        "its own intest.json). Either examples/ was reorganised and this guard's discovery " +
        "no longer matches its shape, or every committed example was removed — either way " +
        "this must not pass silently.");

        var offenders = new List<string>();

        foreach (var dir in exampleDirs)
        {
            var exampleName = Path.GetFileName(dir);

            var intestJsonPath = Path.Combine(dir, "intest.json");
            using var intestJsonDoc = JsonDocument.Parse(File.ReadAllText(intestJsonPath));
            var intestVersion = intestJsonDoc.RootElement.GetProperty("intestVersion").GetString();

            var dotnetToolsPath = Path.Combine(dir, ".config", "dotnet-tools.json");
            if (!File.Exists(dotnetToolsPath))
            {
                offenders.Add($"{exampleName}: no .config/dotnet-tools.json found at '{dotnetToolsPath}'.");
                continue;
            }

            using var dotnetToolsDoc = JsonDocument.Parse(File.ReadAllText(dotnetToolsPath));
            if (!dotnetToolsDoc.RootElement.TryGetProperty("tools", out var tools) ||
                !tools.TryGetProperty("intest.cli", out var intestCli) ||
                !intestCli.TryGetProperty("version", out var cliVersionElement))
            {
                offenders.Add(
                $"{exampleName}: .config/dotnet-tools.json does not pin \"intest.cli\" under " +
                "\"tools\" with a \"version\" field.");
                continue;
            }
            var cliVersion = cliVersionElement.GetString();

            var csprojFiles = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length != 1)
            {
                offenders.Add(
                $"{exampleName}: expected exactly one .csproj, found {csprojFiles.Length}.");
                continue;
            }

            var csprojText = File.ReadAllText(csprojFiles[0]);
            var match = RuntimePackageReferencePattern.Match(csprojText);
            if (!match.Success)
            {
                offenders.Add(
                $"{exampleName}: no <PackageReference Include=\"InTest.Runtime\" (or " +
                "\"InTest.Runtime.MSTest\") Version=\"...\" /> found in " +
                $"{Path.GetFileName(csprojFiles[0])} — either the scaffold shape changed or this " +
                "example lost its runtime reference.");
                continue;
            }
            var runtimeVersion = match.Groups[1].Value;

            if (intestVersion != cliVersion)
            {
                offenders.Add(
                $"{exampleName}: intest.json's intestVersion (\"{intestVersion}\") disagrees " +
                $"with .config/dotnet-tools.json's intest.cli pin (\"{cliVersion}\"). Run " +
                $"`intest upgrade --project examples/{exampleName}` to move both together — " +
                "never hand-edit either marker (see UpgradeCommand's own doc comment for why).");
            }

            if (intestVersion != runtimeVersion)
            {
                offenders.Add(
                $"{exampleName}: intest.json's intestVersion (\"{intestVersion}\") disagrees " +
                $"with the InTest.Runtime PackageReference pinned in the .csproj " +
                $"(\"{runtimeVersion}\"). `intest upgrade` bumps intestVersion and the " +
                "dotnet-tools.json pin together but deliberately never rewrites the .csproj " +
                "([prerelease-reference-migration], UpgradeCommand.DetectRuntimeReferenceMismatch) " +
                $"— change Version=\"{runtimeVersion}\" to \"{intestVersion}\" by hand once " +
                "you have confirmed that version is actually published.");
            }
        }

        offenders.ShouldBeEmpty(
        "Committed example projects' version markers must all agree — see this class's own " +
        "doc comment for the incident this guards against:" + Environment.NewLine +
        string.Join(Environment.NewLine, offenders));
    }
}
