using System.Net;
using System.Net.Http.Headers;
using System.Text;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// <see cref="SpecFetcher"/>'s failure table, driven through a stub transport so every row runs
/// without a socket. Each test asserts the <i>message</i> rather than only the throw, and asserts
/// the absence of "unexpected failure" — the same house rule <c>ConfigLoaderTests</c> states and
/// for the same reason: an exit-code assertion alone would pass against the defect these messages
/// exist to fix, since every one of these was already exit 2 before it had a sentence.
/// </summary>
[TestClass]
public class SpecFetcherTests
{
    private const string Url = "https://orders-staging.example.com/swagger/v1/swagger.json";
    private const string SpecJson = """{"openapi":"3.0.3","info":{"title":"Orders","version":"1.0"},"paths":{}}""";

    /// <summary>
    /// Answers one canned response, or throws one canned exception, and records whether it was
    /// invoked at all — the last of which is what the <c>[no-refetch]</c> tests elsewhere assert
    /// on rather than reading the code.
    /// </summary>
    private sealed class StubTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Respond(
        HttpStatusCode status, string body, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        return response;
    }

    private static async Task<string> ReasonForAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var transport = new StubTransport(respond);
        var message = (await Should.ThrowAsync<SpecLoadException>(
            () => SpecFetcher.FetchAsync(Url, transport, CancellationToken.None))).Message;

