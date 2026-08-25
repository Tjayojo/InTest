using System.Net.Http.Headers;

namespace InTest.Runtime;

/// <summary>
/// Sets <c>Authorization</c> from the registered <see cref="ITestTokenProvider"/> for the
/// ambient identity — F8's remaining half. The interface, <see cref="StaticTokenProvider"/> and
/// <c>Identities</c> shipped in v1-b with nothing calling <c>GetTokenAsync</c>; this is that
/// caller.
/// <para>
/// Attached to <c>InTestClients.Api</c> only, never <c>InTestClients.Readiness</c> (F10,
/// v1-c decision 1): the readiness probe hits an anonymous endpoint, and a handler that requires
/// a reachable identity provider on every request would turn "identity server down" into "API
/// looks dead" after a two-minute wait, exactly the misdiagnosis Readiness's own client exists
/// to prevent.
/// </para>
/// <para>
/// Reads the identity from <see cref="InTestAmbient.Identity"/> rather than a constructor
/// argument, for the same measured reason <see cref="RunIdHandler"/> reads
/// <see cref="InTestAmbient.TestId"/>: this handler is created by <c>IHttpClientFactory</c>, and
/// factory-created handlers are not scoped to the DI container's scope, so a per-test value
/// cannot reach it any other way.
/// </para>
/// </summary>
public sealed class AuthHandler(ITestTokenProvider? tokenProvider, string audience) : DelegatingHandler
{
    private readonly string _audience = audience ?? throw new ArgumentNullException(nameof(audience));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var identity = InTestAmbient.Identity.Value;

        // No provider registered: Catalog and Inventory declare no `security` and their
        // scaffolds register none — StaticTokenProvider needs a token nobody has configured, so
        // there is nothing to construct one from (Task 2 question (b)). A handler that required
        // a provider would make the scaffold fail out of the box for every unsecured spec. The
        // no-token sentinel short-circuits the same way and for the same reason the 401 test
        // exists: no provider should ever be asked for a token it must not send.
        if (tokenProvider is null || identity == InTestIdentities.None)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        string token;
        try
        {
            token = await tokenProvider.GetTokenAsync(_audience, identity, cancellationToken).ConfigureAwait(false);
        }
        // A cancellation of the token this call was given (an MSTest timeout, or
        // HttpClient.Timeout — both cancel through TestContext.CancellationToken, which the
        // Client.SendAsync call in mstest-class.scriban threads into every generated request) is
        // not the provider failing; it is the run being cancelled. Mirrors
        // FixtureRunner.cs:107-124's identical distinction one layer up: a cancelled run must not
        // be blamed on the code that happened to be running when it fired. Left uncaught here, it
        // propagates as a raw OperationCanceledException instead of being wrapped below.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A bare HttpRequestException three layers down names neither the provider nor the
            // identity it failed for — this is the difference between "something in the pipeline
            // threw" and "the identity server rejected this specific identity."
            throw new InvalidOperationException(
                $"{tokenProvider.GetType().Name} failed to issue a token for identity " +
                $"'{identity ?? "(default)"}': {ex.Message}", ex);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
