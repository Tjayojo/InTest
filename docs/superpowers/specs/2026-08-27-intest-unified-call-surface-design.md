# Unified call surface for generated tests

**Status:** Design · Revision 5
**Date:** 2026-08-27
**Scope:** Piece 1 of two. Piece 2 — making the typed-client path the default — is explicitly out
of scope and gets its own cycle.

**Revision note — rev 5.** Fourth review. Core sound again; every named decision held. Three
blockers, and one retracts a claim this document made twice. **`[prefer-the-platform]` said no
HTTP-library alternative had ever been recorded. That is false** — the 2026-08-16 spec names Flurl
ten times, shipped it as the *primary* HTTP pack in its own rev 2, and deferred it to a v2 backlog
with reasoning, including the non-2xx behaviour this document presented as a fresh finding. Also:
§8 prescribed regenerating `examples/`, which cannot compile; and the body argument's prohibition
had no mechanism and misattributed today's guard.

**Revision note — rev 4.** Reviewed again. The core was judged sound and every load-bearing claim
in §2, §3, §4 and §7 was independently re-confirmed in source. Four fixes: §8's vacuity list was
itself incomplete — the same defect it exists to prevent; "§5's compatibility table" was the wrong
cross-reference, cited twice, and ambiguous across two documents; **P1's push seam is replaced by a
pull seam**, which is both more correct and deletes P1's entire compatibility argument; and the
optional trailing body argument weakened "fail loudly".

**Revision note — rev 3.** Reviewed. Three findings would have produced wrong work, six were
smaller. Rev 2's core survived — its three rev-1 corrections were independently confirmed in
source — but it restated a Golden timing figure this repo has already corrected twice, it proposed
a change to `ApiTestCore.BeginTest` that the method's own compatibility overload argues against at
IL level, and it would have left three assertions passing vacuously.

## 1. Why

The generated test is the product. An adopting team commits it, reads it when it fails, and
extends it with hand-written partials. Design principle #6 in
`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` states it: *"Generated code
is idiomatic and direct. No facades that obscure failure messages."*

**That principle is the one most in tension with this change, so confront it rather than cite it.**
Consolidating a send into the runtime does put a facade in front of it. The defence is narrow and
should be stated as such: `ApiResponseAssertions` already constructs the failure message today and
still carries method, URI, status, run id and body, so *assertion* failures read identically. The
genuine loss is that a **transport-level** throw — connection refused, DNS failure, TLS error —
now stack-traces into `InTest.Runtime` rather than at the generated line that issued it. That is a
real cost of this change, accepted, not argued away.

Today each generated case costs about ten lines, of which roughly seven are identical everywhere:

```csharp
using var request = new HttpRequestMessage(
    HttpMethod.Delete,
    InTestUrl.Build("/api/orders/{id}", FixtureParameter("delete_api_orders_id", "id")));

var stopwatch = Stopwatch.StartNew();
using var response = await Client.SendAsync(request, TestContext.CancellationToken);
stopwatch.Stop();

await ApiResponseAssertions.ShouldMatchStatusAsync(
    response, 204, TestId, stopwatch.Elapsed, TestContext.CancellationToken);
```

The stopwatch, the cancellation token and `TestId` are threaded by hand into every case. And
`ApiResponseAssertions` carries **four** entry points — `ShouldMatchStatusAsync`,
`ShouldMatchContractAsync`, and a `...Captured...` pair that exists only because PR #6's
typed-client path captures responses differently.

**Scope, stated plainly because rev 2 left it ambiguous.** Piece 1 consolidates the **raw branch
only**. The client branch keeps its stopwatch, its pinned `try` / exception-filter / `catch`, and its
explicit `ShouldMatchCaptured*` call — moving that filter into the runtime is exactly what
`[one-terminal-call]` argues against. So piece 1 *widens* the gap between the two branches in the
short term: a raw case becomes one call while a client case stays around ten lines. That is a
deliberate trade, not an oversight. What converges is the assertion layer beneath both; the emitted
shapes converge in piece 2 or not at all.

