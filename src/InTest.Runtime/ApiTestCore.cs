using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InTest.Runtime;

/// <summary>
/// Neutral base for a generated test class: configuration, services, client, identifiers and
/// scope lifecycle. Deliberately nothing domain-specific — base classes in test projects
/// become dumping grounds, so helpers belong in the team's own base class.
/// <para>
/// Deliberately not named <c>InTestApiTestCore</c> or similar: in this codebase, an
/// <c>InTest</c>-prefixed name (<see cref="InTestAmbient"/>, <see cref="InTestId"/>,
/// <see cref="InTestRun"/>, <see cref="InTestUrl"/>, <see cref="InTestClients"/>,
/// <see cref="InTestIdentities"/>) marks a static ambient/utility type — none of which is an
/// instantiable base class a generated project derives from. This type is exactly that, so
/// borrowing the prefix here would blur a naming signal that currently carries real information:
/// a reader who sees the prefix already knows "static, ambient, nothing to instantiate," and this
/// type would be the one exception.
/// </para>
/// <para>
/// <see cref="ApiTestBase"/> is the thin, framework-specific adapter over this class —
/// see its own doc for the split. This class itself must never reference any test framework
/// (<c>NeutralityTests</c> enforces this at source level for every file directly under
/// <c>src/InTest.Runtime/</c>).
/// </para>
/// </summary>
public abstract class ApiTestCore
{
    private IServiceScope _scope = null!;

    /// <summary>
    /// The resolved test id for the currently-running test, or null before <see cref="BeginTest"/>
    /// has run and after <see cref="EndTest"/> has cleared it. Backing field for <see cref="TestId"/>.
    /// </summary>
    private string? _testId;

    /// <summary>
    /// This test's own diagnostics sink, set by <see cref="BeginTest"/> and cleared by
    /// <see cref="EndTest"/> — <c>[warn-on-swallowed-exception]</c>
    /// (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md). Backing field for
    /// <see cref="WarnSwallowedClientException"/>, the only reader.
    /// <para>
    /// Deliberately per-test, not the assembly-scoped sink <c>InTestRun.InitializeAsync</c> already
    /// receives: that instance is wrapped around the <em>assembly</em>'s own <c>TestContext</c> and
    /// is never retained anywhere <see cref="ApiTestCore"/> could reach it after
    /// <c>InitializeAsync</c> returns, nor would reusing it be correct — MSTest hands out a fresh
    /// <c>TestContext</c> per running test, and a message attached to the wrong one attributes to
    /// the wrong result. <see cref="ApiTestBase.ApiTestInitialize"/> instead builds a fresh
    /// <c>TestHost.TestContextDiagnostics</c> around <em>its own</em> per-test <c>TestContext</c> and
    /// passes it here — the same <c>testDisplayName</c> seam <see cref="BeginTest"/> already uses,
    /// extended to a second per-test fact this class needs from the framework without needing to
    /// name it.
    /// </para>
    /// </summary>
    private IRunDiagnostics? _diagnostics;

    protected IConfiguration Config => InTestRun.Configuration;

    protected IServiceProvider Services => _scope.ServiceProvider;

    protected SchemaBundle Schemas => InTestRun.Schemas;

    protected string RunId => InTestRun.RunIdValue;

