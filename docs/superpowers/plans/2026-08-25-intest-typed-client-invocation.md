# Opt-in invocation through a team's pre-generated API client

**Status: complete, 2026-08-26.** Five commits, in order, plus Task 5 (`[lockfile-recovery]`)
below:

| Stage | Commit | What it landed |
|---|---|---|
| 1 — `[capture-infrastructure]` | `722fe7f` | `ResponseCaptureHandler`, `InTestAmbient.LastCapturedResponse`/`CapturedResponseSlot`, `[client-rides-the-api-pipeline]`'s authority guard. Inert: nothing registers the handler yet |
| 1b — the decisive proof | `69ede59` | A hand-written golden test class, calling a fake Kiota-shaped client, proves raw-bytes schema validation survives the client's own deserialization — before `ClientCallPlanner` exists |
| 2 — `[convention-and-config]` | `336b166` | The `client` config section, `client-map.json`, `ClientCallPlanner`, `TestPlanBuilder` wiring. Plans the call expression; nothing renders it yet |
| 3 — `[template-and-render]` | `0104a4d` | The template branch, `GenerateCommand` writing `clientCaptureEnabled`, full golden proof of *generated* code |
| fix — the schema-less gap | `19ab080` | A client-routed case with no response schema (a 204, or any `client-map.json` override) now still routes through the client instead of silently falling back to raw HTTP |
| 5 — `[lockfile-recovery]` | `02af19d` | `intest init --client-lockfile <path>`: recovers `spec.source` — and, where the lockfile names one, a `client` section — from a client generator's own lockfile, for a team that owns a generated client but not the OpenAPI document it came from. Kiota only; NSwag was measured and scoped out. See `[lockfile-recovery]` below, which supersedes `[lockfile-configures]`'s "nothing reads them yet" |
| 6 — `[typed-path-parameters]` | *(this change, uncommitted)* | A path parameter's fixture value is now converted to the type Kiota's per-parameter item-builder indexer actually declares (`Guid.Parse(...)`/`int.Parse(...)`/`long.Parse(...)`) before being spliced into a client-routed call, so the generated call binds the typed, non-obsolete indexer overload instead of the deprecated `this[string]` one. **Retires** the "Generator-version fragility" risk's dated finding below — see that risk entry, kept rather than deleted, for the closure note |

**Goal:** Let a team opt in, via a new `client` section in `intest.json`, to having generated
tests invoke their own pre-generated API client (Kiota, NSwag, Refit) instead of building
`HttpRequestMessage` by hand. Absent that section, output is byte-identical to before this
feature existed — every existing golden file and every project without a `client` section is
unaffected.

---

## Context

### The finding that shapes the whole design

The OpenAPI document feeds InTest two categorically different kinds of fact:

- **Invocation facts** — path template, method, path/query params, body media type. A typed
  client encodes these *better* than the spec does, because it encodes them typed.
- **Contract facts** — expected statuses, response schemas, `security` scopes,
  `example`/`default`, `required`. A generated client **discards** these on its way to producing
  a strongly-typed result. They are the entirety of what InTest asserts.

A generated client is therefore a **contract-lossy projection** of the spec, and the two are not
interchangeable planning inputs. Planning from a client instead of the spec would drop schema
validation, both auth cases, and fixture tiers 1–3 — a suite that passes while asserting almost
nothing, which is exactly what CLAUDE.md's "fail loudly / never substitute plausible defaults"
rule forbids. It is also circular: a test generated from `C(S)` (a client generated from spec
`S`) can only prove the API agrees with `C`, and typed deserializers are deliberately tolerant of
exactly the drift a contract test exists to catch — a required property silently defaulting to
`null`, an unknown enum member silently becoming the enum's default, are features of a
well-behaved client and the precise failures a contract test exists to surface.

**So `[spec-is-truth]`: the spec stays required and stays `TestPlanBuilder`'s only planning
input. The client changes only *how* a request is issued, never *what* is asserted.**
`TestPlanBuilder.Build` reads nothing new from the document because of this feature — it gains
one additional, optional parameter (`ClientPlanningConfig?`) carrying the adopter's *own*
configuration, not a second reading of the spec.

Two consequences follow directly, and both are load-bearing enough to be their own decisions
below: raw-bytes schema validation has to survive a typed client's deserialization
(`[capture-not-deserialize]`), and there is no common surface across generators to reflect over,
so a call expression has to be *derived per generator* rather than discovered
(`[convention-plus-override]`).

### Why this is not the §5 "HTTP pack" axis

The design spec's §5 frozen-axes table reserves an "HTTP pack" row for a *future* v2 concern:
`ApiTestBase.Client` being typed per pack (`HttpClient` under one pack, `IFlurlClient` under
another) — a single concrete base class cannot expose both, so switching packs is frozen and
would need a template-set swap. **This feature is not that.** `Client` stays exactly what it has
always been — `HttpClient` — and this feature adds a **parallel route** alongside it: a
client-routed Success case calls `ApiClient<TClient>()` instead of `Client.SendAsync(...)`, but
`Client` itself is untouched, still resolves the same way, and every raw-HTTP case (which is
still most of a mixed suite — see `[success-only]`) still uses it. The frozen-axis reasoning
transfers (an adopter's hand-written code that touches `ApiClient<T>()` would need to change if
the client type changed), but the *identity* does not: this is not the HTTP pack axis wearing a
new name, and the design spec update below is careful not to claim it is.

### How the runtime-framework split changes the picture

This feature landed after `docs/superpowers/plans/2026-08-25-runtime-framework-split.md` split
`InTest.Runtime` into a neutral package and `InTest.Runtime.MSTest`. That split is what makes
`[neutral-helper]` below free rather than a tradeoff: `ApiClient<T>()` and the captured-response
accessor land on `ApiTestCore`, which is framework-neutral, so a future xUnit or NUnit adapter
gets client-routed invocation for nothing — no MSTest-specific code exists to duplicate.

---

## Decisions

Named with slugs per `CONTRIBUTING.md`'s "Writing plans" — insertion and reordering cannot break
a slug, and several of these are quoted verbatim in doc comments already shipped in `src/`
(`InTestAmbient.cs`, `ResponseCaptureHandler.cs`, `ClientCallPlanner.cs`, `ClientCallMap.cs`,
`mstest-class.scriban`) — this document is the canonical explanation those comments point at.

### `[spec-is-truth]` — the spec stays required and stays the sole planning input

