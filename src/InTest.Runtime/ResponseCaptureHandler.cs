using System.Text;

namespace InTest.Runtime;

/// <summary>
/// [capture-not-deserialize] — this handler is the feature's whole viability. A client-routed
/// generated test case calls a team's own typed client (Kiota, NSwag, Refit) instead of building
/// <c>HttpRequestMessage</c> by hand, but that client deserializes the response and discards the
/// raw bytes on its way to producing a strongly-typed result — and raw bytes are exactly what
/// <see cref="SchemaBundle.Validate"/> needs. This handler sits in the pipeline, between
/// <see cref="AuthHandler"/> and the wire, and buffers + stashes the raw response into
/// <see cref="InTestAmbient.LastCapturedResponse"/> before handing a still-readable copy of it back
/// up the chain for the typed client to consume normally.
/// <para>
/// Takes the normalized base URL by constructor injection, mirroring <see cref="AuthHandler"/>'s
/// <c>audience</c> parameter: <c>InTestRun.InitializeAsync</c> hoists <c>baseUrl</c> once and passes
/// it to both. This handler must not read <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// itself — doing so would give <c>Api:BaseUrl</c> two independent readers that could in principle
/// disagree (one reading it fresh per call, one reading the value <c>InTestRun</c> already
/// normalized once), which is exactly the kind of "two sources of truth for the same thing" this
/// change's own <c>[client-rides-the-api-pipeline]</c> guard exists to eliminate elsewhere.
/// </para>
/// <para>
/// Registered unconditionally by <see cref="InTestRun.InitializeAsync"/>, but only actually
/// attached to <see cref="InTestClients.Api"/> — never <see cref="InTestClients.Readiness"/>, the
/// same F10 exclusion <see cref="AuthHandler"/> already observes — when
/// <c>InTestRun.RegisterInTestClients</c>'s <c>captureEnabled</c> parameter is true, which in turn
/// is driven by <c>clientCaptureEnabled</c> in the generated project's <c>spec-paths.json</c>
/// ([capture-is-opt-in]: the <see cref="HttpResponseMessage.Content"/> replacement below carries an
/// unverified risk for whatever downstream deserializer eventually reads it, so it is worth
/// confining that risk to adopters who genuinely opted a client-routed case in, rather than
/// running it unconditionally for every suite this package ships to). Positioned after
/// <see cref="AuthHandler"/> in the handler chain, so it is the last handler to see the response on
/// its way back up — the closest observer to what the wire actually returned.
/// </para>
/// </summary>
public sealed class ResponseCaptureHandler(Uri baseUrl) : DelegatingHandler
{
    private readonly Uri _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));

    /// <summary>
    /// Why this authority check exists here, and why <see cref="InTestUrl.EnsureNoPrefixDuplication"/>
    /// does not already cover the hazard it guards against — the two are easy to conflate because
    /// both reason about a request ending up at the wrong place, but they catch genuinely different
    /// misconfigurations. <see cref="InTestUrl.EnsureNoPrefixDuplication"/> runs once, at
    /// <c>InitializeAsync</c> time, entirely in terms of <c>Api:BaseUrl</c> and the spec's own
    /// operation-path prefix; it says nothing about what an individual request's URI looks like at
    /// send time, because for every raw-HTTP case there is nothing to say — <c>HttpClient.BaseAddress</c>
    /// resolves every relative request URI those cases build, so there is exactly one place the
    /// request can go.
    /// <para>
    /// A typed client changes that. Kiota's request adapter is constructed with its own
    /// <c>BaseUrl</c> and builds a fully-qualified, <em>absolute</em> request URI directly from it —
    /// and per <see cref="HttpRequestMessage.RequestUri"/>'s own documented behavior, when a request
    /// URI is absolute, <c>HttpClient.BaseAddress</c> is not consulted at all. So a client
    /// constructed with a <c>BaseUrl</c> that disagrees with <c>Api:BaseUrl</c> compiles, runs, gets
    /// a run id and an auth header exactly like any other request through this pipeline, and is
    /// capture-recorded exactly like any other — but was sent wherever <em>that client's own
    /// configuration</em> pointed, silently ignoring <c>Api:BaseUrl</c> the whole time. Nothing about
    /// that failure mode involves a repeated path prefix, so <see cref="InTestUrl.EnsureNoPrefixDuplication"/>
    /// has no way to see it; it needs a check against the request actually being sent, which only
    /// this handler is positioned to make.
    /// </para>
    /// <para>
    /// A relative request URI needs no check at all: <c>HttpClient.BaseAddress</c> — set to
    /// <paramref name="baseUrl"/> by <c>InTestRun.RegisterInTestClients</c> — governs it
    /// unconditionally, the same way it already does for every raw-HTTP case, so there is no second
    /// authority for a relative URI to disagree with.
    /// </para>
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is { IsAbsoluteUri: true } uri &&
            !string.Equals(uri.Authority, _baseUrl.Authority, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "[client-rides-the-api-pipeline]: the outgoing request's authority " +
                $"'{uri.Authority}' does not match the configured Api:BaseUrl authority " +
                $"'{_baseUrl.Authority}'. Your typed client was constructed with its own base URL " +
                "that disagrees with Api:BaseUrl. HttpClient.BaseAddress only governs a *relative* " +
                "request URI — a typed client (Kiota, NSwag, Refit) that builds absolute request " +
                "URIs from its own configured base silently bypasses Api:BaseUrl entirely, so the " +
                "request still went through this pipeline (auth header, run id, capture) but landed " +
                "on the wrong host. Construct the client over " +
                "IHttpClientFactory.CreateClient(InTestClients.Api) and point its own base URL at " +
                "the same address as Api:BaseUrl.");
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Buffered into a byte array rather than left as the network's own StreamContent: a
        // network stream can only be read once, and the whole point of this handler is that a
        // downstream typed client is about to read it too — Kiota and NSwag both deserialize via
        // ReadAsStreamAsync, never ReadAsStringAsync (see the golden-test doc naming this same
        // fact for why a fake client that reads via ReadAsStringAsync would not actually prove
        // re-readability). Reading the bytes here first, then replacing Content with a fresh
        // ByteArrayContent built from them, is what makes both this read and the client's possible.
        var bytes = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // Decoded as UTF-8 unconditionally, matching ApiResponseAssertions.ReadBodyAsync's own
        // ReadAsStringAsync for a raw-HTTP case: neither this handler nor that existing method
        // consults Content-Encoding to decide whether to decompress first. That is not an oversight
        // specific to this change — see the Content-Encoding remarks a few lines down for the
        // confirmed reason it is already the existing behavior this handler simply does not
        // regress.
        var body = Encoding.UTF8.GetString(bytes);

        // Mutates the slot's own field rather than reassigning InTestAmbient.LastCapturedResponse
        // itself — the two are not interchangeable. See InTestAmbient.LastCapturedResponse's own
        // doc for the direct-experiment evidence: a plain AsyncLocal reassignment made here, this
        // deep inside an awaited call, would revert the instant control returns to whatever test
        // method awaited the typed client call that led here, because that is exactly how
        // ExecutionContext capture/restore behaves across a genuinely-suspending await. Mutating
        // the CapturedResponseSlot object that ApiTestCore.BeginTest already flowed down into this
        // handler is ordinary heap mutation, not dependent on that propagation at all, so it is
        // what actually survives. A null slot means no test's BeginTest is currently active for
        // this async flow (fixtures or readiness issuing a request during AssemblyInitialize, say)
        // — nothing to stash into, and not an error, mirroring how AuthHandler already treats a
        // null Identity override as ordinary rather than exceptional.
        if (InTestAmbient.LastCapturedResponse.Value is { } slot)
        {
            slot.Value = new CapturedResponse(
                (int)response.StatusCode, body, request.Method.Method, request.RequestUri?.ToString());
        }

        var replacement = new ByteArrayContent(bytes);
        if (response.Content is not null)
        {
            // Every header from the original Content.Headers is copied onto the replacement,
            // Content-Encoding and Content-Length included — confirmed by direct experiment
            // (ResponseCaptureHandlerTests.GzipEncodedContentRoundTripsCorrectlyWhenHeadersAreCopiedOntoTheReplacement)
            // to round-trip correctly rather than corrupt anything, and here is why: IHttpClientFactory's
            // default primary handler (SocketsHttpHandler) does not enable AutomaticDecompression,
            // so nothing in this pipeline ever decompresses a gzip-encoded response — the bytes
            // ReadAsByteArrayAsync just read above are already whatever the server actually sent
            // on the wire, compressed or not, exactly the same bytes a downstream typed client's
            // own ReadAsStreamAsync would have received had this handler never buffered them at
            // all. Copying Content-Length is likewise a no-op rather than a hazard: ByteArrayContent's
            // constructor already computes and sets its own Content-Length from bytes.Length, and
            // TryAddWithoutValidation on an already-present single-value header replaces rather than
            // duplicates it (HttpContentHeaders stores Content-Length as a single value, not a list),
            // so the copied value simply overwrites the constructor's — and the two must be numerically
            // identical anyway, since bytes.Length is exactly the length of the content the original
            // Content-Length claimed. None of this means a gzip-encoded body is ever decompressed for
            // schema validation purposes — CapturedResponse.Body above is the raw, still-compressed
            // bytes decoded as if they were UTF-8 text when Content-Encoding: gzip is present, which
            // is unusable for SchemaBundle.Validate. That is an existing limitation this handler does
            // not introduce (ApiResponseAssertions.ReadBodyAsync has the identical gap for a raw-HTTP
            // case, for the identical reason — AutomaticDecompression is off), not a new one worth
            // solving here.
            foreach (var header in response.Content.Headers)
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        response.Content = replacement;
        return response;
    }
}
