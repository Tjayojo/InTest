using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// F10: <c>InTestRun.InitializeAsync</c> registered exactly one named client
/// (<see cref="InTestClients.Api"/>) and handed that same client to
/// <see cref="Readiness.WaitAsync"/>. An adopter following the getting-started guide attaches a
/// bearer handler to <see cref="InTestClients.Api"/> via <c>ConfigureServices</c>; when the
/// identity provider is unreachable that handler throws on every request through the client —
/// including the anonymous <c>/health/ready</c> probe, which needed no token at all. The result
/// was a dead identity server reported as a dead API, after a 120-second wait.
/// <para>
/// This exercises <see cref="InTestRun.RegisterInTestClients"/> directly rather than
/// hand-duplicating its registrations — the seam <c>InTestRun.InitializeAsync</c> itself calls —
/// so this proves something about <c>InTestRun</c>'s own code, not merely about
/// <c>Microsoft.Extensions.Http</c>'s named-client isolation (a review of the first version of
/// this test found exactly that gap and it was deleted rather than fixed; this replaces it).
/// <c>InitializeAsync</c> as a whole still gets no in-process harness — see
/// <c>TestHostTests</c>'s note on <c>TestContextDiagnostics</c> for why — but
/// <see cref="InTestRun.RegisterInTestClients"/> needs none of what makes that true: no
/// <c>AppContext.BaseDirectory</c>, no real <c>TestContext</c>, no live HTTP.
/// </para>
/// </summary>
[TestClass]
public class InTestClientsTests
{
    /// <summary>Always throws — stands in for a bearer handler that cannot reach an unreachable
    /// identity provider. Records whether it ran at all, which is the only thing this test
    /// needs: the readiness probe must never reach it.</summary>
    private sealed class ThrowingHandler : DelegatingHandler
    {
        public bool Ran { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ran = true;
            throw new HttpRequestException("identity provider unreachable");
        }
    }

    /// <summary>Stands in for the live health endpoint so this test sends no real network
    /// traffic — always answers 200 immediately.</summary>
    private sealed class AlwaysReadyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    [TestMethod]
    public async Task ReadinessProbeDoesNotRunApiClientHandlers()
    {
        var throwing = new ThrowingHandler();

        var services = new ServiceCollection();
        services.AddTransient(_ => new RunIdHandler(() => "run-1"));

        // The exact registration InTestRun.InitializeAsync performs, via the seam it calls — not
        // a hand-duplicated copy of it.
        InTestRun.RegisterInTestClients(services, new Uri("https://h.invalid/api/"));

        // Stand in for the live probe so this test sends no real network traffic. Additive to
        // whatever RegisterInTestClients already configured for this name (named-HttpClient
        // configuration composes rather than replaces).
        services.AddHttpClient(InTestClients.Readiness).ConfigurePrimaryHttpMessageHandler(() => new AlwaysReadyHandler());

        // Where an adopter's ConfigureServices attaches an auth handler, per the getting-started
        // guide: to InTestClients.Api specifically. This is how F10's bug reached the readiness
        // probe when both roles shared one client.
        services.AddHttpClient(InTestClients.Api).AddHttpMessageHandler(() => throwing);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Readiness);
        using var cts = TestSupport.TimeoutToken();

        await Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 1), cts.Token);

        throwing.Ran.ShouldBeFalse("a handler attached to InTestClients.Api must never run for the readiness probe");
    }

    /// <summary>
    /// Task 2's own wiring, proven the same way F10's fix above is: through
    /// <see cref="InTestRun.RegisterInTestClients"/> itself, not a hand-duplicated copy of its
    /// registrations. A provider is registered and reachable here, so a wiring mistake that put
    /// <see cref="AuthHandler"/> on the wrong client — or on neither — would show up as a
    /// missing or misplaced header, not as an exception.
    /// </summary>
    [TestMethod]
    public async Task ApiClientCarriesAuthHandlerButReadinessDoesNotEvenWhenAProviderIsRegistered()
    {
        var apiInner = new TestSupport.CapturingHandler();
        var readinessInner = new TestSupport.CapturingHandler();

        var services = new ServiceCollection();
        services.AddTransient(_ => new RunIdHandler(() => "run-1"));
        services.AddSingleton<ITestTokenProvider>(new StaticTokenProvider("tok-abc"));
        services.AddTransient(sp => new AuthHandler(sp.GetService<ITestTokenProvider>(), "api://orders"));

        // The exact registration InTestRun.InitializeAsync performs, via the seam it calls.
        InTestRun.RegisterInTestClients(services, new Uri("https://h.invalid/api/"));

        services.AddHttpClient(InTestClients.Api).ConfigurePrimaryHttpMessageHandler(() => apiInner);
        services.AddHttpClient(InTestClients.Readiness).ConfigurePrimaryHttpMessageHandler(() => readinessInner);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        InTestAmbient.Identity.Value = "default";
        try
        {
            await factory.CreateClient(InTestClients.Api).GetAsync("https://h.invalid/api/orders");
            await factory.CreateClient(InTestClients.Readiness).GetAsync("https://h.invalid/api/health/ready");
        }
        finally
        {
            InTestAmbient.Identity.Value = null;
        }

        apiInner.SeenRequest!.Headers.Authorization.ShouldNotBeNull(
        "AuthHandler must be attached to InTestClients.Api — this is F8's whole point");
        readinessInner.SeenRequest!.Headers.Authorization.ShouldBeNull(
        "AuthHandler must never reach the anonymous readiness probe (F10, decision 1)");
    }
}
