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

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { return $"<body could not be read: {ex.GetType().Name}>"; }
    }

    private static ContractAssertionException Failure(
        HttpResponseMessage response, int expectedStatus, string testId,
        TimeSpan elapsed, string body, IReadOnlyList<SchemaViolation> violations)
    {
        var request = response.RequestMessage;
        var sb = new StringBuilder();

        sb.Append(request?.Method.Method ?? "?").Append(' ')
          .Append(request?.RequestUri?.ToString() ?? "<unknown uri>")
          .Append(" → expected ").Append(expectedStatus)
          .Append(", got ").Append((int)response.StatusCode)
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
