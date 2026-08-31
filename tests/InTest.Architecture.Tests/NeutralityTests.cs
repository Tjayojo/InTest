using System.Xml.Linq;
using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// §3 requires the neutral layer (src/InTest.Runtime/) to stay free of any test framework, so
/// xUnit and NUnit adapters can be added later as siblings of InTest.Runtime.MSTest rather than
/// forcing a rewrite. Two layers of guard now enforce that, deliberately overlapping rather than
/// deduplicated, because each catches a different regression:
/// <para>
/// <b>Layer 1 — the compiler, primary.</b> src/InTest.Runtime.csproj carries no MSTest
/// reference, so it gets no implicit <c>global using
/// Microsoft.VisualStudio.TestTools.UnitTesting;</c>. A neutral file that names an MSTest type
/// (accidentally reintroducing a dependency the code, not just the manifest, actually uses) fails
/// the build outright, immediately, with a compiler error at the exact line — this is the
/// strongest guard available and it requires nothing from this test class to work.
/// </para>
/// <para>
/// <b>Layer 2 — these tests, secondary, and covering what the compiler cannot see.</b> The
/// compiler only reacts to source that references a member of the forbidden namespace; it says
/// nothing about the project *manifest*. <see cref="NeutralPackageDeclaresNoTestFrameworkDependency"/>
/// below is the one that matters most: someone adds
/// <c>&lt;PackageReference Include="MSTest.TestFramework" /&gt;</c> back to
/// src/InTest.Runtime.csproj to silence some unrelated build error, and the project still
/// compiles fine — nothing in the source *uses* MSTest, so there is no CS0246 to trip over — but
/// InTest.Runtime, the package every future test-framework adapter is supposed to depend on
/// without inheriting MSTest, silently regains an MSTest dependency again. Only a test that reads
/// the .csproj can see that; the compiler has no opinion on unused references.
/// <see cref="AdapterPackageDeclaresItsTestFramework"/> is that guard's mirror image: without it,
/// emptying or deleting src/InTest.Runtime.MSTest/ (or, since the xUnit and NUnit framework pack
/// tasks, src/InTest.Runtime.xUnit/ or src/InTest.Runtime.NUnit/) would make the neutral-package
/// guard pass trivially (a test-framework reference genuinely nowhere in the repo) while the
/// product itself is broken, because nothing ships that adapter for InTest.Runtime any more. It is
/// parameterised over every adapter via <c>[DataRow]</c> precisely so it cannot go blind to one of
/// them the way a single hardcoded csproj path once did.
/// <see cref="NeutralSourcesDoNotReferenceMSTest"/>, kept below, is a fast, legible secondary of
/// its own within this same layer: it fails with one sentence naming §3 instead of forty
/// CS0246s, and — unlike the compiler — it still catches a commented-out <c>using</c> or a stale
/// doc <c>cref</c> naming the forbidden namespace, because no project in this repo sets
/// <c>GenerateDocumentationFile</c>, so <c>CS1574</c> (the compiler's own check for a
/// <c>cref</c> that fails to resolve) never fires here at all.
/// </para>
/// <para>
/// <b>Layer 3 — not yet built.</b> A later task adds scripts/ci/pack-and-verify.ps1, which packs
/// the real .nupkg and inspects its .nuspec — the only layer that can see a *transitive* leak
/// (an innocent-looking neutral dependency that itself depends on a test framework two hops
/// away, which neither the compiler nor a direct-PackageReference scan of one .csproj can catch).
/// </para>
///
/// The neutral sources live directly under src/InTest.Runtime/ — the assembly itself is
/// the neutrality boundary now that InTest.Runtime.MSTest exists as its own sibling
/// project. TestHost.cs and ApiTestBase.cs, the two files that legitimately reference
/// MSTest, live under src/InTest.Runtime.MSTest/ and are outside the recursive scan of
/// src/InTest.Runtime/ below, so every file this test finds is neutral by construction —
/// no exclusion is needed for them.
/// </summary>
[TestClass]
public class NeutralityTests
{
    private const string ForbiddenNamespace = "Microsoft.VisualStudio.TestTools.UnitTesting";

