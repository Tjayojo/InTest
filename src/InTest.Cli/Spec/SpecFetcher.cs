using System.Net;
using System.Text;

namespace InTest.Cli.Spec;

/// <summary>
/// Fetches an OpenAPI document over HTTP, for a <c>spec.source</c> that names a URL (§9). A
/// sibling of <see cref="SpecLoader"/> rather than a third <c>LoadFrom*</c> overload on it: what
/// lives here is HTTP policy — timeout, size cap, status handling, content-type sniffing — and
/// none of that is parsing. <see cref="SpecLoader"/> stays a type that turns text into an
/// <c>OpenApiDocument</c>, whatever produced the text.
/// <para>
/// This type replaces <c>SpecLoader.UrlReason</c>, which existed only to apologise for the
/// capability being absent. Every message below inherits that constant's shape, which is the shape
/// every refusal in this repository uses (see <see cref="Naming.CSharpIdentifier.EmptyValueReason"/>):
/// name the setting, quote what was written, say what is wrong with it, then the remedy.
/// </para>
/// <para>
/// <b>Only <c>generate</c> in write mode calls this</b> — <c>[no-refetch]</c> in
/// <c>docs/superpowers/plans/2026-08-24-intest-url-spec-source.md</c>. <c>generate --check</c> and
/// <c>fixtures repair</c> both read the committed snapshot instead, so CI stays hermetic and a
/// command that only writes <c>fixtures/</c> never gets an opinion about what the spec now says.
/// </para>
/// </summary>
public static class SpecFetcher
{
    /// <summary>
    /// Well short of <see cref="HttpClient"/>'s own 100-second default, which is a very long time
    /// for a command to look hung with nothing on screen. A spec endpoint is a static document
    /// served by the API under test; if it has not answered in 30 seconds the interesting failure
    /// is "that host is not healthy", and saying so quickly is more useful than waiting.
    /// </summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Roughly an order of magnitude above the largest OpenAPI documents seen in practice (the
    /// big published cloud-provider specs run to a few MB). The cap exists so that a URL which
    /// turns out to serve something else entirely — a log stream, a tarball, an HTML error page
    /// with an infinite redirect loop behind it — fails with a sentence naming the size rather
    /// than by exhausting memory. Enforced twice, because either check alone has a hole: the
    /// <c>Content-Length</c> header is absent on a chunked response, and a header can lie.
    /// </summary>
    internal const int MaxBytes = 32 * 1024 * 1024;

    /// <summary>
    /// The remedy for the one failure an adopter cannot fix by fixing their URL. Quoted from
    /// <c>docs/getting-started.md</c>'s Phase 1, which has documented this exact fallback since
    /// before URLs were supported at all — so it is already written, already true, and already
    /// what an adopter behind an authenticated Swagger endpoint has to do.
    /// </summary>
    private const string AuthRemedy =
        "InTest fetches the document anonymously and cannot send credentials. Fetch it yourself " +
        "and commit the file — `curl -o specs/openapi.json <url>` — then point spec.source at " +
        "that path instead.";

    /// <summary>
    /// The one sentence about YAML, said the same way wherever it is noticed. Two layers can
    /// reach this conclusion — this type, from a <c>Content-Type</c> header or a leading
    /// <c>openapi:</c> line, and <see cref="SpecSnapshot.Reprint"/>, from a body that is not JSON
    /// at all — and an adopter pointing at a <c>/swagger/v1/swagger.yaml</c> endpoint should get
    /// the same answer either way rather than one sentence about YAML and one about a malformed
    /// document.
    /// </summary>
    internal const string YamlReason =
        "The document appears to be YAML. InTest reads OpenAPI documents as JSON; YAML input is " +
        "designed but not built, from a file or a URL alike. Point spec.source at the JSON form " +
        "of the document — most producers serve both.";

