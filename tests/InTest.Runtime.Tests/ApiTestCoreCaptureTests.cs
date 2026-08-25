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

        public TClient ExposedApiClient<TClient>() where TClient : class => ApiClient<TClient>();

        public static CapturedResponse ExposedLastCapturedResponse => LastCapturedResponse;

        public void ExposedEndTest() => EndTest();
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
}