Covered in Context above. The mechanical consequence: `TestPlanBuilder.Build(OpenApiDocument
document, ClientPlanningConfig? client = null)` gained one optional trailing parameter. Every
existing call site (`GenerateCommand`, `FixturesRepairCommand`, every single-argument test call)
compiles unchanged, and a project with no `client` section in `intest.json` resolves
`clientPlanningConfig` to `null` in `GenerateCommand.RunAsync`, which short-circuits
`ResolveClientCall` to return `null` for every case before `ClientCallPlanner` is ever touched —
this is the mechanism behind "absent config, byte-identical output."

### `[capture-not-deserialize]` — a `DelegatingHandler` buffers raw bytes before the client sees them

**This is the feature's whole viability**, per `ResponseCaptureHandler`'s own doc comment.
`ResponseCaptureHandler` sits in the `InTestClients.Api` pipeline, after `AuthHandler` (closest
to the wire), and on the way back up: reads the response body into a `byte[]` via
`ReadAsByteArrayAsync`, stashes a `CapturedResponse` (status, body, method, URI) into the ambient
slot (see `[capture-is-opt-in]` for when it is even attached), then **replaces**
`response.Content` with a fresh `ByteArrayContent` built from those same bytes, headers copied
across (`Content-Encoding`/`Content-Length` included — confirmed by direct experiment
(`ResponseCaptureHandlerTests.GzipEncodedContentRoundTripsCorrectlyWhenHeadersAreCopiedOntoTheReplacement`)
not to corrupt a gzip-encoded response, because `IHttpClientFactory`'s default primary handler
never enables `AutomaticDecompression`, so nothing in the pipeline ever touches the bytes between
the wire and this handler). The typed client then reads that replacement exactly as it would have
read the original — Kiota and NSwag both deserialize via `ReadAsStreamAsync`, never
`ReadAsStringAsync`, which is why the golden proof's fake client is written to match that
specific API surface rather than a `ReadAsStringAsync` shape that would pass while proving
nothing (see the decisive golden test below).

Without this, the whole feature degrades to "the API answered and the client parsed it," which is
what CLAUDE.md's fail-loudly rule forbids — `SchemaBundle.Validate` needs the raw bytes, and a
typed client discards them on the way to a strongly-typed result.

### `[client-rides-the-api-pipeline]` — the adopter's client MUST be built over `InTestClients.Api`

**The design gap that most needed closing**, and the failure mode an adopter will actually hit
first — this is why `docs/getting-started.md`'s update leads with three concrete registrations
rather than starting from the config file.

The adopter's typed client must be constructed over
`IHttpClientFactory.CreateClient(InTestClients.Api)`. This is *not* the same shape as registering
`ITestTokenProvider`: a token provider is consumed *by* InTest; a typed client instead carries its
own `HttpClient` unless deliberately built over InTest's named one. Miss this and an adopter
silently loses `ResponseCaptureHandler`, `AuthHandler` and `RunIdHandler` at once — three
pipeline behaviours gone with no error, because nothing about a client built over a bare
`new HttpClient()` fails to compile or fails to run; it just talks to whatever base address it
was independently configured with (or none at all).

Two guards make the miss self-diagnosing rather than mysterious:

1. **`ApiTestCore.LastCapturedResponse` throws, naming the cause, rather than returning
   `default`.** A silent `default` would make a misconfigured client's test pass against status 0
   and an empty body — the exact "passes while asserting almost nothing" outcome CLAUDE.md
   forbids, and a far worse failure than an immediate, named exception.
2. **`ResponseCaptureHandler` compares the outgoing request's absolute-URI authority against the
   configured `Api:BaseUrl` authority, and throws on mismatch.** This is a genuinely new hazard,
   not one `InTestUrl.EnsureNoPrefixDuplication` already covers, and the two are easy to conflate
   because both reason about a request ending up in the wrong place:
   - `EnsureNoPrefixDuplication` runs once, at `InitializeAsync` time, comparing `Api:BaseUrl`
     against the spec's own operation-path prefix. It says nothing about what an individual
     request's URI looks like at send time, because for every *raw-HTTP* case there is nothing
     to say: `HttpClient.BaseAddress` resolves every relative URI those cases build, so there is
     exactly one place the request can go.
   - A typed client changes that. Kiota's request adapter is constructed with its own `BaseUrl`
     and builds a fully-qualified, **absolute** request URI directly from it — and per
     `HttpRequestMessage.RequestUri`'s own documented behavior, `HttpClient.BaseAddress` is not
     consulted at all once a request URI is absolute. A client whose own `BaseUrl` disagrees with
     `Api:BaseUrl` compiles, runs, gets a run id and an auth header exactly like any other request
     through this pipeline, and is capture-recorded exactly like any other — but was sent wherever
     *that client's own configuration* pointed, silently ignoring `Api:BaseUrl` the whole time.
     Nothing about that failure mode involves a repeated path prefix, so
     `EnsureNoPrefixDuplication` has no way to see it. A relative request URI needs no such check:
     `HttpClient.BaseAddress` (set to the same normalized `baseUrl` by
     `InTestRun.RegisterInTestClients`) governs it unconditionally, exactly as it already does for
     every raw-HTTP case, so there is no second authority for a relative URI to disagree with.

### `[captured-response-is-the-verdict]` — the pinned `try`/filter/`catch`, and why each part is load-bearing

A Success case whose typed-client call actually returns 500 is precisely what a generated
contract test exists to catch — and every typed client (Kiota, NSwag, Refit) throws its own
generator-specific exception on a non-2xx response *before* deserializing at all. Without this
decision, the adopter would see a bare Kiota `ApiException` stack trace instead of InTest's own
contract failure (run id, expected vs. actual status, elapsed, body excerpt) — a real regression
in error-reporting quality in exactly the case that matters most. The template emits, per
client-routed case:

```csharp
var stopwatch = Stopwatch.StartNew();
try
{
    await ApiClient<Orders.ApiClient.OrdersApiClient>().Api.Orders[FixtureParameter("getOrderById", "id")].GetAsync(cancellationToken: TestContext.CancellationToken);
}
catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null) { throw; }
catch (Exception) { /* the captured response is the verdict */ }
stopwatch.Stop();

await ApiResponseAssertions.ShouldMatchCapturedContractAsync(
    LastCapturedResponse, 200, "OrderResponse", Schemas, TestId, stopwatch.Elapsed, TestContext.CancellationToken);
```

Three details in it are load-bearing, not stylistic, and the emitted comment says so:

