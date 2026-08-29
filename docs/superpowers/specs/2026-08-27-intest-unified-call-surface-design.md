# Unified call surface for generated tests

**Status:** Design · Revision 7
**Date:** 2026-08-27
**Scope:** Piece 1 of two. Piece 2 — making the typed-client path the default — is explicitly out
of scope and gets its own cycle.

**Revision note — rev 7.** Fifth review, and the first where the reviewer **implemented** the design
rather than reading it — template plus a real `ApiTestCore`/`ApiTestBase`, so the Golden suite could
build and run.

**The headline is a validation, and it belongs above the defect list: the design as specified compiles
and runs.** `dotnet build InTest.sln` — 0 warnings, 0 errors under `TreatWarningsAsErrors=true`.
`CompileVerificationTests` 7/7. `GeneratedSuiteExecutionTests` 22/23, the single failure being one
assertion this document already predicted. The `protected override` seam across the package boundary
compiles clean.

Three blocking defects, two of them introduced by rev 6 itself:

1. **§8 was never updated when §1 adopted the middle option.** Four sites still scoped the change to
   the raw branch, and §8's vacuity analysis was calibrated to that abandoned scope — measurably
   false under the adopted one. An implementer following §8 literally would have shipped
   `ExpectCapturedStatus`/`ExpectCapturedContract` with no caller: this document's own definition of
   dead code, the argument it used one section earlier to fold P1 in.
2. **Rev 6's "a forcing mechanism already exists" for `examples/` was false.** It was reached by
   reading `ExampleProjectVersionMarkerTests`' comment and treating it as the test's behaviour. The
   assertions compare the three markers to each other and never to `CliVersion.Current`. Retracted;
   the honest mechanism is a release-checklist step, labelled as weaker than a test.
3. **"Four entry points, each with a required-body overload"** gave the captured pair a body
   parameter they can never use, and propagated "eight signatures" into §4's cost argument. Six.

**§8's test inventory is now measured and must not be re-derived by reading.** Every revision that
hand-derived it got it wrong — three consecutively. The measurement found a bucket no revision had
named: an assertion that stays **green for a reason unrelated to what it guards**
(`TemplateRendererClientTests.cs:401`, satisfied by the pinned catch filter after the change), which
is more dangerous than a vacuous one because nothing goes red to announce it. It also found
`TemplateRendererTests.cs:160`, which neither the author nor the reviewer had named, and **corrected
rev 6's cost estimate downward** for direct `Expect*` tests — one reflection hatch, not two.

