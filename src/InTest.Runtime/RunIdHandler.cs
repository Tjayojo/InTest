namespace InTest.Runtime;

/// <summary>
/// Stamps every outgoing request with a correlation id. Framework-neutral.
/// Falls back to the run id because readiness probing and assembly fixtures issue HTTP
/// during AssemblyInitialize, when no test is in scope and the ambient value is null.
/// </summary>
public sealed class RunIdHandler(Func<string> runIdAccessor) : DelegatingHandler
{
    public const string HeaderName = "X-Test-Run-Id";

    private readonly Func<string> _runIdAccessor =
        runIdAccessor ?? throw new ArgumentNullException(nameof(runIdAccessor));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var value = InTestAmbient.TestId.Value ?? _runIdAccessor();
        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, value);
        return base.SendAsync(request, cancellationToken);
    }
}
