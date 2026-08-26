# Getting started

End-to-end walkthrough: from an existing .NET API with an OpenAPI document, to a committed
integration test suite running as a post-deployment gate.

> **Phase 0 (`survey`) does not exist yet, nor does `fixtures promote` within Phase 5.
> Everything else below does, including `--check` and `upgrade` within Phase 8.**
>
> `init`, `generate` and `fixtures repair` (Phase 5) work end to end and are verified against
> live APIs, request bodies included, by the acceptance run below — Catalog and Inventory pass in
> full, twice each, against an unreset store. Orders — the one sample with declared `security` —
> now generates 24 tests: **0 failed, 4 skipped, 20 passed**. The 4 skips are the wrong-scope 403
> cases whose secondary identity already holds the scope the operation requires — the runtime
> guard (`RequireSecondaryIdentityLacks`, see the Auth section below) skips each with a stated
> reason instead of running it against a request that identity is genuinely authorized for
> (**F11, closed**; see [`v0-acceptance.md`](v0-acceptance.md), which records the run this closed
> on). The 3 write-scope 403s — the cases the sample's identity pair can actually prove — ran and
> passed. `generate --check` and `upgrade` (Phase 8) also work end to end — walking this document
> against a live sample after they shipped is how the defects a later review found were caught —
> but that acceptance run predates both commands and has not yet been extended to exercise them;
> [`v0-acceptance.md`](v0-acceptance.md) says so directly in its own "still open" list rather than
> implying coverage it does not have. That same run is also why every command below Phase 2 is
> shown as `dotnet intest …` rather than bare `intest` — the acceptance run installed the packaged
> tool for the first time in this project's history and found bare `intest` does not resolve
> against the local manifest `init` scaffolds (**F13, closed**; see `v0-acceptance.md`). Phase 2
> explains why. Not yet built: `survey`
> (Phase 0), `fixtures promote` (Phase 5), `assertions add`, `generate --emit-plan`,
> variation tests, and YAML input — from a file or a URL alike. A URL `spec.source` **is** built
> (Phase 1): `generate` snapshots it to a committed `spec.json`.
> `InTest.Cli` and `InTest.Runtime` `0.1.0-preview.1` are published to nuget.org as a
> prerelease — see the Prerequisites and Phase 2 below for what that does and does not change
> about this walkthrough.
>
> The walkthrough is kept whole rather than trimmed to what ships, because tracing it end to end
> is what finds gaps — it is how the unowned creation of the first fixture files was caught, and
> how the v0 acceptance run found four defects. If you spot another, that is the most useful
> thing you can send us.
>
> Design detail lives in the [specification](superpowers/specs/2026-08-16-intest-api-test-generator-design.md);
> section references like §10 point there.

Running example: an `Orders` API using Swashbuckle, deployed to a `staging` environment.

## Prerequisites

| | |
|---|---|
| .NET SDK | 10.0 or later — the **test project** targets `net10.0`; your API can target anything |
| Test framework | MSTest. xUnit and NUnit are not supported in v1 |
| Spec | OpenAPI 3.x, JSON — a **local file**, or a URL InTest can reach anonymously. YAML is not built yet |
| API | Deployed and reachable from wherever the tests run |

`InTest.Cli` and `InTest.Runtime` `0.1.0-preview.1` are published to nuget.org as a prerelease
— a global `dotnet tool install -g InTest.Cli --version 0.1.0-preview.1` gets you a working
`intest` on `PATH` for Phase 0/2 without building anything. Building from source is still how
you get any change past that tag.

---

## Phase 0 — decide whether to adopt

Before scaffolding anything, find out what adoption will cost you.

> **`survey` is not built yet** (see the banner above) — skip this command and go straight to
> Phase 1; everything from there on works today.

```bash
dotnet tool install -g InTest.Cli
intest survey "https://orders-staging.example.com/swagger/v1/swagger.json"
```

`survey` takes a glob over local files or a URL directly — broader than `spec.source`, which
names one document — because when you are still deciding whether to adopt, a Swagger endpoint is
often all you have. It reads specs and reports; it writes nothing. What it tells you and why you
care:

| Measure | What it means for you |
|---|---|
| % with `operationId` | How many test names are synthesized from method and path rather than taken from the spec |
| % with request `example` | **Your fixture workload.** The single biggest number here — see Phase 5 |
| % with response schemas | How many tests assert a full contract vs. status code only |
| % with `security` declared | How many auth tests you get |
| OpenAPI 3.0 vs 3.1, keyword census | Whether any schema uses keywords the validator cannot evaluate (§9) |

Low `example` coverage is not a blocker, but it is work. Better to know now than in Phase 5.

---

## Phase 1 — make the spec available

### Same repository as the API

Emit the document at build time so it is always current.

