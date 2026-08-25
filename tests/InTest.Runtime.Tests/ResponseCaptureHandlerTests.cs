using System.IO.Compression;
using System.Net;
using System.Text;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// [capture-not-deserialize]: <see cref="ResponseCaptureHandler"/> is the feature's whole
/// viability, per the plan's own words. These tests exercise it directly against the handler
/// pipeline, the same shape <see cref="AuthHandlerTests"/> and <see cref="RunIdHandlerTests"/>
/// already use, rather than through the full <c>InTestRun.InitializeAsync</c> weight.
/// </summary>
[TestClass]
public class ResponseCaptureHandlerTests
{
    private static readonly Uri ConfiguredBaseUrl = new("https://h.invalid/api/");

    /// <summary>
    /// Answers with a <see cref="StreamContent"/> body over a fresh <see cref="MemoryStream"/> —
    /// the same shape a live network response actually has, and a stream can only be read once.
    /// That single-read property is exactly what <see cref="ResponseCaptureHandler"/>'s own
    /// buffer-then-replace must survive: a fake that answered with <see cref="StringContent"/> or
    /// <see cref="ByteArrayContent"/> instead (both re-readable on their own) would make the
    /// re-readability tests below pass without proving anything.
    /// </summary>
    private sealed class StreamRespondingHandler(
        byte[] body, HttpStatusCode status = HttpStatusCode.OK,
        IEnumerable<(string Name, string Value)>? headers = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { Content = new StreamContent(new MemoryStream(body)) };
            foreach (var (name, value) in headers ?? [])
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
            return Task.FromResult(response);
        }
    }

    private static HttpMessageInvoker BuildInvoker(
        byte[] body, HttpStatusCode status = HttpStatusCode.OK,
        IEnumerable<(string Name, string Value)>? headers = null, Uri? configuredBaseUrl = null)
    {
        var inner = new StreamRespondingHandler(body, status, headers);
        var handler = new ResponseCaptureHandler(configuredBaseUrl ?? ConfiguredBaseUrl) { InnerHandler = inner };
        return new HttpMessageInvoker(handler);
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Stands in for <c>ApiTestCore.BeginTest</c> assigning a fresh <see cref="CapturedResponseSlot"/>
    /// before a test's requests run — required per <see cref="InTestAmbient.LastCapturedResponse"/>'s
    /// own doc: a plain <see cref="AsyncLocal{T}"/> reassignment made inside
    /// <see cref="ResponseCaptureHandler"/>'s own awaited call does not survive back up to this test
    /// method (confirmed by direct experiment), so every test here must flow a mutable slot
    /// downward first and read the handler's mutation back out of that same slot afterward, exactly
    /// as the real BeginTest/EndTest pairing does.
    /// </summary>
    [TestInitialize]
    public void Reset() => InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot();

    [TestCleanup]
    public void ClearSlot() => InTestAmbient.LastCapturedResponse.Value = null;

    [TestMethod]
    public async Task CapturesStatusBodyMethodAndUri()
    {
        using var invoker = BuildInvoker(Encoding.UTF8.GetBytes("""{"id":"a"}"""), HttpStatusCode.OK);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));
        var slot = InTestAmbient.LastCapturedResponse.Value!;

        await invoker.SendAsync(request, CancellationToken.None);

        var captured = slot.Value;
        captured.ShouldNotBeNull();
        captured!.Value.Status.ShouldBe(200);
        captured.Value.Body.ShouldBe("""{"id":"a"}""");
        captured.Value.RequestMethod.ShouldBe("GET");
        captured.Value.RequestUri.ShouldBe("https://h.invalid/api/orders/7");
    }

    /// <summary>
    /// No test's <c>BeginTest</c> is active (fixtures or readiness issuing a request during
    /// <c>AssemblyInitialize</c>, say) — the handler must not throw, must still forward the request
    /// normally, and simply has nothing to stash into. Mirrors how <see cref="AuthHandler"/> already
    /// treats a null <see cref="InTestAmbient.Identity"/> override as ordinary rather than
    /// exceptional.
    /// </summary>
    [TestMethod]
    public async Task DoesNotThrowWhenNoTestScopeIsActive()
    {
        InTestAmbient.LastCapturedResponse.Value = null;
        using var invoker = BuildInvoker(Encoding.UTF8.GetBytes("""{"id":"a"}"""));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        InTestAmbient.LastCapturedResponse.Value.ShouldBeNull();
    }

    /// <summary>
    /// The load-bearing assertion (per the task brief): a downstream Kiota/NSwag client
    /// deserializes via <c>ReadAsStreamAsync</c>, never <c>ReadAsStringAsync</c>, so the
    /// replacement content set by <see cref="ResponseCaptureHandler"/> must genuinely support a
    /// second, independent stream read after this handler already consumed the original
    /// (single-read) network stream to populate <see cref="InTestAmbient.LastCapturedResponse"/>.
    /// </summary>
    [TestMethod]
    public async Task ReplacementContentIsGenuinelyReReadableViaReadAsStreamAsync()
    {
        using var invoker = BuildInvoker(Encoding.UTF8.GetBytes("""{"id":"a"}"""));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).ShouldBe("""{"id":"a"}""");
    }

    [TestMethod]
    public async Task ContentHeadersSurviveTheReplacement()
    {
        using var invoker = BuildInvoker(
            Encoding.UTF8.GetBytes("""{"id":"a"}"""),
            headers: [("Content-Type", "application/json"), ("X-Custom-Header", "custom-value")]);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Content.Headers.TryGetValues("X-Custom-Header", out var values).ShouldBeTrue();
        values!.ShouldContain("custom-value");
    }

    /// <summary>
    /// THE UNVERIFIED EXPERIMENT, settled empirically per the task brief rather than reasoned
    /// about: what actually happens when <c>Content-Encoding: gzip</c> and <c>Content-Length</c>
    /// are copied onto the replacement <see cref="ByteArrayContent"/>, against genuinely gzipped
    /// bytes. <see cref="Microsoft.Extensions.Http"/>'s default <c>IHttpClientFactory</c> primary
    /// handler does not enable <c>AutomaticDecompression</c>, so this handler — like every other
    /// handler in the pipeline — only ever sees whatever bytes the server actually sent, compressed
    /// or not; nothing between the socket and this handler ever decompresses them.
    /// <para>
    /// <b>Measured result:</b> copying both headers verbatim onto <c>ByteArrayContent</c> round-trips
    /// cleanly. The replacement content's raw bytes are byte-identical to the original compressed
    /// input (proven below by re-reading and independently gzip-decompressing them back to the
    /// original text), and <c>Content-Length</c> ends up correct without any special-casing:
    /// <c>ByteArrayContent</c>'s constructor already computes and sets its own <c>Content-Length</c>
    /// from the buffered byte count, and copying the original header's value on top of it does not
    /// corrupt that — the two values are numerically identical by construction, since the byte count
    /// this handler buffered is exactly what the original <c>Content-Length</c> claimed. No
    /// corruption, no exception, no need to skip either header when copying.
    /// </para>
    /// <para>
    /// What is <em>not</em> solved by this — and is not a regression this handler introduces —
    /// is <see cref="CapturedResponse.Body"/> itself for a compressed response: it is decoded as
    /// UTF-8 unconditionally, without regard to <c>Content-Encoding</c>, so for a gzip-encoded
    /// response it holds mojibake, not the original JSON text. This is not new:
    /// <c>ApiResponseAssertions.ReadBodyAsync</c> has the exact same gap for a raw-HTTP case, for
    /// the exact same reason (no decompression anywhere in the pipeline). Schema validation against
    /// a compressed response was already unsupported before this change; this handler does not make
    /// that worse, and fixing it is out of scope here.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GzipEncodedContentRoundTripsCorrectlyWhenHeadersAreCopiedOntoTheReplacement()
    {
        const string originalText = """{"id":"compressed-value"}""";
        var compressedBytes = Gzip(originalText);

        using var invoker = BuildInvoker(
            compressedBytes,
            headers: [("Content-Encoding", "gzip"), ("Content-Type", "application/json")]);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));
        var slot = InTestAmbient.LastCapturedResponse.Value!;

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        // The raw bytes a downstream client's own ReadAsByteArrayAsync/ReadAsStreamAsync would
        // see are byte-identical to what the "server" sent — no corruption from the buffer-and-
        // replace step itself.
        var replayedBytes = await response.Content.ReadAsByteArrayAsync();
        replayedBytes.ShouldBe(compressedBytes);

        // A caller that decompresses explicitly (what a real typed client's own deserializer
        // would already have to do for a gzip-encoded response, on the live network, since
        // AutomaticDecompression is off there too) recovers the exact original text — proving the
        // replacement content is not corrupted, merely still compressed exactly as the original
        // network response was.
        await using var gzipStream = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);
        (await reader.ReadToEndAsync()).ShouldBe(originalText);

        // Content-Length survives the copy without corruption: ByteArrayContent's own computed
        // value and the copied original value are numerically identical, so TryAddWithoutValidation
        // effectively overwrites the constructor's value with an equal one.
        response.Content.Headers.ContentLength.ShouldBe(compressedBytes.LongLength);
        response.Content.Headers.ContentEncoding.ShouldContain("gzip");

        // The gap this test also confirms, named in this method's own doc: the captured Body is
        // not decompressed, so it is not the original JSON text for a compressed response.
        slot.Value!.Value.Body.ShouldNotBe(originalText);
    }

    [TestMethod]
    public async Task ThrowsWhenAnAbsoluteRequestUriAuthorityDoesNotMatchTheConfiguredBaseUrl()
    {
        using var invoker = BuildInvoker([], configuredBaseUrl: new Uri("https://configured.invalid/api/"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://different.invalid/api/orders/7"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
        () => invoker.SendAsync(request, CancellationToken.None));

        ex.Message.ShouldContain("[client-rides-the-api-pipeline]");
        ex.Message.ShouldContain("configured.invalid");
        ex.Message.ShouldContain("different.invalid");
    }

    [TestMethod]
    public async Task DoesNotThrowWhenAnAbsoluteRequestUriAuthorityMatches()
    {
        using var invoker = BuildInvoker(Encoding.UTF8.GetBytes("{}"), configuredBaseUrl: ConfiguredBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://h.invalid/api/orders/7"));

        await Should.NotThrowAsync(() => invoker.SendAsync(request, CancellationToken.None));
    }

    /// <summary>
    /// A relative request URI is unaffected by the authority check — <c>HttpClient.BaseAddress</c>
    /// governs it unconditionally, the same as every raw-HTTP case already relies on, so there is
    /// no second authority for it to disagree with. Sent through a bare <see cref="HttpMessageInvoker"/>
    /// rather than <see cref="HttpClient"/> specifically so the request URI reaches the handler
    /// still relative: <c>HttpClient.SendAsync</c> itself combines a relative URI with
    /// <c>BaseAddress</c> into an absolute one before any handler ever sees it, which would make
    /// this test unable to exercise the branch it exists to cover.
    /// </summary>
    [TestMethod]
    public async Task ARelativeRequestUriIsUnaffectedByTheAuthorityCheck()
    {
        using var invoker = BuildInvoker(Encoding.UTF8.GetBytes("{}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("orders/7", UriKind.Relative));

        await Should.NotThrowAsync(() => invoker.SendAsync(request, CancellationToken.None));
    }
}