- **The stopwatch starts before the `try`, not inside it.** `ShouldMatchCapturedContractAsync`
  takes `elapsed`, and the throwing path still needs a real number — if the stopwatch started
  inside the `try`, a case that throws before the first `await` completes would report a
  meaningless (or unset) elapsed time in its own failure message.
- **It is an exception filter (`when (...)`), not catch-and-test inside the `catch` body.** A
  filter rethrows without ever entering the `catch` block on the "nothing captured" path, and —
  critically — it never touches the *throwing* `ApiTestCore.LastCapturedResponse` property. If the
  template instead wrote `catch (Exception) { if (LastCapturedResponse is null) throw; ... }`, the
  read of `LastCapturedResponse` itself would throw its own "[client-rides-the-api-pipeline]:
  nothing was captured" `InvalidOperationException` — masking whatever the real exception was
  (frequently the *actual* `[client-rides-the-api-pipeline]` authority-mismatch exception thrown
  from inside `ResponseCaptureHandler`, which lands in exactly this `catch`) and reporting the
  wrong cause to the adopter. The filter instead reads `InTestAmbient.LastCapturedResponse.Value?.Value`
  directly — the non-throwing ambient slot, not the throwing property.
- **Two `?.`s, not one.** `InTestAmbient.LastCapturedResponse` is
  `AsyncLocal<CapturedResponseSlot?>`. The first `?.` covers "no slot exists at all" (no test
  scope currently active — `ApiTestCore.BeginTest` has not run, or `EndTest` already cleared it);
  the second covers "a slot exists, but `ResponseCaptureHandler` has not written anything into it
  yet." Both conditions mean "there is no captured response to report the verdict from," so the
  filter falls through to `throw;` in either case, letting the original exception (whatever a
  bare `HttpClient` failure, a DNS error, or `[client-rides-the-api-pipeline]`'s own authority
  check produced) propagate unchanged.

### `[capture-is-opt-in]` — registration is derived from `clientCaptureEnabled`, not an always-on toggle

`ResponseCaptureHandler` is registered in DI unconditionally (`services.AddTransient(_ => new
ResponseCaptureHandler(baseUrl))`), but only actually **attached** to `InTestClients.Api` —
never `InTestClients.Readiness`, the same F10 exclusion `AuthHandler` already observes — when
`InTestRun.RegisterInTestClients`'s `captureEnabled` parameter is `true`. That parameter is read,
once, from `clientCaptureEnabled` in the generated project's `Generated/spec-paths.json`
(`InTestRun.ReadSpecPaths`), a key `GenerateCommand.BuildOutputs` writes as `true` — never `false`,
never written at all otherwise — exactly when at least one case in the plan resolved a non-null
`TestCasePlan.ClientCallExpression`.

This is justified by blast radius, not by worrying about toggle drift (an `appsettings.json` flag
would drift too, and that is not the reason this was rejected). `ResponseCaptureHandler` replaces
`response.Content` on **every** response that passes through `InTestClients.Api` once attached —
a change with unverified interaction risk for whatever downstream code eventually reads that
content. Deriving attachment from whether the plan actually produced a client-routed case confines
that risk to the adopters who opted in, rather than running it unconditionally for the ~100% of
suites (today, all of them) that never will. `GenerateCommand.cs`'s own comment on this point notes
one more subtlety: the check is against the *plan's* `ClientCallExpression`, not
`TemplateRenderer`'s per-case `client_call_expression` — the latter can additionally render `null`
for a schema-less case in an older build (fixed by `19ab080`, see below) — but registering the
handler is harmless even for a case that ends up rendering raw HTTP anyway, because `Client` and
every typed client both resolve over the same `InTestClients.Api` pipeline, so the handler simply
has nothing extra to do for those cases.

### `[convention-plus-override]` — derive per generator; `client-map.json` overrides unconditionally

`ClientCallPlanner.Resolve` is the single place a call expression is decided, called only for
`CaseRole.Success` cases (`[success-only]`, below), with the override lookup running **first,
before either gate is even inspected**: an explicit entry in `client-map.json` bypasses
convention-derivation, the query-parameter gate, and the request-body gate all at once, because
the adopter wrote real C# and owns it. See `[compiler-is-oracle]` and the two measured findings
below for why convention exists for Kiota only.

### `[compiler-is-oracle]` — a wrong guess fails the adopter's own build, loudly, at a generated line

No convention-derived or override expression is validated beyond syntax-adjacent checks
(`client-map.json`'s blank-value refusal; `client.typeName`'s
`CSharpIdentifier.TryValidateDottedName` check, since it reaches the template in *reference*
position, `ApiClient<T>()`, not inside a string literal). Whether the expression actually compiles
against the adopter's real generated client is left entirely to the C# compiler on the adopter's
next build. This is a genuinely stronger oracle than a name-existence check would be: the compiler
proves the whole call expression — receiver chain, indexer overload resolution, argument list — not
merely that a method with that name exists, and `InTest.Golden.Tests` already runs a real `dotnet
build` on generated output, so a convention that is *wrong* fails CI immediately rather than
compiling into a test that asserts nothing useful.

### `[success-only]` — only `CaseRole.Success` cases ever resolve a client call

`TestPlanBuilder.Build`'s main loop calls `ResolveClientCall` at exactly one site, building the
`Success` case — declared-error (404) and auth (401/403) cases are built afterward, from separate
helper methods (`TryPlanDeclaredNotFound`, `PlanAuthCases`), and neither ever touches
`ClientCallPlanner`, regardless of whether `client` is configured. Those cases exist to exercise
the *API's* behaviour against a deliberately unmatchable path value (a GUID that does not exist, a
wrong-scope token) — the test's whole point is what the API does with a bad input, not how a typed
client happens to handle one, and routing them through a client would demand per-generator
exception-shape knowledge (does this client throw on 404? What type? Does it deserialize the
`ProblemDetails` body or discard it?) for no gain: the assertion those cases make is purely about
status and (for errors) schema, exactly what `[capture-not-deserialize]` already lets a
raw-HTTP case verify without any client involved.

### `[neutral-helper]` — `ApiClient<T>()` and the captured-response accessor live on `ApiTestCore`

