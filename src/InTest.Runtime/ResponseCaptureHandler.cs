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

        // Compared via GetLeftPart(UriPartial.Authority), not the bare Authority property, because
        // Authority is host+port only and cannot see a scheme downgrade: confirmed by direct
        // experiment, new Uri("https://api.example.com/").Authority and
        // new Uri("http://api.example.com/").Authority are both "api.example.com" (and stay equal
        // with explicit :443/:80 appended), while GetLeftPart(UriPartial.Authority) returns
        // "https://api.example.com" for the first and "http://api.example.com" for the second — the
        // distinction this check exists to make. Without this, a Kiota client built with
        // BaseUrl = "http://…" against an Api:BaseUrl of "https://…" would build an absolute URI,
        // bypass HttpClient.BaseAddress, pass the (bare-Authority) check, and go out in plaintext —
        // with AuthHandler having already attached the bearer token upstream of this handler, so the
        // token itself would be what leaked over the downgraded scheme.
        if (request.RequestUri is { IsAbsoluteUri: true } uri &&
            !string.Equals(
                uri.GetLeftPart(UriPartial.Authority),
                _baseUrl.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "[client-rides-the-api-pipeline]: the outgoing request's authority " +
                $"'{uri.GetLeftPart(UriPartial.Authority)}' does not match the configured " +
                $"Api:BaseUrl authority '{_baseUrl.GetLeftPart(UriPartial.Authority)}'. Your typed " +
                "client was constructed with its own base URL that disagrees with Api:BaseUrl. " +
                "HttpClient.BaseAddress only governs a *relative* request URI — a typed client " +
                "(Kiota, NSwag, Refit) that builds absolute request URIs from its own configured " +
                "base silently bypasses Api:BaseUrl entirely, so the request still went through " +
                "this pipeline (auth header, run id, capture) but landed on the wrong host or " +
                "scheme. Construct the client over IHttpClientFactory.CreateClient(InTestClients.Api) " +
                "and point its own base URL at the same address as Api:BaseUrl.");
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Captured before this handler does anything else to response.Content. Two things below
        // both need the *original* HttpContent, not whatever ends up in response.Content by the
        // time they run: the byte read just below, and the disposal at the very end of this method
        // (see that Dispose call's own comment for why it must be the original, not the
        // replacement — HttpResponseMessage.Dispose only disposes whatever Content is current at
        // dispose time).
        var originalContent = response.Content;

        // Buffered into a byte array rather than left as the network's own StreamContent: a
        // network stream can only be read once, and the whole point of this handler is that a
        // downstream typed client is about to read it too — Kiota and NSwag both deserialize via
        // ReadAsStreamAsync, never ReadAsStringAsync (see the golden-test doc naming this same
        // fact for why a fake client that reads via ReadAsStringAsync would not actually prove
        // re-readability). Reading the bytes here first, then replacing Content with a fresh
        // ByteArrayContent built from them, is what makes both this read and the client's possible.
        // Everything from here down that can observe originalContent — the byte read immediately
        // below, most obviously, but in principle any statement through the return — is wrapped in
        // try/finally specifically so the Dispose in the finally block runs on BOTH exits from this
        // region: the ordinary one (falls through to `return response;`) and the exceptional one
        // (ReadAsByteArrayAsync, or anything else in here, throws and unwinds). A plain statement at
        // the bottom of the method, by contrast, only ever runs on the ordinary exit — an earlier
        // version of this method had exactly that shape and its comment claimed the mid-stream-throw
        // case was covered, which was false: an exception thrown out of ReadAsByteArrayAsync
        // propagates straight out of SendAsync, and a statement positioned after that call, however
        // it reads on the page, never gets control back to execute. finally is the one construct the
        // CLR guarantees runs on both paths, which is exactly the guarantee this dispose needs.
        try
        {
            var bytes = originalContent is null
                ? []
                : await originalContent.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            var replacement = new ByteArrayContent(bytes);
            if (originalContent is not null)
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
                //
                // Headers are copied onto the replacement BEFORE the body is read from it, below —
                // deliberately, not just incidentally, because the body read needs whatever
                // Content-Type/charset header the response actually carried, and that header only
                // exists on replacement once this loop has run.
                foreach (var header in originalContent.Headers)
                {
                    replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Read via replacement.ReadAsStringAsync(), NOT Encoding.UTF8.GetString(bytes) — those are
            // not the same operation, and a comment here once claimed they were parity with
            // ApiResponseAssertions.ReadBodyAsync's own ReadAsStringAsync for a raw-HTTP case. They are
            // not: confirmed by direct experiment on net10.0, for the bytes EF BB BF (a UTF-8 BOM)
            // followed by {"state":"ok"}, Encoding.UTF8.GetString produces a string whose first
            // character is U+FEFF (length 15) — which JsonDocument.Parse then rejects with "'0xEF' is
            // an invalid start of a value" — while ByteArrayContent.ReadAsStringAsync strips the BOM
            // and produces a string starting at U+007B '{' (length 14), which parses cleanly.
            // ReadAsStringAsync also honours a charset parameter on Content-Type when one is present,
            // which GetString does not consult at all. An API that emits a UTF-8 BOM (common outside
            // ASP.NET Core, which never emits one) would therefore fail every client-routed case with a
            // bogus schema-validation error while its raw-HTTP sibling — which genuinely does go
            // through ReadAsStringAsync, via ApiResponseAssertions.ReadBodyAsync — passed. Reading from
            // replacement rather than originalContent because replacement is the one whose headers
            // (charset included) were populated just above; originalContent's headers are also intact
            // at this point; either would work for the header lookup, but replacement is what
            // downstream code retains, so reading from the same object keeps this reasoned about in one
            // place rather than two.
            var body = await replacement.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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

            response.Content = replacement;

            return response;
        }
        finally
        {
            // Dispose the ORIGINAL content, not the replacement now sitting in response.Content —
            // HttpResponseMessage.Dispose() only disposes whatever Content is current at the time it
            // is called, which on the ordinary path is the replacement; the original StreamContent
            // this handler drains above would otherwise never be disposed at all, since nothing else
            // holds a reference to it once response.Content is overwritten. This lives in a finally
            // block, not a plain statement after `response.Content = replacement;`, specifically so it
            // also runs when originalContent.ReadAsByteArrayAsync above throws mid-stream (a truncated
            // body, a dropped connection): a plain trailing statement is unreachable on that path — the
            // exception unwinds straight out of the try block and past where such a statement would
            // sit — while a finally block is the one construct the CLR runs regardless of whether the
            // try block completed normally or is currently unwinding an exception. On the exceptional
            // path response.Content is never reassigned, so originalContent is still the same
            // still-undrained StreamContent it was on entry; disposing it here is what returns its
            // connection rather than leaving it held indefinitely. On the ordinary path this runs
            // exactly once, after `return response;` has already evaluated its operand (a `return`
            // inside a try schedules the finally to run before control actually leaves the method), so
            // there is no double-dispose of either originalContent (only ever referenced here) or
            // replacement (never disposed here at all — it is now response.Content and is the caller's
            // to dispose via the returned HttpResponseMessage).
            originalContent?.Dispose();
        }
    }
}
