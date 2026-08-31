using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Golden.Tests;

[TestClass]
public class GoldenFileTests
{
    private static string SpecPath => Path.Combine(AppContext.BaseDirectory, "Specs", "orders.json");
    private static string ExpectedPath => Path.Combine(AppContext.BaseDirectory, "Expected", "OrdersTests.g.cs.txt");

    /// <summary>
    /// The golden in the *source* tree. Updating must not write to the build output: with
    /// CopyToOutputDirectory="PreserveNewest" the freshly written copy under bin/ becomes newer
    /// than the committed one, so MSBuild stops refreshing it and the assertion then compares
    /// that copy against itself — green forever, whatever the repository actually contains.
    /// </summary>
    private static string SourceExpectedPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Expected", "OrdersTests.g.cs.txt"));

    private static async Task<string> RenderAsync()
    {
        var spec = await SpecLoader.LoadFromFileAsync(SpecPath);
        var plan = TestPlanBuilder.Build(spec.Document);
        var ordersClass = plan.Classes.Single(c => c.ClassName == "OrdersTests");
        return new TemplateRenderer("mstest").RenderClass(ordersClass, "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");
    }

    [TestMethod]
    public async Task OutputMatchesTheGoldenFile()
    {
        var actual = await RenderAsync();

        if (Environment.GetEnvironmentVariable("INTEST_UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(SourceExpectedPath, actual);
            Assert.Inconclusive(
                $"Golden file updated at {SourceExpectedPath}. Review the diff, then rebuild and "
                + "re-run without INTEST_UPDATE_GOLDEN to verify.");
        }

        actual.ShouldBe(await File.ReadAllTextAsync(ExpectedPath));
    }

    [TestMethod]
    public async Task GenerationIsDeterministic()
    {
        (await RenderAsync()).ShouldBe(await RenderAsync());
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
        var spec = await SpecLoader.LoadFromFileAsync(SpecPath);
        var plan = TestPlanBuilder.Build(spec.Document);
        var cases = plan.Classes.SelectMany(c => c.Cases).ToList();

        cases.Select(c => c.Role).Distinct().Count().ShouldBeGreaterThan(1,
            "this spec no longer exercises multiple CaseRole values — the point of asserting " +
            "against it rather than a synthetic single-role plan.");
        cases.ShouldAllBe(c => c.Category == "Contract");
    }
}