| Producer | Add | Note |
|---|---|---|
| Swashbuckle | `Microsoft.Extensions.ApiDescription.Server` | — |
| Built-in `Microsoft.AspNetCore.OpenApi` | Native build-time generation | JSON only; YAML at build time is not supported yet |
| NSwag | `NSwag.MSBuild` | Set `NoBuild=true` or the build recurses |

The document lands somewhere like `Orders/bin/Debug/net10.0/orders.json`. Pointing InTest at a
build artifact is correct — it cannot go stale.

### Different repository, or only a URL

Point `spec.source` at the URL. `generate` fetches it and writes the document into your project
as `spec.json` — a committed snapshot — so a spec change still arrives as a reviewable diff on
the pull request (§9).

```bash
intest init --name Orders.ApiTests --spec https://orders-staging.example.com/swagger/v1/swagger.json
```

Four things worth knowing before you rely on it:

- **Commit `spec.json`.** It is what `generate --check` compares against in Phase 8, and it is
  the only thing that gives a URL-sourced spec a diff at all. Leave it uncommitted and Phase 8
  fails against a file that is not in the repository.
- **Only `generate` fetches.** `generate --check` and `fixtures repair` read the committed
  snapshot and never open a socket, so CI needs no access to your API to verify the suite is up
  to date. Refreshing the spec is a deliberate act, on a branch, where you can see what changed.
- **A failed fetch fails the command.** If the URL is unreachable, `generate` exits `2` and
  leaves the existing snapshot untouched rather than quietly regenerating against it — a stale
  spec that looks like a fresh one is the failure this tool is least willing to ship.
- **The fetch is anonymous**, so an endpoint behind auth returns `401`/`403` and InTest says so.
  That is the one case where you still fetch by hand:

  ```bash
  curl -o specs/orders.json https://orders-staging.example.com/swagger/v1/swagger.json
  ```

  Then point `spec.source` at `specs/orders.json` and refresh it yourself. **YAML endpoints take
  this route too** — YAML is not built yet, from a file or a URL, and InTest refuses one by name
  rather than failing as a parse error.

Pointing InTest at a build artifact is still the better option where you have one: it cannot go
stale, and it needs no network at all.

---

## Phase 2 — scaffold the test project

```bash
mkdir Orders.ApiTests && cd Orders.ApiTests
intest init --name Orders.ApiTests --spec ../Orders/bin/Debug/net10.0/orders.json
```

`init` refuses to overwrite anything that already exists (exit `3`). It writes:

| File | Owner | Purpose |
|---|---|---|
| `intest.json` | yours | Configuration |
| `Orders.ApiTests.csproj` | yours | Pins packages — including `InTest.Runtime.MSTest`, which brings in `InTest.Runtime` transitively at the same version — copies the spec to output, sets `RunSettingsFilePath`, adds the `INTEST0001` guard |
| `AssemblyInfo.cs` | yours | `[assembly: DoNotParallelize]` — the **only** place parallelization is declared |
| `TestStartup.cs` | yours | DI registrations, named `HttpClient`, handlers |
| `OrdersTestBase.cs` | yours | Your shared helpers; derives from `ApiTestBase` |
| `appsettings.json`, `appsettings.staging.json` | yours | Profiles, base URLs, readiness |
| `Orders.ApiTests.runsettings` | yours | Named after the project (`<Name>.runsettings`), not the API — ships with `profile` **commented out**, see Phase 3 |
| `.config/dotnet-tools.json` | yours | Pins the CLI version so CI and your machine agree |
| `.gitattributes` | yours | Pins `Generated/`, `coverage-report.json` and `fixtures/**/*.json` to CRLF, so a clone with `core.autocrlf=input` — which normalizes to LF on commit but does not re-expand it back to CRLF on checkout, unlike `core.autocrlf=true` — cannot flatten them to LF and fail `generate --check` on every line |

Everything above is yours to edit and is never regenerated.

**Every command from here on is `dotnet intest …`, not bare `intest`.** `.config/dotnet-tools.json`
is a **local** tool manifest (`isRoot: true`) — that is the whole reason it exists: it pins the
exact CLI version so CI and your machine restore the same one, and CI proves it by running
`dotnet tool restore` (Phase 8) before anything else. A local manifest is never added to `PATH`,
unlike a global install's shim directory, so after a real restore bare `intest` is simply not
found — confirmed cross-shell, Git Bash and PowerShell both reject it the same way. `dotnet
intest …` is the SDK's short form for a manifest-restored local tool (available without `dotnet
tool run` since .NET 7); `dotnet tool run intest …` is the longer equivalent, and both resolve the
version this project just pinned, where a stray global copy on `PATH` would not (**F13, closed**;
see [`v0-acceptance.md`](v0-acceptance.md) for the experiment). `init` above is the one exception,
shown bare: it is what *creates* the manifest, so there is nothing yet to restore against.
Getting a working `intest` on `PATH` to run `init` at all is now an ordinary global install —
`dotnet tool install -g InTest.Cli --version 0.1.0-preview.1` resolves against the published
prerelease on nuget.org, no local feed required (see `docs/v0-acceptance.md`'s publish record).
Building from source remains the only way to pick up anything past that tag.

---

## Phase 3 — configure

### Base URL

In `appsettings.staging.json`:

```json
{ "Api": { "BaseUrl": "https://orders-staging.example.com/api/" } }
```

**Keep the trailing slash.** `https://host/api` + `orders/1` resolves to `https://host/orders/1`
— the `api` segment is silently dropped, and you get a green suite hitting the wrong routes.
InTest normalizes this, but the configured value is yours and worth getting right.

