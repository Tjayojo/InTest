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
    /// <summary>
    /// What to say about a body that is not JSON and does not look like YAML either. The case
    /// this exists for is the most likely mistake in this whole feature: pointing spec.source at
    /// the Swagger <i>UI</i> page rather than the document it renders. That arrives as
    /// <c>text/html</c> (so the Content-Type check does not fire) beginning
    /// <c>&lt;!DOCTYPE html&gt;</c> (so the YAML sniff does not fire either), and answering it
    /// with <see cref="YamlReason"/> would be a confident, wrong diagnosis — sending the adopter
    /// hunting for a YAML/JSON toggle that has nothing to do with their problem.
    /// </summary>
    internal const string NotJsonReason =
        "InTest reads OpenAPI documents as JSON. Check that spec.source names the document " +
        "itself — a Swagger UI page, a login redirect or an error page is not the document, " +
        "even when the URL that serves it looks right.";

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
        // to be secure — is refused by SocketsHttpHandler itself, so no policy is needed here.
        transport ??= new SocketsHttpHandler();

        try
        {
            // Timeout.InfiniteTimeSpan, and the linked token below carries the deadline instead.
            //
            // This is not a way of saying "no timeout": HttpClient.Timeout is the wrong mechanism
            // here and quietly does much less than it appears to. It stops applying the moment
            // GetAsync returns, which — because ExchangeAsync asks for ResponseHeadersRead — is
            // as soon as the *headers* arrive. Reads on the content stream afterwards are covered
            // by nothing: SocketsHttpHandler has no read timeout of its own.
            //
            // Measured, because this is the opposite of what the property name suggests: a server
            // that sends `HTTP/1.1 200 OK`, a Content-Length, 19 bytes of body and then stalls
            // leaves a Timeout=5s client waiting past 60 seconds with no exception, no output and
            // no exit. `intest generate` hangs indefinitely on a half-open connection — exactly
            // the outcome FetchTimeout exists to prevent, and a plausible one, since a spec
            // endpoint stalling mid-response is what a struggling API under deployment looks like.
            //
            // A linked CancellationTokenSource covers the whole exchange instead: it cancels the
            // header phase and every body read alike, so one deadline means one thing. The catch
            // in ReadAsync still tests the *caller's* token to tell a genuine Ctrl+C from this
            // deadline firing — see there.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(FetchTimeout);

            using var client = new HttpClient(transport, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            return await ReadAsync(client, url, deadline.Token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsTransport)
            {
                transport.Dispose();
            }
        }
    }

    /// <summary>
    /// The whole exchange — headers <i>and</i> body — inside one translation of transport
    /// exceptions into adopter-facing sentences.
    /// <para>
    /// <b>Both halves, deliberately.</b> An earlier version wrapped only the <c>GetAsync</c> call,
    /// which is the obvious shape and is wrong here for a reason
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> introduces: that option makes
    /// <c>GetAsync</c> return the moment the headers arrive, so a server that stalls or drops the
    /// connection part-way through a large document fails <i>after</i> the translation has already
    /// run. The raw <c>TaskCanceledException</c> then escaped to <c>Program</c>'s crash floor and
    /// an entirely ordinary slow API was reported as "intest: unexpected failure". Pinned by
    /// <c>SpecFetcherTests.ReportsATimeoutThatHappensWhileReadingTheBody</c> and its
    /// connection-lost twin.
    /// </para>
    /// <para>
    /// The <see cref="SpecLoadException"/>s thrown inside the <c>try</c> — a failed status, an
    /// oversized or empty or YAML body — pass through untouched, because only the two transport
    /// exception types are caught. A catch-all here would re-wrap this type's own curated
    /// messages as though the network had failed.
    /// </para>
    /// </summary>
    private static async Task<string> ReadAsync(
        HttpClient client, string url, CancellationToken deadline, CancellationToken cancellationToken)
    {
        try
        {
            return await ExchangeAsync(client, url, deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine cancellation (Ctrl+C), not the deadline. Both arrive here as an
            // OperationCanceledException and the caller's token is the only thing that separates
            // them — without this branch, cancelling a slow fetch would be reported to the
            // adopter as "the server did not respond within 30 seconds", blaming a healthy server
            // for a keystroke. Rethrown rather than wrapped: Program's crash floor handles it.
            //
            // Tested on the *caller's* token deliberately, never on the linked one: the linked
            // token is cancelled in both cases, so testing it would collapse the distinction this
            // branch exists to draw.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Caught as OperationCanceledException rather than TaskCanceledException: the two
            // phases surface differently — the header phase raises TaskCanceledException (a
            // subclass), while a cancelled body read raises the base type — and catching only the
            // subclass would let the body case escape to the crash floor.
            throw new SpecLoadException(
                $"spec.source '{url}' did not respond within {FetchTimeout.TotalSeconds:0} seconds. " +
                "Check that the API is running and that the URL is reachable from here.", ex);
        }
        catch (HttpRequestException ex)
        {
            // Covers DNS failure, connection refused and TLS validation failure. The inner
            // message is included rather than flattened to "could not be reached": those have
            // entirely different remedies, and the adopter cannot tell which one happened from a
            // sentence that describes all of them equally well.
            throw new SpecLoadException(
                $"spec.source '{url}' could not be fetched: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            // A connection dropped part-way through the body, which is NOT an
            // HttpRequestException however much it looks like one: since .NET 8 a premature EOF
            // on the content stream raises System.Net.Http.HttpIOException, which derives from
            // IOException. Measured against a real socket that sends headers, 19 bytes and then
            // closes: "System.Net.Http.HttpIOException … The response ended prematurely, with at
            // least 982 additional bytes expected. (ResponseEnded)", with `is
            // HttpRequestException` false and `is IOException` true.
            //
            // Without this clause that escapes both of GenerateCommand's catches and reaches
            // Program's crash floor as "intest: unexpected failure: HttpIOException" — the tool
            // blamed for the network, and the exact sentence this type's messages exist to avoid.
            // Kept as IOException rather than HttpIOException so an ordinary socket IOException
            // is covered by the same clause.
            throw new SpecLoadException(
                $"spec.source '{url}' could not be fetched: {ex.Message}", ex);
        }
    }

    private static async Task<string> ExchangeAsync(
        HttpClient client, string url, CancellationToken cancellationToken)
    {
        // ResponseHeadersRead, not the default ResponseContentRead: the Content-Length check
        // below is only worth anything if it runs *before* the body has already been buffered
        // into memory. With the default, an oversized response is fully downloaded and then
        // reported as too large, which is the wrong order to do those two things in. See
        // ReadAsync's doc comment for what this option costs and how that is paid for.
        using (var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
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

    /// <summary>
    /// Whether <paramref name="text"/> opens like a YAML OpenAPI document. Examines the first
    /// non-blank, non-comment line and nothing further: a JSON document's first non-whitespace
    /// character is always one of <c>{ [ " -</c>, a digit, or <c>t</c>/<c>f</c>/<c>n</c>, none of
    /// which can begin <c>openapi:</c> or <c>swagger:</c>. A JSON string whose <i>value</i> is
    /// <c>"openapi: 3.0.3"</c> still starts its line with a quote.
    /// <para>
    /// Internal because <see cref="SpecSnapshot.Reprint"/> asks the same question from the other
    /// side — see its own comment for why the answer has to be conditional there rather than
    /// assumed.
    /// </para>
    /// <para>
    /// Scans to the first line break rather than <c>Split('\n')</c>-ing the whole body. Against
    /// the <see cref="MaxBytes"/> cap that split would materialise the entire document a third
    /// time (buffer, string, then array of lines) purely to read one line, which undercuts the
    /// point of having a cap at all.
    /// </para>
    /// </summary>
    internal static bool LooksLikeYaml(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var line = text.AsSpan(index, lineEnd - index).Trim();
            index = lineEnd + 1;

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            return line.StartsWith("openapi:", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("swagger:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
