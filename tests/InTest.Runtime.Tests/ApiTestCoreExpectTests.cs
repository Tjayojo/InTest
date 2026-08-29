using System.Net;
using System.Reflection;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// [one-terminal-call]: direct tests for the consolidated call surface on <see cref="ApiTestCore"/>.
/// Deliberately avoids <c>InTestRun.InitializeAsync</c>, the same way
/// <see cref="ApiTestCoreCaptureTests"/> does — the status-only path needs neither a live
/// <c>InTestRun.Root</c> nor a <c>SchemaBundle</c>, only <c>Client</c> and <c>TestId</c>, both
/// reachable with the reflection hatches this class's subclass exposes.
/// </summary>
[TestClass]
public class ApiTestCoreExpectTests
{
    private sealed class TestableApiTestCore : ApiTestCore
    {
        /// <summary>
        /// <c>Client</c> is <c>{ get; private set; }</c>, set only inside <c>BeginTest</c> — the
        /// same escape-hatch shape <see cref="ApiTestCoreCaptureTests"/> uses for <c>_scope</c>.
        /// </summary>
        public void SetClient(HttpClient client) =>
            typeof(ApiTestCore).GetProperty("Client", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(this, client);

        public void SetTestId(string testId) =>
            typeof(ApiTestCore).GetField("_testId", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(this, testId);

        /// <summary>
        /// Overrides the pull seam so a test can supply a token without an MSTest TestContext —
        /// which is the entire point of the seam being <c>virtual</c> rather than a constructor
        /// argument.
        /// </summary>
        public CancellationToken TokenToReturn { get; set; } = CancellationToken.None;

        protected override CancellationToken TestCancellationToken => TokenToReturn;

        public CancellationToken ExposedTestCancellationToken => TestCancellationToken;

        public Task ExposedExpectStatus(int expectedStatus, HttpMethod method, string url) =>
            ExpectStatus(expectedStatus, method, url);

        public Task ExposedExpectStatus(int expectedStatus, HttpMethod method, string url, string body) =>
            ExpectStatus(expectedStatus, method, url, body);

        public Task ExposedExpectCapturedStatus(int expectedStatus, TimeSpan elapsed) =>
            ExpectCapturedStatus(expectedStatus, elapsed);

        public Task ExposedExpectCapturedContract(int expectedStatus, string schemaKey, TimeSpan elapsed) =>
            ExpectCapturedContract(expectedStatus, schemaKey, elapsed);
    }

    /// <summary>
    /// A stub handler that records what it was asked to send and returns a canned response, so a
    /// test can assert on the request the consolidated call built without a live server.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        public int CallCount { get; private set; }

        /// <summary>
        /// When set, the handler cancels this source <em>while the request is in flight</em> and
        /// then observes its own token. That is the only way to prove the caller's token reached
        /// the send: <see cref="HttpClient"/> links the caller's token with its own timeout source
        /// and <b>disposes that linked source when the request completes</b>, so a token captured
        /// here is already detached by the time the awaiting test body could cancel anything.
        /// Cancelling from inside the handler observes the linkage while it still exists, and needs
        /// no timing assumptions.
        /// </summary>
        public CancellationTokenSource? CancelDuringSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;

            if (CancelDuringSend is not null)
            {
                CancelDuringSend.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
                RequestMessage = request,
            };
        }
    }

    private static (TestableApiTestCore Core, StubHandler Handler) Harness(
        HttpStatusCode status, string body = "")
    {
        var handler = new StubHandler(status, body);
        var core = new TestableApiTestCore();
        core.SetClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        core.SetTestId("test-id");
        return (core, handler);
    }

    /// <summary>
    /// Observes <see cref="ApiTestCore"/>'s own default, which requires a subclass that does
    /// <b>not</b> override the seam. <see cref="TestableApiTestCore"/> overrides it, so a test
    /// written against that subclass cannot see the base implementation at all — it would pass even
    /// if the base threw, which is exactly the "green for a reason unrelated to what it guards"
    /// failure this project keeps finding. Verified by mutation: making the base body
    /// <c>throw new NotSupportedException()</c> must turn this test red.
    /// </summary>
    private sealed class UnoverriddenApiTestCore : ApiTestCore
    {
        public CancellationToken ExposedTestCancellationToken => TestCancellationToken;
    }

    /// <summary>
    /// The seam's default must be <see cref="CancellationToken.None"/>, not a throw: the neutral
    /// package has no way to obtain a real token, and a base class that threw would make
    /// <see cref="ApiTestCore"/> unusable to any adapter that has not overridden it yet.
    /// </summary>
    [TestMethod]
    public void TestCancellationTokenDefaultsToNoneWhenNotOverridden()
    {
        var core = new UnoverriddenApiTestCore();

        core.ExposedTestCancellationToken.ShouldBe(CancellationToken.None);
    }

    [TestMethod]
    public async Task ExpectStatusSendsTheMethodAndUrlAndPassesOnAMatch()
    {
        var (core, handler) = Harness(HttpStatusCode.NoContent);

        await core.ExposedExpectStatus(204, HttpMethod.Delete, "/api/orders/42");

        handler.CallCount.ShouldBe(1);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/orders/42");
    }

