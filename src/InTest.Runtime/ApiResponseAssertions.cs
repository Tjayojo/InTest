using System.Globalization;
using System.Text;

namespace InTest.Runtime;

/// <summary>
/// Contract assertions. Framework-neutral, and justified by message quality rather than by
/// swapping: these messages are constructed, so unlike Shouldly's source-reading they do not
/// degrade when tests run from a published artifact.
/// </summary>
public static class ApiResponseAssertions
{
    private const int BodyExcerptLimit = 2000;

    public static async Task ShouldMatchContractAsync(
        HttpResponseMessage response, int expectedStatus, string schemaKey,
        SchemaBundle schemas, string testId, TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(schemas);

        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode != expectedStatus)
        {
            throw Failure(response, expectedStatus, testId, elapsed, body, []);
        }

        var violations = schemas.Validate(schemaKey, body);
        if (violations.Count > 0)
        {
            throw Failure(response, expectedStatus, testId, elapsed, body, violations);
        }
    }

    public static async Task ShouldMatchStatusAsync(
        HttpResponseMessage response, int expectedStatus, string testId, TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if ((int)response.StatusCode == expectedStatus)
        {
            return;
        }

        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        throw Failure(response, expectedStatus, testId, elapsed, body, []);
    }

    /// <summary>
    /// [captured-response-is-the-verdict]: the client-routed counterpart of
    /// <see cref="ShouldMatchContractAsync"/>. A Success case whose typed client call actually
    /// returns 500 is precisely what a generated contract test exists to catch, and every typed
    /// client (Kiota, NSwag, Refit) throws its own generator-specific exception on that response
    /// before this method would ever otherwise run — see the mstest-class.scriban template's pinned
    /// <c>try</c>/exception-filter/<c>catch</c> for how that exception is caught and this method
    /// called from inside the filtered <c>catch</c> instead, so the adopter still sees InTest's own
    /// contract failure (run id, expected vs. actual status, elapsed, body excerpt) rather than a
    /// bare client exception naming none of that.
    /// <para>
    /// Takes a <see cref="CapturedResponse"/> rather than an <see cref="HttpResponseMessage"/>
    /// because there may be no live <see cref="HttpResponseMessage"/> left to inspect by the time
    /// this runs — the typed client may have already thrown out of its own deserialization, and
    /// <see cref="ResponseCaptureHandler"/> is what preserved the raw facts before that happened.
    /// Synchronous under the hood (returns <see cref="Task.CompletedTask"/> rather than genuinely
    /// awaiting anything): unlike <see cref="ShouldMatchContractAsync"/>, there is no
    /// <c>response.Content.ReadAsStringAsync</c> left to await — <see cref="CapturedResponse.Body"/>
    /// already holds the fully-buffered text <see cref="ResponseCaptureHandler"/> read once, up
    /// front. The <c>Async</c> suffix and <see cref="Task"/> return type are kept anyway, matching
    /// this class's other two public methods, so a caller does not need to know which of the three
    /// happens to need real I/O and which does not.
    /// </para>
    /// </summary>
    public static Task ShouldMatchCapturedContractAsync(
        CapturedResponse captured, int expectedStatus, string schemaKey,
        SchemaBundle schemas, string testId, TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        cancellationToken.ThrowIfCancellationRequested();

        if (captured.Status != expectedStatus)
        {
            throw Failure(
            captured.Status, captured.RequestMethod, captured.RequestUri,
            expectedStatus, testId, elapsed, captured.Body, []);
        }

        var violations = schemas.Validate(schemaKey, captured.Body);
        if (violations.Count > 0)
        {
            throw Failure(
            captured.Status, captured.RequestMethod, captured.RequestUri,
            expectedStatus, testId, elapsed, captured.Body, violations);
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { return $"<body could not be read: {ex.GetType().Name}>"; }
    }

    /// <summary>
    /// Thin adapter over the lower-level <see cref="Failure(int, string?, string?, int, string, TimeSpan, string, IReadOnlyList{SchemaViolation})"/>
    /// for the raw-<see cref="HttpResponseMessage"/> callers (<see cref="ShouldMatchContractAsync"/>,
    /// <see cref="ShouldMatchStatusAsync"/>): pulls method/URI out of
    /// <see cref="HttpResponseMessage.RequestMessage"/>, which a captured-response call has no
    /// equivalent of to read from.
    /// </summary>
    private static ContractAssertionException Failure(
        HttpResponseMessage response, int expectedStatus, string testId,
        TimeSpan elapsed, string body, IReadOnlyList<SchemaViolation> violations)
    {
        var request = response.RequestMessage;
        return Failure(
        (int)response.StatusCode, request?.Method.Method, request?.RequestUri?.ToString(),
        expectedStatus, testId, elapsed, body, violations);
    }

    /// <summary>
    /// The one implementation of failure-message formatting, extracted so
    /// <see cref="ShouldMatchCapturedContractAsync"/> and the two existing
    /// <see cref="HttpResponseMessage"/>-based methods (via the <see cref="Failure(HttpResponseMessage, int, string, TimeSpan, string, IReadOnlyList{SchemaViolation})"/>
    /// overload above) produce byte-identical message shapes from whichever facts they each
    /// actually have on hand — an <see cref="HttpResponseMessage"/> in one case, a
    /// <see cref="CapturedResponse"/>'s already-buffered fields in the other. <paramref name="status"/>,
    /// <paramref name="method"/> and <paramref name="uri"/> stand in for what the two callers would
    /// otherwise each read differently (<c>(int)response.StatusCode</c> vs. <c>captured.Status</c>,
    /// and so on), so this method itself needs no knowledge of either source.
    /// </summary>
    private static ContractAssertionException Failure(
        int status, string? method, string? uri, int expectedStatus, string testId,
        TimeSpan elapsed, string body, IReadOnlyList<SchemaViolation> violations)
    {
        var sb = new StringBuilder();

        sb.Append(method ?? "?").Append(' ')
          .Append(uri ?? "<unknown uri>")
          .Append(" → expected ").Append(expectedStatus)
          .Append(", got ").Append(status)
          .Append(" (").Append(elapsed.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)).AppendLine("ms)");

        if (violations.Count > 0)
        {
            sb.AppendLine($"Schema: {violations.Count} violation(s)");
            foreach (var v in violations)
            {
                sb.Append("  ").Append(v.Kind).Append(" at ").AppendLine(v.Path);
            }
        }

        sb.Append("Run:  ").AppendLine(testId);
        sb.Append("Body: ").AppendLine(body.Length > BodyExcerptLimit ? body[..BodyExcerptLimit] + "…" : body);

        return new ContractAssertionException(sb.ToString());
    }
}
