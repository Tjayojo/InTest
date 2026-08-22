using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// Every string assertion in the suite rests on one property of Shouldly that its method names do
/// not advertise: <c>ShouldContain</c>, <c>ShouldNotContain</c>, <c>ShouldStartWith</c> and
/// <c>ShouldEndWith</c> all take <c>Case caseSensitivity = Case.Insensitive</c>. The deliberate
/// <c>Case.Sensitive</c> arguments elsewhere in the suite are load-bearing only while that default
/// holds, and the far larger number of un-annotated calls quietly change meaning if it flips. A
/// Shouldly upgrade that reversed either would otherwise be invisible — nothing would fail, the
/// assertions would merely start claiming something other than what their authors checked.
/// </summary>
[TestClass]
public class ShouldlyStringDefaultsTests
{
    private const string SettingPath = "project.rootNamespace";

    [TestMethod]
    public void ShouldContainCannotTellCasingApartUnlessAskedTo()
    {
        // The default passes against a PascalCased impostor of the setting path — which is why
        // a message naming `Project.RootNamespace` went undetected across 33 tests.
        SettingPath.ShouldContain("Project.RootNamespace");

        // Case.Sensitive is what turns it into an assertion about casing at all.
        Should.Throw<ShouldAssertException>(
            () => SettingPath.ShouldContain("Project.RootNamespace", Case.Sensitive));
    }

    [TestMethod]
    public void ShouldNotContainInheritsTheSameDefaultAndIsThereforeStrongerThanItReads()
    {
        // A case-insensitive negative rejects more than it says, not less. That is the safe
        // direction, and it is why the suite's ShouldNotContain calls are left un-annotated.
        Should.Throw<ShouldAssertException>(() => SettingPath.ShouldNotContain("Project.RootNamespace"));
        SettingPath.ShouldNotContain("Project.RootNamespace", Case.Sensitive);
    }

    [TestMethod]
    public void TheRemainingFourOverloadsShareTheSameDefault()
    {
        // The family is exactly these six. Established by reflecting over Shouldly 4.3.0 for
        // every method taking a Case parameter, then pinned here behaviourally — a reflected
        // signature says a default exists, not what it does.
        const string flag = "--project is empty.";

        flag.ShouldStartWith("--Project");
        Should.Throw<ShouldAssertException>(() => flag.ShouldStartWith("--Project", Case.Sensitive));

        Should.Throw<ShouldAssertException>(() => flag.ShouldNotStartWith("--Project"));
        flag.ShouldNotStartWith("--Project", Case.Sensitive);

        flag.ShouldEndWith("EMPTY.");
        Should.Throw<ShouldAssertException>(() => flag.ShouldEndWith("EMPTY.", Case.Sensitive));

        Should.Throw<ShouldAssertException>(() => flag.ShouldNotEndWith("EMPTY."));
        flag.ShouldNotEndWith("EMPTY.", Case.Sensitive);
    }

    [TestMethod]
    public void TheCollectionOverloadIsOrdinalAndTakesNoCaseArgument()
    {
        // A different overload with a different default: IEnumerable<string>.ShouldContain uses
        // the default equality comparer. TestPlanBuilder's scope assertions are already
        // case-sensitive for this reason, and Case.Sensitive would not compile there.
        string[] scopes = ["orders.read", "ORDERS.READ"];
        scopes.ShouldContain("ORDERS.READ");
        Should.Throw<ShouldAssertException>(() => scopes.ShouldContain("Orders.Read"));
    }
}