**A cheaper middle option exists and this document should not ship without ruling on it.** The only
alternative considered so far is moving the client branch's pinned `try`/filter/`catch` into the
runtime, which `[one-terminal-call]` rightly rejects. There is a third: collapse only the client
branch's *assertion* ceremony — something like `await ExpectCaptured(200, "Order",
stopwatch.Elapsed)`, folding `LastCapturedResponse`, `Schemas`, `TestId` and the token — while
leaving the pinned `try`/`catch` and the stopwatch exactly where they are. That **narrows** the gap
instead of widening it, touches nothing `[one-terminal-call]` argues against, and dissolves the
two-diffs-for-adopters cost this document closes on.

For a product whose thesis is "the generated test is the product", shipping a release where the
newest feature yields the ugliest file is a poor trade when a cheaper one is available. **Adopt it
or reject it on the record — silence is the one option not available.**

## 2. What revision 1 got wrong

Recorded rather than quietly replaced, because the reasoning that produced an error is the
reasoning most likely to recur. A review found three blockers; all were confirmed in source.

1. **The worked example erased the distinction that makes its own sibling cases correct.**
   Revision 1 proposed `Operation(key).Delete(path)` resolving `{id}` "from the fixture because the
   call already knows the key". But path parameters resolve from the fixture **only for
   `CaseRole.Success`** — `TemplateRenderer.cs:145` gates `emits_fixture_lookup = c.Role ==
   CaseRole.Success`, and `QueryExpression` returns early for every other role. In
   `examples/Orders.ApiTests/Generated/OrdersTests.g.cs`, four cases share one operation key and
   path: `_Contract` (line 25) resolves from the fixture, while `_Forbidden`, `_NotFound` and
   `_Unauthorized` (lines 45, 61, 80) deliberately send `Guid.NewGuid().ToString()`. Revision 1
   would have pointed the 404 case at real seeded data and asserted 404 against a live 200 —
   **breaking correct tests**, while promising identical outcomes. Revision 1 quoted from that file
   and did not read the three methods beneath the one it copied.
2. **There is no runtime typed-client registry, and there cannot be one.**
   `src/InTest.Runtime/InTestClients.cs` is fifteen lines holding two string constants. Client
   routing is a *generation-time* decision that emits strongly-typed C# derived from Kiota/NSwag
   conventions measured against real generator output. A runtime call object cannot reconstruct it.
3. **The chain dropped four things the current shape carries** — the request body, query
   parameters, the schema key, and the cancellation token.

## 3. Named decisions

### `[one-terminal-call]` — consolidate the ceremony, do not build a fluent chain

The generated case becomes a single call:

```csharp
RequireFixture("delete_api_orders_id");

await ExpectStatus(204, HttpMethod.Delete,
    InTestUrl.Build("/api/orders/{id}", FixtureParameter("delete_api_orders_id", "id")));
```

Two methods, with the request body as an optional trailing argument so `POST`/`PUT`/`PATCH` need
no separate overload. The contract form keeps the schema key **explicit**, because it cannot be
derived: `TestPlanBuilder.ResolveSchemaKey` returns either a component name (`"ProblemDetails"`) or
a synthesized `op:{key}:{status}:application/json`, and the first is not recoverable from key and
status alone.

```csharp
await ExpectContract(404, "ProblemDetails", HttpMethod.Delete,
    InTestUrl.Build("/api/orders/{id}", Guid.NewGuid().ToString()));
