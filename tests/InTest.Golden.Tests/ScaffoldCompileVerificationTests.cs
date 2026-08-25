using InTest.Cli;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// Task 10 item 7: <c>ScaffoldStillBuildsWithNoTokenProviderRegistered</c> moved here from
/// <c>InTest.Cli.Tests.InitCommandTests</c> — before this branch, <c>InTest.Cli.Tests</c>
/// contained no out-of-process build at all; every one lived in <c>InTest.Golden.Tests</c>. Under
/// a solution-level <c>dotnet test</c> the assemblies run concurrently (Cli finishes in ~6s while
/// Golden runs ~1m40s, fully overlapping), so two independent MSBuild invocations could build
/// scaffolded projects that both <c>ProjectReference</c> the same <c>InTest.Runtime.MSTest.csproj</c>
/// simultaneously — a known source of intermittent <c>obj/</c> file-lock failures. A separate
/// class, not a method on <see cref="CompileVerificationTests"/>: that class's own
/// <c>[TestInitialize]</c> hand-writes an <c>intest.json</c> and csproj into <c>_root</c> before
/// every test runs, which this test's own call to <c>InitCommand.Run</c> would then refuse to
/// scaffold over.
/// </summary>
[TestClass]
public class ScaffoldCompileVerificationTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-scaffold-notoken-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveDirectory()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ScaffoldStillBuildsWithNoTokenProviderRegistered()
    {
        // Task 6's own point: asserting the comment exists (InitCommandTests, elsewhere) would
        // not have caught a live registration slipping in — only actually building the fresh
        // scaffold, with nothing uncommented, does. StaticTokenProvider needs a token Catalog
        // and Inventory have no source for, so a live registration here breaks Task 8 Step 5 the
        // moment it is added; this test is what would fail if that ever happened. Kept exactly
        // as it was before the move (Task 10 item 7): this is the one assertion that would catch
        // a live provider registration slipping into the scaffold.
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(0);

        var runtimeProject = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "InTest.Runtime.MSTest", "InTest.Runtime.MSTest.csproj"));
        var csprojPath = Path.Combine(_root, "Orders.ApiTests.csproj");
        var csprojText = File.ReadAllText(csprojPath);

        // The needle must track CliVersion.Current, not a hardcoded "0.1.0": before
        // docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's Task 2, InitCommand's
        // scaffold always wrote Version="0.1.0" for InTest.Runtime because Directory.Build.props
        // pinned that literal (Task 1's [scaffold-reads-itself] switched the scaffold to
        // interpolate CliVersion.Current, but Current itself still evaluated to "0.1.0" until
        // Task 2's MinVer switch made it a moving, commit-derived value) — so a hardcoded needle
        // happened to match by coincidence, not by design. Task 2 broke that coincidence: this
        // was found failing with NU1101 (InTest.Runtime unresolvable) the moment CliVersion.Current
        // stopped being "0.1.0", because String.Replace with no match found is a silent no-op —
        // exactly the failure mode CLAUDE.md warns about — and the untouched PackageReference then
        // tried to restore from nuget.org, where nothing is published. Asserted below (not just
        // interpolated) so a future scaffold-format change fails loudly here rather than silently
        // reintroducing the same NU1101 several steps downstream, pointing at the wrong cause.
        var needle = $"""<PackageReference Include="InTest.Runtime.MSTest" Version="{CliVersion.Current}" />""";
        csprojText.ShouldContain(needle, Case.Sensitive,
        "InitCommand's scaffold no longer writes InTest.Runtime.MSTest's PackageReference in the " +
        "expected shape (Include=\"InTest.Runtime.MSTest\" Version=\"{CliVersion.Current}\") — " +
        "update this test's needle alongside whatever changed, or the ProjectReference swap below " +
        "silently no-ops and this test fails downstream with a confusing NU1101 instead.");

        File.WriteAllText(csprojPath, csprojText.Replace(
        needle,
        $"""<ProjectReference Include="{runtimeProject}" />""",
        StringComparison.Ordinal));

        // The csproj copies Generated/spec-schemas.json and Generated/spec-paths.json to the
        // output directory — this test never runs `generate`, so they must exist for the build
        // to have anything to copy from.
        Directory.CreateDirectory(Path.Combine(_root, "Generated"));
        File.WriteAllText(Path.Combine(_root, "Generated", "spec-schemas.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "Generated", "spec-paths.json"), "{}");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");

        exitCode.ShouldBe(0,
        $"a fresh scaffold with no ITestTokenProvider registered must still build:{Environment.NewLine}{output}");
    }
}
