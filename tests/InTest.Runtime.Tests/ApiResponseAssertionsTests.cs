using System.Net;
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
}
