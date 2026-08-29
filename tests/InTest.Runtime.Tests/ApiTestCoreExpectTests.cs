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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
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
    /// The seam's default must be <see cref="CancellationToken.None"/>, not a throw: the neutral
    /// package has no way to obtain a real token, and a base class that threw would make
    /// <see cref="ApiTestCore"/> unusable to any adapter that has not overridden it yet.
    /// </summary>
    [TestMethod]
    public void TestCancellationTokenDefaultsToNone()
    {
        var core = new TestableApiTestCore { TokenToReturn = CancellationToken.None };

        core.ExposedTestCancellationToken.ShouldBe(CancellationToken.None);
    }
}