Both are `protected` members of the neutral `ApiTestCore`, not the MSTest-specific `ApiTestBase`:
`ApiClient<TClient>()` resolves `Services.GetRequiredService<TClient>()` (the same DI scope
`Client` itself resolves from), and `LastCapturedResponse` reads
`InTestAmbient.LastCapturedResponse.Value?.Value`, throwing `[client-rides-the-api-pipeline]`'s
named exception when nothing was captured. Neither needs anything MSTest-shaped, so placing them
on `ApiTestCore` — made possible by the runtime-framework split landing first — means a future
xUnit or NUnit adapter inherits client-routed invocation for free, the same reasoning that already
applies to `RequireFixture` and the rest of that class.

### `[refit-override-only]` — "Refit" names an interface shape, not one generator with one convention

Refit clients are reachable from more than one source — Refitter, NSwag's own Refit template
output, or a hand-written interface — so there is no single naming/shape convention to derive at
all, unconditionally, independent of any measurement. `ClientKind.Refit` gets no
`ClientCallPlanner` branch; every Refit operation routes through `client-map.json`.

### `[lockfile-configures]` — noted as a design constraint, not shipped in this feature (stages 1–3/fix)

`kiota-lock.json`'s `clientClassName`/`clientNamespaceName` and `nswag.json`'s
`operationGenerationMode`/`className` *configure* what a generator's convention actually produces,
rather than leaving it purely guessed — a fact worth recording because it bears on the
generator-version-fragility risk below, but reading either lockfile automatically (to recover
`client.typeName`, or to detect a convention-breaking regeneration) was not built in this feature
and ships with no code behind it. `client.typeName` is adopter-supplied, hand-written JSON.

**Superseded in one direction by Task 5 below**: `init --client-lockfile` now does read
`kiota-lock.json` automatically — but only at scaffold time, to *recover* `spec.source` and an
initial `client` section for a project that has neither yet, never to detect a *later*
regeneration drifting out from under an already-scaffolded `intest.json`. That second use — a
lockfile diff at `generate` time flagging a convention that quietly stopped applying — is still
undesigned and unbuilt; `[compiler-is-oracle]` remains the only defence against it. `typeName`
recovered from a lockfile is still adopter-supplied in every sense that matters downstream: it is
validated the same way (`CSharpIdentifier.TryValidateDottedName`) and written into the same
hand-editable `intest.json` a directly-typed value would be, not read fresh on every `generate`.

### `[lockfile-recovery]` — Task 5: `init --client-lockfile`, Kiota only, measured against a real config each way

**What was built.** `intest init --client-lockfile <path>`, mutually exclusive with `--spec` (both
given is refused, naming both; neither given is refused exactly like a blank `--spec` always has
been — one voice, not two). `ClientLockfile.Recover` (`src/InTest.Cli/Spec/ClientLockfile.cs`) — a
DISTINCT FOURTH concern beside `SpecLoader`, `SpecFetcher` and `SpecSnapshot`, because it parses an
entirely different file format produced by a third-party tool, not an OpenAPI document or the
transport that fetches one — reads a `kiota-lock.json`:

- `descriptionLocation` recovers `spec.source` directly, handled identically whether it names a
  local path or an `http(s)` URL — this type does no URL-specific parsing of its own, and the
  recovered value flows into `InitCommand`'s existing `specSource` variable, through the
  unchanged `SpecLoader.IsUrl` / `SpecFetcher.TryValidateUrl` / `MSBuildPropertyValue.TryEscape`
  path, never a parallel one.
- `clientClassName` + `clientNamespaceName`, dot-joined, recover `client.typeName` — confirmed
  against a real kiota 1.34.1 lockfile (`kiota generate --openapi
  samples/Orders.Api/Orders.Api.json --class-name OrdersApiClient --namespace-name
  Orders.ApiClient --language CSharp`): `"OrdersApiClient"` + `"Orders.ApiClient"` gives exactly
  `Orders.ApiClient.OrdersApiClient`, the same value getting-started's own worked example already
  uses. Present only when both fields are present in the lockfile (kiota always writes them
  together — a lockfile naming only one is refused as more likely hand-edited than a legitimate
  partial state); absent, `init` scaffolds a `spec.source` with no `client` section, same as a
  plain `--spec` project.
- A required field missing, renamed, blank or wrong-typed fails loudly, naming the field —
  `ClientLockfileException`, caught by `InitCommand` the same way `GenerateCommand` already
  catches `SpecLoadException`/`ConfigLoadException` (print the message bare, exit 2) — never a
  silent null that would resurface, far from here, as `ConfigLoader`'s "spec.source is empty"
  refusal.

**One correction to the risk section below, made by direct measurement rather than left open**:
`kiotaVersion` (`"1.34.1"` in the measured fixture) IS a stable, always-present field in
`kiota-lock.json` — the generator-version-fragility risk previously left this unverified because
nothing had measured it. Nothing reads `kiotaVersion` today (there is no version-drift detection
built), but the field existing and being stable is now a measured fact, not an open question.

**NSwag was measured and scoped out, not skipped.** `nswag new` (NSwag 14.7.1) was run to get real
ground truth. What it produces, `nswag.json`, is materially different from a lockfile in the sense
this task needed: it is the *input config* an adopter writes and maintains themselves *before*
generation, not a record the generator writes *after* — so recovering a spec location from it
returns little a team without the OpenAPI document does not already have. More fatally for
`client.typeName` specifically: under NSwag's own default `operationGenerationMode`
(`MultipleClientsFromOperationId`), `codeGenerators.openApiToCSharpClient.className` is
`"{controller}Client"` — a *naming template* with a placeholder, not a concrete type name, and
that same generation mode produces one class *per controller*, not the single class
`client.typeName` names. Resolving the template against the spec's actual controllers is exactly
the kind of per-generator guessing `[compiler-is-oracle]` already rejected for NSwag convention
derivation (measured finding 2, above); reading it from a config file rather than deriving it from
spec text does not change that verdict. Consistent with that same call, NSwag lockfile recovery is
not built — a NSwag-shaped file handed to `--client-lockfile` fails loudly on the same
"no descriptionLocation" message any unrecognised JSON object gets, an honest "cannot recover
this" rather than a wrong answer.

**Verification.** `ClientLockfileTests` (parse a real-shaped `kiota-lock.json`, both
`descriptionLocation` forms, missing file, missing/renamed/blank/wrong-typed field refusal,
malformed JSON, partial client-identity refusal) and `InitCommandTests` (`--client-lockfile` alone
scaffolds the recovered `spec.source` and a working `client` section verified through
`ConfigLoader.Load` — not a text scan, so the double-`$` raw-string brace trap in `intest.json`'s
template would actually be caught; both flags together refused naming both; neither given refused
exactly like a blank `--spec` always has been).

