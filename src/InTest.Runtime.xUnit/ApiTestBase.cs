using Xunit;

namespace InTest.Runtime;

/// <summary>
/// xUnit adapter over <see cref="ApiTestCore"/>, mirroring <c>InTest.Runtime.MSTest</c>'s
/// <c>ApiTestBase</c>. Generated classes derive from a project base class deriving from this, and
/// call <c>RequireMultipleIdentities()</c>, <c>RequireSecondaryIdentityLacks(...)</c>,
/// <c>UseIdentity(...)</c>, <c>RequireFixture(...)</c>, <c>FixtureBody(...)</c>, <c>Client</c>,
/// <c>TestId</c> and <c>Schemas</c> — all of which live on the neutral base and need no adapting.
/// <para>
/// This class's whole job is the two seams <see cref="ApiTestCore"/> cannot own without naming a
/// test framework: lifecycle, and turning a skip <em>reason</em> (a plain <c>string?</c>, null
/// meaning "run") into an actual skip call.
/// </para>
/// <para>
/// <b>[lifecycle-is-the-real-difference]: lifecycle is where the frameworks genuinely differ.</b> MSTest uses
/// <c>[TestInitialize]</c>/<c>[TestCleanup]</c>. xUnit v3 uses <see cref="IAsyncLifetime"/>, which
/// declares <b>only</b> <c>InitializeAsync</c> and inherits <see cref="IAsyncDisposable"/> — the
/// v2 shape with both on the interface does not exist here. Verified: inside
/// <c>InitializeAsync</c>, <c>TestContext.Current.Test</c> is non-null and its
/// <c>TestDisplayName</c> is populated, which is what makes this the right place to call
/// <see cref="ApiTestCore.BeginTest"/>; and <c>DisposeAsync</c> runs on pass, fail <em>and</em>
/// skip, so <see cref="ApiTestCore.EndTest"/> is not missed on any path.
/// </para>
/// <para>
/// [snapshot-at-call-time]: <c>TestContext.Current</c> is read at each use and never cached — xUnit documents it as a
/// point-in-time snapshot. Its static type is <c>ITestContext</c>, not <c>TestContext</c>.
/// </para>
/// </summary>
public abstract class ApiTestBase : ApiTestCore, IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BeginTest(TestContext.Current.Test?.TestDisplayName, new TestHost.XunitDiagnostics());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        EndTest();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// [skip-is-a-reason]: the neutral layer returns a reason string, null meaning "run". MSTest's
    /// adapter turns that into <c>Assert.Inconclusive</c>; xUnit's into <c>Assert.Skip</c>. Verified
    /// to produce trx <c>outcome="NotExecuted"</c> — the same outcome MSTest reports — with the
    /// reason in <c>&lt;Output&gt;&lt;StdOut&gt;</c> rather than <c>&lt;Message&gt;</c>, which
    /// matters to any acceptance check asserting on skip reasons.
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        if (MultipleIdentitiesSkipReason() is { } reason)
        {
            Assert.Skip(reason);
        }
    }

    /// <inheritdoc cref="RequireMultipleIdentities"/>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        if (SecondaryIdentityScopeSkipReason(requiredScopes) is { } reason)
        {
            Assert.Skip(reason);
        }
    }
}