### Choosing a profile

Precedence, first match wins:

1. `.runsettings` → `TestRunParameters` → `profile`
2. Environment variable `INTEST_PROFILE`
3. Default in `appsettings.json`

The scaffolded `Orders.ApiTests.runsettings` leaves `profile` commented out on purpose. Uncomment it and
tier 1 always matches, making `INTEST_PROFILE` unreachable. Pin the profile only in
environment-specific files like `qa.runsettings`.

### Secrets

Never in `intest.json`, never in fixtures. Register providers in `TestStartup.cs` — user-secrets
locally, whatever your organisation uses in CI — and reference them from fixtures as
`{{config:Orders:ApiKey}}` (§10).

### Auth

`AuthHandler` is already attached to `InTestClients.Api`. It reads the ambient identity for
each test, asks the registered `ITestTokenProvider` for a token, and sets `Authorization` before
the request goes out. A secured API needs exactly one thing from a team: an implementation of
the interface.

```csharp
public sealed class OrdersTokenProvider : ITestTokenProvider
{
    public IReadOnlyList<TestIdentity> Identities { get; } =
    [
        new TestIdentity("orders-client", ["orders.read", "orders.write"]),
        new TestIdentity("orders-readonly", ["orders.read"])
    ];

    public Task<string> GetTokenAsync(string audience, string? identity = null,
                                      CancellationToken ct = default) => /* ... */;
}
```

Register it in `TestStartup.Register`:

```csharp
private static void Register(IServiceCollection services, IConfiguration configuration)
{
    services.AddSingleton<ITestTokenProvider, OrdersTokenProvider>();
}
```

**Do not also append a `DelegatingHandler` of your own** to `InTestClients.Api` for auth. Two
handlers both setting `Authorization` does not fail loudly — the one registered last silently
wins, and whichever one lost looks, from the outside, like it was never called.

`Identities` decides which auth tests run, and for the "wrong scope → 403" cases, so does each
identity's declared `Scopes`. Return one identity and every "wrong scope → 403" case skips at run
time with a stated reason (`RequireMultipleIdentities`, §9). Return two or more and each case
runs unless the second identity's `Scopes` already cover everything the operation requires, in
which case `RequireSecondaryIdentityLacks` (§9) skips it instead. A read-only second identity
like `orders-readonly` above is the common case for a real API, and without declaring its
`Scopes`, its read operations' "wrong scope → 403" tests cannot pass — there is nothing for the
guard to skip them on, so they run against a request that identity is genuinely authorized for,
and fail. `Scopes` has two distinct empty-looking shapes and the guard treats them differently
from a non-empty declaration but identically to each other: leaving it `null` means "not
declared" — unknown, so the test runs — and declaring it `[]` means "declared, and holds none" —
also always runs, since an empty set can never cover a non-empty requirement. Only a declared,
non-empty `Scopes` that actually covers everything an operation requires makes the guard skip.
The "no token → 401" cases always run regardless of how many identities a provider
advertises or what scopes they hold. InTest ships only a static-token provider — no cloud SDK, no
identity library — so anything past one identity is the team's to write.

**Readiness never depends on any of this.** It probes on `InTestClients.Readiness`, a client
with no auth handler attached at all, so an unreachable identity provider cannot make the
anonymous `/health/ready` probe fail before a single token is ever requested — the failure mode
that reads as a two-minute-long "dead API" and is actually a dead identity server (§13). The
requirement runs the other way too: the readiness client carries no auth handler and can *never*
send a token, so `/health/ready` itself must stay anonymous on your API. Put it behind
authorization and readiness fails before the first test runs, for every run, on every machine —
not a misdiagnosis this time but a hard block. The samples model this: `Orders.Api` — the one
sample with auth at all — marks its `/health/ready` `.AllowAnonymous()` alongside every
authorized endpoint it declares.

### Typed client invocation (opt-in)

If your team already owns a pre-generated API client for this API — Kiota, NSwag, or Refit — you
can opt generated tests into calling it instead of building `HttpRequestMessage` by hand. Add a
`client` section to `intest.json`:

```json
{
  "client": { "kind": "kiota", "typeName": "Orders.ApiClient.OrdersApiClient" }
}
```

`kind` is `"kiota"`, `"nswag"` or `"refit"`; `typeName` is the client's fully-qualified C# type
name, exactly as you would write it in code. Leave the section out entirely and nothing changes —
every generated case keeps building its own `HttpRequestMessage`, byte-for-byte identical to a
project with no `client` section at all.

**Own a generated client but not the OpenAPI document it came from?** Kiota already recorded where
it generated from, in `kiota-lock.json` — point `init` at it instead of hunting the document down
by hand:

```bash
intest init --name Orders.ApiTests --client-lockfile ../Orders.ApiClient/kiota-lock.json
```

This recovers `spec.source` from the lockfile's `descriptionLocation` (a local path or a URL,
either way) and, since it also names `clientClassName`/`clientNamespaceName`, scaffolds a working
`client` section too — no hand-editing needed for the common case. `--spec` and
`--client-lockfile` are mutually exclusive; give neither and `init` refuses exactly like a blank
`--spec` always has. NSwag's own config (`nswag.json`) is not supported here — measured directly
(NSwag 14.7.1's default `nswag new` output), its `className` is a naming *template*
(`"{controller}Client"`) under NSwag's default generation mode, not the single concrete type name
a `client.typeName` needs, so there is nothing safe to recover automatically.

**The one thing that matters more than the config: register the client over InTest's own
`HttpClient`, or it silently stops working.** `AuthHandler`, `RunIdHandler`, and the handler that
lets a client-routed test still validate the raw response bytes are all attached to one named
client, `InTestClients.Api` — the same one `Client` itself resolves from. A typed client built
over any other `HttpClient` never passes through those handlers at all: no run-id header, no auth
token, and (worse) no captured response for the assertion to check against, which fails loudly
with a message naming the cause rather than passing against nothing — but only *after* you have
spent time wondering why a "working" client fails every test. Construct it in
`TestStartup.Register`, resolving the named client from `IHttpClientFactory` exactly the way
`Client` itself is resolved:

```csharp
private static void Register(IServiceCollection services, IConfiguration configuration)
{
    // Kiota — HttpClientRequestAdapter needs no auth provider of its own; AuthHandler already
    // sets the Authorization header on every request that passes through InTestClients.Api.
    services.AddTransient(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = httpClient.BaseAddress!.ToString()   // must agree with Api:BaseUrl — see below
        };
        return new OrdersApiClient(adapter);
    });
}
```

```csharp
// NSwag — most generated clients take (string baseUrl, HttpClient httpClient) directly.
services.AddTransient(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
    return new OrdersClient(httpClient.BaseAddress!.ToString(), httpClient);
});
```

```csharp
// Refit — RestService.For<T> reads BaseAddress off the HttpClient it is given, so nothing else
// needs pointing anywhere.
services.AddTransient(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
    return RestService.For<IOrdersApi>(httpClient);
});
```

**Why the base URL has to agree, and why a mismatch is a hard failure rather than a quiet one.**
`HttpClient.BaseAddress` only resolves a *relative* request URI. Kiota's adapter (and some NSwag
clients) build a fully-qualified, absolute URI from their own configured base URL instead — so if
that base URL disagrees with `Api:BaseUrl`, `HttpClient.BaseAddress` is never even consulted, and
the request goes wherever the client's own configuration points, silently ignoring the
`appsettings.*.json` value you actually meant to hit. InTest cannot stop that request from being
sent, but it does refuse to let it pass unnoticed: the handler that captures the response compares
the outgoing request's authority against `Api:BaseUrl` and throws a message naming the exact
cause the moment they disagree, rather than letting the test run against the wrong host and pass
or fail for the wrong reason.

**Which operations get routed through the client, and which need `client-map.json`.** Only
`Success` cases ever route through a client — a 404 or a 401/403 case is testing what your *API*
does with a bad request, not what your client does with one, so those always build
`HttpRequestMessage` directly regardless of this section. Among `Success` cases, `generate`
derives the call automatically today only for Kiota, and only for an operation with **no query
parameters and no request body** — Kiota binds query parameters through a
`RequestConfiguration` lambda and takes a typed model object for a request body, and there is no
fixture value InTest could safely splice into either shape. `coverage-report.json` (Phase 4) notes
every operation this withholds convention for, and points at `client-map.json`.