    /// <summary>
    /// Whether <paramref name="source"/> is a well-formed absolute <c>http</c>/<c>https</c> URI.
    /// Called after <see cref="SpecLoader.IsUrl"/> has already routed a value here, so this is a
    /// well-formedness question, not a kind-of-source one: <c>https://</c> on its own passes the
    /// prefix test and is not a URL anyone can fetch.
    /// <para>
    /// One validator, two callers — <c>InitCommand</c> judging <c>--spec</c> and
    /// <see cref="Configuration.ConfigLoader"/> judging <c>spec.source</c> — for the same reason
    /// the deleted <c>UrlReason</c> was one constant: the adopter's next move is identical either
    /// way, so the two sites must not answer in two voices.
    /// </para>
    /// </summary>
    public static bool TryValidateUrl(string source, string setting, out string reason)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"{setting} '{source}' starts with a URL scheme but is not a well-formed absolute " +
            "URL. It must name a host and a path — for example " +
            "\"https://orders-staging.example.com/swagger/v1/swagger.json\".";
        return false;
    }

    /// <summary>
    /// GETs <paramref name="url"/> and returns the response body as text. Every failure throws
    /// <see cref="SpecLoadException"/>, which <c>generate</c> already catches and maps to §5's
    /// exit 2 — so this method never needs its own exit-code opinion.
    /// </summary>
    /// <param name="transport">
    /// Test seam. <c>null</c> in production, where this method constructs and disposes its own
    /// handler. Tests pass a stub <see cref="HttpMessageHandler"/> so the failure table above can
    /// be driven without a socket. Named for what it is rather than typed as an
    /// <c>HttpClient</c>: handing the client in would let a caller also set the timeout and base
    /// address, which are this type's policy, not a caller's.
    /// </param>
    public static async Task<string> FetchAsync(
        string url, HttpMessageHandler? transport, CancellationToken cancellationToken)
    {
        if (!TryValidateUrl(url, "spec.source", out var reason))
        {
            throw new SpecLoadException(reason);
        }

        // Disposed only when this method created it. A handler passed in belongs to the test that
        // built it — disposing a caller's handler here would make a second call in the same test
        // fail on a disposed transport, which is a confusing way to learn about ownership.
        var ownsTransport = transport is null;

        // AllowAutoRedirect is left at its default (true) deliberately. A Swagger endpoint behind
        // a load balancer or an http->https upgrade redirects routinely, and refusing to follow
        // would reject a working URL for no benefit. The dangerous direction — an https URL
        // redirected down to http, which would silently drop TLS from a fetch the adopter asked
        // to be secure — is refused by SocketsHttpHandler itself and needs no policy here;
        // SpecFetcherTests.RefusesAnHttpsToHttpRedirect pins that rather than trusting this
        // comment.
        transport ??= new SocketsHttpHandler();

        try
        {
            using var client = new HttpClient(transport, disposeHandler: false) { Timeout = FetchTimeout };
            return await ReadAsync(client, url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsTransport)
            {
                transport.Dispose();
            }
        }
    }

    private static async Task<string> ReadAsync(
        HttpClient client, string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead, not the default ResponseContentRead: the Content-Length check
            // below is only worth anything if it runs *before* the body has already been buffered
            // into memory. With the default, an oversized response is fully downloaded and then
            // reported as too large, which is the wrong order to do those two things in.
            response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine cancellation (Ctrl+C), not the timeout. HttpClient reports both as
            // TaskCanceledException, so the token is the only thing that tells them apart —
            // without this branch, cancelling a slow fetch would be reported to the adopter as
            // "the server did not respond within 30 seconds", blaming a healthy server for a
            // keystroke. Rethrown rather than wrapped: Program's crash floor already handles it.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new SpecLoadException(
                $"spec.source '{url}' did not respond within {FetchTimeout.TotalSeconds:0} seconds. " +
                "Check that the API is running and that the URL is reachable from here.", ex);
        }
        catch (HttpRequestException ex)
        {
            // Covers DNS failure, connection refused and TLS validation failure alike. The inner
            // message is included rather than flattened to "could not be reached": those three
            // have entirely different remedies, and the adopter cannot tell which one happened
            // from a sentence that describes all of them equally well.
            throw new SpecLoadException(
                $"spec.source '{url}' could not be fetched: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new SpecLoadException(DescribeFailedStatus(url, response));
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > MaxBytes)
            {
                throw new SpecLoadException(
                    $"spec.source '{url}' returned {declaredLength} bytes, which is larger than " +
                    $"the {MaxBytes} byte limit InTest reads. Check that the URL names an OpenAPI " +
                    "document rather than something else served from the same host.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && IsYamlMediaType(mediaType))
            {
                throw new SpecLoadException(
                    $"spec.source '{url}' returned Content-Type '{mediaType}'. {YamlReason}");
            }

            var text = await ReadCappedAsync(response, url, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new SpecLoadException(
                    $"spec.source '{url}' returned an empty response body. The request succeeded " +
                    $"({(int)response.StatusCode}), so the URL resolves — check that it names the " +
                    "OpenAPI document itself rather than a page that merely links to it.");
            }

            // The Content-Type check above is necessary but not sufficient: plenty of servers
            // return application/octet-stream, or text/plain, for a .yaml file. Sniffing the
            // first non-blank line catches those, and catches nothing else — a JSON document
            // cannot begin with `openapi:` or `swagger:` at column 0, since its first
            // non-whitespace character is always `{`.
            if (LooksLikeYaml(text))
            {
                throw new SpecLoadException($"spec.source '{url}' did not return JSON. {YamlReason}");
            }

            return text;
        }
    }

    /// <summary>
    /// Streams the body, refusing at <see cref="MaxBytes"/>. The <c>Content-Length</c> check in
    /// <see cref="ReadAsync"/> does not make this redundant: a chunked response declares no
    /// length at all, and a declared length is a claim rather than a measurement.
    /// </summary>
    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response, string url, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[81920];
        using var accumulated = new MemoryStream();
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (accumulated.Length + read > MaxBytes)
            {
                throw new SpecLoadException(
                    $"spec.source '{url}' returned more than the {MaxBytes} byte limit InTest " +
                    "reads. Check that the URL names an OpenAPI document rather than something " +
                    "else served from the same host.");
            }

            accumulated.Write(buffer, 0, read);
        }

        // UTF-8 with BOM detection, and deliberately not response.Content.ReadAsStringAsync():
        // that honours the charset in the Content-Type header, so a server declaring
        // `charset=iso-8859-1` on a document that is really UTF-8 would silently mangle every
        // non-ASCII description in the spec. JSON is UTF-8 by specification (RFC 8259 §8.1), so
        // the document's own rule is a better authority here than the header's claim about it.
        // detectEncodingFromByteOrderMarks strips a UTF-8 BOM if the server sent one — left in
        // place it would reach the JSON parser as a leading U+FEFF and fail the document.
        accumulated.Position = 0;
        using var reader = new StreamReader(accumulated, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string DescribeFailedStatus(string url, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        // 401 and 403 get their own sentence because they are the one failure the adopter cannot
        // fix by correcting the URL — the URL is right, InTest simply cannot prove who it is
        // ([anonymous]). Without this branch they read as "the fetch failed", sending someone to
        // check a URL that was never the problem.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return $"spec.source '{url}' returned {status} {response.ReasonPhrase}. {AuthRemedy}";
        }

        return $"spec.source '{url}' returned {status} {response.ReasonPhrase}. " +
               "Check that the URL names the OpenAPI document and that the API is running.";
    }

    private static bool IsYamlMediaType(string mediaType) =>
        mediaType.Contains("yaml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("yml", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeYaml(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            return trimmed.StartsWith("openapi:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("swagger:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