    /// <summary>
    /// The current test's correlation id, computed once by <see cref="BeginTest"/> and cleared by
    /// <see cref="EndTest"/> — not recomputed on every read the way the pre-split property
    /// (<c>InTestId.ForTest(TestHost.RunIdValue, TestContext.TestDisplayName)</c>, evaluated
    /// fresh on each access) was. The value read is identical either way for every read that
    /// happens inside a test body, since nothing about the run id or display name changes
    /// mid-test; the difference is only what happens <em>outside</em> one.
    /// <para>
    /// That difference is this task's one deliberate behaviour change. Before the split, reading
    /// <c>TestId</c> outside a running test threw <see cref="NullReferenceException"/> — it read
    /// through to MSTest's <c>TestContext.TestDisplayName</c>, which throws when no test is
    /// active. That exception named nothing about what went wrong; a caller saw a bare NRE with
    /// no hint that "call this only inside a test" was the actual rule being broken. Failing on a
    /// field read lets this class say that outright instead.
    /// </para>
    /// <para>
    /// <b>Why a plain <c>string?</c> field rather than the <c>ITestIdentity</c> interface the
    /// design spec (§3) currently prescribes for this exact seam:</b> that interface would carry
    /// exactly one member — a display-name getter — and calling through it would deliver the same
    /// <c>string?</c> this field already holds, just one indirection later. An interface earns its
    /// keep by having more than one implementation with genuinely different behaviour, or by
    /// letting a caller defer resolution to a point it doesn't control yet; neither is true here.
    /// The display name is obtainable only from <em>inside</em> the test framework's own per-test
    /// callback — <c>TestContext.TestDisplayName</c> is only valid inside
    /// <c>[TestInitialize]</c> — and that per-test callback is exactly where <see cref="BeginTest"/>
    /// is already called from. There is nowhere upstream of that call site that would need to
    /// store or thread an <c>ITestIdentity</c> instance before handing it to
    /// <see cref="ApiTestCore"/>; the adapter already has the string in hand at the only moment it
    /// is ever available, and can simply pass it. <c>ITestIdentity</c> is recorded here as the
    /// rejected alternative, per this codebase's convention of keeping rejected alternatives
    /// rather than deleting them — the design spec's own §3 text is updated to match in a later
    /// task of this same change.
    /// </para>
    /// <para>
    /// The value <see cref="BeginTest"/> derives its <c>testDisplayName</c> parameter from is
    /// MSTest's <c>TestContext.TestDisplayName</c>, never <c>TestContext.TestName</c> — see
    /// <see cref="ApiTestBase.ApiTestInitialize"/>'s call site for that framework-specific
    /// reasoning, now that the choice of which MSTest property to read lives there rather than
    /// here.
    /// </para>
    /// </summary>
    protected string TestId => _testId
                               ?? throw new InvalidOperationException(
                               "TestId is only available inside a test — ApiTestBase's [TestInitialize] sets it via BeginTest.");

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// A silent, do-nothing <see cref="IRunDiagnostics"/> — the private, internal-only implementation
    /// <see cref="IRunDiagnostics"/>'s own doc says is deliberately absent from the shipped surface
    /// ("Deliberately no <c>RunDiagnostics.Null</c> or other shipped convenience implementation
    /// here... a test-local double is enough until a real caller needs otherwise"). This one is not
    /// that shipped convenience: it is never public, never returned, never exposed anywhere outside
    /// this class, and exists solely so <see cref="BeginTest(string?)"/> — the one-argument
    /// compatibility overload below — has something non-null to hand the two-argument
    /// <see cref="BeginTest(string?, IRunDiagnostics)"/> without changing what a caller supplying no
    /// diagnostics sink actually observes. See <see cref="BeginTest(string?)"/>'s own doc for why a
    /// null object is the right shape here rather than an optional parameter.
    /// <para>
    /// This overload's arrival is exactly the "real caller" <see cref="IRunDiagnostics"/>'s own doc
    /// contemplates as the condition for eventually shipping a convenience null implementation — but
    /// arriving does not by itself change the answer. This type still has no reason to expose itself
    /// as public API: nothing outside <see cref="BeginTest(string?)"/> constructs or references it, so
    /// it stays private here rather than being promoted onto <see cref="IRunDiagnostics"/> as a
    /// shipped <c>.Null</c> member. A second real caller wanting the same no-op would be the actual
    /// trigger for that promotion; one does not exist yet.
    /// </para>
    /// </summary>
    private sealed class NullDiagnostics : IRunDiagnostics
    {
        /// <summary>The only instance this type is ever constructed as — one shared, stateless
        /// no-op is all any caller could ever need from it.</summary>
        public static readonly NullDiagnostics Instance = new();

        public void Note(string message)
        {
            // Deliberately empty: this is the discard behaviour the old one-argument BeginTest
            // already had by construction, before diagnostics existed as a concept at all — there
            // was no sink to write routine progress to, so there is still none now.
        }

        public void Warn(string message)
        {
            // Deliberately empty: this is what makes BeginTest(string?) reproduce the old
            // one-argument BeginTest's observable behaviour exactly. Before [warn-on-swallowed-exception]
            // introduced WarnSwallowedClientException, a swallowed client exception left no trace at
            // all — _diagnostics was simply null, and WarnSwallowedClientException's own `_diagnostics?.Warn(...)`
            // already treats a null sink as a silent no-op. Routing Warn through this empty method
            // instead of leaving _diagnostics null reproduces that exact silence while still letting
            // the two-argument BeginTest's non-null guard (ArgumentNullException.ThrowIfNull) pass.
        }
    }

