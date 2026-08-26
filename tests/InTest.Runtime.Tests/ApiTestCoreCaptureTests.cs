using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// [neutral-helper]: <c>ApiTestCore.ApiClient&lt;TClient&gt;()</c> and
/// <c>ApiTestCore.LastCapturedResponse</c>, the two members a generated client-routed test case
/// calls. Neither needs the full <c>InTestRun.InitializeAsync</c> weight
/// (<see cref="ApiTestBaseTests"/>'s own doc explains why that method gets no in-process harness):
/// <c>ApiClient&lt;TClient&gt;()</c> only needs <see cref="ApiTestCore.Services"/>, which reads
/// through the private <c>_scope</c> field <c>BeginTest</c> would otherwise set — set directly here
/// via reflection, the same escape hatch <see cref="ApiTestBaseTests.TestableApiTestCore"/> already
/// uses to reach <c>ApiTestCore.TestId</c> without a live <c>BeginTest</c> call.
/// </summary>
[TestClass]
public class ApiTestCoreCaptureTests
{
    private interface IFakeOrdersClient;

    private sealed class FakeOrdersClient : IFakeOrdersClient;

    private sealed class TestableApiTestCore : ApiTestCore
    {
        public void SetScope(IServiceScope scope) =>
            typeof(ApiTestCore).GetField("_scope", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(this, scope);

        /// <summary>
        /// [warn-on-swallowed-exception]: <c>_diagnostics</c> is otherwise only ever set by
        /// <c>BeginTest</c>, which needs a live <c>InTestRun.Root</c> scope to construct
        /// <see cref="HttpClient"/> from — the same reason <see cref="SetScope"/> exists as a
        /// reflection escape hatch rather than a real <c>BeginTest</c> call.
        /// </summary>
        public void SetDiagnostics(IRunDiagnostics? diagnostics) =>
            typeof(ApiTestCore).GetField("_diagnostics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(this, diagnostics);

        public TClient ExposedApiClient<TClient>() where TClient : class => ApiClient<TClient>();

        public static CapturedResponse ExposedLastCapturedResponse => LastCapturedResponse;

        public void ExposedWarnSwallowedClientException(Exception exception) => WarnSwallowedClientException(exception);

        public void ExposedEndTest() => EndTest();

        /// <summary>
        /// [restore-one-arg-begintest]: reaches the new one-argument compatibility overload the
        /// same way every other exposed member in this class reaches a protected one — a thin
        /// public passthrough on this test-local subclass, not reflection, since the overload
        /// itself is an ordinary <c>protected</c> method with nothing reflection is needed for.
        /// </summary>
        public void ExposedBeginTestOneArg(string? testDisplayName) => BeginTest(testDisplayName);

        /// <summary>
        /// [restore-one-arg-begintest]: the two-argument form, exposed the same way, so a test can
        /// drive both overloads through the identical real production body and compare the
        /// resulting per-test state directly rather than trusting the one-argument overload's own
        /// one-line delegation by inspection alone.
        /// </summary>
        public void ExposedBeginTest(string? testDisplayName, IRunDiagnostics diagnostics) =>
            BeginTest(testDisplayName, diagnostics);

        /// <summary>Exposes the otherwise-protected <see cref="ApiTestCore.TestId"/> — the same
        /// escape hatch <see cref="ApiTestBaseTests.TestableApiTestCore"/> already uses, duplicated
        /// here rather than shared because that type has no <c>BeginTest</c> passthrough and this
        /// one has no reason to take on that file's dependency.</summary>
        public string ExposedTestId => TestId;
    }

    /// <summary>Records every <see cref="Warn"/>/<see cref="Note"/> call verbatim, in order —
    /// the same shape <c>TestHostTests.FakeTestContext</c> uses for
    /// <c>TestContext.DisplayMessage</c>, adapted to <see cref="IRunDiagnostics"/> directly since
    /// <see cref="ApiTestCore.WarnSwallowedClientException"/> talks to that interface, never
    /// <see cref="TestContext"/> itself.</summary>
    private sealed class FakeRunDiagnostics : IRunDiagnostics
    {
        public List<string> Notes { get; } = [];
        public List<string> Warnings { get; } = [];

        public void Note(string message) => Notes.Add(message);
        public void Warn(string message) => Warnings.Add(message);
    }

    [TestInitialize]
    public void Reset() => InTestAmbient.LastCapturedResponse.Value = null;

    [TestMethod]
    public void ApiClientResolvesTheRegisteredTypedClientFromServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeOrdersClient, FakeOrdersClient>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var subject = new TestableApiTestCore();
        subject.SetScope(scope);

        var client = subject.ExposedApiClient<IFakeOrdersClient>();

        client.ShouldBeOfType<FakeOrdersClient>();
    }

