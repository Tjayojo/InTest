# Unified Call Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four-to-six lines of HTTP ceremony in every generated test case with one terminal call — `ExpectStatus` / `ExpectContract` for the raw branch, `ExpectCapturedStatus` / `ExpectCapturedContract` for the client branch.

**Architecture:** The send and the assertion move into `ApiTestCore` (the neutral runtime package) behind four `protected` methods. Cancellation reaches them through a **pull seam** — `protected virtual CancellationToken TestCancellationToken`, overridden in `ApiTestBase` to return `TestContext.CancellationToken` — so the generated file stops naming MSTest types entirely. Generation-time dispatch is unchanged: the CLI still decides raw vs. client and still decides what each parameter resolves to, so `CaseRole` gating is untouched.

**Tech Stack:** .NET 10 · MSTest 4.3.3 · Scriban 7.2.6 (template) · NJsonSchema (schema validation) · Shouldly 4.3.0 (test assertions)

**Source spec:** `docs/superpowers/specs/2026-08-27-intest-unified-call-surface-design.md` (revision 7). Named decisions referenced below — `[one-terminal-call]`, `[role-stays-in-the-argument]`, `[captured-is-the-single-shape]`, `[dispatch-stays-generation-time]`, `[prefer-the-platform]` — are defined in its §3.

---

## Before you start

**Read these three things.** They are short and each one prevents a specific mistake this plan cannot prevent for you:

1. **`CLAUDE.md`'s "Commands" section**, for the current Golden-suite timing figure. Tasks 8 and 9 run that suite. It takes minutes, not seconds, and a tool's default ~2-minute timeout cuts it off mid-flight in a way that reads as a hang. Pass an explicit timeout well past whatever figure is quoted there. **Do not** take a timing number from this plan or from the spec — both are copies, and copies go stale.
2. **The spec's §8**, for the measured assertion inventory. Tasks 6-8 below reproduce it, but §8 carries the reasoning and the caveats.
3. **`CLAUDE.md`'s "Three separate text-safety rules"**, if you touch anything that reaches the template. Task 5 does.

**This plan's test-file line numbers were measured against commit `5022526`.** If `git log --oneline -1` shows something else, re-locate each assertion by its content rather than trusting the line number. Every entry below names the assertion text for exactly this reason.

**Scope note:** Tasks 1-4 change the runtime packages. Tasks 5-8 change the generator and its tests. **Tasks 2, 3, 4 and 5 must land together** — the spec's §4 is explicit that a pull seam with no caller, or `Expect*` methods with no caller, are "dead code wearing a compatibility label". Task 1 is the one genuine standalone prerequisite. Commit per task, but do not open a PR that stops between 2 and 5.

---

## File Structure

**Modified — runtime (shipped packages, public surface):**

| File | Responsibility after this change |
|---|---|
| `src/InTest.Runtime/ApiResponseAssertions.cs` | The captured pair become the single implementations; the raw pair become thin adapters that convert `HttpResponseMessage` → `CapturedResponse` and delegate (Task 1) |
| `src/InTest.Runtime/ApiTestCore.cs` | Gains the pull seam and the four `Expect*` methods plus one private `SendAndAssertAsync` (Tasks 2-4) |
| `src/InTest.Runtime.MSTest/ApiTestBase.cs` | Gains one `protected override` returning `TestContext.CancellationToken` (Task 2). No signature changes |

**Modified — generator:**

| File | Responsibility after this change |
|---|---|
| `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` | Emits one terminal call per raw case and one consolidated assertion per client case |
| `src/InTest.Cli/Rendering/TemplateRenderer.cs` | Unchanged in behaviour — verify only. The model fields the new template shape needs already exist |

**Modified — tests:**

| File | Why |
|---|---|
| `tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs` | Task 1's blast radius |
| `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs` | **Created** — direct tests for the four `Expect*` methods and the pull seam |
| `tests/InTest.Cli.Tests/TemplateRendererTests.cs` | 13 assertions across 12 test methods (Task 6) |
| `tests/InTest.Cli.Tests/TemplateRendererClientTests.cs` | 5 loud + 5 vacuous + 1 falsely-green (Task 7) |
| `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs` | 1 loud + 2 vacuous + 1 stale comment (Task 8) |
| `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` | Regenerated (Task 8) |

**Modified — docs (Task 9):** the 2026-08-16 spec's §9 and four samples, `docs/getting-started.md`, `CONTRIBUTING.md`'s publishing checklist, and both `examples/*/…csproj` comments.

**Explicitly NOT modified:** `examples/*/Generated/**` — see Task 9 for why, and why that is a decision rather than an omission.

---

### Task 1: P2 — make the captured pair the single implementation

The raw pair and the captured pair currently duplicate the compare-then-format logic. `Failure(int status, string? method, string? uri, …)` is already the single message formatter, so this is a genuine extraction, not a rewrite.

**One behaviour must be preserved exactly, and it is the whole risk of this task:** `ShouldMatchStatusAsync` returns *before* reading the body when the status matches. A naive adapter that always converts to `CapturedResponse` would read the body on every happy-path call — a real behaviour change inside a change the spec promises is behaviour-identical.

**Files:**
- Modify: `src/InTest.Runtime/ApiResponseAssertions.cs`
- Test: `tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`

- [ ] **Step 1: Write the failing test for the preserved early return**

Add to `tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`:

```csharp
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
```

Add these usings to the top of the file if they are not already present:

```csharp
using System.Net;
using System.Text;
```

- [ ] **Step 2: Run the tests to verify they pass against today's code**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ShouldMatchStatusAsyncDoesNotReadTheBody|FullyQualifiedName~ShouldMatchStatusAsyncReadsTheBody"
```

Expected: **both PASS**. This is deliberate and is not a TDD violation — these are *characterization* tests pinning behaviour that already exists, written before the refactor precisely so the refactor cannot silently change it. They are the tests that will fail if you write the naive adapter in Step 3.

- [ ] **Step 3: Add the conversion helper**

In `src/InTest.Runtime/ApiResponseAssertions.cs`, add next to `ReadBodyAsync`:

```csharp
/// <summary>
/// [captured-is-the-single-shape]: the one place an <see cref="HttpResponseMessage"/> becomes a
/// <see cref="CapturedResponse"/>. Reads the body, so callers that can avoid needing it (see
/// <see cref="ShouldMatchStatusAsync"/>'s early return on a matching status) must not call this
/// before they know they need it.
/// </summary>
private static async Task<CapturedResponse> CaptureAsync(
    HttpResponseMessage response, CancellationToken cancellationToken)
{
    var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
    var request = response.RequestMessage;
    return new CapturedResponse(
        (int)response.StatusCode, body, request?.Method.Method, request?.RequestUri?.ToString());
}
```

- [ ] **Step 4: Rewrite the raw pair as adapters**

Replace the bodies of `ShouldMatchContractAsync` and `ShouldMatchStatusAsync` (signatures unchanged — they are public surface published in `0.1.0-preview.1`):

```csharp
public static async Task ShouldMatchContractAsync(
    HttpResponseMessage response, int expectedStatus, string schemaKey,
    SchemaBundle schemas, string testId, TimeSpan elapsed,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(response);
    ArgumentNullException.ThrowIfNull(schemas);

    var captured = await CaptureAsync(response, cancellationToken).ConfigureAwait(false);
    await ShouldMatchCapturedContractAsync(
        captured, expectedStatus, schemaKey, schemas, testId, elapsed, cancellationToken)
        .ConfigureAwait(false);
}