    /// <summary>
    /// Compatibility overload: exists only so that code compiled against the pre-<c>[warn-on-swallowed-exception]</c>
    /// one-argument <c>BeginTest(string?)</c> signature — a hypothetical third-party xUnit/NUnit
    /// adapter, or any other caller outside this repository that already built against
    /// <c>InTest.Runtime</c> before <see cref="BeginTest(string?, IRunDiagnostics)"/> gained its
    /// second parameter — keeps compiling and keeps running, unchanged, against the version of
    /// <c>InTest.Runtime</c> that ships this file. <c>InTest.Runtime</c>
    /// <c>0.1.0-preview.1</c> is already published to nuget.org (CLAUDE.md's "What this is"), so
    /// adding a required parameter to an existing public method is a source break at that package
    /// boundary; this overload is the fix.
    /// <para>
    /// <b>Not the preferred call for new code inside this repository.</b>
    /// <see cref="ApiTestBase.ApiTestInitialize"/> keeps calling the two-argument
    /// <see cref="BeginTest(string?, IRunDiagnostics)"/> with a real
    /// <c>TestHost.TestContextDiagnostics</c> sink, unchanged by this overload's existence — every
    /// caller this repository controls already has a live per-test diagnostics sink to supply and
    /// should keep supplying it, so <c>[warn-on-swallowed-exception]</c> reaches the operator on
    /// every path this repository ships. This overload exists purely for callers this repository
    /// does <em>not</em> control, who compiled before that sink existed to supply.
    /// </para>
    /// <para>
    /// <b>Why a private null-object <see cref="IRunDiagnostics"/> rather than making
    /// <c>diagnostics</c> an optional parameter with a default value on the existing method.</b> An
    /// optional parameter is a source-compatible change, not a binary-compatible one: the default
    /// value is substituted by the <em>caller's compiler</em> at the call site, baked into that
    /// caller's own IL at compile time — it is not part of the callee's method signature at all.
    /// An already-compiled caller's IL already contains a direct call instruction to a one-argument
    /// <c>BeginTest(string)</c> method token; IL call-site resolution matches that token against the
    /// callee assembly's actual set of declared overloads at load time, and a two-argument method
    /// with a default value on its second parameter is still, in IL, a single method that requires
    /// two arguments — no one-argument overload exists for that call site to bind to, so the
    /// existing caller fails to load (a <see cref="MissingMethodException"/>) rather than silently
    /// picking up the default. Only a genuinely separate, declared one-argument overload — this
    /// method — creates the second IL method token an old caller's call site can still resolve
    /// against. Restoring the exact old <em>observable behaviour</em> through that overload, via
    /// <see cref="NullDiagnostics"/>, rather than reasoning about what a default argument value
    /// would have produced, is what makes this method more than "compiles": see this method's own
    /// body and <see cref="NullDiagnostics"/> for how.
    /// </para>
    /// </summary>
    /// <param name="testDisplayName">Forwarded unchanged to <see cref="BeginTest(string?, IRunDiagnostics)"/>
    /// — see that overload's own parameter doc.</param>
    protected void BeginTest(string? testDisplayName) => BeginTest(testDisplayName, NullDiagnostics.Instance);

    /// <summary>
    /// Starts one test's scope: a fresh DI scope, the resolved <see cref="TestId"/>, this test's
    /// own diagnostics sink, the ambient identity a request authenticates as by default, and the
    /// <see cref="Client"/> generated request-sending methods use. The body of what used to be
    /// <c>ApiTestBase</c>'s <c>[TestInitialize]</c>-attributed <c>ApiTestInitialize</c> before the
    /// neutral/adapter split; <see cref="ApiTestBase.ApiTestInitialize"/> now carries the
    /// <c>[TestInitialize]</c> attribute itself and simply calls this method, passing MSTest's
    /// <c>TestContext.TestDisplayName</c> through as <paramref name="testDisplayName"/> and a
    /// fresh <c>TestHost.TestContextDiagnostics</c> wrapping its own per-test <c>TestContext</c>
    /// through as <paramref name="diagnostics"/>.
    /// </summary>
    /// <param name="testDisplayName">The framework's resolved display name for the running test,
    /// or null if the framework has none to offer. <see cref="InTestId.ForTest"/> already accepts
    /// and handles a null display name, so this method applies no null-guard of its own.</param>
    /// <param name="diagnostics">This test's own diagnostics sink — <c>[warn-on-swallowed-exception]</c>,
    /// the same <c>testDisplayName</c> seam extended to a second per-test fact
    /// <see cref="ApiTestCore"/> needs without naming the framework that supplies it. Required,
    /// not optional: unlike <paramref name="testDisplayName"/>, there is no meaningful "none to
    /// offer" case — <see cref="ApiTestBase.ApiTestInitialize"/> always has a live per-test
    /// <c>TestContext</c> to wrap by the time this runs, so a null here would only ever mean a
    /// caller forgot to wire one up, which should fail loudly rather than silently disable
    /// <see cref="WarnSwallowedClientException"/>.</param>
    protected void BeginTest(string? testDisplayName, IRunDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        _scope = InTestRun.Root.CreateScope();
        _testId = InTestId.ForTest(InTestRun.RunIdValue, testDisplayName);
        InTestAmbient.TestId.Value = _testId;
        _diagnostics = diagnostics;

        // A fresh CapturedResponseSlot, not merely a clear — see InTestAmbient.LastCapturedResponse's
        // own doc for why this field carries a mutable cell rather than a CapturedResponse directly
        // (confirmed by direct experiment: a plain AsyncLocal reassignment made deep inside
        // ResponseCaptureHandler's awaited call does not survive back up to this test method, so
        // the handler mutates a cell instead — and that cell must be this test's own, never a
        // previous test's, which a brand-new object guarantees purely by holding nothing yet).
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot();

        // The Default slot, resolved (v1-c decision 7): every test authenticates as this unless
        // a generated auth case overrides it before sending its request. Resolved here, once per
        // test, from whatever ITestTokenProvider the generated project registered — never a
        // literal identity name, since the CLI that generated this suite could not have known one.
        //
        // Reads InTestRun.TokenProvider rather than resolving a fresh instance from _scope
        // (Task 10 item 1): MultipleIdentitiesSkipReason and UseIdentity already read that same
        // static, and under the scaffold's documented AddSingleton registration the two are the
        // same object — but under any other lifetime they would not be, so a provider whose
        // Identities is computed per instance could gate the 403 case on one object while this
        // Default identity came from another. Reading the static here removes that lifetime
        // question entirely, since it is already resolved from the same container.
        InTestAmbient.Identity.Value = ResolveDefaultIdentity(InTestRun.TokenProvider);

        Client = _scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
    }

