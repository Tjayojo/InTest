using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// F8's remaining half: <see cref="ITestTokenProvider"/>, <see cref="StaticTokenProvider"/> and
/// <c>Identities</c> have shipped since v1-b with nothing calling <c>GetTokenAsync</c>.
/// <see cref="AuthHandler"/> is that caller.
/// </summary>
[TestClass]
public class AuthHandlerTests
{
    /// <summary>Records exactly which identity it was asked for, so a test can assert on the
    /// ambient value AuthHandler actually forwarded rather than merely on the resulting header.</summary>
    private sealed class RecordingProvider(string token, IReadOnlyList<TestIdentity>? identities = null) : ITestTokenProvider
    {
        public string? LastAudience;
        public string? LastIdentity;

        public IReadOnlyList<TestIdentity> Identities { get; } = identities ?? [new TestIdentity("default"), new TestIdentity("secondary")];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
        {
            LastAudience = audience;
            LastIdentity = identity;
            return Task.FromResult(token);
        }
    }

    private sealed class ThrowingProvider : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = [new TestIdentity("default")];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("identity server unreachable");
    }

    /// <summary>
    /// Cancels the very <see cref="CancellationTokenSource"/> driving the request while
    /// <c>GetTokenAsync</c> is "awaiting" it, then throws through that same token — modelling
    /// HttpClient.Timeout or TestContext.CancellationToken firing mid-request, the path every
    /// generated test sends through (the Client.SendAsync call in mstest-class.scriban).
    /// </summary>
    private sealed class CancelingProvider(CancellationTokenSource cancellationTokenSource) : ITestTokenProvider
    {
        public IReadOnlyList<TestIdentity> Identities { get; } = [new TestIdentity("default")];

        public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
        {
            cancellationTokenSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("unreachable");
        }
    }

    private static async Task<HttpRequestMessage> SendThroughHandler(
        ITestTokenProvider? provider, string audience = "api://orders", CancellationToken cancellationToken = default)
    {
        var inner = new TestSupport.CapturingHandler();
        var handler = new AuthHandler(provider, audience) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        await client.GetAsync("https://example.invalid/", cancellationToken);
        return inner.SeenRequest!;
    }

    [TestInitialize]
    public void Reset() => InTestAmbient.Identity.Value = null;

    [TestMethod]
    public async Task SetsAuthorizationHeaderFromTheProvider()
    {
        InTestAmbient.Identity.Value = "default";
        var provider = new RecordingProvider("tok-abc");

        var request = await SendThroughHandler(provider);

        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization!.Parameter.ShouldBe("tok-abc");
    }

    [TestMethod]
    public async Task RequestsTheTokenForTheAmbientIdentityNotAlwaysTheDefault()
    {
        InTestAmbient.Identity.Value = "secondary";
        var provider = new RecordingProvider("tok-xyz");

        await SendThroughHandler(provider);

        provider.LastIdentity.ShouldBe("secondary");
    }

    /// <summary>
    /// Makes the previously-dead <see cref="RecordingProvider.LastAudience"/> field load-bearing.
    /// Question (c)'s audience resolution lives in <c>InTestRun.ResolveAudience</c> (covered
    /// separately in <c>TestHostTests</c>) and is passed into <see cref="AuthHandler"/>'s
    /// constructor; this pins the second half — that whatever audience the handler was
    /// constructed with is the one that actually reaches the provider, not a value hardcoded
    /// somewhere in between.
    /// </summary>
    [TestMethod]
    public async Task RequestsTheTokenForTheAudienceItWasConstructedWith()
    {
        InTestAmbient.Identity.Value = "default";
        var provider = new RecordingProvider("tok-abc");

        await SendThroughHandler(provider, audience: "api://custom-audience");

        provider.LastAudience.ShouldBe("api://custom-audience");
    }

    [TestMethod]
    public async Task SendsNoAuthorizationHeaderForTheNoTokenIdentity()
    {
        // The 401 test does not "use a bad token" — it sends none. A handler that always sets a
        // header would make that test unwritable.
        InTestAmbient.Identity.Value = InTestIdentities.None;
        var provider = new RecordingProvider("tok-abc");

        var request = await SendThroughHandler(provider);

        request.Headers.Authorization.ShouldBeNull();
        provider.LastIdentity.ShouldBeNull("the sentinel must short-circuit before the provider is ever asked for a token");
    }

    [TestMethod]
    public async Task NoOpsWhenNoProviderIsRegistered()
    {
        InTestAmbient.Identity.Value = "default";

        var request = await SendThroughHandler(provider: null);

        request.Headers.Authorization.ShouldBeNull();
    }

    [TestMethod]
    public async Task AProviderThatThrowsNamesTheProviderAndTheIdentity()
    {
        // Deliberately not "default": the implementation's catch clause falls back to the
        // literal string "(default)" when identity is null, so asserting on "default" is
        // satisfied by that fallback whether or not the identity is ever actually interpolated
        // into the message. A distinctive identity that cannot collide with the fallback is the
        // only way this assertion discriminates.
        InTestAmbient.Identity.Value = "identity-under-test";

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => SendThroughHandler(new ThrowingProvider()));

        ex.Message.ShouldContain(nameof(ThrowingProvider),
        customMessage: "a bare HttpRequestException doesn't say which provider or identity failed");
        ex.Message.ShouldContain("identity-under-test");
    }

    [TestMethod]
    public async Task CancellationPropagatesUnwrappedInsteadOfBeingReportedAsAProviderFailure()
    {
        // Mirrors FixtureRunner.cs:107-124's precedent: a cancelled run must not be reported as
        // the token provider (here) or the fixture (there) failing. Without the fix, this catches
        // OperationCanceledException in AuthHandler's catch-all and rethrows it as
        // InvalidOperationException("...failed to issue a token...: A task was canceled."),
        // exactly the misdiagnosis decision 1 exists to eliminate, one layer down.
        InTestAmbient.Identity.Value = "default";
        using var cts = new CancellationTokenSource();
        var provider = new CancelingProvider(cts);

        await Should.ThrowAsync<OperationCanceledException>(
        () => SendThroughHandler(provider, cancellationToken: cts.Token));
    }

    [TestMethod]
    public async Task AmbientIdentityIsIsolatedPerAsyncFlow()
    {
        async Task<string?> RunWith(string identity)
        {
            InTestAmbient.Identity.Value = identity;
            var handlerProvider = new RecordingProvider("tok");
            await SendThroughHandler(handlerProvider);
            return handlerProvider.LastIdentity;
        }

        var first = Task.Run(() => RunWith("identity-a"));
        var second = Task.Run(() => RunWith("identity-b"));
        var results = await Task.WhenAll(first, second);

        results.ShouldBe(["identity-a", "identity-b"], ignoreOrder: true);
    }
}
