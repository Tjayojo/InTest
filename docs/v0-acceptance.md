# Acceptance runs — v0, v1-a, v1-b, v1-c, the F11 phase, v1-e Task 6, the 0.1.0-preview.1 publish, the adopter dry run, the full Phase 0-8 walkthrough, the three-package-split acceptance run, the framework-pack acceptance run, and the 0.1.0-preview.2 publish

A living record. Each phase ends by regenerating against `samples/` and appending its results
here, so the defect numbering (`F1`, `F2`, …) runs continuously across phases and the "carried
forward" list at the end is always the current one.

| Phase | Date | Commit | Headline |
|---|---|---|---|
| v0 | 2026-08-17 | `bec4ee1` + F1 fix | Catalog **6 of 9**; Orders and Inventory generated but never run |
| v1-a | 2026-08-17 | `466e118` | All three run live: **22 of 22**; **44 sentinels** filled by hand |
| v1-b | 2026-08-19 (UTC) | `f07ce4c` + this commit | Catalog **9 of 9 twice, sequentially** (not concurrently — §11), a negative control reproducing F7 on the same suite/database with the fixture unregistered, and a drain-isolation run proving cleanup, not a test, deletes the seeded row. **F7 closed** |
| v1-c | 2026-08-19 (UTC) | `f09f2d5` + this commit | Orders live against a real Duende identity server: 401s real, write-scope 403s real (**F8 closed for real**), a dead identity server fails by name, not as a readiness timeout (**F10 closed for real**). Two new findings the live run — not the unit suite — exposed: 4 of 7 wrong-scope 403 tests cannot pass against the sample's only identity pair, because read operations need no scope the read-only identity lacks (**F11**); a mis-scoped write request 415s, not the 400 Task 8 Step 3 predicted (**F12**). Catalog **13 of 13 twice**, Inventory **9 of 9 twice**, neither gains an auth test — v1-b's guarantee survives |
| F11 | 2026-08-21 | `0cf649a` | Orders live against a real Duende identity server, correctly scoped: **20 passed, 0 failed, 4 skipped**, all 4 skips bottoming out in `RequireSecondaryIdentityLacks` with a stated reason, the 3 write-scope 403s running and passing (**F11 closed**). Independently reproduced from scratch by a second agent with its own suite, provider, fixtures and ports — both runs agree exactly. A negative control (declared `Scopes` set to `null`) reproduces the original F11 failure on demand: 4 failed, not skipped. Catalog **13 of 13 twice**, Inventory **9 of 9 twice**, neither gains an auth test |
| v1-e Task 6 | 2026-08-22 | `cc43714` + this commit | The verdict run for `generate --check` and `intest upgrade`. `dotnet tool restore` — Phase 8's first line, never exercised before this run because every earlier acceptance run substituted `dotnet run --project` or a `ProjectReference` — made to work for real via a temporary local NuGet feed, not substituted around; a stale `InTest.Runtime 0.1.0` in the global package cache (built one commit behind HEAD) was found and cleared, not packed over. All 5 steps ran against live `samples/Orders.Api` + `samples/Identity.Server`: spec-edit drift (exit 1), a true orphaned-file case built by removing every `/api/customers` operation (exit 1, extra file named, sibling class byte-identical), a contrived version mismatch pre-empting a simultaneous diff (exit 4, §8's exact message), `intest upgrade` resolving it and the suite passing again (20/0/4), and a real `core.autocrlf=true` cross-platform checkout — proved both ways: `.gitattributes` present keeps every byte LF and `--check` clean, absent it every generated file corrupts to CRLF and fails on every line, and `upgrade`'s migration path (scaffold `.gitattributes` if absent) fixes it live. One new finding: bare `intest …`, as shown in every code block in `getting-started.md`, does not run against a locally-restored tool — needs `dotnet intest …` (**F13**) |
| 0.1.0-preview.1 publish | 2026-08-24 | `35056b8` (tagged) | First real tag push. `pack.yml` and `release.yml` both ran on GitHub Actions for real — trigger firing, matrix fan-out, cross-job artifact transfer, the OIDC exchange and the `nuget-release` environment gate all previously unexercised. Both jobs green; nuget.org accepted all four artifacts. Phase 8's `dotnet tool restore` now works from a bare clone for the first time — previously every acceptance run had to substitute something. The `.snupkg`-under-`tools/` question (open since readiness spec revision 2) is answered: nuget.org accepts it |
| Adopter dry run | 2026-08-24 | `a4c8315` + this commit | The first real adoption run against the published packages (publish-actions item 6, previously open). Found both committed examples' `intestVersion` and `.config/dotnet-tools.json` pins hand-edited to `0.1.0` — a version never published — while their `.csproj`'s `InTest.Runtime` reference correctly named `0.1.0-preview.1` (**F14**). Reproduced the consequence: `dotnet tool restore` fails outright on a bare clone. Fixed with the published `0.1.0-preview.1` CLI's own `intest upgrade`, not by hand; regeneration changed nothing beyond the two version markers. Re-proved the full `dotnet tool restore` → `dotnet intest generate --check` → `dotnet build` path against both examples, cold `NUGET_PACKAGES`, all green. New guard, `ExampleProjectVersionMarkerTests`, proven to fire by reverting one marker |
| Full Phase 0-8 walkthrough | 2026-08-25 | `b349e25` + this commit | The first full `getting-started.md` walkthrough — Phase 0 through Phase 8, in order — against the published packages with zero substitutions: `dotnet tool install -g InTest.Cli --version 0.1.0-preview.1`, three fresh `intest init` scaffolds, `fixtures repair`, a hand-written `ITestTokenProvider` for Orders, `dotnet tool restore`, `generate --check`, and `dotnet build` all against a cold, isolated `NUGET_PACKAGES` with no local leftovers. Live results: Catalog **13 of 13**, Orders **20 passed, 0 failed, 4 skipped** (all four `RequireSecondaryIdentityLacks`), Inventory **9 of 9** — reproducing `getting-started.md`'s own stated banner numbers for the first time against the *published* tool rather than a local build. `generate --check`'s exit `4` and `intest upgrade` both exercised on a fresh scaffold and closed the loop (exit 0, suite passing again). **No new defect found** — every phase matched the document exactly. One re-run of `dotnet test InTest.sln` in the repo caught a transient `MSB3713` file-lock build failure in `InTest.Golden.Tests`, consistent with the other session's concurrent activity in this shared tree; reproduced clean in isolation, not a product defect |
| Three-package split | 2026-08-26 | `d152412` + this commit | First adoption walk against the **runtime-framework split** (`InTest.Runtime.MSTest`, never published) — a *simulated* publish (three packages packed locally, never nuget.org) via `scripts/local-e2e-test.ps1`, run twice. All three packages pack at one identical version; a fresh scaffold's `InTest.Runtime.MSTest` `PackageReference` matches it and resolves `InTest.Runtime` transitively at the exact same version (confirmed via `dotnet list package --include-transitive`, not assumed). Extended beyond the script's own documented scope with a live run: Catalog **13 of 13** over real HTTP against the locally-packed three-package build. `release.yml`/`pack.yml` confirmed three-package-complete (the release job's own 6-asset positive control already covered this), with a stale two-package step name/comment fixed alongside the one real defect found: **F15**, `local-e2e-test.ps1`'s own "cache is clean" confirmation printing even on a run where its own tripwire had just warned that pre-existing `intest.cli`/`intest.runtime` entries (an unrelated earlier session's leftovers) were sitting in the global cache — fixed with a `$CacheClean` flag, negative-controlled by re-running against the same still-dirty cache
| Framework packs | 2026-08-31 | `69f0918` | The first live run of a **generated suite that is not MSTest**. xUnit and NUnit each reproduce the MSTest numbers exactly against the same live APIs: Catalog **13 of 13**, Orders **20 passed, 0 failed, 4 skipped**. All 4 skips bottom out in `RequireSecondaryIdentityLacks` under all three adapters' different skip mechanisms (`Assert.Inconclusive` / `Assert.Skip` / `Assert.Ignore`), and the 3 write-scope 403s run and pass — so the skip decision is per-operation, not a blanket avoidance. `[error-is-the-sink]` confirmed end to end: NUnit's assembly-scope `Note`/`Warn` reach the `.trx`. **No InTest defect found.** Two findings, neither a product defect: **F16**, NUnit3TestAdapter's `.trx` `<Counters>` reports `notExecuted="0"` while four `NotExecuted` results are present in the same file; **F17**, the sample SQLite stores are never reset, so committed example fixture values no longer apply to a long-lived store |
| 0.1.0-preview.2 publish | 2026-08-31 | `b7fab09` (tagged) | Second real tag push, and the first at **five packages**. All three `release.yml` jobs green (`Pack (verify against tag)`, `Publish to nuget.org`, `Create GitHub Release`); the GitHub Release carries exactly **10 assets** (five `.nupkg` + five `.snupkg`), a count `release.yml` asserts mechanically. `InTest.Runtime.MSTest`, `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit` are **published for the first time**. `dotnet tool install -g InTest.Cli --version 0.1.0-preview.2` resolves and reports the exact tagged commit; all three adapters restore from nuget.org and resolve `InTest.Runtime 0.1.0-preview.2` transitively, holding §3's compatibility contract at the published version. One propagation-lag data point recorded, not a defect: install failed for ~4 minutes right after the push before the registration index caught up. **`ubuntu-latest` only, one run — no claim about a stable tag or a second OS's `publish` job** |

---

# v0 acceptance run

**Task:** Plan Task 22 — point InTest at real deployed APIs and record what happens.

The v0 plan's acceptance criterion was a run against "one real API in a real pipeline". This
run used three purpose-built sample APIs instead (`samples/`), one per OpenAPI producer, so the
producer matrix and the acceptance run are the same exercise. They are committed, so every
finding below is reproducible — with one qualification the original v0 run did not know to state:
reaching the ports named throughout this document requires setting `ASPNETCORE_URLS` externally
when starting each sample, which nothing here or in `samples/README.md` records; see F9 in the
v1-b section below, found by literally following the README's own commands.

## What was exercised

| Sample | Auth | Producer | OpenAPI version | Operations | `operationId` |
|---|---|---|---|---|---|
| `Catalog.Api` | none | built-in `Microsoft.AspNetCore.OpenApi` | **3.1.1** | 9 | 0 of 9 |
| `Orders.Api` | Duende, client-credentials | Swashbuckle 10.2.3 | **3.0.4** | 7 | 0 of 7 |
| `Inventory.Api` | none | NSwag 14.7.1 | **3.0.0** | 6 | 6 of 6 |

All three producers behaved exactly as §6 documents. Swashbuckle and the built-in package emit
no `operationId` for controller actions; NSwag derives `{Controller}_{Action}` — `Stock_GetAll`,
`Warehouses_GetById`.

Three producers also produced **three different OpenAPI versions**, which was not planned and is
worth knowing: the built-in package emits 3.1, Swashbuckle 3.0.4, NSwag 3.0.0. A tool claiming
OpenAPI 3.x support meets all three in a single organisation.

## Results

| Stage | Result |
|---|---|
| `intest generate` across all three | **22 operations generated, 0 skipped** |
| Generated projects compile | **3 of 3** |
| Catalog suite against a live API | **6 of 9 passing** |
| Orders, Inventory live runs | **Not run** — closed in v1-a below |

**16 of 22 operations (73%) ran on synthesized operationIds.** The decision to treat synthesis
as a first-class path rather than a fallback (§6) is load-bearing, not defensive: on this
corpus most of the suite depends on it.

## Defects found

### F1 — `appsettings.json` never reached the output directory · **fixed**

Every generated project failed at `AssemblyInitialize`:

```
System.IO.FileNotFoundException: The configuration file 'appsettings.json' was not found
and is not optional. The expected physical path was '…/bin/Debug/net10.0/appsettings.json'.
```

`init` scaffolds `appsettings.json`, but the generated `.csproj` copied only
`Generated/spec-schemas.json` to the output directory. `TestHost` resolves configuration from
`AppContext.BaseDirectory`, so nothing could start.

**No existing test caught this**, and the reason matters: §16's compile-verification test proves
generated code *builds*, never that it *runs*. Building and running are different gates, and v0
only had the first.

Fixed in `InitCommand` by adding `appsettings*.json` to the copied content, and now guarded by
`GeneratedSuiteExecutionTests` — which was verified to fail with this exact exception when the
fix is reverted.

### F2 — readiness path is resolved against the API base URL · **fixed**

Readiness probed `http://localhost:5081/api/health/ready` and got 404, because the base URL was
`…/api/` and the probe path is relative. Health endpoints conventionally live at the **host
root**, not under the API prefix — the sample follows that convention, as most services do.

**Not a design flaw — a scaffold default.** Ordinary URI resolution already distinguishes the
two cases: `health/ready` resolves against the base URL, `/health/ready` against the origin.
The scaffold shipped the former; it now ships the latter, and both forms are tested.

### F3 — base URL and spec path prefix silently duplicate · **fixed**

```
GET http://localhost:5081/api/api/products/aaaaaaaa-… → expected 200, got 404 (2ms)
```

The configured base URL was `http://localhost:5081/api/`; the spec's paths already begin
`/api/products`. InTest ignores `servers[]` by design (§7), so the configured base URL plays
that role and spec paths append to it — meaning the base must be the **origin**, not the API
prefix, whenever the spec's paths carry it.

Nothing states this. §7 documents the *opposite* failure at length — a missing trailing slash
silently dropping a base segment — and the guard added for it does not detect duplication.
The symptom is every test returning 404 with a correct-looking configuration.

Detected rather than documented: `generate` writes the shared operation prefix to
`Generated/spec-paths.json`, and `AssemblyInitialize` fails before the first request with a
message naming both halves and the value to use instead.

### F4 — readiness burns the full timeout on a 404 · **fixed**

F2 took the full 120 seconds to fail. A 404 or 405 on the probe path is a misconfiguration, not
a cold start, and no amount of waiting fixes it. 404, 405, 410 and 501 are now terminal, and the
message explains leading-slash resolution — F2 would have been reported in three seconds.

### F5 — route constraints do not disambiguate OpenAPI paths · **sample fixed, worth documenting**

The first Inventory spec was rejected:

```
The OpenAPI document could not be parsed:
  The path signature '/api/stock/{}' MUST be unique.
exit code 2
```

`GET /api/stock/{sku}` and `DELETE /api/stock/{id:int}` are distinct routes to ASP.NET, which
disambiguates by constraint. **OpenAPI has no notion of route constraints** — both collapse to
`/api/stock/{}`, which the specification requires to be unique. Any producer will emit this
invalid document from such a controller.

InTest behaved correctly: it refused to generate, named the exact problem, and returned exit
code 2 per §5's convention. This is a real-world trap worth a line in the documentation, since
the API compiles and serves traffic perfectly well.

## Known v0 gaps confirmed

The three Catalog failures were all `POST`/`PUT`:

```
POST http://localhost:5081/api/products → expected 201, got 415 (2ms)
Body: {"title":"Unsupported Media Type","status":415,…}
```

No request body was sent, because v0 has no fixtures — `TestData` covered path parameters only.
This was the documented v0 boundary and the entire subject of plan **v1-a**. It was not a
defect, and it is **closed below**: all three are green.

Also confirmed working as designed:

- **Failure messages.** Every failure named method, URL, expected vs actual, elapsed time, run
  id and response body. Diagnosing F3 took one message.
- **Run identity.** `tjayo-20260817T111559Z-c578e154-postapiproducts-contract` — prefix, UTC
  timestamp, entropy, and a slug derived from the display name, all ASCII.
- **Readiness messages.** `Service did not become ready within 120s (last response: 404).
  Probed 'health/ready' expecting 200, requiring 2 consecutive successes.` Named everything
  needed to diagnose F2.
- **Status-only tests.** 4 of 22 operations returned 204 and generated status-only tests rather
  than being skipped — the case an earlier revision silently dropped.

## v0 actions

All five closed.

| # | Action | Resolution |
|---|---|---|
| 1 | F3 — detect base-URL/path-prefix duplication | `generate` writes the shared operation prefix to `Generated/spec-paths.json`; `AssemblyInitialize` fails before the first request, naming both halves and the correct value. Segment-wise, so `/api` against `/apiary` is not flagged. §7 documents `Api:BaseUrl` as substituting for `servers[0].url` |
| 2 | F2 — readiness path resolution | Not a design flaw: ordinary URI rules already distinguish `health/ready` (base-relative) from `/health/ready` (origin-rooted). The scaffold shipped the former; it now ships the latter. Both forms tested |
| 3 | A test that **runs** a generated suite | `GeneratedSuiteExecutionTests` scaffolds, generates, builds and runs against a live `HttpListener` stub. **Negative control performed**: with the F1 fix reverted it fails with the original `FileNotFoundException`; restored, it passes |
| 4 | F4 — terminal readiness statuses | 404, 405, 410 and 501 now fail immediately with a message explaining leading-slash resolution, rather than consuming the timeout |
| 5 | F5 — route-constraint trap | Documented in `docs/getting-started.md` under "Things that will bite you" |

Two further defects surfaced while fixing these, both in the scaffold and both fixed: the
runsettings file was named `orders.runsettings` regardless of project name, and the default
`Api:BaseUrl` shipped **with** an `/api/` prefix — which is what produced F3 in the first place.

Test count went from 103 to 123.

---

# v1-a acceptance run — fixtures

**Date:** 2026-08-17 (UTC) · **Commit:** `466e118` (branch `feature/v1a-fixtures`)
**Task:** Plan v1-a Task 10 — regenerate against the samples now that fixtures exist, and
measure the fixture workload a real adopter faces.

Unit suite before the run: **226 passing, 0 failing** — Architecture 2, Cli 130, Runtime 88,
Golden 6.

Each sample got a **fresh test project in a scratch directory outside the repository**, taken
through `intest init` → `intest generate` → `intest fixtures repair` → fill sentinels →
`intest generate` → `dotnet test` against the live API. One deviation from a real adopter's
setup, in every project: `InTest.Runtime` is not published to NuGet, so the scaffolded
`PackageReference` was swapped for a `ProjectReference` — the same substitution
`GeneratedSuiteExecutionTests` makes.

## Results

| Sample | Ops | Fixtures composed | Sentinels filled | Live result |
|---|---|---|---|---|
| `Catalog.Api` | 9 | 8 | **23** | **9 of 9** |
| `Orders.Api` | 7 | 5 | **14** | **7 of 7** |
| `Inventory.Api` | 6 | 4 | **7** | **6 of 6** |
| **Total** | **22** | **17** | **44** | **22 of 22** |

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 3 s - Catalog.ApiTests.dll (net10.0)
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 3 s - Orders.ApiTests.dll (net10.0)
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 3 s - Inventory.ApiTests.dll (net10.0)
```

**Catalog reached 9 of 9**, as the plan predicted. The three v0 failures were exactly the three
operations carrying a request body — `POST /api/categories`, `POST /api/products`,
`PUT /api/products/{id}` — all of which returned 415 for want of one. Every one is now green.

**Orders and Inventory ran live for the first time**, closing the largest v0 gap. Orders needed
`samples/Identity.Server` for a client-credentials token; auth *tests* are still v1-c, so what
Orders proves here is that a secured API's bodies and parameters flow correctly under a bearer
token — not the 401/403 paths.

`generate` exiting 1 while fixtures are unresolved is by design (Task 4), and it did, every
time. Catalog's first run:

```
delete_api_categories_id: no fixture found.
get_api_categories_id: no fixture found.
get_api_products: no fixture found.
get_api_products_id: no fixture found.
get_api_products_id_tags: no fixture found.
post_api_categories: no fixture found.
post_api_products: no fixture found.
put_api_products_id: no fixture found.
Run 'intest fixtures repair' to create or update the fixture(s) listed above.
exit code 1
```

**Reproduced independently.** A second scratch project built from scratch off the same spec
produced `Created 8 fixture(s), updated 0 fixture(s)` and the same **23** sentinels; a second
live run against a freshly seeded database returned **9 of 9** again.

## The fixture workload — what `intest survey` will need to predict

This is the measurement the task existed for. **44 sentinels across 17 fixture files for 22
operations — two per operation on average**, but the average badly understates the shape:

| Where the work is | Sentinels |
|---|---|
| Path parameters (one per operation that has one) | 12 |
| Request-body properties | 32 |

Body properties are **73% of the work** and they cluster. Catalog's single
`post_api_products` is **11 of that sample's 23** — one operation, nearly half the API's
fixture cost — because every leaf property of a request body is sentinelled, required or not:

```jsonc
{
  "$meta": { "tier": 4, "operationId": "post_api_products", "generatedBy": "intest 0.1.0" },
  "body": {
    "sku": "TODO:sku",              "name": "TODO:name",
    "description": "TODO:description", "price": "TODO:price",
    "stockQuantity": "TODO:stockQuantity", "categoryId": "TODO:categoryId",
    "category": "TODO:category",    "availableFrom": "TODO:availableFrom",
    "supplierEmail": "TODO:supplierEmail", "dimensions": "TODO:dimensions",
    "tags": [ "TODO:tags" ]
  }
}
```

Only five of those eleven are in the schema's `required` set. **A useful predictor is therefore
not operation count but total leaf-property count across all JSON request bodies, plus one per
path parameter** — not `required` count, which would have predicted 5 where the real cost was 11.

Three shapes cost **nothing**, and all three are decisions working as designed:

- **Operations with no parameters and no body compose no fixture at all** — `GET /api/categories`,
  `GET /api/customers`, `GET /api/warehouses`.
- **Optional query parameters are omitted entirely** unless the spec gives them an `example` or
  a `default` (decision 1), so an operation whose only parameters are optional filters also
  composes nothing — `GET /api/orders` and `GET /api/stock`.

  Together those account for **5 of 22 operations, which is why there are 17 fixtures and not 22.**
- **Where a default exists, it is used and no sentinel appears.** Catalog's `GET /api/products`
  has five optional query parameters and produced a **tier-3 fixture with zero sentinels**:

  ```jsonc
  { "$meta": { "tier": 3, … }, "$parameters": { "page": "1", "pageSize": "20" } }
  ```

  This is the case the plan's self-review flagged as nearly fatal: sentinelling every parameter
  would have blocked an operation that already passed in v0 and finished v1-a *below* v0's six.
  The decision held.

## Defects found

### F6 — a nullable object property composes a scalar sentinel, losing its shape · **fixed**

`CreateProductRequest.dimensions` is a nullable reference to another schema. The built-in
producer emits OpenAPI 3.1's idiom for that:

```json
"dimensions": { "oneOf": [ { "type": "null" }, { "$ref": "#/components/schemas/DimensionsRequest" } ] }
```

`FixtureComposer.ComposeFromSchema` handles `$ref`, `object`, and `array`, but not `oneOf` or
`anyOf`. The schema is none of the three, so composition falls through to the bottom of the
method and emits `"dimensions": "TODO:dimensions"` — a **string** sentinel for what is actually
an object with three required properties. The adopter is told the property needs a value and
given no indication of its shape; the real fixture had to be written by hand:

```jsonc
"dimensions": { "lengthCentimetres": 10.0, "widthCentimetres": 5.0, "heightCentimetres": 2.0 }
```

**Nesting itself is fine** — the contrast proves it. Orders' `CreateOrderRequest.lines` is a
plain `array` of `$ref` and composed correctly, all the way into the nested object:

```jsonc
"lines": [ { "sku": "TODO:sku", "quantity": "TODO:quantity", "unitPrice": "TODO:unitPrice" } ]
```

So the gap is specifically **un-navigated `oneOf`/`anyOf`**. It is not cosmetic: `oneOf` with a
null branch is how OpenAPI 3.1 expresses *any* nullable complex property, and 3.1 is what the
built-in ASP.NET producer emits. Every adopter on the default .NET stack with a nullable
sub-object hits this.

Not blocking — the sentinel still fails loudly and the property here was optional — but it
under-reports the workload, and a required nullable sub-object would leave an adopter guessing.

**Fixed in `8d0367a`, hardened in `6952aeb`.** `ComposeFromSchema` now resolves a
`oneOf`/`anyOf`/`allOf` union by discarding branches that declare the JSON `null` type and
recursing into the single survivor. Zero or more than one remaining branch is genuine ambiguity
and still falls through to a sentinel rather than guessing — so the OpenAPI 3.0 composition
idiom `allOf: [{$ref: Base}, {…}]` is unchanged, not silently half-composed.

The check sits *after* the object and array checks, so a schema carrying both `type: object` and
an `allOf` still composes its declared properties. **That ordering is the fragile part of the
fix**, and review caught it stated only in a commit message: moving the check up beside the
`$ref` navigation reads as a tidy-up, leaves every test green, and silently drops those declared
properties. It is now pinned by a comment at the call site and by a regression test —
**negative control performed**: hoisting the check above the object check makes that test fail,
restoring it returns the suite to green.

This **changes the measurement above**: `dimensions` becomes three sentinels instead of one, so
Catalog goes from 23 to **25** and the corpus total from 44 to **46**. Orders (14) and Inventory
(7) are unchanged — verified by re-running `init` → `generate` → `fixtures repair` against all
three specs. The workload table and totals earlier in this section record the run **as it was
measured**, before the fix; they are left as-run rather than retconned.

Existing hand-filled fixtures are undisturbed: against the filled Catalog set the new composer
leaves `generate` at exit 0 and `repair` reporting `Nothing to repair`.

**One residual limitation, accepted deliberately.** OpenAPI 3.0's *composition* idiom —
`allOf: [{$ref: Base}, {type: object, properties: {…}}]` — has two non-null branches, so it is
ambiguous under the rule above and still composes to one opaque sentinel. Resolving it properly
means genuinely merging the branches' properties, not picking one, which is a different
operation from selecting a nullable union's single real branch. None of the three sample specs
uses it, so it is recorded here rather than guessed at; it is the natural follow-up if 3.0-style
composition shows up in a real document.

### F7 — the generated suite is not idempotent against a persistent store · **closed in v1-b**

> **Closed.** See "v1-b acceptance run — the suite runs twice" below, including a negative
> control that reproduces this exact finding on demand (same suite, same database, fixture
> removed) and independent proof that cleanup — not a test — is what removes the seeded row.
> The failure recorded immediately below is preserved as the original v1-a evidence this finding
> was opened on; it is what the fix is measured against, not a live description of current
> behaviour.

Running the Catalog suite a second time against the same database, changing nothing:

```
Failed!  - Failed: 3, Passed: 6, Skipped: 0, Total: 9 - Catalog.ApiTests.dll (net10.0)
```

```
POST http://localhost:5081/api/categories → expected 201, got 409 (12ms)
Body: {"title":"A category named 'Accessories' already exists.","status":409}

POST http://localhost:5081/api/products → expected 201, got 409 (3ms)
Body: {"title":"A product with SKU 'ACC-0100' already exists.","status":409}
```

The third failure was `DeleteApiCategoriesId_Contract`, whose target the first run had already
deleted. The 9-of-9 above is therefore **9 of 9 on a freshly seeded database** — stated plainly
because the number is otherwise misleading.

This is inherent to literal fixture values plus a stateful API, not a coding error. What
matters is how much of it v1-a can already solve, which was measured rather than assumed:

- **`{{runId}}` fixes the free-form case.** Changing the category name to
  `"Accessories-{{runId}}"` and running the same test twice in a row passed both times.
- **It cannot fix a format-constrained unique field.** The SKU must match `^[A-Z]{3}-[0-9]{4}$`;
  no run id fits that pattern.
- **It cannot fix deleting a seeded row.** Nothing in v1-a creates the row to delete.

The designed answer to the remaining two is `{{fixture:…}}` with `IAssemblyFixture`, deferred to
**v1-b**, which now has a measured justification rather than a predicted one. Until then the
honest guidance is: a generated suite expects a reset database per run, and adopters should use
`{{runId}}` wherever the uniqueness constraint is free-form. That guidance is now written down,
with this second-run result as its evidence, under getting-started Phase 5.

### F8 — `ITestTokenProvider` has no consumers · **closed in v1-c**

> **Closed.** `AuthHandler` (`src/InTest.Runtime/Neutral/AuthHandler.cs`, `ea2b979`) is the
> consumer this finding was waiting on: a `DelegatingHandler` attached to `InTestClients.Api`
> that calls `ITestTokenProvider.GetTokenAsync` for the ambient identity and sets
> `Authorization`. `TestPlanBuilder` (`1d285c2`) now emits a no-token 401 case and a wrong-scope
> 403 case for every operation that declares `security`, selecting identities by slot
> (`IdentitySlot`), never by name. Proven over the wire, not just at the unit level, by
> `GeneratedSuiteExecutionTests.AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass`
> (`tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`): a generated suite against a
> secured stub receives real 401, 403 and 200 responses for its three cases, all three requests
> reach the stub, and the run exits 0. This is the same negative-control shape this finding was
> originally opened with, below — restoring the registration returns a suite from uniformly 401
> to passing — now exercised by the generated tests themselves instead of ~40
> lines of hand-written scaffold code. The scaffold's own `Register` doc comment (`ea2b979`) and
> getting-started Phase 3 (this document's own edit) were updated alongside, so the extension
> point an adopter reads points at a handler that actually runs, and the stand-in
> `BearerTokenHandler` it used to show is gone rather than merely corrected.
> The finding text below is preserved as the original v1-a evidence this finding was opened on —
> including its description of what getting-started Phase 3 said and its closing line that the
> interface "still has no consumers" — and is not a description of current behaviour. Phase 3 no
> longer opens with a warning that nothing calls `GetTokenAsync`, because Task 7 of the v1-c plan
> deleted that warning and the hand-written stand-in it introduced; what Phase 3 shows today is
> the `ITestTokenProvider` implementation itself, the interface's only remaining consumer-facing
> content.

The scaffold's `TestStartup.cs` says "Add configuration providers and an ITestTokenProvider
implementation here", and getting-started Phase 3 tells adopters to implement it. Nothing calls
it. Every reference to the interface in `src/`:

```
src/InTest.Cli/Commands/InitCommand.cs:113:  /// ITestTokenProvider implementation here.</summary>
src/InTest.Runtime/Neutral/ITestTokenProvider.cs:7:   public interface ITestTokenProvider
src/InTest.Runtime/Neutral/StaticTokenProvider.cs:4:  public sealed class StaticTokenProvider(…) : ITestTokenProvider
src/InTest.Runtime/Neutral/StaticTokenProvider.cs:15:  "Implement ITestTokenProvider with more than one identity…"
```

`GetTokenAsync` is declared and implemented, and called from nowhere. The generated template
sends `Client.SendAsync(request, …)` with no `Authorization` header, so **implementing the
interface has no effect on any generated request**.

Every Orders operation declares `security`. **Measured as a negative control** — the same suite,
same fixtures, same live server, with only the handler registration commented out:

```
GET    http://localhost:5082/api/customers      → expected 200, got 401 (3ms)
POST   http://localhost:5082/api/customers      → expected 201, got 401 (3ms)
GET    http://localhost:5082/api/orders         → expected 200, got 401 (1ms)
POST   http://localhost:5082/api/orders         → expected 201, got 401 (1ms)
DELETE http://localhost:5082/api/orders/dddddddd-…  → expected 204, got 401 (1ms)
…
Failed!  - Failed: 7, Passed: 0, Skipped: 0, Total: 7 - Orders.ApiTests.dll (net10.0)
```

Restoring the registration returns it to 7 of 7. So the entire Orders result rests on ~40 lines
of hand-written `DelegatingHandler` in `TestStartup.Register`. That handler is legitimate
team-owned code, but the adopter has no way to know it is required: the documented extension
point is a dead end, and the failure mode is a uniformly 401 suite.

Auth *tests* are correctly v1-c. **Reaching a secured endpoint at all is not an auth test** —
it is the precondition for every other test on a secured API, and v1-a generates suites for
such APIs today.

Documented rather than left as a trap, in both places an adopter looks. getting-started Phase 3
now opens with the fact that nothing calls `GetTokenAsync`, and shows the `DelegatingHandler`
that does work. More importantly the **scaffold itself** was fixed (`40fd2cb`, `32e23a6`): the
`Register` doc comment in every generated `TestStartup.cs` used to say "add … an
`ITestTokenProvider` implementation here", which is the version an adopter actually reads,
sitting in their own file. It now names the `DelegatingHandler` on `InTestClients.Api` and is
honest that `ITestTokenProvider` is not wired up yet.

That is guarded by a test in the same shape as the one next to it, whose message —
*a scaffold must not teach a dead API* — is exactly the principle this violated.
**Negative control performed**: restoring the old comment makes it fail with that message.

The interface still has no consumers. Closing that is v1-c's job; this only stops the scaffold
from pointing adopters at it.

## v1-a actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | F6 — navigate `oneOf`/`anyOf`/`allOf` in `ComposeFromSchema`, choosing the single non-null branch | v1-a | **Closed** — `8d0367a` + `6952aeb`; suite 226 → 234, including a negative-controlled guard on the check's ordering |
| 2 | F8 — stop the scaffold and the docs telling adopters to register an `ITestTokenProvider` that nothing calls; point both at the `DelegatingHandler` that works today | v1-a | **Closed** — getting-started Phase 3, plus `40fd2cb` + `32e23a6` fixing the generated `TestStartup.cs` comment, guarded by a negative-controlled test |
| 3 | F8 — actually consume `ITestTokenProvider` from the generated template, so the documented extension point stops being a dead end | v1-c | Open |
| 4 | F7 — document that a generated suite assumes a reset environment, and that `{{runId}}` is the v1-a tool for free-form uniqueness | v1-a docs | **Closed** — getting-started Phase 5 |
| 5 | F7 — `{{fixture:…}}` / `IAssemblyFixture`, so create-then-delete and constrained-unique values stop depending on a reset database | v1-b | **Closed** — see the v1-b acceptance run below |
| 6 | `intest survey` should predict from **total request-body leaf properties + path parameters**, not operation count and not `required` count | v1-d | Open, input recorded above |
| 7 | Merge `allOf` composition (`[{$ref: Base}, {…}]`) rather than treating it as an ambiguous union | when a real spec needs it | Open, recorded under F6 |

---

# v1-b acceptance run — the suite runs twice

**Date:** 2026-08-19 (UTC; local machine date 2026-08-18, corrected here to match the run-id
timestamps cited as evidence throughout this section) · **Commit:** `f07ce4c` + this commit
**Task:** Plan v1-b Task 8 — the verdict task. Tasks 1–7 (the fixture lifecycle: `IAssemblyFixture`,
`FixtureGraph`, `FixtureRunner`, `TokenResolver`'s `{{fixture:…}}`, cleanup drain on
`AssemblyCleanup`) were all green in isolation before this run started. This is the only task
that proves they compose into what F7 actually needs: a suite that survives a second run against
the database the first run left behind.

Unit suite before the run: **313 passing, 0 failing** — Architecture 2, Cli 141, Runtime 161,
Golden 9.

## Results

| Sample | Runs | Result |
|---|---|---|
| `Catalog.Api`, closed suite (`CatalogSeedFixture` registered) | 2, sequential, same database | **9 of 9 both times** |
| `Catalog.Api`, negative control (no fixture, literal values) | 2, sequential, same database | 9 of 9, then **6 of 9** — reproduces F7 exactly |
| `Catalog.Api`, drain-isolation (fixture forced to throw) | 1 | 0 of 9 pass (all abort in `AssemblyInitialize`, by design) — seeded row still removed |
| `Orders.Api` | 1 | **7 of 7**, unchanged from v1-a |
| `Inventory.Api` | 1 | **6 of 6**, unchanged from v1-a |

## What was built

A fresh `Catalog.ApiTests` suite, scaffolded outside the repository the same way v1-a's was
(`intest init` → `intest generate` → `intest fixtures repair` → fill sentinels → `intest
generate` → `dotnet test`, `InTest.Runtime` swapped from `PackageReference` to `ProjectReference`
since it is not published — the same substitution `GeneratedSuiteExecutionTests` makes).

One addition beyond v1-a: `CatalogSeedFixture`, an `IAssemblyFixture` registered in
`TestStartup.cs` the way an adopter would (`services.AddSingleton<IAssemblyFixture,
CatalogSeedFixture>();`, replacing the scaffold's placeholder comment). Each run it:

1. **Creates a category** via a live `POST /api/categories`, with a name made unique by
   `Guid.NewGuid()` (not `{{runId}}` — this runs as plain C#, before any token is resolved).
   Publishes `seededCategory.id` and registers `OnCleanup` to `DELETE` it, tolerating a 404 —
   `DeleteApiCategoriesId_Contract` may already have deleted this exact row as part of the same
   run.
2. **Creates a product** via a live `POST /api/products`, with a SKU generated to match
   `^[A-Z]{3}-[0-9]{4}$` (three letters and four digits derived from `Guid.NewGuid().ToByteArray()`
   — `{{runId}}` cannot satisfy that pattern, which is the entire reason this fixture exists).
   Publishes `seededProduct.id`. **No cleanup is registered for it** — `ProductsController` has
   no `DELETE` endpoint by design (products are deactivated, never removed), so nothing exists to
   undo the call. Because the SKU is generated fresh every run, this does not collide with the
   next run, but it does leave one more row behind, permanently — see "What 'closed' does not
   claim" below.
3. **Generates a second, independent SKU** (`newProduct.sku`), published for the suite's own
   `POST /api/products` test body — it has to differ from the one used in step 2, or the very
   first run would 409 against the fixture's own seed product.

Fixture files were pointed at the published keys deliberately, not uniformly:
`get_api_categories_id.json` was left on the stable, never-deleted seed row (`22222222-…`,
"Software") rather than the fixture's own category, so the read and delete tests do not share a
target MSTest gives no ordering guarantee between. `delete_api_categories_id.json` alone points
at `{{fixture:seededCategory.id}}`; `get_api_products_id.json`,
`get_api_products_id_tags.json` and `put_api_products_id.json` all point at
`{{fixture:seededProduct.id}}`; `post_api_products.json`'s `sku` points at
`{{fixture:newProduct.sku}}`; `post_api_categories.json`'s `name` uses `Accessories-{{runId}}`,
the free-form case v1-a already closed.

Source: `CatalogSeedFixture.cs` and `fixtures/*.json` in the scratch project (not committed —
outside the repository, same as v1-a's). Inlined below in full — this is the load-bearing
artifact the entire closure rests on, and the SKU generator in particular is the crux of the
format-constrained claim, so it is reproduced rather than merely described:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InTest.Runtime;

namespace Catalog.ApiTests;

public sealed class CatalogSeedFixture(IHttpClientFactory httpClientFactory) : IAssemblyFixture
{
    // Fixed seed category (CatalogDbContext.SeedAsync) — stable across every run, never
    // deleted by any generated test, safe to reference directly rather than via a fixture.
    private static readonly Guid HardwareCategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Type[] DependsOn { get; } = [];
    public string[] AppliesTo { get; } = []; // every profile

    public async Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(InTestClients.Api);

        // 1. A category this run owns, so DeleteApiCategoriesId_Contract has a target that
        // still exists on every run. Guid.NewGuid() keeps the name unique per run without
        // needing the {{runId}} token machinery — this runs as plain C#, before any token is
        // resolved.
        var categoryName = $"InTest-Seed-{Guid.NewGuid():N}";
        using var categoryResponse = await client.PostAsJsonAsync(
            "/api/categories", new { name = categoryName }, ct);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var categoryId = category.GetProperty("id").GetString()!;
        ctx.Publish("seededCategory.id", categoryId);

        ctx.OnCleanup(async () =>
        {
            using var response = await client.DeleteAsync($"/api/categories/{categoryId}", ct);
            // Tolerate 404: DeleteApiCategoriesId_Contract may already have deleted this exact
            // row as part of the run it was seeded for. Anything else is a real cleanup failure.
            if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
            }
        });

        // 2. A product this run owns, with its own run-scoped SKU, so GetApiProductsId_Contract,
        // GetApiProductsIdTags_Contract and PutApiProductsId_Contract all have a stable target
        // via {{fixture:seededProduct.id}} instead of a fixed seed row.
        var seededSku = GenerateSku();
        using var productResponse = await client.PostAsJsonAsync("/api/products", new
        {
            sku = seededSku,
            name = $"InTest Seed Product {seededSku}",
            price = 9.99m,
            stockQuantity = 1,
            categoryId = HardwareCategoryId
        }, ct);
        productResponse.EnsureSuccessStatusCode();
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var productId = product.GetProperty("id").GetString()!;
        ctx.Publish("seededProduct.id", productId);

        // No cleanup registered for the product: ProductsController deliberately has no DELETE
        // endpoint (products are deactivated, never removed — see its own doc comment), so
        // nothing exists to undo this HTTP call. Because the SKU is freshly generated every
        // run, this leaves one more row behind per run — see "What 'closed' does not claim".

        // 3. A second, independently generated SKU for the suite's own POST /api/products test
        // body. Must differ from the one used above, or the very first run would 409 against
        // the seed product this fixture just created.
        ctx.Publish("newProduct.sku", GenerateSku());
    }

    /// <summary>Generates a value matching <c>^[A-Z]{3}-[0-9]{4}$</c> from fresh randomness, so
    /// two calls in the same run are independent of each other and of any previous run.</summary>
    private static string GenerateSku()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var letters = new char[3];
        for (var i = 0; i < 3; i++)
        {
            letters[i] = (char)('A' + (bytes[i] % 26));
        }
        var number = BitConverter.ToUInt16(bytes, 3) % 10000;
        return $"{new string(letters)}-{number:D4}";
    }
}
```

Two more copies of this same scaffold were built for the additional evidence further below:
`catalog-suite-negative` (identical generation, no `IAssemblyFixture` registered, static
literal fixture values) and `catalog-suite-drainproof` (identical to the closed suite, with
`CatalogSeedFixture` edited to throw immediately after registering the category's `OnCleanup`).
Neither is committed, same as the closed suite.

## The suite runs twice, sequentially

`samples/Catalog.Api` was started fresh for this run (its `catalog.db` deleted first, so the
first run really is against a freshly seeded database — the same comparison basis v1-a used), with
the port explicit rather than left to the ASP.NET default (see F9):

```bash
rm -f samples/Catalog.Api/bin/Debug/net10.0/catalog.db*
ASPNETCORE_URLS="http://localhost:5081" dotnet run --project samples/Catalog.Api --no-build
```

Both `dotnet test` invocations below ran against the **same, never-restarted** API process and
the **same, never-reset** `catalog.db`, one after the other — this proves sequential
repeatability, not concurrent (see "What 'closed' does not claim" below). Pasted directly from
the terminal, not summarised.

Run 1:

```
Test run for C:\…\catalog-suite\bin\Debug\net10.0\Catalog.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\catalog-suite\bin\Debug\net10.0\Catalog.ApiTests.dll
All fixtures resolved cleanly.
  Passed DeleteApiCategoriesId_Contract [75 ms]
  Passed GetApiCategoriesId_Contract [33 ms]
  Passed GetApiCategories_Contract [22 ms]
  Passed PostApiCategories_Contract [8 ms]
  Passed GetApiProductsIdTags_Contract [71 ms]
  Passed GetApiProductsId_Contract [10 ms]
  Passed GetApiProducts_Contract [72 ms]
  Passed PostApiProducts_Contract [9 ms]
  Passed PutApiProductsId_Contract [24 ms]
  Standard Output Messages:


 TestContext Messages:
 InTest fixture cleanup: drained 1 action(s).



Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 4.6315 Seconds
```

Run 2, same database, nothing reset in between:

```
Test run for C:\…\catalog-suite\bin\Debug\net10.0\Catalog.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\catalog-suite\bin\Debug\net10.0\Catalog.ApiTests.dll
All fixtures resolved cleanly.
  Passed DeleteApiCategoriesId_Contract [16 ms]
  Passed GetApiCategoriesId_Contract [20 ms]
  Passed GetApiCategories_Contract [6 ms]
  Passed PostApiCategories_Contract [6 ms]
  Passed GetApiProductsIdTags_Contract [13 ms]
  Passed GetApiProductsId_Contract [6 ms]
  Passed GetApiProducts_Contract [14 ms]
  Passed PostApiProducts_Contract [8 ms]
  Passed PutApiProductsId_Contract [4 ms]
  Standard Output Messages:


 TestContext Messages:
 InTest fixture cleanup: drained 1 action(s).



Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 3.9209 Seconds
```

**9 of 9, both times, same database.** The `POST /api/categories → 409`, `POST /api/products →
409`, and `DeleteApiCategoriesId_Contract` 404-on-an-already-deleted-target failures F7 recorded
do not reproduce. `--logger "console;verbosity=detailed"` was used throughout, per this task's
own note that a passing `[AssemblyInitialize]`'s output reaches no sink under VSTest +
MSTest.TestAdapter 4.3.3 on .NET 10 — `AssemblyCleanup`'s "drained 1 action(s)" line, which does
reach the console, is exactly the corroborating detail that line is there for.

### The negative control — the fixture is the only variable that can be varied independently

Two runs passing is not, by itself, proof the fixture is *why*. A second copy of the identical
generated suite was built — same spec, same generation — with two changes, not one:
`CatalogSeedFixture` was left unregistered, and `fixtures/*.json` was hand-filled with the
literal, non-unique values F7's original reproduction used (`post_api_categories.json`'s `name`
is the literal string `"Accessories"`, no `{{runId}}`; `post_api_products.json`'s `sku` is the
literal string `"ACC-0100"`; `delete_api_categories_id.json` targets `33333333-…`, the fixed seed
"Deprecated" category, which only one `DELETE` can ever succeed against). The second change is
not independent of the first: `{{fixture:seededCategory.id}}` and `{{fixture:newProduct.sku}}`
cannot resolve to anything without a registered fixture publishing those keys — `TokenResolver`
throws `FixtureResolutionException` before any request is sent — so removing the fixture forces
the literal-value change as its only working substitute. Together they are one change with two
visible edits, not two independent ones; a design that could vary fixture-presence alone while
holding the fixture-file contents fixed does not exist for this suite. Run against the **same
live `samples/Catalog.Api` process and the same `catalog.db`** the closed suite above just ran
against — same machine, same database, same generated suite otherwise.

Run 1:

```
Test run for C:\…\catalog-suite-negative\bin\Debug\net10.0\Catalog.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\catalog-suite-negative\bin\Debug\net10.0\Catalog.ApiTests.dll
All fixtures resolved cleanly.
  Passed DeleteApiCategoriesId_Contract [25 ms]
  Passed GetApiCategoriesId_Contract [20 ms]
  Passed GetApiCategories_Contract [5 ms]
  Passed PostApiCategories_Contract [5 ms]
  Passed GetApiProductsIdTags_Contract [4 ms]
  Passed GetApiProductsId_Contract [11 ms]
  Passed GetApiProducts_Contract [8 ms]
  Passed PostApiProducts_Contract [4 ms]
  Passed PutApiProductsId_Contract [2 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 4.6371 Seconds
```

Run 2, same database, nothing reset in between:

```
Test run for C:\…\catalog-suite-negative\bin\Debug\net10.0\Catalog.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\catalog-suite-negative\bin\Debug\net10.0\Catalog.ApiTests.dll
All fixtures resolved cleanly.
  Failed DeleteApiCategoriesId_Contract [54 ms]
  Error Message:
   Test method Catalog.ApiTests.CategoriesTests.DeleteApiCategoriesId_Contract threw exception:
InTest.Runtime.ContractAssertionException: DELETE http://localhost:5081/api/categories/33333333-3333-3333-3333-333333333333 → expected 204, got 404 (3ms)
Body: {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,…}

  Passed GetApiCategoriesId_Contract [24 ms]
  Passed GetApiCategories_Contract [7 ms]
  Failed PostApiCategories_Contract [5 ms]
  Error Message:
   Test method Catalog.ApiTests.CategoriesTests.PostApiCategories_Contract threw exception:
InTest.Runtime.ContractAssertionException: POST http://localhost:5081/api/categories → expected 201, got 409 (3ms)
Body: {"title":"A category named 'Accessories' already exists.","status":409}

  Passed GetApiProductsIdTags_Contract [3 ms]
  Passed GetApiProductsId_Contract [8 ms]
  Passed GetApiProducts_Contract [8 ms]
  Failed PostApiProducts_Contract [2 ms]
  Error Message:
   Test method Catalog.ApiTests.ProductsTests.PostApiProducts_Contract threw exception:
InTest.Runtime.ContractAssertionException: POST http://localhost:5081/api/products → expected 201, got 409 (1ms)
Body: {"title":"A product with SKU 'ACC-0100' already exists.","status":409}

  Passed PutApiProductsId_Contract [2 ms]

Test Run Failed.
Total tests: 9
     Passed: 6
     Failed: 3
 Total time: 3.9290 Seconds
```

**6 of 9 — the exact three operations F7 named, with the identical error strings**
(`"A category named 'Accessories' already exists."`, `"A product with SKU 'ACC-0100' already
exists."`, and the `DELETE` returning 404 on a row the first run already removed). Same machine,
same database, same generated suite as the 9-of-9/9-of-9 pair above; the bundled change described
above — fixture unregistered, its published tokens necessarily replaced with the literal values
they used to resolve to — is the only thing that differs. That is causation for the bundle, not
merely correlation, and it is defensible precisely because the bundle could not be split further:
it is not a claim that the fixture *registration line alone*, independent of what the fixture
files say, is what flips the result. This is what makes the closure above checkable by a skeptic
rather than merely asserted.

## Cleanup ran, and the drain (not a test) is what did it

The two runs above prove cleanup happens *somewhere*: after both, the live API was queried
directly rather than trusting the "drained 1 action(s)" line alone.

```
GET http://localhost:5081/api/categories
[
  {"id":"34b6a730-…","name":"Accessories-tjayo-20260819T003450Z-dba63a39","notes":null},
  {"id":"0bb9081c-…","name":"Accessories-tjayo-20260819T003511Z-eb2252d2","notes":null},
  {"id":"33333333-…","name":"Deprecated","notes":"Unused, safe to delete"},
  {"id":"11111111-…","name":"Hardware","notes":"Physical goods"},
  {"id":"22222222-…","name":"Software","notes":null}
]
```

Five categories: the three fixed seed rows, and the two `Accessories-{{runId}}` categories
`PostApiCategories_Contract` created — one per run, neither ever meant to be deleted. **Zero
`InTest-Seed-*` categories remain.**

That query alone under-proves the claim, and the gap is worth naming rather than glossing:
`delete_api_categories_id.json` always targets the fixture's own category, so
`DeleteApiCategoriesId_Contract` deletes it on every run before `OnCleanup` ever runs — `OnCleanup`
always hits an already-tolerated 404. The category's absence is consistent with "the drain
deleted it" and equally consistent with "the test did, and the drain found nothing left to do."
The query above cannot tell those apart.

**A third run isolates the drain from every test.** A copy of the same suite had
`CatalogSeedFixture` edited to throw immediately after registering the category's `OnCleanup`,
before creating the product or publishing anything else — `FixtureRunner.RunAsync`'s own
documented behaviour is to drain whatever cleanup is already registered before rethrowing, so
this exercises that path, not `AssemblyCleanup`'s. `AssemblyInitialize` then fails, which aborts
every test before its body runs — no test method in this run ever calls the live API at all:

```
Test run for C:\…\catalog-suite-drainproof\bin\Debug\net10.0\Catalog.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\catalog-suite-drainproof\bin\Debug\net10.0\Catalog.ApiTests.dll
  Failed DeleteApiCategoriesId_Contract
  Error Message:
   Assembly Initialization method Catalog.ApiTests.TestStartup.AssemblyInit threw exception. InTest.Runtime.FixtureLifecycleException: Fixture 'Catalog.ApiTests.CatalogSeedFixture' failed during InitializeAsync: Forced failure for the v1-b drain-proof experiment: no test should run after this.. Fix the underlying error in that fixture; later fixtures were not run because they may depend on the state this one was building.. Aborting test execution.
  [ …and the same "Assembly Initialization method … threw exception" failure repeated for the
  other 8 tests — every one aborted before its body ran, none of them touched the live API ]

Test Run Failed.
Total tests: 9
     Failed: 9
 Total time: 4.4687 Seconds
```

Categories immediately before this run and immediately after — note this is a *different* set of
five than the listing above: the negative control (previous section) ran in between and left its
own mark on the same database — `"Deprecated"` is gone (its `DELETE` succeeded on the negative
control's first run and, being a fixed seed row, could not be recreated) and a literal
`"Accessories"` row now exists (created by that same first run) alongside the two
`Accessories-{{runId}}` rows the closed suite's two runs created earlier. None of that is
`CatalogSeedFixture`'s doing; it is exactly the state the negative control's own transcript above
predicts:

```
BEFORE: [{"name":"Accessories",…},{"name":"Accessories-…dba63a39",…},{"name":"Accessories-…eb2252d2",…},{"name":"Hardware",…},{"name":"Software",…}]
AFTER:  [{"name":"Accessories",…},{"name":"Accessories-…dba63a39",…},{"name":"Accessories-…eb2252d2",…},{"name":"Hardware",…},{"name":"Software",…}]
```

Identical, five rows both times — no `InTest-Seed-*` row before, none after. The category this
run's fixture created and immediately threw on was created, then removed, entirely inside
`AssemblyInitialize`, before a single `[TestMethod]` executed. No `DeleteApiCategoriesId_Contract`
ran — it is in the failed list above for the same reason every other test is, `AssemblyInitialize`
never finished — so nothing but the drain could have removed that row. This is what makes the
"cleanup actually ran" claim for the ordinary two runs above credible rather than merely
plausible: the same mechanism, exercised in isolation, demonstrably deletes.

(No "drained N action(s)" line appears in this run's console output. That line is written only by
`TestHost.CleanupAsync`'s own drain in `[AssemblyCleanup]`, which still runs unconditionally
afterward but finds nothing left — `FixtureRunner.RunAsync`'s catch block already drained the
context, synchronously, before rethrowing — so its absence here is expected. But "consistent with
the runtime" is not, on its own, proof of anything: that same absence is equally consistent with
`[AssemblyCleanup]` simply never running after a failed `[AssemblyInitialize]`, which would be a
much bigger problem than a quiet log line. That question is checkable independently of this run,
so it was checked directly rather than left as an inference. A minimal MSTest 4.3.3/net10.0
project — no InTest code at all — with a throwing `[AssemblyInitialize]` and an
`[AssemblyCleanup]` that only writes a marker file:

```csharp
[AssemblyInitialize]
public static void AssemblyInit(TestContext context) =>
    throw new InvalidOperationException("Forced failure to probe whether AssemblyCleanup still runs.");

[AssemblyCleanup]
public static void AssemblyClean() => File.WriteAllText(MarkerPath, "ran");
```

```
Failed DoesNothing
  Error Message:
   Assembly Initialization method AssemblyCleanupProbe.TestStartup.AssemblyInit threw exception. System.InvalidOperationException: Forced failure to probe whether AssemblyCleanup still runs.. Aborting test execution.

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 31 ms - AssemblyCleanupProbe.dll (net10.0)
```

```
$ cat bin/Debug/net10.0/cleanup-ran.marker
ran
```

The marker was written. `[AssemblyCleanup]` does run after a failed `[AssemblyInitialize]` under
this project's actual runner, independent of anything InTest does — which is what makes "this run's
absent log line is merely the other, already-documented drain path" a safe reading rather than a
hopeful one. Two different, both-documented drain paths; this run exercises the other one.)

Products, queried with `X-Include-Inactive: true` — the plain endpoint hides `SPR-0002`
(`IsActive: false`, `ProductsController.cs` line 31) and returns only 5 of the 6 rows that exist —
show six rows after the two main runs: the two fixed seed products (`WGT-0001`, `SPR-0002`), and
four created across the two runs — one `CatalogSeedFixture`-seeded product per run plus one
`PostApiProducts_Contract`-created product per run, each with its own generated SKU (`GVW-3966`,
`NNF-9731`, `XDU-2311`, `YVR-3354` — all four distinct, confirming the SKU generator did not
collide with itself, the fixed seed data, or across runs). No products are deleted, by design —
see above.

```
GET http://localhost:5081/api/products?pageSize=100
{"items":[…5 active rows…],"totalCount":5,…}

GET http://localhost:5081/api/products?pageSize=100  (header: X-Include-Inactive: true)
{"items":[…6 rows, SPR-0002 now included…],"totalCount":6,…}
```

## Orders and Inventory

Both regenerated and run live, single run each, using the same fixed seed data v1-a used (no new
`IAssemblyFixture` was written for either — that was not this task's scope; Catalog is where F7
lived).

```
Test run for C:\…\inventory-suite\bin\Debug\net10.0\Inventory.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\inventory-suite\bin\Debug\net10.0\Inventory.ApiTests.dll
All fixtures resolved cleanly.
  Passed StockAdjust_Contract [196 ms]
  Passed StockDelete_Contract [93 ms]
  Passed StockGetAll_Contract [23 ms]
  Passed StockGetBySku_Contract [13 ms]
  Passed WarehousesGetAll_Contract [19 ms]
  Passed WarehousesGetById_Contract [12 ms]

Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 4.8669 Seconds
```

```
Test run for C:\…\orders-suite\bin\Debug\net10.0\Orders.ApiTests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
C:\…\orders-suite\bin\Debug\net10.0\Orders.ApiTests.dll
All fixtures resolved cleanly.
  Passed GetApiCustomersId_Contract [30 ms]
  Passed GetApiCustomers_Contract [5 ms]
  Passed PostApiCustomers_Contract [9 ms]
  Passed DeleteApiOrdersId_Contract [3 ms]
  Passed GetApiOrdersId_Contract [5 ms]
  Passed GetApiOrders_Contract [6 ms]
  Passed PostApiOrders_Contract [13 ms]

Test Run Successful.
Total tests: 7
     Passed: 7
 Total time: 3.8746 Seconds
```

**Inventory 6 of 6, Orders 7 of 7 — unchanged from v1-a.** Neither number moved. Orders needed
`samples/Identity.Server`, started fresh for this run (not reused from a prior session), which is
what let the Duende trial-mode warning finally be observed — see "Carried forward" at the end of
this document.

## F7 verdict: closed

Catalog runs 9 of 9 against a freshly seeded database and 9 of 9 again against the same,
unreset database — the exact reproduction F7 recorded, now passing both times. Three pieces of
evidence back that, not one: the two-run pass itself; a negative control that reproduces F7's
exact 6-of-9 failure, with identical error strings, on the identical suite and database with the
fixture unregistered and its published tokens replaced by the literal values they used to
resolve to (causation for that bundle, not correlation — see the negative control's own section
for why it cannot be split any finer); and a drain-isolation run that removes the fixture-created
row with zero test code executed, proving the drain — not `DeleteApiCategoriesId_Contract` — is
what deletes it. `{{fixture:…}}` / `IAssemblyFixture` (v1-a action 5, above) is closed.

**What "closed" does not claim.**

- This closes the two cases F7 named — a format-constrained unique field, and deleting a seeded
  row — for a suite that adopts `IAssemblyFixture`. It does not claim every possible seeding
  shape is covered (composite unique constraints across two fields, for instance, were not
  exercised).
- It does not claim cleanup is guaranteed — §14's "best effort, not on a crash" caveat is
  unchanged and untested by this run (see "Carried forward").
- **This proves sequential repeatability only, not concurrent.** Borrowing getting-started's own
  wording verbatim so the two documents agree: "This does not make a suite runnable twice
  *concurrently*. Two runs seeding at the same time still collide on the same unique constraints
  — cross-process coordination is not solved at this layer (§11). What this buys is sequential
  repeatability: run, then run again, without hand-editing fixtures or resetting the environment
  in between." Both `dotnet test` invocations above ran one after the other, never
  overlapping — that is the only shape this run, or the plan's Task 8/8a, was ever asked to prove.
- **It does not claim the fixture's own resource usage is free.** `CatalogSeedFixture` deletes
  the category it creates but has no way to delete the product it creates (`ProductsController`
  has no `DELETE` endpoint) — every run leaves one more product row behind, permanently. Two runs
  in this acceptance left two; `n` runs leave `n`. This is not claimed to be harmless: unbounded
  per-run growth in a shared, long-lived environment is exactly the class of problem §14's
  out-of-band sweeper exists for, and this pattern — a seeding fixture creating a resource its own
  API cannot delete — was not previously named as a case that sweeper needs to cover. See
  "Carried forward" below.

## Defects found

### F9 — following `samples/README.md`'s run commands literally does not reach the documented ports · **closed in v1-c**

> **Closed.** `samples/README.md`'s "Running them" section now sets an explicit, distinct
> `ASPNETCORE_URLS` per project rather than a bare `dotnet run`. Fixed by measurement, the same
> discipline this finding itself demanded: each of the four sample projects was run alone, with
> no `ASPNETCORE_URLS` set, and each bound to `http://localhost:5000` — the ASP.NET Core
> default — confirmed from its own "Now listening on" line, not assumed from the one instance
> this finding originally measured. Starting a second one alongside the first, still with no
> port set, failed with `AddressInUse` — the collision this finding's own commands would hit the
> moment an adopter tried to run more than one at a time, which `Orders.Api` needing
> `Identity.Server` up makes the ordinary case, not an edge one. The replacement commands
> (`http://localhost:5081`–`5084`) were run concurrently and all four answered `/health/ready`
> with 200 at once.
>
> `/health/ready` alone is not sufficient evidence, since it is anonymous on every sample and
> would pass even with `Orders.Api` and `Identity.Server` unpaired — the port fix's first pass
> stopped there and a review caught it. Measured past that: a bare port swap moves where
> `Identity.Server` listens without moving where `Orders.Api` looks for it (both default to
> `https://localhost:5443` in source), and `Orders.Api` additionally 500s on a plain-HTTP
> authority while running in the `Production` hosting environment that no `launchSettings.json`
> lets it leave. The commands now also set `IdentityServer__IssuerUri` /
> `Identity__Authority` to the same address and `ASPNETCORE_ENVIRONMENT=Development` on
> `Orders.Api`. With that in place: `GET /api/orders` with no `Authorization` header returned
> `401`; a token requested from `Identity.Server`'s `/connect/token` and replayed on the same
> request returned `200` with the seeded order list. No InTest-side change; the fix is entirely
> `samples/README.md`, as this finding predicted.

```bash
dotnet run --project samples/Catalog.Api        # http://localhost:5081
```

Run exactly as written, this binds to the ASP.NET default (`http://localhost:5000` in this
environment), not `5081`. None of the four sample projects sets a port in source, an
`appsettings.json`, or a `launchSettings.json` — every acceptance run to date (v0, v1-a, and this
one) must have set `ASPNETCORE_URLS` externally to reach the documented addresses, and nothing
records that. Followed as an adopter would follow it — copy the command, run it — the README's
own worked example does not reach itself.

**Not fixed here** — samples were explicitly out of scope for what the v1-b task above could
edit; see the closure note above this section for the fix, made when v1-c next touched
`samples/`, exactly as recorded here it would be.

### F10 — Phase 3's auth `DelegatingHandler` is registered on the same client the readiness probe uses, so a token failure surfaces as a misleading readiness timeout · **closed in v1-c**

> **Closed.** `TestHost.InitializeAsync` now resolves a second named client,
> `InTestClients.Readiness`, registered with `RunIdHandler` but never `AuthHandler`
> (`RegisterInTestClients`, `232cf46`/`c672db5`), and probes readiness on it instead of
> `InTestClients.Api`. This is exactly the "obvious alternative" this finding named and
> explained was not made here — now made. Guarded two ways: `InTestClientsTests.
> ReadinessProbeDoesNotRunApiClientHandlers` proves, at the registration seam itself, that a
> handler attached to `InTestClients.Api` never runs for a readiness probe resolved from
> `InTestClients.Readiness`; `GeneratedSuiteExecutionTests.ReadinessProbeSurvivesAThrowingApiHandler`
> proves it over the wire, against a real generated-and-built suite with a throwing handler
> attached to the API client — the run's output never contains `ReadinessTimeoutException`, and
> the first *test* that hits the throwing handler (`GetStatus_Contract`) fails with the
> handler's own message, not readiness. getting-started Phase 3 was rewritten alongside (this
> document's own edit) to state the guarantee plainly, next to the auth example, rather than
> leaving it to be rediscovered.

Following getting-started Phase 3's own worked example exactly —
`services.AddHttpClient(InTestClients.Api).AddHttpMessageHandler<BearerTokenHandler>();` — attaches
the handler to the *named client `TestHost` also uses for the readiness probe* (`TestHost.
InitializeAsync` resolves `InTestClients.Api` and hands it straight to `Readiness.WaitAsync`,
before any test runs). When the identity provider is unreachable — which is exactly what happened
here first: `samples/Identity.Server`'s dev TLS certificate was not the one this machine's
`dotnet dev-certs https --trust` had trusted (see below) — the handler's own token request throws
on *every* request through that client, readiness included, even though `/health/ready` itself is
anonymous and would have answered immediately with no token at all:

```
InTest.Runtime.ReadinessTimeoutException: Service did not become ready within 120s
(last response: HttpRequestException). Probed 'http://localhost:5082/health/ready'
expecting 200, requiring 2 consecutive successes.
```

That message says nothing about a token, an identity provider, or TLS — it reads exactly like F2
and F4 (a dead or slow API), and the getting-started guidance right above it — "keep that
endpoint anonymous or readiness will fail" — describes a *different* trap (the health endpoint
requiring auth on the server side) that does not apply here. Diagnosing this took reading the raw
exception type and cross-checking it against what the handler does, not anything the message or
the docs said. A note in Phase 3, next to the `DelegatingHandler` example, naming this failure
mode would have saved that.

**The obvious alternative — give the readiness probe its own client, without the auth handler
attached — was considered, not overlooked.** F2 modelled exactly this kind of argument: name the
runtime change explicitly, then say why it was or was not made. Here: `TestHost.InitializeAsync`
resolves one client via `IHttpClientFactory.CreateClient(InTestClients.Api)` and hands that same
instance to both `Readiness.WaitAsync` and, later, the generated tests — a second, unhandlered
client for readiness alone would decouple the two, and would have made this exact failure surface
as a real readiness timeout with no auth noise in the way. It was not made here because it is a
runtime change to `TestHost`, and Task 8 is an acceptance run, not an implementation task — making
it would be exactly the kind of edit this task's own rules say to report rather than perform.
Recorded as the concrete fix candidate for whichever phase next touches `TestHost`'s readiness
wiring, rather than left as an unnamed possibility.

**Not fixed here** at the time — both the runtime change above and the docs-only note were
recorded rather than made, for the same out-of-scope reasoning as F9. See the closure note at
the top of this section for what v1-c did with both.

## Environment note, not an InTest finding: this machine had an untrusted active dev certificate

The proximate cause of F10 surfacing at all: `dotnet dev-certs https --check --trust` reported an
already-trusted certificate, but Kestrel was actually serving a *different* `CN=localhost`
certificate (three were present in the personal store) that was never added to the trusted root
store. `Invoke-WebRequest` against the discovery endpoint failed with `UntrustedRoot`; `curl -k`
(skipping validation) succeeded, confirming it was specifically a trust problem, not a
connectivity one. Re-running `dotnet dev-certs https --trust` opens an interactive Windows
confirmation dialog, which a non-interactive session cannot answer, and this task's own rules
prohibit programmatically installing a certificate into a trust store to work around that.

**Worked around without touching the trust store or any sample source**: `samples/Identity.Server`
and `samples/Orders.Api` both already read their issuer/authority from configuration
(`IdentityServer:IssuerUri`, `Identity:Authority`), so both were restarted with those set to a
plain-HTTP address (`http://localhost:5444`) instead of the default HTTPS one, and
`CatalogSeedFixture`'s sibling `BearerTokenHandler` in the Orders scratch project pointed at the
same. This is an environment quirk of this machine, not a defect in InTest or the samples — recorded
here only because it is exactly the kind of thing that costs a real adopter an afternoon the first
time they stand up `Identity.Server` locally, and nothing currently warns about it.

## v1-b actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | F7 — `{{fixture:…}}` / `IAssemblyFixture` closes both cases F7 named | v1-b | **Closed** — this run: two-run pass, negative control, drain-isolation run |
| 2 | F9 — add the missing `ASPNETCORE_URLS` (or a `launchSettings.json` per project) to `samples/README.md`'s run commands | next phase touching `samples/` | **Closed** — v1-c, see F9's closure note above |
| 3 | F10 — note in getting-started Phase 3, next to the `DelegatingHandler` example, that it shares the readiness client and a token failure there reads as a plain readiness timeout | next phase touching getting-started Phase 3 | **Closed** — v1-c, superseded: the stand-in example is gone, not annotated (see F10's closure note above and getting-started's Auth section) |
| 4 | F10 — give the readiness probe its own client, decoupled from any team-registered auth handler | next phase touching `TestHost`'s readiness wiring | **Closed** — v1-c, `InTestClients.Readiness` (see F10's closure note above) |
| 5 | The product-row leak (`CatalogSeedFixture` creates a product every run with no way to delete it) is a case §14's sweeper needs to cover explicitly, and getting-started's `IAssemblyFixture` section should say so | next phase touching getting-started's `IAssemblyFixture` section | Open |
| 6 | F8 — actually consume `ITestTokenProvider` from the generated template | v1-c | **Closed** — v1-c, see F8's closure note above |
| 7 | `intest survey` should predict from **total request-body leaf properties + path parameters** | v1-d | Open, carried from v1-a action 6, unchanged by this run |
| 8 | Merge `allOf` composition (`[{$ref: Base}, {…}]`) rather than treating it as an ambiguous union | when a real spec needs it | Open, carried from v1-a action 7, unchanged by this run |

---

## Carried forward — not covered by any run

Closed by v1-a:

- ~~Orders and Inventory were generated and compiled, but not run live.~~ Both now run live,
  7 of 7 and 6 of 6.
- ~~Operations with a request body cannot send one.~~ Closed — that was the point of v1-a.

Closed by v1-b:

- ~~The generated suite is not idempotent against a persistent store (F7).~~ Closed — Catalog
  runs 9 of 9 twice against the same, unreset database. See below.
- ~~The Duende trial-mode startup warning was not observed.~~ Observed this run, twice
  (`fail: Duende.Private.Licencing.V2.LicenseValidator[263521618]  You do not have a valid
  license key for the Duende software...`) — `samples/Identity.Server` was started fresh for
  this acceptance run rather than reusing an already-running instance.

Closed by v1-c:

- ~~No auth tests were generated in v0/v1-a/v1-b, and no fresh acceptance run against
  `samples/Orders.Api` itself had confirmed F8's golden-suite proof over the wire.~~ Closed —
  see the v1-c acceptance run below: 24 tests generated live against Orders, 401s and 3 of 7
  403s proven real (F11 records why the other 4 cannot be, against this sample's identity
  pair), and the write-scope 403s proven able to fail (F12 records the exact status the
  prediction got wrong).
- ~~Inventory had no twice-run proof — only Catalog did, from v1-b.~~ Closed — see the v1-c
  acceptance run below, `InventorySeedFixture`.

Still open, stated rather than glossed:

- **No pipeline run.** All runs were local. "In a real pipeline" remains unmet.
- **`X-Test-Run-Id` was not verified in server-side telemetry.** The header is sent, but no
  sink was configured to confirm arrival.
- **`survey`, YAML input, and variation tests** are unbuilt, so nothing about them was exercised.
- **`generate --check` and `upgrade`** were built after this acceptance run (v1-e). No run
  recorded in this document exercises either one — this file predates both and has not yet been
  extended to cover them. ~~Closed~~ — see "v1-e Task 6 acceptance run" below: both commands ran
  against live Orders, five scenarios each, all passing, plus a genuine cross-platform proof
  (`core.autocrlf=true`) neither this run nor any earlier one had exercised.
- **One sample was measured per producer.** The corpus is deliberate but small; nothing here
  says how the composer behaves on a large real-world document.
- **Cleanup was confirmed for the delete case, not the crash case.** §14 and getting-started
  both say cleanup is best-effort, not guaranteed on a crash or cancelled run — this acceptance
  run only exercised the ordinary path (`AssemblyCleanup` running to completion), not that
  failure mode.
- **`CatalogSeedFixture`'s product row is never reclaimed — every run leaves one more behind,
  permanently.** `ProductsController` has no `DELETE` endpoint, so nothing in this pattern can
  undo the `POST` that creates it. Not exercised: how many runs before that matters in a shared
  environment, and whether §14's out-of-band sweeper — designed for crash-abandoned rows, not for
  a seeding fixture's own by-design accumulation — actually covers this case or needs to be
  extended to. See "v1-b actions" above.
- **This run proves sequential repeatability only.** Both `dotnet test` invocations always ran
  one after the other, never concurrently — §11 states cross-process coordination is unsolved at
  this layer, and nothing here touches that.
- **The wrong-scope 403 case assumes the Secondary identity lacks every scope any secured
  operation needs — undocumented, and false for a "full access vs. read-only" identity pair on
  its own read-scoped operations (F11).** Neither `TestPlanBuilder` nor `ITestTokenProvider`'s
  own doc comment states this precondition; a "full access vs. read-only" split — arguably the
  most common real-world shape — silently produces 4 unprovable wrong-scope 403 tests for every
  3 provable ones on `samples/Orders.Api`. See "v1-c actions" below.
- **Task 8 Step 3's prediction table for a mis-scoped, bodyless `POST` is wrong: 415, not 400
  (F12).** `DELETE`'s prediction (404) is correct — the difference is `[ApiController]` model
  binding returning 415 for a missing `Content-Type` before validation (400's source) ever
  runs. Not yet corrected at its source, `docs/superpowers/plans/2026-08-19-intest-v1c-error-and-auth-tests.md:691`
  — decision 6 itself (plan lines 135–147) predicts only DELETE's 404 and makes no POST
  prediction. See "v1-c actions" below.
- **`InventorySeedFixture` inserts directly into `inventory.db` rather than through
  `Inventory.Api`'s own HTTP surface**, because `StockController` has no create endpoint —
  unlike `CatalogSeedFixture`, which seeds entirely over HTTP. Not exercised: whether a direct-
  write seeding pattern like this is worth naming as a supported shape in getting-started's
  `IAssemblyFixture` section, alongside the HTTP-only pattern it currently shows.

---

# v1-c acceptance run — auth tests against a live identity server

**Date:** 2026-08-19 (UTC) · **Commit:** `f09f2d5` + this commit
**Task:** v1-c plan Task 8 — the verdict task. Tasks 1–7 (`AuthHandler`, `InTestClients.Readiness`,
`TestPlanBuilder`'s declared-error and auth cases, the runtime multi-identity guard, coverage
reporting) were all green in isolation before this run started. This is the only task that proves
any of it works against a real identity server rather than a mock or the golden-suite stub.

Unit suite before the run: **394 passing, 0 failing** — Architecture 2, Cli 193, Runtime 186,
Golden 13, confirmed by running it before touching anything below. Unchanged after — this task
generates and runs suites outside the repository (per its own instructions) and edits only
documentation inside it.

## Results

| Step | What | Result |
|---|---|---|
| 1 | Two-identity `ITestTokenProvider` for Orders, against `samples/Identity.Server`'s two Duende clients | `OrdersTokenProvider` — `orders-client` (full access), `orders-readonly` (read only) |
| 2 | Orders live, both identities correctly scoped | **20 of 24** — every success, every declared-404, every no-token 401, and the 3 auth-403 cases whose operation actually needs the write scope. 4 auth-403 cases fail — **F11**, not a defect in this run's setup |
| 3 | Orders live, `orders-readonly` slot mis-scoped to request `orders-client`'s token | **17 of 24** — the 3 previously-passing write-scope 403s now fail, with the status decision 6 predicts for `DELETE` and a different one for `POST` — **F12** |
| 4 | Orders live, `Identity.Server` stopped | **7 of 24** (the 7 no-token 401s only); every other failure names `OrdersTokenProvider`, none is a readiness timeout |
| 5 | Catalog and Inventory, twice each, same unreset database | **13 of 13 twice**, **9 of 9 twice** — neither generates an auth test |

## Step 1 — the two-identity token provider

`samples/Identity.Server/Config.cs` names two Duende clients precisely for this: `orders-client`
(`orders.read orders.write`) and `orders-readonly` (`orders.read` only), sharing one secret
(`sample-secret-not-a-real-credential`). `OrdersTokenProvider` (in the scratch suite, not
committed) implements `ITestTokenProvider` with
`Identities => ["orders-client", "orders-readonly"]` — index 0 the Default slot every ordinary
case authenticates as, index 1 the Secondary slot the wrong-scope 403 case selects (decision 7) —
and requests a client-credentials token from `POST /connect/token` for whichever client id
`AuthHandler` asks for.

`Api:Audience` had to be set explicitly to `"orders-api"` in `appsettings.json`:
`TestHost.ResolveAudience` falls back to the base URL's authority (`localhost:5082`) when unset,
and Identity.Server never issues a token for that audience — every authenticated request would
401 with a provider that is otherwise entirely correct. Not a defect: `ResolveAudience`'s own doc
comment names exactly this fallback and why (v1-c Task 2 question (c)); it just has to actually be
set for a secured sample whose audience isn't its own host.

Sanity-measured before generating anything, the same way `samples/README.md` recommends:
`GET /api/orders` with no `Authorization` returned `401`; a token from `orders-client` replayed on
the same request returned `200`.

## Step 2 — Orders live, correctly scoped

Generated: **24 tests across 7 operations** — `coverage-report.json`:

```json
{
  "generated": 24, "operationsGenerated": 7,
  "declaredErrorTestsGenerated": 3, "authTestsGenerated": 14,
  "authTestsGatedOnSecondIdentity": 7
}
```

7 success + 3 declared-404 (the 3 operations with a path parameter — `delete_api_orders_id`,
`get_api_orders_id`, `get_api_customers_id`) + 14 auth (401 and 403, one pair per operation).

Run against a freshly-started `Orders.Api` (`orders.db` deleted first, so this is a genuinely
clean comparison basis, the same discipline v1-b used for Catalog):

```
Passed GetApiCustomersId_Contract [289 ms]
Failed GetApiCustomersId_Forbidden [104 ms]  → expected 403, got 404
Passed GetApiCustomersId_NotFound [10 ms]
Passed GetApiCustomersId_Unauthorized [4 ms]
Passed GetApiCustomers_Contract [20 ms]
Failed GetApiCustomers_Forbidden [6 ms]  → expected 403, got 200
Passed GetApiCustomers_Unauthorized [1 ms]
Passed PostApiCustomers_Contract [49 ms]
Passed PostApiCustomers_Forbidden [4 ms]
Passed PostApiCustomers_Unauthorized [1 ms]
Passed DeleteApiOrdersId_Contract [25 ms]
Passed DeleteApiOrdersId_Forbidden [4 ms]
Passed DeleteApiOrdersId_NotFound [4 ms]
Passed DeleteApiOrdersId_Unauthorized [1 ms]
Passed GetApiOrdersId_Contract [80 ms]
Failed GetApiOrdersId_Forbidden [7 ms]  → expected 403, got 404
Passed GetApiOrdersId_NotFound [5 ms]
Passed GetApiOrdersId_Unauthorized [1 ms]
Passed GetApiOrders_Contract [39 ms]
Failed GetApiOrders_Forbidden [6 ms]  → expected 403, got 200
Passed GetApiOrders_Unauthorized [1 ms]
Passed PostApiOrders_Contract [37 ms]
Passed PostApiOrders_Forbidden [4 ms]
Passed PostApiOrders_Unauthorized [1 ms]

Test Run Failed.
Total tests: 24
     Passed: 20
     Failed: 4
```

**The 4 failures are not a setup mistake — see F11.** Every other case passed.

**The 7 no-token 401s are not passing vacuously.** With no provider registered, every request is
anonymous and every 401 would go green while everything else went 401-red — that is exactly what
v1-a's negative control (F8, above) demonstrated. Here the evidence is the opposite shape, in the
same run: all 7 **success** cases (`*_Contract`) pass alongside the 7 401s, which is only possible
if the requests the success cases send really do carry a valid, accepted bearer token — an
anonymous run would fail every one of those 7 too. 401 and 200 both being real in the same run is
the proof; no separate run is needed to show it, which is why it is stated here rather than left
for the totals to imply.

## Step 3 — proving the write-scope 403s can fail

`OrdersTokenProvider` was edited so that requesting a token for `"orders-readonly"` requests
`orders-client`'s token instead — the Secondary slot now points at full access. `Orders.Api`
restarted fresh (`orders.db` deleted) so the comparison basis matches Step 2's.

```
Failed PostApiCustomers_Forbidden [7 ms]
 → expected 403, got 415
 Body: {"title":"Unsupported Media Type","status":415, …}

Failed DeleteApiOrdersId_Forbidden [7 ms]
 → expected 403, got 404
 Body: {"title":"Not Found","status":404, …}

Failed PostApiOrders_Forbidden [4 ms]
 → expected 403, got 415
 Body: {"title":"Unsupported Media Type","status":415, …}

Test Run Failed.
Total tests: 24
     Passed: 17
     Failed: 7
```

The 4 read-scope failures from Step 2 are unchanged (still 404/200/404/200 — mis-scoping the write
client to also be read-capable changes nothing there, since it already was read-capable). The
**new** failures are exactly the 3 previously-passing write-scope 403s —
`DeleteApiOrdersId_Forbidden`, `PostApiOrders_Forbidden`, `PostApiCustomers_Forbidden` — 17 of 24
instead of 20 of 24.

**`DeleteApiOrdersId_Forbidden` matches decision 6's prediction exactly: `expected 403, got 404`,
not `204`** — the unmatchable id (decision 6) means the request that is no longer denied still
finds no row to delete, so nothing was actually deleted by this run.

**`PostApiOrders_Forbidden` and `PostApiCustomers_Forbidden` do not match Task 8 Step 3's
prediction.** The prediction table in this plan's Task 8 Step 3 (line 691) says a mis-scoped
POST should give `expected 403, got 400` — "no body is sent". Decision 6 itself makes no POST
prediction; only DELETE's 404 is its claim. The measured status is **415, not 400** — see F12.

**The trade decision 6 makes, stated plainly:** a *passing* auth test proves the authorization
check ran and denied — `DeleteApiOrdersId_Forbidden` and the two POST cases passing in Step 2 is
real evidence Orders.Api's authorization is doing its job. A *failing* one, as here, proves only
that authorization did not deny — it cannot distinguish "authorization allowed the request" from
any other rejection downstream (routing, model binding), which is exactly why the failing status
here is 404/415 rather than a real delete or create. That is the price decision 6 pays for
guaranteeing a mis-scoped auth test is never destructive, and it costs no more to state than to
leave implied.

`OrdersTokenProvider` was reverted immediately after this run — restored to requesting
`orders-readonly`'s own token for `"orders-readonly"` — before Step 4.

## Step 4 — a dead identity server fails by name

`Identity.Server` stopped (`taskkill`); `Orders.Api` and its database left exactly as Step 3
finished. Rebuilt with the reverted provider, then run:

```
Failed GetApiCustomersId_Contract [4 s]
Error Message:
 Test method Orders.ApiTests.CustomersTests.GetApiCustomersId_Contract threw exception:
System.InvalidOperationException: OrdersTokenProvider failed to issue a token for identity
'orders-client': No connection could be made because the target machine actively refused it.
(localhost:5084) ---> System.Net.Http.HttpRequestException: …
   at Orders.ApiTests.OrdersTokenProvider.GetTokenAsync(...)
   at InTest.Runtime.AuthHandler.SendAsync(...)
```

```
Passed GetApiCustomersId_Unauthorized [8 ms]
…
Total tests: 24
     Passed: 7
     Failed: 17
```

**Every one of the 7 passes is a no-token 401** — `AuthHandler` never calls the provider at all
for the `InTestIdentities.None` sentinel, so those 7 short-circuit correctly regardless of whether
Identity.Server is reachable. **All 17 failures name `OrdersTokenProvider` and the identity it
failed for**, not `ReadinessTimeoutException` — confirmed by grep, zero occurrences anywhere in
the run's output. Each failing test took ~4s (a real TCP connection-refused round trip, not a
120-second wait), which is itself corroborating: a masqueraded readiness timeout would have taken
120s on the *first* test and never reached the rest. **F10 is closed against a real failure, not
only the golden-suite stub `ReadinessProbeSurvivesAThrowingApiHandler` already covers.**

## Step 5 — Catalog and Inventory unaffected

Neither declares `security`; `coverage-report.json` for both: `"authTestsGenerated": 0`. Both
scaffolded fresh (`ProjectReference` to `InTest.Runtime`, same substitution v1-a/v1-b/this task's
Orders suite all make), both started against a freshly-deleted `.db`, both run twice back-to-back
with nothing reset in between.

**Catalog** reused v1-b's own `CatalogSeedFixture` verbatim (this document's own v1-b section) —
this task needed the same twice-run guarantee, not a new one:

```
Run 1: Total tests: 13   Passed: 13
Run 2: Total tests: 13   Passed: 13
```

**Inventory had no assembly fixture in v1-a or v1-b — it was never run twice before.**
`StockController` exposes only `Adjust` and `Delete`, no create endpoint, so
`StockDelete_Contract`'s target (a fixed seed row, ids 1–2) would be gone by the second run
without a fixture to replace it — the same class of problem F7 named for Catalog. A new
`InventorySeedFixture` inserts a fresh `StockItem` row directly into `inventory.db` via a minimal
shadow `DbContext` (not raw SQL — letting EF write `LastCountedAt` is what guarantees
`Inventory.Api`'s own EF stack reads it back correctly) and publishes its id for
`StockDelete_Contract`; `StockAdjust_Contract` uses a fixed `delta: 1` against the permanent
seed row, safe indefinitely since it only ever increases:

```
Run 1: Total tests: 9   Passed: 9
Run 2: Total tests: 9   Passed: 9
```

Queried directly after both runs: `WGT-0001`'s `quantityOnHand` is 122 (120 + 1 per run), and only
the two original seed rows (`WGT-0001`, `SPR-0002`) remain — both fixture-seeded rows were deleted
by their own run's `StockDelete_Contract`, exactly as intended.

**v1-b's guarantee survives v1-c's changes to `TestPlanBuilder`, `TestCasePlan` and the
template.** Neither sample gained a spurious auth test, and Catalog's own repeat-run proof
(fixture-driven, not incidental) still holds byte-for-byte.

## Defects found

### F11 — the wrong-scope 403 test is generated per operation, not per required scope — 4 of 7 cannot pass against the sample's own identity pair · **closed in the F11 phase**

> **Closed.** See "F11 phase acceptance run — scope-aware 403s, reproduced independently" below.
> `RequireSecondaryIdentityLacks` (commit `0cf649a` and its predecessors on `f11-scope-aware-403`)
> now weighs each secured operation's declared scope against what the Secondary identity's
> `ITestTokenProvider.Identities` actually declares holding, and skips a `_Forbidden` case —
> with a stated reason naming the identity and the scope — only when that identity provably
> cannot receive a 403 for it. Proven live against `samples/Orders.Api` and a real Duende
> identity server, then independently reproduced from scratch by a second agent with its own
> scaffolded suite, its own token provider, its own fixtures and its own ports: both runs agree
> exactly, 20 passed / 0 failed / 4 skipped, and the 3 write-scope 403s — the cases the sample's
> identity pair can actually prove — ran and passed. A negative control (the Secondary identity's
> declared `Scopes` set to `null`, everything else held fixed) reproduces this exact finding on
> demand: 4 of those same cases fail, not skip, with the read-authorized identity's real 200s and
> 404s standing in for the 403 the test expected. The finding recorded immediately below is
> preserved as the original v1-c evidence this finding was opened on; it is what the fix is
> measured against, not a live description of current behaviour.

`TestPlanBuilder` emits a `_Forbidden` case for every operation that declares `security`
(`TestPlanBuilder.cs:197-239`), unconditionally, independent of which scope that operation's own
`security` requirement names. Decision 3 gates *whether* the case runs (on identity count) but not
*whether it can be true* — that depends on the Secondary identity actually lacking whatever scope
the specific operation needs.

`samples/Orders.Api`'s own `AuthorizeOperationFilter` (measured directly against the generated
spec) declares:

```
GET    /api/customers      → ["orders.read"]
GET    /api/customers/{id} → ["orders.read"]
GET    /api/orders         → ["orders.read"]
GET    /api/orders/{id}    → ["orders.read"]
POST   /api/customers      → ["orders.write"]
POST   /api/orders         → ["orders.write"]
DELETE /api/orders/{id}    → ["orders.write"]
```

`samples/Identity.Server/Config.cs`'s own doc comment names `orders-readonly` "used to prove write
endpoints return 403" — it was built for exactly 3 of these 7, not all 7. It genuinely has
`orders.read`, so it is not "wrong scope" for the 4 read-only operations: `GET /api/orders` and
`GET /api/customers` (no path parameter, decision 6 doesn't apply) return a real `200` with real
data; `GET /api/orders/{id}` and `GET /api/customers/{id}` (decision 6's unmatchable id) return a
real `404`. Both are the API behaving correctly under a token that genuinely has the scope it
needs — the generated test's `expected 403` is simply wrong for these 4, not the sample's fault
and not this run's setup.

This is not specific to this sample. A "full access vs. read-only" identity pair — the shape
`Config.cs` chose — is one of the most common real-world role splits, and it produces exactly this
outcome: every write-scoped operation's wrong-scope 403 is provable, every read-scoped operation's
is not, for the structural reason that a read-only identity is never "wrong scope" for a read.
`TestPlanBuilder` already reads each operation's declared scope (`TestPlanBuilder.cs:197`) — that
is spec data, not identity data. What decision 7 actually rules out is different: the CLI runs
long before any provider exists, so it can never know which scopes the Secondary identity itself
holds. Restricting the case to "write-scoped" operations isn't blocked by that — the spec's own
scope strings are sitting right there to read. The real obstacle is that nothing in the spec marks
a scope as read-like or write-like; `orders.read` and `orders.write` read that way only because
this sample named them that way. Classifying a scope as "write" from its name is a heuristic the
spec does not support, not a fact decision 7 hides.

**Not fixed here** — Task 8 is an acceptance run, not an implementation task, and this is a
generation-logic question, not a runtime one. Recorded as the concrete gap for whichever phase
next revisits `TestPlanBuilder`'s auth-case generation or `ITestTokenProvider`'s documented
contract: either state explicitly that the Secondary identity must lack every scope any secured
operation could require — not merely "some other identity" — or teach the generator to weigh each
operation's declared scope against some notion of "write-like". If it's the former, state it in
all three places that currently under-state it, not just the first two anyone reaches for:
getting-started's Auth section, `ITestTokenProvider.Identities`'s own doc comment, **and** §9's
auth table in `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`, whose
"Needs: A second identity" row carries the identical gap — miss it and a later fix leaves the spec
disagreeing with the docs it was supposed to agree with. The generator-side
option is not something decision 7 rules out; decision 7 only blocks the generator from knowing
what scopes the Secondary identity actually holds. The obstacle to the scope-name approach is that
the spec gives no principled way to tell a "write" scope from a "read" one — it would be a guess,
not a rule.

### F12 — a mis-scoped, bodyless POST 415s, not the 400 Task 8 Step 3 predicts

Task 8 Step 3's own prediction table (this plan, line 691) says: `POST /api/orders` mis-scoped
gives "`expected 403, got 400`" because "no body is sent". Decision 6 (lines 135–147) makes no
prediction for POST at all — its only claim is DELETE's 404, which held. Measured in Step 3: both
`PostApiOrders_Forbidden` and `PostApiCustomers_Forbidden` return **415 Unsupported Media Type**,
not 400.

The generated request (`Generated/OrdersTests.g.cs`) sets no `request.Content` and no
`Content-Type` header at all for an auth case — decision 6's "no body" premise is accurate. But
ASP.NET Core's `[ApiController]` model-binding pipeline returns **415**, not 400, when a request
declaring a `[FromBody]` parameter arrives with no `Content-Type` header — no input formatter can
be selected, and that failure is reported before model validation (the source of 400) ever runs.
`DELETE /api/orders/{id}` has no body parameter at all, so it never enters this path and gives
exactly the `404` decision 6 predicts — the DELETE case's prediction was correct precisely because
it doesn't touch this mechanism.

Consequential rather than merely cosmetic: an adopter reading Task 8 Step 3's prediction table
(this plan, line 691) or a future revision carrying the same text, and asserting on the literal
status `400` for a mis-scoped POST, would find their own negative-control test failing against
reality — the same class of surprise this whole task exists to catch before an adopter does.

**Not fixed here**, same reasoning as F11. The concrete fix is textual: Task 8 Step 3's prediction
table at `docs/superpowers/plans/2026-08-19-intest-v1c-error-and-auth-tests.md:691` should read
`expected 403, got 415` for a mis-scoped POST with no body, not `400`. Grepped `docs/getting-
started.md` and `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` for
`expected 403`, `got 400`, `415` and `Unsupported Media`: zero hits in either, so the wrong
prediction is not repeated there — this plan's line 691 is its only occurrence.

## v1-c actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | F8 — actually consume `ITestTokenProvider` from the generated template, proven live against `samples/Orders.Api` and a real Duende identity server, not only the golden-suite stub | v1-c | **Closed** — Steps 1–2 above |
| 2 | F10 — give the readiness probe its own client, proven by stopping the real `Identity.Server` and reading the failure, not only by the golden-suite stub | v1-c | **Closed** — Step 4 above |
| 3 | F11 — the wrong-scope 403 case assumes the Secondary identity lacks every scope any secured operation needs | `docs/superpowers/plans/2026-08-20-intest-f11-scope-aware-403.md` | **Closed** — a runtime scope guard (`RequireSecondaryIdentityLacks`), not a documented requirement. Proven live against `samples/Orders.Api` and a real Duende identity server, then independently reproduced from scratch by a second agent with its own suite, provider, fixtures and ports: both runs agree exactly — 20 passed, 0 failed, 4 skipped with a stated reason, and the 3 write-scope 403s ran and passed. See "F11 phase acceptance run" below |
| 4 | F12 — correct Task 8 Step 3's prediction table for a mis-scoped, bodyless POST from 400 to 415, at its source: `docs/superpowers/plans/2026-08-19-intest-v1c-error-and-auth-tests.md` (decision 6 itself makes no POST prediction and needs no correction) | done 2026-08-20 | **Closed** — table now reads 415, with the content-negotiation reason stated so it is not re-predicted as 400 |
| 5 | Inventory now has the same twice-run proof Catalog has had since v1-b (`InventorySeedFixture`, Step 5 above) — not previously true, closed incidentally by this task rather than a dedicated one | v1-c | **Closed** — Step 5 above |
| 6 | README.md line 80 claimed "every declared-error test (404s, 400s)" — decision 5 excludes 400 declared-error tests outright (no deterministic fixture-free trigger). Dropped the "400s" claim | found while writing this acceptance log (Task 8, commit `8df32f1`); not requested by any Task 8 step, fixed here as a one-line factual correction rather than deferred | **Closed** |


---

# F11 phase acceptance run — scope-aware 403s, reproduced independently

**Date:** 2026-08-20–21 (UTC) · **Commit:** `0cf649a`
**Task:** F11 plan Task 6 Step 1 — the verdict step for
`docs/superpowers/plans/2026-08-20-intest-f11-scope-aware-403.md`. Tasks 1–5
(`RequireSecondaryIdentityLacks`, the containment check against
`ITestTokenProvider.Identities`'s declared `Scopes`, the ordering fix ahead of
`RequireMultipleIdentities`, and the golden-suite pin added in `0cf649a`) were all green in
isolation before this run started. This is the only step that proves the guard works against a
real identity server rather than the golden-suite stub, and the only one performed twice,
independently, to rule out a single scaffold's own error.

Unit suite before the run: **434 passing, 0 failing** — 2 Architecture + 212 Cli + 205 Runtime +
15 Golden. Unchanged after — this run generates and executes suites outside the repository, per
the same discipline v1-c's Task 8 followed, and touches only documentation inside it.

## What was run

The same shape as v1-c's Task 8: `samples/Orders.Api` and `samples/Identity.Server` live, a
two-identity `ITestTokenProvider` (`orders-client`, full access; `orders-readonly`, `orders.read`
only), a generated Orders suite executed with `dotnet test`. Run once, then **independently
reproduced from scratch by a second agent** — its own scaffolded suite, its own token provider,
its own fixtures, its own ports. Both agree exactly.

## Results

**Orders suite, both identities correctly scoped:**

```
Test Run Successful.
Total tests: 24
     Passed: 20
    Skipped: 4
```

All 4 skips carry this message verbatim:

> `Assert.Inconclusive. Skipped: the secondary identity 'orders-readonly' holds orders.read,
> which this operation requires, so it cannot produce a 403. Declare different scopes on that
> identity, or leave Scopes null to run this test anyway.`

All 4 stack traces bottom out in `RequireSecondaryIdentityLacks` — **not**
`RequireMultipleIdentities`, the guard that gates a different decision (identity count, not scope
containment). The `.trx` shows `outcome="NotExecuted"` for exactly those 4.

**The three write-scope 403s ran and passed** — `PostApiCustomers_Forbidden`,
`PostApiOrders_Forbidden`, `DeleteApiOrdersId_Forbidden`, all `outcome="Passed"`,
`<Counters executed="20">`. This is the number that makes the run meaningful: a fix that skipped
all 7 wrong-scope cases would also show 0 failures.

| # | Method | Path | Scope | `_Forbidden` outcome |
|---|---|---|---|---|
| 1 | GET | `/api/customers` | `orders.read` | Skipped — `RequireSecondaryIdentityLacks` |
| 2 | GET | `/api/customers/{id}` | `orders.read` | Skipped — `RequireSecondaryIdentityLacks` |
| 3 | POST | `/api/customers` | `orders.write` | **Ran, passed** (real 403) |
| 4 | GET | `/api/orders` | `orders.read` | Skipped — `RequireSecondaryIdentityLacks` |
| 5 | GET | `/api/orders/{id}` | `orders.read` | Skipped — `RequireSecondaryIdentityLacks` |
| 6 | POST | `/api/orders` | `orders.write` | **Ran, passed** (real 403) |
| 7 | DELETE | `/api/orders/{id}` | `orders.write` | **Ran, passed** (real 403) |

This is F11's own table of 7 wrong-scope cases, with the outcome column filled in by the guard
instead of by hand: the 4 cases F11 named as structurally unprovable against this sample's
identity pair (rows 1, 2, 4, 5 — all `orders.read`) now skip with a reason instead of running and
failing; the 3 F11 named as provable (rows 3, 6, 7 — all `orders.write`) ran and passed.

## Negative control

The secondary identity's declared `TestIdentity.Scopes` set to `null`, the token request itself
unchanged, everything else held fixed:

```
Test Run Failed.
Total tests: 24
     Passed: 20
     Failed: 4
```

```
GetApiCustomersId_Forbidden → expected 403, got 404
GetApiCustomers_Forbidden   → expected 403, got 200
GetApiOrdersId_Forbidden    → expected 403, got 404
GetApiOrders_Forbidden      → expected 403, got 200
```

The 404s are structural, not a control artifact: auth cases use `Guid.NewGuid()` as the path id
(decision 6), so a read-authorized identity necessarily gets 404 there rather than 403. Restoring
the declared scopes returns the run to 20 passed / 4 skipped; the control flips in both
directions with declared `Scopes` as the only variable changed. This is the same failure F11 was
originally opened on, reproduced on demand rather than merely cited.

## Catalog and Inventory, unaffected

Neither declares `security` (`authTestsGenerated: 0` for both). Catalog **13 of 13 twice**,
Inventory **9 of 9 twice**, against an unreset store — v1-b's guarantee survives this change, as
it survived v1-c's.

## Now permanently guarded

Commit `0cf649a` extended the golden execution test so a scoped 403 case that must *run* is
pinned too, not only the ones that must skip: a regression flipping the containment check from
`All` to `Any` now fails that test. Previously such a regression would have made every scoped 403
skip while the repository's own suite stayed green — the golden suite proved the guard could skip
but not that it would ever let a provable case through.

## Residual gaps — not covered by this run

An acceptance log that records only successes is not evidence. Four gaps the guard's design
leaves open, none exercised here:

- **Partial containment is untested live.** This run covers "holds all required scopes → skip"
  (the 4 read-only cases) and "lacks the required scope → run" (the 3 write-scope cases); the
  negative control covers "`Scopes` null → run". The "holds some but not all of several required
  scopes → run" branch is pinned only by unit tests and, as of `0cf649a`, the golden suite — no
  sample operation requires two scopes, so nothing here exercises it against a real identity
  server.
- **The OR/AND union across multiple `security` requirements remains a latent gap.** Flattening
  scopes across more than one `security` requirement into a single containment check is stricter
  than OpenAPI's OR-of-requirements semantics: for a multi-requirement spec, a case that should
  skip (satisfiable by requirement A even though the identity lacks something requirement B
  needs) can still run and fail — F11 one level in, on the union rather than the single
  requirement. Every sample used across v0 through the F11 phase declares at most one `security`
  requirement per operation, so nothing exercises this.
- **Nothing verifies a declared `Scopes` is true.** The guard trusts
  `ITestTokenProvider.Identities`'s declared `Scopes` completely; a provider that over-declares —
  claims a scope its token doesn't actually carry — silently converts a provable 403 into a
  silent skip instead of a failure. This is inherent to `ITestTokenProvider`'s declared-capability
  design (its own doc comment: "a declared capability, never a probe"), not a defect in the guard,
  but it is the residual risk a skip count cannot distinguish from a correctly-scoped one.
- **`.trx` `<Counters>` reports `notExecuted="0"`** in both runs above even where 4 per-result
  entries carry `outcome="NotExecuted"`. Anyone confirming skip counts by grepping `<Counters>`
  rather than reading individual `<UnitTestResult>` entries sees nothing — the per-result
  attribute is authoritative, the aggregate counter is not, and nothing in InTest's own docs says
  so yet.

## F11 phase actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | F11 — the wrong-scope 403 case assumes the Secondary identity lacks every scope any secured operation needs | F11 (`docs/superpowers/plans/2026-08-20-intest-f11-scope-aware-403.md`) | **Closed** — Results and negative control above |
| 2 | Partial containment (holds some but not all required scopes) has no live coverage | next phase touching Orders or another multi-scope sample | Open — recorded above, not fixed here |
| 3 | The OR/AND union across multiple `security` requirements is stricter than OpenAPI's OR semantics | next phase revisiting `TestPlanBuilder`'s auth-case generation | Open — recorded above, not fixed here |
| 4 | Nothing verifies a declared `ITestTokenProvider.Identities.Scopes` is true; an over-declaring provider silently converts a provable 403 into a silent skip | inherent to the declared-capability design; recorded as residual risk | Open — recorded above, not fixed here |
| 5 | `.trx` `<Counters>` under-reports skips (`notExecuted="0"` while per-result `outcome="NotExecuted"` entries exist) | next phase touching acceptance-run tooling or docs | Open — recorded above, not fixed here |

---

# v1-e Task 6 acceptance run — `generate --check` and `intest upgrade`

**Date:** 2026-08-22 (UTC) · **Commit:** `cc43714` + this commit
**Task:** v1-e plan Task 6 — the verdict task for
`docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md`. Tasks 1–5 (`ConfigLoader`
surfacing `intestVersion`, LF normalization and the scaffolded `.gitattributes`, `generate
--check`, `intest upgrade`, and the documentation catch-up) were all green in isolation before
this run started. Per the plan's own framing: *"This task is the verdict."* It is not a checklist
— it is the run that decides whether these two commands work for a team that adopts them.

Unit suite before the run: **652 passing, 0 failing** — Architecture 2, Cli 410, Runtime 205,
Golden 35, measured directly rather than assumed. Unchanged after — this run generates and
executes suites outside the repository, touching only the two documentation files this commit
changes.

## Step 1 — Phase 8's first line, and what it actually took to run it for real

`getting-started.md` Phase 8 opens with `dotnet tool restore`. The plan warned this in advance:
nothing is published to NuGet and the repository ships no `nuget.config` or local feed, so the
restore fails on its first line — and the trap is not the failure, it is quietly substituting
`dotnet run --project src/InTest.Cli` for it, recording a pass, and reproducing inside this
verdict the exact defect the plan exists to close.

**Neither substitute was used.** `dotnet tool restore` was made to work for real:

```bash
dotnet pack src/InTest.Cli/InTest.Cli.csproj -c Release -o <scratch>/feed
dotnet pack src/InTest.Runtime/InTest.Runtime.csproj -c Release -o <scratch>/feed
```

with a temporary `nuget.config` in the scaffolded project pointing at that feed plus `nuget.org`
for the transitive packages (`MSTest.*`, `Shouldly`, `Microsoft.Extensions.*`). Run against a
scaffolded `Orders.ApiTests` project:

```
Tool 'intest.cli' (version '0.1.0') was restored. Available commands: intest

Restore was successful.
```

**This is real evidence the mechanism works, not evidence the repository is CI-ready today.**
Nothing in the checked-in repository provides the feed or the config — a completely fresh clone
still cannot run Phase 8 verbatim, because nothing is published. What this run proves is that the
`dotnet tool restore` → pinned-version → `generate --check` chain functions correctly once that
one piece of infrastructure exists, which is the actual question §8's design rests on. That gap
— publishing, or an equivalent documented local-feed setup for contributors — is recorded as
still open below, not closed by this run.

**Second trap, found during Task 5 and confirmed here.** `~/.nuget/packages/intest.runtime/0.1.0`
already held a package before this run started — built from commit `c3899b8`, one commit behind
this run's `cc43714`, per its own embedded `<repository commit="…">` metadata:

```
<repository type="git" commit="c3899b8a3d46d82ae88ec15ffceb7cc327e89803" />
```

A scaffolded project's `PackageReference Include="InTest.Runtime" Version="0.1.0"` resolves
against whatever is in the global package cache for that exact version **before** it looks at any
feed — NuGet's cache lookup is keyed on package id and version, not on which feed most recently
published that version, so a newly-packed `0.1.0` sitting in the local feed is invisible as long
as a same-numbered `0.1.0` already sits in the cache. **Cleared, not packed over**:

```bash
rm -rf ~/.nuget/packages/intest.runtime ~/.nuget/packages/intest.cli
```

confirmed empty before restoring. **What this means for a real adopter, stated plainly**: the
moment `InTest.Runtime` is actually published at a version number someone has already built
locally from source at that same number — which `CONTRIBUTING.md`'s "building from source" path
actively encourages while nothing is published — that person's next `dotnet restore` silently
keeps using their stale local build, with no error, no warning, and no visible reason for the
mismatch. This is worth a documented warning (in `CONTRIBUTING.md`'s "Releases" section or
alongside the local-build instructions) that anyone who has packed `InTest.Runtime` locally
should run `dotnet nuget locals global-packages --clear` — or clear just that package's cache
folder — before trusting a newly published version with the same number. **Not fixed here**;
recorded as an action below.

**With both traps cleared, the rest of Phase 8's block ran exactly as documented, using the real
packaged tool throughout** — `dotnet intest generate`, `dotnet intest generate --check`,
`dotnet intest upgrade`, invoked via the actual `PackAsTool` package, not `dotnet run`. This is
also the first acceptance run in this project's history to do that: v1-a through the F11 phase
all substituted `dotnet run --project src/InTest.Cli` or swapped the scaffolded `PackageReference`
for a `ProjectReference`, because nothing was ever packed before. Because it wasn't substituted
this time, this run found something those earlier runs structurally could not have found — see
F13 below.

### The Orders spec, verified rather than trusted

The plan states `CustomersTests.g.cs` has 3 operations (all under `/api/customers`) and
`OrdersTests.g.cs` has 4 (all under `/api/orders`). Verified against the actual spec before
relying on it:

```
Method Path                Tags      OpId
------ ----                ----      ----
GET    /api/customers      Customers
POST   /api/customers      Customers
GET    /api/customers/{id} Customers
GET    /api/orders         Orders
POST   /api/orders         Orders
GET    /api/orders/{id}    Orders
DELETE /api/orders/{id}    Orders

Total ops: 7   Customers: 3   Orders: 4
```

Matches the plan exactly.

### The ordinary run

Scaffolded fresh (`intest init`, bootstrapped via `dotnet run --project src/InTest.Cli` since no
tool exists yet at that point — the one place `dotnet run` is legitimate, because Phase 8 assumes
`.config/dotnet-tools.json` is already committed, not that it springs into existence). `intest
generate` reported drift for all 5 operations needing a fixture (`delete_api_orders_id`,
`get_api_customers_id`, `get_api_orders_id`, `post_api_customers`, `post_api_orders`), exit `1`.
`intest fixtures repair` created 5 fixtures, exit `0`. Sentinels filled with values read from the
live seed data (`GET /api/customers`, `GET /api/orders` against a valid client-credentials token),
respecting real constraints found in the controllers' `DataAnnotations` (`Reference` `MaxLength(20)`
ruled out `{{runId}}`, which is 50+ characters — a fixed literal was used instead, and the
database reset between runs rather than relying on run-scoped uniqueness, since idempotence across
runs is v1-b/F7's concern, not this task's). One live-data correction needed: the first
`delete_api_orders_id` target (`ORD-0002`) was already `Shipped`, and `OrdersController` correctly
refuses to cancel a shipped order (`409`, `"Order in status 'Shipped' cannot be cancelled."`) — not
a defect, a business rule the fixture had to respect, same as the general fixture-workload lesson
from v1-a. Swapped to `ORD-0001` (status `Placed`), which cancels cleanly.

`intest generate` (real fixtures): `Generated 24 test(s) across 2 class(es).`, exit `0`. Base URL
configured to the origin (`http://localhost:5082/`, not the `/api/` prefix — the same F3 trap the
v0 run found), an `OrdersTokenProvider` written per the Phase 3 worked example (two identities,
`orders-client` full access and `orders-readonly` read-only, both real client-credentials tokens
from `samples/Identity.Server`). Full Phase 8 PR block, run for real:

```
=== dotnet tool restore ===
Tool 'intest.cli' (version '0.1.0') was restored. Available commands: intest
=== dotnet intest generate --check ===
Generated/ and coverage-report.json match a fresh render.
=== dotnet test ===
Test Run Successful.
Total tests: 24
     Passed: 20
    Skipped: 4
```

The 4 skips are the same `RequireSecondaryIdentityLacks` cases the F11 phase run established
(read-only identity holds `orders.read`, so those 403s cannot be provable) — this run did not
re-litigate F11, it inherited the same live identity pair and got the same shape.

## Step 2 — spec drift, not regenerated

`samples/Orders.Api/Orders.Api.json`'s `info.title` edited by hand, **not** followed by
`intest generate`. `intest generate --check`:

```
exit 1
coverage-report.json differs from a fresh render.
Run 'intest generate' to update.
```

**No-write held, checked precisely, not assumed**: `Generated/` and `coverage-report.json` on
disk were byte-identical and mtime-unchanged against a snapshot taken immediately after the Step 1
run — this is the assertion `[no-write]`'s own test exists to make mechanical, exercised here
against the real binary rather than only the unit test. Spec restored via a saved backup; `--check`
returned to exit `0` afterward, confirming the restore was exact.

## Step 3 — orphaning a file, the way that actually exercises the case

Deleting one path does not orphan a file when classes are per-tag — a multi-operation tag with one
operation removed keeps the same filename with different content, which is Step 2's case, not this
one. **All three `/api/customers` operations were removed** (both path items,
`"/api/customers"` and `"/api/customers/{id}"`), so the `Customers` tag disappears from a fresh
render entirely. Verified as valid JSON before running (`ConvertFrom-Json` round-trip). `intest
generate --check`:

```
exit 1
Generated/CustomersTests.g.cs exists on disk but a fresh render does not produce it.
Generated/spec-paths.json differs from a fresh render.
Generated/spec-schemas.json differs from a fresh render.
coverage-report.json differs from a fresh render.
Run 'intest generate' to update.
```

The stale-file row fired exactly as designed — this is the "silently-permissive" case the plan
called out, where a naive per-rendered-file comparison would report nothing wrong. `OrdersTests.g.cs`
was confirmed **byte-identical** to the Step 1 snapshot (`diff -q`, zero output), isolating this
case cleanly from Step 2's, per the plan's own instruction. `[no-write]` held here too: `Generated/`
and `coverage-report.json` on disk unchanged from the Step 1 snapshot, including `CustomersTests.g.cs`
itself — the file `--check` reports as an orphan is not touched by `--check`, only reported. Spec
restored; `--check` returned to exit `0`.

## Step 4 — version mismatch pre-empting a real diff

`intest.json`'s `intestVersion` hand-edited to `9.9.9` (running tool is `0.1.0`) **and** the spec's
`info.title` edited simultaneously, specifically to test the plan's ordering claim — a version
mismatch and a diff must yield `4`, not `1`, and the version check must run before any output
comparison. `intest generate --check`:

```
exit 4
intest.json was generated by intest 9.9.9; running tool is 0.1.0.
Regenerate with the pinned version, or run `intest upgrade` to adopt 0.1.0 deliberately.
```

Exact §8 wording, and **no mention of the diff that was also present** — confirms the ordering.
`[no-write]` held under this combined case too. `intest upgrade`:

```
exit 0
Generated 24 test(s) across 2 class(es).
Upgraded intest.json and .config/dotnet-tools.json to intest 0.1.0.
```

`intestVersion` and the `.config/dotnet-tools.json` pin both moved to `0.1.0` together, in one
command. `intest generate --check` afterward: exit `0`. **"The workflow passes again" was checked
past the gate, not just at it** — the live database was reset and `dotnet test` re-run against the
post-upgrade `Generated/`: `Total tests: 24, Passed: 20, Skipped: 4`, identical to Step 1. Spec
restored to its pre-Step-2 state; `git status` confirmed clean before continuing.

## Step 5 — cross-platform, proved both ways

No Linux host was available, so this used the plan's explicitly sanctioned alternative:
`core.autocrlf=true`, the Git-for-Windows default rev 1's pre-flight could not see because it
never varied autocrlf at all.

**Positive proof.** The Step 1 scaffold (clean, `Generated/` and `.gitattributes` both present)
committed to a throwaway git repository with `core.autocrlf=false`, then **cloned** into a fresh
directory with `-c core.autocrlf=true` — the actual git mechanism a checkout uses, not a
hand-simulated substitute. Byte-checked every committed generated/fixture file after checkout:

```
Generated\CustomersTests.g.cs : CR=0 LF=205
Generated\OrdersTests.g.cs    : CR=0 LF=279
Generated\spec-paths.json     : CR=0 LF=3
Generated\spec-schemas.json   : CR=0 LF=314
coverage-report.json          : CR=0 LF=22
fixtures\post_api_orders.json : CR=0 LF=17
```

Zero `\r` bytes anywhere, despite `autocrlf=true`. `intest generate --check` in the clone (`dotnet
tool restore` against the same local feed, run there too): exit `0`, `Generated/ and
coverage-report.json match a fresh render.`

**Negative control — the same clone, minus `.gitattributes` only.** An identical scaffold with
`.gitattributes` deleted before the commit, same clone-with-`autocrlf=true` treatment:

```
Generated\CustomersTests.g.cs : CR=205 LF=205
coverage-report.json          : CR=22  LF=22
```

Every line ending converted. `intest generate --check`:

```
exit 1
Generated/CustomersTests.g.cs differs from a fresh render.
Generated/OrdersTests.g.cs differs from a fresh render.
Generated/spec-paths.json differs from a fresh render.
Generated/spec-schemas.json differs from a fresh render.
coverage-report.json differs from a fresh render.
Run 'intest generate' to update.
```

Every generated artifact, on every line — precisely the "diff nobody can act on" `[lf-everywhere]`
was written to prevent, reproduced on demand rather than only reasoned about.

**The migration path, exercised on the broken clone, not merely described.** `intest upgrade` in
that same corrupted checkout:

```
exit 0
Generated 24 test(s) across 2 class(es).
Upgraded intest.json and .config/dotnet-tools.json to intest 0.1.0. Also scaffolded
.gitattributes, which this project did not have yet — see InitCommand.GitattributesContent
for what it pins and why.
```

`.gitattributes` now present with the exact content this repository's own file uses as its model.
`generate`'s own write (not git) restored every generated artifact to pure LF in place — `--check`
immediately after: exit `0`. This closes the loop Task 2 Step 3 opened: a project scaffolded
before `[lf-everywhere]` shipped has exactly one remedy, and it works.

## F13 — bare `intest …`, as shown in every code block in `getting-started.md`, does not run

Every Phase in `getting-started.md` — 1 through 8 — shows commands as bare `intest init …`,
`intest generate`, `intest generate --check`, `intest upgrade`. After a real `dotnet tool restore`
(this run's whole point, per Step 1), none of them run that way:

```
$ intest --help
bash: intest: command not found
```

```
PS> intest --help
The term 'intest' is not recognized as a name of a cmdlet, function, script file, or executable
program. Check the spelling of the name, or if a path was included, verify that the path is
correct and try again.
```

Confirmed cross-shell (Git Bash and PowerShell), so this is not a shell-specific artifact. The
correct invocation is `dotnet intest …` (the SDK's local-tool short form, available without
`dotnet tool run` since .NET 7) or `dotnet tool run intest …`; both work:

```
$ dotnet intest --version
0.1.0+cc437140d8425ab9aac468540f4e182d96077f48
```

`Program.cs` sets `<ToolCommandName>intest</ToolCommandName>` and Phase 2 scaffolds
`.config/dotnet-tools.json`, both of which signal a **local** tool via manifest, not a global
install — `dotnet tool restore` never adds anything to `PATH` for a local tool, unlike a global
install's shim directory. **No prior acceptance run could have found this**: v0 through the F11
phase all invoked InTest via `dotnet run --project src/InTest.Cli` or a swapped
`ProjectReference`, never through the packaged `PackAsTool` output — this is the first run in the
project's history to install and invoke the real tool, which is a direct consequence of Step 1's
refusal to substitute around the restore. Not fixed here — a documentation change (`dotnet
intest …` throughout, or a stated PATH-setup step) belongs to whoever next touches
`getting-started.md`; recorded as an action below.

## What v1-e Task 6 did not cover — stated rather than glossed

- **A fresh, zero-setup clone still cannot run Phase 8 verbatim.** This run built a temporary
  local feed and `nuget.config` by hand; neither is checked into the repository. Until `InTest.Cli`
  and `InTest.Runtime` are actually published, or the repository documents an equivalent
  contributor-facing local-feed setup, Phase 8's first line remains unrunnable from a bare clone —
  this run proves the *mechanism* works, not that the *repository* is ready to hand to CI today.
- **The stale-global-cache trap is not yet guarded against anywhere in the docs.** Recorded above
  as an action, not fixed.
- **F13 is not fixed**, only found and documented — `getting-started.md` still shows bare `intest`
  throughout.
- **No CI pipeline ran any of this.** Every step above ran on one local machine, against real
  processes, but "in a real pipeline" — the same gap v0's own acceptance criterion named and this
  document has carried open ever since — remains unmet for `--check` and `upgrade` specifically,
  same as it does for everything else in this file.
- **Only Orders was used.** Catalog and Inventory declare no `security` and were not part of this
  run — `--check` and `upgrade`'s own logic does not depend on auth, so this is a reasonable
  scope choice, not an oversight, but it means neither command has been exercised against a spec
  producer other than Swashbuckle in this run.
- **Partial containment, the OR/AND union gap, and the other F11-phase residual gaps are
  unaffected and unexercised here** — this run reused the same identity pair and did not attempt
  to close them; see the F11 phase section above.

## §5's marker column

While reading §5 for this run's Step 4/5 evidence, its own command-surface table lists 9 commands
under the framing "the full v1 surface" with no way to tell which of them actually run today —
`generate --emit-plan`, `fixtures promote`, `survey` and `assertions add` do not exist, and
nothing in the table said so. **Decided: add a marker column** (`Ships today`), directly in
`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`. Two reasons, not one:
first, §5 is explicitly the exit-code contract as well as a design description, and a reader
relying on it as a contract needs to know what is reachable; second, the same section already
uses this exact pattern two tables up (the frozen-axes table's `n/a in v1` row for the HTTP pack),
so this is consistency with an existing convention, not a new one. The column points at
`CONTRIBUTING.md` and `CLAUDE.md` as the actively-maintained source of the same fact rather than
duplicating it a third time, per this repository's "one canonical explanation" rule.

## v1-e Task 6 actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Document the stale-global-package-cache trap (`dotnet nuget locals global-packages --clear`, or clear just the affected package, before trusting a freshly published version number that was ever built locally under the same number) | `CONTRIBUTING.md`'s "Releases" section, or wherever local-build instructions live | Open — recorded above, not fixed here |
| 2 | F13 — `getting-started.md` shows bare `intest …` throughout; every invocation needs `dotnet intest …` or `dotnet tool run intest …` against a local-tool manifest | next phase touching `getting-started.md` | **Closed** — verified independently (pack + restore against a real local manifest, cross-shell) and fixed in `docs/getting-started.md` (Phase 2 explains why, Phase 4/5/8 commands corrected) and `README.md`; `init` and pre-adoption `survey` are left bare, deliberately — see Phase 2's note for why those two differ |
| 3 | Publish `InTest.Cli`/`InTest.Runtime`, or document a contributor-facing local-feed setup, so Phase 8 is runnable from a bare clone without the scaffolding this run built by hand | pre-v1 release readiness | **Closed** — `InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` published to nuget.org; see "0.1.0-preview.1 publish acceptance run" below |
| 4 | §5's command-surface table gave no way to distinguish shipped commands from designed-only ones | v1-e Task 6 | **Closed** — `Ships today` column added, see above |

---

# 0.1.0-preview.1 publish acceptance run

**Date:** 2026-08-24 · **Commit:** `35056b8` (tagged `0.1.0-preview.1`, tag pushed by the repository
owner — [tag-is-the-release] stays a human decision; nothing in this run or in `release.yml` cut the
tag itself)
**Task:** the first real tag push since `.github/workflows/release.yml` (NuGet Trusted Publishing)
was written and `docs/superpowers/specs/2026-08-23-nuget-publish-readiness-design.md` revision 8
shipped. Every earlier record of this workflow — CONTRIBUTING.md's "one honest gap", the readiness
spec's own revision-8 note, `release.yml`'s and `pack.yml`'s header comments — said the same thing:
every command sequence had been run locally by hand and passed `actionlint`, but the GitHub Actions
runtime itself (trigger firing, matrix fan-out, cross-job artifact transfer, the OIDC exchange, the
environment gate) had never fired for real. This run is that first firing.

## What was exercised

| Step | What | Result |
|---|---|---|
| 1 | `git tag 0.1.0-preview.1` on `35056b8`, pushed | Triggered both `pack.yml` (branch: `main`, tag push) and `release.yml` (tag push only) |
| 2 | `release.yml`'s `pack` job: checkout, restore, `scripts/ci/pack-and-verify.ps1 -ExpectedTag '0.1.0-preview.1'`, upload both `.nupkg` and `.snupkg` for both packages | Green |
| 3 | `release.yml`'s `publish` job: `environment: nuget-release` gate, `NuGet/login` OIDC exchange, `dotnet nuget push` | Green |
| 4 | nuget.org accepts the push | All four artifacts created |
| 5 | Install the published tool into a scratch directory | Resolves and registers cleanly |

nuget.org's own response, for the record:

```
InTest.Cli.0.1.0-preview.1.nupkg / .snupkg      Created  Your package was pushed.
InTest.Runtime.0.1.0-preview.1.nupkg / .snupkg  Created  Your package was pushed.
```

Both packages are live and installable, confirmed by installing `InTest.Cli` from the public feed
into a scratch directory outside the repository and getting a working tool registration — not just
a green Actions run trusted on its own exit code, the same "ask the thing that decides" discipline
CONTRIBUTING.md's own ground rules ask for elsewhere in this repository.

Published via NuGet Trusted Publishing (OIDC): the `publish` job's OIDC token was exchanged for a
short-lived nuget.org API key at push time. No `NUGET_API_KEY` secret exists anywhere in this
repository, before this run or after — confirming `release.yml`'s own header comment's central claim
rather than merely trusting it.

## Three things this closes

**Phase 8's `dotnet tool restore` now works from a bare clone, for real, for the first time.**
Every earlier acceptance run in this document — v1-a, v1-b, v1-c, F11, v1-e Task 6 — had to
substitute something for this line: a `ProjectReference` in place of the scaffolded
`PackageReference`, or v1-e Task 6's own temporary local NuGet feed built by hand. A bare clone
pointed at the default nuget.org source can now genuinely resolve `InTest.Cli`/`InTest.Runtime`
`0.1.0-preview.1` and restore against them — nothing about this run's steps 4–5 above required
any local feed, `nuget.config` override, or pre-seeded package cache.

**The `.snupkg`-under-`tools/` question, open since the readiness spec's revision 2, is answered.**
`InTest.Cli` packs as a tool (`PackAsTool`), which puts its PDB under `tools/net10.0/any/` rather
than `lib/`. Whether nuget.org's symbol-package intake accepts a `.snupkg` built that way was
unprovable without pushing — revision 8 of the readiness spec still called it "not established" and
pointed at this exact event ("§8 checks it at first publish"). It does. Both `.snupkg` files were
accepted alongside their `.nupkg` siblings, confirmed on both packages, not inferred from one.
See the readiness spec's revision 9 for the design-record close and `CONTRIBUTING.md`'s Publishing
checklist item 9 for the operational one.

**`pack.yml` and `release.yml` have now completed real Actions runs.** Trigger firing on a tag push,
the two-job matrix, cross-job artifact upload/download between `pack` and `publish`, the OIDC token
exchange, and the `nuget-release` environment gate enforcing that `id-token: write` only ever ran in
a job scoped to that environment — all of it was previously "exercised locally, on both platforms
where applicable, by hand" and nothing more. All of it fired for real this run and all of it went
green.

## What this run does not claim

Stated directly, because overstating it is exactly the failure mode this document exists to avoid:

- **One tag, one push.** This is one data point, not a claim that every future tag push behaves
  identically — a stable (non-preview) tag, a tag carrying a version bump across a major, or a
  second push after this one, are all still unexercised.
- **One platform's runners.** `release.yml`'s `publish` job runs `ubuntu-latest` only, by design
  (see that file's own comment on the choice — `pack.yml`'s own matrix already proves cross-platform
  packing parity, and re-proving it here would duplicate that job for no new evidence). This run
  says nothing new about `windows-latest` runners specifically publishing anything.
- **Prereleases only.** `0.1.0-preview.1` is a prerelease; nothing about a `0.1.0` stable tag's
  publish path has been exercised differently from what this run already covers, but "stable" also
  carries different adopter expectations this run does not speak to.
- **This is not a full adoption-path acceptance pass.** It proves the publish mechanism and the CI
  plumbing behind it. It says nothing new about `generate --check`/`upgrade`'s own behaviour (v1-e
  Task 6, above, already covers that against a local build), about `survey`/YAML/variation tests
  (still unbuilt), or about a fresh adopter's experience running Phase 0–Phase 8 end to end against
  the published packages for the first time — that walkthrough has not been re-run against the
  published version specifically.
- **The Publishing checklist's remaining human steps are unaffected.** ID-prefix reservation
  benefit aside (the push itself already claimed the `InTest.` prefix as a side effect —
  `CONTRIBUTING.md` step 2 is about the prefix-protection benefit, not a publishing prerequisite),
  a required-reviewer gate on the `nuget-release` environment remains recommended and not confirmed
  configured, and `PackageValidationBaselineVersion` is deliberately deferred to the release *after*
  this one (`CONTRIBUTING.md` step 11).

## 0.1.0-preview.1 publish actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Stop every doc claiming nothing is published — README, getting-started, CLAUDE.md, CONTRIBUTING.md, SECURITY.md all asserted the pre-publish state | this run | **Closed** — see this run's own commit for the full file list |
| 2 | Record `.snupkg`-under-`tools/` as resolved rather than open | readiness spec | **Closed** — revision 9, see above |
| 3 | Add a `CHANGELOG.md` and a standing changelog practice | this run | **Closed** — see `CHANGELOG.md` and `CONTRIBUTING.md`'s "Changelog" section |
| 4 | Create a GitHub Release from `release.yml` on a tag push, without letting `contents: write` and `id-token: write` coexist in one job | this run | **Closed** — see `release.yml`'s `release` job |
| 5 | Confirm a required-reviewer gate is configured on the `nuget-release` environment | repository owner, GitHub Settings | Open — recommended, not confirmed configured, same gap the readiness spec and `CONTRIBUTING.md` already name |
| 6 | Re-run the full Phase 0–Phase 8 adopter walkthrough against the published `0.1.0-preview.1` specifically, rather than a local build | pre-v1 release readiness | **Closed** — see "Adopter dry run against the published packages" below, which also found and fixed **F14** |

---

# Adopter dry run against the published packages

**Task:** publish-actions item 6, above — re-run the Phase 0–Phase 8 adopter walkthrough against
the packages actually on nuget.org (`0.1.0-preview.1`, and only that version; no `0.1.0` has ever
been published), rather than a local build or a temporary local feed. This is the first time any
acceptance run in this document used the published CLI itself, installed the ordinary way, to act
on this repository's own committed examples.

## F14 — both committed examples pinned a version that was never published

`examples/Catalog.ApiTests` and `examples/Orders.ApiTests` each carry three independent version
markers: `intest.json`'s `intestVersion`, `.config/dotnet-tools.json`'s `intest.cli` pin, and the
`.csproj`'s `InTest.Runtime` `PackageReference`. Before this run, the first two named `0.1.0` in
both examples; the `.csproj` correctly named `0.1.0-preview.1`, with a comment explaining why (the
first NuGet publish went out as a preview, not the plain `0.1.0` `intest init` normally scaffolds).
Only the `.csproj` had been corrected after publish — the other two markers were missed.

Reproduced the consequence in a scratch copy with an isolated `NUGET_PACKAGES`, before any fix:

```
dotnet restore      -> succeeds (InTest.Runtime 0.1.0-preview.1 resolves from nuget.org)
dotnet tool restore -> Version 0.1.0 of package intest.cli is not found in NuGet feeds
```

That is Phase 8's first command, failing for anyone who copies an example verbatim. Past it sits a
second failure: `intestVersion: 0.1.0` against a `0.1.0-preview.1` CLI trips `generate --check`'s
`[exact-match]` gate and exits 4 — v1-e Task 6 above proved that exit code fires on exactly this
shape of mismatch, on a different pair of values.

**Nothing in this repo's existing suites caught it.** `PackageVersionCouplingTests`
(`tests/InTest.Architecture.Tests`) guards the *scaffold template* three sites duplicate by
design (`Directory.Packages.props`, `InitCommand.cs`, `CompileVerificationTests.cs`) — it reads no
file under `examples/` at all. The committed examples drifted silently, and the first signal was
this run, following the documented adoption path by hand.

## Fixed with `intest upgrade`, using the published CLI

`intest upgrade` exists precisely to move `intestVersion` and the `intest.cli` pin together and
regenerate (`UpgradeCommand.cs`) — hand-editing one marker and missing the others is exactly the
mistake that produced F14, so the fix used the command rather than repeating it. To make the fix
itself a real test of the shipped artifact, the published package was installed the ordinary way
rather than built locally:

```
dotnet tool install --tool-path <scratch>/upgrade-tool intest.cli --version 0.1.0-preview.1
<scratch>/upgrade-tool/intest.exe --version
  -> 0.1.0-preview.1+35056b8cab6fa21981f9fa67e160b223a0378768
```

Run against both examples:

```
intest upgrade --project examples/Catalog.ApiTests
  -> Generated 13 test(s) across 2 class(es).
     Noted 1 operation(s) - see coverage-report.json.
     Upgraded intest.json and .config/dotnet-tools.json to intest 0.1.0-preview.1.
  -> exit 0

intest upgrade --project examples/Orders.ApiTests
  -> Generated 24 test(s) across 2 class(es).
     Upgraded intest.json and .config/dotnet-tools.json to intest 0.1.0-preview.1.
  -> exit 0
```

**What changed, checked explicitly rather than assumed:** `git status` after both runs shows only
the two version-marker files per example (`intest.json`, `.config/dotnet-tools.json`) modified.
`Generated/` and `coverage-report.json` are byte-identical to what was already committed — the
regenerate-first step `upgrade` always runs (Decision 4, `UpgradeCommand.cs`) confirmed the
committed output already matched a fresh render against the unchanged specs; the version drift was
isolated to the two markers, not a symptom of stale generated output. Neither example got a fresh
`.gitattributes` (both already had one).

**`[prerelease-reference-migration]` did not fire, for the right reason.** `upgrade`'s own
`DetectRuntimeReferenceMismatch` reports — never rewrites — a stale `InTest.Runtime` `.csproj`
reference after every write succeeds. It printed nothing for either example, because the `.csproj`
already named `0.1.0-preview.1`, matching the running CLI: there was nothing to report. This is
the code path that would have caught a *fourth* combination of this same defect (a stale
`.csproj` reference surviving an `upgrade` that only fixed the other two markers) — worth naming
explicitly since it is easy to mistake silence for "untested" rather than "checked, and clean".

## Re-proved the adoption path from a cold, bare-clone position

Same three commands publish-actions item 6 asks for, run against fresh scratch copies of both
fixed examples, with an isolated, empty `NUGET_PACKAGES` so nothing could resolve from a leftover
local cache:

| Step | Catalog.ApiTests | Orders.ApiTests |
|---|---|---|
| `dotnet tool restore` | `Tool 'intest.cli' (version '0.1.0-preview.1') was restored.` — exit 0 | same — exit 0 |
| `dotnet intest generate --check` | `Generated/ and coverage-report.json match a fresh render.` — exit 0 | same — exit 0 |
| `dotnet build` | `Build succeeded. 0 Warning(s) 0 Error(s)` | same |

The invocation is `dotnet intest generate --check`, not bare `intest …` — F13 above, already fixed
in `getting-started.md`. This is also the second time in this document `dotnet tool restore` has
worked from a bare clone at all (the first was the 0.1.0-preview.1 publish run above, installing
the tool standalone into a scratch directory) — the first time it has done so against one of this
repository's own committed, adopter-facing example projects, which is what publish-actions item 6
actually asked to see proven.

## New guard: `ExampleProjectVersionMarkerTests`

`tests/InTest.Architecture.Tests/ExampleProjectVersionMarkerTests.cs` reads all three version
markers for every directory under `examples/` that carries its own `intest.json`, and fails,
naming the example and both disagreeing values, if `intestVersion` does not equal both the
`intest.cli` pin and the `InTest.Runtime` `PackageReference` version. Deliberately checks internal
consistency between the three markers, not "matches nuget.org": what actually caused F14 was the
markers disagreeing with each other, not any one of them being wrong in isolation, and a guard
that reaches nuget.org from every CI run would be fragile against network flakiness for a fact
(what is published today) that has nothing to do with the commit under test.

**Proven to fire, not merely written and trusted.** Reverted `examples/Orders.ApiTests/intest.json`'s
`intestVersion` from `0.1.0-preview.1` back to `0.1.0` — the exact F14 shape — and ran the new test
alone (`--filter FullyQualifiedName~ExampleProjectVersionMarkerTests`):

```
Failed ThreeVersionMarkersAgreeAcrossEveryExample
Orders.ApiTests: intest.json's intestVersion ("0.1.0") disagrees with .config/dotnet-tools.json's
  intest.cli pin ("0.1.0-preview.1"). Run `intest upgrade --project examples/Orders.ApiTests` ...
Orders.ApiTests: intest.json's intestVersion ("0.1.0") disagrees with the InTest.Runtime
  PackageReference pinned in the .csproj ("0.1.0-preview.1"). ...
```

Reverting the edit restored a clean pass. The mutation was not kept as a second permanent build
configuration — proven once, then trusted, the same practice this repository already follows for
`TemplateEscapingGuardTests` and `JsonWritingOptionsGuardTests`.

## What this run does not claim

- **Only the version markers were exercised as a defect class.** This run did not re-walk Phase
  0–Phase 8 narratively step by step the way the original v0/v1-a runs did against a local build;
  it targeted the specific gap publish-actions item 6 named (the published packages, end to end)
  and the specific defect that gap surfaced.
- **`init` against the published CLI was not separately re-run.** `upgrade` and `generate --check`
  were exercised directly; a fresh `intest init` scaffold from the published tool, compared against
  what `InitCommand.cs` currently emits, is not part of what this run covers.
- **One version.** `0.1.0-preview.1` is the only version that has ever been published; this run
  says nothing about `upgrade`'s behaviour crossing a future major, which Decision 3 in
  `UpgradeCommand.cs` already documents as out of scope for the command as it exists today.

## Adopter dry run actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Fix both committed examples' `intestVersion` and `.config/dotnet-tools.json` pins to name a published version | this run | **Closed** — via `intest upgrade`, not by hand; see above |
| 2 | Add a mechanical guard so committed examples cannot silently re-drift | this run | **Closed** — `ExampleProjectVersionMarkerTests`, proven to fire by reversion |
| 3 | Clean up `intest.*` packages and tool installs this run added to the local machine | this run | **Closed** — see this run's own commit message |

---

# Adopter walkthrough acceptance run — full Phase 0-8, published packages, no substitutions

**Task:** run `docs/getting-started.md` end to end, Phase 0 through Phase 8, treating it as the
specification — do only what it says, record any divergence rather than working around it — using
the **published** `InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` throughout. Every earlier
acceptance run in this document substituted something for at least one step (a `ProjectReference`,
`dotnet run --project`, a hand-built local NuGet feed); the previous "adopter dry run" (above)
exercised the published tool but only against the two committed `examples/` projects and only for
`upgrade`/`generate --check`, not a fresh scaffold walked narratively through every phase. This is
that walkthrough.

## Environment

- **Repo state: local, not `origin/main`.** `HEAD` was `b349e257854245af51df8c46862a1190b8b7915a`,
  7 commits ahead of `origin/main`, with another session's uncommitted work already sitting in
  `src/InTest.Cli/Spec/SpecFetcher.cs` and its test — left untouched throughout, per this run's own
  constraints. **This mattered**: the local tree carries a `Properties/launchSettings.json` per
  sample project (4 commits back), pinning fixed, non-colliding ports, that `origin/main` does not
  have yet. `dotnet run --project samples/<X>` therefore "just worked" on the documented ports with
  no environment variable set by hand. An adopter cloning from GitHub today still hits the older
  gap this document's own F9 (v1-b section) already recorded: `ASPNETCORE_URLS` has to be set
  externally, or the launch profiles copied in by hand, until those commits reach `origin/main`.
- **Two of the four sample processes were already running** when this run started — `Identity.Server`
  (port 5084) and `Orders.Api` (port 5082), both started ~00:49 by the other session sharing this
  tree. Health-checked (`/health/ready` and `/.well-known/openid-configuration`, both 200) and
  reused rather than restarted, so as not to disturb concurrent work; `Catalog.Api` (5081) and
  `Inventory.Api` (5083) were not running and were started by this run.
- **NuGet**: `NUGET_PACKAGES` set to a cold, empty directory under this run's own scratch space for
  every restore, so nothing could resolve from a locally-built leftover — the exact trap
  getting-started.md's own "stale local package cache" section warns about.
- **Scaffolding done entirely outside the repository**, in scratch directories, per this run's own
  constraints — nothing under `Catalog.ApiTests/`, `Orders.ApiTests/` or `Inventory.ApiTests/`
  below is committed to this repository.

## Phase 0 — skipped, per the document's own banner

`survey` is not built. Went straight to Phase 1, as instructed.

## Phase 1 — spec availability

Used the "same repository as the API" path for all three samples: built each sample project
(`dotnet build samples/<Project>`, one at a time — `dotnet build a b c d` in one invocation is
rejected by MSBuild itself with `MSB1008: Only one project can be specified`, an MSBuild limitation
unrelated to InTest) and pointed `spec.source` at the resulting `bin/Debug/net10.0/<Project>.json`
build artifact, exactly as Phase 1's table recommends. `Orders.Api`'s build failed to finish
(`MSB3027`, its `.exe` locked by the already-running process noted above) but its OpenAPI document
had already been written to disk earlier that session and did not need to change, so the existing
artifact was used as-is — confirmed valid JSON and current for the running code before relying on
it. The URL-sourced `spec.source` path (`spec.json` snapshot, Phase 1's "different repository, or
only a URL" branch) was **not exercised** this run — noted below under what this run does not
claim.

## Phase 2 — scaffold, against the published tool

```
dotnet tool install -g InTest.Cli --version 0.1.0-preview.1
```

Resolved from nuget.org into the cold `NUGET_PACKAGES`, no local feed involved. `intest --version`
afterward: `0.1.0-preview.1+35056b8cab6fa21981f9fa67e160b223a0378768`.

```
intest init --name Catalog.ApiTests   --spec .../Catalog.Api/bin/Debug/net10.0/Catalog.Api.json
intest init --name Orders.ApiTests    --spec .../Orders.Api/bin/Debug/net10.0/Orders.Api.json
intest init --name Inventory.ApiTests --spec .../Inventory.Api/bin/Debug/net10.0/Inventory.Api.json
```

All three exit `0`, bare `intest`, as Phase 2 says is the one deliberate exception. (One scratch
directory named `Orders.ApiTests` already existed from an earlier, unrelated run — `intestVersion:
"0.1.0"`, a version never published — and `init` correctly refused it with exit `3`, "`intest.json`
already exists." Deleted and re-scaffolded cleanly rather than reused, to keep this run's own
evidence uncontaminated by leftover state.)

**The gap this run existed to close**: every scaffolded project's three version markers —
`intest.json`'s `intestVersion`, `.config/dotnet-tools.json`'s `intest.cli` pin, and the
`.csproj`'s `InTest.Runtime` `PackageReference` — all independently named `0.1.0-preview.1`,
matching the running CLI's own version exactly (`[scaffold-reads-itself]`,
`InitCommand.cs`'s `CliVersion.Current` substitution). The previous "adopter dry run" proved this
holds for the two hand-maintained `examples/` projects after a fix; this run is the first time it
was checked against a **freshly scaffolded** project, from the published prerelease tool, with
nothing hand-edited first.

## Phase 3 — configure

`Api:BaseUrl` in each project's `appsettings.json` (the `local` default profile) set to the
sample's origin — `http://localhost:5081/`, `5082/`, `5083/` respectively, no path prefix repeated,
per the F3 guard's own guidance. `readiness.path` was already `/health/ready` (absolute) as
scaffolded — the F2 fix holds.

For `Orders.ApiTests` (the one sample with declared `security`), wrote `OrdersTokenProvider.cs`:
a client-credentials `ITestTokenProvider` against `samples/Identity.Server`, with two identities —
`orders-client` (`orders.read`, `orders.write`) and `orders-readonly` (`orders.read`) — matching
that sample's own `Config.cs` exactly, and registered it in the scaffold's `TestStartup.Register`
exactly as the (post-F8) scaffold doc comment instructs:

```csharp
services.AddSingleton<ITestTokenProvider, OrdersTokenProvider>();
```

No hand-written `DelegatingHandler` was needed — `AuthHandler` is already attached to
`InTestClients.Api`, confirming F8's closure holds for a fresh scaffold, not just the runtime's own
unit suite.

## Phase 4 — generate (first pass)

```
dotnet intest generate
```

Run **before any explicit `dotnet tool restore`** in any of the three projects — worth recording
positively: it worked anyway (the SDK's implicit local-tool restore on first invocation), so an
adopter following Phase 4 verbatim, without first running the `dotnet tool restore` Phase 8 shows
separately, is not stranded. All three exited `1`, "no fixture found" for every operation, matching
this document's own v1-a section shape exactly:

```
Catalog.ApiTests:   8 operations named, exit 1
Orders.ApiTests:    5 operations named, exit 1
Inventory.ApiTests: 4 operations named, exit 1
```

## Phase 5 — fixtures

```
dotnet intest fixtures repair
```

`Created 8 fixture(s)`, `Created 5 fixture(s)`, `Created 4 fixture(s)` — identical counts to the
v1-a and F14 runs on the same three specs. Every `TODO:` sentinel filled by hand with real,
schema-conformant values: SKUs matching Catalog's `^[A-Z]{3}-[0-9]{4}$` pattern, `{{runId}}` for
the two free-form-uniqueness fields (Catalog category name, Orders customer email), and real row
IDs — either stable seed rows or rows left behind by this document's own earlier acceptance
sections — for every GET/DELETE/PUT target. **F6's fix confirmed still holding**:
`post_api_products.json`'s `dimensions` composed as a real three-property nested object
(`lengthCentimetres`/`widthCentimetres`/`heightCentimetres`), not a flat string sentinel.

## Phase 4 — generate (second pass)

```
dotnet intest generate
```

All three exit `0`:

| Project | Generated | Classes | Notes |
|---|---|---|---|
| `Catalog.ApiTests` | 13 tests | 2 | 1 operation noted |
| `Orders.ApiTests` | 24 tests | 2 | — |
| `Inventory.ApiTests` | 9 tests | 2 | 1 operation noted |

Exactly the counts `getting-started.md`'s own banner states for Orders (24) and that this
document's earlier sections recorded for Catalog and Inventory.

## Phase 8, pulled forward — restore and check

```
dotnet tool restore
dotnet intest generate --check
```

All three: `Tool 'intest.cli' (version '0.1.0-preview.1') was restored.`, exit `0`; then
`Generated/ and coverage-report.json match a fresh render.`, exit `0`.

```
dotnet build
```

All three: **Build succeeded, 0 Warning(s), 0 Error(s)** — `InTest.Runtime 0.1.0-preview.1`, a
prerelease and the CLI's own version, resolved and compiled from nuget.org into a completely cold,
isolated `NUGET_PACKAGES` with no local leftovers of any kind. **This is the specific gap the task
existed to close**: a *committed* example restoring the prerelease runtime was proven in the
previous acceptance run; a *freshly scaffolded* project had never been tried. It restores and
builds cleanly.

## Phase 6 — run, against real HTTP

```
dotnet test --logger "console;verbosity=detailed"
```

```
Catalog.ApiTests:   Total tests: 13   Passed: 13   Failed: 0   Skipped: 0
Orders.ApiTests:    Total tests: 24   Passed: 20   Failed: 0   Skipped: 4
Inventory.ApiTests: Total tests: 9    Passed: 9    Failed: 0   Skipped: 0
```

All four Orders skips are `RequireSecondaryIdentityLacks`, with the exact stated reason:

```
Assert.Inconclusive. Skipped: the secondary identity 'orders-readonly' holds orders.read, which
this operation requires, so it cannot produce a 403. Declare different scopes on that identity,
or leave Scopes null to run this test anyway.
```

This reproduces `getting-started.md`'s own stated banner — "24 tests: 0 failed, 4 skipped, 20
passed" — **for the first time against the published packages** rather than a local build. No
`TODO:` sentinel failures, no genuine failures: every test that ran, ran against live HTTP and
passed or was correctly gated.

## Phase 8 — the rest

**Post-deployment gate filter**, run a second time against the same, unreset `Catalog.Api`
database:

```
dotnet test --filter "TestCategory=Contract" --settings Catalog.ApiTests.runsettings
```

`Failed: 2, Passed: 11, Total: 13` — `DeleteApiCategoriesId_Contract` 404s (its target already
deleted by the first run) and `PostApiProducts_Contract` 409s (its SKU already created by the
first run). This is **not a new defect** — it is Phase 5's own documented non-idempotency (F7)
reproducing exactly as written, because this run's fixtures used literal/`{{runId}}` values with
no `IAssemblyFixture` seeding (the repeatability pattern v1-b already proved separately). Confirmed
in passing: every generated test today carries `[TestCategory("Contract")]` — variation tests
don't exist yet to be excluded by this filter, so nothing here differs from Phase 6's own numbers
except the operations a second, unreset run cannot repeat.

**Exit 4 and `upgrade`**, on `Inventory.ApiTests`:

```
# intestVersion hand-edited 0.1.0-preview.1 -> 9.9.9
dotnet intest generate --check
  -> exit 4
     intest.json was generated by intest 9.9.9; running tool is 0.1.0-preview.1.
     Regenerate with the pinned version, or run `intest upgrade` to adopt 0.1.0-preview.1 deliberately.

dotnet intest upgrade
  -> exit 0
     Generated 9 test(s) across 2 class(es).
     Noted 1 operation(s) — see coverage-report.json.
     Upgraded intest.json and .config/dotnet-tools.json to intest 0.1.0-preview.1.

dotnet intest generate --check
  -> exit 0, Generated/ and coverage-report.json match a fresh render.

dotnet build
  -> Build succeeded, 0 Warning(s), 0 Error(s)
```

Exact §8 wording, both version numbers named, `intestVersion` and the `.config/dotnet-tools.json`
pin moved back together in one command — matching the F14/adopter-dry-run section's own
description of `upgrade` exactly, now exercised on a fresh scaffold instead of a hand-maintained
example.

## F13, re-checked — and why this run could not fully re-exercise it

Bare `intest` **did** resolve inside a `dotnet tool restore`-completed project directory in this
run — the opposite of F13's own finding. This is not a contradiction: this run's Phase 2 installed
the *same* version (`0.1.0-preview.1`) globally first, per the document's own Prerequisites
section, so the global shim on `PATH` happens to shadow the local manifest's pin with an identical
version rather than a different one. Phase 2's own prose already names exactly this risk — "`dotnet
intest …` … resolve[s] the version this project just pinned, where a stray global copy on PATH
would not" — but proving the *divergent* case (global and local pins disagreeing) is not
constructible today: `0.1.0-preview.1` is the only version ever published to nuget.org. Recorded as
an observation the doc already covers, not a new finding, and not something this run's environment
could fully exercise.

## What this run does not claim

- **No new defect found.** Every phase, every command, every exit code matched
  `getting-started.md` exactly, against the published packages, with zero substitutions — the
  first time that has been true of a full Phase 0–8 walkthrough in this document's history.
- **The URL-sourced `spec.source` path was not exercised.** All three projects used a local build
  artifact (Phase 1's first table), not a URL and its `spec.json` snapshot.
- **`survey`, `fixtures promote`, YAML input, variation tests, `assertions add`,
  `generate --emit-plan`** remain unbuilt and unexercised, per the document's own banner.
- **No CI pipeline ran any of this.** One local machine, real processes, real HTTP — not "in a real
  pipeline," the same gap this document has carried open since v0.
- **Phase 7 ("commit") was reviewed conceptually, not exercised.** The three scratch projects are
  not git repositories; the file-categorization table was checked against what Phase 2/4/5 actually
  wrote to disk, not against an actual `git add`/`git commit` cycle.
- **F13's divergent-version case remains unconstructed** — see above.

## Cleanup

- `Catalog.Api` and `Inventory.Api` — the two sample processes this run started — were stopped.
  `Identity.Server` and `Orders.Api`, already running before this run started and belonging to the
  other session sharing this tree, were left running, untouched.
- `intest.cli` uninstalled from the global tool registry (`dotnet tool uninstall -g InTest.Cli`).
- `intest.cli` and `intest.runtime` removed from `~/.nuget/packages` — **despite** every restore in
  this run using an isolated, cold `NUGET_PACKAGES`, `dotnet tool install -g`'s own asset
  resolution populated the *default* global packages folder anyway for the tool's own install. This
  is a `dotnet`/NuGet behavior, not an InTest one, but worth recording for whoever repeats this
  exercise: isolating `NUGET_PACKAGES` for restores inside scaffolded projects does not also
  isolate a `-g` tool install.

## Repo suite afterward

`dotnet test InTest.sln` in `D:/TestGen` at `HEAD = b349e25` (unchanged by this run — nothing
under `src/`, `tests/`, or `docs/superpowers/` was touched):

```
Passed!  - Failed: 0, Passed:  10, Skipped: 0, Total:  10 - InTest.Architecture.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 205, Skipped: 0, Total: 205 - InTest.Runtime.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 483, Skipped: 0, Total: 483 - InTest.Cli.Tests.dll (net10.0)
Failed!  - Failed: 1, Passed:  34, Skipped: 0, Total:  35 - InTest.Golden.Tests.dll (net10.0)
```

The one Golden failure, `ReadinessProbeSurvivesAThrowingApiHandler`:

```
error MSB3713: The file "obj\Debug\net10.0\InTest.Runtime.AssemblyInfo.cs" could not be created.
The process cannot access the file '...\src\InTest.Runtime\obj\Debug\net10.0\InTest.Runtime.AssemblyInfo.cs'
because it is being used by another process.
```

A file-lock race on `src/InTest.Runtime`'s own `obj/` directory — that Golden test builds a
scaffolded project via a `ProjectReference` back into this repo's own `InTest.Runtime`, and this
tree is shared with another session doing its own uncommitted work throughout this run (see
Environment, above). Re-ran the single test in isolation immediately after: **1 of 1 passed, no
retry, no code change.** Recorded as a shared-tree flake, not a product defect, per this run's own
instruction not to quietly substitute a clean result — the raw failure is shown above rather than
only the clean rerun.

**Total with the flake counted as failed: 733 tests, 732 passed, 1 failed, 0 skipped.**

## Full Phase 0-8 walkthrough actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Prove a freshly scaffolded project (not just the committed `examples/`) restores `InTest.Runtime 0.1.0-preview.1` from nuget.org into a cold cache | this run | **Closed** — see Phase 2/Phase 8 above |
| 2 | Walk `getting-started.md` Phase 0 through Phase 8 narratively, against the published tool, with no substitutions, and reproduce the document's own stated Orders banner (24/20/4/0) live | this run | **Closed** — see Phase 6 above |
| 3 | Exercise `generate --check`'s exit 4 and `intest upgrade` against a fresh scaffold (not only `examples/`) using the published tool | this run | **Closed** — see Phase 8 above |
| 4 | Add `origin/main`'s missing `Properties/launchSettings.json` for the four sample projects, so a bare GitHub clone gets fixed ports without `ASPNETCORE_URLS` set by hand | repository owner, next push | Open — local commits exist (`43d0a02`..`1b5287c`), not yet on `origin/main` |
| 5 | Note in `CONTRIBUTING.md` or `docs/getting-started.md`'s stale-cache section that isolating `NUGET_PACKAGES` for scaffolded-project restores does not also isolate a `dotnet tool install -g`, which still populates the default global packages folder | pre-v1 release readiness | Open — recorded above, not fixed here |
| 6 | Clean up `intest.*` packages/tool installs and sample API processes this run added to the local machine | this run | **Closed** — see Cleanup above |
---

# Three-package-split acceptance run - a simulated publish

**Task:** prove the **runtime-framework split** -- `src/InTest.Runtime.MSTest`, a new package with
`<PackageId>InTest.Runtime.MSTest</PackageId>`, which `InitCommand`'s scaffold now references
instead of `InTest.Runtime` directly -- works end to end as a **three-package** adoption before it
is tagged. `0.1.0-preview.2` does not exist; nuget.org still carries only the two-package
`0.1.0-preview.1`. Shipping the next tag with only `InTest.Cli`/`InTest.Runtime` published would
leave every freshly scaffolded project referencing an `InTest.Runtime.MSTest` package that cannot
restore -- the most user-visible form of the `[paired]` defect this document keeps catching (F14
above is the same defect class, one version marker out of sync with the packages that actually
exist).

**State clearly, as this document's own discipline requires:** every artifact in this run carries
a version of the shape `0.1.0-local.<UTC timestamp>.pid<pid>` -- packed from a source copy outside
git via `-p:MinVerVersionOverride`, restored from a scratch local feed, never touching nuget.org.
**This simulates a publish. It does not exercise nuget.org, NuGet Trusted Publishing, the OIDC
exchange, or the `nuget-release` environment gate for the third package** -- see "What this run
does not claim" below for exactly what remains unproven.

## Environment

- `HEAD = d152412a2cfabdf620d8a2034af98e5645efc32f`, working tree clean, unit suite **943
  passing, 0 failing** (Architecture 12, Runtime 253, Cli 628, Golden 50) -- confirmed before this
  run, and reconfirmed identical afterward (see "Repo suite afterward" below), since nothing under
  `src/` or `tests/` was touched.
- **The global NuGet package cache was already dirty before this run started** --
  `~/.nuget/packages/intest.cli/0.1.0-preview.1` and `~/.nuget/packages/intest.runtime/0.1.0-preview.1`
  were present, left behind by an unrelated earlier session (no `intest.runtime.mstest` entry
  existed -- that package has never been packed on this machine before today). This is the
  *real*, published prerelease version, not a locally-packed collision risk, but it matters below
  (see F15): it is what exposed a bug in the harness's own "cache is clean" confirmation.
- All scaffolding done in `%TEMP%`, outside the repository, via `scripts/local-e2e-test.ps1`'s own
  isolation (a private `src-copy` outside git, a redirected `NUGET_PACKAGES`, a version that can
  never collide with a release) -- nothing here is committed.

## Step 1 - pack and verify all three, via `scripts/local-e2e-test.ps1`

Read first, as instructed. **It already packs and verifies three packages** -- `InTest.Cli`,
`InTest.Runtime`, and `InTest.Runtime.MSTest` -- not two, so no harness change was needed to widen
its scope; the runtime-framework-split merge that landed `d152412` had already updated it. Used
as-is, unmodified in this respect, as the primary harness this task's own instructions asked for.

Run once with `-KeepScratch` (local version `0.1.0-local.20260826222256-pid2867016`) so the
scaffold could be extended below into a live run the script deliberately keeps out of its own
scope. All nine steps passed:

```
pack InTest.Cli / InTest.Runtime / InTest.Runtime.MSTest  -> exit 0, one identical version confirmed for all three
intest init (dotnet run bootstrap)                        -> exit 0
dotnet tool restore                                        -> exit 0, "Tool 'intest.cli' (version '0.1.0-local....') was restored."
intest generate (no fixtures yet)                          -> exit 1, 8 operations named
intest fixtures repair                                     -> exit 0, "Created 8 fixture(s), updated 0 fixture(s)."
intest generate                                             -> exit 0, "Generated 13 test(s) across 2 class(es). Noted 1 operation(s)."
intest generate --check (clean)                             -> exit 0
dotnet build                                                 -> Build succeeded, 0 Warning(s), 0 Error(s)
intest generate --check (contrived version mismatch)         -> exit 4, exact section-8 message
intest upgrade                                               -> exit 0, regenerated, both version markers moved together
intest generate --check (post-upgrade)                       -> exit 0
```

The scaffolded `.csproj` was verified -- not assumed -- to carry exactly one
`Include="InTest.Runtime.MSTest" Version="0.1.0-local.20260826222256-pid2867016"`, matching the
CLI's own running version (`[scaffold-reads-itself]`).

## Step 2 - transitive resolution, verified not assumed

The task's own crux. With `NUGET_PACKAGES` still pointed at the run's private scratch cache (never
the machine-wide one), `dotnet list package --include-transitive` inside the scaffold:

```
Top-level Package             Requested                              Resolved
> InTest.Runtime.MSTest       0.1.0-local.20260826222256-pid2867016   0.1.0-local.20260826222256-pid2867016

Transitive Package            Resolved
> InTest.Runtime               0.1.0-local.20260826222256-pid2867016
```

**Both resolved to the identical version** -- not merely a compatible one. A fresh restore pulls
`InTest.Runtime.MSTest` directly and `InTest.Runtime` transitively through it, at the exact version
that was packed alongside it, exactly as `getting-started.md`'s Phase 2 table and
`pack-and-verify.ps1`'s `Assert-AdapterDependsOnExactNeutralVersion` both promise.

## Step 3 - extending past the script's own documented scope: live HTTP

`scripts/local-e2e-test.ps1`'s own header states plainly that a live `dotnet test` run is
deliberately out of its scope. This task asked for one, so the kept scaffold above was carried
further by hand, the same way `v1-a`'s and every later acceptance run's fixtures were:

- Every `TODO:` sentinel in the 8 generated Catalog fixtures replaced with real values against
  `samples/Catalog.Api`'s deterministic seed data (`CatalogDbContext.SeedAsync` -- the Hardware /
  Software / Deprecated categories, the Widget / Sparse products), following the same
  target-separation discipline `v1-b`'s `CatalogSeedFixture` fixtures used (the `GET` and `DELETE`
  category tests point at *different* rows, `22222222-...` and `33333333-...`, so MSTest's lack of
  ordering guarantee can't make one interfere with the other); `{{runId}}` for the one free-form
  uniqueness field (the created category's name); a fresh SKU (`TGN-0001`, matching
  `^[A-Z]{3}-[0-9]{4}$`) for the created product, chosen not to collide with the seeded
  `WGT-0001`/`SPR-0002`.
- `dotnet intest fixtures repair` reported `Nothing to repair` and `dotnet intest generate --check`
  stayed clean after the edits -- confirming the fixture edits didn't drift generation.
- `samples/Catalog.Api` started fresh (its `catalog.db` deleted first) on its pinned port 5081
  (`Properties/launchSettings.json`); the scaffold's `appsettings.json` `Api:BaseUrl` set to
  `http://localhost:5081/` (`readiness.path` was already the correct absolute `/health/ready`, as
  scaffolded).
- `dotnet test --logger "console;verbosity=detailed"`, `NUGET_PACKAGES` still pointed at the run's
  own scratch cache:

```
Passed DeleteApiCategoriesId_Contract [142 ms]     Passed DeleteApiCategoriesId_NotFound [89 ms]
Passed GetApiCategoriesId_Contract [20 ms]         Passed GetApiCategoriesId_NotFound [5 ms]
Passed GetApiCategories_Contract [17 ms]           Passed PostApiCategories_Contract [42 ms]
Passed GetApiProductsIdTags_Contract [89 ms]       Passed GetApiProductsIdTags_NotFound [3 ms]
Passed GetApiProductsId_Contract [57 ms]           Passed GetApiProductsId_NotFound [4 ms]
Passed GetApiProducts_Contract [52 ms]             Passed PostApiProducts_Contract [33 ms]
Passed PutApiProductsId_Contract [24 ms]

Test Run Successful.
Total tests: 13     Passed: 13     Total time: 5.0991 Seconds
```

**13 of 13, real HTTP, against the locally-packed three-package build -- no `TODO:` sentinel
failures, no genuine failures.** This is the live proof the task asked for: a project that
restores `InTest.Runtime.MSTest` and `InTest.Runtime` transitively from the split packages not
only compiles but actually runs a full suite over the wire.

## Step 4 - a second, independent run, and a bug the first run's dirty cache exposed

The whole script was run a second time, start to finish, with default cleanup (no `-KeepScratch`,
local version `0.1.0-local.20260826222914-pid3131216`) -- both to prove the pipeline reproduces
independently and to serve as the negative control for **F15** below. All eleven steps passed
identically to Step 1.

### F15 - `local-e2e-test.ps1`'s own cache-clean confirmation printed even when it wasn't true

The script's `finally` block is supposed to be the thing that makes "confirm the package cache is
clean" trustworthy without a human re-checking by hand -- this task's own instructions lean on
exactly that guarantee. Both runs above ended with:

```
WARNING: UNEXPECTED: C:\Users\tjayo\.nuget\packages\intest.cli exists in the machine-wide NuGet cache. ...
WARNING: UNEXPECTED: C:\Users\tjayo\.nuget\packages\intest.runtime exists in the machine-wide NuGet cache. ...

Confirmed: C:\Users\tjayo\.nuget\packages has no intest.cli, intest.runtime or intest.runtime.mstest entries.
```

**Both lines, together, in the first run.** The "Confirmed" message is wrong on its own terms --
`intest.cli` and `intest.runtime` visibly exist, two lines above it says so -- and the cause is a
plain logic bug, not a race: the tripwire loop (`scripts/local-e2e-test.ps1`, lines 502-516) only
ever set `$Failed` inside the main `try` block's `catch`, never inside the tripwire loop itself, so
the closing `if (-not $Failed) { Write-Host "Confirmed: ..." }` prints on *any* exception-free run,
regardless of what the loop immediately above it just found. In this run the two pre-existing
`0.1.0-preview.1` entries (Environment, above) were what exposed it -- a locally-packed collision
would have exposed the identical bug, only with a much higher-stakes "Confirmed" lie sitting on top
of it.

**Fixed** (`scripts/local-e2e-test.ps1`): a `$CacheClean` flag, set `$false` inside the loop
alongside the existing `Write-Warning`, gates the final message alongside `$Failed`. **Negative
control performed**: Step 4's second run, against the same still-dirty cache, with the fix
applied, printed only the two `WARNING` lines and correctly withheld the "Confirmed" message --
exactly the contrast the bug predicts. Not a functional change to what the script packs, restores,
or verifies; only to whether its own final claim is trustworthy.

## `release.yml` / `pack.yml` - three-package-complete, but with stale text

Checked directly against the running scripts and workflow logic, not against comments alone:

- `scripts/ci/pack-and-verify.ps1` (used by both `pack.yml` and `release.yml`) already packs and
  verifies all three projects, selects each `.nupkg` by its nuspec `<id>` rather than a filename
  glob (deliberately, since `InTest.Runtime.MSTest`'s id extends `InTest.Runtime`'s with a dot --
  see the script's own `Get-NupkgById` comment), asserts all three pack at one identical version,
  asserts `InTest.Runtime`'s nuspec carries no MSTest/xUnit/NUnit/`Microsoft.NET.Test.Sdk`
  dependency, and asserts `InTest.Runtime.MSTest`'s nuspec depends on *exactly* the `InTest.Runtime`
  version packed alongside it (plus `MSTest.TestFramework` as a positive control).
- `release.yml`'s `publish` job pushes `"${{ runner.temp }}/intest-release-pack/*.nupkg"` -- a glob,
  so all three packages' `.nupkg` are pushed in one `dotnet nuget push` (NuGet resolves each
  sibling `.snupkg` automatically).
- `release.yml`'s `release` job hard-asserts `$assets.Count -eq 6` (three packages x `.nupkg` +
  `.snupkg`) before creating the GitHub Release -- a positive control that fails the job outright if
  the third package's artifacts ever went missing from the upload.

**This is genuinely three-package-complete**, not a defect. What both files *did* still carry was
stale human-facing text left over from before the runtime-framework split: `pack.yml`'s own
comment said "packs both projects" and both `pack.yml` and `release.yml` named their pack step
"Pack and verify InTest.Cli / InTest.Runtime" -- accurate before the split, misleading since, even
though the actual behaviour underneath was already correct. Fixed alongside F15 above, in the same
commit: the comment now names all three projects and the step names now read "Pack and verify
InTest.Cli / InTest.Runtime / InTest.Runtime.MSTest". Not numbered as its own finding -- no
functional gap existed, only a stale label on already-correct logic.

## Package cache: confirmed clean

Checked directly, not by trusting the (now-fixed) script's own message:

```
$ ls ~/.nuget/packages | grep -i intest
(none found - clean)
```

Both the two pre-existing `0.1.0-preview.1` entries (Environment, above -- left by an unrelated
earlier session, not by this run) and the run's own scratch temp directories were removed as part
of this run's own cleanup. `~/.nuget/packages/intest.cli`, `~/.nuget/packages/intest.runtime`, and
`~/.nuget/packages/intest.runtime.mstest` are all confirmed absent.

## What this run does not claim

- **Does not exercise nuget.org.** No OIDC exchange, no NuGet Trusted Publishing, no
  `nuget-release` environment gate -- that machinery was proven once, for two packages, at the real
  `0.1.0-preview.1` tag push (see that section above). Adding `InTest.Runtime.MSTest` to that
  *live* flow remains unproven until an actual tag is pushed; this run only proves the
  packing/verification/artifact-count logic (`pack-and-verify.ps1`, `release.yml`'s asset-count
  assertion) is correct, not that nuget.org will accept a third package under this account.
- **Only `Catalog.Api` ran live this run.** No auth, the simplest of the three samples. It was
  sufficient to prove the crux (a live HTTP run against the split three-package build actually
  passing), but `Orders.Api`'s auth path (401/403 against `AuthHandler`/`ITestTokenProvider`) and
  `Inventory.Api` were not re-run live against the split runtime specifically in this session --
  both were already proven live against the *published* two-package `0.1.0-preview.1` in the "Full
  Phase 0-8 walkthrough" section above, which this run does not repeat or supersede.
- **A local manifest `dotnet tool restore` was exercised, not a global `dotnet tool install -g`
  against the local feed.** Deliberate, matching `local-e2e-test.ps1`'s own design and
  `getting-started.md`'s own guidance: a global install is documented as the *published*-prerelease
  path (Phase 0/2), while a collision-proof local version restored through the project's own local
  manifest is the documented way to iterate on a change ahead of the last published tag (Phase 8's
  "stale local package cache" section) -- and the "Full Phase 0-8 walkthrough" run above already
  found that `dotnet tool install -g` populates the *default* global packages folder regardless of
  `NUGET_PACKAGES`, which is exactly the isolation this run exists to preserve.
- **The URL-sourced `spec.source` / committed `spec.json` snapshot path was not exercised.** A
  local file spec was used throughout, matching `local-e2e-test.ps1`'s own scope.
- **`generate --check`'s exit `4` was exercised via a contrived `intestVersion` edit**, the same
  method every prior acceptance run in this document has used -- there is no real "next" published
  tool version to bump to yet, so a genuine version-drift scenario (two real, different published
  versions) remains unconstructed, same limitation `v1-e Task 6` already recorded for its own
  contrived-mismatch step.
- **No CI pipeline ran any of this.** One local machine, real processes, real HTTP -- not "in a real
  pipeline," the gap this document has carried open since v0.

## Cleanup

- `samples/Catalog.Api`, the only sample process this run started, was stopped -- via
  `taskkill /IM dotnet.exe /T`, which is untargeted: it force-killed *every* `dotnet.exe` process on
  the machine at that moment, not only the one PID this run launched. No other work was observed
  disrupted, but this is a blunter instrument than intended and worth a narrower replacement (e.g.
  killing the specific PID tree) for whoever repeats this.
- Both scratch temp directories (`%TEMP%\intest-local-e2e-*`) removed.
- Global NuGet package cache: see "Package cache: confirmed clean" above.

## Repo suite afterward

`dotnet test InTest.sln` at `HEAD = d152412` (unchanged by this run under `src/`/`tests/` -- only
`scripts/local-e2e-test.ps1`, `.github/workflows/pack.yml` and `.github/workflows/release.yml`
were touched):

```
Passed!  - Failed: 0, Passed:  12, Skipped: 0, Total:  12 - InTest.Architecture.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 253, Skipped: 0, Total: 253 - InTest.Runtime.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 628, Skipped: 0, Total: 628 - InTest.Cli.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  50, Skipped: 0, Total:  50 - InTest.Golden.Tests.dll (net10.0)
```

**943 passing, 0 failed, 0 skipped** -- identical to the pre-run baseline.

## Three-package-split acceptance actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Prove a freshly scaffolded project restores `InTest.Runtime.MSTest` *and* `InTest.Runtime` transitively at the exact same locally-packed version | this run | **Closed** -- Step 2 above, verified via `dotnet list package --include-transitive` |
| 2 | Run a generated suite live, over real HTTP, against the three-package split build | this run | **Closed** -- Step 3 above, Catalog 13 of 13 |
| 3 | F15 -- `local-e2e-test.ps1`'s cache-clean confirmation could print even when the tripwire had just found a dirty cache | this run | **Closed** -- `$CacheClean` flag added, negative-controlled by Step 4's second run |
| 4 | Confirm `release.yml`/`pack.yml` pack and push all three packages, not two | this run | **Closed** -- three-package-complete already; stale two-package step name/comment fixed alongside F15 |
| 5 | Exercise `Orders.Api`/`Inventory.Api` live against the split three-package runtime specifically (not just the published two-package build) | future run | Open -- see "What this run does not claim" |
| 6 | Prove the third package publishes for real (OIDC/Trusted Publishing/`nuget-release` gate against `InTest.Runtime.MSTest`) | the actual tag push | Open -- cannot be simulated locally by design |

---

# Framework-pack acceptance run

**Task:** prove the xUnit and NUnit framework packs against real HTTP. Every check in PR #10 and
PR #11 was static or ran against an in-process stub; **no generated non-MSTest suite had ever made a
real HTTP request** before this run. Run before tagging `0.1.0-preview.2`, deliberately: a NuGet
version is permanent, so anything this run could still change had to be found first.

## Environment

All four samples started from their own `launchSettings.json` with no hand-set environment
variables — the F9 condition the v0 run recorded is genuinely fixed. Verified by measurement, not
by `/health/ready`, which is anonymous and passes even when the identity pairing is wrong:

| check | result |
|---|---|
| `GET /api/orders`, no `Authorization` | **401** |
| same, full-access `orders-client` token | **200** |
| `POST /api/orders`, read-only `orders-readonly` token | **403** |

Runs were **sequential, never concurrent** — the same discipline v1-b established (not
concurrently — §11). These APIs share one SQLite store; two suites at once produce
interference that reads as a product defect.

## Results

| Suite | Framework | Result |
|---|---|---|
| Catalog | xUnit | **13 of 13** |
| Orders | xUnit | **24 total — 20 passed, 0 failed, 4 skipped** |
| Catalog | NUnit | **13 of 13** |
| Orders | NUnit | **24 total — 20 passed, 0 failed, 4 skipped** |

Both reproduce the MSTest figures recorded for the full Phase 0-8 walkthrough exactly. Verified from
each `.trx` directly, not from the runner's summary line.

## The skip path, which is why Orders matters more than Catalog

Skip is the seam that differs most between adapters: MSTest `Assert.Inconclusive`, xUnit
`Assert.Skip`, NUnit `Assert.Ignore`. All three reduce to the neutral layer's reason `string?`
(null meaning "run"), and all three produced the same outcome here.

The four skipped cases are the **GET** siblings — `GetApiOrders_Forbidden`,
`GetApiOrdersId_Forbidden`, `GetApiCustomers_Forbidden`, `GetApiCustomersId_Forbidden` — each
with the same reason:

> Skipped: the secondary identity 'orders-readonly' holds orders.read, which this operation
> requires, so it cannot produce a 403. Declare different scopes on that identity, or leave
> Scopes null to run this test anyway.

The three **write** cases — `PostApiOrders_Forbidden`, `PostApiCustomers_Forbidden`,
`DeleteApiOrdersId_Forbidden` — **ran and passed** against real 403s. That contrast is the
whole point: `RequireSecondaryIdentityLacks` decides per operation. A blanket skip would have shown
seven skips and still reported "green", which is precisely the vacuous-suite failure §16 exists to
prevent.

## `[error-is-the-sink]`, confirmed outside a unit test

NUnit's `.trx` carries two `<RunInfo>` entries holding InTest's own assembly-scope diagnostics
(`InTest run id: ...`, `All fixtures resolved cleanly.`). `InTest.Runtime.NUnit.Tests` already proved
`TestContext.Error` is the only sink that survives a passing run; this is the same claim holding in a
real VSTest run rather than a subprocess grep. Every rejected candidate fails **silently**, so this
is worth having twice.

## F16 — NUnit's `.trx` `<Counters>` under-reports skips - **not a product defect**

The Orders NUnit `.trx` reports `total="24" executed="20" ... notExecuted="0"` while the same file
contains four `<UnitTestResult ... outcome="NotExecuted">` entries. The per-result data is right; the
summary attribute is not. This is NUnit3TestAdapter's trx writer, not InTest — InTest writes
no `.trx`.

**CI is unaffected, checked rather than assumed:** `scripts/ci/assert-trx-results.ps1` guards on
`total` (it fails a run reporting `total=0`), which NUnit reports correctly. Recorded so a future
reader comparing the counter against the result list does not conclude four tests vanished.

## F17 — the sample stores are never reset - **environmental**

The SQLite stores still hold rows from acceptance runs dated 2026-08-19 onward. Consequences, both
handled at the fixture level with no product code touched:

- Catalog's seeded "deletable" category `33333333-...` **was already deleted** by an earlier run, so
  `delete_api_categories_id` must point at a freshly created category rather than the seed row the
  committed example uses.
- POST fixtures collide with real unique indexes on `sku`/`name`/`email`/`reference`, so each run
  needs values distinct from every previous run's.

`getting-started.md` already names the unreset-store condition; what is new is that **the committed
examples' own fixture values no longer work against it**. Anyone following the examples verbatim on
this machine hits a 404 and a 409 before any InTest behaviour is in question.

## What this run does not claim

- **Not a publish.** Nothing was pushed to nuget.org; `release.yml` remains unexercised at five
  packages. `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit` are not published, so both scaffolds
  substituted a `ProjectReference` for the scaffolded `PackageReference`. A real adopter's path
  still has not been walked for either framework.
- **Not Inventory.** Only Catalog and Orders were run per framework. Inventory adds a third producer
  and string/int route parameters, not a new adapter path, and MSTest already covers it 9 of 9.
- **Not a fresh store.** See F17 — these numbers come from a long-lived database, not a
  clean one.

---

# 0.1.0-preview.2 publish acceptance run

**Date:** 2026-08-31 · **Commit:** `b7fab09` (tagged `0.1.0-preview.2`, tag pushed by the
repository owner — [tag-is-the-release] stays a human decision; nothing in this run or in
`release.yml` cut the tag itself)
**Task:** the second real tag push, and the first at the five-package shape the
**0.1.0-preview.1 publish acceptance run** and the **Three-package split** and **Framework-pack**
acceptance runs above all named as unproven: neither a locally-simulated pack (Three-package
split) nor an in-process/`ProjectReference` run (Framework packs) can stand in for a real
`release.yml` publish, because only nuget.org's own acceptance of an artifact — and a real restore
against it — proves the artifact is actually installable. This run is that proof, for
`InTest.Runtime.MSTest`, `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit`'s first publish.

## What was exercised

| Step | What | Result |
|---|---|---|
| 1 | `git tag 0.1.0-preview.2` on `b7fab09`, pushed | Triggered both `pack.yml` and `release.yml` |
| 2 | `release.yml`'s three jobs: `Pack (verify against tag)`, `Publish to nuget.org`, `Create GitHub Release` | All green — **first run at five packages** (the previous tag published two) |
| 3 | GitHub Release for `0.1.0-preview.2` | Marked prerelease, carries exactly **10 assets** (five `.nupkg` + five `.snupkg`) — `release.yml` asserts that count from a derived package-id list, not a literal |
| 4 | `dotnet tool install -g InTest.Cli --version 0.1.0-preview.2` | Installs and runs, reporting `0.1.0-preview.2+b7fab09cc78c5ec65563cd21d3bed74635c53d2c` — the commit SHA matches the tagged commit exactly |
| 5 | All three adapters (`InTest.Runtime.MSTest`, `InTest.Runtime.xUnit`, `InTest.Runtime.NUnit`) restored from nuget.org, `dotnet list package --include-transitive` | Each resolves **`InTest.Runtime 0.1.0-preview.2`** — the §3 compatibility contract holding at the published version |

One propagation-lag data point, worth recording because it reads exactly like a broken release
rather than what it is: packages were live on the flat-container API roughly **four minutes**
before `dotnet tool install` could resolve them, failing with "not found in NuGet feeds" for the
whole of that window. This is ordinary NuGet registration-index lag, not a failed publish — the
same distinction CONTRIBUTING.md's "Ask the thing that decides" section asks for elsewhere: the
symptom looks identical to a broken push, and the only way to tell them apart was to keep
retrying against the real feed rather than concluding the publish had failed from the first
attempt alone.

## What this closes

**The five-package, ten-artifact shape is now proven, not just designed.** The
0.1.0-preview.1 publish acceptance run above closed the two-package case — `InTest.Cli` and
`InTest.Runtime` restoring from a bare clone — and named the three-package extension as the next
tag push's job. The Three-package split acceptance run went as far as a *simulated* local publish
could take it, and the Framework-pack acceptance run proved the xUnit and NUnit adapters' generated
code against real HTTP but with a `ProjectReference` standing in for the unpublished package. This
run is the first time all three adapters were installed the way a real adopter installs them —
restored from nuget.org itself, not packed locally and not substituted around — and the first time
`dotnet list package --include-transitive` confirmed the §3 compatibility contract
(`InTest.Runtime.MSTest`/`.xUnit`/`.NUnit` **N.x** accepting code generated by `InTest.Cli` **N.y**)
against packages nuget.org actually served, rather than a local feed.

**CONTRIBUTING.md's "Also unproven by this run" note, attached to the 0.1.0-preview.1 record, is
now closed.** That note said the runtime-framework split and the later xUnit/NUnit additions "all
landed after it, so no tag push has yet published the five-package, ten-artifact shape" — this tag
push is that test, and it passed.

## What this run does not claim

Stated directly, in the same register as the two publish/framework runs above, because
overstating it is exactly the failure mode this document exists to avoid:

- **One tag, one push, one runner.** This is a second data point, not proof that every future tag
  push behaves identically. `release.yml`'s `publish` job still runs `ubuntu-latest` only, by
  design (unchanged from the 0.1.0-preview.1 run) — this run says nothing new about
  `windows-latest` runners specifically publishing anything.
- **Still prereleases only.** `0.1.0-preview.2` is a prerelease, same as `0.1.0-preview.1`; nothing
  about a `0.1.0` stable tag's publish path has been exercised by either run.
- **Not a fresh full-adoption walkthrough.** This run reproduces the installability and transitive-
  resolution steps of Phase 8, not a complete Phase 0–Phase 8 pass against the newly-published
  adapters — the Full Phase 0-8 walkthrough above did that for the two-package shape at
  `0.1.0-preview.1`; it has not been repeated end to end against all three frameworks at
  `0.1.0-preview.2`.
- **Not a live-HTTP run.** This run is about the publish mechanism and package resolution, not
  about running a generated suite against `samples/` — the Framework-pack acceptance run above
  already covers Catalog and Orders under xUnit and NUnit live, against a `ProjectReference` build
  from before this tag existed; that evidence is not re-established against the published packages
  specifically by this run.
- **`examples/` still pins the pre-publish shape.** Retargeting `examples/` to the newly-published
  adapter packages is separate, ongoing work (tracked outside this run) and is deliberately not
  claimed here — see this update's own report for the exact clauses left alone pending that work.
- **The Publishing checklist's remaining human steps are unaffected**, in the same way the
  0.1.0-preview.1 record already stated: a required-reviewer gate on the `nuget-release`
  environment remains recommended and not confirmed configured. `PackageValidationBaselineVersion`
  for `InTest.Runtime` was due starting with this release and, checked directly against
  `src/InTest.Runtime/InTest.Runtime.csproj` while writing this record, is **not present** — see
  CONTRIBUTING.md's Publishing checklist item 11 for the same finding stated where the rule lives.

## 0.1.0-preview.2 publish actions

| # | Action | Owner phase | Status |
|---|---|---|---|
| 1 | Prove the five-package, ten-artifact shape publishes for real, closing the gap the 0.1.0-preview.1 record left open | this run | **Closed** — see "What was exercised" above |
| 2 | Confirm all three adapters resolve `InTest.Runtime` transitively at the exact published version, against nuget.org rather than a local feed | this run | **Closed** — Step 5 above |
| 3 | Add `<PackageValidationBaselineVersion>` to `InTest.Runtime`'s project file, pointing at `0.1.0-preview.1` (due starting with this release, per CONTRIBUTING.md's Publishing checklist item 11) | this release | **Closed** — done differently than originally described here: set centrally in `Directory.Build.props` (not per-project) at `0.1.0-preview.2` (not `0.1.0-preview.1`, which regresses three already-shipped, already-`CHANGELOG.md`-recorded runtime/adapter-split members) for all four class libraries at once, guarded against silent removal by `InTest.Architecture.Tests`' `PackageValidationBaselineTests`. See `Directory.Build.props`'s own `[package-validation-baseline]` comment and CONTRIBUTING.md's Publishing checklist item 11 for the full reasoning. |
| 4 | Retarget `examples/` to reference whichever adapter package now matches each example's `project.framework`, now that all three exist on nuget.org | separate, ongoing work | Open — deliberately out of scope for this update |
| 5 | Re-run the full Phase 0–Phase 8 adopter walkthrough against `0.1.0-preview.2` specifically, across all three frameworks, the way the 0.1.0-preview.1 record's item 6 did for the two-package shape | pre-v1 release readiness | Open |
| 6 | Confirm a required-reviewer gate is configured on the `nuget-release` environment | repository owner, GitHub Settings | Open — recommended, not confirmed configured, same gap every earlier publish record in this document already names |
