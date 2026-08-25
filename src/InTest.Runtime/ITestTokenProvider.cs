namespace InTest.Runtime;

/// <summary>
/// Supplies bearer tokens to generated tests. InTest ships only <see cref="StaticTokenProvider"/>;
/// everything else is the adopter's, so that no identity or cloud library is imposed.
/// </summary>
public interface ITestTokenProvider
{
    /// <summary>
    /// Identities this provider can issue tokens for, in order. A count of one or zero gates the
    /// wrong-scope and wrong-tenant auth tests off, and is the source of the coverage
    /// report's gated-test count. A declared capability, never a probe.
    /// <para>
    /// <c>IReadOnlyList</c>, not <c>IReadOnlyCollection</c>: the CLI generates test code long
    /// before an adopter has written a provider, so generated code can never reference an
    /// identity by name — only by position (v1-c decision 7). Index 0 is therefore the default
    /// identity every ordinary case authenticates as; index 1, when present, is some other
    /// identity, whose <see cref="TestIdentity.Scopes"/> decide which wrong-scope 403 cases are
    /// provable for it. This is a breaking change from the <c>IReadOnlyCollection&lt;string&gt;</c>
    /// this shipped as, made while nothing outside this repository implements the interface yet —
    /// the last point at which it was free, and the same reasoning now covers
    /// <see cref="TestIdentity"/> as the element type too. From the first published version
    /// onward, this ordering is a semver promise (§3), not an implementation detail.
    /// </para>
    /// </summary>
    IReadOnlyList<TestIdentity> Identities { get; }

    /// <param name="audience">The token's intended audience — configuration's <c>Api:Audience</c>,
    /// falling back to the base URL's authority (v1-c Task 2 question (c)).</param>
    /// <param name="identity">The <see cref="TestIdentity.Name"/> of one of <see cref="Identities"/>,
    /// selecting which identity to issue a token for. Null means the provider's own default —
    /// <see cref="AuthHandler"/> passes null through unchanged whenever
    /// <c>InTestAmbient.Identity</c> is unset, rather than resolving it to
    /// <c>Identities[0].Name</c> itself.</param>
    /// <param name="cancellationToken"></param>
    Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default);
}
