namespace InTest.Runtime;

/// <summary>The only implementation InTest ships. One token, one identity.</summary>
public sealed class StaticTokenProvider(string token, string identityName = "default") : ITestTokenProvider
{
    private readonly string _token = token ?? throw new ArgumentNullException(nameof(token));

    public IReadOnlyList<TestIdentity> Identities { get; } = [new TestIdentity(identityName)];

    public Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default)
    {
        if (identity is not null && !Identities.Any(i => i.Name == identity))
        {
            throw new ArgumentException(
            // Named by identity, not by rendering a TestIdentity record — the message is for a
            // human reading a test failure, not a dump of the descriptor.
            $"StaticTokenProvider serves only '{string.Join(", ", Identities.Select(i => i.Name))}'; '{identity}' was requested. " +
            "Implement ITestTokenProvider with more than one identity to enable the 403 auth tests.",
            nameof(identity));
        }

        return Task.FromResult(_token);
    }
}
