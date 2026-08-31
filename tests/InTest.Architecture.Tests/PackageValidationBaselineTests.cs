using System.Xml.Linq;
using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// Guards <c>[package-validation-baseline]</c> (<c>Directory.Build.props</c>'s own comment next to
/// <c>EnablePackageValidation</c>): <c>EnablePackageValidation</c> alone does not compare anything.
/// API Compat only runs against a *previously published version*, named by
/// <c>PackageValidationBaselineVersion</c> — and that property was missing from this repository
/// entirely from the day <c>EnablePackageValidation</c> was turned on until this guard's own task,
/// which is exactly the silent-degradation shape this repository already guards against elsewhere
/// (<see cref="NeutralityTests"/>, <see cref="ExampleProjectVersionMarkerTests"/>,
/// <c>scripts/ci/examples.ps1</c>'s discovery-not-listing reasoning): a property whose absence
/// produces no error, no warning, and a build that stays green — package validation was running,
/// reported success, and had never once compared the API surface against anything.
/// <para>
/// If someone deletes or blanks <c>PackageValidationBaselineVersion</c> from
/// <c>Directory.Build.props</c> to "fix" an unrelated build problem, or a future refactor of that
/// file drops it by accident, every subsequent <c>dotnet pack</c> keeps succeeding — API Compat
/// silently has nothing to diff against — and nobody notices until an actual unintentional breaking
/// change ships past it uncaught. This test is the only thing that turns that specific omission
/// into a red test rather than a silent no-op.
/// </para>
/// <para>
/// <b>Why this reads <c>Directory.Build.props</c> as text/XML rather than invoking MSBuild to
/// evaluate the property:</b> mirrors <see cref="NeutralityTests"/>'s own reasoning for reading
/// <c>.csproj</c> files directly — the rule under test is about what the repository's build
/// configuration *says*, not about a runtime-observable side effect of a real pack. Actually
/// running <c>dotnet pack</c> here (to prove API Compat truly executes) would duplicate what
/// <c>scripts/ci/pack-and-verify.ps1</c> already does — packing all five packages for real and
/// failing loudly on any <c>CP00xx</c>/<c>NU5xxx</c> error — at the cost of a full restore-and-pack
/// cycle inside what is otherwise a fast, in-process suite. This test's job is narrower and
/// cheaper: confirm the one property that makes that comparison possible at all is still present
/// and non-empty.
/// </para>
/// <para>
/// <b>Proven to fire, not merely written and trusted</b> (the same practice
/// <see cref="ExampleProjectVersionMarkerTests"/>'s own doc comment records): blanking
/// <c>Directory.Build.props</c>'s <c>&lt;PackageValidationBaselineVersion&gt;</c> element to empty,
/// and separately deleting the element outright, both fail
/// <see cref="PackageValidationBaselineVersionIsSetToANonEmptyValue"/> — restoring either edit
/// makes it pass again. See this task's own report for the exact before/after run.
/// </para>
/// </summary>
[TestClass]
public class PackageValidationBaselineTests
{
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

    [TestMethod]
    public void PackageValidationBaselineVersionIsSetToANonEmptyValue()
    {
        var propsPath = Path.Combine(RepoRoot(), "Directory.Build.props");
        File.Exists(propsPath).ShouldBeTrue($"Expected to find {propsPath}.");

        var doc = XDocument.Load(propsPath);

        // EnablePackageValidation is the companion property this guard exists to keep honest: it
        // is what actually turns API Compat on for the four class libraries (a no-op for
        // InTest.Cli — see the element's own comment). Asserting it is still present and "true"
        // first means a failure of *this* test names the right property instead of surfacing as a
        // confusing failure of the baseline check below against a props file that no longer
        // enables validation at all.
        var enableElement = doc.Descendants("EnablePackageValidation").FirstOrDefault();
        enableElement.ShouldNotBeNull(
        "Directory.Build.props no longer declares <EnablePackageValidation>. Package " +
        "validation guards the four class-library packages (InTest.Runtime, " +
        "InTest.Runtime.MSTest, InTest.Runtime.xUnit, InTest.Runtime.NUnit) against " +
        "unintentional breaking API changes -- see [package-validation-baseline] in " +
        "Directory.Build.props.");
        enableElement!.Value.ShouldBe("true",
        "Directory.Build.props's <EnablePackageValidation> is no longer \"true\" -- package " +
        "validation is now disabled for every class library. See [package-validation-baseline] " +
        "in Directory.Build.props for why this must stay on.");

        var baselineElements = doc.Descendants("PackageValidationBaselineVersion").ToList();
        baselineElements.ShouldNotBeEmpty(
        "Directory.Build.props no longer declares <PackageValidationBaselineVersion> at all. " +
        "Without it, EnablePackageValidation has nothing to compare against -- API Compat " +
        "requires a *previously published* version to diff the current API surface against, " +
        "and package validation has silently stopped catching unintentional breaking changes " +
        "the moment this element goes missing: dotnet pack keeps succeeding, with no warning " +
        "and no error, exactly the anti-pattern CLAUDE.md names -- 'never substitute plausible " +
        "defaults that let a suite pass while asserting nothing.' Restore " +
        "<PackageValidationBaselineVersion>0.1.0-preview.2</PackageValidationBaselineVersion> " +
        "(or the version most recently published -- see CONTRIBUTING.md's \"Cutting a release, " +
        "end to end\" list, which bumps this value every release) next to " +
        "<EnablePackageValidation> in Directory.Build.props. See [package-validation-baseline] " +
        "there for the full reasoning, including why the value is centralised across all four " +
        "class libraries rather than set per-project.");

        var baselineValue = baselineElements[0].Value;
        baselineValue.ShouldNotBeNullOrWhiteSpace(
        $"Directory.Build.props declares <PackageValidationBaselineVersion> but its value is " +
        $"empty or whitespace (\"{baselineValue}\"). A blank value silently disables package " +
        "validation's baseline comparison the same way a missing element does -- dotnet pack " +
        "keeps succeeding with API Compat having nothing to diff against, no warning, no error. " +
        "Restore it to the version most recently published (see CONTRIBUTING.md's \"Cutting a " +
        "release, end to end\" list) -- an intentionally blank per-project override belongs on " +
        "one newly added adapter's own .csproj, immediately before its own first publish, never " +
        "on the centralised Directory.Build.props value every existing package relies on. See " +
        "[package-validation-baseline] in Directory.Build.props for the full reasoning.");
    }
}
