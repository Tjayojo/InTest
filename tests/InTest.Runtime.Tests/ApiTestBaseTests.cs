using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// <see cref="ApiTestCore"/> as a whole is not given an in-process harness — its
/// <see cref="ApiTestCore.BeginTest"/> depends on <c>InTestRun.Root</c>, which only exists after
/// the full, heavy <c>InTestRun.InitializeAsync</c> has run (see <c>TestHostTests</c>'s own note
/// on why that method gets no harness either). <see cref="ApiTestCore.ResolveDefaultIdentity"/> is
/// pulled out as an internal, dependency-free seam specifically so the one genuinely new decision
/// this task adds — which identity a test defaults to — has a real test rather than shipping
/// unverified alongside a mechanical field-set.
/// </summary>
[TestClass]
public class ApiTestBaseTests
{
    private sealed class FakeProvider(IReadOnlyList<string> identityNames) : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = identityNames.Select(n => new TestIdentity(n)).ToArray();

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    /// <summary>Exposes the otherwise-protected <see cref="ApiTestCore.TestId"/> so
    /// <see cref="TestIdThrowsInvalidOperationExceptionWhenReadOutsideATest"/> can read it without
    /// the weight of a live <c>BeginTest</c> call — that method needs a real <c>InTestRun.Root</c>
    /// scope to construct <see cref="HttpClient"/> from, which this test has no business standing
    /// up just to prove a field starts unset.</summary>
    private sealed class TestableApiTestCore : ApiTestCore
    {
        public string ExposedTestId => TestId;
    }

    [TestMethod]
    public void ResolvesToTheFirstIdentityWhenTheProviderHasOne()
    {
        var provider = new FakeProvider(["default", "secondary"]);

        ApiTestCore.ResolveDefaultIdentity(provider).ShouldBe("default");
    }

    [TestMethod]
    public void ResolvesToTheNoTokenSentinelWhenTheProviderHasZeroIdentities()
    {
        // ITestTokenProvider.cs already documents this as an explicitly contemplated state, not
        // an error: indexing Identities[0] blind here would throw ArgumentOutOfRangeException in
        // [TestInitialize], before a single request is built, for every test in the suite —
        // turning a gating state into a suite-wide crash (decision 7).
        var provider = new FakeProvider([]);

        ApiTestCore.ResolveDefaultIdentity(provider).ShouldBe(InTestIdentities.None);
    }

    [TestMethod]
    public void ResolvesToTheNoTokenSentinelWhenNoProviderIsRegistered()
    {
        // Catalog and Inventory declare no security and register no provider at all — the
        // majority case. This must behave exactly as an empty Identities list would.
        ApiTestCore.ResolveDefaultIdentity(null).ShouldBe(InTestIdentities.None);
    }

    [TestMethod]
    public void TestIdThrowsInvalidOperationExceptionWhenReadOutsideATest()
    {
        // Task 5's one deliberate behaviour change: TestId used to recompute
        // InTestId.ForTest(...) from TestContext.TestDisplayName on every read, and
        // TestContext.TestDisplayName itself throws NullReferenceException outside a running
        // test — an unhelpful failure for anything that touches TestId before BeginTest has run.
        // Now TestId is a field BeginTest assigns once and EndTest clears; reading it before
        // BeginTest (or after EndTest) must fail with a message that names the actual cause
        // rather than surfacing framework plumbing as a NullReferenceException.
        var subject = new TestableApiTestCore();

        var ex = Should.Throw<InvalidOperationException>(() => subject.ExposedTestId);

        ex.Message.ShouldContain("BeginTest");
    }
}