**Revision note — rev 6.** Worked through with the reviewer rather than another review round.
Two rev-5 edits had not landed as meant: `[prefer-the-platform]`'s four bullets appeared **twice**
with the second copy carrying two sentences the first had dropped, and the body resolution
contradicted an unedited sentence 25 lines above it in the same section. Both fixed. Four open
questions are now decided: the pull seam ships but **folds into the main change** (alone it is a
virtual member with no caller — this document's own verdict on dead code); the client-branch middle
option is **adopted**, verified against the pinned shape; **the spec's frozen-axes list stays live**
and is *advanced* rather than foreclosed; and `examples/` stay un-regenerated **with a forcing
mechanism** — one the codebase already provides — because "leave them" otherwise becomes "forget
them".

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

**Scope, stated plainly because rev 2 left it ambiguous — and revised again below.** Piece 1 was
scoped to the **raw branch only**. The client branch keeps its stopwatch and its pinned `try` /
exception-filter / `catch` — moving that filter into the runtime is exactly what
`[one-terminal-call]` argues against. Under that scope piece 1 *widened* the gap between the two
branches in the short term: a raw case became one call while a client case stayed around ten lines.

**That trade is superseded.** The middle option adopted immediately below keeps the `try` and the
stopwatch exactly where they are but collapses the client branch's *assertion* into
`ExpectCapturedStatus`/`ExpectCapturedContract`, so the gap **narrows** instead. The paragraph above
is retained because the reasoning about the filter still holds and §7's risk analysis rests on it —
but wherever this document says "raw branch only", the adopted scope is raw **and** the client
assertion. What converges is the assertion layer beneath both; the emitted
shapes converge in piece 2 or not at all.

**A cheaper middle option exists, and it is adopted.** Rev 5 left this open; it is now decided,
and it changes what piece 1 delivers: the gap **narrows** rather than widening.

The only alternative previously considered was moving the client branch's pinned
`try`/filter/`catch` into the runtime, which `[one-terminal-call]` rightly rejects. The third option
is to collapse only the client branch's *assertion* ceremony — folding `LastCapturedResponse`,
`Schemas`, `TestId` and the token — while leaving the pinned `try`/`catch` **and the stopwatch**
exactly where they are:

```csharp
await ExpectCapturedContract(200, "Order", stopwatch.Elapsed);
```

**The pinned shape survives, verified rather than assumed.** The constraint (template comment, and
`TemplateRendererClientTests` ~330-341) is that the stopwatch must start *before* the `try`, because
the throwing path still needs a real elapsed. This honours it exactly: the stopwatch stays a visible
local in the generated method and `Elapsed` stays an explicit argument. The `try` / exception-filter
/ `catch` / `WarnSwallowedClientException` block is untouched.

**It is two client methods, not one.** The client branch has a status-only form for schema-less
cases (`TemplateRendererClientTests` ~395-405, `GeneratedSuiteExecutionTests:1066`), so the full
surface is four: `ExpectStatus` / `ExpectContract` / `ExpectCapturedStatus` /
`ExpectCapturedContract`.

**Those names are load-bearing, not cosmetic.** Every affected test is a *substring* assertion, and
`ExpectStatus(` is not a substring of `ExpectCapturedStatus(`, nor `ExpectContract(` of
`ExpectCapturedContract(` — so the new discriminators are safe in both directions. A name like
`ExpectCaptured` colliding as a substring would silently reintroduce exactly the vacuity §8 exists
to prevent.

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

**Four entry points.** Two serve the raw branch (`ExpectStatus`, `ExpectContract`) and two the
client branch (`ExpectCapturedStatus`, `ExpectCapturedContract`, adopted in §1). **The required-body
overload belongs to the raw pair only** — see the body resolution below for why the "optional
trailing argument" convenience was withdrawn, and note that the captured pair never build a request:
the adopter's typed client sends the body, so a body parameter on them would be public surface with
no possible caller. **Six signatures, not eight.** The contract forms keep the schema key **explicit**,
because it cannot be derived: `TestPlanBuilder.ResolveSchemaKey` returns either a component name
(`"ProblemDetails"`) or a synthesized `op:{key}:{status}:application/json`, and the first is not
recoverable from key and status alone.

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
favour of loudness: **a separate required-body overload on every form that can carry a body** —
`ExpectStatus` *and* `ExpectContract` both need one, since a `POST` returning 201 with a schema is
body-bearing and contract-checked at once. The "no separate overload" convenience is withdrawn; it
was an ergonomic preference and it loses to `CLAUDE.md`'s named anti-pattern.

```csharp
RequireFixture("post_api_orders");

await ExpectContract(201, "Order", HttpMethod.Post,
    InTestUrl.Build("/api/orders"),
    FixtureBody("post_api_orders")!);
```

**`ArgumentNullException.ThrowIfNull(body)` closes the null hole, not the fail-loudly hole.** It
only fires if the generated code called the body overload at all. A template regression that emits
the *no-body* form for a `has_body` case sends nothing, asserts a status, and passes green — the
guard never runs. **Close it where it actually lives — and that guard already exists.**
`TemplateRendererTests.RendersAStringContentBodyFromTheFixture` (line 195) already asserts that a
body-bearing plan renders `FixtureBody("createOrder")`, `new StringContent(` and `application/json`.
The implementer's job is to **update its expected strings to the new call shape, not to write a new
test** — an earlier revision described this as work to be created, which risks someone adding a
duplicate while deleting the original. Without it the overload is ceremony and the hole this
paragraph exists to close stays open.

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

**After `[one-terminal-call]` the raw pair have no callers in generated code** — though not zero
in-repo callers, because §8 leaves `examples/` un-regenerated and their committed output calls
`ShouldMatchStatusAsync`/`ShouldMatchContractAsync` 37 times across four files. Those calls are
compiled against the published `0.1.0-preview.1`, which is exactly the compatibility case this
decision protects. Once the examples are regenerated the count goes to zero, and
`ApiResponseAssertionsTests` becomes the only thing keeping the pair honest. They keep direct test
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
spec §3 line 172 and spec §5 line 475 as a designed-for v2 axis.

So this is **not a new decision. It is v1 confirming a decision already made**, and the reasoning
below is corroboration, not novelty:

**And it leads with the decisive reason, which this document had buried under ergonomics.** Spec
lines 180-181: *"its last commit is 2025-01-01 and its last release 2024-01-17, so it was the wrong
candidate for first-class support regardless."* Flurl fails `CONTRIBUTING.md`'s dependency policy on
maintenance grounds alone — that settles it before any ergonomic question is reached. The bullets
below explain why it would also have fitted badly even had it been actively maintained:
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
  not build the request at all. Per-call bearer application would break the case PR #6 enabled.
- **It would ship to adopters.** `InTest.Runtime` is published, so any dependency it takes lands
  transitively in every adopter's test project. `CONTRIBUTING.md`'s dependency policy is
  deliberately hard about that, and the generated project is explicitly the team's to own.

**The HTTP-pack axis stays live — this change *advances* it rather than foreclosing it.** Rev 5 left
that open; it is now decided, and the argument is stronger than "not a constraint". *(Section numbers
below are the 2026-08-16 spec's. Its "Frozen vs. additive axes" is a subsection of §5 at line 444 —
not to be confused with **this** document's §5, "Unchanged", the collision rev 4 already had to
correct once at `[captured-is-the-single-shape]`.)*

The axis has exactly one defined blocker, stated identically in four places (spec §2 line 119, §3
lines 170-180, §5 line 475, §17 line 2603): **`ApiTestBase.Client` is typed per pack** —
`HttpClient` under one, `IFlurlClient` under another — and one concrete base class in one package
cannot expose both. §3 lines 172-177 list three candidate resolutions and reject each, the third being
*a base class exposing no client at all*, rejected because it "pushes a resolve line into every
test."

`[one-terminal-call]` **is that third resolution applied to generated code only, with its objection
removed.** The distinction matters: `Client` stays exposed on `ApiTestCore` for hand-written
partials, so this is not the full "base class exposing no client" the spec rejected — it is that
resolution where it was actually blocking. Generated raw cases
stop touching `Client`, and they do not gain a resolve line — they gain `ExpectStatus`, which is
less code, not more. Today every raw case names `HttpRequestMessage`, `Client.SendAsync` and
`HttpResponseMessage`: three `HttpClient`-specific types in the generated file, in every case. After
this change the generated file's only transport-facing type is `HttpMethod` — BCL, and accepted by
Flurl itself. A future pack reimplements the four `Expect*` bodies and **the generated file does not
change one character.**

So the frozen-axes list is not retired. It is owed a *strengthening* note: consolidating the send
removes the generated-code half of the pack coupling, leaving v2's resolution to deal only with
hand-written partials that touch `Client` — which stays exposed on `ApiTestCore`, so the axis
remains **frozen**. Cheaper to add is not the same as unfrozen.

**The principle generalises: prefer what the platform already provides.** `HttpClient`,
`DelegatingHandler`, `Uri.EscapeDataString` and `HttpMethod` are the platform's, and InTest uses
them directly. `InTestUrl.Build` survives this test only because the BCL has no URI-template
substituter — the nearest option, `Microsoft.AspNetCore.Routing`'s template binder, would pull ASP.NET
Core routing into a test project to save a hundred lines that already exist and are tested. It is a
thin wrapper over `Uri.EscapeDataString`, not a layer of its own.

The assertion machinery is the genuinely custom part, and it stays custom because JSON Schema
validation has no BCL equivalent — NJsonSchema is already a pinned dependency and already does it.

## 4. Prerequisites

**Only P2 is a genuine prerequisite.** Rev 4 called both "correct independently, shippable alone";
that is true of P2 and false of P1. Extracting the captured conversion stands alone and is testable
alone. The pull seam shipped by itself is a `protected virtual` member **with no caller**, added to
a published public surface — which is this document's own verdict on the raw assertion adapters two
sections earlier: *"adapters with no caller and no test are dead code wearing a compatibility
label."* It applies verbatim. **P1 folds into the main change.**

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

**The simplest option was never weighed, and it would delete P1 entirely.** The token could just
stay an argument: `await ExpectStatus(204, HttpMethod.Delete, url, TestContext.CancellationToken)`.
That preserves read-at-call-time — the pull seam's own justification — adds no virtual member, needs
no compatibility argument, and keeps cancellation visible in the code the adopter reads. **Under that
option there is no P1 at all.**

**Decided: the pull seam, for a reason the "one argument per call" framing understates.** The cost is
not one argument; it is one argument on **every signature and every call site**. Four methods each
with a required-body overload on the raw pair is six signatures, and `TestContext.CancellationToken`
returns to
every generated line. That is precisely what `[one-terminal-call]` exists to remove: `TestContext` is
the MSTest-specific type in the generated file, and re-admitting it to every call would leave the raw
branch as un-portable as the client branch is today — while this change's whole claim is that the
generated file's only transport-facing type becomes `HttpMethod`. The explicit-argument option is
simpler in the runtime and worse in the artifact that is the product.

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
consolidated call it would also cover the UTF-8 *decode* — not materialization, since the first
risk bullet above establishes that `SendAsync(request, ct)` is `ResponseContentRead` and already
buffers the body inside the stopwatch. Reported milliseconds change in every
failure message. Cosmetic, but stated because §5 promises identical outcomes.

**Cancellation behaviour changes, and "identical outcomes" does not cover it.**
`ShouldMatchStatusAsync` returns today without touching the token when the status matches; both
`ShouldMatchCaptured*` call `ThrowIfCancellationRequested` first. After P2 a cancelled-but-matching
case throws instead of passing. That is probably the better behaviour — but §5 promises identical
outcomes, so it is an exception stated here rather than a `GeneratedSuiteExecutionTests` surprise.

**A pre-existing gap this design inherits unchanged.** `TestPlanBuilder` keys operations by the BCL
`HttpMethod` rather than a closed enum, so a spec carrying a non-standard verb (OpenAPI 3.2's
`additionalOperations`) makes `ToPascalMethod` emit `HttpMethod.Purge` and the generated file will
not compile. `ExpectStatus` taking an `HttpMethod` inherits the gap as-is.

**This one is reasoned, not measured** — the distinction `CLAUDE.md` requires. What *is* confirmed in
source: `TestPlanBuilder.cs:65` iterates `pathItem.Operations` with no verb filter, and
`TemplateRenderer.ToPascalMethod` is generic over the verb string, so nothing guards the path. What is
**not** established is whether Microsoft.OpenApi surfaces an `additionalOperations` verb into that
dictionary at all. An implementer should not report this as a confirmed defect without running it.

**Docs that must move in the same change — the earlier list was short.** In the 2026-08-16 spec:
§9's typed-client section, the `### Contract tests` sample (~1202, *already* stale — it shows
`TestData.Get` and `Schemas.Order`, both long removed), the `### Declared-error contract tests`
sample (~1318), the `### Auth contract tests` sample (~1402), and the prose at ~1337 calling
`ShouldMatchContractAsync`/`ShouldMatchStatusAsync` "the form you will actually see generated". In
`docs/getting-started.md`: line 290 ("every generated case keeps building its own
`HttpRequestMessage`, byte-for-byte identical to a project with no `client` section at all") and
line 371. `CLAUDE.md` requires the spec to change alongside the behaviour.

## 8. Files touched

- `src/InTest.Runtime/ApiTestCore.cs` — the `CancellationToken` seam (P1) and the consolidated call.
- `src/InTest.Runtime/ApiResponseAssertions.cs` — captured pair as single implementations, raw pair
  as adapters (P2).
- `src/InTest.Runtime.MSTest/ApiTestBase.cs` — overrides `TestCancellationToken` (P1). No
  signature changes.
- `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` and `TemplateRenderer.cs` — emit the
  consolidated call for the raw branch **and** the consolidated assertion for the client branch (the
  adopted scope; earlier revisions said "raw branch only"). Role gating unchanged.

**This inventory is measured, not derived.** Every earlier revision hand-derived it and every earlier
revision got it wrong — three in a row, and rev 6's own scope decision invalidated rev 5's list. The
tables below come from applying the change in a clone: template plus a real `ApiTestCore`/`ApiTestBase`
implementation, so the Golden suite could build and run. **Do not re-derive them by reading.**

**Headline, and it should temper the alarm in the rest of this section: the design compiles and
runs.** `dotnet build InTest.sln` succeeded with **0 warnings, 0 errors** under
`TreatWarningsAsErrors=true`. `GeneratedSuiteExecutionTests`: **22 passed, 1 failed**.
`CompileVerificationTests`: **7 passed, 0 failed** — including hostile spec text, the NSwag convention
call, integer/long path parameters and the self-closing client-map override.
`GeneratedSuiteBuildsAndPassesAgainstALiveService`, the auth-over-the-wire cases, the mixed-idiom
class and the run-twice idempotency case all pass green. Exactly **one** Golden assertion breaks.

*Caveat on scope of the measurement: **P2 was not implemented** in the harness — the raw pair are
still the real implementations rather than adapters over the captured pair. So §7's
cancellation-on-match and UTF-8-decode deltas are* not *exercised by these numbers and remain
reasoned.*

**Bucket 1 — fails loudly.** No risk; the implementer cannot miss these.

| file | lines |
|---|---|
| `TemplateRendererTests.cs` | 152, 159, **166**, 200, 201, 235, 362, 389, 407, 471, 483, 522, 581 (12 test methods) |
| `TemplateRendererClientTests.cs` | 348, 383, 402, 417, 426 (5 methods) |
| `GeneratedSuiteExecutionTests.cs` | 1066 (1 method) |
| `GoldenFileTests.cs` | `OutputMatchesTheGoldenFile` — whole-file compare |

**Bucket 2 — goes vacuous. Each needs its discriminator replaced, not merely to survive the edit.**

| file:line | today | replace with |
|---|---|---|
| `TemplateRendererTests.cs:160` | `ShouldNotContain("ShouldMatchContractAsync")` | `ShouldNotContain("ExpectContract(")` |
| `TemplateRendererClientTests.cs:349` | `ShouldNotContain("ShouldMatchContractAsync(
            response,")` | `ShouldNotContain("ExpectContract(")` |
| `TemplateRendererClientTests.cs:357` | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |
| `TemplateRendererClientTests.cs:358` | `ShouldNotContain("Client.SendAsync(")` | `ShouldNotContain("ExpectContract(")` |
| `TemplateRendererClientTests.cs:403` | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |
| `TemplateRendererClientTests.cs:404` | `ShouldNotContain("ShouldMatchCapturedContractAsync(")` | `ShouldNotContain("ExpectCapturedContract(")` |
| `GeneratedSuiteExecutionTests.cs:1069` | `ShouldNotContain("ShouldMatchCapturedContractAsync(")` | `ShouldNotContain("ExpectCapturedContract(")` |
| `GeneratedSuiteExecutionTests.cs:1070` | `ShouldNotContain("new HttpRequestMessage(")` | `ShouldNotContain("ExpectStatus(")` |

`TemplateRendererTests.cs:160` was named by no previous revision, and by neither party in review until
it was measured — which is the entire argument for not hand-deriving this table.

**Only one of these sits in a test that still reports green** —
`EmitsNoRawHttpRequestBuildingForAClientRoutedCase` (357/358), measured `Passed: 1`. The rest fail at
a sibling positive, so the implementer will already be in the file. **That is not safety.** Rewriting
348 and leaving 349, or 1066 and leaving 1069/1070, yields a green test discriminating nothing.

**Bucket 3 — survives and still discriminates.** Everything else, including all whitespace guards
(they caught a real stray-blank-line defect during the measurement), `StartsTheStopwatchBeforeTheTryBlock`
(335-340), the `ApiClient<` guards at 384/416, and 22 of 23 `GeneratedSuiteExecutionTests`. Also
`TemplateRendererClientTests.cs:385` (`ShouldNotContain("LastCapturedResponse")`) — it *looks* like it
should go vacuous and does not: the pinned `catch … when (InTestAmbient.LastCapturedResponse.Value?.Value
is null)` keeps that substring in client output, so the guard still trips on a regression.

**Bucket 4 — stays green for a reason unrelated to what it guards. One member, and it is real.**
`TemplateRendererClientTests.cs:401` (`ShouldContain("LastCapturedResponse")`) was written to pin *the
assertion call consuming the captured response*. After the change it is satisfied by the pinned catch
filter instead — measured by updating only line 402 and leaving 401 untouched: `Passed: 1`. Delete it
as redundant (402 now carries the real claim) or retarget it to something the catch filter cannot
satisfy. **This is the most dangerous category in the section**, because nothing goes red to announce it.

**Cancellation coverage needs an explicit statement, or a reader will conclude nothing was lost.**
`TemplateRendererTests.ThreadsTheCancellationTokenSoCooperativeCancellationWorks` (166) **fails** — it
renders a raw-only plan, so `TestContext.CancellationToken` genuinely disappears. It is bucket 1, not a
silent loss. But after this change `TemplateRendererClientTests.cs:266`
(`PassesTheCancellationTokenByNameRatherThanPositionally`) becomes the **only** surviving
`TestContext.CancellationToken` assertion in the repository, and it covers the *client call
expression* — Kiota's own `cancellationToken:` argument — **not InTest's send**. "Cancellation is still
covered by a fast test" would then be technically true and materially false.

  **Its replacement, with a corrected cost.** Nothing at template level can guard this after the pull
  seam, because no raw generated case names cancellation at all. It moves to `ApiTestCore`: a test-only
  subclass overriding `TestCancellationToken` with an already-cancelled token, asserting `ExpectStatus`
  throws `OperationCanceledException` before the stub handler is invoked. **Rev 6's cost estimate here
  was inflated and would have pushed an implementer to Golden-only coverage** — the one outcome this
  entry exists to prevent. Measured: the **status-only** form needs no `SchemaBundle` (only
  `ExpectContract` touches `InTestRun.Schemas`) and no live `InTestRun.Root` — only `Client`, which is
  `{ get; private set; }` and reachable with a single reflection hatch of the same shape
  `ApiTestCoreCaptureTests` already uses for `_scope`. So: **one hatch plus a stub `HttpMessageHandler`**,
  not two hatches plus a hand-built bundle.

- **`tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`** — directly in P2's blast radius, and
  the one area these measurements do not cover, since the harness did not implement P2.
- **`tests/InTest.Cli.Tests/TemplateEscapingGuardTests.cs`** — it parses the template and classifies
  each `tc.<name>` by quote parity, mechanically enforcing one of the three text-safety rules
  `CLAUDE.md` calls non-negotiable. The new shape keeps `expected_status`/`http_method_pascal` bare
  and the `*_literal` fields quoted, so it passes unchanged — **measured, not predicted**: it passed
  in both harness variants (raw-branch-only and the adopted scope).
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
  Three options, weighed rather than defaulted: leave them un-regenerated until the next publish
  (they then show the old shape until `0.1.0-preview.2`), or move them to a `ProjectReference` (which
  breaks the "an adopter restores from nuget.org" premise their own csproj comment states), or accept
  two broken examples. Silence would have shipped a green run and two dead example projects.
  **Decided: leave them un-regenerated.** `ProjectReference` is out — those two projects are the
  only artifact in the repo proving the *published* packages restore and build for a real adopter,
  which is why `ExampleProjectVersionMarkerTests` exists at all (it came out of the F14 incident,
  found exactly that way). Trading that permanently to avoid one release of staleness is a bad
  trade. "Accept two broken projects" is not available in a repo whose `CLAUDE.md` names
  fail-loudly non-negotiable. And leaving them is mechanically free: nothing under `.github/` or
  `scripts/` references `examples`, neither project is in the solution, so nothing goes red and
  nothing goes silently green — nothing runs.
  **Correction — rev 6 claimed a forcing mechanism already exists. It does not.** That claim was
  reached by reading `ExampleProjectVersionMarkerTests`' comment and treating it as the test's
  behaviour. The assertions say otherwise: `ThreeVersionMarkersAgreeAcrossEveryExample` compares the
  three markers **to each other** (`intestVersion != cliVersion`, `intestVersion != runtimeVersion`)
  and never against `CliVersion.Current`, and `RuntimePackageReferencePattern` deliberately matches
  *either* package id. So all three markers can agree at `0.1.0-preview.1` indefinitely, with the old
  id, and the suite stays green. Combined with the verified facts that nothing under `.github/` or
  `scripts/` references `examples/` and neither project is in `InTest.sln`, **nothing whatsoever
  forces regeneration.** The outcome this entry exists to prevent is exactly what the decision
  permits.
  **The forcing point cannot be a test, and that is the actual reason none exists.** The trigger is
  "at the next publish", which no assertion can express: a check comparing `examples/` to
  `CliVersion.Current` would go red on `main` the moment the CLI moves ahead, and stay red for the
  whole development cycle — pressure to migrate `examples/` preemptively, which
  `ExampleProjectVersionMarkerTests`' comment explicitly forbids (*"do NOT 'fix' that migration by
  touching `examples/` preemptively"*). A permanently-red guard is not a forcing mechanism; it is a
  broken build people learn to ignore.
  **So put it where per-release human steps already live:** `CONTRIBUTING.md`'s "Publishing
  checklist" (line 564), as a line requiring both example projects be regenerated and their
  `PackageReference` id moved to `InTest.Runtime.MSTest` before the tag is cut. Reinforce with a note
  beside each example's existing preview-pin comment (*this committed output predates the unified
  call surface; regenerate when the `PackageReference` id moves*), and extend the marker test's
  comment to point at the checklist. **This is weaker than a test and must be labelled as such** — it
  is a documented human step, and the honest statement is that `examples/` staleness is caught by
  release discipline, not by CI.
- The template header's `using System.Diagnostics;` and `using System.Text;`. These behave
  **differently** and the earlier note conflated them. `Encoding` appears at exactly one place in the
  template — line 103, the raw body arm — and the client branch never used it, so `using System.Text;`
  becomes unused in **every** generated class, not merely one with no client-routed case.
  `using System.Diagnostics;` behaves as stated: the client branch keeps its stopwatch, so only a
  class with no client-routed case stops needing it. Not a build error (the scaffold sets `Nullable` but
  not `TreatWarningsAsErrors`, and unused usings are IDE-only), but the header is a file this change
  touches.
- The design spec §9 and `docs/getting-started.md`.

**A cost worth naming, and the adopted scope reduces it.** The golden file and both `examples/`
projects still take a shape churn now and another in piece 2 — two reviewed diffs for adopters
instead of one. But because the middle option consolidates the client *assertion* as well, piece 2's
remaining churn is confined to moving cases from the raw branch to the client branch, rather than
also restating how every client case asserts. Rev 5 framed this cost against the raw-branch-only
scope, where it was larger.