```

What moves inside is exactly the plumbing — `Schemas`, `TestId`, the stopwatch and the cancellation
token. Seven arguments become four.

**The `url` parameter is a `string`, not a `Uri`.** `QueryExpression` emits
` + InTestUrl.BuildQuery(...)` as a concatenation *outside* the `InTestUrl.Build(...)` call, so a
`Uri` parameter would silently break every query-carrying success case. All three worked examples
here happen to be query-free, which is exactly how that would go unnoticed.

**Two qualifications the claim "nothing describing what the test does is hidden" needs.**

`Encoding.UTF8` and `"application/json"` move out of the generated file into the runtime. They are
hardcoded in the template today, so this is a lateral move rather than a new assumption — but they
are facts about the request leaving the code the adopter reads, and the claim should not paper over
that.

**The body needs a mechanism, not a prohibition — and rev 4 got today's guard wrong.**

`FixtureBody(key)!` does **not** throw. `!` is the null-forgiving operator, a compile-time no-op
that emits nothing. What throws today is `StringContent`'s own `ArgumentNullException`. An
implementer who believes the `!` is the guard will pass `FixtureBody(key)!` into an optional
parameter and get precisely the silent degradation this paragraph forbids.

**And the stated ergonomic conflicts with the stated requirement.** "No separate overload" and "a
body-bearing case must fail loudly" cannot both hold with `string? body = null`. Resolve it in
favour of loudness: **a separate required-body overload**, `ExpectStatus(int, HttpMethod, string,
string body)` with `ArgumentNullException.ThrowIfNull(body)`. The "no separate overload" convenience
is withdrawn — it was an ergonomic preference, and it loses to `CLAUDE.md`'s named anti-pattern.

The implementer must show the emitted `POST`/`PUT`/`PATCH` case. It is the only shape carrying this
risk and no worked example of it appears anywhere in this document.

Rejected: a fluent chain (`Operation(key).Delete(path).ShouldReturn(204)`). Three reasons, any one
sufficient.

**A lazy chain that omits its terminal compiles, sends nothing, and passes green.** That is
verbatim the shape `CLAUDE.md` forbids — *"never substitute plausible defaults that let a suite pass
while asserting nothing"*. Today's two-statement form at least issues the request whether or not the
assertion follows.

**It would freeze a verb surface.** `TestPlanBuilder` iterates `pathItem.Operations` with no verb
filter and `ToPascalMethod` handles any verb generically. A fixed `Get/Post/Put/Patch/Delete` chain
breaks a spec declaring `head:`, `options:` or `trace:` — and once shipped in a public package the
verb set is semver-frozen.

Rev 2 offered a third reason — "it puts the stack trace in the runtime" — which is **withdrawn
because it does not discriminate**. Generated code already calls into `ApiResponseAssertions`, and
`ExpectStatus` moves the send and the throw there too. Under an explicit "any one sufficient", a
non-discriminating third reason weakens the record rather than strengthening it.

### `[role-stays-in-the-argument]` — the generator keeps deciding what a parameter resolves to

The role distinction lives where it already lives: in the argument expression the generator
computes. Success cases emit `FixtureParameter(key, name)`; other roles emit an unmatchable value —
and *which* unmatchable value is itself per-kind, since `UnmatchableValueFor` picks `"2147483647"`
for an integer-kinded parameter rather than a GUID, because an ASP.NET Core binder answers 400
before the action's 404 path runs.

**The operation key therefore appears twice in a Success case, and that is correct.** Revision 1
treated the repetition as the defect to fix. The two appearances mean different things — one is the
skip gate, one is the value lookup — and collapsing them is exactly what broke the role
distinction. Removing that duplication is no longer a goal.

### `[captured-is-the-single-shape]` — one result type, two implementations behind four methods

`CapturedResponse` — `record struct (int Status, string Body, string? RequestMethod, string?
RequestUri)` — already exists and the client path already produces it. The raw path will produce it
too, so `ShouldMatchCapturedStatusAsync` and `ShouldMatchCapturedContractAsync` become the single
implementations.

**The raw pair are kept as thin adapters, not deleted.** `ApiResponseAssertions` is public surface
in a package published as `0.1.0-preview.1`, and the **§3 compatibility table** in
`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` (under "Versioning and
compatibility") reserves public-surface changes for a major bump. Rev 3 cited "§5" twice; §5 of that
document is "Configuration and command surface", and *this* document's own §5 is "Unchanged" — so
the reference was both wrong and ambiguous across two files. Pre-1.0 the exception is available, but taking it as a side effect of an
internal consolidation is not the same as taking it deliberately. Adapters give the same
consolidation at no compatibility cost.

**After `[one-terminal-call]` the raw pair have zero in-repo callers**, so
`ApiResponseAssertionsTests` becomes the only thing keeping them honest. They keep direct test
coverage; adapters with no caller and no test are dead code wearing a compatibility label.

**The compatibility argument cuts both ways, and rev 2 only applied one edge.** Declining to delete
two public statics is careful. But adding `ExpectStatus`/`ExpectContract` to the runtime and having
the CLI emit calls to them immediately breaks the *first* row of that §3 table — "`InTest.Runtime`
**N.x** accepts code generated by `InTest.Cli` **N.y** for any `y`" — because regenerating with a
newer CLI against a pinned older runtime becomes a compile error. The mitigations exist
(`InitCommand` pins `InTest.Runtime.MSTest` to `CliVersion.Current`; `upgrade` reports drift) and
the typed-client work set the precedent with `ApiClient<T>()`. It is accepted deliberately here
rather than left silent, because being scrupulous about a removal nobody hits while saying nothing
about an addition every regenerating adopter hits reads as an oversight.

**This must not route raw traffic through `ResponseCaptureHandler`.** `[capture-is-opt-in]` exists
because the handler's `response.Content` replacement carries a documented unverified risk for
downstream deserializers, and `GenerateCommand` enables it only when at least one case resolved a
client call. Making it unconditional would impose that risk on every suite, including today's
`examples/`, for nothing. The conversion is extracted; the handler is not made universal.

### `[dispatch-stays-generation-time]` — the runtime does not choose

Client routing remains a CLI decision. §9's rule that **only `Success` cases are ever client-routed**
is untouched — declared-error and auth cases continue to use raw `HttpRequestMessage`
unconditionally. Revision 1's proposal to unify dispatch at runtime would have overturned a shipped
spec decision without arguing for it.

If a single surface must one day span both idioms, the seam is a delegate the *generator* supplies,
not a registry the runtime consults.

### `[prefer-the-platform]` — no HTTP-client library

**Retraction first.** Revisions 2–4 of this document said the reasoning "was never recorded
before" and called the BCL choice "an unexamined default". **That is wrong.** The 2026-08-16 design
spec names Flurl ten times: its own rev 2 shipped Flurl as the **primary** HTTP pack with
`HttpClient` second (line 167), rev 3 cut to one pack and moved Flurl to the **v2 backlog** (lines
180, 2603, 2634), and line 949 records that a construct existed "only because Flurl throws by
default" — the very argument this section presented as a fresh finding. `IFlurlClient` is named at
§3 line 172 and §5 line 475 as a designed-for v2 axis.

So this is **not a new decision. It is v1 confirming a decision already made**, and the reasoning
below is corroboration, not novelty:

- **Flurl throws on non-2xx by default, and asserting on non-2xx is the job.** Three of the four
  cases generated per operation are error cases. Every call would need `AllowAnyHttpStatus()`,
  fighting the library's central ergonomic. (Already recorded at spec line 949.)
- **`AppendPathSegment` does not model an OpenAPI path.** The spec supplies the template
  `/api/orders/{id}`, and that template is also the key the plan and `coverage-report.json` are
  built on. Flurl composes URLs from segments you already have; here the requirement is
  substitution *into* a template, which you would write yourself and then hand to Flurl.
- **`WithOAuthBearer` would be a regression.** Auth is applied by `AuthHandler`, a
  `DelegatingHandler` on the `HttpClient`, driven by `ITestTokenProvider` and identity slots. That
  covers every request without per-call code — including the typed-client path, where InTest does
  not build the request at all.
- **It would ship to adopters.** `InTest.Runtime` is published, so any dependency it takes lands
  transitively in every adopter's test project.

**Open question this raises, which this document must not answer silently.** §5's "Frozen vs.
additive axes" still lists an HTTP pack as a v2 axis the architecture is designed to admit. Nothing
here closes that axis — `ExpectStatus`/`ExpectContract` are new *surface*, not a new *constraint*,
and a future pack would reimplement them. But if the intent is that v1's consolidation forecloses a
second pack, **§5 becomes dead text and must be retired in the same change.** Decide it; do not
leave two documents disagreeing.

- **Flurl throws on non-2xx by default, and asserting on non-2xx is the job.** Three of the four
  cases generated per operation are error cases. Every call would need `AllowAnyHttpStatus()`,
  fighting the library's central ergonomic.
- **`AppendPathSegment` does not model an OpenAPI path.** The spec supplies the template
  `/api/orders/{id}`, and that template is also the key the plan and `coverage-report.json` are
  built on. Flurl composes URLs from segments you already have; here the requirement is
  substitution *into* a template, which you would write yourself and then hand to Flurl.
- **`WithOAuthBearer` would be a regression.** Auth is applied by `AuthHandler`, a
  `DelegatingHandler` on the `HttpClient`, driven by `ITestTokenProvider` and identity slots. That
  covers every request without per-call code — including the typed-client path, where InTest does
  not build the request at all. Per-call bearer application would break the case PR #6 enabled.
- **It would ship to adopters.** `InTest.Runtime` is published, so any dependency it takes lands
  transitively in every adopter's test project. `CONTRIBUTING.md`'s dependency policy is
  deliberately hard about that, and the generated project is explicitly the team's to own.

**The principle generalises: prefer what the platform already provides.** `HttpClient`,
`DelegatingHandler`, `Uri.EscapeDataString` and `HttpMethod` are the platform's, and InTest uses
them directly. `InTestUrl.Build` survives this test only because the BCL has no URI-template
substituter — the nearest option, `Microsoft.AspNetCore.Routing`'s template binder, would pull ASP.NET
Core routing into a test project to save a hundred lines that already exist and are tested. It is a
thin wrapper over `Uri.EscapeDataString`, not a layer of its own.

The assertion machinery is the genuinely custom part, and it stays custom because JSON Schema
validation has no BCL equivalent — NJsonSchema is already a pinned dependency and already does it.

## 4. Prerequisites — each shippable alone

Both are correct independently and each makes the main change smaller.

**P1 — the `CancellationToken` seam. A pull seam, not a push seam.**

Moving the send into `ApiTestCore` needs a token there, and `ApiTestCore` cannot obtain one from
`TestContext`. The mechanism is the **compiler**, not a test: `InTest.Runtime` has no MSTest
`PackageReference`, so there is no implicit global using and the type is simply unavailable.
(`NeutralityTests`' source scan matches the *namespace string* — `ApiTestCore.cs` already names
`TestContext` in a dozen doc comments and passes. Its own class doc calls the compiler "Layer 1,
primary". Rev 2 cited the wrong evidence for a right conclusion, which in this codebase is itself a
defect.)

```csharp
// InTest.Runtime — neutral
protected virtual CancellationToken TestCancellationToken => CancellationToken.None;