public static async Task ShouldMatchStatusAsync(
    HttpResponseMessage response, int expectedStatus, string testId, TimeSpan elapsed,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(response);

    // The early return is load-bearing, not an optimisation: converting to CapturedResponse reads
    // the body, and today a matching status never touches it. Pinned by
    // ShouldMatchStatusAsyncDoesNotReadTheBodyWhenTheStatusMatches.
    if ((int)response.StatusCode == expectedStatus)
    {
        return;
    }

    var captured = await CaptureAsync(response, cancellationToken).ConfigureAwait(false);
    await ShouldMatchCapturedStatusAsync(
        captured, expectedStatus, testId, elapsed, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 5: Delete the now-unused `Failure` overload**

Remove the `Failure(HttpResponseMessage response, int expectedStatus, string testId, TimeSpan elapsed, string body, IReadOnlyList<SchemaViolation> violations)` overload entirely, including its doc comment. Nothing calls it once Step 4 lands — the captured path uses the `Failure(int, string?, string?, …)` overload directly.

If the build reports it as still referenced, stop: that means Step 4 did not fully replace both bodies.

- [ ] **Step 6: Run the full runtime suite**

```bash
dotnet test tests/InTest.Runtime.Tests
```

Expected: **PASS**, including the two characterization tests from Step 1. If `ShouldMatchStatusAsyncDoesNotReadTheBodyWhenTheStatusMatches` fails, you wrote the naive adapter — restore the early return.

- [ ] **Step 7: Verify no public surface changed**

```bash
dotnet build InTest.sln
```

Expected: **Build succeeded, 0 Warning(s), 0 Error(s)** (`TreatWarningsAsErrors=true` is on, so any warning is an error).

- [ ] **Step 8: Commit**

```bash
git add src/InTest.Runtime/ApiResponseAssertions.cs tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs
git commit -m "refactor: captured assertions become the single implementation

[captured-is-the-single-shape] (P2 of the unified call surface design). The raw
HttpResponseMessage pair become thin adapters that convert to CapturedResponse and
delegate. Public signatures unchanged — both are published surface in 0.1.0-preview.1.

ShouldMatchStatusAsync keeps its early return on a matching status. Converting reads
the body, and today a match never touches it; two characterization tests pin that,
because the difference is invisible in a passing suite and shows up only as an extra
read against a real API."
```

---

### Task 2: The cancellation pull seam

`ApiTestCore` cannot name `TestContext`. It needs a token, and the token must be read *at call time* — MSTest replaces `TestContext.CancellationToken`'s source per test, so a value stashed at `BeginTest` goes stale.

**Files:**
- Modify: `src/InTest.Runtime/ApiTestCore.cs`
- Modify: `src/InTest.Runtime.MSTest/ApiTestBase.cs`
- Test: `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs` (created here, extended in Tasks 3-4)

- [ ] **Step 1: Write the failing test**

Create `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs`:

```csharp
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
}
```

> **Why the extra subclass.** The obvious version of this test —
> `new TestableApiTestCore { TokenToReturn = CancellationToken.None }` then asserting the result is
> `None` — is a tautology: it asserts that an override returns the value it was just assigned, and
> it passes even when `ApiTestCore`'s own default throws. An earlier revision of this plan
> prescribed exactly that, and it was caught by mutation rather than by review. **Do not collapse
> the two subclasses back into one.**

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **compile error** —
`error CS0115: '…TestableApiTestCore.TestCancellationToken': no suitable method found to override`.
(Not `CS1061 'does not contain a definition for'` — that is what the compiler emits for a missing
*member reference*; an `override` with nothing to bind to is CS0115.)

- [ ] **Step 3: Add the seam to `ApiTestCore`**

In `src/InTest.Runtime/ApiTestCore.cs`, add near `Schemas` and `TestId`:

```csharp
/// <summary>
/// [one-terminal-call]: how a cancellation token reaches the consolidated call without this
/// neutral class naming a test framework. A <b>pull</b> seam — read at the moment of use — not a
/// push seam that stashes a token at <c>BeginTest</c>.
/// <para>
/// The distinction is load-bearing and was established by reading MSTest's own behaviour, not
/// assumed: MSTest replaces the <c>CancellationTokenSource</c> behind
/// <c>TestContext.CancellationToken</c> per test (this is how <c>[Timeout]</c> is implemented), so
/// a token captured once at <c>BeginTest</c> and reused would be stale — cancellation would never
/// reach the request. Reading through this property at call time preserves today's
/// read-at-call-time semantics exactly.
/// </para>
/// <para>
/// Defaults to <see cref="CancellationToken.None"/> rather than throwing: an adapter that has not
/// overridden this is not broken, it simply has no token to offer, and a throwing default would
/// make this class unusable to it. <c>ApiTestBase</c> in <c>InTest.Runtime.MSTest</c> overrides it
/// with <c>TestContext.CancellationToken</c>.
/// </para>
/// </summary>
protected virtual CancellationToken TestCancellationToken => CancellationToken.None;
```

- [ ] **Step 4: Override it in `ApiTestBase`**

In `src/InTest.Runtime.MSTest/ApiTestBase.cs`, add inside the class:

```csharp
/// <summary>
/// [one-terminal-call]: the MSTest half of <see cref="ApiTestCore.TestCancellationToken"/>. Reads
/// <c>TestContext.CancellationToken</c> on every access, deliberately — see the base member's doc
/// for why caching it would break cancellation.
/// </summary>
protected override CancellationToken TestCancellationToken => TestContext.CancellationToken;
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **PASS**, 1 test.

- [ ] **Step 6: Verify the override compiles across the package boundary**

```bash
dotnet build InTest.sln
```

Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**. A `protected virtual` in one package overridden in another is ordinary C#, but this is the first such member in this codebase, so the build is the proof rather than the assumption.

- [ ] **Step 7: Commit**

```bash
git add src/InTest.Runtime/ApiTestCore.cs src/InTest.Runtime.MSTest/ApiTestBase.cs tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs
git commit -m "feat: add the TestCancellationToken pull seam

[one-terminal-call] P1. A protected virtual read at call time, not a token stashed at
BeginTest — MSTest replaces the CancellationTokenSource behind TestContext.CancellationToken
per test, so a cached token would be stale and cancellation would never reach the request.

Defaults to CancellationToken.None rather than throwing, so an adapter that has not
overridden it is degraded rather than broken. ApiTestBase overrides it.

Lands with the Expect* methods that consume it (design §4): alone it is a virtual member
with no caller."
```

---

### Task 3: `ExpectStatus` and `ExpectContract`

The raw branch's terminal call. Takes `string` for the URL, **not `Uri`** — `TemplateRenderer.QueryExpression` concatenates `+ InTestUrl.BuildQuery(...)` *outside* the `InTestUrl.Build(...)` call, so what the template hands over is a string expression.

**Files:**
- Modify: `src/InTest.Runtime/ApiTestCore.cs`
- Test: `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the `ApiTestCoreExpectTests` class:

```csharp
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
```

```csharp
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
/// Cancellation is triggered <em>from inside the handler</em>, not from the test body after the
/// await. Three simpler-looking assertions were tried and all three fail to discriminate:
/// comparing token identity (<see cref="HttpClient"/> hands the handler a linked token, never the
/// caller's instance), checking <c>CanBeCanceled</c> (true either way, because the timeout source
/// is always linked in), and cancelling after the send — which was written into an earlier revision
/// of this plan and <b>failed against correct code</b>, because <see cref="HttpClient"/> disposes
/// the linked source when the request completes and thereby detaches the handler's token before the
/// test body regains control.
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
```

**Both directions of this test were verified before it entered the plan** — it passes against the
real implementation and fails (`OperationCanceledException` not thrown) when
`Client.SendAsync(request, cancellationToken)` is changed to `Client.SendAsync(request)`. The
previous revision's version was not verified, and did not work.

And add these passthroughs to `TestableApiTestCore`:

```csharp
public Task ExposedExpectStatus(int expectedStatus, HttpMethod method, string url) =>
    ExpectStatus(expectedStatus, method, url);

public Task ExposedExpectStatus(int expectedStatus, HttpMethod method, string url, string body) =>
    ExpectStatus(expectedStatus, method, url, body);
```

(`using System.Net;` is already at the top of this file from Task 2 — `System.Net` is **not** in
this project's implicit usings, which carry `System.Net.Http` but not `System.Net`.)

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **compile error** — `'ApiTestCore' does not contain a definition for 'ExpectStatus'`.

- [ ] **Step 3: Implement the send-and-assert core**

In `src/InTest.Runtime/ApiTestCore.cs`, add these usings at the top:

```csharp
using System.Diagnostics;
using System.Text;
```

(Neither is in this project's implicit-usings set — confirmed against `obj/*/InTest.Runtime.GlobalUsings.g.cs`, which lists only `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading` and `System.Threading.Tasks`.)

Then add the methods:

```csharp
/// <summary>
/// [one-terminal-call]: the raw branch's whole HTTP ceremony — build the request, start the
/// stopwatch, send with the seam's token, stop, assert — behind one call. Before this, every
/// generated raw case spelled all five steps out, which meant every generated case named
/// <c>HttpRequestMessage</c>, <c>Client.SendAsync</c> and <c>HttpResponseMessage</c> directly.
/// <para>
/// <paramref name="url"/> is a <see cref="string"/> and deliberately not a <see cref="Uri"/>: the
/// generator emits query strings by concatenating <c>InTestUrl.BuildQuery(...)</c> *outside* the
/// <c>InTestUrl.Build(...)</c> call, so what arrives here is a string expression rather than a
/// composed URI. Taking <see cref="Uri"/> would force a parse the generator has no reason to pay.
/// </para>
/// </summary>
protected Task ExpectStatus(int expectedStatus, HttpMethod method, string url) =>
    SendAndAssertAsync(expectedStatus, schemaKey: null, method, url, body: null);

/// <inheritdoc cref="ExpectStatus(int, HttpMethod, string)"/>
/// <remarks>
/// The required-body overload. Deliberately a separate overload rather than an optional
/// <c>string? body = null</c> parameter: a body-bearing case whose body silently resolved to null
/// would send an empty request and assert a status against it, which is exactly the
/// "plausible default that lets a suite pass while asserting nothing" CLAUDE.md forbids.
/// </remarks>
protected Task ExpectStatus(int expectedStatus, HttpMethod method, string url, string body)
{
    ArgumentNullException.ThrowIfNull(body);
    return SendAndAssertAsync(expectedStatus, schemaKey: null, method, url, body);
}

/// <summary>
/// [one-terminal-call]: the contract form. <paramref name="schemaKey"/> stays explicit because it
/// cannot be derived at runtime — <c>TestPlanBuilder.ResolveSchemaKey</c> returns either a
/// component name or a synthesized <c>op:{key}:{status}:application/json</c>, and the first is not
/// recoverable from operation key and status alone.
/// </summary>
protected Task ExpectContract(int expectedStatus, string schemaKey, HttpMethod method, string url) =>
    SendAndAssertAsync(expectedStatus, schemaKey, method, url, body: null);

/// <inheritdoc cref="ExpectContract(int, string, HttpMethod, string)"/>
/// <remarks>The required-body overload — see <see cref="ExpectStatus(int, HttpMethod, string, string)"/>.</remarks>
protected Task ExpectContract(
    int expectedStatus, string schemaKey, HttpMethod method, string url, string body)
{
    ArgumentNullException.ThrowIfNull(body);
    return SendAndAssertAsync(expectedStatus, schemaKey, method, url, body);
}

/// <summary>
/// The single implementation behind all four raw entry points. <paramref name="schemaKey"/> null
/// means status-only.
/// </summary>
private async Task SendAndAssertAsync(
    int expectedStatus, string? schemaKey, HttpMethod method, string url, string? body)
{
    ArgumentNullException.ThrowIfNull(method);
    ArgumentNullException.ThrowIfNull(url);

    // Read the seam once per call, not once per use: the contract is read-at-call-time, and a
    // single call is one logical moment.
    var cancellationToken = TestCancellationToken;
    cancellationToken.ThrowIfCancellationRequested();

    using var request = new HttpRequestMessage(method, url);
    if (body is not null)
    {
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    var stopwatch = Stopwatch.StartNew();
    using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    stopwatch.Stop();

    if (schemaKey is null)
    {
        await ApiResponseAssertions.ShouldMatchStatusAsync(
            response, expectedStatus, TestId, stopwatch.Elapsed, cancellationToken)
            .ConfigureAwait(false);
    }
    else
    {
        await ApiResponseAssertions.ShouldMatchContractAsync(
            response, expectedStatus, schemaKey, Schemas, TestId, stopwatch.Elapsed, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **PASS**, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/InTest.Runtime/ApiTestCore.cs tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs
git commit -m "feat: add ExpectStatus and ExpectContract to ApiTestCore

[one-terminal-call]: the raw branch's build/send/time/assert ceremony behind one call.
url is string, not Uri — the generator concatenates BuildQuery outside Build, so what
arrives is a string expression.

Body is a required-body overload rather than an optional parameter: a body-bearing case
whose body resolved to null would send nothing and still assert a status, which is the
plausible-default failure CLAUDE.md forbids.

Includes the replacement cancellation guard for the template-level test this change
deletes — asserts the seam's token is observed before the handler runs, not merely
passed through."
```

---

### Task 4: `ExpectCapturedStatus` and `ExpectCapturedContract`

The client branch's consolidated assertion. **The pinned `try`/exception-filter/`catch` and the stopwatch stay in the generated code** — moving the filter into the runtime is what `[one-terminal-call]` explicitly argues against, and the stopwatch must start before the `try` because the throwing path still needs a real elapsed. Only the assertion call collapses.

**Files:**
- Modify: `src/InTest.Runtime/ApiTestCore.cs`
- Test: `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `ApiTestCoreExpectTests`:

```csharp
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
```

And the passthroughs:

```csharp
public Task ExposedExpectCapturedStatus(int expectedStatus, TimeSpan elapsed) =>
    ExpectCapturedStatus(expectedStatus, elapsed);

public Task ExposedExpectCapturedContract(int expectedStatus, string schemaKey, TimeSpan elapsed) =>
    ExpectCapturedContract(expectedStatus, schemaKey, elapsed);
```

> **Note on `ExpectCapturedContract`:** it is not directly unit-tested here, because it needs a real `SchemaBundle` from `InTestRun.Schemas` — a static loaded from a `spec-schemas.json` on disk. Its coverage is `GeneratedSuiteExecutionTests`, which runs a real generated suite against a stub with a real bundle. This is a deliberate, stated gap, not an oversight: the status-only form needs no bundle, which is why it *can* be tested here and is.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **compile error** — `'ApiTestCore' does not contain a definition for 'ExpectCapturedStatus'`.

- [ ] **Step 3: Implement**

In `src/InTest.Runtime/ApiTestCore.cs`:

```csharp
/// <summary>
/// [one-terminal-call]: the client branch's assertion, consolidated. Folds
/// <see cref="LastCapturedResponse"/>, <see cref="TestId"/> and the cancellation token into the
/// call, leaving the generated method's pinned <c>try</c>/exception-filter/<c>catch</c> and its
/// stopwatch exactly where they are.
/// <para>
/// <paramref name="elapsed"/> stays an explicit argument rather than being timed in here, and that
/// is the constraint that shapes this whole method: the stopwatch must start <em>before</em> the
/// generated <c>try</c>, because a typed client that throws still needs a real elapsed for the
/// failure message. Timing inside this call would measure nothing on the throwing path — the exact
/// path a contract test exists to report on.
/// </para>
/// </summary>
protected Task ExpectCapturedStatus(int expectedStatus, TimeSpan elapsed) =>
    ApiResponseAssertions.ShouldMatchCapturedStatusAsync(
        LastCapturedResponse, expectedStatus, TestId, elapsed, TestCancellationToken);

/// <inheritdoc cref="ExpectCapturedStatus(int, TimeSpan)"/>
/// <remarks>
/// The contract form. <paramref name="schemaKey"/> stays explicit for the same reason it does on
/// <see cref="ExpectContract(int, string, HttpMethod, string)"/> — it is not derivable at runtime.
/// </remarks>
protected Task ExpectCapturedContract(int expectedStatus, string schemaKey, TimeSpan elapsed) =>
    ApiResponseAssertions.ShouldMatchCapturedContractAsync(
        LastCapturedResponse, expectedStatus, schemaKey, Schemas, TestId, elapsed, TestCancellationToken);
```

There is deliberately **no** body overload on either captured method: the adopter's typed client sends the body, so a body parameter here could never have a caller.

- [ ] **Step 4: Run to verify they pass**

```bash
dotnet test tests/InTest.Runtime.Tests
```

Expected: **PASS**, whole suite.

- [ ] **Step 5: Build**

```bash
dotnet build InTest.sln
```

Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Runtime/ApiTestCore.cs tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs
git commit -m "feat: add ExpectCapturedStatus and ExpectCapturedContract

[one-terminal-call]: the client branch's assertion consolidated, folding
LastCapturedResponse, TestId and the token. The pinned try/exception-filter/catch and the
stopwatch stay in generated code — moving the filter into the runtime is what
[one-terminal-call] argues against, and the stopwatch must start before the try because
the throwing path still needs a real elapsed.

No body overload on either: the adopter's typed client sends the body, so the parameter
could never have a caller."
```

---

### Task 5: Emit the new shape from the template

**Files:**
- Modify: `src/InTest.Cli/Rendering/Templates/mstest-class.scriban:90-118`
- Verify only (expect no change): `src/InTest.Cli/Rendering/TemplateRenderer.cs`

- [ ] **Step 1: Replace the client branch's assertion**

In `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`, replace this block:

```
{{~ if tc.schema_key_literal ~}}
        await ApiResponseAssertions.ShouldMatchCapturedContractAsync(
            LastCapturedResponse, {{ tc.expected_status }}, "{{ tc.schema_key_literal }}", Schemas, TestId, stopwatch.Elapsed,
            TestContext.CancellationToken);
{{~ else ~}}
        await ApiResponseAssertions.ShouldMatchCapturedStatusAsync(
            LastCapturedResponse, {{ tc.expected_status }}, TestId, stopwatch.Elapsed, TestContext.CancellationToken);
{{~ end ~}}
```

with:

```
{{~ if tc.schema_key_literal ~}}
        await ExpectCapturedContract({{ tc.expected_status }}, "{{ tc.schema_key_literal }}", stopwatch.Elapsed);
{{~ else ~}}
        await ExpectCapturedStatus({{ tc.expected_status }}, stopwatch.Elapsed);
{{~ end ~}}
```

Leave the `try`, both `catch` clauses, `stopwatch.Stop();` and the `ApiClient<...>()` call **exactly as they are**.

- [ ] **Step 2: Replace the raw branch**

Replace this block:

```
        using var request = new HttpRequestMessage(
            HttpMethod.{{ tc.http_method_pascal }},
            InTestUrl.Build("{{ tc.path_template_literal }}"{{ tc.path_argument_list }}){{ tc.query_expression }});
{{~ if tc.has_body ~}}
        request.Content = new StringContent(FixtureBody("{{ tc.operation_key_literal }}")!, Encoding.UTF8, "application/json");
{{~ end ~}}

        var stopwatch = Stopwatch.StartNew();
        using var response = await Client.SendAsync(request, TestContext.CancellationToken);
        stopwatch.Stop();

{{~ if tc.schema_key_literal ~}}
        await ApiResponseAssertions.ShouldMatchContractAsync(
            response, {{ tc.expected_status }}, "{{ tc.schema_key_literal }}", Schemas, TestId, stopwatch.Elapsed,
            TestContext.CancellationToken);
{{~ else ~}}
        await ApiResponseAssertions.ShouldMatchStatusAsync(
            response, {{ tc.expected_status }}, TestId, stopwatch.Elapsed, TestContext.CancellationToken);
{{~ end ~}}
```

with:

```
{{~ if tc.schema_key_literal ~}}
        await ExpectContract({{ tc.expected_status }}, "{{ tc.schema_key_literal }}", HttpMethod.{{ tc.http_method_pascal }},
            InTestUrl.Build("{{ tc.path_template_literal }}"{{ tc.path_argument_list }}){{ tc.query_expression }}{{ if tc.has_body }},
            FixtureBody("{{ tc.operation_key_literal }}")!{{ end }});
{{~ else ~}}
        await ExpectStatus({{ tc.expected_status }}, HttpMethod.{{ tc.http_method_pascal }},
            InTestUrl.Build("{{ tc.path_template_literal }}"{{ tc.path_argument_list }}){{ tc.query_expression }}{{ if tc.has_body }},
            FixtureBody("{{ tc.operation_key_literal }}")!{{ end }});
{{~ end ~}}
```

**Three things to be careful about here:**
- `tc.query_expression` already renders as ` + InTestUrl.BuildQuery(...)` **outside** the `Build(...)` parenthesis, or as empty. Do not wrap it.
- The `{{ if tc.has_body }}` inside the argument list uses `{{` not `{{~` deliberately — the `~` variants trim surrounding whitespace and would eat the newline and indentation that keep the emitted argument list readable.
- `tc.expected_status` and `tc.http_method_pascal` stay **bare** (unquoted); the `*_literal` fields stay **quoted**. `TemplateEscapingGuardTests` enforces exactly this by quote parity, mechanically.

- [ ] **Step 3: Remove the now-unused header usings**

In the same template, the class header emits `using System.Diagnostics;` and `using System.Text;`. After this change:
- `Encoding` appeared only in the raw body arm you just deleted, and the client branch never used it — so `using System.Text;` is now unused in **every** generated class. Remove it.
- `Stopwatch` is still used by the client branch. Keep `using System.Diagnostics;`, but it is now unused in a class with **no** client-routed case.

Unused usings are not a build error here (the scaffold sets `Nullable` but not `TreatWarningsAsErrors` — verified at `InitCommand.cs:463`), so this is tidiness, not correctness. Do not spend time on conditional emission of `using System.Diagnostics;`; note it and move on.

- [ ] **Step 4: Confirm `TemplateRenderer.cs` needs no change**

```bash
grep -n "emits_fixture_lookup\|QueryExpression\|has_body" src/InTest.Cli/Rendering/TemplateRenderer.cs
```

Expected: `emits_fixture_lookup = c.Role == CaseRole.Success` at ~line 145, plus the `QueryExpression` method. **No edits.** `[role-stays-in-the-argument]` and `[dispatch-stays-generation-time]` both mean the generator's decisions are unchanged — only the text it emits around them moves.

- [ ] **Step 5: Render once by hand and read the output**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~TemplateRendererTests.CallsTheContractAssertionWhenASchemaIsKnown"
```

Expected: **FAIL**, with the Shouldly diff showing the new `await ExpectContract(...)` shape. Read that diff — it is the first sight of the emitted code, and a malformed argument list or a doubled `+` shows up here rather than three tasks later.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Cli/Rendering/Templates/mstest-class.scriban
git commit -m "feat: emit the unified call surface from the template

[one-terminal-call]: raw cases become one ExpectStatus/ExpectContract call; client cases
keep their stopwatch and pinned try/catch and collapse only the assertion.

Generated code no longer names HttpRequestMessage, Client.SendAsync, HttpResponseMessage
or TestContext.CancellationToken. Its only transport-facing type is now HttpMethod.

Test updates follow in the next commits — this one is deliberately red."
```

---

### Task 6: Update `TemplateRendererTests`

**These break loudly.** No vacuity risk except one entry, which is called out.

**Files:**
- Modify: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`

- [ ] **Step 1: Run the suite and capture the failure list**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~TemplateRendererTests"
```

Expected: **12 failed**. The measured list, by line and by what each asserts:

| line | assertion | change to |
|---|---|---|
| 152 | `ShouldContain("ShouldMatchContractAsync")` | `ShouldContain("ExpectContract(")` |
| 159 | `ShouldContain("ShouldMatchStatusAsync")` | `ShouldContain("ExpectStatus(")` |
| **160** | `ShouldNotContain("ShouldMatchContractAsync")` | **`ShouldNotContain("ExpectContract(")`** — see Step 3 |
| 166 | `ShouldContain("TestContext.CancellationToken")` | **delete the test** — see Step 4 |
| 200 | `ShouldContain("new StringContent(")` | delete — the runtime builds the content now |
| 201 | `ShouldContain("application/json")` | delete — same |
| 235 | `IndexOf("new HttpRequestMessage(")` ordering vs `RequireFixture(` | anchor on `"await ExpectContract("` — **not** `ExpectStatus`, see below |
| 362, 471, 483, 581 | anchor `"…\r\n\r\n        using var request"` | anchor on `"        await Expect"` |
| 389 | `IndexOf("new HttpRequestMessage(")` vs `RequireMultipleIdentities(` | anchor on `"await Expect"` |
| 407 | `IndexOf("new HttpRequestMessage(")` vs `UseIdentity(` | anchor on `"await Expect"` |
| 522 | 3-way ordering on `IndexOf("new HttpRequestMessage(")` | anchor on `"await Expect"` |

- [ ] **Step 2: Update the straightforward positives**

For lines 152 and 159, replace the asserted string as in the table. For the ordering assertions
(235, 389, 407, 522) replace only the `IndexOf` anchor:

```csharp
var buildRequest = rendered.IndexOf("await ExpectContract(", StringComparison.Ordinal);
```

**Which terminal call each test emits differs, so render the fixture and look rather than copying an
anchor from this plan.** Measured: line 235's `CallsRequireFixtureBeforeBuildingTheRequest` builds
its plan with `schemaKey: "Order"`, so it emits `await ExpectContract(`. The other three use
`PlanAuth(...)`, whose `SchemaKey` is always null, so `await ExpectStatus(` is correct there — and
the bare prefix `"await Expect"` is safer still, since these tests care about *a* raw terminal call,
not which one. An earlier revision of this plan gave `ExpectStatus` for all four and was wrong
for 235.

For the four blank-line anchors (362, 471, 483, 581), replace `using var request` with `await Expect`:

```csharp
rendered.ShouldContain("        UseIdentity(IdentitySlot.Secondary);\r\n\r\n        await Expect");
```

Leave every `ShouldNotContain("\r\n\r\n\r\n")` and `ShouldNotContain("\r\n\r\n    }")` whitespace guard **exactly as is** — they survive the change and still discriminate. They caught a real stray-blank-line defect during the design's own measurement, so they are earning their place.

- [ ] **Step 3: Fix the one vacuous assertion**

Line 160 is `ShouldNotContain("ShouldMatchContractAsync")` inside `FallsBackToStatusOnlyWhenNoSchemaIsDeclared`. After this change nothing emits that string under **any** template regression, so it would keep passing while discriminating nothing.

```csharp
rendered.ShouldNotContain("ExpectContract(");
```

This entry was named by no revision of the design and by neither party in review until it was measured. If you find another `ShouldNotContain` whose forbidden string can no longer be emitted, treat it the same way and say so in the commit message.

- [ ] **Step 4: Delete `ThreadsTheCancellationTokenSoCooperativeCancellationWorks`**

It asserts `ShouldContain("TestContext.CancellationToken")` against a raw-only plan. After the pull seam no raw generated case names cancellation, so the string is genuinely gone and the test cannot be repaired at template level.

Its replacement is **two** tests from Task 3, and both are required for the deletion to be honest:
`ExpectStatusHonoursTheSeamTokenBeforeSending` (an already-cancelled token is refused before the
request goes out) and `ExpectStatusPassesTheSeamTokenToTheSend` (the token reaches
`Client.SendAsync`, which is what the word *cooperative* in the deleted test's name refers to).
**Confirm both exist and pass before deleting anything here.** The first alone does not cover
threading — measured: with the token dropped from the send, the whole runtime suite still passed. Delete the test method and add a comment where it was:

```csharp
// [one-terminal-call]: the cancellation-threading guard moved to
// InTest.Runtime.Tests/ApiTestCoreExpectTests.ExpectStatusHonoursTheSeamTokenBeforeSending.
// It cannot live here any more: after the pull seam no raw generated case names cancellation at
// all, so there is no string for a template-level assertion to match. Note that
// TemplateRendererClientTests.PassesTheCancellationTokenByNameRatherThanPositionally still passes
// and still mentions TestContext.CancellationToken — but it covers the *client call expression*
// (the typed client's own cancellationToken: argument), not InTest's send. Do not read it as
// proof that this branch is still covered.
```

That comment is not decoration. Without it, the next reader greps for `TestContext.CancellationToken`, finds the client test green, and concludes nothing was lost.

- [ ] **Step 5: Confirm `RendersAStringContentBodyFromTheFixture` still passes**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~RendersAStringContentBodyFromTheFixture"
```

Its line 199 — `ShouldContain("FixtureBody(\"createOrder\")")` — **survives untouched**, because the body overload still emits `FixtureBody("…")!`. Only its siblings at 200/201 needed deleting.

**This test is already the guard the design asks for** ("a `TemplateRendererTests` assertion that a `has_body` case emits the body argument"). Do not write a new one. Do not delete this one.

- [ ] **Step 6: Run the file green**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~TemplateRendererTests"
```

Expected: **PASS**.

- [ ] **Step 7: Commit**

```bash
git add tests/InTest.Cli.Tests/TemplateRendererTests.cs
git commit -m "test: update TemplateRendererTests for the unified call surface

12 methods updated. Ordering anchors move from new HttpRequestMessage( to await Expect;
the two StringContent assertions go away because the runtime builds the content now.

Line 160's ShouldNotContain(\"ShouldMatchContractAsync\") would have gone vacuous —
nothing can emit that string any more — so it now forbids ExpectContract( instead. That
entry was named by no revision of the design and by neither party in review; it was found
by measuring.

ThreadsTheCancellationTokenSoCooperativeCancellationWorks is deleted rather than repaired:
after the pull seam no raw case names cancellation, so no template-level assertion can
cover it. Replaced by ApiTestCoreExpectTests.ExpectStatusHonoursTheSeamTokenBeforeSending,
with a comment left behind warning that the surviving client-branch token test covers
Kiota's argument, not InTest's send."
```

---

### Task 7: Update `TemplateRendererClientTests`

**This is the file that fails quietly.** Five assertions break loudly, five would go vacuous, and one stays green for a reason unrelated to what it guards.

**Files:**
- Modify: `tests/InTest.Cli.Tests/TemplateRendererClientTests.cs`

- [ ] **Step 1: Fix the loud failures**

| line | today | change to |
|---|---|---|
| 348 | `ShouldContain("ShouldMatchCapturedContractAsync(\r\n            LastCapturedResponse, 200, …")` | `ShouldContain("await ExpectCapturedContract(200, \"Order\", stopwatch.Elapsed);")` |
| 383, 417, 426 | `ShouldContain("new HttpRequestMessage(")` | the terminal call that case actually emits — see the note below |
| 402 | `ShouldContain("ShouldMatchCapturedStatusAsync(\r\n            LastCapturedResponse, …")` | `ShouldContain("await ExpectCapturedStatus(200, stopwatch.Elapsed);")` |

**On 383, 417 and 426:** these three assert that a *raw* case in a client-routed class still builds
its own request. Whether each now emits `await ExpectStatus(` or `await ExpectContract(` depends on
whether that test's plan fixture declares a schema key — **do not guess**. Run the file first and
read the Shouldly diff, which prints the rendered output and shows you which form appeared.
Asserting on the bare prefix `"await Expect"` is also acceptable and is arguably better: what these
tests actually care about is "a raw terminal call rather than a client call", not which of the two
it is.

- [ ] **Step 2: Replace the five vacuous discriminators**

Each of these is a `ShouldNotContain` whose forbidden string can no longer be emitted by any regression. **Rewriting the positive half and leaving these produces a green test that discriminates nothing** — which is the entire failure mode this step exists to prevent.

| line | today | replace with |
|---|---|---|
| 349 | `ShouldNotContain("ShouldMatchContractAsync(\r\n            response,")` | `ShouldNotContain("ExpectContract(")` |
| 357 | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |
| 358 | `ShouldNotContain("Client.SendAsync(")` | `ShouldNotContain("ExpectContract(")` |
| 403 | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |
| 404 | `ShouldNotContain("ShouldMatchCapturedContractAsync(")` | `ShouldNotContain("ExpectCapturedContract(")` |

357 and 358 sit in `EmitsNoRawHttpRequestBuildingForAClientRoutedCase`, which **still reports green** after the change — measured. It is the only one of the five that gives you no signal at all. Together the replacement pair means "neither raw terminal call appears in a client-routed case", which is what the test was always for.

The new names are substring-safe in both directions: `ExpectStatus(` is not a substring of `ExpectCapturedStatus(`, nor `ExpectContract(` of `ExpectCapturedContract(`. This is why the methods are named as they are — a name like `ExpectCaptured` would collide and silently reintroduce the vacuity.

- [ ] **Step 3: Fix the assertion that stays green for the wrong reason**

Line 401 is `rendered.ShouldContain("LastCapturedResponse")`, inside `RoutesThroughTheClientAndAssertsStatusOnlyWhenTheCaseHasNoSchemaKey`. It was written to pin *the assertion call consuming the captured response*. After this change it is satisfied by the pinned `catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null)` filter instead — measured directly by updating only line 402 and leaving 401 alone: `Passed: 1`.

Delete line 401. Line 402's replacement now carries the real claim.

**This is the most dangerous category in the change, because nothing goes red to announce it.** If you find another assertion in this file that passes both before and after your edit for a *different* reason than it was written for, treat it the same way.

- [ ] **Step 4: Leave these alone — they survive and still discriminate**

- **385** `ShouldNotContain("LastCapturedResponse")` — counter-intuitive but correct: the pinned catch filter keeps that substring in client-branch output, so a regression that rendered the client branch for a raw case still trips it. An earlier review claimed this goes vacuous and **withdrew the claim on re-measurement**. Do not "fix" it.
- **384, 416** `ShouldNotContain("ApiClient<")`
- **335-340** `StartsTheStopwatchBeforeTheTryBlock` — the stopwatch stays a visible local, so this passes unchanged. It is the guard on the one constraint Task 4 had to respect.
- **266** `PassesTheCancellationTokenByNameRatherThanPositionally` — passes unchanged. See Task 6 Step 4's comment for why it is not proof of cancellation coverage.
- **440, 441** whitespace guards.

- [ ] **Step 5: Run green**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: **PASS**, all 628 tests.

- [ ] **Step 6: Commit**

```bash
git add tests/InTest.Cli.Tests/TemplateRendererClientTests.cs
git commit -m "test: update TemplateRendererClientTests, including five silent ones

Five assertions break loudly and are retargeted. Five ShouldNotContain assertions would
have gone vacuous — their forbidden strings can no longer be emitted — and now forbid the
new call names instead. Only one of those five sits in a test that still reports green
(EmitsNoRawHttpRequestBuildingForAClientRoutedCase), so four would have been repaired by
accident while in the file and one would not.

Line 401's ShouldContain(\"LastCapturedResponse\") is deleted: after this change it is
satisfied by the pinned catch filter rather than by the assertion call it was written to
pin. Measured by updating only 402 and leaving 401 — the test passed. Nothing goes red to
announce that category, which makes it the most dangerous one here.

Line 385 is deliberately untouched: it looks like it should go vacuous and does not, for
the same catch-filter reason."
```

---

### Task 8: Golden suite and the golden file

`InTest.Golden.Tests` is the only suite proving generated code both **compiles and runs**. Exactly one of its assertions breaks.

**Files:**
- Modify: `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`
- Modify: `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt`

- [ ] **Step 1: Read the current Golden timing figure**

```bash
grep -n "3m4\|golden\|Golden" CLAUDE.md | head -20
```

Take the figure from CLAUDE.md's Commands section and pass a timeout **well past** it on every command in this task. The suite shells out to real `dotnet build` and `dotnet test` per scaffolded temp project; a cold NuGet cache roughly doubles it. A run cut off at a default ~2-minute timeout looks exactly like a hang.

- [ ] **Step 2: Fix the one loud failure**

Line 1066: `ShouldContain("ShouldMatchCapturedStatusAsync(")` → `ShouldContain("await ExpectCapturedStatus(")`.

- [ ] **Step 3: Fix the two vacuous assertions**

| line | today | replace with |
|---|---|---|
| 1069 | `ShouldNotContain("ShouldMatchCapturedContractAsync(")` | `ShouldNotContain("ExpectCapturedContract(")` |
| 1070 | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |

Both sit in the same test as 1066, so it fails and you will be in the file — but rewriting 1066 and leaving these yields a green test discriminating nothing.

- [ ] **Step 4: Fix the stale comment at line ~1812**

It describes "a bare `HttpRequestMessage`/`Client.SendAsync` pair". Update it to describe the `ExpectStatus`/`ExpectContract` shape. Leave the `ApiClient<` occurrence-count assertion near 1815 alone — it survives.

- [ ] **Step 5: Regenerate the golden file**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **Inconclusive**, having written the source copy. This is the documented contract of that environment variable — it writes and then refuses to claim success.

- [ ] **Step 6: Re-run without the variable to actually verify**

```bash
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **PASS**.

- [ ] **Step 7: Read the regenerated golden file**

```bash
git diff tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt
```

This diff **is the product**. Read every line of it. You are looking for:
- each raw case reduced to one `await ExpectStatus(...)` or `await ExpectContract(...)`
- `FixtureParameter(...)` still present on **Success** cases only, and `Guid.NewGuid().ToString()` still on the 401/403/404 siblings — `[role-stays-in-the-argument]`, and the single most important thing to confirm by eye
- no stray blank lines, no doubled `+` in a query expression, no `using System.Text;`

If the role gating looks wrong, stop and re-read `TemplateRenderer.cs:145`. Nothing in this plan should have changed it.

- [ ] **Step 8: Run the whole Golden suite**

```bash
dotnet test tests/InTest.Golden.Tests
```

Expected: **PASS**. Reference point from the design's own measurement of this exact change: `GeneratedSuiteExecutionTests` 22 passed / 1 failed *before* Step 2's fix, `CompileVerificationTests` 7 passed / 0 failed unchanged throughout.

- [ ] **Step 9: Commit**

```bash
git add tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt
git commit -m "test: update the Golden suite and regenerate the golden file

One loud failure at 1066. Two ShouldNotContain assertions at 1069/1070 would have gone
vacuous in a test that fails at 1066 first — repairable by accident, so stated explicitly.
Stale comment at ~1812 updated.

The regenerated golden file is the actual deliverable of this change: every raw case is
now one terminal call, and role gating is unchanged — FixtureParameter on Success cases,
unmatchable values on the 401/403/404 siblings."
```

---

### Task 9: Documentation and the release gate

**Files:**
- Modify: `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` (§9 and four samples)
- Modify: `docs/getting-started.md:290`, `:371`
- Modify: `CONTRIBUTING.md` — the "Publishing checklist" section (~line 564)
- Modify: `examples/Catalog.ApiTests/Catalog.ApiTests.csproj`, `examples/Orders.ApiTests/Orders.ApiTests.csproj`
- Modify: `tests/InTest.Architecture.Tests/ExampleProjectVersionMarkerTests.cs` (comment only)

- [ ] **Step 1: Update the 2026-08-16 spec**

Five sites, all showing the old generated shape:

| location | what is wrong |
|---|---|
| §9's typed-client section | shows the old client assertion |
| `### Contract tests` sample (~1202) | **already stale before this change** — shows `TestData.Get` and `Schemas.Order`, both long removed. Fix both problems |
| `### Declared-error contract tests` sample (~1318) | old shape |
| `### Auth contract tests` sample (~1402) | old shape |
| prose at ~1337 | calls `ShouldMatchContractAsync`/`ShouldMatchStatusAsync` "the form you will actually see generated" — no longer true |

`CLAUDE.md` requires the spec to change in the same commit as the behaviour, so this is not optional
cleanup.

**The target shape for every sample.** A raw contract case:

```csharp
RequireFixture("get_api_orders_id");

await ExpectContract(200, "Order", HttpMethod.Get,
    InTestUrl.Build("/api/orders/{id}", FixtureParameter("get_api_orders_id", "id")));
```

A raw declared-error case (note the unmatchable value — `[role-stays-in-the-argument]`):

```csharp
await ExpectContract(404, "ProblemDetails", HttpMethod.Get,
    InTestUrl.Build("/api/orders/{id}", Guid.NewGuid().ToString()));
```

A client-routed case, keeping its stopwatch and pinned `try`:

```csharp
var stopwatch = Stopwatch.StartNew();
try
{
    await ApiClient<OrdersClient>().GetOrderByIdAsync(id);
}
catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null) { throw; }
catch (Exception ex) when (ex is not OperationCanceledException) { WarnSwallowedClientException(ex); }
stopwatch.Stop();

await ExpectCapturedContract(200, "Order", stopwatch.Elapsed);
```

The `### Contract tests` sample at ~1202 additionally still shows `TestData.Get` and `Schemas.Order`,
neither of which exists any more — replace the whole sample with the first shape above rather than
patching one line of it.

- [ ] **Step 2: Update `docs/getting-started.md`**

Line 290 currently reads *"every generated case keeps building its own `HttpRequestMessage`,
byte-for-byte identical to a project with no `client` section at all."* The first clause is now
false; the byte-for-byte claim is still true and is still the point. Replace:

```markdown
every generated case keeps issuing its own direct HTTP call, byte-for-byte identical to a
project with no `client` section at all.
```

Line 371 reads *"those always build `HttpRequestMessage` directly regardless of this section."*
Replace:

```markdown
those always issue a direct HTTP call regardless of this section.
```

- [ ] **Step 3: Add the release-gate line to `CONTRIBUTING.md`**

In the "Publishing checklist" section, add:

```markdown
- [ ] **Regenerate `examples/Catalog.ApiTests` and `examples/Orders.ApiTests`, and move each
      `PackageReference` from `InTest.Runtime` to `InTest.Runtime.MSTest`.** Both are required and
      both are easy to forget, because **no test enforces either**.
      `ExampleProjectVersionMarkerTests` compares the three version markers *to each other*, never
      against `CliVersion.Current`, and its package-reference regex deliberately matches either id
      — so stale examples stay green indefinitely. Nothing under `.github/` or `scripts/` builds
      them and neither is in `InTest.sln`.
      This is a human step by necessity, not by omission: the trigger is "at the next publish",
      and a test encoding it would go red on `main` the moment the CLI version moves ahead and
      stay red for the whole development cycle — which is pressure to migrate `examples/`
      preemptively, exactly what `ExampleProjectVersionMarkerTests`' own comment forbids.
```

**Do not add a failing test instead.** That trade was evaluated and rejected; the reasoning is in the design's §8.

- [ ] **Step 4: Note the staleness in both example csproj files**

Beside each existing preview-pin comment, add:

```xml
<!--
  This project's committed Generated/ output predates the unified call surface and is
  deliberately NOT regenerated here: it is the only artifact in the repo proving the
  *published* packages restore and build for a real adopter, so it stays pinned to what is
  actually on nuget.org. Regenerate it when this PackageReference id moves to
  InTest.Runtime.MSTest — see CONTRIBUTING.md's publishing checklist.
-->
```

- [ ] **Step 5: Extend the marker test's comment**

In `tests/InTest.Architecture.Tests/ExampleProjectVersionMarkerTests.cs`, the comment above `RuntimePackageReferencePattern` already explains the either-id match and forbids preemptive migration. Add one sentence pointing at the checklist:

```csharp
// The regeneration that must accompany that id edit is a release-checklist step, not a test —
// see CONTRIBUTING.md's publishing checklist for why it cannot be enforced here.
```

- [ ] **Step 6: Verify nothing regressed**

```bash
dotnet test InTest.sln
```

Expected: **PASS**, all four suites. Pass a timeout well past CLAUDE.md's Golden figure — this runs the slow suite too.

- [ ] **Step 7: Commit**

```bash
git add docs/ CONTRIBUTING.md examples/ tests/InTest.Architecture.Tests/ExampleProjectVersionMarkerTests.cs
git commit -m "docs: update the spec, getting-started, and the release gate

Five sample sites in the 2026-08-16 spec showed the old generated shape; the Contract tests
sample was already stale before this change (TestData.Get, Schemas.Order — both long
removed) and is fixed for both reasons.

examples/ stay un-regenerated deliberately: they are the only artifact proving the
published packages restore and build for a real adopter. Nothing forces regeneration —
ExampleProjectVersionMarkerTests compares markers to each other, never to
CliVersion.Current — so the gate is a publishing-checklist line, explicitly labelled as
release discipline rather than CI. A test cannot express \"at the next publish\" without
going permanently red on main."
```

---

## Verification

`generate --check` proves the output is **deterministic**, not that behaviour is **preserved** — once the golden file is regenerated it compares new bytes against new bytes. The load-bearing proofs are:

1. **`CompileVerificationTests`** — generated code actually compiles. 7 cases.
2. **`GeneratedSuiteExecutionTests`** — generated code actually runs against a stub. 23 cases.
3. **The live Orders acceptance run** — hand-run, needs the sample API plus a Duende identity server with a correctly-scoped identity pair. CI does not do this.

**The acceptance target is 24 total: 20 passed, 0 failed, 4 skipped**, recorded in `README.md:19` and `docs/v0-acceptance.md`. **Assert per-assembly counts and skip *reasons*, not the total** — a skip that changes reason still counts four, so the total alone cannot catch a regression that converts one skip into a different skip.

Sample APIs need specific environment variables (ports, issuer/authority pairing, `ASPNETCORE_ENVIRONMENT=Development`). See `samples/README.md`; getting them wrong produces 500s or silent 404s rather than an obvious failure.

---

## What this plan does not cover

Stated so the next reader does not mistake these for oversights:

- **`ExpectCapturedContract` has no direct unit test** (Task 4). It needs a real `SchemaBundle` from a `spec-schemas.json` on disk. Covered by `GeneratedSuiteExecutionTests` instead.
- **§7's cancellation-on-match and UTF-8-decode deltas were never measured**, only reasoned. The design's own harness did not implement P2, so Task 1 is the first time that path runs. Task 1's two characterization tests are the guard.
- **The `additionalOperations` verb gap** (a spec with a non-standard verb makes `ToPascalMethod` emit `HttpMethod.Purge`, which will not compile) is pre-existing and untouched. `ExpectStatus` taking an `HttpMethod` inherits it as-is. Whether Microsoft.OpenApi actually surfaces such a verb is **not established** — do not report it as a confirmed defect without running it.
- **`examples/Generated/**` is not regenerated.** Task 9 Step 3 is the gate.