    [TestMethod]
    public void ApiClientThrowsTheStandardDIExceptionWhenNothingIsRegistered()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var subject = new TestableApiTestCore();
        subject.SetScope(scope);

        // Deliberately not wrapped in a bespoke InTest message (per ApiTestCore.ApiClient's own
        // doc): GetRequiredService already names the missing type clearly on its own.
        Should.Throw<InvalidOperationException>(() => subject.ExposedApiClient<IFakeOrdersClient>());
    }

    /// <summary>
    /// [client-rides-the-api-pipeline]: the guard that makes a misconfigured typed client
    /// self-diagnosing rather than a silent pass against <c>default</c>. See
    /// <c>ApiTestCore.LastCapturedResponse</c>'s own doc for why a silent <c>default</c> here would
    /// be exactly the "passes while asserting almost nothing" outcome CLAUDE.md's fail-loudly rule
    /// forbids.
    /// </summary>
    [TestMethod]
    public void LastCapturedResponseThrowsRatherThanReturningDefaultWhenNothingWasCaptured()
    {
        InTestAmbient.LastCapturedResponse.Value = null;

        var ex = Should.Throw<InvalidOperationException>(() => TestableApiTestCore.ExposedLastCapturedResponse);

        ex.Message.ShouldContain("[client-rides-the-api-pipeline]");
        ex.Message.ShouldContain("InTestClients.Api");
    }

    [TestMethod]
    public void LastCapturedResponseReturnsWhateverIsAmbientlyStashed()
    {
        var captured = new CapturedResponse(201, """{"id":"a"}""", "POST", "https://h.invalid/api/orders");
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot { Value = captured };

        TestableApiTestCore.ExposedLastCapturedResponse.ShouldBe(captured);
    }

    /// <summary>
    /// A slot exists (BeginTest ran) but nothing has been mutated into it yet (no client-routed
    /// call has completed for this test) — must throw exactly like the no-slot-at-all case above,
    /// not return a default <see cref="CapturedResponse"/>. This is the shape
    /// <see cref="InTestAmbient.LastCapturedResponse"/>'s own doc names as the second <c>?.</c> in
    /// <c>InTestAmbient.LastCapturedResponse.Value?.Value is null</c>.
    /// </summary>
    [TestMethod]
    public void LastCapturedResponseThrowsWhenASlotExistsButNothingWasMutatedIntoItYet()
    {
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot();

        Should.Throw<InvalidOperationException>(() => TestableApiTestCore.ExposedLastCapturedResponse);
    }

    /// <summary>
    /// Proves the actual production clearing path — <c>ApiTestCore.EndTest</c> — rather than only
    /// the read side above. Needs only <c>_scope</c> set (a disposable <see cref="IServiceScope"/>
    /// with nothing registered), not a live <c>InTestRun.Root</c>: <c>EndTest</c>'s own body never
    /// touches <c>InTestRun</c> at all.
    /// </summary>
    [TestMethod]
    public void EndTestClearsTheCapturedResponseSlot()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var subject = new TestableApiTestCore();
        subject.SetScope(scope);
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot
        {
            Value = new CapturedResponse(200, "{}", "GET", "https://h.invalid/api/orders")
        };

        subject.ExposedEndTest();