    private static string RuntimeDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InTest.sln")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("Could not locate the repository root (InTest.sln).");
        return Path.Combine(dir!.FullName, "src", "InTest.Runtime");
    }

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
    /// Reads every <c>&lt;PackageReference Include="..."/&gt;</c> id out of a .csproj file.
    /// Parsed as XML via <see cref="XDocument"/> rather than by the regex-over-source-text
    /// approach <c>PackageVersionCouplingTests</c> uses, deliberately: that class's regexes scan
    /// C# string *literals* inside InitCommand.cs — text that only resembles a .csproj and is
    /// never itself parsed as one — where a real XML parser would be the wrong tool entirely. The
    /// two files this method reads are actual, well-formed project files MSBuild itself parses as
    /// XML, so structural parsing is both the natural fit and more robust than a regex here: it
    /// is indifferent to attribute order, self-closing vs. open/close element form, and comments
    /// interleaved between elements, none of which a hand-rolled regex can be trusted to survive
    /// unchanged.
    /// </summary>
    private static List<string> ReadPackageReferenceIds(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    [TestMethod]
    public void NeutralSourcesDoNotReferenceMSTest()
    {
        var offenders = new List<string>();

        // bin/ and obj/ are build output, not source: this project no longer references
        // MSTest.TestFramework at all (that reference lives in the sibling
        // InTest.Runtime.MSTest project now), but stale build output from before the split
        // could still be sitting in these folders on a developer machine, and build output
        // should never count as source regardless.
        var binSubfolder = Path.Combine(RuntimeDirectory(), "bin") + Path.DirectorySeparatorChar;
        var objSubfolder = Path.Combine(RuntimeDirectory(), "obj") + Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(RuntimeDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.StartsWith(binSubfolder, StringComparison.Ordinal) ||
                file.StartsWith(objSubfolder, StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains(ForbiddenNamespace, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.ShouldBeEmpty(
        $"These files are under src/InTest.Runtime/ but reference " +
        $"{ForbiddenNamespace}. Move them to src/InTest.Runtime.MSTest/, or remove the " +
        "dependency. See §3.");
    }

    [TestMethod]
    public void NeutralDirectoryIsNotEmpty()
    {
        Directory.EnumerateFiles(RuntimeDirectory(), "*.cs", SearchOption.TopDirectoryOnly)
            .ShouldNotBeEmpty("The neutrality test would pass vacuously against an empty directory.");
    }

    /// <summary>
    /// The primary guard described in this class's own doc comment (Layer 2): reads
    /// src/InTest.Runtime/InTest.Runtime.csproj as XML and fails if it declares a
    /// <c>PackageReference</c> to any test framework — the regression the compiler cannot see,
    /// because a project can carry an unused <c>PackageReference</c> and still compile cleanly.
    /// <para>
    /// The forbidden set is deliberately a family of prefixes rather than a handful of exact
    /// package ids, because the point is to catch *any* test framework landing here, including
    /// one this repository has never referenced before: <c>MSTest.</c> (TestFramework,
    /// TestAdapter, Analyzers — anything under the MSTest family), <c>xunit</c> (xunit itself,
    /// xunit.core, xunit.runner.visualstudio, ...), <c>NUnit</c> (NUnit itself,
    /// NUnit3TestAdapter, ...), and the one exact id <c>Microsoft.NET.Test.Sdk</c>, which is not
    /// a framework by name but is meaningless outside a test project and is exactly as strong a
    /// signal that MSTest/xUnit/NUnit test infrastructure has leaked into the neutral layer.
    /// </para>
    /// <para>
    /// <b>Assertion libraries (Shouldly, FluentAssertions) are deliberately NOT in this forbidden
    /// set.</b> The neutral layer already throws its own <c>ContractAssertionException</c> for
    /// its runtime-contract checks (see <c>Neutral/</c>), which is a design choice this test does
    /// not second-guess — but an assertion library is not a *test framework*: it supplies
    /// <c>Should*</c>/<c>Assert*</c> extension methods usable from ordinary application code, has
    /// no test discovery, no test runner integration, and does not itself drag in MSTest, xUnit,
    /// or NUnit. Forbidding it here would conflate "this package happens to be popular in test
    /// projects" with "this package makes InTest.Runtime a test-framework-coupled package," which
    /// is the actual defect §3 exists to prevent. If a future change gives the neutral layer a
    /// genuine reason to reference Shouldly or FluentAssertions, this guard should not be the
    /// thing standing in the way — the reasoning would need revisiting on its own merits, not as
    /// a side effect of this list.
    /// </para>
    /// <para>
    /// <b>Anti-vacuity:</b> asserts at least one <c>PackageReference</c> was found before checking
    /// any of them against the forbidden set — the same reasoning
    /// <c>PackageVersionCouplingTests.ReadCentralPackageVersions</c> already documents for its own
    /// zero-match guard: a parsing bug that silently returns an empty list would otherwise make
    /// this test pass forever while checking nothing at all.
    /// </para>
    /// </summary>
    [TestMethod]
    public void NeutralPackageDeclaresNoTestFrameworkDependency()
    {
        const string relativePath = "src/InTest.Runtime/InTest.Runtime.csproj";
        var csprojPath = Path.Combine(RepoRoot(), "src", "InTest.Runtime", "InTest.Runtime.csproj");
        var packageIds = ReadPackageReferenceIds(csprojPath);

        packageIds.ShouldNotBeEmpty(
        $"No <PackageReference> elements were found in {relativePath} at all. Either its XML " +
        "shape changed and NeutralityTests.ReadPackageReferenceIds no longer finds them, or " +
        "this guard is passing vacuously — do not leave it silently disabled.");

        foreach (var packageId in packageIds)
        {
            var isForbidden =
                packageId.StartsWith("MSTest.", StringComparison.OrdinalIgnoreCase) ||
                packageId.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                packageId.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(packageId, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase);

            isForbidden.ShouldBeFalse(
            $"{relativePath} declares a PackageReference to \"{packageId}\", a test-framework " +
            $"package. §3 requires src/InTest.Runtime/ to stay neutral of any test framework so " +
            "xUnit and NUnit adapters can be added later without a rewrite. Adding a test " +
            "framework dependency here re-breaks every consumer of the neutral InTest.Runtime " +
            "package: it would start receiving that test framework transitively again, the exact " +
            "defect the InTest.Runtime / InTest.Runtime.MSTest split exists to prevent. Remove " +
            "the reference, or move whatever needed it to src/InTest.Runtime.MSTest/.");
        }
    }

    /// <summary>
    /// The mirror image of <see cref="NeutralPackageDeclaresNoTestFrameworkDependency"/>, needed
    /// so that guard cannot pass vacuously: without this test, deleting or emptying an adapter
    /// project directory (or quietly dropping its test-framework reference, or its
    /// <c>ProjectReference</c> back to the neutral project) would leave "no test framework
    /// PackageReference anywhere named InTest.Runtime.csproj" trivially true while the shipped
    /// product no longer has that adapter at all — a real regression the neutral-package guard
    /// alone cannot see, because it only ever looks at one file.
    /// <para>
    /// Parameterised over every adapter that ships alongside the neutral InTest.Runtime package —
    /// InTest.Runtime.MSTest, InTest.Runtime.xUnit and InTest.Runtime.NUnit — via <c>[DataRow]</c>,
    /// rather than one hardcoded csproj path. Before the xUnit framework pack task, this method
    /// named only src/InTest.Runtime.MSTest/InTest.Runtime.MSTest.csproj directly; that hardcoding
    /// would have passed vacuously for InTest.Runtime.xUnit by simply never looking at it —
    /// deleting or breaking that adapter alone would have left this guard, and the whole suite,
    /// green. A per-adapter <c>[DataRow]</c> is what let the NUnit framework pack task add its own
    /// row (rather than a fourth near-identical test method) and made a broken or missing
    /// InTest.Runtime.NUnit fail the same way today's guard already fails for a broken MSTest or
    /// xUnit adapter: by omission from this list, not by silent vacuity.
    /// </para>
    /// <para>
    /// Checks both halves of what makes each project an adapter rather than a copy: it must
    /// declare its own test framework's package as a <c>PackageReference</c> (so it is genuinely
    /// coupled to that framework), and it must declare a <c>ProjectReference</c> back to
    /// ../InTest.Runtime/InTest.Runtime.csproj (so it is genuinely an *adapter* for the neutral
    /// package rather than an unrelated test-framework-coupled project that happens to share a
    /// naming convention).
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("InTest.Runtime.MSTest", "MSTest.TestFramework", DisplayName = "InTest.Runtime.MSTest / MSTest.TestFramework")]
    [DataRow("InTest.Runtime.xUnit", "xunit.v3.extensibility.core", DisplayName = "InTest.Runtime.xUnit / xunit.v3.extensibility.core")]
    [DataRow("InTest.Runtime.NUnit", "NUnit", DisplayName = "InTest.Runtime.NUnit / NUnit")]
    public void AdapterPackageDeclaresItsTestFramework(string adapterProjectName, string testFrameworkPackageId)
    {
        var relativePath = $"src/{adapterProjectName}/{adapterProjectName}.csproj";
        var csprojPath = Path.Combine(RepoRoot(), "src", adapterProjectName, $"{adapterProjectName}.csproj");

        var packageIds = ReadPackageReferenceIds(csprojPath);
        packageIds.ShouldNotBeEmpty(
        $"No <PackageReference> elements were found in {relativePath} at all. Either its XML " +
        "shape changed and NeutralityTests.ReadPackageReferenceIds no longer finds them, or " +
        "this guard is passing vacuously — do not leave it silently disabled.");

        packageIds.ShouldContain(testFrameworkPackageId,
        $"{relativePath} no longer declares a PackageReference to {testFrameworkPackageId}. " +
        $"Without it this project is not a {testFrameworkPackageId}-based adapter at all, which " +
        "would let NeutralPackageDeclaresNoTestFrameworkDependency pass vacuously — the neutral " +
        "package having no test-framework reference is meaningless if nothing else in the repo " +
        "has one either.");

        var doc = XDocument.Load(csprojPath);
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => include!)
            .ToList();

        var referencesNeutralProject = projectReferences.Any(include =>
            include.Replace('\\', '/').EndsWith(
            "InTest.Runtime/InTest.Runtime.csproj", StringComparison.OrdinalIgnoreCase));

        referencesNeutralProject.ShouldBeTrue(
        $"{relativePath} no longer has a ProjectReference to ../InTest.Runtime/" +
        "InTest.Runtime.csproj. Without it this project is not an adapter for the neutral " +
        "package at all, which would let NeutralPackageDeclaresNoTestFrameworkDependency pass " +
        $"vacuously in the same way a missing {testFrameworkPackageId} reference would.");
    }
}