        message.ShouldNotContain("unexpected failure");
        return message;
    }

    [TestMethod]
    public async Task ReturnsTheBodyOfASuccessfulResponse()
    {
        using var transport = new StubTransport(_ => Respond(HttpStatusCode.OK, SpecJson));

        (await SpecFetcher.FetchAsync(Url, transport, CancellationToken.None)).ShouldBe(SpecJson);
    }

    [TestMethod]
    public async Task IssuesAGetToTheConfiguredUrl()
    {
        HttpRequestMessage? seen = null;
        using var transport = new StubTransport(request =>
        {
            seen = request;
            return Respond(HttpStatusCode.OK, SpecJson);
        });

        await SpecFetcher.FetchAsync(Url, transport, CancellationToken.None);

        seen!.Method.ShouldBe(HttpMethod.Get);
        seen.RequestUri!.ToString().ShouldBe(Url);
    }

    /// <summary>
    /// [anonymous]. The one failure an adopter cannot fix by correcting their URL — the URL is
    /// right, InTest simply cannot prove who it is — so it gets its own sentence carrying the
    /// fetch-it-yourself workaround. Without this branch it reads as "the fetch failed", sending
    /// someone to check a URL that was never the problem.
    /// </summary>
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, DisplayName = "401")]
    [DataRow(HttpStatusCode.Forbidden, DisplayName = "403")]
    public async Task ExplainsThatAuthenticatedEndpointsAreNotSupported(HttpStatusCode status)
    {
        var reason = await ReasonForAsync(_ => Respond(status, "denied", "text/plain"));

        reason.ShouldContain(Url, Case.Sensitive);
        reason.ShouldContain(((int)status).ToString());
        reason.ShouldContain("curl",
            customMessage: "the remedy has to be a command the adopter can run, not a shrug");
    }

    [TestMethod]
    public async Task NamesTheStatusCodeOfAFailedResponse()
    {
        var reason = await ReasonForAsync(_ => Respond(HttpStatusCode.NotFound, "no", "text/plain"));

        reason.ShouldContain(Url, Case.Sensitive);
        reason.ShouldContain("404");
    }

    /// <summary>
    /// A 200 with nothing in it. Worth its own sentence because the request demonstrably
    /// succeeded, so "could not be fetched" would be false — what went wrong is that the URL
    /// names something other than the document.
    /// </summary>
    [TestMethod]
    public async Task ExplainsAnEmptyBody()
    {
        var reason = await ReasonForAsync(_ => Respond(HttpStatusCode.OK, string.Empty));

        reason.ShouldContain("empty");
        reason.ShouldNotContain("could not be fetched",
            customMessage: "the request succeeded — saying otherwise sends the adopter to the wrong place");
    }

    /// <summary>
    /// [json-only], caught from the header. YAML is out of scope from a file or a URL alike, and
    /// an adopter pointing at <c>/swagger/v1/swagger.yaml</c> has made a specific, nameable
    /// mistake rather than an unparseable one.
    /// </summary>
    [TestMethod]
    [DataRow("application/yaml", DisplayName = "application/yaml")]
    [DataRow("text/yaml", DisplayName = "text/yaml")]
    [DataRow("application/x-yaml", DisplayName = "application/x-yaml")]
    public async Task ExplainsAYamlContentType(string mediaType)
    {
        var reason = await ReasonForAsync(_ => Respond(HttpStatusCode.OK, "openapi: 3.0.3", mediaType));

        reason.ShouldContain("YAML");
        reason.ShouldContain(Url, Case.Sensitive);
    }

    /// <summary>
    /// [json-only], caught from the body. The header check above is necessary but not sufficient:
    /// plenty of servers return <c>text/plain</c> or <c>application/octet-stream</c> for a
    /// <c>.yaml</c> file. Sniffing the first non-blank line catches those and nothing else — a
    /// JSON document's first non-whitespace character is always <c>{</c>.
    /// </summary>
    [TestMethod]
    public async Task ExplainsAYamlBodyServedUnderAnUnhelpfulContentType()
    {
        var reason = await ReasonForAsync(
            _ => Respond(HttpStatusCode.OK, "# a comment\nopenapi: 3.0.3\ninfo:\n  title: Orders\n", "text/plain"));

        reason.ShouldContain("YAML");
    }

    /// <summary>The sniff must not fire on JSON that merely mentions those words.</summary>
    [TestMethod]
    public async Task DoesNotMistakeJsonForYaml()
    {
        using var transport = new StubTransport(_ => Respond(
            HttpStatusCode.OK, """{"openapi":"3.0.3","info":{"description":"swagger: not yaml"},"paths":{}}"""));

        var fetched = await SpecFetcher.FetchAsync(Url, transport, CancellationToken.None);

        fetched.ShouldContain("openapi");
    }

    [TestMethod]
    public async Task RefusesABodyLargerThanTheCapWithoutBufferingIt()
    {
        var reason = await ReasonForAsync(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            // A declared length far past the cap, checked before the body is read at all — which
            // is why FetchAsync asks for ResponseHeadersRead rather than the default.
            response.Content.Headers.ContentLength = SpecFetcher.MaxBytes + 1L;
            return response;
        });

        reason.ShouldContain("larger than");
        reason.ShouldContain(SpecFetcher.MaxBytes.ToString());
    }

    /// <summary>
    /// The cap again, from the other side: a chunked response declares no <c>Content-Length</c>
    /// at all, so the header check above cannot see it and the streaming check has to.
    /// </summary>
    [TestMethod]
    public async Task RefusesAnUndeclaredBodyLargerThanTheCap()
    {
        var reason = await ReasonForAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream()),
        });

        reason.ShouldContain(SpecFetcher.MaxBytes.ToString());
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)' ');
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A failure that happens while the <i>body</i> is being read, not while the headers are.
    /// This is a real gap that reaching for <see cref="HttpCompletionOption.ResponseHeadersRead"/>
    /// opens up and that a naive implementation misses: with that option the <c>GetAsync</c> call
    /// returns as soon as the headers arrive, so a server that hangs, or drops the connection,
    /// part-way through a large document fails <i>after</i> the code that translates transport
    /// exceptions into adopter-facing sentences has already run.
    /// <para>
    /// Left unhandled it escapes as a raw <c>TaskCanceledException</c> and reaches
    /// <c>Program</c>'s crash floor, so a slow API — an entirely ordinary thing for a spec
    /// endpoint to be — is reported to the adopter as "intest: unexpected failure". Same exit
    /// code, and a sentence that blames the tool for the network.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ReportsATimeoutThatHappensWhileReadingTheBody()
    {
        var reason = await ReasonForAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(() => new TaskCanceledException("timed out"))),
        });

        reason.ShouldContain("30 seconds");
        reason.ShouldContain(Url, Case.Sensitive);
    }

    /// <summary>The same gap, reached by a connection dropped mid-body rather than a stall.</summary>
    [TestMethod]
    public async Task ReportsAConnectionLostWhileReadingTheBody()
    {
        var reason = await ReasonForAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(
                () => new HttpRequestException("The response ended prematurely."))),
        });

        reason.ShouldContain(Url, Case.Sensitive);
        reason.ShouldContain("ended prematurely");
    }

    /// <summary>Cancellation stays cancellation even when it happens mid-body.</summary>
    [TestMethod]
    public async Task PropagatesCancellationThatHappensWhileReadingTheBody()
    {
        using var cancelled = new CancellationTokenSource();
        using var transport = new StubTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(() =>
            {
                cancelled.Cancel();
                return new TaskCanceledException("cancelled");
            })),
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => SpecFetcher.FetchAsync(Url, transport, cancelled.Token));
    }

    /// <summary>A stream that fails on the first read, with whatever the test asks for.</summary>
    private sealed class ThrowingStream(Func<Exception> failure) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw failure();
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) => throw failure();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    public async Task NamesTheUnderlyingReasonForATransportFailure()
    {
        var reason = await ReasonForAsync(
            _ => throw new HttpRequestException("No such host is known. (orders-staging.example.com:443)"));

        reason.ShouldContain(Url, Case.Sensitive);
        reason.ShouldContain("No such host is known",
            customMessage: "DNS failure, connection refused and a TLS error have entirely " +
                           "different remedies — flattening them to one sentence hides which happened");
    }

    /// <summary>
    /// <see cref="HttpClient"/> reports a timeout and a genuine cancellation as the same
    /// exception type, so the token is the only thing that separates them. Reporting a Ctrl+C as
    /// "the server did not respond within 30 seconds" would blame a healthy server for a
    /// keystroke.
    /// </summary>
    [TestMethod]
    public async Task ReportsATimeoutAsATimeout()
    {
        var reason = await ReasonForAsync(_ => throw new TaskCanceledException("timed out"));

        reason.ShouldContain("30 seconds");
        reason.ShouldContain(Url, Case.Sensitive);
    }

    [TestMethod]
    public async Task PropagatesCancellationRatherThanRelabellingItATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var transport = new StubTransport(_ => throw new TaskCanceledException("cancelled"));

        await Should.ThrowAsync<OperationCanceledException>(
            () => SpecFetcher.FetchAsync(Url, transport, cancelled.Token));
    }

    /// <summary>
    /// A UTF-8 BOM reaches the JSON parser as a leading U+FEFF and fails the document, so the
    /// reader strips it. Servers do send one.
    /// </summary>
    [TestMethod]
    public async Task StripsAByteOrderMark()
    {
        using var transport = new StubTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(SpecJson)])
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        });

        (await SpecFetcher.FetchAsync(Url, transport, CancellationToken.None)).ShouldBe(SpecJson);
    }

    /// <summary>
    /// The document's own encoding rule beats the header's claim about it. JSON is UTF-8 by
    /// specification (RFC 8259 §8.1), so a server mislabelling a UTF-8 document as
    /// <c>iso-8859-1</c> must not cause every non-ASCII description in the spec to be mangled —
    /// which is what <c>HttpContent.ReadAsStringAsync</c> would do, since it honours the header.
    /// </summary>
    [TestMethod]
    public async Task ReadsAsUtf8EvenWhenTheHeaderClaimsOtherwise()
    {
        const string body = """{"openapi":"3.0.3","info":{"description":"café ✓"},"paths":{}}""";
        using var transport = new StubTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "iso-8859-1" } },
            },
        });

        (await SpecFetcher.FetchAsync(Url, transport, CancellationToken.None)).ShouldBe(body);
    }

    // ---- TryValidateUrl -----------------------------------------------------------------------

    [TestMethod]
    [DataRow("https://example.com/openapi.json", DisplayName = "https")]
    [DataRow("http://example.com/openapi.json", DisplayName = "http")]
    [DataRow("https://example.com:8080/swagger/v1/swagger.json?api-version=2", DisplayName = "port and query")]
    public void AcceptsAWellFormedUrl(string url)
    {
        SpecFetcher.TryValidateUrl(url, "spec.source", out var reason).ShouldBeTrue(reason);
    }

    /// <summary>
    /// The values that clear <see cref="SpecLoader.IsUrl"/>'s prefix test and are still not
    /// fetchable. This is the whole reason that predicate and this validator are two separate
    /// questions rather than one.
    /// </summary>
    [TestMethod]
    [DataRow("https://", DisplayName = "scheme only")]
    [DataRow("http://", DisplayName = "scheme only, http")]
    public void RefusesAMalformedUrl(string url)
    {
        SpecFetcher.TryValidateUrl(url, "spec.source", out var reason).ShouldBeFalse();

        reason.ShouldStartWith("spec.source", Case.Sensitive);
        reason.ShouldContain(url, Case.Sensitive);
        reason.ShouldContain("for example \"");
    }
}