### `[typed-path-parameters]` — Task 6: convert a fixture value to the declared type before splicing it into the indexer

**What was built.** `PathParameterKind` (`src/InTest.Cli/Planning/PathParameterKind.cs`) grew from
two members (`String`, `Integer`) to four: `String`, `Integer`, `Long`, `Guid`. Deliberately kept
to those four — they cover id-shaped path parameters, which is what path parameters overwhelmingly
are; date/decimal/etc. were not speculatively added.
`TestPlanBuilder.ResolvePathParameterKind` (the per-parameter classification
`ResolvePathParameterKinds` maps every declared path parameter schema through) now reads
`IOpenApiSchema.Format` alongside `.Type`: `string` + `format: uuid` → `Guid`; `integer` +
`format: int64` → `Long`; `integer` with any other or absent format (`int32` included) → `Integer`,
unchanged from before this task; everything else → `String`, also unchanged. `IsNumericType` is
reused for the "numeric at all" question rather than reimplemented — only the format-based
sub-classification is new logic.

`TestPlanBuilder.Build`'s Success-case construction now also calls `ResolvePathParameterKinds` and
sets `TestCasePlan.PathParameterKinds` — previously only the declared-error and auth branches
(`TryPlanDeclaredNotFound`, `PlanAuthCases`) populated this field, because the raw-HTTP
declared-error/auth branch was its only consumer. The client-routed branch is why Success needs it
too now.

`TemplateRenderer.BuildClientCallExpression` — the client-routed branch's only consumer of this —
wraps each `{param}` placeholder's `FixtureParameterCall` splice per its resolved kind
(`WrapForClientCall`): `Guid.Parse(...)` for `Guid`, `int.Parse(...)` for `Integer`,
`long.Parse(...)` for `Long`, and no wrap at all for `String` (the fixture value already has the
type a string-typed indexer expects). **This applies to the client-routed branch only** —
`TemplateRenderer.PathArguments`'s raw-HTTP branch, which feeds `InTestUrl.Build` (a
`string`-typed API), still splices every Success path parameter bare regardless of declared kind,
exactly as it always has; `PathArguments`'s declared-error/auth arm (`UnmatchableValueFor`) is
untouched in behaviour too — it still renders the same numeric literal for both `Integer` and
`Long`, and the same fresh GUID for both `String` and `Guid`, since a raw-HTTP request needs no
type conversion at all and only ever needed "numeric or not" to pick a well-typed unmatchable
value (decision 6). The `#pragma warning disable CS0618` / `#pragma warning restore CS0618` pair
around the client-routed call in `mstest-class.scriban` is deleted outright — nothing this
template emits reaches the deprecated overload any more, so there is nothing left to suppress.

`ClientCallPlanner.Resolve` gained no fifth gate for an "unsupported path-parameter kind" — asked
for, and found unreachable by construction: `ResolvePathParameterKind` is exhaustive over every
schema shape it can see, with `String` as the catch-all for anything it does not specifically
recognize, so there is no fifth, unmapped kind for such a gate to ever detect. Adding one would
have been dead code guarding a state the type system and `TestPlanBuilder`'s own resolution logic
already make impossible; `ClientCallPlanner`'s own doc comment now records this explicitly rather
than leaving a future reader to wonder why the gate is missing.

**Verification.** `TestPlanBuilderTests` resolves all four kinds from real schema shapes
(`{"type":"string"}` → `String`; `{"type":"string","format":"uuid"}` → `Guid`;
`{"type":"string","format":"date-time"}` → `String`, proving an unrecognized format falls through
rather than being misclassified; `{"type":"integer"}` and `{"type":"integer","format":"int32"}` →
`Integer`; `{"type":"integer","format":"int64"}` → `Long`) — read off the Success case specifically,
since that is the new call site this task added and the one that would actually catch a regression
in that wiring. `TemplateRendererClientTests` covers all four kinds' splice shape directly
(`Guid.Parse(FixtureParameter(...))`, `int.Parse(FixtureParameter(...))`,
`long.Parse(FixtureParameter(...))`, and the unwrapped `FixtureParameter(...)` for `String`).

**The golden proof — this is the one that actually closes the risk below, not merely asserts it.**
`GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer` (renamed
from the pre-existing `GeneratedClientRoutedSuccessCaseWithAPathParameterCompilesDespiteTheObsoleteIndexer`,
which this task's fix obsoletes by construction — its own name described the exact defect that no
longer exists) changed `SpecWithPathParameter`'s `id` parameter from a bare `type: string` to
`type: string, format: uuid`, so the case it generates resolves to `PathParameterKind.Guid`.
`GoldenTypedClientSources.FakeOrdersApiClient`'s `FakeStatusRequestBuilder` already carried both a
`this[Guid position]` (non-obsolete) and an `[Obsolete]`-marked `this[string position]` overload,
matching real kiota 1.34.1 output — added by the *previous* task's `[finding-3]` fix, at which
point `this[Guid]` was present but unused (the generated call still spliced a bare `string`, so it
bound `this[string]` instead). This task reverses which overload the generated call actually
binds; the golden test's own doc comment and the two indexer declarations' inline comments in
`GoldenTypedClientSources.cs` were updated to say so. The test asserts the generated line reads
`Api.Status[Guid.Parse(FixtureParameter("getStatusById", "id"))].GetAsync(...)`, asserts **no**
pragma appears anywhere in the generated source, and still builds with
`-p:WarningsAsErrors=CS0618` — passing with no `CS0618` in the build output at all, because the
generated call never reaches the deprecated overload in the first place. Before this task the same
build-clean-under-that-flag result depended on the pragma; now it depends on nothing but the
generated call binding the right overload, which is the actual claim this plan's risk section
needed proven, not merely asserted.

---

## Two findings settled by direct experiment, not reasoning — and where the plan document was wrong

CLAUDE.md asks this codebase to record whether a claim was "confirmed by direct experiment" or
merely "the docs say." Both findings below correct the original plan sketch after it was measured
against real generator output; the corrected shape is what shipped, and each is pinned by a test
that fails if the correction is reverted.

### 1. `AsyncLocal` cannot carry the capture directly — the naive shape was built and measured, and it fails

The first, intuitive design was `ResponseCaptureHandler.SendAsync` doing
`InTestAmbient.LastCapturedResponse.Value = new CapturedResponse(...)` directly — a plain
reassignment — with a generated test method reading that same static property back after its
`await client.SomeCall()` returned. **This was built first and measured, not merely reasoned
about, and it does not work.**

