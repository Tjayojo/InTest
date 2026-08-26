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
}
