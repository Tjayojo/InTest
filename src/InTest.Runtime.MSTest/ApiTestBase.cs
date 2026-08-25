namespace InTest.Runtime;

/// <summary>
/// MSTest adapter over <see cref="ApiTestCore"/>, the neutral base that now holds the actual
/// implementation. This class exists so a generated project's scaffolded test classes — which
/// derive from a project base class deriving from <c>ApiTestBase</c>, and call
/// <c>RequireMultipleIdentities()</c>, <c>RequireSecondaryIdentityLacks(...)</c>,
/// <c>UseIdentity(...)</c>, <c>RequireFixture(...)</c>, <c>FixtureBody(...)</c>,
/// <c>Client.SendAsync(...)</c>, <c>TestId</c>, <c>Schemas</c> and
/// <c>TestContext.CancellationToken</c> — keep compiling unchanged while the split into a neutral
/// <c>InTest.Runtime</c> package and an MSTest-specific adapter proceeds. Task 6 moves this file
/// into its own <c>InTest.Runtime.MSTest</c> project; nothing here anticipates that beyond what
/// this task asks for.
/// <para>
/// This class's whole job is the two MSTest-specific seams <see cref="ApiTestCore"/> cannot own
/// itself without naming a test framework: wiring <see cref="TestContext"/> into
/// <see cref="ApiTestCore.BeginTest"/> / <see cref="ApiTestCore.EndTest"/> via the
/// <c>[TestInitialize]</c> / <c>[TestCleanup]</c> attributes, and turning a skip <em>reason</em>
/// (a plain <c>string?</c>, null meaning "run") into an actual <c>Assert.Inconclusive</c> call.
/// </para>
/// </summary>
public abstract class ApiTestBase : ApiTestCore
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Delegates to <see cref="ApiTestCore.BeginTest"/>, supplying MSTest's own resolved display
    /// name. Derived from <c>TestContext.TestDisplayName</c>, never <c>TestContext.TestName</c>:
    /// <c>TestName</c> returns the bare method name for every <c>[DataRow]</c> row, so all
    /// variations of one operation would share one <see cref="ApiTestCore.TestId"/> instead of
    /// each getting its own — the reason this call site, not <see cref="ApiTestCore"/> itself,
    /// gets to make this choice at all is that <c>TestDisplayName</c> is an MSTest concept with no
    /// neutral equivalent; a different adapter reads whatever its own framework calls the
    /// per-data-row display name.
    /// </summary>
    [TestInitialize]
    public void ApiTestInitialize() => BeginTest(TestContext.TestDisplayName);

    [TestCleanup]
    public void ApiTestCleanup() => EndTest();

    /// <summary>
    /// Turns <see cref="ApiTestCore.MultipleIdentitiesSkipReason"/>'s reason string into the
    /// actual MSTest skip a generated 403 case observes.
    /// <para>
    /// The message passed to <c>Assert.Inconclusive</c> is what makes this decision 3's actual
    /// argument rather than a quieter skip: confirmed on MSTest 4.3.3 / .NET 10 to survive
    /// verbatim into the .trx's <c>&lt;Message&gt;</c>, prefixed only with
    /// "Assert.Inconclusive. " — and the .trx spells the outcome <c>NotExecuted</c>, not the
    /// console summary's "Skipped". <c>MemberCondition</c>, decision 3's rejected alternative, was
    /// measured to be evaluated 15ms before <c>[AssemblyInitialize]</c> on this same MSTest
    /// version and so could never see anything the DI container built; calling
    /// <see cref="ApiTestCore.MultipleIdentitiesSkipReason"/> from inside the test body instead
    /// runs after <c>InTestRun.InitializeAsync</c> has genuinely finished.
    /// </para>
    /// <para>
    /// <c>protected internal</c> for the same two reasons <see cref="ApiTestCore.MultipleIdentitiesSkipReason"/>'s
    /// own doc gives: <c>protected</c> so a generated suite in a different assembly can call it
    /// like its <c>protected static</c> neighbours <c>RequireFixture</c> and <c>FixtureBody</c>;
    /// <c>internal</c> so <c>InTest.Runtime.Tests</c> can call it directly via this project's
    /// <c>InternalsVisibleTo</c>.
    /// </para>
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        if (MultipleIdentitiesSkipReason() is { } reason)
        {
            Assert.Inconclusive(reason);
        }
    }

    /// <summary>
    /// Turns <see cref="ApiTestCore.SecondaryIdentityScopeSkipReason"/>'s reason string into the
    /// actual MSTest skip a generated wrong-scope 403 case observes. See
    /// <see cref="RequireMultipleIdentities"/>'s own doc for the .trx-specific evidence behind
    /// using <c>Assert.Inconclusive</c> here rather than some other skip mechanism — the same
    /// reasoning applies unchanged.
    /// </summary>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        if (SecondaryIdentityScopeSkipReason(requiredScopes) is { } reason)
        {
            Assert.Inconclusive(reason);
        }
    }
}