`AsyncLocal<T>` reassignments made inside a nested, genuinely-suspending `await` are isolated to
that nested call's own continuation and are **reverted the instant control returns to the awaiting
caller** — the same `ExecutionContext` capture/restore mechanism that (correctly, and by design)
already makes `TestId` and `Identity` flow *downward* from `ApiTestCore.BeginTest` into every
handler a test's requests pass through is exactly what kills the *upward* direction this capture
needs. The downward flow is not in question — it is what makes `LastCapturedResponse` reachable
inside `ResponseCaptureHandler` at all — but a value set *deep inside* an awaited call, meant to
be read back by the *caller* once that call returns, does not survive the same boundary.

Verified with two isolated repros: a bare nested `async` method, and the exact
`HttpMessageInvoker`-over-`DelegatingHandler` shape this handler actually runs in. Both showed the
naive reassignment reverting to its prior value the moment the outer `await` returned. Independent
confirmation the shipped fix actually depends on this: patching the handler back to the naive
reassignment and rerunning `ApiTestCoreCaptureTests.CapturesStatusBodyMethodAndUri` and the gzip
round-trip test makes both fail.

**The fix:** flow a *mutable reference cell* (`CapturedResponseSlot`, a plain class with one
settable `Value` property) downward via the `AsyncLocal`, and have `ResponseCaptureHandler` mutate
the cell's own field rather than ever reassigning the `AsyncLocal`'s `Value` itself. Mutating an
already-shared reference type is ordinary heap mutation, independent of `ExecutionContext`
propagation entirely, so it survives the await-return boundary that a plain reassignment does not.
`ApiTestCore.BeginTest` assigns a **fresh** `CapturedResponseSlot` (not merely clears a stale one),
so a test that never makes a client-routed call can never observe a previous test's leftover
capture purely by construction — a brand-new object holds nothing yet.

**This is why `[captured-response-is-the-verdict]`'s exception filter needs two `?.`s, not one**:
the slot itself can be null (no test scope active) independently of whether the slot, once
present, holds a captured value yet.

### 2. NSwag convention derivation does not work; Kiota's does — measured, not assumed

The original plan sketched a `{OperationId}Async` convention meant to apply across generators.
Measured against **kiota 1.34.1** and **nswag 14.7.1**, both run against
`samples/Orders.Api/Orders.Api.json` — which declares **no `operationId`** anywhere, the common
ASP.NET Core case — the results narrowed this materially, in Kiota's favor and against NSwag's:

- **Kiota — convention ships.** Confirmed directly from the generated builder classes: each
  literal path segment becomes a PascalCase property on a fluent builder chain, each `{param}`
  segment becomes an indexer, and the verb becomes a method (`GetAsync`, `PostAsync`, `PutAsync`,
  `PatchAsync`, `DeleteAsync`). `GET /api/orders/{id}` becomes
  `client.Api.Orders[id].GetAsync()`. Crucially, **every item builder Kiota emits carries both a
  `this[Guid position]` indexer and an `[Obsolete]`-marked `this[string position]` overload**
  (confirmed in `OrdersItemRequestBuilder.cs`, `CustomersItemRequestBuilder.cs`) — which is exactly
  why splicing `FixtureParameter("opKey", "param")` (a `string`-returning helper) into the
  indexer compiles at all. Without that string overload this convention would need a
  per-parameter type conversion, the same problem that rules NSwag out below.
- **NSwag — override-map-only, on measured evidence, not caution.** The plan's original
  `{OperationId}Async` convention turned out wrong twice over, against the same no-`operationId`
  spec:
  1. NSwag synthesizes `{Resource}{VERB}Async` and invents a **collection-vs-item distinction**
     no path-segment convention like Kiota's predicts without also knowing NSwag's own naming
     settings: `CustomersAllAsync()` for `GET /api/customers` (the list), but
     `CustomersGETAsync(id)` for `GET /api/customers/{id}` (the item).
  2. **Fatally, parameters are strongly typed with no string overload** —
     `OrdersGETAsync(System.Guid id)`. Splicing a fixture's `string` value there does not compile.
     `[compiler-is-oracle]` would catch this loudly, which is the design working as intended — but
     shipping a convention *already measured* to emit uncompilable code is not a guess worth
     making regardless.

NSwag support is **deferred, not closed off**. It needs a type-mapping layer that turns the spec's
`type`/`format` into a conversion expression (`format: uuid` → `Guid.Parse(...)`, `integer` →
`int.Parse(...)`) before a fixture's raw string value could ever be spliced into a strongly-typed
NSwag parameter and compile. `TestPlanBuilder.PathParameterKinds` (used today for a different
purpose — deciding fixture-value shape) already carries a coarser version of exactly this verdict,
so that is where such a layer would extend from, rather than inventing a second type-classification
mechanism. It must additionally match whatever NSwag itself chose for a given parameter, which
depends on NSwag's own generation settings — one more reason this is deferred rather than guessed
at.

---

## Convention gates, precisely as built

`ClientCallPlanner.Resolve`'s gate order, in the order it actually runs:

1. **Override lookup runs first, unconditionally.** A `client-map.json` entry for the operation
   key returns that value verbatim and skips every gate below — including for an operation with
   query parameters or a request body.
2. **Kind gate.** Anything other than `ClientKind.Kiota` gets no convention attempt at all —
   `[refit-override-only]` for Refit unconditionally, and NSwag on the measured evidence above.
3. **Query-parameter gate.** Kiota binds query parameters through a `RequestConfiguration<...>`
   lambda, never positional arguments; convention derivation has no fixture value to splice into
   that shape, so any operation with one or more query parameters withholds convention and emits a
   `CoverageNote` pointing at `client-map.json`.
4. **Request-body gate.** Kiota's `PostAsync`/`PutAsync`/`PatchAsync` take a **typed model
   object** as their first positional parameter, never a JSON string — and a fixture's request
   body is raw JSON text. Splicing it would mean guessing the generated model type's name, which
   this planner never attempts; any operation with a JSON request body to compose withholds
   convention the same way, with the same note.

Both gates apply only when reached — an override already returned before either runs, so an
operation with query parameters *and* an entry in `client-map.json` is fully covered by the
override, not silently dropped to raw HTTP.

---

## What does not change

