# Unified call surface for generated tests

**Status:** Design · Revision 3

**Revision note — rev 3.** Reviewed. Three findings would have produced wrong work, six were
smaller. Rev 2's core survived — its three rev-1 corrections were independently confirmed in
source — but it restated a Golden timing figure this repo has already corrected twice, it proposed
a change to `ApiTestCore.BeginTest` that the method's own compatibility overload argues against at
IL level, and it would have left three assertions passing vacuously.
**Date:** 2026-08-27
**Scope:** Piece 1 of two. Piece 2 — making the typed-client path the default — is explicitly out
of scope and gets its own cycle.

## 1. Why

The generated test is the product. An adopting team commits it, reads it when it fails, and
extends it with hand-written partials. Design principle #6 in
`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` states it: *"Generated code
is idiomatic and direct. No facades that obscure failure messages."*

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
token. Seven arguments become four; nothing describing *what the test does* is hidden.

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
in a package published as `0.1.0-preview.1`, and §5's compatibility table reserves public-surface
changes for a major bump. Pre-1.0 the exception is available, but taking it as a side effect of an
internal consolidation is not the same as taking it deliberately. Adapters give the same
consolidation at no compatibility cost.

**After `[one-terminal-call]` the raw pair have zero in-repo callers**, so
`ApiResponseAssertionsTests` becomes the only thing keeping them honest. They keep direct test
coverage; adapters with no caller and no test are dead code wearing a compatibility label.

**The compatibility argument cuts both ways, and rev 2 only applied one edge.** Declining to delete
two public statics is careful. But adding `ExpectStatus`/`ExpectContract` to the runtime and having
the CLI emit calls to them immediately breaks the *first* row of §5's table — "`InTest.Runtime`
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

Evaluated and rejected: Flurl, and third-party HTTP builders generally. The reasoning was never
recorded before — no HTTP-library alternative appears anywhere in the spec or `CONTRIBUTING.md`,
which made this an unexamined default rather than a decision. It is now a decision.

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

**P1 — the `CancellationToken` seam. Add a third overload; do not change a signature.**

Moving the send into `ApiTestCore` needs a token there, and `ApiTestCore` cannot obtain one from
`TestContext`. The mechanism is the **compiler**, not a test: `InTest.Runtime` has no MSTest
`PackageReference`, so there is no implicit global using and the type is simply unavailable.
(`NeutralityTests`' source scan matches the *namespace string* — `ApiTestCore.cs` already names
`TestContext` in a dozen doc comments and passes. Its own class doc calls the compiler "Layer 1,
primary". Rev 2 cited the wrong evidence for a right conclusion, which in this codebase is itself a
defect.)

So the token is a genuine fifth seam alongside `IRunDiagnostics`, the profile string, the display
name and the skip reason. **But it must arrive as a new overload.** `ApiTestCore.cs` already carries
a compatibility overload for `BeginTest`, and its doc argues at IL level that adding a parameter to
this exact method is a source break against published `0.1.0-preview.1` — it exists because
`BeginTest` gained a second parameter once already. Rev 2 proposed doing the thing that file
forbids. Add `BeginTest(string?, IRunDiagnostics, CancellationToken)` delegating from the two-arg
form, or state that the pre-1.0 exception is being taken deliberately.

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

**Docs that must move in the same change:** §9's typed-client section and its `### Contract tests`
sample, and `docs/getting-started.md`. `CLAUDE.md` requires the spec to change alongside the
behaviour.

## 8. Files touched

- `src/InTest.Runtime/ApiTestCore.cs` — the `CancellationToken` seam (P1) and the consolidated call.
- `src/InTest.Runtime/ApiResponseAssertions.cs` — captured pair as single implementations, raw pair
  as adapters (P2).
- `src/InTest.Runtime.MSTest/ApiTestBase.cs` — supplies the token.
- `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` and `TemplateRenderer.cs` — emit the
  consolidated call for the **raw branch only**; role gating unchanged.
- **`tests/InTest.Cli.Tests/TemplateRendererTests.cs`** — ordering assertions anchored on
  `IndexOf("new HttpRequestMessage(")` and assertions naming `ShouldMatchStatusAsync` /
  `ShouldMatchContractAsync`. These break loudly, which is fine.
- **`tests/InTest.Cli.Tests/TemplateRendererClientTests.cs` — the one that fails *quietly*.**
  Lines 357, 358 and 403 assert `ShouldNotContain("new HttpRequestMessage(")` and
  `ShouldNotContain("Client.SendAsync(")`. Once the raw branch stops emitting those strings, those
  three keep passing while discriminating nothing — the raw-versus-client separation they exist to
  prove silently evaporates, and their paired positive control at line 426 is the half that fails,
  so the pair does not fail as a unit. **Replace the discriminator in the same change** — assert on
  `ExpectStatus(` versus `ApiClient<` — so the vacuity is closed rather than discovered later, if
  at all. This is the "a suite silently matching nothing cannot read as green" rule that
  `assert-trx-results.ps1` exists for.
- **`tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`** — directly in P2's blast radius.
- `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` — regenerated golden file.
- `examples/Catalog.ApiTests/`, `examples/Orders.ApiTests/` — regenerated.
- The design spec §9 and `docs/getting-started.md`.
