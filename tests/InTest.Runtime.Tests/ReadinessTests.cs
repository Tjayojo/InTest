using System.Net;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class ReadinessTests
{
    private sealed class ScriptedHandler(params HttpStatusCode[] script) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        public string? LastRequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastRequestUri = r.RequestUri?.ToString();
            var status = script[Math.Min(_index++, script.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [TestMethod]
    public async Task RequiresConsecutiveSuccessesSoAnOldInstanceCannotSatisfyIt()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        await Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token);

        handler.Calls.ShouldBe(4);
    }

    [TestMethod]
    public async Task ThrowsWithTheLastResponseWhenItNeverBecomesReady()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token));

        ex.Message.ShouldContain("did not become ready");
        ex.Message.ShouldContain("503");
    }

    /// <summary>
    /// Never answers, so the probe is cancelled by <see cref="HttpClient.Timeout"/> and raises
    /// <see cref="TaskCanceledException"/> rather than <see cref="HttpRequestException"/> — the
    /// half-open-connection shape.
    /// </summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// In production <see cref="HttpClient"/>'s 100s default fires inside
    /// <see cref="ReadinessOptions.TimeoutSeconds"/>' 120s window, so a stalled server used to
    /// escape as a raw <see cref="TaskCanceledException"/> naming neither the probe path nor the
    /// service. <see cref="StallingHandler.Calls"/> is asserted because the right exception type
    /// alone would also pass if the clause gave up after one attempt.
    /// </summary>
    [TestMethod]
    public async Task KeepsPollingAndReportsAReadinessTimeoutWhenEveryProbeStalls()
    {
        var handler = new StallingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://h/api/"),
            Timeout = TimeSpan.FromMilliseconds(200),
        };
        using var cts = TestSupport.TimeoutToken();

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token));

        ex.Message.ShouldContain("did not become ready");
        ex.Message.ShouldContain(nameof(TaskCanceledException),
            customMessage: "the last outcome must name what actually happened to the probe");
        handler.Calls.ShouldBeGreaterThan(1,
            "a stalled probe is one failed attempt, not a reason to abandon the wait");
    }

    /// <summary>
    /// The other side of that clause's filter. Without it, a Ctrl+C is absorbed as a probe failure
    /// and reported as a readiness timeout — blaming a service for a keystroke.
    /// </summary>
    [TestMethod]
    public async Task ACancelledTokenPropagatesRatherThanBeingTreatedAsAFailedProbe()
    {
        var handler = new StallingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(
            () => Readiness.WaitAsync(client, TestSupport.Options(consecutiveSuccesses: 2), cts.Token));
    }

    [TestMethod]
    public async Task DisabledReadinessIssuesNoRequests()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.Enabled = false;

        await Readiness.WaitAsync(client, options, cts.Token);
        handler.Calls.ShouldBe(0);
    }

    [TestMethod]
    public async Task FailsImmediatelyOnAStatusThatWillNeverChange()
    {
        // A 404 means the probe path is wrong, not that the service is starting. Waiting the
        // full timeout for one turns a three-second diagnosis into a two-minute one.
        var handler = new ScriptedHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.TimeoutSeconds = 120;

        var ex = await Should.ThrowAsync<ReadinessTimeoutException>(
            () => Readiness.WaitAsync(client, options, cts.Token));

        handler.Calls.ShouldBe(1, "it must not keep polling a terminal status");
        ex.Message.ShouldContain("will not change by waiting");
        ex.Message.ShouldContain("leading slash");
    }

    [TestMethod]
    [DataRow("health/ready", "https://h/api/health/ready", DisplayName = "no leading slash resolves under the base URL")]
    [DataRow("/health/ready", "https://h/health/ready", DisplayName = "leading slash resolves against the host root")]
    public async Task ProbePathResolutionFollowsStandardUriSemantics(string path, string expected)
    {
        // Health endpoints conventionally sit at the host root while the API sits under a
        // prefix, so the scaffold ships "/health/ready". Both forms must remain available.
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://h/api/") };
        using var cts = TestSupport.TimeoutToken();

        var options = TestSupport.Options(consecutiveSuccesses: 2);
        options.Path = path;

        await Readiness.WaitAsync(client, options, cts.Token);

        handler.LastRequestUri.ShouldBe(expected);
    }
}
