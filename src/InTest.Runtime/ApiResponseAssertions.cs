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

        var captured = await CaptureAsync(response, cancellationToken).ConfigureAwait(false);
        await ShouldMatchCapturedContractAsync(
            captured, expectedStatus, schemaKey, schemas, testId, elapsed, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task ShouldMatchStatusAsync(
        HttpResponseMessage response, int expectedStatus, string testId, TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        // The early return is load-bearing, not an optimisation: converting to CapturedResponse reads
        // the body, and today a matching status never touches it. Pinned by
        // ShouldMatchStatusAsyncDoesNotReadTheBodyWhenTheStatusMatches.
        if ((int)response.StatusCode == expectedStatus)
        {
            return;
        }

        var captured = await CaptureAsync(response, cancellationToken).ConfigureAwait(false);
        await ShouldMatchCapturedStatusAsync(
            captured, expectedStatus, testId, elapsed, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The client-routed counterpart of <see cref="ShouldMatchStatusAsync"/>, for the same reason
    /// <see cref="ShouldMatchCapturedContractAsync"/> exists alongside <see cref="ShouldMatchContractAsync"/>:
    /// a client-routed case whose declared response carries no schema (a bodiless 204/205/304, or
    /// any <c>client-map.json</c> override — that override path bypasses every
    /// client-call-planning gate by design, so it can point a schema-less operation at the client
    /// too) still needs a captured-response assertion to call, or the template falls back to raw
    /// HTTP for a case the adopter explicitly opted into routing through the client.
    /// Status-only, deliberately: there is no schema to validate against when
    /// <c>TestCasePlan.SchemaKey</c> is null, exactly mirroring why
    /// <see cref="ShouldMatchStatusAsync"/> itself takes no <c>schemaKey</c> parameter.
    /// <para>
    /// Synchronous under the hood for the same reason as <see cref="ShouldMatchCapturedContractAsync"/>:
    /// <see cref="CapturedResponse.Body"/> is already fully buffered by <see cref="ResponseCaptureHandler"/>,
    /// so there is no I/O left to await. The <c>Async</c> suffix and <see cref="Task"/> return type
    /// are kept anyway, matching this class's other methods.
    /// </para>
    /// </summary>
    public static Task ShouldMatchCapturedStatusAsync(
        CapturedResponse captured, int expectedStatus, string testId, TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (captured.Status == expectedStatus)
        {
            return Task.CompletedTask;
        }

        throw Failure(
        captured.Status, captured.RequestMethod, captured.RequestUri,
        expectedStatus, testId, elapsed, captured.Body, []);
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
    /// [captured-is-the-single-shape]: the one place an <see cref="HttpResponseMessage"/> becomes a
    /// <see cref="CapturedResponse"/>. Reads the body, so callers that can avoid needing it (see
    /// <see cref="ShouldMatchStatusAsync"/>'s early return on a matching status) must not call this
    /// before they know they need it.
    /// </summary>
    private static async Task<CapturedResponse> CaptureAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var request = response.RequestMessage;
        return new CapturedResponse(
            (int)response.StatusCode, body, request?.Method.Method, request?.RequestUri?.ToString());
    }

    /// <summary>
    /// The one implementation of failure-message formatting. [captured-is-the-single-shape]: now
    /// that <see cref="ShouldMatchContractAsync"/> and <see cref="ShouldMatchStatusAsync"/> convert
    /// to a <see cref="CapturedResponse"/> and delegate to their captured counterparts (see
    /// <see cref="CaptureAsync"/>), every caller of this method already holds a
    /// <see cref="CapturedResponse"/>'s already-buffered fields — the
    /// <see cref="HttpResponseMessage"/>-shaped overload that used to sit in front of this one is
    /// gone, not merely unused. <paramref name="status"/>, <paramref name="method"/> and
    /// <paramref name="uri"/> stand in for <c>captured.Status</c>/<c>captured.RequestMethod</c>/
    /// <c>captured.RequestUri</c> so this method itself needs no knowledge of
    /// <see cref="CapturedResponse"/> either — it only formats.
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
