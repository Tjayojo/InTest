using InTest.Cli.Naming;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class CSharpIdentifierTests
{
    [TestMethod]
    [DataRow("getOrderById", "GetOrderById")]
    [DataRow("get_orders_id", "GetOrdersId")]
    [DataRow("orders-v2", "OrdersV2")]
    [DataRow("2fa", "_2fa")]
    // PascalCasing is itself the keyword escape: every C# reserved keyword is lowercase, so
    // capitalizing the first character already yields a legal identifier. The '@' guard in
    // ToPascalCase is unreachable defence-in-depth, kept for callers that bypass casing.
    [DataRow("class", "Class")]
    public void ToPascalCase_ProducesValidIdentifiers(string input, string expected)
    {
        CSharpIdentifier.ToPascalCase(input).ShouldBe(expected);
    }

    [TestMethod]
    public void ToPascalCase_ThrowsOnEmptyInput()
    {
        Should.Throw<ArgumentException>(() => CSharpIdentifier.ToPascalCase("   "));
    }

    [TestMethod]
    public void Dedupe_LeavesUniqueNamesUntouched()
    {
        var input = new Dictionary<string, string> { ["a"] = "GetOrder", ["b"] = "PostOrder" };
        CSharpIdentifier.Dedupe(input).Values.ShouldBe(["GetOrder", "PostOrder"], ignoreOrder: true);
    }

    [TestMethod]
    public void Dedupe_SuffixesCollisionsWithAStableKeyHash()
    {
        var input = new Dictionary<string, string> { ["get_a"] = "GetOrder", ["get_b"] = "GetOrder" };
        var result = CSharpIdentifier.Dedupe(input);

        result["get_a"].ShouldNotBe(result["get_b"]);
        result.Values.ShouldAllBe(v => v.StartsWith("GetOrder"));
    }

    [TestMethod]
    public void Dedupe_IsIndependentOfInsertionOrder()
    {
        var forward = CSharpIdentifier.Dedupe(new Dictionary<string, string> { ["get_a"] = "GetOrder", ["get_b"] = "GetOrder" });
        var reverse = CSharpIdentifier.Dedupe(new Dictionary<string, string> { ["get_b"] = "GetOrder", ["get_a"] = "GetOrder" });

        forward["get_a"].ShouldBe(reverse["get_a"]);
        forward["get_b"].ShouldBe(reverse["get_b"]);
    }

    [TestMethod]
    [DataRow("Orders.ApiTests")]
    [DataRow("Orders.ApiTests.OrdersTestBase")]
    [DataRow("Orders")]
    [DataRow("_Orders")]
    [DataRow("Orders2")]
    public void TryValidateDottedName_AcceptsWellFormedDottedNames(string value)
    {
        CSharpIdentifier.TryValidateDottedName(value, "project.rootNamespace", out var reason).ShouldBeTrue();
        reason.ShouldBe(string.Empty);
    }

    // One row per rule violation. Every per-segment and empty-segment message quotes the whole
    // value the adopter typed (the FixtureDocument.TryValidateOperationKey precedent), so the
    // only case with nothing to quote is empty/whitespace/null input — those three assert
    // "is empty" instead of the value itself.
    [TestMethod]
    [DataRow("My Project")]
    [DataRow("Orders..Base")]
    [DataRow(".Orders")]
    [DataRow("Orders.")]
    [DataRow("2fa.Tests")]
    [DataRow("Orders.class")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    [DataRow("Orders.ApiTests; public class Injected { static Injected() { System.Console.WriteLine(\"x\"); } } //")]
    public void TryValidateDottedName_RejectsEachRuleViolation(string? value)
    {
        CSharpIdentifier.TryValidateDottedName(value, "project.rootNamespace", out var reason).ShouldBeFalse();

        // The actual requirement: the reason names the setting and quotes the offending value,
        // not just that the boolean came back false — a caller cannot compose a useful error
        // message otherwise.
        reason.ShouldContain("project.rootNamespace", Case.Sensitive);
        reason.ShouldContain(string.IsNullOrWhiteSpace(value) ? "is empty" : value);
    }

    [TestMethod]
    public void TryValidateDottedName_ReportsAGenericFirstCharacterFailureWhenItIsNotADigit()
    {
        // Distinct from the digit-specific message: a segment starting with a symbol takes the
        // "generically otherwise" branch, not the digit branch — it must still name the
        // offending character, not just avoid the digit wording.
        CSharpIdentifier.TryValidateDottedName("Orders.$Test", "project.rootNamespace", out var reason).ShouldBeFalse();
        reason.ShouldContain("project.rootNamespace", Case.Sensitive);
        reason.ShouldContain("'$'");
        // The rule sentence itself mentions "digits", so pin the digit-specific phrase exactly
        // rather than the bare substring "digit".
        reason.ShouldNotContain("starts with a digit");
    }
}