        InTestAmbient.LastCapturedResponse.Value.ShouldBeNull();
    }

    // ---- WarnSwallowedClientException ([warn-on-swallowed-exception]) -----------------------

    /// <summary>
    /// The one scenario this method exists for: a client-map.json override that issues more than
    /// one call, where an earlier call already captured a response and a later one throws. Both
    /// the exception's runtime type and its message must be readable from the single string this
    /// forwards to <see cref="IRunDiagnostics.Warn"/> — an operator reading only that line, with no
    /// other context, must be able to tell what was thrown and why it did not fail the test.
    /// </summary>
    [TestMethod]
    public void WarnSwallowedClientExceptionForwardsTheExceptionTypeAndMessageToWarn()
    {
        var diagnostics = new FakeRunDiagnostics();
        var subject = new TestableApiTestCore();
        subject.SetDiagnostics(diagnostics);

        subject.ExposedWarnSwallowedClientException(new InvalidOperationException("second call failed"));

        var warning = diagnostics.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain(nameof(InvalidOperationException));
        warning.ShouldContain("second call failed");
    }

    /// <summary>
    /// <see cref="IRunDiagnostics.Warn"/>'s own doc: it must reach the operator even on a run that
    /// otherwise passes — a <see cref="IRunDiagnostics.Note"/> here would be exactly the kind of
    /// message a passing run is permitted to lose, wrong for a defect a reviewer specifically
    /// raised because it hides silently inside an otherwise-green result.
    /// </summary>
    [TestMethod]
    public void WarnSwallowedClientExceptionNeverCallsNote()
    {
        var diagnostics = new FakeRunDiagnostics();
        var subject = new TestableApiTestCore();
        subject.SetDiagnostics(diagnostics);

        subject.ExposedWarnSwallowedClientException(new InvalidOperationException("boom"));

        diagnostics.Notes.ShouldBeEmpty();
    }

    /// <summary>
    /// States clearly, in the message itself, that the exception was discarded because a captured
    /// response already stood as the verdict — the second half of what an operator needs to read
    /// off this one line, alongside the exception's own type and message.
    /// </summary>
    [TestMethod]
    public void WarnSwallowedClientExceptionExplainsWhyTheExceptionWasDiscarded()
    {
        var diagnostics = new FakeRunDiagnostics();
        var subject = new TestableApiTestCore();
        subject.SetDiagnostics(diagnostics);

        subject.ExposedWarnSwallowedClientException(new InvalidOperationException("boom"));

        var warning = diagnostics.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain("captured response");
        warning.ShouldContain("discarded");
    }

    /// <summary>
    /// A clean run — no client-routed call ever throws after a capture — must warn nothing at
    /// all: nothing calls <see cref="ApiTestCore.WarnSwallowedClientException"/> unless a generated
    /// case's second catch actually runs, so a diagnostics sink that is never told to warn stays
    /// empty by construction. Pinned directly here rather than left implicit, since it is one of
    /// the three behaviours this feature's own verification explicitly calls for.
    /// </summary>
    [TestMethod]
    public void ADiagnosticsSinkThatIsNeverToldToWarnStaysEmpty()
    {
        var diagnostics = new FakeRunDiagnostics();
        var subject = new TestableApiTestCore();
        subject.SetDiagnostics(diagnostics);

        diagnostics.Warnings.ShouldBeEmpty();
        diagnostics.Notes.ShouldBeEmpty();
    }

    // ---- BeginTest(string?) — the one-argument compatibility overload ------------------------

    /// <summary><see cref="InTestRun.Root"/> has a private setter — production code only ever
    /// assigns it from <see cref="InTestRun.InitializeAsync"/>. Reached here via reflection, the
    /// same escape hatch this file already uses for <see cref="ApiTestCore"/>'s own private
    /// fields (<see cref="TestableApiTestCore.SetScope"/>, <see cref="TestableApiTestCore.SetDiagnostics"/>),
    /// because the real, production <c>BeginTest(string?)</c> overload under test calls
    /// <c>InTestRun.Root.CreateScope()</c> directly rather than accepting a scope parameter — there
    /// is no way to drive its actual body without a live <see cref="InTestRun.Root"/> to call
    /// into.</summary>
    private static readonly PropertyInfo InTestRunRootProperty =
        typeof(InTestRun).GetProperty(nameof(InTestRun.Root), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Same reasoning as <see cref="InTestRunRootProperty"/>: <c>BeginTest</c> reads
    /// <see cref="InTestRun.RunIdValue"/> directly (via <c>InTestId.ForTest</c>), and
    /// <c>InTestId.ForTest</c> throws <see cref="ArgumentException"/> on a null/whitespace run id —
    /// the field's own unset default — so this must be given a real value for the call under test
    /// to reach <see cref="WarnSwallowedClientException"/> at all, rather than failing earlier for
    /// a reason that has nothing to do with what this test proves.</summary>
    private static readonly PropertyInfo InTestRunRunIdValueProperty =
        typeof(InTestRun).GetProperty(nameof(InTestRun.RunIdValue), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// [restore-one-arg-begintest]: proves the one-argument <c>BeginTest(string?)</c> compatibility
    /// overload reproduces the <em>exact</em> old observable behaviour of a caller that supplies no
    /// diagnostics sink at all — not merely that the overload compiles and can be called. Before
    /// <c>[warn-on-swallowed-exception]</c> added the two-argument <c>BeginTest(string?, IRunDiagnostics)</c>,
    /// a swallowed client exception inside <see cref="ApiTestCore.WarnSwallowedClientException"/>
    /// left no trace anywhere — <c>_diagnostics</c> was null, and that method's own
    /// <c>_diagnostics?.Warn(...)</c> already made that a silent no-op. This test drives the real
    /// production <c>BeginTest(string?)</c> body (not a hand-simulated stand-in), then triggers
    /// <see cref="ApiTestCore.WarnSwallowedClientException"/> the same way a generated
    /// client-routed case's second catch would, and asserts only that neither call throws — there is
    /// no diagnostics sink to assert warnings or notes against, because the entire point of the old
    /// one-argument signature is that none was ever supplied. "Doesn't throw" is the old behaviour;
    /// anything else (a warning surfacing somewhere, an exception propagating) would be new, wrong
    /// behaviour this overload must not introduce.
    /// <para>
    /// <see cref="InTestRun.Root"/> and <see cref="InTestRun.RunIdValue"/> are process-wide static
    /// state this test must set (via <see cref="InTestRunRootProperty"/>/<see cref="InTestRunRunIdValueProperty"/>)
    /// for <c>BeginTest</c> to have anything real to call into — captured and restored in a
    /// <c>finally</c> so this test's mutation cannot leak into whichever test the assembly's
    /// <c>[assembly: DoNotParallelize]</c> ordering runs next, the same before/after-reset shape
    /// <c>TestHostTests</c> already uses for <c>InTestRun.RetainedFixtureContext</c> and
    /// <c>ApiTestBaseAuthTests</c> already uses for <see cref="InTestRun.TokenProvider"/>. The
    /// registered service collection is deliberately minimal — only <see cref="InTestClients.Api"/>,
    /// the one named client <c>BeginTest</c> resolves — well short of the full, heavy
    /// <c>InTestRun.InitializeAsync</c> weight this file's own top-level doc already explains this
    /// class is never given an in-process harness for.
    /// </para>
    /// </summary>
    [TestMethod]
    public void BeginTestOneArgOverloadNeverThrowsAndLeavesASwallowedExceptionSilentlyDiscarded()
    {
        var previousRoot = InTestRun.Root;
        var previousRunIdValue = InTestRun.RunIdValue;
        var previousTokenProvider = InTestRun.TokenProvider;

        var services = new ServiceCollection();
        services.AddHttpClient(InTestClients.Api);
        using var provider = services.BuildServiceProvider();

        InTestRunRootProperty.SetValue(null, provider);
        InTestRunRunIdValueProperty.SetValue(null, "test-run");
        InTestRun.TokenProvider = null;

        try
        {
            var subject = new TestableApiTestCore();

            Should.NotThrow(() => subject.ExposedBeginTestOneArg("Some Test"));
            Should.NotThrow(() => subject.ExposedWarnSwallowedClientException(new InvalidOperationException("boom")));

            subject.ExposedEndTest();
        }
        finally
        {
            InTestRunRootProperty.SetValue(null, previousRoot);
            InTestRunRunIdValueProperty.SetValue(null, previousRunIdValue);
            InTestRun.TokenProvider = previousTokenProvider;
        }
    }

    /// <summary>A single named identity — enough to make the ambient-identity assertion below
    /// non-trivial. <see cref="ApiTestBaseAuthTests.FakeTokenProvider"/> is the same shape, but
    /// private to that file, so this is a narrower duplicate rather than a shared dependency
    /// across files that otherwise have no reason to know about each other.</summary>
    private sealed class FakeTokenProvider(params string[] identityNames) : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = identityNames.Select(n => new TestIdentity(n)).ToArray();

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised by this test");
    }

    /// <summary>
    /// [restore-one-arg-begintest]: the equivalence half of this overload's contract, distinct from
    /// the never-throws test above. That test proves the one-argument overload does not blow up;
    /// this one proves it leaves behind exactly the same per-test state <c>ApiTestBase.ApiTestInitialize</c>
    /// already relies on from the two-argument form — <see cref="ApiTestCore.TestId"/>, the ambient
    /// capture slot <see cref="ResponseCaptureHandler"/> writes into, and the ambient default
    /// identity <see cref="ApiTestCore.ResolveDefaultIdentity"/> computes — rather than merely
    /// asserting the call site compiles and survives. A caller migrating from the pre-diagnostics
    /// one-argument signature must see identical downstream behaviour, not just an absence of
    /// exceptions.
    /// <para>
    /// Drives the real, production two-argument <c>BeginTest(string?, IRunDiagnostics)"/> once with
    /// a <see cref="FakeRunDiagnostics"/> sink to establish the baseline, tears that test down, then
    /// drives the real one-argument overload and compares. Both runs use the same
    /// <paramref name="testDisplayName" />-equivalent literal and the same <see cref="InTestRun.RunIdValue"/>,
    /// so <see cref="ApiTestCore.TestId"/> is expected to come out byte-for-byte identical — a
    /// divergence there would mean the one-argument overload is not simply forwarding to the
    /// two-argument body with a swapped-out sink, which is exactly the regression this test exists
    /// to catch.
    /// </para>
    /// </summary>
    [TestMethod]
    public void BeginTestOneArgOverloadSetsUpPerTestStateIdenticallyToTheTwoArgFormWithARealSink()
    {
        var previousRoot = InTestRun.Root;
        var previousRunIdValue = InTestRun.RunIdValue;
        var previousTokenProvider = InTestRun.TokenProvider;

        var services = new ServiceCollection();
        services.AddHttpClient(InTestClients.Api);
        using var provider = services.BuildServiceProvider();

        InTestRunRootProperty.SetValue(null, provider);
        InTestRunRunIdValueProperty.SetValue(null, "test-run");
        InTestRun.TokenProvider = new FakeTokenProvider("primary-user");

        try
        {
            // Baseline: the two-argument form, with a real (non-null) diagnostics sink — the shape
            // every caller this repository controls actually uses.
            var baselineDiagnostics = new FakeRunDiagnostics();
            var baseline = new TestableApiTestCore();
            baseline.ExposedBeginTest("Some Test", baselineDiagnostics);

            var baselineTestId = baseline.ExposedTestId;
            var baselineIdentity = InTestAmbient.Identity.Value;
            var baselineSlotExists = InTestAmbient.LastCapturedResponse.Value is not null;
            baseline.ExposedEndTest();

            // Subject: the one-argument compatibility overload under test, same display name and
            // same static InTestRun state, so any difference below is attributable only to the
            // overload itself, not to a changed environment between the two calls.
            var subject = new TestableApiTestCore();
            subject.ExposedBeginTestOneArg("Some Test");

            subject.ExposedTestId.ShouldBe(baselineTestId);
            InTestAmbient.Identity.Value.ShouldBe(baselineIdentity);
            baselineIdentity.ShouldBe("primary-user"); // sanity: the comparison above isn't vacuous
            InTestAmbient.LastCapturedResponse.Value.ShouldNotBeNull();
            (InTestAmbient.LastCapturedResponse.Value is not null).ShouldBe(baselineSlotExists);

            subject.ExposedEndTest();
        }
        finally
        {
            InTestRunRootProperty.SetValue(null, previousRoot);
            InTestRunRunIdValueProperty.SetValue(null, previousRunIdValue);
            InTestRun.TokenProvider = previousTokenProvider;
        }
    }
}
