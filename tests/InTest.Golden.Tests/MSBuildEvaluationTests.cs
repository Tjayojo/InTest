using System.Text.Json;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// <c>MSBuildPropertyValue.TryEscape</c> is the fix; every existing test for it goes through
/// <c>XDocument</c> in <c>InTest.Cli.Tests.InitCommandTests</c> (<c>EscapesAmpersandSoThe...</c>,
/// <c>EscapesDollarParenSoIt...</c>, <c>EscapesQuestionMarkSoThe...</c>). <c>XDocument</c> proves
/// the generated <c>.csproj</c> is well-formed XML, which is genuine coverage of the ampersand
/// case — an unescaped <c>&amp;</c> is not well-formed and <c>XDocument.Parse</c> throws on it —
/// but it knows nothing about MSBuild. It cannot say whether <c>%24</c> would expand as a
/// property reference, or whether <c>%3F</c> would resolve as a literal path segment rather than
/// a single-character glob wildcard. Those assertions pin that the escape sequence is present in
/// the file, not that it survives an actual MSBuild evaluation — CONTRIBUTING.md's "Ask the thing
/// that decides" names this exact gap: the thing that decides whether an MSBuild property value
/// round-trips is MSBuild, not a generic XML parser reading the same bytes.
/// <para>
/// This class asks MSBuild directly, the same way <see cref="CompileVerificationTests"/> asks
/// <c>csc</c> directly for <c>CSharpLiteral.Escape</c> rather than trusting a string assertion.
/// Two tests, because the two escaped characters fail in observably different ways: an unescaped
/// <c>&amp;</c> breaks XML well-formedness, which <c>-getProperty</c> reports as a load failure
/// (MSB4025) — loud, and already partly caught by <c>XDocument</c>. An unescaped <c>?</c> stays
/// well-formed XML and evaluates to a <i>different, silently wrong</i> file once it reaches an
/// <c>Include=</c> glob — nothing about parsing the file can see this, only evaluating the
/// resulting item list can, which is why <see cref="MsBuildResolvesTheSpecItemToTheLiteralPathNotTheGlobDecoy"/>
/// exists as a separate instrument rather than a second assertion bolted onto the first test.
/// </para>
/// </summary>
[TestClass]
public class MSBuildEvaluationTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-msbuild-eval-" + Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>
    /// <c>dotnet msbuild &lt;csproj&gt; -getProperty:InTestSpecSource</c> evaluates the property
    /// the same way any real build would, without needing a package restore first — confirmed
    /// here by using the exact <c>InTest.Runtime.MSTest</c> <c>PackageReference</c> <c>InitCommand</c>
    /// itself writes (a version that does not need to resolve for property evaluation to
    /// succeed) rather than a hand-simplified project. <paramref name="hazardous"/> exercises
    /// both escaping layers and several of <c>MSBuildPropertyValue</c>'s MSBuild specials at
    /// once: <c>&amp;</c> (layer 2, XML), and <c>?</c> and <c>$(</c> (layer 1, MSBuild) — the
    /// value was confirmed by hand against a real evaluation before this test was written.
    /// </summary>
    [TestMethod]
    public async Task MsBuildEvaluatesInTestSpecSourceBackToExactlyWhatTheAdopterTyped()
    {
        const string hazardous = "../R&D/orders?v=1$(Cfg).json";

        InitCommand.Run(_root, "Orders.ApiTests", hazardous).ShouldBe(0);

        var csprojPath = Path.Combine(_root, "Orders.ApiTests.csproj");
        var (exitCode, output) = await ProcessRunner.RunAsync(
        "dotnet", $"msbuild \"{csprojPath}\" -getProperty:InTestSpecSource");

        // Asserted before the value check, and separately from it: a load failure (an unescaped
        // '&' breaking XML well-formedness, reported as MSB4025) surfaces here as a non-zero exit
        // with MSBuild's own diagnostic in `output`, rather than as a confusing empty-string or
        // garbled-text mismatch against `hazardous` below.
        exitCode.ShouldBe(0, $"dotnet msbuild failed to evaluate the generated project:{Environment.NewLine}{output}");

        // `$(Cfg)` staying unexpanded is the actual proof `XDocument` cannot offer: it only ever
        // sees the escaped "%24(Cfg)" text and has no way to know whether MSBuild would later
        // expand it as a property reference. Only MSBuild evaluating the property can say so.
        output.Trim().ShouldBe(hazardous,
        customMessage: "MSBuild's evaluated InTestSpecSource must equal exactly what the adopter typed, " +
                       "with $(Cfg) left unexpanded — anything else means the escape/unescape round trip " +
                       "through %XX and XML entities lost or transformed the adopter's path");
    }

    /// <summary>
    /// The case an XML parser structurally cannot see. <c>&amp;</c> fails loudly; <c>?</c> does
    /// not — <c>&lt;Content Include="$(InTestSpecSource)"&gt;</c>-shaped items glob-expand, so an
    /// unescaped <c>?</c> produces a <i>green build against a file the adopter never named</i>.
    /// <c>InitCommand</c> itself never writes an <c>Include="$(InTestSpecSource)"</c> item today
    /// — nothing in the current scaffold globs the property — so a custom item type is appended
    /// to this test's own copy of the generated <c>.csproj</c> purely so this test has an item to
    /// evaluate. It globs under exactly the same MSBuild rules any <c>Content</c> item would;
    /// deliberately not named <c>Content</c>, so this evaluation cannot be confused with the
    /// scaffold's real <c>Content</c> items (<c>appsettings*.json</c>,
    /// <c>fixtures/**/*.json</c>, ...) also present in the same file.
    /// <para>
    /// Confirmed by hand against a real evaluation before this test was written: with
    /// <c>specs/orders.json</c> and <c>specs/ordersX.json</c> both on disk and the property set
    /// to the escaped form of <c>specs/orders?.json</c>, the item resolved to the literal
    /// <c>specs/orders?.json</c>; with the raw, unescaped value, it silently resolved to
    /// <c>specs\ordersX.json</c> instead. Neither evaluation errors — <c>-getItem</c> is exactly
    /// as clean either way, which is the whole hazard: only reading which <c>Identity</c> came
    /// back distinguishes the correct behavior from the silent wrong-file one.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task MsBuildResolvesTheSpecItemToTheLiteralPathNotTheGlobDecoy()
    {
        Directory.CreateDirectory(Path.Combine(_root, "specs"));
        File.WriteAllText(Path.Combine(_root, "specs", "orders.json"), "{}"); // the real spec
        File.WriteAllText(Path.Combine(_root, "specs", "ordersX.json"), "{}"); // what an unescaped '?' would glob-match instead

        InitCommand.Run(_root, "Orders.ApiTests", "specs/orders?.json").ShouldBe(0);

        var csprojPath = Path.Combine(_root, "Orders.ApiTests.csproj");
        var csprojText = File.ReadAllText(csprojPath);

        File.WriteAllText(csprojPath, csprojText.Replace(
        "</Project>",
        """
          <ItemGroup>
            <InTestSpecCheck Include="$(InTestSpecSource)" />
          </ItemGroup>
        </Project>
        """,
        StringComparison.Ordinal));

        var (exitCode, output) = await ProcessRunner.RunAsync(
        "dotnet", $"msbuild \"{csprojPath}\" -getItem:InTestSpecCheck");

        exitCode.ShouldBe(0, $"dotnet msbuild failed to evaluate the generated project:{Environment.NewLine}{output}");

        using var doc = JsonDocument.Parse(output);
        var identity = doc.RootElement.GetProperty("Items").GetProperty("InTestSpecCheck")[0]
            .GetProperty("Identity").GetString();

        // No separate ShouldNotBe("specs\ordersX.json") — it is strictly implied by the ShouldBe
        // below and can never fail unless that one already has. The decoy's identity and why it
        // is on disk at all live here instead, at the assertion that actually discriminates.
        identity.ShouldBe("specs/orders?.json",
        customMessage: "the escaped '?' (%3F) must survive as literal text in the resolved item, not " +
                       "glob-match specs/ordersX.json, the decoy on disk an unescaped '?' would have " +
                       "silently resolved to instead — the exact defect MSBuildPropertyValue exists to prevent");
    }
}