Add an entry there to route anything convention does not cover — a query-parameter operation, a
POST, or any NSwag/Refit operation at all (neither gets an automatic convention in v1; see
`docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md` for the measured reason
NSwag's generated methods cannot be spliced into safely):

```json
{
  "overrides": {
    "getOrderById": "Api.Orders[{id}].GetAsync"
  }
}
```

Keyed by operation key, one entry per operation you want routed through the client. The value is
a real C# expression — everything after `ApiClient<T>()`. `{param}` placeholders are replaced with
the same per-operation fixture value the raw-HTTP path already resolves; wrap it in parentheses
with any additional argument the call needs. A wrong expression fails your own next `dotnet
build` at the generated line — the same way a typo in any other line of your test project would —
rather than being silently accepted.

---

## Phase 4 — generate

```bash
dotnet intest generate
```

Writes `Generated/` — one `.g.cs` class per tag (`OrdersTests.g.cs` here), `spec-paths.json` and
`spec-schemas.json` — plus `coverage-report.json` at the project root. All regenerated wholesale;
never hand-edit them. `TestHost` itself is not generated — it ships in `InTest.Runtime.MSTest`
(a thin facade over `InTest.Runtime`'s neutral `InTestRun`) and `TestStartup.cs` delegates
`[AssemblyInitialize]` to it (Phase 2).

Read `coverage-report.json` now. It tells you what was skipped and why, which operations run on
synthesized IDs, which produce status-only tests, and which auth tests are gated on a second
identity.

If any operation needs a fixture — a request body, or a required path/query parameter — `generate`
exits non-zero and reports the missing fixtures. That is expected on a first run: even a bodiless
`GET /products/{id}` needs one, for `id`.

---

## Phase 5 — fixtures

```bash
dotnet intest fixtures repair
```

The only command that writes under `fixtures/`. It creates missing fixtures, adds `TODO:`
sentinels for newly-required properties and parameters, flags properties that left the schema,
and **never overwrites a value you wrote**.

Now the real work. A generated fixture looks like:

```jsonc
{
  "$meta": { "tier": 4, "operationId": "createOrder", "generatedBy": "intest 1.0.0" },
  "$parameters": {
    "id": "TODO:id"
  },
  "body": {
    "customerId": "TODO:customerId",
    "items": [ { "sku": "TODO:sku", "quantity": 1 } ]
  }
}
```

Path and query parameters live in the same file, under `$parameters` — there is no separate
`TestData` mechanism (§10). A path parameter always gets a value; an optional query parameter
appears only when the spec gives it an `example` or a `default`, and is otherwise omitted
entirely so the generated request never sends it.

**Tests fail while `TODO:` sentinels remain, by design.** The alternative is inventing
plausible values, which a permissive endpoint accepts — leaving a green suite that asserts
nothing. A red test gets fixed; a passing test that proves nothing never does. Failures are
aggregated into a single message at startup naming every unresolved sentinel and its file. Only
the operations that actually depend on a broken fixture fail — a bad fixture does not take down
tests that never touch it (§10).

Replace sentinels with real values, or with tokens:

| Token | Resolved |
|---|---|
| `{{config:Orders:ApiKey}}` | Once per run, from configuration — keeps credentials out of committed files |
| `{{runId}}` | Once per run |
| `{{utcNow}}` | Per request |
| `{{fixture:seededCustomer.id}}` | After every registered `IAssemblyFixture` completes — see below |

### Running a suite more than once

A fixture holds one literal value, so an operation that **creates** something creates the same
thing every run, and an operation that **deletes** something can only delete it once. Run the
sample Catalog suite twice against the same database and the second run drops from 9 of 9 to
6 of 9: two 409s on a duplicate name and a duplicate unique key, and a 404 deleting a row the
first run removed.

Plan for a reset target first — a database restored per run, an ephemeral environment, a
container started fresh. It needs no fixture work at all, and it is still the simplest fix.
Where that is not possible, two tools make a suite repeatable without one: `{{runId}}` for
free-form uniqueness, and `{{fixture:…}}` (next) for the two cases it cannot reach.

Every run mints a fresh `{{runId}}`, so any field with no format constraint can just include it:

```jsonc
"body": { "name": "Accessories-{{runId}}" }   // unique per run, so the 201 stays a 201
```

This is the right tool for free-form uniqueness, and it needs nothing beyond the token itself —
no registration, no code.

### Seeding data with IAssemblyFixture

`{{runId}}` cannot reach two cases: a field constrained to a fixed format — a SKU matching
`^[A-Z]{3}-[0-9]{4}$` has no room for a run id like `tjay-20260816T142233Z-a3f91c2e` — and an
operation that deletes seeded data, because nothing creates that row first. Both need something
seeded once, by code, before any test runs — an `IAssemblyFixture`:

```csharp
public sealed class SeededCustomerFixture(OrdersApiClient api) : IAssemblyFixture
{
    public Type[] DependsOn { get; } = [];
    public string[] AppliesTo { get; } = [];   // empty = every profile

    public async Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
    {
        var customer = await api.CreateCustomerAsync(ct);
        ctx.Publish("seededCustomer.id", customer.Id);       // now available to {{fixture:…}}
        ctx.OnCleanup(() => api.DeleteCustomerAsync(customer.Id));
    }
}
```

Register it in `TestStartup.cs`'s `Register` method:

```csharp
services.AddSingleton<IAssemblyFixture, SeededCustomerFixture>();
```

**Only `AddSingleton` is supported.** Registering a fixture `AddScoped` or `AddTransient` while
it also implements `IDisposable` sets a trap: the DI scope that resolves it during
`AssemblyInit` disposes it before the run really starts, while any `OnCleanup` closure it
registered lives on and still runs at `AssemblyCleanup` — against a fixture object that is
already disposed.

Then, wherever a fixture needs that seeded value:

```jsonc
"body": { "customerId": "{{fixture:seededCustomer.id}}" }
```

`{{fixture:…}}` resolves once every registered `IAssemblyFixture` has finished — `DependsOn`
orders fixtures that depend on each other's published values, and `AppliesTo` restricts a
fixture to specific profiles (a fixture skipped this way also skips anything that transitively
depends on it, logged by name). Reference a key nothing published, and it is reported the same
way an unfilled `TODO:` sentinel is (above): once, in the aggregated report — this is real
output from `FixtureValidation.Report.BuildMessage`, for a suite with two other fixtures still
carrying unfilled `TODO:` sentinels alongside `update-order.json`'s unpublished `{{fixture:…}}`:

```
5 problems found across fixtures. Run `intest fixtures repair` or fill them in by hand:
  create-order:
    - 'customerId' in create-order.json is still unfilled (TODO:customerId).
    - 'items[0].sku' in create-order.json is still unfilled (TODO:sku).
  ship-order:
    - 'carrier' in ship-order.json is still unfilled (TODO:carrier).
    - 'trackingNumber' in ship-order.json is still unfilled (TODO:trackingNumber).
  update-order:
    - 'customerId' in update-order.json: Fixture key 'seededTenant.id' required by 'update-order.json' is not published. Published keys: seededCustomer.id, seededRegion.code. Check the key name for a typo, or confirm the fixture that publishes it is registered and its AppliesTo includes the active profile.
```

— naming the requested key and every key that *was* published, and only the operations that
actually depend on it fail, not the rest of the suite.

**A `DependsOn` dependent can also read a published value directly, in code**, with
`ctx.Get(key)` — the supported alternative to a `{{fixture:…}}` token when a fixture needs the
value itself rather than embedding it in a request body:

```csharp
public sealed class SeededOrderFixture(OrdersApiClient api) : IAssemblyFixture
{
    public Type[] DependsOn { get; } = [typeof(SeededCustomerFixture)];
    public string[] AppliesTo { get; } = [];

    public async Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
    {
        var customerId = ctx.Get("seededCustomer.id");   // safe: DependsOn guarantees it already ran
        var order = await api.CreateOrderAsync(customerId, ct);
        ctx.Publish("seededOrder.id", order.Id);
        ctx.OnCleanup(() => api.DeleteOrderAsync(order.Id));
    }
}
```

`Get` is only safe against a key published by a fixture actually listed in `DependsOn` —
`FixtureGraph` orders fixtures so every dependency finishes before its dependent starts, which is
what makes the value be there at all. Calling it for a key nothing has published yet (a missing
`DependsOn` entry, or a typo) throws, naming the requested key and every key published so far, the
same way an unpublished `{{fixture:…}}` token does.

**`ctx.Publish` and `ctx.OnCleanup` are safe to call concurrently** from within one fixture's
`InitializeAsync` — for example, several `Task.WhenAll(...)` branches each seeding a different row
to keep `AssemblyInitialize` fast. `FixtureRunner` still runs different *fixtures* strictly one
after another; the concurrency guarantee is only about calls made from inside a single fixture.

Cleanup is registered next to what created it (`ctx.OnCleanup`, above) and drained in reverse
when `AssemblyCleanup` runs. Make every cleanup idempotent.

**Cleanup is best-effort, not guaranteed.** `AssemblyCleanup` does not run on a crash, a
cancelled pipeline, or an agent timeout (§14), and `IAssemblyFixture` does not remove the need
for the out-of-band sweeper described under "Things that will bite you" below — write one
regardless of whether you adopt fixtures.

**This does not make a suite runnable twice *concurrently*.** Two runs seeding at the same time
still collide on the same unique constraints — cross-process coordination is not solved at this
layer (§11). What this buys is sequential repeatability: run, then run again, without
hand-editing fixtures or resetting the environment in between.

### Reducing this work permanently

> **`fixtures promote` is not built yet** (see the banner above) — there is no command to run
> here today. The snippet below describes what it will produce once it exists.

```bash
dotnet intest fixtures promote
```

Prints a paste-ready snippet — an `ISchemaFilter`, an XML `<example>`, a transformer — for
adding examples to the API itself. It writes nothing, because `spec.source` is a build artifact
the next build would overwrite. Examples added there improve your Swagger UI and any generated
clients too, and every InTest run reports the percentage so the number visibly moves.

---

## Phase 6 — run

```bash
dotnet test
```

Startup order: build configuration → mint the run ID → build the service provider → load the
schema bundle → check the base URL for a repeated path prefix → wait for readiness → run
assembly fixtures → validate every fixture (§13).

Readiness matters more than it sounds. Post-deploy cold start is the largest single source of
flaky gates, so InTest polls until the service answers — by default requiring two consecutive
successes, because during a slot swap a single 200 can come from the old instance. It fails
with `Service did not become ready within 120s (last response: 503)` rather than 200 confusing
test failures.

Every request carries `X-Test-Run-Id: {TestId}`, so a failed gate run can be traced in your
telemetry down to the individual test.

---

## Phase 7 — commit

| Commit | Ignore |
|---|---|
| `Generated/`, `coverage-report.json` | `appsettings.local.json` |
| `fixtures/`, `intest.json` | user-secrets |
| `appsettings*.json` (non-local), `*.runsettings` | anything with a credential in it |
| `.config/dotnet-tools.json` | |
| `.gitattributes` | |
| **`spec.json`** — only when `spec.source` is a URL (Phase 1) | `spec.json` is **not** created for a local `spec.source`; the build copies that file instead |

Generated code is committed so a spec change arrives as a reviewable diff on the pull request,
where someone can see that an endpoint's contract moved.

**Commit `spec.json` if your `spec.source` is a URL.** It is the snapshot `generate` took, it is
what `--check` compares against in Phase 8, and it is the only thing that gives a URL-sourced
spec a reviewable diff at all — leave it uncommitted and Phase 8 fails against a file that is
not in the repository. For a local `spec.source` no snapshot is taken and the path you committed
in Phase 1 plays that role instead.

---

## Phase 8 — wire CI

Two pipelines, two different jobs.

### Pull request

```bash
dotnet tool restore
dotnet build ../Orders                 # produce the spec artifact — local spec.source only
dotnet intest generate --check         # fail if committed output is stale
dotnet test
```

If `spec.source` is a URL there is no artifact to build, and the second line drops out: `--check`
reads the committed `spec.json` snapshot and **never fetches**, so this job needs no network
access to your API at all. A snapshot that has not been committed is reported as exit `1` —
outstanding work, not a tool error — naming `intest generate` as the fix.

`--check` compares `Generated/` and `coverage-report.json` against a fresh run, writing nothing
either way. Exit codes: `0` identical, `1` `Generated/` or `coverage-report.json` differs, or a
fixture has drifted from what the spec now requires — same meaning as plain `generate`'s exit 1,
different message; `2` tool error; `4` the running tool's version does not match `intestVersion`
in `intest.json`. The version check runs **before** any output is compared, so a stale tool never
reports "the spec changed" when the real story is "the generator changed" — and it only fires
when `intest.json` declares a version at all: an older config with no `intestVersion` is not
claiming a match or a mismatch, so `--check` skips the version check and compares output as
usual.

That last exit code exists so a tool upgrade is never mistaken for spec drift:

```bash
dotnet intest upgrade                  # regenerate, then bump the version pin deliberately
```

`upgrade` regenerates against the running tool, then bumps `intestVersion` in `intest.json` and
the `intest.cli` pin in `.config/dotnet-tools.json` together — so the version change and its
output change land in one reviewable commit rather than arriving disguised as spec drift. It also
scaffolds `.gitattributes` if the project does not already have one (see Phase 2) — the only way
a project created before this file existed can get one.

Cross-repo, the API build step means cloning the API repo. That is a real cost and worth
knowing before you start.

### Post-deployment gate

```bash
dotnet test --filter "TestCategory=Contract" --settings qa.runsettings
```

Contract tests only. Variation tests send hundreds of malformed payloads — useful in lower
environments, noise in a gate, and liable to trip a WAF or rate limiter.

---

## The steady state

```
spec changes  →  regenerate on a branch  →  `generate` reports drift, exits non-zero
              →  `fixtures repair`       →  fill new sentinels
              →  PR shows the whole change as a diff
```

The gate never sees red, because red happens on the branch where someone can fix it. That is
the entire point of generating at pull-request time rather than in the pipeline.

---

## Things that will bite you

**Trailing slashes.** Covered above, and worth repeating: it fails silently and looks like
passing tests.

**Do not repeat a path prefix in the base URL.** `Api:BaseUrl` substitutes for the spec's
`servers[0].url` and operation paths are appended to it. If your paths already begin `/api`,
the base URL must be the origin — `https://host/`, not `https://host/api/`. InTest now detects
this at startup and names both halves, but the failure it prevents was nine tests returning 404
against configuration that read perfectly.

**Health endpoints usually sit at the host root.** `readiness.path` follows ordinary URI rules:
`/health/ready` resolves against the origin, `health/ready` against the API base URL. The
scaffold ships the leading slash. A 404 on the probe fails immediately rather than waiting out
the timeout, because a missing route does not appear by waiting.

**Route constraints do not disambiguate OpenAPI paths.** `GET /api/stock/{sku}` and
`DELETE /api/stock/{id:int}` are distinct routes to ASP.NET, which separates them by constraint.
OpenAPI has no such concept: both collapse to the path signature `/api/stock/{}`, which the
specification requires to be unique. Every producer will happily emit this invalid document from
an API that compiles and serves traffic correctly. InTest refuses it with exit code 2 and names
the colliding signature; the fix is to give one of the routes a distinct segment.

**Non-ASCII in display names.** `X-Test-Run-Id` must be ASCII — `HttpClient` throws otherwise.
InTest transliterates and appends a hash when a name is lossy, so emoji and RTL variation cases
stay distinct. Custom display names you write yourself go through the same path.

**Parallelization.** Declared **only** in `AssemblyInfo.cs`. Setting `MSTestParallelizeScope` in
the project file generates a second attribute and breaks the build; InTest catches this with a
clear `INTEST0001` error rather than letting you meet `CS0579`. The default is sequential.
Before enabling parallelism, make sure every test creates its own data — and note that two
concurrent pipelines against one environment cannot coordinate at all.

**Fixture diagnostics go missing during a *passing* run.** `TestContext.WriteLine`,
`Console.Out`, and `Console.Error` written during `[AssemblyInitialize]` are invisible on a
passing run — not stdout, not the `.trx` (mechanics: spec §18, New findings). That is why the
fixture-validation report and `FixtureRunner`'s skip lines (Phase 5) go through
`TestContext.DisplayMessage` instead. Do the same in your own `IAssemblyFixture` logging, and
pick `Warning` or `Informational` — `MessageLevel.Error` fails the run.

**Cleanup is best-effort.** `AssemblyCleanup` does not run on a crash, a cancelled pipeline, or
an agent timeout. Everything is tagged with a run ID whose timestamp is UTC, so an out-of-band
sweeper can delete anything older than a day using the ID alone. Write one. Without it,
cancelled pipelines slowly fill your environment with orphans nobody can reproduce locally.

To confirm cleanup is running at all, run `dotnet test --logger "console;verbosity=detailed"`
once a fixture has registered at least one `OnCleanup`, and look for
`InTest fixture cleanup: drained N action(s).` Unlike `[AssemblyInitialize]` output on a passing
run (above), that line does reach both the console at that verbosity and the `.trx`'s last test
result — so seeing neither means cleanup did not run, not that it succeeded quietly.

**A stale local package cache can shadow a fresh build.** This is specifically a trap for
testing an *unpublished* change: `InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` are published
to nuget.org now, so an ordinary Phase 2 restore against a released version no
longer needs a local feed at all. Iterating on a change ahead of the last published tag still
does — a scaffolded project resolves `InTest.Runtime.MSTest` (which brings in `InTest.Runtime`
transitively, at the exact same version) from wherever you point NuGet, typically a local feed you
`dotnet pack` yourself. NuGet does not overwrite an already-cached version: an older
`InTest.Runtime.MSTest 0.1.0` — or, for the neutral package it depends on, an older
`InTest.Runtime 0.1.0` — left in `~/.nuget/packages/intest.runtime.mstest` /
`~/.nuget/packages/intest.runtime` by an earlier local build resolves ahead of a freshly packed
one carrying the identical version number, and the scaffolded project fails to compile against
members that plainly exist in the source you just built (`RequireFixture`, `FixtureBody` and
similar — confirmed by direct experiment: deleting that cache entry and rebuilding is what fixes
it, not any change to the generated code). Clear the specific entries
(`dotnet nuget locals global-packages --clear` is blunt but works; deleting just the
`intest.runtime.mstest` and `intest.runtime` folders under the packages directory is narrower)
before rebuilding, or bump the local packages' version so neither can collide with what is cached.

**The honest way to avoid this, rather than recover from it after the fact**: don't improvise
Phase 8 by hand at all. Run `scripts/local-e2e-test.ps1` (see `CONTRIBUTING.md`'s "Testing
against a local build") — it packs at a version that can never collide with a real release and
redirects the whole run's restores away from your real package cache, so this cannot happen in
the first place. Phase 8 being runnable from a bare clone against a *published* version is no
longer an open gap — see `docs/v0-acceptance.md`'s publish record — but this script is still the
current answer to "how do I try a change I haven't tagged and pushed yet."

**This is for pre-production.** InTest adds no guard rails against being pointed at production,
deliberately. Pointing it there is your decision and your consequences.

