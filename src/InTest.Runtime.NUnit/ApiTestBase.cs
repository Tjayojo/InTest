using NUnit.Framework;

namespace InTest.Runtime;

/// <summary>
/// NUnit adapter over <see cref="ApiTestCore"/>, mirroring the MSTest and xUnit adapters. Generated
/// classes derive from a project base class deriving from this; everything they call —
/// <c>UseIdentity</c>, <c>RequireFixture</c>, <c>FixtureBody</c>, <c>Client</c>, <c>TestId</c>,
/// <c>Schemas</c> — lives on the neutral base and needs no adapting.
/// <para>
/// Lifecycle is <c>[SetUp]</c>/<c>[TearDown]</c>, NUnit's per-test hooks. The display name comes
/// from <c>TestContext.CurrentContext.Test.Name</c>, which — unlike MSTest's <c>TestName</c> —
/// already distinguishes data-row variations (verified: two <c>[TestCase]</c> rows reported
/// <c>…ForEachRow(1)</c> and <c>(2)</c>), so the correlation id stays distinct per row.
/// </para>
/// </summary>
[TestFixture]
public abstract class ApiTestBase : ApiTestCore
{
    [SetUp]
    public void ApiTestSetUp() =>
        BeginTest(TestContext.CurrentContext.Test.Name, new TestHost.NUnitDiagnostics());

    [TearDown]
    public void ApiTestTearDown() => EndTest();

    /// <summary>
    /// [skip-is-a-reason]: the neutral layer returns a reason string, null meaning "run". MSTest's
    /// adapter turns that into <c>Assert.Inconclusive</c>, xUnit's into <c>Assert.Skip</c>, and
    /// NUnit's into <c>Assert.Ignore</c> — verified to produce trx <c>outcome="NotExecuted"</c>,
    /// the same outcome as the other two, with the reason in both <c>&lt;StdOut&gt;</c> and
    /// <c>&lt;ErrorInfo&gt;&lt;Message&gt;</c>.
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        if (MultipleIdentitiesSkipReason() is { } reason)
        {
            Assert.Ignore(reason);
        }
    }

    /// <inheritdoc cref="RequireMultipleIdentities"/>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        if (SecondaryIdentityScopeSkipReason(requiredScopes) is { } reason)
        {
            Assert.Ignore(reason);
        }
    }
}