- **`TestPlanBuilder.Build`'s existing signature and every existing call site.** The new
  `ClientPlanningConfig?` parameter is optional and trailing; every caller that predates this
  feature compiles and behaves unchanged.
- **A project with no `client` section in `intest.json`.** `ConfigLoader.Load`'s
  `ReadOptionalClientConfig` returns `null`; `GenerateCommand` never constructs a
  `ClientPlanningConfig`; `TestPlanBuilder.Build` never resolves a client call; `clientCaptureEnabled`
  is never written to `spec-paths.json`; `ResponseCaptureHandler` is registered in DI (always) but
  never attached to `InTestClients.Api`. Output is byte-for-byte identical to a build that predates
  this feature — the golden regression suite pins this.
- **`Client`.** Still `HttpClient`, resolved exactly as before. See "Why this is not the §5 HTTP
  pack axis" above.
- **`CSharpLiteral.Escape` and `FixtureDocument.TryValidateOperationKey`.** Neither applies to a
  `client-map.json` override value — it is trusted C# spliced bare, per that file's own
  documented trust model, not a string literal and not a filename.
- **Declared-error and auth cases.** Never route through a client, regardless of configuration —
  `[success-only]`.

---

## Files that carry this feature

| File | Role |
|---|---|
| `src/InTest.Runtime/CapturedResponse.cs` | `readonly record struct` — status, body, method, URI |
| `src/InTest.Runtime/InTestAmbient.cs` | `LastCapturedResponse` (`AsyncLocal<CapturedResponseSlot?>`) and `CapturedResponseSlot` — the mutable-cell fix from finding 1 |
| `src/InTest.Runtime/ResponseCaptureHandler.cs` | `[capture-not-deserialize]` + `[client-rides-the-api-pipeline]`'s authority check |
| `src/InTest.Runtime/ApiTestCore.cs` | `ApiClient<T>()`, `LastCapturedResponse` accessor — `[neutral-helper]` |
| `src/InTest.Runtime/ApiResponseAssertions.cs` | `ShouldMatchCapturedContractAsync`, `ShouldMatchCapturedStatusAsync` — captured-response counterparts of the existing raw-HTTP assertions, sharing one `Failure(...)` message formatter |
| `src/InTest.Runtime/InTestRun.cs` | Registers `ResponseCaptureHandler`; reads `clientCaptureEnabled` from `spec-paths.json`; attaches the handler to `InTestClients.Api` only when true |
| `src/InTest.Cli/Configuration/LoadedClientConfig.cs`, `ConfigLoader.cs` | The `client` section — `kind` + `typeName`, both required together |
| `src/InTest.Cli/Clients/ClientCallMap.cs` | `client-map.json` parsing — trusted overrides |
| `src/InTest.Cli/Planning/ClientKind.cs`, `ClientPlanningConfig.cs`, `ClientCallPlanner.cs` | Kind enum, the assembled planning input, and convention derivation + override resolution |
| `src/InTest.Cli/Planning/TestPlanBuilder.cs` | `ResolveClientCall`, called once per Success case; the stale-override-key `CoverageNote` |
| `src/InTest.Cli/Planning/TestCasePlan.cs` | `ClientCallExpression` — the carried verdict |
| `src/InTest.Cli/Rendering/TemplateRenderer.cs`, `Templates/mstest-class.scriban` | `BuildClientCallExpression`; the pinned `try`/filter/`catch` branch; `client_type_name`/`client_call_expression` as bare (unescaped) template fields |
| `src/InTest.Cli/Commands/GenerateCommand.cs` | Assembles `ClientPlanningConfig` from `LoadedConfig.Client` + `ClientCallMap.Load`; writes `clientCaptureEnabled` |

`src/InTest.Runtime.MSTest/` needs no change — `ApiTestBase` inherits `ApiTestCore`'s new members
for free.

---

## Verification

- **The decisive proof (stage 1b, `69ede59`).** A hand-written golden test class
  (`GoldenTypedClientSources.FakeStatusClient`, deliberately deserializing via
  `ReadAsStreamAsync` — the one API surface a `ReadAsStringAsync`-based fake would fail to
  exercise) calling a fake client against `GoldenApiStub`: a schema-violating body with a
  matching status fails the inner test on a *named schema violation*; a conforming body passes
  and hands the client a usable deserialized result; a 500 surfaces InTest's own contract failure,
  never the client's exception type. Mutation-checked by flipping `clientCaptureEnabled` to
  `false`: the test goes red on its own discriminating assertion, not by accident.
- **The generated-code proof (stage 3, `0104a4d`, extended by `19ab080`).** The same three
  verdicts, now against *generated* code calling a Kiota-shaped builder chain, plus the
  schema-less case (`19ab080`'s bodiless `GET /api/ping` returning 204, chosen for needing no
  fixture machinery) routing through the client rather than silently falling back to raw HTTP.
- **Golden regression.** A project with no `client` section matches the existing golden file
  byte-for-byte — the mechanical proof behind "What does not change" above.
- **`TemplateEscapingGuardTests`.** `client_type_name` and `client_call_expression` are both
  listed in `AllowedInBarePosition` with a one-line reason each, so the guard that scans the
  template for un-escaped spec-derived text does not flag either as a defect.
- **Measured on this branch, 2026-08-26** (`dotnet build InTest.sln`, then each suite
  `--no-build`): build clean, 0 warnings. `InTest.Architecture.Tests` **12** passing,
  `InTest.Cli.Tests` **555** passing, `InTest.Runtime.Tests` **234** passing,
  `InTest.Golden.Tests` **43** passing (3m14s) — the suite that actually proves generated code
  compiles and runs, so do not skip it when touching the template or renderer. Do not trust these
  counts without re-measuring — `main` moves under contributors on this repository.
