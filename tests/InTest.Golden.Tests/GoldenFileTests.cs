using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Golden.Tests;

[TestClass]
public class GoldenFileTests
{
    private static string SpecPath(string specFileName) => Path.Combine(AppContext.BaseDirectory, "Specs", specFileName);
    private static string ExpectedPath(string expectedFileName) => Path.Combine(AppContext.BaseDirectory, "Expected", expectedFileName);

    /// <summary>
    /// The golden in the *source* tree. Updating must not write to the build output: with
    /// CopyToOutputDirectory="PreserveNewest" the freshly written copy under bin/ becomes newer
    /// than the committed one, so MSBuild stops refreshing it and the assertion then compares
    /// that copy against itself — green forever, whatever the repository actually contains.
    /// </summary>
    private static string SourceExpectedPath(string expectedFileName) => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Expected", expectedFileName));

    /// <param name="specFileName">Which fixture spec under Specs/ to load.</param>
    /// <param name="framework">"mstest" or "xunit" — selects the template, per
    /// [framework-selects-template] in TemplateRenderer.</param>
    /// <param name="className">Which TestClassPlan the spec produces to render — a spec can
    /// produce more than one class, grouped by OpenAPI tag (TestPlanBuilder.Build).</param>
    private static async Task<string> RenderAsync(string specFileName, string framework, string className)
    {
        var spec = await SpecLoader.LoadFromFileAsync(SpecPath(specFileName));
        var plan = TestPlanBuilder.Build(spec.Document);
        var testClass = plan.Classes.Single(c => c.ClassName == className);
        return new TemplateRenderer(framework).RenderClass(testClass, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");
    }

    /// <summary>
    /// Pins the template's byte-for-byte output. Task 7 [second-template-was-blind] extended
    /// this from a single mstest/orders.json case to four, over [DataRow] rather than four
    /// hand-copied methods, for the same "one copy of the logic, several inputs" reason
    /// TemplateEscapingGuardTests already applies to the two template files:
    /// <list type="bullet">
    /// <item>orders.json rendered under both frameworks — the direct MSTest/xUnit comparison
    /// Task 7 Step 4 reads by eye for identical role gating (FixtureParameter on Success,
    /// Guid.NewGuid().ToString() on the 401/403/404 siblings for the same path).</item>
    /// <item>mutating-operation.json rendered under both frameworks — <b>the only reason this
    /// second spec exists</b>. orders.json has no operation whose Success case is also
    /// `mutates` (POST/PUT/PATCH/DELETE) — see TestPlanBuilder.Build's `mutates` assignment —
    /// so neither OrdersTests golden file can ever contain `[DoNotParallelize]` or
    /// `[Fact(DisableParallelization = true)]`. Task 6's substitution between those two
    /// attributes would be checked in but unverified by the one artifact meant to verify it.
    /// mutating-operation.json's single POST /widgets operation exists to put exactly one
    /// occurrence of each into checked-in output, so a future edit that breaks either mapping
    /// fails a byte comparison rather than passing silently.</item>
    /// </list>
    /// A dedicated spec, not an extension of orders.json: orders.json is read directly by
    /// CliExitCodeTests, CompileVerificationTests (several cases keyed to its exact two
    /// operations and their path-parameter shapes) and ScaffoldCompileVerificationTests. Adding a
    /// third, mutating operation there would perturb all of them for a fact this test does not
    /// need orders.json to establish, and re-verifying every one of those call sites needs the
    /// full Golden suite. A second, minimal spec proves the substitution with zero risk to any
    /// of that existing coverage.
    /// </summary>
    [TestMethod]
    [DataRow("orders.json", "mstest", "OrdersTests", "OrdersTests.g.cs.txt", DisplayName = "Orders / mstest")]
    [DataRow("orders.json", "xunit", "OrdersTests", "OrdersTests.xunit.g.cs.txt", DisplayName = "Orders / xunit")]
    [DataRow("orders.json", "nunit", "OrdersTests", "OrdersTests.nunit.g.cs.txt", DisplayName = "Orders / nunit")]
    [DataRow("mutating-operation.json", "mstest", "WidgetsTests", "MutatingOperationTests.g.cs.txt", DisplayName = "mutates / mstest")]
    [DataRow("mutating-operation.json", "xunit", "WidgetsTests", "MutatingOperationTests.xunit.g.cs.txt", DisplayName = "mutates / xunit")]
    [DataRow("mutating-operation.json", "nunit", "WidgetsTests", "MutatingOperationTests.nunit.g.cs.txt", DisplayName = "mutates / nunit")]
    public async Task OutputMatchesTheGoldenFile(string specFileName, string framework, string className, string expectedFileName)
    {
        var actual = await RenderAsync(specFileName, framework, className);

        if (Environment.GetEnvironmentVariable("INTEST_UPDATE_GOLDEN") == "1")
        {
            var sourcePath = SourceExpectedPath(expectedFileName);
            await File.WriteAllTextAsync(sourcePath, actual);
            Assert.Inconclusive(
                $"Golden file updated at {sourcePath}. Review the diff, then rebuild and "
                + "re-run without INTEST_UPDATE_GOLDEN to verify.");
        }

        actual.ShouldBe(await File.ReadAllTextAsync(ExpectedPath(expectedFileName)));
    }

    [TestMethod]
    public async Task GenerationIsDeterministic()
    {
        (await RenderAsync("orders.json", "mstest", "OrdersTests"))
            .ShouldBe(await RenderAsync("orders.json", "mstest", "OrdersTests"));
    }

    /// <summary>
    /// Closes a gap review found in TemplateEscapingGuardTests: that test allow-lists
    /// mstest-class.scriban's bare {{ tc.category }} interpolation on the strength of a comment
    /// claiming Category is always the constant TestPlanBuilder.ContractCategory = "Contract"
    /// (TestPlanBuilder.cs:12) — never spec-derived, so nothing needs escaping there. Nothing
    /// mechanically enforced that claim; TestCasePlan.Category is a plain public string with no
    /// invariant of its own. This asserts it directly against the plan that also drives the
    /// golden file, which — unlike TestPlanBuilderTests' narrower specs — already produces
    /// Success, DeclaredError and Auth cases together, so the assertion covers every role this
    /// branch generates without needing its own spec.
    /// </summary>
    [TestMethod]
    public async Task EveryCaseIsCategorizedContract()
    {
        var spec = await SpecLoader.LoadFromFileAsync(SpecPath("orders.json"));
        var plan = TestPlanBuilder.Build(spec.Document);
        var cases = plan.Classes.SelectMany(c => c.Cases).ToList();

        cases.Select(c => c.Role).Distinct().Count().ShouldBeGreaterThan(1,
            "this spec no longer exercises multiple CaseRole values — the point of asserting " +
            "against it rather than a synthetic single-role plan.");
        cases.ShouldAllBe(c => c.Category == "Contract");
    }
}
