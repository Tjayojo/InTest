namespace InTest.Runtime;

/// <summary>
/// Ambient per-test state. This must be AsyncLocal rather than a DI-scoped service:
/// handlers created by IHttpClientFactory are not scoped to the DI scope, so a scoped
/// service cannot be injected into one.
/// </summary>
public static class InTestAmbient
{
    public static readonly AsyncLocal<string?> TestId = new();

    /// <summary>
    /// The identity <see cref="AuthHandler"/> requests a token for. Same reason as
    /// <see cref="TestId"/>: AsyncLocal, not DI-scoped, because handlers built by
    /// IHttpClientFactory are not scoped to the DI container's scope.
    /// <para>
    /// <c>ApiTestCore.BeginTest</c> sets this to the resolved <c>Default</c> slot —
    /// <c>Identities[0]</c>, or <see cref="InTestIdentities.None"/> when the registered provider
    /// advertises none — and <c>ApiTestCore.EndTest</c> clears it, exactly as both already do for
    /// <see cref="TestId"/>. The MSTest adapter, <c>ApiTestBase</c>, is what actually triggers
    /// those two calls, from its <c>[TestInitialize]</c> and <c>[TestCleanup]</c>-attributed
    /// methods respectively — <c>BeginTest</c>/<c>EndTest</c> are the neutral bodies that do the
    /// work; the MSTest attributes are only the adapter's hook for calling them at the right
    /// point in a test's lifecycle. A generated auth case overrides this value before sending its
    /// request to select a different slot (v1-c decision 7): the wrong-scope 403 case sets
    /// <c>Identities[1]</c>, the 401 case sets <see cref="InTestIdentities.None"/>.
    /// </para>
    /// <para>
    /// Null outside any test scope — fixtures and <c>AssemblyInitialize</c> issue requests
    /// through <c>InTestClients.Api</c> before <c>ApiTestCore.BeginTest</c> has ever run.
    /// <see cref="AuthHandler"/> treats null the same as "no override" and forwards it as-is to
    /// <see cref="ITestTokenProvider.GetTokenAsync"/>'s own <c>identity</c> parameter, which is
    /// already documented to mean the provider's default.
    /// </para>
    /// </summary>
    public static readonly AsyncLocal<string?> Identity = new();
}
