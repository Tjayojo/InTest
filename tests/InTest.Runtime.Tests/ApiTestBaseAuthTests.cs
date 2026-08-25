using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// v1-c Task 5: the runtime guard that replaces <c>MemberCondition</c> (decision 3 — measured to
/// be evaluated before <c>[AssemblyInitialize]</c>, so it cannot see anything the DI container
/// built), and <see cref="ApiTestCore.UseIdentity"/>, the override point a generated auth case
/// calls before building its request (decision 7). Also <see cref="ApiTestCore.ResolveIdentitySlot"/>,
/// the slot-to-identity resolution <c>UseIdentity</c> defers to, and Task 2's
/// <see cref="ApiTestCore.SecondaryIdentityScopeSkipReason"/>, the guard that reports a wrong-scope
/// 403 the secondary identity is actually authorized for.
/// <para>
/// Task 5 (the neutral/adapter split) moves most of this class's assertions onto the neutral,
/// pure functions — <see cref="ApiTestCore.MultipleIdentitiesSkipReason"/> and
/// <see cref="ApiTestCore.SecondaryIdentityScopeSkipReason"/> — asserting directly on the reason
/// string each returns (null means "run") rather than catching <see cref="AssertInconclusiveException"/>
/// and parsing its <c>Message</c>. That is strictly stronger: it tests the actual decision instead
/// of an MSTest side effect of the decision. A small number of tests deliberately keep going
/// through <see cref="ApiTestBase.RequireMultipleIdentities"/> / <see cref="ApiTestBase.RequireSecondaryIdentityLacks"/>
/// instead, so the adapter's "reason string in, Assert.Inconclusive out" delegation is itself
/// covered by something — see <see cref="ATwoIdentityProviderLetsTheForbiddenTestRun"/> and
/// <see cref="SecondaryHoldingEveryRequiredScopeSkips"/>.
/// </para>
/// <para>
/// <see cref="InTestRun.TokenProvider"/> is process-wide static state, the same shape
/// <c>TestHostTests</c> already hand-rolls for <c>InTestRun.RetainedFixtureContext</c>: reset
/// before and after every test here so no test is at the mercy of what its predecessor left
/// behind, and so this class never leaks into whatever runs after it.
/// </para>
/// </summary>
[TestClass]
public class ApiTestBaseAuthTests
{
    private sealed class FakeTokenProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; }

        public FakeTokenProvider(params string[] identityNames) =>
            Identities = identityNames.Select(n => new TestIdentity(n)).ToArray();

        // Widened for Task 2 (RequireSecondaryIdentityLacks): those tests need identities that
        // carry TestIdentity.Scopes, not just names.
        public FakeTokenProvider(params TestIdentity[] identities) => Identities = identities;

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by these tests");
    }

    /// <summary>
    /// Gives the tests below a way to call the <c>protected static</c>
    /// <see cref="ApiTestCore.UseIdentity"/> — the same reason <c>FixtureValidationTests</c>
    /// tests <c>FixtureValidation</c> directly rather than through <c>ApiTestCore.RequireFixture</c>
    /// wherever possible, except <c>UseIdentity</c>'s scope-restore behaviour is new enough this
    /// task that it earns a direct test rather than only the golden execution suite's live proof.
    /// Derives from <see cref="ApiTestCore"/> directly, not <see cref="ApiTestBase"/>: UseIdentity
    /// is neutral logic with no MSTest dependency, so testing it needs none either.
    /// </summary>
    private sealed class TestableApiTestCore : ApiTestCore
    {
        public static IDisposable ExposeUseIdentity(IdentitySlot slot) => UseIdentity(slot);
    }

    [TestInitialize]
    public void Reset()
    {
        InTestRun.TokenProvider = null;
        InTestAmbient.Identity.Value = null;
    }

    [TestCleanup]
    public void ResetAfter()
    {
        InTestRun.TokenProvider = null;
        InTestAmbient.Identity.Value = null;
    }

    // --- MultipleIdentitiesSkipReason (decision 3) ---

    [TestMethod]
    public void AOneIdentityProviderReportsASkipReasonNamingTheCount()
    {
        // Must fail if the guard stops reporting a reason OR stops explaining. A bare
        // ShouldBeFalse on some condition property would pass just as well with nothing
        // registered at all — asserting the actual reason text, on a provider deliberately built
        // one-identity, is the point.
        InTestRun.TokenProvider = new FakeTokenProvider("only-one");

        var reason = ApiTestCore.MultipleIdentitiesSkipReason();

        // Task 10 item 4: this must name the count a *registered* provider advertised — the
        // phrase that distinguishes this case from NoRegisteredProviderAlsoReports... below, which
        // has no provider at all. Asserting only "identit"/"403" (as both tests did before this
        // task) passes equally on either message and would not have caught the wording bug that
        // motivated the branch.
        reason.ShouldNotBeNull();
        reason.ShouldContain("advertises 1 identity");
        reason.ShouldContain("403");
    }

    [TestMethod]
    public void NoRegisteredProviderAlsoReportsASkipReason()
    {
        // InTestRun.TokenProvider is null for every spec that declares no security — the same
        // zero-identity state ResolveDefaultIdentity already treats as ordinary, not an error.
        InTestRun.TokenProvider = null;

        var reason = ApiTestCore.MultipleIdentitiesSkipReason();

        // Task 10 item 4: must say no provider is registered, not "advertises 0 identities" —
        // that older wording reads as if a provider *is* registered and simply advertises none,
        // sending a reader hunting for a bug in code they never wrote.
        reason.ShouldNotBeNull();
        reason.ShouldContain("no ITestTokenProvider is registered");
        reason.ShouldContain("403");
    }

    [TestMethod]
    public void ATwoIdentityProviderLetsTheForbiddenTestRun()
    {
        // Kept going through the adapter (Should.NotThrow rather than a bare reason.ShouldBeNull())
        // so ApiTestBase.RequireMultipleIdentities' throw-on-reason delegation is itself covered —
        // see this class's own doc for why only a couple of tests do this.
        InTestRun.TokenProvider = new FakeTokenProvider("default", "wrong-scope");

        Should.NotThrow(ApiTestBase.RequireMultipleIdentities);
    }

    /// <summary>Returns a null <c>Identities</c> despite the interface's non-nullable
    /// annotation — nothing at compile time stops a misbehaving <see cref="ITestTokenProvider"/>
    /// implementation from doing this, and <see cref="ApiTestCore.ResolveDefaultIdentity"/>
    /// already guards against exactly this shape.</summary>
    private sealed class NullIdentitiesProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities => null!;

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    [TestMethod]
    public void AProviderWithANullIdentitiesListReportsASkipReasonRatherThanThrowing()
    {
        // Task 10 item 3: MultipleIdentitiesSkipReason's neighbour, ResolveDefaultIdentity, guards
        // both "no provider" and "provider whose Identities is itself null" — deliberately,
        // since ITestTokenProvider.Identities is non-nullable only by annotation, not by
        // anything the runtime enforces. `InTestRun.TokenProvider?.Identities.Count` chains `?.`
        // through the first access only, so a provider with a null Identities list threw
        // NullReferenceException here instead of the same skip reason every other zero/one-identity
        // state produces.
        InTestRun.TokenProvider = new NullIdentitiesProvider();

        var reason = ApiTestCore.MultipleIdentitiesSkipReason();

        reason.ShouldNotBeNull();
        reason.ShouldContain("identit");
        reason.ShouldContain("403");
    }

    // --- ResolveIdentitySlot (decision 7's resolution, pulled out the same way ResolveDefaultIdentity was) ---

    [TestMethod]
    public void NoneSlotIsAlwaysTheSentinelRegardlessOfTheProvider()
    {
        // The 401 case's whole mechanism: it does not matter what the provider advertises, None
        // must never be resolved to an actual identity.
        var provider = new FakeTokenProvider("default", "secondary");

        ApiTestCore.ResolveIdentitySlot(IdentitySlot.None, provider).ShouldBe(InTestIdentities.None);
    }

    [TestMethod]
    public void SecondarySlotResolvesToTheSecondIdentity()
    {
        var provider = new FakeTokenProvider("default", "wrong-scope");

        ApiTestCore.ResolveIdentitySlot(IdentitySlot.Secondary, provider).ShouldBe("wrong-scope");
    }

    [TestMethod]
    public void DefaultSlotResolvesTheSameWayResolveDefaultIdentityAlreadyDoes()
    {
        var provider = new FakeTokenProvider("default", "secondary");

        ApiTestCore.ResolveIdentitySlot(IdentitySlot.Default, provider).ShouldBe("default");
        ApiTestCore.ResolveIdentitySlot(IdentitySlot.Default, null).ShouldBe(InTestIdentities.None);
    }

    // --- UseIdentity (the generated auth case's override point) ---

    [TestMethod]
    public void UseIdentityOverridesTheAmbientIdentityForTheScope()
    {
        InTestRun.TokenProvider = new FakeTokenProvider("default", "secondary");
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestCore.ExposeUseIdentity(IdentitySlot.Secondary))
        {
            InTestAmbient.Identity.Value.ShouldBe("secondary");
        }
    }

    [TestMethod]
    public void UseIdentityRestoresWhateverWasAmbientBeforeItOnDispose()
    {
        // Scoped rather than assigned outright (decision from Task 5's own plan text): a test
        // that throws mid-body must not leave a secondary identity set for whatever runs next.
        // [TestCleanup] clearing InTestAmbient.Identity is not the only thing standing between
        // one test and the next — the using-scope's own Dispose must restore it independently.
        InTestRun.TokenProvider = new FakeTokenProvider("default", "secondary");
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestCore.ExposeUseIdentity(IdentitySlot.Secondary))
        {
        }

        InTestAmbient.Identity.Value.ShouldBe("default");
    }

    [TestMethod]
    public void UseIdentityWithTheNoneSlotSendsTheSentinel()
    {
        InTestAmbient.Identity.Value = "default";

        using (TestableApiTestCore.ExposeUseIdentity(IdentitySlot.None))
        {
            InTestAmbient.Identity.Value.ShouldBe(InTestIdentities.None);
        }

        InTestAmbient.Identity.Value.ShouldBe("default");
    }

    // --- SecondaryIdentityScopeSkipReason (Task 2: the runtime guard for a wrong-scope 403) ---

    [TestMethod]
    public void SecondaryWithNullScopesAlwaysRuns()
    {
        // null = not declared / unknown (Task 1). Unknown-means-run is deliberate: treating it
        // as a skip would switch auth testing off by default for anyone who never declares
        // scopes on their secondary identity.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("secondary"));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }

    [TestMethod]
    public void SecondaryHoldingTheRequiredScopeReportsASkipReasonNamingTheIdentityAndScope()
    {
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read"]));

        var reason = ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read");

        reason.ShouldNotBeNull();
        reason.ShouldContain("readonly");
        reason.ShouldContain("orders.read");
        reason.ShouldContain("403");
        reason.ShouldNotContain("including");
    }

    [TestMethod]
    public void SecondaryLackingTheRequiredScopeRuns()
    {
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read"]));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.write").ShouldBeNull();
    }

    [TestMethod]
    public void PartialScopeOverlapStillRunsTheTest()
    {
        // Holding one of two required scopes does not authorize the operation, so a 403 is still
        // provable. Must fail against an `Any` implementation — the easy wrong version of this.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read"]));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read", "orders.write").ShouldBeNull();
    }

    [TestMethod]
    public void SecondaryHoldingAStrictSupersetSkipsWithoutClaimingTheExtraScopeIsRequired()
    {
        // The guard skips on superset, not equality — the ordinary shape of a read-only identity
        // that holds several read scopes. A message that joins only the held scopes under "which
        // this operation requires" states something false the moment the identity holds more
        // than the operation needs, and gives the reader no clue which scope to remove.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read", "products.read"]));

        var reason = ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read");

        reason.ShouldNotBeNull();
        reason.ShouldContain("readonly");
        reason.ShouldContain("orders.read");
        reason.ShouldContain("products.read");
        // The identity holds products.read too, but the operation never asked for it — the
        // message must not claim otherwise.
        reason.ShouldNotContain("products.read, which this operation requires");
    }

    [TestMethod]
    public void ScopeComparisonIsOrdinalAndCaseSensitive()
    {
        // RFC 6749 scope tokens are case-sensitive, so the explicit StringComparer.Ordinal
        // passed to the three-argument Contains overload is the correct comparer. Pins the
        // behaviour so a future switch to OrdinalIgnoreCase would be caught rather than passing
        // every existing test.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["ORDERS.READ"]));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();

        // Regression: a secondary whose Scopes is a HashSet<string> built with
        // OrdinalIgnoreCase must not change the outcome. requiredScopes.All(scopes.Contains)
        // (the two-argument form) hits Enumerable.Contains's ICollection<T> fast path, which
        // delegates to the collection's own Contains — HashSet<string>.Contains uses whatever
        // comparer the set was constructed with, not EqualityComparer<string>.Default. That
        // silently made a case-insensitive match look like the identity held the exact scope,
        // skipping a provable 403. The explicit three-argument overload has no such fast path:
        // it always enumerates and compares with the comparer passed to it, regardless of what
        // collection type backs `scopes`.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ORDERS.READ" }));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }

    [TestMethod]
    public void SecondaryHoldingEveryRequiredScopeSkips()
    {
        // Containment is over the whole set: holding both required scopes really does authorize
        // the operation, so the 403 genuinely cannot happen. Kept going through the adapter
        // (Should.Throw rather than a bare reason.ShouldNotBeNull()) so
        // ApiTestBase.RequireSecondaryIdentityLacks' throw-on-reason delegation is itself
        // covered — see this class's own doc for why only a couple of tests do this.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read", "orders.write"]));

        Should.Throw<AssertInconclusiveException>(() =>
            ApiTestBase.RequireSecondaryIdentityLacks("orders.read", "orders.write"));
    }

    [TestMethod]
    public void SecondaryWithAnEmptyScopesDeclarationRuns()
    {
        // [] is a real declaration — "holds no scopes" — not the same as null, but it still can
        // never be a superset of a non-empty requirement, so the test runs either way.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", []));

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.write").ShouldBeNull();
    }

    [TestMethod]
    public void ZeroRequiredScopesRunsEvenWhenSecondaryHoldsScopes()
    {
        // A zero-argument call means the operation declares no scopes at all — it can still 403
        // on other grounds (tenant, role, resource ownership), so this must never skip.
        // `requiredScopes.All(scopes.Contains)` is vacuously true over an empty requiredScopes,
        // which read as "the secondary already holds everything required" and skipped; that is
        // the bug this test exists to catch.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", ["orders.read"]));

        ApiTestCore.SecondaryIdentityScopeSkipReason().ShouldBeNull();
    }

    [TestMethod]
    public void ZeroRequiredScopesRunsEvenWhenSecondaryScopesIsEmpty()
    {
        // Same bug, [] variant: an empty requiredScopes is still vacuously "All" over an empty
        // Scopes, and Scopes being non-null (even though empty) meant the guard's `is not { }
        // scopes` half didn't save it either — this must run regardless.
        InTestRun.TokenProvider = new FakeTokenProvider(
        new TestIdentity("default"),
        new TestIdentity("readonly", []));

        ApiTestCore.SecondaryIdentityScopeSkipReason().ShouldBeNull();
    }

    [TestMethod]
    public void NoRegisteredProviderRunsRatherThanSkippingASecondTime()
    {
        // MultipleIdentitiesSkipReason already owns this skip; never skip twice for one reason.
        InTestRun.TokenProvider = null;

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }

    [TestMethod]
    public void OnlyOneRegisteredIdentityRuns()
    {
        // Same reason: MultipleIdentitiesSkipReason owns the "fewer than two identities" skip.
        InTestRun.TokenProvider = new FakeTokenProvider("only-one");

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }

    [TestMethod]
    public void ANullIdentitiesListRunsRatherThanThrowing()
    {
        // Task 2 step 2: this guard reaches further than MultipleIdentitiesSkipReason
        // (Identities[1], not just Identities.Count), so it must guard the same
        // provider-registered-but-Identities-itself-null shape that guard already does — v1-c's
        // live NullReferenceException on exactly this shape is why.
        //
        // This is the only one of these two null-shape tests that can actually carry a "runs"
        // claim: a null Identities *list* fails MultipleIdentitiesSkipReason's count check
        // (Count 0), so UseIdentity is never reached and "runs" is never falsified. Its sibling
        // below, on a null *element*, passes that same count check (Count 2) and reaches
        // UseIdentity, where ResolveIdentitySlot throws on the null element's .Name — so it
        // cannot make the same claim. That asymmetry is intentional; see the sibling's own doc
        // comment.
        InTestRun.TokenProvider = new NullIdentitiesProvider();

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }

    /// <summary>A provider whose second identity is itself null despite the non-nullable element
    /// type — nothing at compile time stops a misbehaving <see cref="ITestTokenProvider"/> from
    /// doing this, the same reasoning <see cref="NullIdentitiesProvider"/> already covers one
    /// level up.</summary>
    private sealed class NullSecondaryIdentityProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = [new TestIdentity("default"), null!];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    /// <summary>Confirms the null-element shape is not reported as a skip by
    /// <see cref="ApiTestCore.SecondaryIdentityScopeSkipReason"/> itself. It does not confirm the
    /// test goes on to run: the second identity being null violates
    /// <see cref="ITestTokenProvider.Identities"/>'s non-null element annotation, and
    /// <see cref="ApiTestCore.UseIdentity"/>'s subsequent call to <c>ResolveIdentitySlot</c>
    /// throws <see cref="NullReferenceException"/> on exactly this shape — intentionally, so a
    /// provider that breaks its own contract fails loudly rather than being silently defended
    /// against. Unlike <see cref="ANullIdentitiesListRunsRatherThanThrowing"/>, this one cannot
    /// carry a "runs" claim: it passes the count guard that test relies on, so it reaches
    /// <c>UseIdentity</c> instead of stopping short of it.</summary>
    [TestMethod]
    public void ANullSecondaryIdentityElementIsNotReportedAsASkipByThisGuard()
    {
        InTestRun.TokenProvider = new NullSecondaryIdentityProvider();

        ApiTestCore.SecondaryIdentityScopeSkipReason("orders.read").ShouldBeNull();
    }
}