// InTest.Runtime.MSTest — the adapter
protected override CancellationToken TestCancellationToken => TestContext.CancellationToken;
```

**Rev 3 proposed pushing the token through a third `BeginTest` overload. That is withdrawn**, and
the reason matters more than the mechanism.

The four seams already extracted — `IRunDiagnostics`, the profile string, the display name, the skip
reason — are **facts fixed for the duration of a test**, so pushing them in once at the start is
right. A cancellation token is a **live signal**. Pushing it snapshots at `[TestInitialize]` what
the template reads fresh at every send today, and if MSTest replaces `TestContext`'s
`CancellationTokenSource` afterwards — as it is reported to for `[Timeout]` — the stashed token goes
stale and cancellation never reaches the request. That is a behaviour change hiding inside a change
§5 promises is behaviour-identical.

*(The `[Timeout]` replacement is the reviewer's reasoning about MSTest internals and is **not
verified here**. The pull seam is preferred regardless: it is purely additive, preserves today's
read-at-call-time semantics exactly, and needs no compatibility argument at all.)*

**The simplest option was never weighed, and it deletes P1 entirely.** The token could just stay an
argument: `await ExpectStatus(204, HttpMethod.Delete, url, TestContext.CancellationToken)`. That
preserves read-at-call-time — the pull seam's own justification — adds no virtual member, needs no
compatibility argument, and keeps cancellation visible in the code the adopter reads. Cost: one
argument per call. **Under that option there is no P1**, so a prerequisite billed as "correct
independently, shippable alone" rests on a choice this document never made explicitly. Decide it.

Related and also unstated: the client branch still emits `cancellationToken:
TestContext.CancellationToken` inside the client call expression. After a pull seam a mixed
generated file spells the same token two ways, and the client branch stays un-portable to a
non-MSTest adapter.

Rev 3's whole overload discussion — `ApiTestCore.cs`'s existing `BeginTest` compatibility overload,
its IL-level reasoning, whether to take the pre-1.0 exception — **evaporates**, because a new
`virtual` member changes no existing signature. That the simpler option also removes a page of
compatibility argument is the tell that rev 3 asked "how do we push this compatibly" without first
asking whether to push it at all.

Rev 2 also claimed "without it a cancelled run keeps issuing HTTP". **That is false today** — the
template passes `TestContext.CancellationToken` straight into `Client.SendAsync` in the generated
file. It becomes true only *after* the send moves. Stated wrongly it invites a regression test for a
bug that does not exist.

**P2 — `ShouldMatchCaptured*` as the single implementation.** Extract the `HttpResponseMessage` →
`CapturedResponse` conversion, make the raw pair thin adapters over the captured pair. No public API
break, no `[capture-is-opt-in]` conflict, and the string-decode question below becomes a local
decision inside one adapter rather than a design-level risk.

## 5. Unchanged

Fixture files and their format, `intest.json`, exit codes, the drift gate, `--check`'s byte
comparison, all three skip gates, and §9's client-routing rule. **Test count, outcomes and skip
reasons must be identical before and after.**

## 6. Verification

`generate --check` proves the output is *deterministic*, not that behaviour is *preserved* — once
`examples/` and the golden file are regenerated it compares new bytes against new bytes. The
load-bearing proof is `CompileVerificationTests` and `GeneratedSuiteExecutionTests`, which really do
`dotnet build` and `dotnet test` against a stub, plus the live Orders acceptance run.

That run's target is **24 total: 20 passed, 0 failed, 4 skipped**, recorded in `README.md:19` and
`docs/v0-acceptance.md`. It is a hand-run step needing the sample API and a Duende identity server
with a correctly-scoped identity pair — CI does not do it. **Assert per-assembly counts and skip
*reasons***, not the total: a skip that changes reason still counts four.

**Do not restate a Golden timing figure here.** Rev 2 wrote "~90s warm locally", which is the
number `CLAUDE.md` explicitly names as superseded — twice. Read the figure from `CLAUDE.md`'s
Commands section at the time you run it, and pass a timeout well past whatever it says. A fourth
copy in a fourth document is how the third one went stale, and this is the exact sentence an
implementer reads before choosing a timeout — a healthy run cut off at a tool's ~2-minute default
reads as a hang.

## 7. Risks

**A string decode on passing status-only cases.** Not a loss of streaming — the template already
calls the two-argument `Client.SendAsync(request, ct)`, which is `ResponseContentRead` and buffers
the body regardless. The real delta is narrower: `ShouldMatchStatusAsync` currently skips the UTF-8
decode when the status matches, reading the body only on mismatch. Producing a `CapturedResponse`
decodes unconditionally. `ShouldMatchContractAsync` already decodes every time, so only the
status-only path changes.

**The timing window shifts.** The stopwatch currently brackets `SendAsync` alone; inside the
consolidated call it would also cover body materialization. Reported milliseconds change in every
failure message. Cosmetic, but stated because §5 promises identical outcomes.

**Cancellation behaviour changes, and "identical outcomes" does not cover it.**
`ShouldMatchStatusAsync` returns today without touching the token when the status matches; both
`ShouldMatchCaptured*` call `ThrowIfCancellationRequested` first. After P2 a cancelled-but-matching
case throws instead of passing. That is probably the better behaviour — but §5 promises identical
outcomes, so it is an exception stated here rather than a `GeneratedSuiteExecutionTests` surprise.

**A pre-existing gap this design inherits unchanged.** `TestPlanBuilder` keys operations by the BCL
`HttpMethod` rather than a closed enum, so a spec carrying a non-standard verb (OpenAPI 3.2's
`additionalOperations`) makes `ToPascalMethod` emit `HttpMethod.Purge` and the generated file will
not compile. True today; `ExpectStatus` taking an `HttpMethod` inherits it as-is. Named so it is a
known gap rather than a latent one.

**Docs that must move in the same change — the earlier list was short.** In the 2026-08-16 spec:
§9's typed-client section, the `### Contract tests` sample (~1202, *already* stale — it shows
`TestData.Get` and `Schemas.Order`, both long removed), the `### Declared-error contract tests`
sample (~1318), the `### Auth contract tests` sample (~1402), and the prose at ~1337 calling
`ShouldMatchContractAsync`/`ShouldMatchStatusAsync` "the form you will actually see generated". In
`docs/getting-started.md`: line 290 ("every generated case keeps building its own
`HttpRequestMessage`, byte-for-byte identical to a build without the section") and line 371. `CLAUDE.md` requires the spec to change alongside the
behaviour.

## 8. Files touched

- `src/InTest.Runtime/ApiTestCore.cs` — the `CancellationToken` seam (P1) and the consolidated call.
- `src/InTest.Runtime/ApiResponseAssertions.cs` — captured pair as single implementations, raw pair
  as adapters (P2).
- `src/InTest.Runtime.MSTest/ApiTestBase.cs` — overrides `TestCancellationToken` (P1). No
  signature changes.
- `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` and `TemplateRenderer.cs` — emit the
  consolidated call for the **raw branch only**; role gating unchanged.
- **`tests/InTest.Cli.Tests/TemplateRendererTests.cs`** — ordering assertions anchored on
  `IndexOf("new HttpRequestMessage(")` and assertions naming `ShouldMatchStatusAsync` /
  `ShouldMatchContractAsync`. These break loudly, which is fine.
- **`tests/InTest.Cli.Tests/TemplateRendererClientTests.cs` — the one that fails *quietly*.**
  Lines **349**, 357, 358 and 403 — rev 3's list of three was itself incomplete, which is the very
  defect this entry exists to prevent. 357/358/403 assert `ShouldNotContain("new
  HttpRequestMessage(")` and `ShouldNotContain("Client.SendAsync(")`; **349** asserts
  `ShouldNotContain("ShouldMatchContractAsync(
            response,")` while its paired
  positive at 348 asserts on the *client* branch, which this change does not touch — so that pair
  cannot fail as a unit either. Once the raw branch stops emitting those strings, all four keep
  passing while discriminating nothing — the raw-versus-client separation they exist to
  prove silently evaporates, and their paired positive control at line 426 is the half that fails,
  so the pair does not fail as a unit. **Replace the discriminator in the same change** — assert on
  `ExpectStatus(` versus `ApiClient<` — so the vacuity is closed rather than discovered later, if
  at all. This is the "a suite silently matching nothing cannot read as green" rule that
  `assert-trx-results.ps1` exists for.
- **`tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`** — directly in P2's blast radius.
- **`tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs` — absent from rev 3's list
  entirely**, and it is the suite `CLAUDE.md` calls the only one proving generated code both
  compiles *and* runs. Line 1070 asserts `ShouldNotContain("new HttpRequestMessage(")` and goes
  vacuous exactly as above, with its positives at 1062/1065/1066 still passing. The comment at line
  1812 describing "a bare `HttpRequestMessage`/`Client.SendAsync` pair" also goes stale.
- **A home for direct tests of `ExpectStatus`/`ExpectContract`.** Rev 3 named none, leaving
  disposal, body pass-through and token honouring covered only end-to-end through Golden's happy
  path. `ApiTestCoreCaptureTests.cs` and `ApiTestBaseTests.cs` already establish the
  test-only-subclass pattern via `InternalsVisibleTo` — but calling them "the obvious place"
  understates the work. Both deliberately avoid `InTestRun.InitializeAsync`; the former sets
  `ApiTestCore._scope` by reflection precisely because `BeginTest` needs a live `InTestRun.Root`.
  `ExpectStatus` needs `Client` (`{ get; private set; }`, set only inside `BeginTest`);
  `ExpectContract` needs `Schemas` → `InTestRun.Schemas`, a static loaded from a real
  `spec-schemas.json` on disk. Budget two more reflection hatches, a stub `HttpMessageHandler` and a
  hand-built `SchemaBundle` — an implementer who hits this mid-change will fall back to Golden-only
  coverage, which is the one thing this entry exists to prevent.
- **`tests/InTest.Cli.Tests/TemplateEscapingGuardTests.cs`** — it parses the template and classifies
  each `tc.<name>` by quote parity, mechanically enforcing one of the three text-safety rules
  `CLAUDE.md` calls non-negotiable. The new shape keeps `expected_status`/`http_method_pascal` bare
  and the `*_literal` fields quoted, so it *should* pass unchanged. State that as a checked
  conclusion rather than leaving it unmentioned.
- `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` — regenerated golden file.
- **`examples/Catalog.ApiTests/`, `examples/Orders.ApiTests/` — DO NOT regenerate blindly.**
  Both pin `<PackageReference Include="InTest.Runtime" Version="0.1.0-preview.1" />` — the
  *published* package, not a `ProjectReference`. Neither is in `InTest.sln`; nothing in
  `.github/workflows/` or `scripts/ci/` builds them; `ExampleProjectVersionMarkerTests` only checks
  that the three version markers agree. So regenerating emits `ExpectStatus(`/`ExpectContract(`,
  members that **do not exist in `0.1.0-preview.1`**, and **no test in this repository would
  notice**. This is the exact first-row compat break `[captured-is-the-single-shape]` names in the
  abstract, prescribed as an action three sections later. Both Golden suites are safe — they
  substitute a `ProjectReference`; `examples/` is the sole exposure.
  **Decide explicitly, do not default:** leave them un-regenerated until the next publish (they then
  show the old shape until `0.1.0-preview.2`), or move them to a `ProjectReference` (which breaks the
  "an adopter restores from nuget.org" premise their own csproj comment states), or accept two broken
  examples and say so. Silence here ships a green run and two dead example projects.
- The template header's `using System.Diagnostics;` and `using System.Text;` — after the change a
  class with no client-routed case uses neither. Not a build error (the scaffold sets `Nullable` but
  not `TreatWarningsAsErrors`, and unused usings are IDE-only), but the header is a file this change
  touches.
- The design spec §9 and `docs/getting-started.md`.

**A cost worth naming:** because piece 1 consolidates only the raw branch, the golden file and both
`examples/` projects take a shape churn now and another in piece 2 — two reviewed diffs for
adopters instead of one.