- **Re-measured after Task 6, `[typed-path-parameters]`, same 2026-08-26** (`dotnet build
  InTest.sln`, then each suite `--no-build`): build clean, 0 warnings, `samples/*.json` restored
  with `git checkout -- samples/` (a `dotnet build` regenerates them; never staged, per
  CONTRIBUTING.md). `InTest.Architecture.Tests` **12** passing (unchanged — this task never touched
  that project), `InTest.Cli.Tests` **589** passing (+10: six `TestPlanBuilderTests` covering each
  of the four `PathParameterKind`s resolved from a real schema shape, four
  `TemplateRendererClientTests` covering each kind's client-routed splice shape), `InTest.Runtime.Tests`
  **245** passing (unchanged — CLAUDE.md's constraint against touching `src/InTest.Runtime/**`
  applied), `InTest.Golden.Tests` **44** passing (3m14s, unchanged in count — the one path-parameter
  golden test this task touches was renamed and its assertions rewritten in place, not
  duplicated). The intervening commits between the first measurement above and this one
  (`02af19d`, `19d4bb5`) already moved `InTest.Cli.Tests` from 555 to 579 and `InTest.Runtime.Tests`
  from 234 to 245 before this task's own +10 landed — the deltas quoted here are against this
  task's own starting point, not the first bullet's numbers directly. Do not trust these counts
  without re-measuring either — the same caveat applies.

---

## Risks

- **A mixed suite, by design, in v1.** A generated class can contain both client-routed Success
  cases and raw-HTTP siblings (declared-error, auth, and any Success case a gate withheld) —
  reintroducing "two ways of calling the same API" *inside a single file*, the exact problem this
  feature's own motivation names. Accepted for v1: guessing query-parameter or request-body
  binding across three generators is how a wrong test ships that still compiles, and the override
  map covers any operation a team actually cares about routing through the client.
- **Generator-version fragility.** Kiota and NSwag have both changed default naming/topology
  across majors. `[compiler-is-oracle]` is the defence — a convention that compiles today but
  breaks against a regenerated client (regenerated by the adopter's own tooling, entirely outside
  any `intest generate` run) fails the adopter's build loudly, not silently. `[lockfile-configures]`
  documents that the two lockfiles *could* narrow this further; Task 5's `[lockfile-recovery]`
  reads `kiota-lock.json` now, but only at `init` time, to recover a project's *initial*
  `spec.source`/`client` section — not at `generate` time, to detect a *later* regeneration
  drifting the convention out from under an already-scaffolded project. That narrower use is still
  undesigned. One field is now measured rather than assumed, though: `kiotaVersion` is confirmed
  stable and always-present in a real kiota 1.34.1 lockfile — previously listed here as an open
  question, not a verified fact.

  **This risk is now concrete and dated, not hypothetical, for the Kiota convention itself.** A
  code-review finding on the shipped feature confirmed what a real kiota 1.34.1 client's own
  attribute already says, word for word: `OrdersItemRequestBuilder`'s (and every other item
  builder's) `this[string position]` indexer — the exact overload
  `ClientCallPlanner.BuildKiotaConvention`'s splice depends on, since `FixtureParameter` returns
  `string` — carries
  `[Obsolete("This indexer is deprecated and will be removed in the next major version. Use the
  one with the typed parameter instead.")]`. That is Kiota's own generator telling adopters, today,
  that the overload this convention relies on is scheduled for removal, not merely at risk of a
  breaking rename the way "generators change across majors" reads as an abstract possibility above.
  When that major ships, every convention-derived path-parameter call `[compiler-is-oracle]`
  accepted today stops compiling at once — not a slow drift, a single-version cliff. The stopgap
  shipped alongside this finding (a `#pragma warning disable CS0618` scoped to the generated call
  site, so a `TreatWarningsAsErrors` adopter's *current* build does not break on the warning Kiota
  already emits) buys time against the warning, not against the eventual removal — the pragma
  cannot suppress a member that no longer exists. **The real mitigation is the same type-mapping
  layer NSwag support already needs** (see "Two findings settled by direct experiment" above): a
  layer that turns a path parameter's declared `type`/`format` into a conversion expression
  (`format: uuid` → `Guid.Parse(...)`, `integer` → `int.Parse(...)`) before splicing a fixture's raw
  string value in, would let this convention target the *typed* indexer (`this[Guid position]`,
  confirmed present alongside the obsolete one in the same measured fixture) instead of the
  deprecated string one — permanently outrunning the removal rather than merely deferring it. That
  is the same missing layer `TestPlanBuilder.PathParameterKinds` already carries a coarser version
  of, and the same one NSwag's strongly-typed, no-string-overload parameters need before *that*
  generator can get a convention at all. One piece of future work unblocks both gaps this plan
  currently ships without; it remains unbuilt today, so both gaps remain open until it lands.

  **CLOSED for Kiota by Task 6, `[typed-path-parameters]` (this change).** The type-mapping layer
  the paragraph above called for is exactly what was built: `PathParameterKind` grew `Long` and
  `Guid` alongside `String`/`Integer`, `TestPlanBuilder.ResolvePathParameterKind` reads
  `format` (`uuid` → `Guid`, `int64` → `Long`) as well as `type`, and
  `TemplateRenderer.WrapForClientCall` converts a client-routed splice through
  `Guid.Parse(...)`/`int.Parse(...)`/`long.Parse(...)` before it reaches the indexer — so the
  generated call now binds the *typed* overload (`this[Guid position]`, confirmed non-obsolete in
  the same measured fixture) rather than the deprecated `this[string position]` one this finding
  named. The `#pragma warning disable CS0618` stopgap named above is deleted outright, not merely
  left in place alongside the fix: it suppressed a warning the generated call no longer triggers,
  so keeping it would have hidden a real regression instead of a known, accepted one.
  `GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer`
  (`tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`) is the golden proof: it builds a
  generated project with `-p:WarningsAsErrors=CS0618` and asserts both that the pragma is nowhere
  in the generated source *and* that the build still succeeds with no `CS0618` anywhere in its
  output — which is only possible if the generated call binds the non-obsolete overload, not
  merely if a suppression is hiding whichever one it actually bound. The single-version-cliff this
  finding warned about (every convention-derived path-parameter call breaking at once when Kiota's
  next major actually removes `this[string]`) no longer applies to a path parameter InTest can
  classify as `Guid`, `Integer`, or `Long` — it only remains live for a path parameter this
  four-kind classification cannot recognize at all (falls through to `String`, e.g. a spec that
  encodes an id as a bare, non-uuid-formatted string), which was never eligible for the removal
  risk in the first place since a plain `string` never bound the deprecated overload to begin with.
- **Stale `client-map.json` entries warn, not block** — a `CoverageNote`, softer than fixtures'
  drift-blocks-`generate` gate, because (unlike a fixture) there is no second derivable answer to
  diff an override against; the only available check is "does this key still name an operation in
  the plan at all."
- **NSwag and Refit ship override-map-only**, by measured choice for NSwag and by definition for
  Refit (`[refit-override-only]`) — not a gap expected to close automatically as more generators
  are tried, but a decision each would need its own measurement (NSwag) or has no convention to
  measure at all (Refit).
