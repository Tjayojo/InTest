using System.Net;
using System.Text;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class ApiResponseAssertionsTests
{
    private const string BundleJson = """
    { "definitions": { "Order": { "type": "object", "required": ["id"],
      "properties": { "id": { "type": "string" } } } } }
    """;

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://h/api/orders/7")
        };

    [TestMethod]
    public async Task Passes_WhenStatusAndSchemaMatch()
    {
        using var response = Response(HttpStatusCode.OK, """{"id":"a"}""");
        await Should.NotThrowAsync(() => ApiResponseAssertions.ShouldMatchContractAsync(
            response, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.FromMilliseconds(12)));
    }

    [TestMethod]
    public async Task Fails_WithMethodUrlExpectedActualElapsedRunIdAndBody()
    {
        using var response = Response(HttpStatusCode.ServiceUnavailable, """{"error":"upstream timeout"}""");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchContractAsync(
                response, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.FromMilliseconds(1204)));

        ex.Message.ShouldContain("GET https://h/api/orders/7");
        ex.Message.ShouldContain("expected 200, got 503");
        ex.Message.ShouldContain("1,204ms");
        ex.Message.ShouldContain("run-1-test");
        ex.Message.ShouldContain("upstream timeout");
    }

    [TestMethod]
    public async Task Fails_WithEverySchemaViolationPathListed()
    {
        using var response = Response(HttpStatusCode.OK, """{"id":5}""");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchContractAsync(
                response, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.Zero));

        ex.Message.ShouldContain("#/id");
    }

    [TestMethod]
    public async Task StatusOnly_SkipsSchemaValidation()
    {
        using var response = Response(HttpStatusCode.NoContent, "");
        await Should.NotThrowAsync(() => ApiResponseAssertions.ShouldMatchStatusAsync(
            response, 204, "run-1-test", TimeSpan.Zero));
    }

    /// <summary>
    /// [captured-response-is-the-verdict]: the client-routed counterpart of
    /// <see cref="Passes_WhenStatusAndSchemaMatch"/>, against a <see cref="CapturedResponse"/>
    /// rather than a live <see cref="HttpResponseMessage"/> — the shape
    /// <see cref="ResponseCaptureHandler"/> hands a generated client-routed test case.
    /// </summary>
    [TestMethod]
    public async Task Captured_Passes_WhenStatusAndSchemaMatch()
    {
        var captured = new CapturedResponse(200, """{"id":"a"}""", "GET", "https://h/api/orders/7");

        await Should.NotThrowAsync(() => ApiResponseAssertions.ShouldMatchCapturedContractAsync(
            captured, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.FromMilliseconds(12)));
    }

    [TestMethod]
    public async Task Captured_Fails_WithMethodUrlExpectedActualElapsedRunIdAndBody_OnStatusMismatch()
    {
        var captured = new CapturedResponse(503, """{"error":"upstream timeout"}""", "GET", "https://h/api/orders/7");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchCapturedContractAsync(
                captured, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.FromMilliseconds(1204)));

        ex.Message.ShouldContain("GET https://h/api/orders/7");
        ex.Message.ShouldContain("expected 200, got 503");
        ex.Message.ShouldContain("1,204ms");
        ex.Message.ShouldContain("run-1-test");
        ex.Message.ShouldContain("upstream timeout");
    }

    [TestMethod]
    public async Task Captured_Fails_WithEverySchemaViolationPathListed_OnSchemaViolation()
    {
        var captured = new CapturedResponse(200, """{"id":5}""", "GET", "https://h/api/orders/7");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchCapturedContractAsync(
                captured, 200, "Order", SchemaBundle.FromJson(BundleJson), "run-1-test", TimeSpan.Zero));

        ex.Message.ShouldContain("#/id");
    }

    /// <summary>
    /// [stage-3b]: the client-routed counterpart of <see cref="StatusOnly_SkipsSchemaValidation"/> —
    /// closes the gap <c>ApiResponseAssertions</c>'s own class-level doc (its
    /// <see cref="ApiResponseAssertions.ShouldMatchCapturedStatusAsync"/> summary) names: a
    /// client-routed case whose declared response carries no schema (bodiless 204/205/304, or any
    /// <c>client-map.json</c> override) previously had no captured-response assertion to call at
    /// all, so <c>TemplateRenderer</c> fell back to raw HTTP rather than route it through the
    /// client. No body needed here, matching a real bodiless response.
    /// </summary>
    [TestMethod]
    public async Task Captured_StatusOnly_SkipsSchemaValidation()
    {
        var captured = new CapturedResponse(204, "", "DELETE", "https://h/api/orders/7");

        await Should.NotThrowAsync(() => ApiResponseAssertions.ShouldMatchCapturedStatusAsync(
            captured, 204, "run-1-test", TimeSpan.Zero));
    }

    [TestMethod]
    public async Task Captured_StatusOnly_Fails_WithMethodUrlExpectedActualElapsedRunIdAndBody_OnStatusMismatch()
    {
        var captured = new CapturedResponse(503, """{"error":"upstream timeout"}""", "DELETE", "https://h/api/orders/7");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchCapturedStatusAsync(
                captured, 204, "run-1-test", TimeSpan.FromMilliseconds(1204)));

        ex.Message.ShouldContain("DELETE https://h/api/orders/7");
        ex.Message.ShouldContain("expected 204, got 503");
        ex.Message.ShouldContain("1,204ms");
        ex.Message.ShouldContain("run-1-test");
        ex.Message.ShouldContain("upstream timeout");

        // Status-only: no schema was ever consulted, so the failure message must carry no
        // "Schema:" section at all — the same distinction StatusOnly_SkipsSchemaValidation's raw-
        // HTTP counterpart proves is unnecessary to assert on the passing side, but which matters
        // here: a "Schema:" line leaking in would mean this accidentally called the *Contract*
        // overload instead.
        ex.Message.ShouldNotContain("Schema:");
    }

    /// <summary>
    /// [captured-is-the-single-shape]: the raw pair become adapters over the captured pair, and the
    /// obvious adapter — always convert, then delegate — would read the response body on every
    /// matching-status call. Today ShouldMatchStatusAsync returns before touching the body. This test
    /// pins that, because the difference is invisible in every passing suite and shows up only as an
    /// extra read against a real API.
    /// </summary>
    [TestMethod]
    public async Task ShouldMatchStatusAsyncDoesNotReadTheBodyWhenTheStatusMatches()
    {
        var content = new ReadCountingContent("{}");
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        await ApiResponseAssertions.ShouldMatchStatusAsync(
            response, 200, "test-id", TimeSpan.FromMilliseconds(1));

        content.ReadCount.ShouldBe(0);
    }

    /// <summary>
    /// The counterpart: on a mismatch the body IS read, because the failure message quotes it.
    /// </summary>
    [TestMethod]
    public async Task ShouldMatchStatusAsyncReadsTheBodyWhenTheStatusDiffers()
    {
        var content = new ReadCountingContent("nope");
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content };

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            ApiResponseAssertions.ShouldMatchStatusAsync(
                response, 200, "test-id", TimeSpan.FromMilliseconds(1)));

        content.ReadCount.ShouldBe(1);
        ex.Message.ShouldContain("nope");
    }

    /// <summary>
    /// Counts reads so the two tests above can distinguish "did not read" from "read and discarded".
    /// StringContent cannot report this, hence a local subclass rather than a mock framework — this
    /// repository has no mocking dependency and the dependency policy in CONTRIBUTING.md is why.
    /// </summary>
    private sealed class ReadCountingContent : HttpContent
    {
        private readonly byte[] _bytes;

        public ReadCountingContent(string body) => _bytes = Encoding.UTF8.GetBytes(body);

        public int ReadCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            ReadCount++;
            return stream.WriteAsync(_bytes, 0, _bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }
}