    /// <summary>
    /// Ends one test's scope: clears everything <see cref="BeginTest"/> set, in the same order it
    /// was already cleared before the split. The body of what used to be <c>ApiTestBase</c>'s
    /// <c>[TestCleanup]</c>-attributed <c>ApiTestCleanup</c>; <see cref="ApiTestBase.ApiTestCleanup"/>
    /// now carries the <c>[TestCleanup]</c> attribute itself and simply calls this method.
    /// </summary>
    protected void EndTest()
    {
        InTestAmbient.TestId.Value = null;
        InTestAmbient.Identity.Value = null;
        InTestAmbient.LastCapturedResponse.Value = null;
        _testId = null;
        _diagnostics = null;
        _scope.Dispose();
    }

    /// <summary>
    /// [neutral-helper]: resolves an adopter's own typed client (Kiota, NSwag, Refit) from
    /// <see cref="Services"/> — the same scope <see cref="Client"/> itself is resolved from — for a
    /// generated client-routed test case to call directly. Placed here on <see cref="ApiTestCore"/>
    /// rather than the MSTest-specific <c>ApiTestBase</c> so it is available to every future
    /// framework adapter for free, the same reasoning that already applies to
    /// <see cref="RequireFixture"/> and the rest of this class.
    /// <para>
    /// Resolution failure (nothing registered for <typeparamref name="TClient"/>) is deliberately
    /// left to <c>GetRequiredService</c>'s own exception rather than wrapped: an adopter who forgets
    /// to register their client in <c>ConfigureServices</c> gets a standard, well-understood DI
    /// error naming the missing type, not a bespoke InTest message duplicating what
    /// <c>Microsoft.Extensions.DependencyInjection</c> already says clearly.
    /// </para>
    /// </summary>
    protected TClient ApiClient<TClient>() where TClient : class =>
        Services.GetRequiredService<TClient>();

    /// <summary>
    /// The most recent response <see cref="ResponseCaptureHandler"/> observed for the currently
    /// running test — [neutral-helper], the read-side counterpart of <see cref="ApiClient{TClient}"/>.
    /// A generated client-routed Success case calls this, after its typed-client call returns
    /// normally, to run <c>ApiResponseAssertions.ShouldMatchCapturedContractAsync</c> against the
    /// same raw bytes the API actually sent, exactly as a raw-HTTP case would against its own
    /// <see cref="HttpResponseMessage"/>.
    /// <para>
    /// Throws — never returns <c>default</c> — when nothing was captured, naming
    /// <c>[client-rides-the-api-pipeline]</c> and telling the adopter to construct their client over
    /// <c>IHttpClientFactory.CreateClient(InTestClients.Api)</c>. A silent <c>default</c> here would
    /// make a misconfigured client's test pass against status 0 and an empty body — the exact
    /// "passes while asserting almost nothing" outcome CLAUDE.md's fail-loudly rule forbids, and a
    /// far worse failure mode than a clear, immediate exception naming the actual cause: no
    /// response ever reached <see cref="InTestAmbient.LastCapturedResponse"/> because
    /// <see cref="ResponseCaptureHandler"/> never ran on this request at all, most likely because
    /// the client was built over a bare <see cref="HttpClient"/> rather than
    /// <c>InTestClients.Api</c>.
    /// </para>
    /// <para>
    /// Reads <see cref="InTestAmbient.LastCapturedResponse"/> directly rather than exposing any
    /// caching or memoization of its own — this property and that ambient slot are deliberately the
    /// same value at every read, so a generated case calling this more than once (unusual, but nothing
    /// here forbids it) always sees whatever the most recent client-routed call actually produced.
    /// </para>
    /// </summary>
    protected static CapturedResponse LastCapturedResponse =>
        InTestAmbient.LastCapturedResponse.Value?.Value
        ?? throw new InvalidOperationException(
            "[client-rides-the-api-pipeline]: no response has been captured for this test. " +
            "ResponseCaptureHandler only runs on requests sent through InTestClients.Api — " +
            "construct your typed client over IHttpClientFactory.CreateClient(InTestClients.Api) " +
            "rather than a bare HttpClient, so it rides the same handler pipeline as everything " +
            "else InTest sends.");