    [TestMethod]
    public async Task ExpectStatusThrowsWithTheRunFactsOnAMismatch()
    {
        var (core, _) = Harness(HttpStatusCode.InternalServerError, "boom");

        var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
            core.ExposedExpectStatus(204, HttpMethod.Delete, "/api/orders/42"));

        ex.Message.ShouldContain("expected 204");
        ex.Message.ShouldContain("got 500");
        ex.Message.ShouldContain("boom");
    }

    /// <summary>
    /// The body overload exists so a body-bearing case cannot silently send nothing — see the design's
    /// §3. ArgumentNullException.ThrowIfNull is the runtime half; the generator half is
    /// TemplateRendererTests.RendersAStringContentBodyFromTheFixture.
    /// </summary>
    [TestMethod]
    public async Task ExpectStatusWithABodySendsItAsJson()
    {
        var (core, handler) = Harness(HttpStatusCode.Created);

        await core.ExposedExpectStatus(201, HttpMethod.Post, "/api/orders", "{\"id\":1}");

        handler.LastRequestBody.ShouldBe("{\"id\":1}");
        handler.LastRequest!.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [TestMethod]
    public async Task ExpectStatusWithANullBodyThrowsRatherThanSendingNothing()
    {
        var (core, handler) = Harness(HttpStatusCode.Created);

        await Should.ThrowAsync<ArgumentNullException>(() =>
            core.ExposedExpectStatus(201, HttpMethod.Post, "/api/orders", null!));

        handler.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// The replacement for TemplateRendererTests.ThreadsTheCancellationTokenSoCooperativeCancellationWorks,
    /// which this change deletes: after the pull seam no generated raw case names cancellation at all,
    /// so the guard has to live here. Asserts the token is honoured BEFORE the handler runs — a token
    /// merely passed through but never observed would still let the request go out.
    /// </summary>
    [TestMethod]
    public async Task ExpectStatusHonoursTheSeamTokenBeforeSending()
    {
        var (core, handler) = Harness(HttpStatusCode.NoContent);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        core.TokenToReturn = cts.Token;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            core.ExposedExpectStatus(204, HttpMethod.Delete, "/api/orders/42"));

        handler.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// The pre-check above only catches a token that was <em>already</em> cancelled. Cooperative
    /// cancellation — a token cancelled while the request is in flight — requires the token to actually
    /// reach <c>Client.SendAsync</c>, and nothing else in this suite proves it does.
    /// <para>
    /// Established by mutation, not by reading: with <c>Client.SendAsync(request, cancellationToken)</c>
    /// changed to <c>Client.SendAsync(request)</c>, the entire runtime suite still passed. This test is
    /// what closes that hole, and it is the one that makes Task 6's deletion of
    /// <c>ThreadsTheCancellationTokenSoCooperativeCancellationWorks</c> honest.
    /// </para>
    /// <para>
    /// Asserts by cancelling <em>after</em> the send rather than comparing token identity:
    /// <see cref="HttpClient"/> always links the caller's token with its own timeout source, so the
    /// handler never sees the caller's token instance and <c>CanBeCanceled</c> is true either way.
    /// Propagation from this test's own source is the property that actually discriminates.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ExpectStatusPassesTheSeamTokenToTheSend()
    {
        var (core, handler) = Harness(HttpStatusCode.NoContent);
        using var cts = new CancellationTokenSource();
        core.TokenToReturn = cts.Token;

        handler.CancelDuringSend = cts;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            core.ExposedExpectStatus(204, HttpMethod.Delete, "/api/orders/42"));

        // CallCount 1, not 0, is what separates this from
        // ExpectStatusHonoursTheSeamTokenBeforeSending: the request DID reach the handler, so the
        // cancellation observed here came from the token travelling with the send rather than from
        // the pre-check refusing an already-cancelled token.
        handler.CallCount.ShouldBe(1);
    }

    /// <summary>
    /// The client branch's consolidated assertion. Takes elapsed as an argument because the stopwatch
    /// stays a visible local in the generated method — it must start before the pinned try, since the
    /// throwing path still needs a real number.
    /// </summary>
    [TestMethod]
    public async Task ExpectCapturedStatusPassesWhenTheCapturedStatusMatches()
    {
        var core = new TestableApiTestCore();
        core.SetTestId("test-id");
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot
        {
            Value = new CapturedResponse(200, "{}", "GET", "https://example.test/api/orders"),
        };

        try
        {
            await core.ExposedExpectCapturedStatus(200, TimeSpan.FromMilliseconds(5));
        }
        finally
        {
            InTestAmbient.LastCapturedResponse.Value = null;
        }
    }

    [TestMethod]
    public async Task ExpectCapturedStatusThrowsWhenTheCapturedStatusDiffers()
    {
        var core = new TestableApiTestCore();
        core.SetTestId("test-id");
        InTestAmbient.LastCapturedResponse.Value = new CapturedResponseSlot
        {
            Value = new CapturedResponse(500, "boom", "GET", "https://example.test/api/orders"),
        };

        try
        {
            var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
                core.ExposedExpectCapturedStatus(200, TimeSpan.FromMilliseconds(5)));

            ex.Message.ShouldContain("expected 200");
            ex.Message.ShouldContain("got 500");
        }
        finally
        {
            InTestAmbient.LastCapturedResponse.Value = null;
        }
    }
}
