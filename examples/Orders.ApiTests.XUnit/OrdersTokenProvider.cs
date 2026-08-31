using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InTest.Runtime;

namespace Orders.ApiTests;

/// <summary>
/// Real client-credentials tokens from <c>samples/Identity.Server</c>. Two identities —
/// <c>orders-client</c> (full access) and <c>orders-readonly</c> (read only) — matching the two
/// Duende clients declared in <c>samples/Identity.Server/Config.cs</c>, so the generated
/// wrong-scope 403 cases have a second identity to run against (see "Auth" in Phase 3 of
/// getting-started.md). Declaring <c>orders-readonly</c>'s <see cref="TestIdentity.Scopes"/>
/// lets <c>RequireSecondaryIdentityLacks</c> skip, with a stated reason, the wrong-scope 403
/// cases that identity cannot actually prove — the ones needing only <c>orders.read</c>.
/// </summary>
public sealed class OrdersTokenProvider(string identityServerAuthority) : ITestTokenProvider
{
    // Public on purpose — samples/Identity.Server/Config.cs's own doc comment says why: this
    // issues tokens for a sample, and the secret protects nothing real.
    private const string SharedSecret = "sample-secret-not-a-real-credential";

    private static readonly HttpClient Http = new();

    public IReadOnlyList<TestIdentity> Identities { get; } =
    [
        new TestIdentity("orders-client", ["orders.read", "orders.write"]),
        new TestIdentity("orders-readonly", ["orders.read"])
    ];

    public async Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
    {
        var clientId = identity ?? Identities[0].Name;

        try
        {
            using var response = await Http.PostAsync(
                $"{identityServerAuthority.TrimEnd('/')}/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = SharedSecret
                }),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            return payload?.AccessToken
                ?? throw new InvalidOperationException("Identity.Server returned an empty token response.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"OrdersTokenProvider failed to issue a token for identity '{clientId}': {ex.Message}", ex);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";
    }
}