    /// <summary>
    /// <c>[warn-on-swallowed-exception]</c>
    /// (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md): a generated
    /// client-routed Success case's second catch calls this instead of discarding
    /// <paramref name="exception"/> outright. <c>[captured-response-is-the-verdict]</c>'s own
    /// reasoning for that catch existing at all still holds — a response was already captured, so
    /// the client's own generator-specific exception is the wrong verdict to report — but silently
    /// dropping the exception hid a real failure mode a reviewer raised: a <c>client-map.json</c>
    /// override that issues more than one call, where the first reaches the wire and is captured
    /// and a later one fails before reaching it at all (a serialization error, a null argument, an
    /// adapter misconfiguration). Without this, that second failure leaves no trace anywhere — the
    /// case reports whatever the first call's captured response was, and an operator has no way to
    /// learn a second call ever ran, let alone that it threw.
    /// <para>
    /// <b>Warn, not Note</b> — <see cref="IRunDiagnostics.Warn"/>'s own doc is exactly the
    /// contract this needs: it must reach the operator even on a run that otherwise passes and
    /// exits 0, which is precisely the shape this defect takes (the captured response can easily
    /// still satisfy the test's own assertion). A <see cref="IRunDiagnostics.Note"/> here would be
    /// exactly the "fixture silently not running" trap that doc comment names, transplanted to
    /// this call site.
    /// </para>
    /// <para>
    /// Names <paramref name="exception"/>'s runtime type and message directly, and states outright
    /// that it was discarded because a captured response already stood as the verdict — an
    /// operator reading this in isolation, with no other context, must be able to tell both "an
    /// exception happened" and "why it did not fail the test."
    /// </para>
    /// <para>
    /// <c>_diagnostics</c> is read with <c>?.</c> rather than the throwing pattern
    /// <see cref="TestId"/> and <see cref="LastCapturedResponse"/> both use: those two guard a
    /// programming error a generated case would hit on every single call if the guarded state were
    /// ever missing (reading <see cref="TestId"/> before <see cref="BeginTest"/>, or
    /// <see cref="LastCapturedResponse"/> with nothing captured), so failing loudly is the more
    /// useful behaviour. A missing <c>_diagnostics</c> here would only ever mean
    /// <see cref="BeginTest"/> itself was never called with one — impossible through
    /// <see cref="ApiTestBase.ApiTestInitialize"/>, which this class does not control but every
    /// shipped adapter goes through — and this method already runs from inside a <c>catch</c> block
    /// already handling one swallowed exception; turning a missing diagnostics sink into a second,
    /// unrelated throw from there would replace the original exception's own message with a far
    /// less useful one about plumbing this class already guarantees in every real path.
    /// </para>
    /// </summary>
    protected void WarnSwallowedClientException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _diagnostics?.Warn(
            $"[captured-response-is-the-verdict]: a {exception.GetType().FullName} was thrown " +
            "after this test's client-routed call had already captured a response, and was " +
            $"discarded — the captured response is being used as the test's verdict instead. " +
            $"Discarded exception message: \"{exception.Message}\". If this is unexpected, check " +
            "whether this operation's client-map.json override issues more than one call.");
    }

    /// <summary>
    /// The Default slot resolved to a concrete identity (v1-c decision 7): <c>Identities[0]</c>
    /// when the provider advertises at least one, otherwise <see cref="InTestIdentities.None"/> —
    /// including when <paramref name="provider"/> itself is null, which is the ordinary state for
    /// every spec that declares no <c>security</c> (question (b), and Catalog/Inventory's own
    /// scaffolds). <c>ITestTokenProvider.Identities</c>'s own doc already documents a count of
    /// zero as contemplated, not an error; indexing <c>Identities[0]</c> without this check would
    /// throw <see cref="ArgumentOutOfRangeException"/> here, in <see cref="BeginTest"/>, before a
    /// single request is built — for every test in the suite, not just the auth ones.
    /// <para>
    /// Pulled out as an internal, dependency-free seam — rather than left inline in
    /// <see cref="BeginTest"/> — because that method needs a live <c>InTestRun.Root</c> to
    /// exercise at all, and this decision deserves its own test independent of that weight.
    /// </para>
    /// </summary>
    internal static string ResolveDefaultIdentity(ITestTokenProvider? provider) =>
        provider?.Identities is { Count: > 0 } identities ? identities[0].Name : InTestIdentities.None;

    /// <summary>
    /// Generated 403 (wrong-scope) cases consult this first, before anything else in the method
    /// body — decision 3's replacement for <c>MemberCondition</c>, which was measured to be
    /// evaluated 15ms before <c>[AssemblyInitialize]</c> on MSTest 4.3.3 and so can never see
    /// anything the DI container built. The call happens inside the test body instead, after
    /// <c>InTestRun.InitializeAsync</c> has genuinely finished, so it can consult the real,
    /// registered <see cref="ITestTokenProvider"/> rather than a config flag that can drift from
    /// it.
    /// <para>
    /// Returns null to mean "run the test" and a non-null reason to mean "skip it, and here is
    /// why" — the neutral half of decision 3's actual argument, kept separate from <em>how</em> a
    /// skip is reported. <see cref="ApiTestBase.RequireMultipleIdentities"/> is the MSTest
    /// adapter that turns a non-null reason into <c>Assert.Inconclusive(reason)</c>; see that
    /// method's own doc for why the message text and the .trx-specific behaviour it produces
    /// belong there rather than here.
    /// </para>
    /// <para>
    /// <c>protected internal</c> is deliberate and must be kept: <c>protected</c> lets
    /// <see cref="ApiTestBase"/> — which after this class's containing package splits in
    /// two lives in a <em>different</em> assembly — call it exactly like its <c>protected
    /// static</c> neighbours <see cref="RequireFixture"/> and <see cref="FixtureBody"/> (the
    /// "must access through an instance of the derived type" restriction on <c>protected</c>
    /// applies only to <em>instance</em> members, never to <c>static</c> ones, so a plain
    /// <c>protected static</c> already reaches across that assembly boundary with no special
    /// case); <c>internal</c> so <c>InTest.Runtime.Tests</c> can call it directly, without a
    /// test-only subclass, via the <c>InternalsVisibleTo</c> already in
    /// <c>InTest.Runtime.csproj</c>. Plain <c>protected static</c> would match the neighbours but
    /// leave those tests unable to reach it at all.
    /// </para>
    /// </summary>
    protected internal static string? MultipleIdentitiesSkipReason()
    {
        var provider = InTestRun.TokenProvider;

        // `?.Identities?.Count` — not `?.Identities.Count` (Task 10 item 3): the first `?.`
        // guards only "no provider registered"; a registered provider whose Identities is
        // itself null (the property is non-nullable only by annotation, not by anything the
        // runtime enforces) would still throw NullReferenceException on an unguarded
        // `.Count`. ResolveDefaultIdentity's own `provider?.Identities is { Count: > 0 }`
        // already guards both cases — this must match its neighbour.
        var count = provider?.Identities?.Count ?? 0;
        if (count >= 2)
        {
            return null;
        }

        // Task 10 item 4: branched on whether a provider is registered at all, not just its
        // count. "The registered ITestTokenProvider advertises 0 identities" sends a reader
        // hunting for a bug in code they never wrote when the true state — the day-one
        // scaffold, and every spec declaring no `security` — is that there is no registered
        // provider at all. Decision 3's whole argument for reporting a skip reason over silence
        // is that the reason stays visible *and correct*.
        return provider is null
            ? "Skipped: no ITestTokenProvider is registered; a wrong-scope 403 test needs at least 2 identities."
            : $"Skipped: the registered ITestTokenProvider advertises {count} identit{(count == 1 ? "y" : "ies")}; " +
              "a wrong-scope 403 test needs at least 2.";
    }

    /// <summary>
    /// Generated wrong-scope 403 cases consult this — after <see cref="MultipleIdentitiesSkipReason"/>,
    /// before building their request — because a second identity existing is not enough to make a
    /// 403 provable: if the secondary identity's own declared <see cref="TestIdentity.Scopes"/>
    /// already cover everything <paramref name="requiredScopes"/> lists, it is authorized for the
    /// operation and a 403 assertion would fail against a correct API. A read-only identity is
    /// never "wrong scope" for a read it actually holds.
    /// <para>
    /// Containment is over the whole set: the secondary identity must hold <em>every</em> scope in
    /// <paramref name="requiredScopes"/> before this method reports a skip. Holding only some of
    /// several required scopes does not authorize the operation, so the 403 is still real and the
    /// test should still run — <c>All</c>, not <c>Any</c>.
    /// </para>
    /// <para>
    /// Guarded the same way <see cref="MultipleIdentitiesSkipReason"/> is, not the way
    /// <see cref="ResolveIdentitySlot"/> is (they are deliberately opposite) — but not because
    /// nothing runs before this one. Task 4 emits <c>RequireMultipleIdentities</c> first and the
    /// adapter call wrapping this method second, in the same generated method body, so in
    /// generated code its own provider/<c>Identities</c>/count checks are strictly redundant. It
    /// guards anyway for two reasons: its wrong answer is silent — <see cref="ResolveIdentitySlot"/>
    /// failing throws, loud and immediate, while this one failing the guard reports a skip
    /// *without anyone having to notice* — and it is reachable via a <c>protected internal</c>
    /// member on a shipped base class, so an adopter's hand-written 403 test can call the adapter
    /// directly, with nothing having run before it. This gate reaches further than
    /// <see cref="ResolveIdentitySlot"/> does — all the way to <c>Identities[1]</c> and its
    /// <see cref="TestIdentity.Scopes"/> — so every one of "no provider", "<c>Identities</c>
    /// itself null", and "fewer than two identities" must fall through to this method returning
    /// null without reporting a skip. v1-c shipped a live <see cref="NullReferenceException"/> on
    /// exactly this shape (a provider guarded, but not its <c>Identities</c>) in
    /// <c>RequireMultipleIdentities</c> itself; this guard exists precisely so that mistake is not
    /// repeated one index further in. A <c>null</c> <see cref="TestIdentity.Scopes"/> also falls
    /// through here — not declared / unknown means run and allow the test to fail, never skip.
    /// </para>
    /// <para>
    /// "The second element itself is null" also falls through this method without a skip — but
    /// that is narrower than it sounds. This method only ever guarantees it will not itself
    /// *report a skip* on that shape; it does not guarantee the test goes on to run.
    /// A provider whose second element is null violates <see cref="ITestTokenProvider.Identities"/>'s
    /// non-null annotation, and the generated case's very next call, <see cref="UseIdentity"/>,
    /// resolves through <see cref="ResolveIdentitySlot"/>, which indexes
    /// <c>Identities[1].Name</c> unguarded and throws <see cref="NullReferenceException"/> on
    /// exactly that shape. That is intended: failing loudly on a provider that breaks its own
    /// contract is preferable to this method inventing a defensive skip for a state it has no
    /// principled reason to call "not a 403".
    /// </para>
    /// <para>
    /// The empty-<paramref name="requiredScopes"/> guard below must be its own check, checked
    /// before the containment check, rather than falling through to the containment check alone:
    /// <c>requiredScopes.All(...)</c> is vacuously true over an empty <paramref name="requiredScopes"/>,
    /// which would otherwise read as "the secondary already holds everything required" and report
    /// a skip. A scope-free operation can still 403 on other grounds (tenant, role, resource
    /// ownership), and skipping would assert something this method has no basis for.
    /// </para>
    /// <para>
    /// The comparer used for containment is explicit, not incidental:
    /// <c>Enumerable.Contains(source, value)</c> has an <see cref="ICollection{T}"/> fast path
    /// that delegates to the collection's own <c>Contains</c>, so <c>scopes.Contains</c> (the
    /// two-argument form) would use *whatever comparer <c>scopes</c> itself was built with* —
    /// e.g. <see cref="StringComparer.OrdinalIgnoreCase"/>, if the adopter's <c>TestIdentity</c>
    /// used a case-insensitive <see cref="HashSet{T}"/> — rather than a comparer this method
    /// controls. The three-argument overload used below has no such fast path; it always
    /// enumerates and compares with the comparer passed to it. RFC 6749 scope tokens are
    /// case-sensitive, so "ORDERS.READ" must not satisfy a requirement for "orders.read"
    /// regardless of how the secondary identity's <c>Scopes</c> collection happens to compare
    /// equality internally.
    /// </para>
    /// <para>
    /// <c>Except</c>, used below to compute the "extra scopes" clause of the returned message, has
    /// no such fast path to worry about either way: it always builds its own set with the default
    /// comparer, which is ordinal for <see cref="string"/>.
    /// </para>
    /// <para>
    /// <c>protected internal</c> for the same two reasons as <see cref="MultipleIdentitiesSkipReason"/>.
    /// </para>
    /// </summary>
    protected internal static string? SecondaryIdentityScopeSkipReason(params string[] requiredScopes)
    {
        var provider = InTestRun.TokenProvider;
        if (provider?.Identities is not { Count: >= 2 } identities || identities[1] is not { } secondary)
        {
            return null;
        }

        if (secondary.Scopes is not { } scopes) return null; // undeclared: unknown means run
        if (requiredScopes is not { Length: > 0 }) return null; // no requirement to compare against
        // requiredScopes.All(...) is vacuously true over an empty requiredScopes, which is why
        // the line above must be its own check rather than falling through to this one: a
        // scope-free operation can still 403 on other grounds (tenant, role, resource
        // ownership), and reporting a skip would assert something this code has no basis for.
        if (!requiredScopes.All(s => scopes.Contains(s, StringComparer.Ordinal))) return null; // lacks at least one: the 403 is real

        var extra = scopes.Except(requiredScopes).Any();
        return extra
            ? $"Skipped: the secondary identity '{secondary.Name}' holds {string.Join(", ", scopes)} — " +
              $"including {string.Join(", ", requiredScopes)}, which this operation requires — so it " +
              "cannot produce a 403. Declare different scopes on that identity, or leave Scopes null " +
              "to run this test anyway."
            : $"Skipped: the secondary identity '{secondary.Name}' holds {string.Join(", ", scopes)}, " +
              "which this operation requires, so it cannot produce a 403. Declare different scopes on " +
              "that identity, or leave Scopes null to run this test anyway.";
    }

    /// <summary>
    /// Overrides the ambient identity for the remainder of the calling scope — the auth cases'
    /// override point (decision 7). A generated 401 or 403 case calls this after its
    /// wrong-scope-403 guard (403 only) and before building its request, since
    /// <see cref="BeginTest"/> has already set the <c>Default</c> slot by the time any test body
    /// runs.
    /// <para>
    /// Scoped rather than assigned outright: returning an <see cref="IDisposable"/> that restores
    /// whatever was ambient before it, rather than leaving the override in place until
    /// <see cref="EndTest"/> runs, means a test that throws mid-body still restores it —
    /// <see cref="EndTest"/> clearing <see cref="InTestAmbient.Identity"/> to null is not the only
    /// thing standing between one test and a leaked <see cref="IdentitySlot.Secondary"/> reaching
    /// whatever runs after it inside the same scope (a fixture's own cleanup closure, say).
    /// </para>
    /// </summary>
    protected static IDisposable UseIdentity(IdentitySlot slot)
    {
        var previous = InTestAmbient.Identity.Value;
        InTestAmbient.Identity.Value = ResolveIdentitySlot(slot, InTestRun.TokenProvider);
        return new IdentityScope(previous);
    }

    /// <summary>
    /// Resolves a slot to a concrete identity (decision 7), pulled out as an internal,
    /// dependency-free seam for the same reason <see cref="ResolveDefaultIdentity"/> is one:
    /// <see cref="IdentitySlot.Default"/> defers to it entirely, including its zero-identity handling;
    /// <see cref="IdentitySlot.None"/> is always the sentinel, independent of what
    /// <paramref name="provider"/> advertises; <see cref="IdentitySlot.Secondary"/> indexes
    /// <c>Identities[1]</c> directly rather than defensively, because the only caller that ever
    /// selects it — a generated 403 case — has already called the wrong-scope-403 guard first, in
    /// the same method body, which would have reported a skip before reaching this if fewer than
    /// two identities were registered. That prior call says nothing about the element itself
    /// being non-null, though: a provider whose <c>Identities[1]</c> is itself null — violating
    /// <see cref="ITestTokenProvider.Identities"/>'s non-null annotation, which nothing at compile
    /// time or in <see cref="MultipleIdentitiesSkipReason"/> enforces — reaches
    /// <c>Identities[1].Name</c> here unguarded and throws <see cref="NullReferenceException"/>,
    /// deliberately: failing loudly on a provider that breaks its own contract is the intended
    /// treatment, not a gap this method should paper over.
    /// </summary>
    internal static string ResolveIdentitySlot(IdentitySlot slot, ITestTokenProvider? provider) => slot switch
    {
        IdentitySlot.None => InTestIdentities.None,
        IdentitySlot.Secondary => provider!.Identities[1].Name,
        _ => ResolveDefaultIdentity(provider)
    };

    private sealed class IdentityScope(string? previous) : IDisposable
    {
        public void Dispose() => InTestAmbient.Identity.Value = previous;
    }

    /// <summary>
    /// Generated tests call this before building a request. Consults the aggregated validation
    /// report built once at <c>AssemblyInitialize</c> — never <c>InTestRun.Fixtures.Get</c>
    /// directly — so an operation with no fixture at all (the majority case) is a no-op rather
    /// than the <see cref="FixtureNotFoundException"/> a direct <c>Get</c> would throw. Only an
    /// operation whose fixture has an unresolved sentinel or token throws, naming its file and
    /// property (Task 7 / decision 2).
    /// </summary>
    protected static void RequireFixture(string operationKey) =>
        InTestRun.FixtureValidationReport.ThrowIfBlocked(operationKey);

    /// <summary>
    /// The fixture's resolved request body as a compact JSON string, or null when it carries
    /// none. Generated mutating methods call this after <see cref="RequireFixture"/> has already
    /// guaranteed nothing in it is unresolved.
    /// </summary>
    protected static string? FixtureBody(string operationKey) =>
        InTestRun.Fixtures.ResolvedBody(operationKey, InTestRun.FixtureTokens)?.ToJsonString();

    /// <summary>A single resolved path parameter value, sourced from the fixture rather than
    /// the deleted <c>TestData</c> (decision 1).</summary>
    protected static string FixtureParameter(string operationKey, string name) =>
        InTestRun.Fixtures.ResolvedParameter(operationKey, name, InTestRun.FixtureTokens);

    /// <summary>Resolved values for whichever of <paramref name="names"/> the fixture actually
    /// supplies — an optional query parameter with no value is simply absent (decision 1).</summary>
    protected static IReadOnlyDictionary<string, string> FixtureQueryParameters(string operationKey, params string[] names) =>
        InTestRun.Fixtures.ResolvedQueryParameters(operationKey, names, InTestRun.FixtureTokens);
}
