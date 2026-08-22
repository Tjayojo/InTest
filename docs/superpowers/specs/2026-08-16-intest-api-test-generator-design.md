# InTest — API Integration Test Generator

**Status:** Design · Revision 3
**Date:** 2026-08-16
**Supersedes:** Revision 2

Revision 3 replaces rev 2 after every external claim was re-verified against vendor
documentation and, where possible, against a real build. §18 defines *measured*, and records
what changed and why.

> **Name.** The tool is `InTest`, invoked as `intest`. Availability verified against
> nuget.org on 2026-08-16: `InTest`, `InTest.Cli`, `InTest.Runtime` and `InTest.Core` all
> return 404 on the flat-container index, and a fuzzy package search for `intest` returns
> zero hits, and no C# repository of substance uses the name. On GitHub the org name `intest`
> is **not** available — a personal account holds it — so the repository lives at
> **`github.com/Dexom-GH/intest`**, MIT licensed. (Rev 2's provisional name `Jig` was taken:
> nuget.org carries `Jig` 0.2.0–0.3.1.)
>
> Casing convention: `InTest` for packages, namespaces and types; `intest` for the CLI
> command, `intest.json`, and `INTEST_PROFILE`. The split-cap form disambiguates it from the
> unrelated English stem it otherwise shares.

---

## 1. Purpose

InTest generates a complete, owned .NET test project that exercises deployed APIs over real
HTTP, suitable for running as a post-deployment gate in any CI system.

The output is a normal MSTest project. It is committed to the repo, edited by the team, and
run with `dotnet test` like any other test project. InTest is a development-time tool.

### Design principles

1. **The team owns the output.** Full test project, committed, readable, editable.
2. **Generation is a PR-time activity.** Bad output fails on the PR, not in the gate.
3. **Generation never *writes* in the pipeline.** `intest generate --check` reads and compares;
   it is the only InTest invocation CI makes. (Rev 2 said "does not run in the pipeline" while
   also specifying `--check` in CI. This is the resolved form.)
4. **No changes to the gate stage.** The generated project runs there like any other test
   project — `dotnet test`, nothing else. This principle is scoped to the gate deliberately:
   the **PR pipeline does change**, gaining a tool restore, an API build, and a `--check` step
   (§8). Rev 2 stated "no pipeline changes" unqualified, which was not true of the PR pipeline
   and is not true cross-repo.
5. **Fail loudly.** Placeholder or invalid data causes a clear failure a developer must fix.
   No skip-flags, no silent green.
6. **Generated code is idiomatic and direct.** No facades that obscure failure messages.
7. **Prefer the framework's own mechanism.** Parallelization, timeouts, retries, conditional
   execution and filtering all have first-class MSTest mechanisms. InTest uses them rather than
   inventing a parallel control surface.
8. **Stable, non-preview dependencies only.**

### Project status

InTest is open source, MIT licensed, published at `github.com/Dexom-GH/intest`, and intended
for use by organisations and individuals its maintainers will never meet. That is a design
input, not a distribution detail, and it is what the following rules follow from:

- **No capability is gated on the maintainers' own spec population.** A survey informs
  prioritisation; it never decides whether a feature exists (§17). Absent `operationId` and
  undeclared `security` are normal inputs to be handled well, not conditions to be fixed by the
  adopter first (§6, §9).
- **No dependency with a licence surface**, because adopters inherit it. This is what excluded
  `JsonSchema.Net` and FluentAssertions (§4).
- **No assumed vendor.** No identity library and no cloud dependency (§13); CI detection covers
  Azure DevOps, GitHub Actions and a generic fallback (§14); environment names are examples,
  not fixtures of the design (§7).
- **A public compatibility contract**, because other people's builds depend on it — semver,
  a defined public surface, and a support window (§3).
- **Adoption requirements stated up front** (§2) so an evaluation ends quickly when InTest is
  the wrong fit.
- **CI cannot depend on private specs** (§16), and the NuGet IDs must be reserved (§20).

---

## 2. Scope

### In scope for v1

| Area | v1 |
|---|---|
| Input | OpenAPI 3.x, JSON or YAML, file or URL |
| Producers | Swashbuckle, built-in `Microsoft.AspNetCore.OpenApi`, NSwag |
| Test framework | MSTest |
| HTTP pack | **HttpClient via `IHttpClientFactory` — one pack only** |
| Assertions | **Shouldly (primary) and MSTest `Assert` — both ship in v1** |
| Schema validation | NJsonSchema |
| Content types | `application/json` |
| API versions | Every operation in the document; no version selection |
| Test kinds | Contract, declared-error, auth, variation |
| Scoping | Whole spec, by tag/controller, by operation |
| Adoption tooling | `intest survey` — spec-population report, run before committing to InTest |

Scope was reviewed for decomposition and deliberately kept whole.

### Adoption requirements

InTest is consumed by organisations whose constraints its maintainers cannot see, so what it
demands of an adopter is part of the design rather than an implementation detail.

| Requirement | Why | Consequence for an adopter |
|---|---|---|
| The **test project** targets `net10.0` | .NET 8 and 9 both reach end of support 10 November 2026; MSTest v4's floor is .NET 8 | Independent of the API's own TFM (§4) — an API on `net8.0` is fine. Only the test project needs a `net10.0`-capable SDK on developer machines and agents |
| **MSTest** | v1 ships one framework; the lifecycle, parameterization and parallelism models are not interchangeable (§5) | A team standardised on xUnit or NUnit cannot adopt v1. This is the single largest adoption barrier and the most likely v2 request |
| **OpenAPI 3.x** | The parser accepts 2.0, 3.0, 3.1 and 3.2, but generation targets 3.x semantics | Swagger 2.0 documents should be converted first |
| A **deployed, reachable** API | Tests exercise real HTTP (§1) | Not a substitute for unit tests, and not usable against an unbuilt service |

None of these are negotiable in v1, and all are stated here so an evaluation ends in five
minutes rather than after a day of scaffolding.

### Deferred to v2

- **xUnit and NUnit template sets.** The single largest constraint on reach: MSTest is roughly
  21.7% of test-framework downloads (§18). v1 does not ship them, but §3 requires the
  architecture to keep them additive rather than a rewrite — the neutral layers must not name
  an MSTest type. Highest-priority v2 item.
- **A second HTTP pack (Flurl).** See §3 — `ApiTestBase.Client` cannot be typed for two packs
  from one package, and v2 must pick a resolution before adding one.
- **Version selection.** v1 generates every operation in the document (§12).
- **WCF/SOAP.** Teams provide the `svcutil` proxy; InTest will not generate the client.
- **Non-JSON content types** — `multipart/form-data`, `x-www-form-urlencoded`, XML, binary.
- **Scenario-per-class layout** (BDD *grouping*; BDD *naming* is in v1).
- **`[ResourceLock]` and `[DependsOn]`**, both of which ship in MSTest 4.4 (§4).

Every deferral that can cause an operation to go untested or under-tested appears in the
coverage report on every run (§12). The two that cannot — the second HTTP pack and version
selection — change how tests are written, not which operations are covered.

### Explicit non-goals

- **Stateful flow tests.** Per-operation generation has no ordering model. Revisit when
  MSTest 4.4 ships `[DependsOn]`, which is exactly such a model.
- **Load or performance testing.**
- **Full combinatorial input coverage.** That belongs in unit tests.
- **Owning parallelization.** See §11.

---

## 3. Architecture

### Two-stage pipeline

```
spec → [front-end] → TestPlan (JSON) → [template set] → .cs files + fixtures
```

**TestPlan** is the stable internal contract. Front-ends target it; template sets consume it.
Designed to be dumpable via `--emit-plan` for debugging — **not yet built**, so this is design
intent, not a fact about the running tool.

**Design constraint for v2:** `expectedOutcome` in TestPlan must be abstract, not literally an
HTTP status int, or SOAP faults won't fit.

### Template sets — internal, not a public extension point

v1 ships template sets InTest owns. There is no manifest format and no `templates extract`
command; the planned `--emit-plan` (**not yet built**) is meant to cover the debugging need
instead. Until it ships, that is the intended answer, not evidence the gap is already covered.

**Assertion emission is a separate include** (`{{ include "assertions/status" }}`). Rev 2
carried this seam speculatively for a v2 second assertion library. In rev 3 the seam is
**exercised at v1**, because Shouldly and MSTest `Assert` both ship. Unproven abstraction
becomes proven structure.

**One HTTP pack in v1.** Rev 2 shipped two (Flurl primary, HttpClient secondary) with a parity
policy. Rev 3 ships HttpClient only, and there is no parity policy because there is nothing to
keep in parity.

The forcing argument is structural, not preference. `ApiTestBase` exposes `Client`, which is
`HttpClient` under one pack and `IFlurlClient` under the other — a single concrete base class
in a single package cannot serve both. The alternatives were a generic `ApiTestBase<TClient>`
(leaks a generator concern into every hand-written partial), a third `InTest.Runtime.Flurl`
package (contradicts §3's two-package rule), or a base class exposing no client at all
(pushes a resolve line into every test). Shipping one pack removes the constraint instead of
working around it, and removes a template set, a golden-file dimension, and a
compile-verification dimension with it.

Flurl moves to the v2 backlog. Independently: its last commit is 2025-01-01 and its last
release 2024-01-17, so it was the wrong candidate for first-class support regardless.

### Deliverables — two packages

| Artifact | Contents |
|---|---|
| `InTest.Cli` | `dotnet tool`. Front-ends, TestPlan, rendering, CLI. Internal namespaces. |
| `InTest.Runtime` | **NuGet package referenced by the generated project.** Base class, interfaces, readiness, run ID, fixture loading, schema bundle, assertion helpers, HTTP handlers. |

`InTest.Runtime` earns the separation: shared behaviour ships as a versioned dependency, so a
bug fix does not require every team to regenerate.

### Framework portability — designed for three, ships one

v1 ships MSTest. But MSTest is **21.7%** of test-framework downloads against xUnit's 47.4% and
NUnit's 30.9% (§18 records the figures and their limits) — so a design that bakes MSTest into
its lower layers caps the tool's reach permanently. **The architecture must make xUnit and NUnit
additive rather than a rewrite**, on the same reasoning that made the assertion seam pay off:
that seam existed before a second assertion set did, and adding one cost nothing as a result.

This is a design constraint, not a v1 feature. Nothing framework-specific is abstracted
speculatively; what is required is that the neutral layers stay neutral.

**The boundary.** Everything below is framework-neutral and must not name an MSTest type:

| Neutral | Why it can be |
|---|---|
| `TestPlan` | Describes operations, cases, data rows, categories and expected outcomes. It must not carry MSTest attribute names — `[DataRow]`, `[TestCategory]` and `[MemberCondition]` are rendering decisions |
| Configuration, profiles, DI composition | Plain `IConfiguration` / `IServiceProvider` |
| Schema bundle, `ApiResponseAssertions` | Take values in, return results out |
| Readiness, run ID, fixture loading and token resolution | No test-framework surface |
| `IAssemblyFixture`, `ITestTokenProvider`, `ITestDataProvider` | Already framework-neutral interfaces |
| HTTP handlers and the ambient accessor | `AsyncLocal`, not framework state |

Everything below is **framework-specific** and belongs in a thin adapter — one namespace in v1,
one package per framework when a second ships:

| MSTest-specific | What the others need instead |
|---|---|
| `ApiTestBase` and its `TestContext` | xUnit has no `TestContext`; identity and output arrive differently |
| `TestId` from `TestContext.TestDisplayName` (§14) | Each framework exposes the resolved data-row name differently. **This is the sharpest coupling in the design** and needs a neutral `ITestIdentity` the adapter supplies |
| `[AssemblyInitialize]` / `[AssemblyCleanup]` | xUnit: assembly fixtures; NUnit: `SetUpFixture` with `[OneTimeSetUp]` |
| `[DataRow]` / `[DynamicData]` | xUnit: `[InlineData]` / `[MemberData]`; NUnit: `[TestCase]` / `[TestCaseSource]` |
| `[TestCategory]` | xUnit: `[Trait]`; NUnit: `[Category]` |
| `[MemberCondition]` gating (§9) | xUnit has no conditional-execution attribute; needs a different mechanism |
| Parallelization and timeout models (§11) | Differ substantially, and are consumer-owned in every case |

**The practical rule for v1:** if a type in the neutral layer would have to change to support
xUnit, it is in the wrong layer. `project.framework` stays frozen per project (§5) — a suite
cannot be migrated in place — but the *tool* must be able to emit all three.

### Versioning and compatibility

Because `InTest.Runtime` is a package other organisations depend on, and because generated code
is the coupling between the two artefacts, the compatibility contract is public API, not an
internal note.

**Both packages follow semantic versioning**, and their major versions move together. The
contract:

| Guarantee | Meaning |
|---|---|
| `InTest.Runtime` **N.x** accepts code generated by `InTest.Cli` **N.y** for any `y` | A team may upgrade the CLI and regenerate without touching the package reference, and vice versa, within a major |
| Generated code never depends on a runtime newer than the CLI that emitted it | The CLI writes a floor, not a pin, so consumers get patches without regenerating |
| A **major** bump may change generated code shape, the `intest.json` schema, or `InTest.Runtime`'s public surface | Requires `intest upgrade` (§5) and a reviewed diff |
| The previous major is supported for **12 months** after the next ships | Long enough to plan a migration, short enough that two shapes are not maintained forever |
| `schemaVersion` in `intest.json` moves only on a major | It is how the CLI detects a config it must not silently reinterpret |

The **public surface** covered by semver is: `InTest.Runtime`'s exported types, the
`intest.json` schema, the CLI's commands, flags and exit codes (§5), and the coverage report's
JSON shape — the last because CI pipelines assert on it (§12).

Explicitly **not** covered, and free to change in a minor: the exact text of failure messages,
the internal `TestPlan` JSON (`--emit-plan`, once built, is meant as a debugging aid, not an
integration point — see §3), and template internals.

This closes what earlier drafts filed as an open question about how current teams must stay.
The real question was never currency; it was which combinations are supported, in both
directions.

---

## 4. Stack and version policy

**Target `net10.0`.** .NET 8 and .NET 9 both reach end of support on **10 November 2026**.
.NET 10 is LTS through **14 November 2028** (rev 2 said 10 November; corrected).

The test project's TFM is independent of the API's. Even where the API targets `net8.0`, the
test project targets `net10.0`.

### Pinned dependencies

All stable. *Measured:* the full set restores and builds clean together on SDK 10.0.303 with
zero warnings and zero NuGet-audit findings.

| Package | Version | Notes |
|---|---|---|
| `MSTest.TestFramework` / `.TestAdapter` / `.Analyzers` | 4.3.3 | Latest stable |
| `Microsoft.NET.Test.Sdk` | 18.9.0 | **Added in rev 3.** The classic package triple needs it for `dotnet test` under VSTest |
| `Shouldly` | 4.3.0 | Primary assertion set |
| `NJsonSchema` | 11.6.1 | **Added in rev 3.** Response schema validation. MIT |
| `Microsoft.OpenApi` | 3.10.0 | **Was 2.3.x.** See below |
| `Microsoft.OpenApi.YamlReader` | 3.10.0 | Separate package — required for YAML |
| `Microsoft.Extensions.Http` | 10.0.11 | `IHttpClientFactory` |
| `Scriban` | 7.2.6 | Templating |
| `System.CommandLine` | 2.0.11 | GA; 3.0 is preview |

**The `Microsoft.OpenApi` bump is not optional.** Every stable 2.x version is deprecated on
nuget.org carrying a vulnerability advisory ("vulnerability through circular references
resolution"), as are 3.0.0–3.5.3. The floor for a clean version is **3.5.4**. Rev 2's
instruction to pin 2.3.x would have shipped a flagged dependency into every consumer repo,
which central package management and a lock file would then have held in place.

Microsoft.OpenApi 3.x also supports OpenAPI 3.2 while remaining backward compatible with 2.0,
3.0 and 3.1, and ASP.NET Core 11 moves to it — so 3.x is where the ecosystem is heading.

**Do not use `MSTest.Sdk`.** NuGet-provided MSBuild SDKs have limited tooling support for
version updates, and `MSTest.Sdk` defaults to Microsoft.Testing.Platform, which changes the
runsettings surface that §7 and §9 depend on. Use the classic package triple plus
`Microsoft.NET.Test.Sdk`.

**Enforcement, not policy:** central package management in `Directory.Packages.props`, plus a
lock file. The generated scaffold references no prerelease packages.

### Deliberately excluded

| Thing | Why excluded | Revisit when |
|---|---|---|
| Shouldly 5 | Preview (5.0.0-preview.2). Moves to `[CallerArgumentExpression]`, removing the source-reading dependency, but has breaking changes | 5.x GA |
| MSTest `[ResourceLock]` | **Confirmed MSTest 4.4**; docs state it is preview-only until 4.4.0 releases | 4.4.0 stable |
| MSTest `[DependsOn]` | **Also 4.4.** A real ordering model — revisit the stateful-flow non-goal then | 4.4.0 stable |
| MSTEST0073–MSTEST0077 | **4.4.** Includes MSTEST0076, which rev 2 depended on | 4.4.0 stable |
| `JsonSchema.Net` | Stable and technically excellent, but licensed MIT **under an Open Source Maintenance Fee agreement**: commercial users at ≥ US$10,000 annual gross revenue owe a fee. Same licence-surface test that ruled out FluentAssertions | If the fee is accepted |
| FluentAssertions | v8 is commercial (~$130/dev/yr); 7.x is Apache-2.0 but frozen | Not planned |
| `System.CommandLine` 3.x | Preview | GA |

### MSTest 4.3.x floor — what it unlocks

| Feature | Since |
|---|---|
| Cooperative cancellation for `[Timeout]` | 3.6 |
| `TestContext` parameter on `[AssemblyCleanup]` | 3.8 |
| `RetryAttribute`, `RetryBaseAttribute` | 3.8 |
| `ConditionBaseAttribute`, `OSCondition` | 3.8 |
| `CICondition` | 3.10 |
| `MemberCondition`, `ArchitectureCondition`, `ExecutableCondition` | 4.3 |
| `RetryAttribute` at class level | 4.3 |
| `RandomizeTestOrder` / `RandomTestOrderSeed` | 4.3 |

`RetryBaseAttribute.ExecuteAsync` and its `RetryContext`/`RetryResult` types are experimental
(diagnostic `MSTESTEXP`). `MemberCondition` is not.

**Two things rev 2 specified must be removed** because they require 4.4 or no longer exist:

- `mstest_parallel_safety_mode = always` in the generated `.editorconfig` — MSTEST0076 does
  not exist in 4.3.3.
- `ClassCleanupBehavior.EndOfClass` — **the enum is removed in MSTest v4**. End-of-class is
  now the only behaviour, so the pin does not compile.

**Enable cooperative cancellation globally** via runsettings and turn on MSTEST0045. By
default MSTest wraps each timed test in a separate task and merely stops observing it on
timeout — the test keeps running and mutating state. Because every generated test threads
`TestContext.CancellationToken` into `SendAsync` (§9), this setting actually cancels in-flight
requests rather than orphaning them.

---

## 5. Configuration and command surface

`intest.json` at the test project root, committed.

```json
{
  "schemaVersion": 1,
  "intestVersion": "1.0.0",
  "spec": {
    "source": "../Orders/bin/Debug/net10.0/orders.json",
    "producer": "auto"
  },
  "project": {
    "name": "Orders.ApiTests",
    "rootNamespace": "Orders.ApiTests",
    "framework": "mstest",
    "assertions": ["shouldly"],
    "testBaseClass": "Orders.ApiTests.OrdersTestBase"
  },
  "naming": {
    "identifiers": {
      "style": "pascal",
      "class":     "{Tag}Tests",
      "method":    "{OperationId}_Contract",
      "variation": "{OperationId}_{Property}_{Case}"
    },
    "display": {
      "method":    "Given {Tag}, when {OperationId}, then {Status}",
      "variation": "{Property} = {Value} → {Status}"
    }
  },
  "tags": {
    "strategy": "first",
    "map": { "orders-v2": "Orders" },
    "untaggedClass": "DefaultTests"
  },
  "generation": {
    "categories": { "contract": "Contract", "variation": "Variation" },
    "variations": { "strings": true, "numbers": true, "security": false }
  },
  "readiness": {
    "enabled": true,
    "path": "/health/ready",
    "expectStatus": 200,
    "expectVersion": null,
    "consecutiveSuccesses": 2,
    "timeoutSeconds": 120,
    "intervalSeconds": 3
  },
  "runId": { "prefix": null, "maxLength": 40 },
  "operations": {
    "createOrder": { "expect": 201, "mutates": true },
    "deleteTenant": { "skip": "destructive" }
  }
}
```

This is real, strict JSON — `ConfigLoader.Parse` is a bare `JsonDocument.Parse` with
`CommentHandling.Disallow` and `AllowTrailingCommas: false`, so a real `intest.json` cannot carry
the `//` comments an earlier revision of this example did; copying that block verbatim made every
InTest command exit 2, confirmed by running it. The five fields that block used to annotate
inline:

| Field | Note |
|---|---|
| `spec.producer` | `auto` \| `swashbuckle` \| `aspnetcore` \| `nswag` |
| `project.framework` | **Frozen** — see "Frozen vs. additive axes" below |
| `project.assertions` | `shouldly` \| `mstest` — additive, never a swap |
| `naming.identifiers` | **Frozen** — see "Frozen vs. additive axes" below |
| `naming.display` | Changeable any time — cosmetic, no compile impact |

`generation.parallel` does not exist — see §11.

### Frozen vs. additive axes

The rule: **an axis is frozen if changing it invalidates hand-written code.**

| Axis | Status | Why |
|---|---|---|
| Test framework | **Frozen per project** | Lifecycle, parameterization and parallelism models differ, and every hand-written partial targets them — a suite cannot be migrated in place. This does **not** mean the tool emits one framework: §3 requires the architecture to support MSTest, xUnit and NUnit, with v1 shipping MSTest |
| Identifier naming | **Frozen** | Renaming generated classes orphans every hand-written partial |
| HTTP pack | n/a in v1 | One pack ships, so there is no axis. When v2 adds a second, it is **frozen** — `ApiTestBase.Client` is typed per pack, so any hand-written test touching `Client` stops compiling on a swap |
| Assertion set | **Additive** | Hand-written assertions are never migrated — adding a set adds a library, so `assertions` is an array. Command is `intest assertions add`, never "switch" |
| Display naming | Free | Cosmetic; no compile impact |

Attempting to change a frozen axis **fails with a real error**:

```
Cannot change naming.identifiers.class ("{Tag}Tests" → "{Tag}ApiTests") after initialization.
Frozen at init on 2026-03-14 by intest v1.0.0.
7 files contain hand-written partials targeting the current class names.
```

The example uses identifier naming deliberately, because it is the only frozen axis a v1 user
can actually attempt to change — v1 ships one test framework and one HTTP pack, so those
messages are unreachable. The framework message exists for completeness and needs no
migration document until a second framework ships.

The honest migration for any frozen axis is: generate fresh alongside, port hand-written tests
manually, delete the old project. Ship it as a procedure, not a `--force` flag that produces
something uncompilable.

### Naming constraints

- **Sanitize** to valid C# identifiers — no leading digits, no reserved words.
- **Dedupe deterministically** — collisions suffixed by a stable key, never by ordinal.
- **Emit a matching `.editorconfig`** so StyleCop and .NET naming analyzers do not flood a
  snake_case project.
- **Status stays out of identifiers.** Contract methods are `{OperationId}_Contract`; status
  appears in the display name only. Anything volatile goes in display, never identity.

One qualification rev 2 lacked: the stated reason for freezing identifiers is preserving
Azure DevOps test history. **MSTest v4 changed `TestCase.Id` generation**, and Microsoft
notes this "affects Azure DevOps features, for example, tracking test failures over time." So
that history resets on the v3→v4 move regardless of naming. Freezing identifiers is still
right — it protects hand-written partials — but the AzDO-history argument is weaker than rev
2 presented it.

### Command surface

Rev 2 and earlier drafts of rev 3 scattered commands across seven sections, and introduced
`intest upgrade` (§8) without ever defining it. The full v1 surface:

**Ships today** marks what actually runs, added by the v1-e acceptance run (Task 6) after that
run found this table gave no way to tell — the "full v1 surface" framing above is a design
statement, correct on its own terms, but §5 doubles as the exit-code contract, and a reader of a
contract needs to know what is reachable. This follows a pattern the table two sections up
already uses (the frozen-axes table's "n/a in v1" row), rather than introducing a new one. The
authoritative, actively-maintained version of this same distinction is
[`CONTRIBUTING.md`](../../../CONTRIBUTING.md)'s opening paragraph and
[`CLAUDE.md`](../../../CLAUDE.md)'s "What this is" section; this column is a pointer at that fact
inline, not a second copy that must be kept in step by discipline — if it drifts, trust those two
files over this column.

| Command | Writes | Never writes | Exit | Ships today |
|---|---|---|---|---|
| `intest init` | `intest.json`, `.csproj`, `.editorconfig`, `AssemblyInfo.cs`, `TestStartup.cs`, `<Name>TestBase.cs`, `appsettings*.json`, `*.runsettings`, `.config/dotnet-tools.json`, `.gitattributes` | Anything already present — refuses rather than overwrites | 0 ok · 2 an argument was refused, or the scaffold failed · 3 already initialised | Yes |
| `intest generate` | `Generated/`, `coverage-report.json`, and `spec.json` when `spec.source` is a URL (§9) | `fixtures/`, team-owned files | 0 ok · 1 fixture drift or validation failure · 2 an argument was refused, no `intest.json`, malformed `intest.json`, or spec unparseable | Yes |
| `intest generate --check` | Nothing | Everything | 0 identical · 1 `Generated/` or `coverage-report.json` differs, or a fixture has drifted (same code as plain `generate`'s exit 1, §5's exit-1 row already lists both as one code) · 2 tool error · 4 tool-version mismatch, checked before any output comparison and only when `intestVersion` is declared (absent means no claim made, not a mismatch) | Yes |
| `intest generate --emit-plan` | `TestPlan` JSON to stdout | Everything | 0 ok | Not yet |
| `intest fixtures repair` | `fixtures/` — **creates missing fixtures** by tier precedence, adds `TODO:` sentinels for newly-required properties, flags removed ones. Never overwrites an existing value | `Generated/`, team-owned files | 0 ok, including nothing to repair · 2 an argument was refused, no `intest.json`, malformed `intest.json`, spec unparseable, or a committed fixture that cannot be read | Yes |
| `intest fixtures promote` | Nothing — prints a paste-ready snippet and names the target file | Everything, `spec.source` especially (§10) | 0 ok | Not yet |
| `intest survey <spec-glob\|url>` | Nothing — prints a spec-population report (§17) | Everything | 0 ok · 2 no spec matched or unparseable | Not yet |
| `intest upgrade` | Regenerates first — delegates to `generate`, writing `Generated/` and `coverage-report.json` exactly as `generate` does — then, only once that succeeds, bumps `intestVersion` in `intest.json` and the version in `.config/dotnet-tools.json` together; also writes `.gitattributes` **if the project does not already have one** | `fixtures/`, team-owned files — `.gitattributes` is the one narrow exception, written only when absent, never overwritten | 0 ok · 1 fixture drift (delegated `generate` reports it exactly as plain `generate` would — same meaning, same code, not a second condition) · 2 tool error | Yes |
| `intest assertions add <name>` | Appends to `project.assertions`, then re-runs `generate` | Existing assertions in hand-written or generated code | 0 ok · 3 already present | Not yet |

**Argument refusals.** The `init` row above read `0 ok · 2 --name is not a valid C# name · 3
already initialised` until this was measured. It was not merely incomplete about `--project` and
`--spec` — it was *contradicted*: both escaped as unhandled `ArgumentException`s, which
`System.CommandLine` reports as exit **1**. So `init` returned a code the row does not list, and
the code it returned was the one reserved below for outstanding work — a mistyped `--spec` was
indistinguishable to CI from fixture drift, which is the single confusion the 1/2 split exists to
prevent. `intest init --name "My Project"` exited 2 with one sentence while `intest init --name
""` exited 1 with a stack trace: two spellings of one mistake, two contracts. Every command now
refuses a bad argument the same way — setting named, rule stated, example given, exit 2, nothing
written — through `Commands.CommandArguments`, and the rows say "an argument was refused" rather
than enumerating which ones, so that adding a refusal cannot invalidate a row again. That
enumeration is why this table has now gone stale three times (`54fc741`, the `ConfigLoader` work,
and this change); the per-command condition lists are the part worth restructuring.

**Exit-code convention.**

| Code | Meaning |
|---|---|
| `0` | The requested state was reached, **including when no work was needed** — a PR script running `fixtures repair` unconditionally must not fail on a clean tree |
| `1` | Real work is outstanding that a human must do: fixture drift, validation failures, `--check` differences |
| `2` | **Tool error** — the tool did not do the work it was asked to do, and nothing was written: the command line could not be parsed, the spec is unparseable, `spec.source` is missing, `intest.json` is malformed, an exception went unhandled |
| `3` | The command declined because proceeding would destroy or duplicate existing state |
| `4` | Tool/config version mismatch, so CI can distinguish it from a genuine diff |

`2` is returned by **any** command and is listed per-command only where it is likely. It is
separate from `1` deliberately: folding a crash or an unreadable spec into `1` would make CI
unable to tell "the fixtures drifted, fix them" from "the tool blew up" — two failures with
entirely different responses, and only one of them is the developer's to act on.

**Parse failures.** A command line `System.CommandLine` cannot parse exits `2` like any other
tool error. This was not a missing rule so much as a rule that never *reached* far enough: the
row above says `2` is returned by **any** command, and a parse failure happens above all of
them, in the one layer no command owns — no command's code runs, so nothing was there to
override the library's own exit `1`. The cost was the exact confusion the `1`/`2` split exists
to prevent. `intest init --name ""` exited 2 through `Commands.CommandArguments`, while the same
command with `--name` omitted entirely — the same mistake one keystroke apart — exited 1, the
code this table reserves for work a human must go and do. A pipeline could not tell a mistyped
invocation from fixture drift.

Note the form of the rule, which matters more here than its content. It is stated as *the
command line could not be parsed*, not as the two cases that prompted it, and that is why a
third was covered before anyone raised it: bare `intest` names no command at all, which is a
parse failure of the same kind and exits `2` on the same line of code. Exempting it would have
meant *adding* a branch asserting that some parse failures mean outstanding work — a claim the
`1` row denies. This table has gone stale three times (`54fc741`, the `ConfigLoader` work, and
`dc8370d`) from enumerating conditions where it could have stated them; the enumeration above is
now illustration, and the sentence before the colon is the rule.

**Unhandled exceptions.** The `2` row above has always said "an exception went unhandled", and
the same reasoning that put parse failures here applies to it unchanged: `System.CommandLine`'s
default exception handler answers an exception escaping a command's action with `1`. That the
tool returned `2` was true only because `init`, `generate` and `fixtures repair` each carried a
`try/catch` of their own — three copies of one rule, agreeing by discipline, with nothing
structural behind them. A fourth command would have shipped returning `1` for a crash, and no
test could have caught it: the parse layer is not involved and `ParseResult.Errors` is empty.

The rule is now caught in the same layer as the parse rule — above all of them, where no command
owns it — so it holds for commands not yet written, and a new command inherits exit `2` for a
crash by writing no code at all. That is the whole of the guarantee: **a crash in any command is
`2` because it is caught above every command, not because each command remembers to catch it.**

What stayed behind in the commands is the distinction between a crash and a refusal, and it is
load-bearing. A `ConfigLoadException`, `SpecLoadException` or `FixtureFormatException` carries a
sentence written for the adopter and is printed as-is; only an unanticipated escape is prefixed
with "unexpected failure". Both are `2`, so this table does not change — but catching the typed
ones in the outer layer too would relabel every curated refusal as a crash, one sentence for two
very different situations. Only a real process can observe either half: a test that calls a
command method directly never runs the outer layer, and an escaping exception reaches it with no
exit code to assert on.

`--help` and `--version` still exit `0`. Both are terminating actions that suppress the parse
errors prompting them, so those errors never arise and the rule never fires — measured against
the pinned `System.CommandLine`, not inferred from the API's names.

### Invariants

Three properties hold across the whole surface, and they are what make `--check` coherent.
Note that the invariant is **ownership, not location** — an earlier draft claimed only three
commands write outside `Generated/`, which was simply false: `generate` also writes
`coverage-report.json` at the project root, and `assertions add` edits `intest.json`.

- **`generate` never writes `fixtures/` and never writes a team-owned file.** That is the real
  guarantee. It writes `Generated/`, `coverage-report.json`, and — when `spec.source` is a URL
  — the `spec.json` snapshot, all of which it owns outright and regenerates wholesale.
- **Nothing writes to `spec.source`.** When it is a local path it is a build artifact (§10);
  when it is a URL it is someone else's server. Either way InTest only ever reads it.
- **`upgrade` is the one deliberate way to adopt a new tool version**, because `--check` fails
  on a version mismatch by design (§8). It bumps the manifest and the config together and
  regenerates, so the version change and its output change land in one reviewable commit
  rather than arriving disguised as spec drift.

Because `coverage-report.json` is generator-owned, committed and regenerated wholesale,
**`--check` compares it alongside `Generated/`.** It is the one generated artefact whose
content tracks the *shape* of the spec rather than the templates, so a spec change that adds
an untagged operation, a new synthesized operationId, or a newly unevaluatable keyword shows
up there and nowhere else. Excluding it would let exactly the drift the report exists to
surface pass `--check` silently.

### `--check`'s version gate

`intestVersion` in `intest.json` is optional — a config predating this field, or hand-edited
without it, still loads (§5's config grows by addition; see `ConfigLoaderTests.IgnoresSettingsItDoesNotRead`).
Three rules govern how `--check` uses it, and they only make sense read together:

- **Exit 4 fires on any difference, not only a major.** §8's own worked example compares
  `1.0.0` against `1.1.0` — a minor difference — and presents it as the failing case, with the
  message quoted there. Reaching for the `InTest.Runtime` **N.x** accepts `InTest.Cli` **N.y**
  compatibility guarantee above would be a different axis: that guarantee is about whether a
  *package* accepts *generated code*, not about whether *committed output* is fresh against the
  tool that would produce it now. `--check` answers the second question, so it compares by exact
  string equality against the running tool's own version, whatever axis moved.
- **The version check runs before any output is compared.** A version mismatch **and** a real
  output difference is reported as exit 4, not 1 — otherwise a stale tool would report "the spec
  changed" when the true story is "the generator changed", the exact confusion `intestVersion`
  exists to prevent (§8).
- **Absent means no claim made, not a mismatch.** When `intest.json` declares no `intestVersion`
  at all, `--check` skips the version check entirely and compares output as usual — it does not
  fail with a message naming a blank declared version. Exit 4 exists to catch output generated
  by a **different** tool version; a config that claims no version is not claiming a different
  one, so treating absence as a mismatch would invent a failure for a config the loader is
  required to accept.

---

## 6. Spec producers and operationId

The producer is **irrelevant to parsing** — all three emit standard OpenAPI. `spec.producer`
is used only for promotion snippets (§10) and scaffold instructions. It never enters the parse
path. Default `auto`, sniffed from operationId shape and document metadata.

### operationId behaviour differs materially

operationId is the keystone: naming, the `operations` config map, fixture filenames, dedupe
and identity all key on it.

| Producer | Default | Opt-in mechanism |
|---|---|---|
| **Swashbuckle** | **Absent** | `[HttpGet("{id}", Name = "...")]` per route, or a `CustomOperationIds` strategy |
| **Built-in** | **Absent** | `[EndpointName]` attribute, or `WithName` (minimal APIs) |
| **NSwag** | **Always present**, auto-derived `{Controller}_{Action}` | `SwaggerOperationAttribute(operationId)` |

Swashbuckle omits it deliberately — it was dropped in 4.0 because auto-generating an ID that
satisfies OpenAPI's uniqueness requirement while remaining meaningful in client libraries is
non-trivial. **operationId is commonly null in a default controller API.**

NSwag is the more dangerous case: it emits `Beer_GetById`, `Users_Post`, and **ignores the
`Name` property on the HTTP verb attribute**, so `[HttpPost(Name = "CreateUser")]` still
yields `Users_Post`. The ID looks stable but churns silently when someone renames the action.

*Measured:* a Swashbuckle-shaped OpenAPI 3.0 document parsed with Microsoft.OpenApi 3.10.0
returned `operationId = <null>` with tags intact, confirming the null case is what the parser
actually sees.

### InTest's handling — producer-agnostic

1. **Present** → use it.
2. **Absent** → synthesize a stable key from method + normalized path (`post_orders`,
   `get_orders_id`). Never from ordinal or declaration order.
3. **Orphan detection on every generate**, regardless of source. An `intest.json` `operations`
   entry or a `fixtures/` file whose operation no longer exists gets a loud warning. This is
   what catches both rename-churn and route changes.

**Synthesis is a first-class path, not a fallback.** InTest is adopted by teams whose specs it
has never seen, most of them produced by Swashbuckle or the built-in package, where
`operationId` is absent by default. A design that works well only when operationIds are present
would be unusable for the majority of its users on day one.

So the synthesized key must be as good as a hand-written one: stable across regenerations,
derived from method and normalized path, readable in a test name, and reported in the coverage
report so its use is never invisible. Adding `operationId` to a spec is a genuine improvement —
better Swagger UI, better generated clients — and InTest says so in its output. **It is a
recommendation InTest makes, never a precondition it imposes.**

Measuring operationId coverage across a spec population tells an adopter how much of their
suite will run on synthesized keys, and tells the maintainers which path deserves the most
polish. It does not decide whether the feature exists.

### Build-time spec generation

| Producer | Mechanism | Caveat |
|---|---|---|
| Swashbuckle | `Microsoft.Extensions.ApiDescription.Server` (10.0.11 for .NET 10) | — |
| Built-in | Native build-time generation | **YAML at build time isn't supported yet** |
| NSwag | `NSwag.MSBuild` (14.7.1) | Set `NoBuild=true` to avoid build recursion |

`spec.source` pointing at a build artifact is **correct** — the artifact is always current.
Only `promote` must never write there (§10).

---

## 7. Runtime configuration and secrets

The generated project runs with **no changes to the gate stage** — `dotnet test` and nothing
else. (The PR pipeline does change; see principle 4 and §8.)

```
appsettings.json           # committed — defaults, readiness, profile list
appsettings.staging.json   # committed — per-profile base URL, non-secret settings
appsettings.qa.json
appsettings.local.json     # gitignored
+ user-secrets (local)
+ team-registered providers (Key Vault, encrypted file, …)
```

`TestHost` builds an `IConfiguration` in `AssemblyInitialize`. `TestStartup.cs` (team-owned,
scaffolded at init) is where additional providers and the named HTTP client are registered.
**Secrets never live in the test project or in fixtures.**

### Profile selection

Exactly one value identifies the target environment. Precedence, first hit wins:

1. `.runsettings` `TestRunParameters` → `profile` (read via `TestContext.Properties`)
2. Environment variable `INTEST_PROFILE`
3. Default in `appsettings.json`

Set `<RunSettingsFilePath>` in the generated `.csproj` so a `.runsettings` is picked up with
**zero command-line arguments**. Multi-stage pipelines pass `--settings qa.runsettings` or
`dotnet test -- TestRunParameters.Parameter(name="profile", value="qa")`.

**The scaffolded `<Name>.runsettings` (e.g. `Orders.ApiTests.runsettings` — named after the
project, not the API) must ship with `profile` commented out.** Because
`<RunSettingsFilePath>` loads it unconditionally, a scaffold that declares `profile` makes
tier 1 always match, `INTEST_PROFILE` becomes unreachable dead code, and a developer exporting
the variable silently runs against the wrong environment. The scaffold therefore contains:

```xml
<TestRunParameters>
  <!-- Uncommenting this PINS the profile and makes INTEST_PROFILE unreachable.
       Leave commented unless this runsettings file is environment-specific. -->
  <!-- <Parameter name="profile" value="staging" /> -->
</TestRunParameters>
```

Environment-specific files (`qa.runsettings`) *do* declare it — that is their purpose. The
default one does not.

### `Api:BaseUrl` substitutes for `servers[0].url`

Because `servers[]` is ignored (below), the configured base URL takes its place: **the spec's
operation paths are appended to it.** So if those paths already begin with a prefix such as
`/api`, the base URL must **not** repeat it.

Getting this wrong is silent. The v0 acceptance run configured `http://host/api/` against paths
beginning `/api/products` and every request resolved to `/api/api/products` — nine tests, nine
404s, configuration that looked entirely correct. Note this is the *opposite* failure to the
trailing-slash problem below, and the guard for that one does not detect it.

**So it is detected, not documented.** `generate` writes the longest path prefix shared by every
operation to `Generated/spec-paths.json`, and `AssemblyInitialize` fails before the first
request if the base URL repeats it:

```
Base URL 'http://localhost:5081/api/' and the spec's operation paths both start with '/api',
so every request would resolve to '/api/api/...' and return 404.
The base URL substitutes for the spec's servers[0].url, and operation paths are appended to it
— so it must not repeat a prefix the paths already carry.
Set Api:BaseUrl to 'http://localhost:5081/' instead.
```

Comparison is segment-wise, so a base of `/api` against paths under `/apiary` is not flagged.

### `servers[]` is ignored

The base URL comes from configuration only. InTest never reads the spec's `servers[]` block —
it typically points at localhost or a template the deployed environment does not match, and
silently preferring it over `appsettings.{profile}.json` would be the same class of bug as the
trailing-slash problem below. Stated explicitly because teams otherwise assume the spec
supplies it.

Two notes rev 2 lacked. **`TestContext.Properties` is `IDictionary<string, object>` in MSTest
v4** — any `.Contains` call becomes `.ContainsKey`. And `dotnet test` on the .NET 10 SDK still
defaults to **VSTest mode**, so `--filter`, `--settings`, `RunSettingsFilePath` and
`TestRunParameters` all behave as assumed here. *Measured.*

### Base URL normalization — a correctness rule, not a convention

*Measured:* `new Uri(base, relative)` silently drops the last base path segment in three of
four combinations.

```
base=https://h/api    rel=orders/1   -> https://h/orders/1       ← "api" gone
base=https://h/api    rel=/orders/1  -> https://h/orders/1       ← "api" gone
base=https://h/api/   rel=orders/1   -> https://h/api/orders/1   ← only correct form
base=https://h/api/   rel=/orders/1  -> https://h/orders/1       ← "api" gone
```

OpenAPI paths always begin with `/`. Therefore:

- Generated request paths **strip** the leading slash.
- `InTestUrl.NormalizeBase` **appends** a trailing slash to the configured base URL if absent.

Without both, a hand-typed base URL missing its slash produces a green suite hitting the wrong
routes — precisely the false-green failure mode §10 exists to prevent.

---

## 8. Generated project

### Layout

```
Orders.ApiTests/
├── .config/dotnet-tools.json     # pins the InTest CLI version — committed
├── intest.json                      # config — committed
├── coverage-report.json          # generator-owned — committed, regenerated, --check'd
├── appsettings*.json
├── Orders.ApiTests.runsettings   # named after the project, not the API
├── .editorconfig                 # naming style
├── .gitattributes                # pins Generated/, coverage-report.json, fixtures/**/*.json to LF
├── Generated/                    # regenerated wholesale — NEVER hand-edited
│   ├── OrdersTests.g.cs          # one class per tag; TestHost itself ships in InTest.Runtime,
│   │                             # not here — TestStartup.cs (below) delegates to it
│   ├── spec-paths.json           # shared operation path prefix, for the base-URL guard (§7)
│   └── spec-schemas.json         # bundled response/component schemas, keyed under "definitions"
├── fixtures/
│   ├── create-order.json
│   └── qa/create-order.json      # environment overlay
├── AssemblyInfo.cs               # team-owned — parallelization intent, authoritative
├── TestStartup.cs                # team-owned — DI + named HttpClient registration
├── OrdersTestBase.cs             # team-owned — shared helpers
├── Fixtures/DatabaseSeed.cs      # team-owned — IAssemblyFixture
└── OrdersTests.cs                # team-owned — same partial class
```

`spec.json` is **not** in this tree. It is copied to the output directory at build time (§9).

### Regeneration model

- Generated classes are `partial`. Hand-written code lives in a same-named partial in a
  non-`.g.cs` file.
- Regeneration touches **only `Generated/`**. Never fixtures, never team-owned files.
- Generated output is **committed**, so spec changes show as a reviewable diff on the PR.
- CI runs `intest generate --check` and fails if committed output differs from a fresh run.
  That comparison covers **`Generated/` and `coverage-report.json`** — see §5. The report is
  where a spec-shape change surfaces (a newly untagged operation, a new synthesized
  operationId, a newly unevaluatable keyword), so omitting it from the comparison would let
  precisely the drift it exists to surface slip through.

`--check` is only coherent because `generate` never writes fixtures (§10).

### What `--check` costs the PR pipeline

Rev 2 waved this away as "one prerequisite, not two." That holds only when the API and the
tests are in the same solution. Stated properly, `--check` requires three things:

1. **The API built.** `spec.source` is a build artifact. Same-repo, the test project's own
   build already needs it (§9), so it is free. **Cross-repo, the PR pipeline must clone and
   build the API** — a real added step, and the reason principle 4 is scoped to the gate stage
   rather than claimed for all pipelines.
2. **A pinned tool version.** The generated scaffold includes `.config/dotnet-tools.json`
   pinning the InTest CLI, and CI runs `dotnet tool restore`. Without it, any version drift
   between a developer's machine and the agent produces a diff that reads as "the spec
   changed" when the generator changed.
3. **A version match.** `--check` compares `intestVersion` in `intest.json` against the running
   tool and fails with a distinct message before comparing any output:

   ```
   intest.json was generated by intest 1.0.0; running tool is 1.1.0.
   Regenerate with the pinned version, or run `intest upgrade` to adopt 1.1.0 deliberately.
   ```

   Otherwise a legitimate tool upgrade is indistinguishable from spec drift, and the natural
   reaction to a confusing `--check` failure is to regenerate — which silently adopts the new
   version across the repo.

### Tag → class mapping

- **Multiple tags** → first tag wins by default (`tags.strategy: "first"`), overridable via
  `tags.map`.
- **No tags** → `tags.untaggedClass` (default `DefaultTests`), **and a line in the coverage
  report.** Silently bucketing untagged operations is how they get forgotten.

---

## 9. HTTP invocation, schema validation, and test kinds

### HTTP client construction

HttpClient via `IHttpClientFactory`. Registration happens once, in scaffolded
`TestStartup.cs`:

```csharp
services.AddHttpClient(InTestClients.Api, c => c.BaseAddress = InTestUrl.NormalizeBase(baseUrl))
        .AddHttpMessageHandler<RunIdHandler>()     // X-Test-Run-Id
        .AddHttpMessageHandler<AuthHandler>();     // wraps ITestTokenProvider

services.AddHttpClient(InTestClients.Readiness, c => c.BaseAddress = InTestUrl.NormalizeBase(baseUrl))
        .AddHttpMessageHandler<RunIdHandler>();    // no AuthHandler — see Readiness, §13
```

`RunIdHandler` stamps `X-Test-Run-Id` by reading an `AsyncLocal<string?>` accessor set in
`[TestInitialize]`. It **must** be `AsyncLocal` rather than an injected scoped service:
factory-created handlers are not scoped to the DI scope. *Measured:* the ambient value flows
correctly into the handler across successive scopes. `AuthHandler` reads its own ambient
identity the same way, for the same reason (below).

**The ambient is null before any test runs.** `IAssemblyFixture` seeding issues HTTP through
`InTestClients.Api` during `AssemblyInitialize`, when no test is in scope. Readiness probing no
longer shares that client — it moved to `InTestClients.Readiness`, which carries no
`AuthHandler` at all (F10, §13) — but fixture traffic still does, so the fallback below still
matters for it. The handler therefore falls back:

```csharp
request.Headers.TryAddWithoutValidation("X-Test-Run-Id",
    InTestAmbient.TestId.Value ?? TestHost.RunId);
```

This matters most for fixtures: entities they seed carry the run ID, and the sweeper (§14) is
what eventually deletes them. A null header on fixture traffic would leave exactly the
orphaned data §14 plans against.

Consequence for rev 2: `TestHost.CreateClient(TestId)` — constructing a client per test — is
removed. It is the anti-pattern under `IHttpClientFactory`. Tests resolve the named client;
per-test data travels via the ambient accessor.

`AllowAnyHttpStatus()` also disappears. `HttpClient` never throws on a non-2xx status unless
`EnsureSuccessStatusCode()` is called. That is one fewer concept in every generated method,
and it existed in rev 2's generated code only because Flurl throws by default.

### Schema validation

`NJsonSchema` 11.6.1, chosen for three reasons: it is plain MIT with no licence surface, it
handles both OpenAPI dialects from one code path, and it validates through instances rather
than a process-global registry — which matters because a global registry would introduce
shared mutable state into exactly the area §11 disclaims owning.

*Measured*, against the same schema expressed as OpenAPI 3.0 (`nullable: true`) and 3.1
(`"type": ["null","string"]`):

```
{"id":"a","quantity":2,"notes":null}   valid=True
{"id":"a","quantity":2,"notes":"hi"}   valid=True
{"id":"a","quantity":0}                valid=False  NumberTooSmall@#/quantity
{"id":"a"}                             valid=False  PropertyRequired@#/quantity
{"id":"a","quantity":2,"notes":5}      valid=False  StringExpected@#/notes
```

`Kind` + `Path` are the raw material for the custom failure messages §15 justifies
`ApiResponseAssertions` on.

#### Where the schemas come from

The generated `.csproj` copies the spec to the output directory at build time:

```xml
<PropertyGroup>
  <InTestSpecSource>../Orders/bin/Debug/net10.0/orders.json</InTestSpecSource>
</PropertyGroup>
<ItemGroup>
  <Content Include="$(InTestSpecSource)" Link="spec.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

*Measured:* `spec.json` lands beside the test DLL, **`dotnet publish` carries it into the
artifact** — the property that makes a separate gate stage work — and a missing spec is a
build error (`MSB3030`), not a silent skip. InTest wraps that with a clearer message via a guard
target, but the fail-loud behaviour is already correct.

The spec is neither committed nor embedded. Diffs stay small and there is no second copy to
drift.

##### When `spec.source` is a URL

§2 accepts a URL as input, and for many developers it is the only input they have — a Swagger
endpoint on a running service, with no build artifact anywhere. MSBuild cannot copy from
`https://`, so the mechanism above does not apply and an earlier draft simply left this
undefined.

**A URL source is snapshotted at generation time.** `intest generate` fetches it and writes
`spec.json` into the project as a generator-owned, committed file; the `.csproj` copies that
local file to the output directory exactly as above. Everything downstream — bundling,
publishing into a gate stage, the `MSB3030` guard — is then identical for both source kinds.

Two consequences, both wanted:

- **The snapshot is committed**, so a spec change arrives as a reviewable diff on the PR, which
  is the same property §8 relies on for generated code. A URL source otherwise has no diff at
  all, and the spec would change under the suite silently.
- **`--check` does not re-fetch.** It compares against the committed snapshot, so CI stays
  hermetic and does not depend on the service being reachable. Refreshing is what `generate`
  is for — a deliberate act by a developer, on a branch.

This keeps the ownership invariant intact: `spec.json` is generator-owned like `Generated/` and
`coverage-report.json`, and `generate` still never writes `fixtures/` or a team-owned file (§5).

#### Bundling

Each `components.schemas` entry is serialized and assembled into a single
`{"definitions": { … }}` document with `#/components/schemas/` rewritten to `#/definitions/`,
written by `generate` to `Generated/spec-schemas.json` — a JSON file, not a `.g.cs` class, and it
holds full schema **bodies** under `"definitions"`, not just keys. `TestHost` loads it once at
`[AssemblyInitialize]` into a `SchemaBundle` (`InTest.Runtime`); generated test methods reference
one entry by a plain string key (`"ProblemDetails"`, `"ProductResponse"`) passed alongside that
bundle to the assertion helper — there is no generated `Schemas.Order`-style typed accessor.

**Bundle with `definitions`; never inline the `$ref`s.** Self-referential schemas are common,
and circular-reference resolution is the exact defect that deprecated the entire
Microsoft.OpenApi 2.x line.

##### Inline response schemas

Not every response schema lives in `components.schemas`. Anonymous ones are common —
`type: array` with inline `items`, inline error envelopes — and Swashbuckle produces them
routinely. A bundle built only from `components.schemas` would leave those operations with no
key, and a contract test with no schema to assert silently degrades to a status-code check.

**Every response schema gets a bundle entry.** Schemas absent from `components.schemas` are
bundled under a synthesized, deterministic key:

```
op:{operationId}:{statusCode}:{mediaType}      → op:listOrders:200:application/json
```

The key derives from operation identity, never from ordinal, so it is stable across
regenerations for the same reason operationId synthesis is (§6). The generated test method
splices this key in as a quoted string literal (`CSharpLiteral.Escape`d, like any other
spec-derived value the template quotes) the same way it does a `components.schemas` name — there
is no separate generated schema class for either case. Inline schemas are reported as a coverage
**note**, not a skip — those operations are tested.

##### Unevaluatable keywords

*Measured:* NJsonSchema evaluates **27 of 27** OpenAPI 3.0 Schema Object keywords correctly
(§18). It **silently ignores** seven JSON Schema 2019-09/2020-12 keywords — `const`,
`if`/`then`/`else`, `prefixItems`, `unevaluatedProperties`, `dependentSchemas`,
`dependentRequired`, `contains`/`minContains`. None of those are legal in an OpenAPI 3.0
Schema Object; all can appear in OpenAPI 3.1, which is JSON Schema 2020-12.

Silent ignoring means an invalid response passes. That is the false-green failure mode this
design exists to prevent, so it is not left silent: **bundling scans every schema for keywords
the validator cannot evaluate and reports them per operation in the coverage report.** Under-
validation becomes visible under-validation.

This is the whole mitigation. A second validator is not shipped — the only .NET library that
handles all seven correctly is `JsonSchema.Net`, which carries a maintenance fee for commercial
users, and InTest being open source does not exempt the commercial teams consuming it. If the
v0 survey shows meaningful OpenAPI 3.1 usage, the decision reopens with data (§17).

*Measured:* response schemas arrive from the parser as `OpenApiSchemaReference` and serialize
to `{"$ref":"#/components/schemas/Order"}`, so the bundle-and-rewrite step is unavoidable
regardless of validator choice.

One useful property of Microsoft.OpenApi 3.x, discovered by measurement: serializing a 3.0
schema through `SerializeAsV31` normalizes `nullable: true` into `"type": ["null","string"]`,
valid JSON Schema 2020-12. NJsonSchema handles both forms directly so this is not required —
but it is available if a future validator needs strict 2020-12 input, and it only works
through the object model, never the raw document text.

### Contract tests

One `[TestMethod]` per operation. Valid input, no variation. `TestCategory("Contract")`.
Asserts expected status code, schema conformance, and well-formedness.

#### Operations with no response schema are still tested

Rev 3 originally skipped these. That was wrong, and inconsistent with the paragraph above about
inline schemas: if a contract test silently degrading to a status-code check is bad enough to
justify synthesized bundle keys, then a status-code check plainly has value — so throwing it
away by skipping the operation destroys value rather than protecting it.

Worse, it deleted correct tests. **204, 205 and 304 carry no body by definition**, so a
schema-less response is not a spec defect there; it is the specification. Skipping meant
every `DELETE` returning 204 vanished from the suite.

The rule:

| Case | Behaviour |
|---|---|
| Response schema declared (named or inline) | Full contract test — status + schema + well-formedness |
| No schema, status is 204/205/304 | **Status-only contract test.** Additionally asserts the body is empty. Not reported as a gap — this is correct |
| No schema, any other status | **Status-only contract test**, plus a coverage **note** so the missing schema is visible and fixable in the spec |

Nothing is skipped for lack of a schema.

```csharp
public partial class OrdersTests : OrdersTestBase
{
    [TestMethod, TestCategory("Contract")]
    public async Task GetOrderById_Contract()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            InTestUrl.Build("orders/{id}", TestData.Get("getOrderById", "id")));

        using var response = await Client.SendAsync(request, TestContext.CancellationToken);

        await ApiResponseAssertions.ShouldMatchContractAsync(
            response, expectedStatus: 200, schema: Schemas.Order, TestId);
    }
}
```

**Latency is recorded, not asserted.** Cold start, JIT, shared agent and noisy neighbour are
exactly the flake class readiness gating removes; asserting a latency budget in the gate
contradicts §13's reason for existing. Elapsed time appears in the failure message and the
`.trx`. If a bound is genuinely wanted, MSTest's global `TestTimeout` covers it.

**This is the post-deployment gate.**

### The precondition every generated case must clear

**Before emitting a case, ask what else guarantees a different status.**

This is the general rule the exclusions below are instances of, and it is stated as a rule
because enumerating the instances has repeatedly turned out to be enumerating the ones found so
far. Five have been *measured*, four of them by running a generated suite rather than by reading
the generator; each was a case asserting one status against a guaranteed different one, forever.

The reason they keep recurring is structural. A fixture-free case sends a **deliberately
incomplete** request — no body, an unmatchable id, no token, a different identity — and every
element it omits or substitutes is a chance for something *upstream of the behaviour under test*
to answer first. A request is answered by the first stage that can reject it:

```
content negotiation  →  model binding  →  routing  →  authentication  →  authorization  →  handler
```

**A case is valid only when the status it asserts is produced by the stage it means to test, and
nothing earlier can answer first.** The generator's omissions move the answer leftward; the
assertion sits at the right. That gap is the defect.

| Measured instance | Asserts | Actually answered by | Stage |
|---|---|---|---|
| Bodyless request to a required `[FromBody]` | 404 | 400 | model binding |
| Bodyless `POST` under `[ApiController]` — no `Content-Type` | 403 | **415** | content negotiation |
| Required query parameter omitted | 404 | 400 *or* 404, framework-dependent | model binding |
| 404 case on an operation with no path parameter | 404 | 200 — nowhere to put an unmatchable value | handler |
| Wrong-scope 403 where the second identity holds the scope — **fixed** (F11) | 403 | 200 or 404 — the API is correct | authorization |

The last is F11, and it is the one that shows why the rule needs stating in this form: it is not
about a *malformed* request at all. The request is perfectly well-formed and correctly
authorized — the assertion is simply not true of it. A rule phrased around malformed input, which
the first four would have suggested, does not catch it.

**When the precondition cannot be cleared, note it — never guess.** The case is not generated,
its operation's other cases still generate, and `coverage-report.json` carries a note saying
which operation and why. This is not a skip (§12): a skip is a test that exists and did not run,
and conflating the two is what makes a coverage number unreadable.

**Where the answer depends on something the generator cannot know**, the question moves to
runtime rather than being guessed or dropped — the same move as §9's `MemberCondition` amendment
below, where the question turned out to be answerable just not at the moment the original design
asked it.

> **F11 is fixed** (`docs/superpowers/plans/2026-08-20-intest-f11-scope-aware-403.md`). Which
> scopes an identity holds is unknowable when the code is generated and knowable when it runs, so
> the wrong-scope 403 case carries the operation's declared scopes — the distinct union of every
> OAuth scope named across every `security` requirement and every scheme within it, ordered —
> and the generated test calls `RequireSecondaryIdentityLacks(...)` with them, after
> `RequireMultipleIdentities()` and before selecting the secondary identity. The runtime guard
> skips, with a stated reason, only when the secondary identity's own declared scopes already
> cover everything the operation requires; a `null` `Scopes` means "not declared" rather than
> "holds nothing", so the test runs — deliberately, so auth testing can never be switched off
> silently by an adopter who has not populated it. `coverage-report.json` reports
> `authTestsRequiringAnUnderScopedSecondIdentity`, counting how many generated cases have a
> provability that depends on the second identity's scopes; like its sibling, it is **not** a
> skip count (§12) — the CLI still cannot know at generation time which of them a project's
> registered provider will actually skip. The row above records the defect that motivated the
> fix, not current behaviour.

### Declared-error contract tests

Generated today, from declared responses only — **never inferred**. v1-c generates a case for
`404` only, and only for an operation with at least one path parameter — a 404 needs somewhere
to put an unmatchable value, and telling a lookup query parameter from a filter is itself a
guess InTest does not make. Everything else in the 4xx range is excluded; each exclusion is the
precondition above failing for a different reason, not a separate rule:

| Status | Why not v1-c |
|---|---|
| `400` | No deterministic fixture-free trigger exists. Provoking one means sending malformed input — the variation subsystem, deferred to v1-c2 rather than built here |
| `401`, `403` | The auth cases below already own these. An operation declaring 401 would otherwise get both an auth 401 (no token, correctly expects 401) and a declared-error 401 (a valid authenticated request, always fails) |
| `409`, `422`, others | Need specific conflicting state or input the generator cannot construct fixture-free |

An operation declaring 404 falls back to a coverage **note** rather than a guessed case in three
situations — the precondition above, applied to the 404 case specifically. The list is open: a
fourth situation is a defect to record, not a contradiction of this section.

- **no path parameter to target.** `GET /orders` declaring 404 has nowhere to send an
  unmatchable value.
- **a required query parameter.** Whether a framework answers 400 or 404 for a missing required
  parameter depends on binding and route configuration — a measurement to take, not an
  assumption to ship (recorded as a candidate deterministic 400 trigger for a later plan).
  Sending only the unmatchable path id and omitting a required parameter risks asserting 404
  against what a compliant, correctly-routed API answers with 400.
- **a required request body.** The strictly stronger case of the one above: against an ASP.NET
  Core `[ApiController]` with a non-nullable `[FromBody]` parameter, a bodyless request
  (decision 6, below, sends no body) is rejected by model binding with 400 before the action's
  `NotFound()` path ever runs — confirmed against the shipped samples' own controllers
  (`PUT /api/products/{id}`, `POST /api/stock/{sku}/adjustments`), not assumed.

```csharp
[TestMethod, TestCategory("Contract")]
[Description("Given Orders, when getOrderById, then 404")]
public async Task GetOrderById_NotFound()
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        InTestUrl.Build("/orders/{id}", Guid.NewGuid().ToString()));

    var stopwatch = Stopwatch.StartNew();
    using var response = await Client.SendAsync(request, TestContext.CancellationToken);
    stopwatch.Stop();

    await ApiResponseAssertions.ShouldMatchContractAsync(
        response, 404, "ProblemDetails", Schemas, TestId, stopwatch.Elapsed,
        TestContext.CancellationToken);
}
```

`Guid.NewGuid()`, not a fixture value (decision 6): a generated, unmatchable id so no seeded row
can collide and an unfilled fixture can never block a case that needs no data. The declared-error
case asks the same `ResolveSchemaKey` the success case uses, so it schema-checks the body
whenever the 404 declares one — as every 404 in every shipped sample does, which makes
`ShouldMatchContractAsync` the form you will actually see generated. A 404 declared with no
response schema falls back to `ShouldMatchStatusAsync` instead, status-only, the same way a
success case does.

### Auth contract tests

Generated today for every operation that declares `security` at the operation level.
Deterministic, fixture-free, gate-safe, and it catches the accidental-`[AllowAnonymous]` class
of bug that reaches production. (Operation-level only — v1-c does not resolve document-level
`security` inheritance; an operation that relies on it gets a coverage note instead, so the gap
is visible rather than silent.)

`AuthHandler` is what finally consumes the registered `ITestTokenProvider` (§13): a
`DelegatingHandler`, attached to `InTestClients.Api` only, that sets `Authorization` from a
token issued for the ambient identity. It reads that identity from an `AsyncLocal`, the same
measured reason `RunIdHandler` does above — factory-created handlers are not DI-scoped.

**These split by what the shipped token provider can actually do**, because §13 ships a
static-token provider only — one token, one identity:

| Test | Needs | Behaviour |
|---|---|---|
| no token → 401 | Nothing. Send no `Authorization` header | **Always generated, always runs** |
| wrong scope → 403 | A second identity, and — the two are separate requirements, not one — that identity's declared scopes must lack at least one the operation requires. A second identity alone is necessary but not sufficient: one that holds everything the operation needs is authorized for it, and asserting 403 against it would fail a correct API | Generated; skips at runtime with a stated reason when either requirement is unmet — fewer than two identities, or a second identity that already covers the operation's scopes |
| wrong tenant → 403 | A concept of "another tenant" a spec has no way to declare | **Not in v1-c.** `IdentitySlot.Secondary` and `Identities[1]` are the same mechanism a team could seed as a second tenant, but `TestPlanBuilder` builds no case that asserts it — nothing in a spec distinguishes "wrong scope" from "wrong tenant" for it to key on. `ITestTokenProvider.cs`'s own doc comment and this section still name the possibility; recorded rather than lost, the same treatment decision 5 gives its own exclusions |

Both send a generated, unmatchable id and no body — decision 6, and the reasoning is safety, not
just tidiness: a `DELETE /orders/{id}` 403 case pointed at a real id succeeds exactly when auth
is broken, which is the one condition under which the test needs to fail.

**The gate is a runtime guard, not `MemberCondition`.** An earlier draft of this document
recommended the framework-native mechanism §9 already uses for variations:

```csharp
[TestMethod, TestCategory("Contract")]
[MemberCondition(typeof(InTestConditions), nameof(InTestConditions.MultiIdentityAvailable))]
public async Task GetOrderById_WrongScope_Returns403() { … }
```

**Measured, on MSTest 4.3.3, not to work here.** `MemberCondition` is evaluated *before*
`[AssemblyInitialize]` runs, so it cannot see anything the DI container built — including
whatever `ITestTokenProvider` a project registered:

```
09:48:17.759  condition-read Root=NULL      <- MemberCondition evaluated
09:48:17.774  assembly-initialize            <- 15ms later
09:48:17.783  plain-test-body-ran
```

The gated test was **Skipped and the run reported `Passed!`** — a green suite with auth testing
silently switched off, worse than the exception one might expect, because nothing surfaces.
`MemberCondition` remains correct where the condition is knowable without DI — a config or
environment flag, which is how it is used for variations, below — and wrong wherever the answer
lives in the service provider.

The wrong-scope case therefore calls a plain method, in the test body, after
`[AssemblyInitialize]` has genuinely finished:

```csharp
[TestMethod, TestCategory("Contract")]
[Description("Given Orders, when getOrderById, then 403")]
public async Task GetOrderById_Forbidden()
{
    RequireMultipleIdentities();
    using var _ = UseIdentity(IdentitySlot.Secondary);

    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        InTestUrl.Build("/orders/{id}", Guid.NewGuid().ToString()));

    var stopwatch = Stopwatch.StartNew();
    using var response = await Client.SendAsync(request, TestContext.CancellationToken);
    stopwatch.Stop();

    await ApiResponseAssertions.ShouldMatchStatusAsync(
        response, 403, TestId, stopwatch.Elapsed, TestContext.CancellationToken);
}
```

`getOrderById` above declares `security: [{"bearerAuth": []}]` with no scopes at all — it is
secured but scope-free — so this `_Forbidden` case carries only the multiple-identities guard.
An operation whose `security` does name scopes gets both guards, in this same order; `listOrders`
in the same spec (`orders.read`) is the one that does — see its own `ListOrders_Forbidden` in
`OrdersTests.g.cs`.

`RequireMultipleIdentities` (`ApiTestBase`) reads the registered provider's `Identities.Count`
and calls `Assert.Inconclusive` with a stated reason below two — confirmed to survive verbatim
into the `.trx`'s `<Message>`, spelled `NotExecuted` there, not the console summary's "Skipped".

`RequireSecondaryIdentityLacks` (F11) runs next, before `UseIdentity` selects the secondary
identity — always after `RequireMultipleIdentities`, but not because that guard is what makes
`Identities[1]` safe to index; `RequireSecondaryIdentityLacks` re-checks `Identities` itself and
falls through safely even when called on its own (`ApiTestBase`'s doc comment on the member says
so). The two guards' firing conditions are mutually exclusive, not a matter of precedence:
`RequireMultipleIdentities` falls through to `Assert.Inconclusive`
iff the provider advertises fewer than two identities, and `RequireSecondaryIdentityLacks`
returns immediately on that exact condition — `provider?.Identities
is not { Count: >= 2 } identities || identities[1] is not { } secondary` — before it ever
reaches a scope comparison. So the scope guard can only fire in a state the identity-count guard
has already passed; swapping the two would change neither behavior nor the surfaced message. The
ordering is a readability convention — coarse precondition before fine — not a correctness or
messaging requirement: `ApiTestBaseAuthTests.OnlyOneRegisteredIdentityRuns` and
`NoRegisteredProviderRunsRatherThanSkippingASecondTime` already say so, in as many words —
"`RequireMultipleIdentities` owns the 'fewer than two identities' skip" and "never skip twice
for one reason." Its arguments are the operation's declared scopes: the distinct union of every
OAuth scope named across every `security` requirement and every scheme
within it, in sorted order (`TestPlanBuilder.RequiredScopes`). It `Assert.Inconclusive`s, with the
secondary identity's name and held scopes in the message, only when that identity's own
`TestIdentity.Scopes` already contains every scope listed here — otherwise the 403 is still real,
and the method falls through and the test runs. `mstest-class.scriban` emits this call only for an
operation whose scope union is non-empty; an operation with no declared scopes (or `security` with
no scopes at all) never renders it, and its 403 case still runs unconditionally once
`RequireMultipleIdentities` passes. A `null` `TestIdentity.Scopes` — the identity's own scopes not
declared, distinct from an empty list declaring it holds none — always falls through too: not
declared means unknown, and unknown means run, never skip, so auth testing cannot be switched off
merely by an adopter leaving `Scopes` unset. The no-token case needs no such guard and always
runs:

```csharp
[TestMethod, TestCategory("Contract")]
[Description("Given Orders, when getOrderById, then 401")]
public async Task GetOrderById_Unauthorized()
{
    using var _ = UseIdentity(IdentitySlot.None);
    // … same request shape, expects 401
}
```

**A case selects an identity by *slot*, never by name.** `IdentitySlot` is `Default`,
`Secondary` or `None` — nothing anywhere emits a literal identity name into a plan or a
template, because the CLI generates this code long before any provider exists and can never
know one. `Secondary` resolves to `Identities[1]`; an empty or single-element `Identities`
resolves `Default` to `InTestIdentities.None`, exactly as if no provider were registered at
all — a documented state (§13), not an `ArgumentOutOfRangeException` waiting in
`[TestInitialize]`.

`coverage-report.json` records `authTestsGenerated` and `authTestsGatedOnSecondIdentity` —
named "gated **on**", not "skipped for want of": whether a generated case is actually skipped is
decided at runtime, against whatever `ITestTokenProvider` a project registers, and the CLI
writes this report long before that provider exists. What it can say honestly is how many
generated cases *require* a second identity to run at all — only the wrong-scope 403 case does;
the no-token 401 case always runs regardless. A third key, `authTestsRequiringAnUnderScopedSecondIdentity`
(F11), narrows that further to how many of those cases belong to an operation that declares
required scopes at all — the ones whose skip additionally depends on the second identity's own
declared `Scopes`, not just its presence. Like its sibling, this is **not** a skip count, for the
same reason: the CLI cannot know at generation time what any project's provider will advertise.

**The cost is now entirely the team's** — rev 2 costed this as "a multi-identity token
provider" that InTest would ship; rev 3 ships none, so the 401 half is free and the 403 half
costs the team an `ITestTokenProvider` implementation.

**A known limitation: the scope union is stricter than OpenAPI's OR semantics.** `security` is a
logical OR across requirements — an identity satisfying any one requirement in full is
authorized — but `RequiredScopes` flattens every requirement's every scheme into a single union
(`TestPlanBuilder.RequiredScopes`), which is an over-approximation for any operation that declares
more than one requirement. That enlargement is only safe against one failure mode: it cannot make
a case skip when it should have run. It does not prevent the opposite — for a multi-requirement
operation, an identity that fully satisfies one alternative requirement gets measured against the
union of *every* requirement's scopes rather than just the one it satisfies, so a case that should
skip as unable to produce a 403 can instead run and fail against a status the API is correct to
return. Every sample spec today declares exactly one security requirement, so this is a real,
documented gap, not one any shipped spec has triggered.

**This is unconditional in v1.** An earlier draft made auth tests go/no-go on whether one
organisation's specs widely declared `security`, which is the wrong dependency for a tool other
people adopt: a spec population that rarely declares `security` is a reason for those tests to
generate nothing, not a reason for the capability to be absent. The generation rule is local
and self-limiting — operations that declare `security` get auth tests, operations that don't
get none — so a spec with zero `security` declarations costs nothing and a spec full of them is
served on day one.

### Variation tests

Boundary and negative input, data-driven. `TestCategory("Variation")`.

**These must not run against a post-deploy prod gate** — hundreds of malformed payloads per
deploy is noise, can trip WAF or rate limiting, and says nothing about deployment health.

```bash
dotnet test --filter "TestCategory=Contract"                          # gate
dotnet test --filter "TestCategory=Contract|TestCategory=Variation"   # lower envs
```

A category filter does not survive someone running bare `dotnet test`. Belt and braces via
`MemberCondition` (stable in 4.3):

```csharp
[TestMethod, TestCategory("Variation")]
[MemberCondition(typeof(InTestConditions), nameof(InTestConditions.VariationsEnabled))]
public async Task CreateOrder_Quantity_Negative() { … }
```

`InTestConditions.VariationsEnabled` reads the profile flag. Framework-native, no custom skip
logic.

### DataRow vs. DynamicData

| Data shape | Attribute |
|---|---|
| Scalar constants (`-1`, `0`, `""`, `" "`) | `[DataRow]` with generated `DisplayName` |
| Objects, mutated bodies, fixture-loaded | `[DynamicData]` with `DynamicDataDisplayName` |

**Display names are mandatory**, and in rev 3 they are load-bearing rather than cosmetic —
`TestId` derives from `TestContext.TestDisplayName` (§14).

```csharp
[DataRow(-1, 400, DisplayName = "quantity = -1 → 400")]
[DataRow(0,  400, DisplayName = "quantity = 0 → 400")]
[DataRow(2,  200, DisplayName = "quantity = 2 → 200")]
```

Derive display names from **property name and literal value**, never positional index.

### Variation strategy: one-at-a-time

Hold a known-valid baseline payload; vary a **single property per case**. Linear in property
count, unambiguous failures. Full combinatorial is opt-in per operation.

### String variation catalog

String handling at an HTTP boundary crosses layers a unit test never touches: model binding,
JSON deserialization, URL encoding, charset, middleware/WAF, and the DB column at the far end.

| Case | Catches |
|---|---|
| `null` vs. omitted vs. `""` | Three distinct requests a DTO-level unit test cannot distinguish. Highest yield |
| Leading/trailing whitespace | Trimming behaviour, which differs by binding position |
| `maxLength` + 1 | Truncation, DB overflow, 500 instead of 400 |
| Unicode — emoji, combining chars, RTL | Encoding, `.Length` vs. grapheme count, collation |
| Percent-encoding — `/ ? # % +`, space | Route and query handling |

**Position changes meaning.** The catalog is per-position, not global:

| Position | Notes |
|---|---|
| Route segment | Empty string doesn't route → 404 from routing, not 400 from validation. Excluded by default |
| Query param | `?name=` vs. `?name` bind differently |
| Body property | Only place `null` vs. omitted is expressible |
| Header | Empty headers often dropped before reaching app code |

**Security payloads** are an opt-in `security` category, **off by default.** They trip WAFs,
can get the agent IP blocked, and generate alerts someone must triage.

### Expected-outcome policy

For most variations the spec does not say what should happen. A guessed `Assert 400` produces
a wall of wrong failures that get bulk-ignored.

**Default: assert what is true regardless.** No unhandled 5xx, well-formed response. Config
**promotes** specific cases once a human decides:

```jsonc
"operations": {
  "createOrder": {
    "variations": {
      "notes.emptyString": { "expect": 200 },
      "quantity.negative": { "expect": 400 }
    }
  }
}
```

The suite ratchets stricter as knowledge accumulates.

---

## 10. Fixtures

The hardest part of the system, and where every hand-edit originates.

### Source precedence

| Tier | Source |
|---|---|
| 1 | Request body media-type `example` / `examples` — used verbatim |
| 2 | Composed from per-property `example` values |
| 3 | `default` values |
| 4 | Schema-derived shape with `TODO:` sentinels |

Tier recorded in each fixture's `$meta`.

### Path and query parameters live in fixtures, not a separate mechanism

There is no `TestData` file and no second source of truth for request inputs. A path or query
parameter is composed by the same tier precedence as a request body, and lands in the same
fixture, under `$parameters`:

```jsonc
{
  "$meta": { "tier": 4, "operationId": "getOrderById", "generatedBy": "intest 1.0.0" },
  "$parameters": {
    "id": "TODO:id"
  }
}
```

Field order is fixed — `$meta`, then `$parameters` (sorted by name), then `body` — and each is
omitted entirely, never written empty, when the operation has none. That is what keeps a
committed fixture's diff reviewable: two fixtures with identical content always serialize
byte-for-byte the same, so a real change is a one-line diff, not a reordering.

Which parameters get a value, and what kind, is one rule, not a case-by-case judgment call:

| Parameter | Appears | Value |
|---|---|---|
| Path | Always | `example`/`default` from the spec if present (tier 2/3); `TODO:` sentinel otherwise (tier 4) |
| Query, `required: true` | Always | Same as path |
| Query, optional, spec gives `example` or `default` | Always | That value (tier 2/3) — never a sentinel |
| Query, optional, no `example`/`default` | Never | Omitted from `$parameters` — never sent |

**A path parameter is sentinelled whatever the document's `required` flag claims.** An unrouted
path segment does not produce a smaller version of the same test — it is a 404 from routing
before the handler under test ever runs, which is a different operation, not a lenient one.
Treating `required: false` on a path parameter as license to skip it would mean silently
generating a request that cannot route. So InTest disregards the flag there and always
sentinels the path.

**An optional query parameter with neither `example` nor `default` is the one input InTest
declines to invent a value for.** Sentinelling it would assert on a query parameter nobody
asked to test; omitting it instead means the generated request exercises the API's actual
default behaviour when that parameter is absent, which is the faithful contract test. This is
the same reasoning as tier 4 sentinels generally, applied in the direction of *not* fabricating
a value rather than fabricating an obviously-fake one.

### Fail loudly, don't flag

There is **no review flag.** A tier-4 fixture contains obvious sentinels and the test fails.

**Fail on the fixture, in a pre-flight check before the request** — not on the response.
Otherwise "bad data fails loudly" degrades into "everything returns 400 and nobody knows why."

Validation is **aggregated at assembly init**, not per test:

```
Fixture validation failed (3 fixtures, 5 problems):
  create-order.json    customerId  → "TODO:customerId"
  create-order.json    items[0].sku → "TODO:sku"
  update-order.json    {{fixture:seededTenant.id}} — key not published
                       available: seededCustomer.id, seededRegion.code
```

This works because generation happens in a branch: developer regenerates, tests fail on the
PR, fixtures get fixed, PR merges. **The gate never sees red.**

#### A bad fixture blocks its own operations, not the run

The aggregated report above names every problem across every fixture, but that report is not
also a reason to stop the whole suite from running. **A bad fixture fails only the operations
that consume it.** Everything else — every operation whose fixture resolved cleanly, and every
operation that needed no fixture at all — runs normally.

The alternative was tried first and rejected. Aborting the run at `AssemblyInitialize` the
moment any fixture has a problem is the more obvious design — it is what "aggregated
validation" sounds like it should do — but it fails a test for a reason that has nothing to do
with that test. On the Catalog sample corpus (§10 acceptance), a single unresolved sentinel in
`update-product.json` would have turned all 9 generated tests red, 6 of which pass cleanly
against fixtures that are perfectly fine. That is not "fail loudly", it is "fail everything
because something, somewhere, is wrong" — a report a developer has to read all the way through
before learning that most of it doesn't apply to them.

Blocking per-operation instead keeps the report exactly as valuable — it is still one message,
built once, naming every problem and its file — while making the *consequence* proportional to
the *cause*. A developer fixing `update-product.json` sees `updateProduct` fail and the other 8
Catalog tests pass, which is a truer signal of what is and is not broken than a wall of red.

This does not reopen principle 5's "no skip-flags, no silent green." Nothing is skipped: every
operation still runs, or fails, on its own account — `RequireFixture` throws
`FixtureUnresolvedException` naming the fixture file and the unresolved property, the same
message the aggregated report already carries, so nothing about the failure loses detail by
being scoped down. And nothing goes quietly green: an operation whose fixture is broken fails,
loudly, every time, until the fixture is fixed. What changes is only which *other* tests share
that fate — and the answer is now none of them.

#### This conflicts with "easy to adopt", knowingly

Principle 5 and the goal of being easy for any developer to pick up pull in opposite directions
here, and the spec should say so rather than pretend otherwise.

On a POST-heavy API, a first run generates a suite in which a large fraction of tests fail
immediately on `TODO:` sentinels. That is the design working correctly, and to a newcomer it
reads as the tool being broken. **The decision is to keep failing anyway**, because the
alternative — a suite that is green while asserting nothing — is the failure this whole section
exists to prevent, and it is unrecoverable: nobody investigates a passing test.

What makes it navigable is honesty rather than a softer default:

- Validation is aggregated into one message naming every unresolved sentinel and its file, not
  N identical per-test failures.
- `intest survey` (§17) predicts the fixture burden from `example` coverage **before** anyone
  adopts InTest, so the cliff is a known cost rather than a surprise.
- A meaningful suite runs on day one with no fixture work at all: every GET and DELETE contract
  test, every declared-error test, and every no-token 401 test need no request body.

Plausible-but-fake values (`"string"`, `0`) are the genuinely dangerous alternative —
schema-valid, so a permissive endpoint returns 200 and the suite asserts nothing while looking
healthy.

### Runtime tokens

**Referential integrity is the real problem** — `CreateOrder` needs a `customerId` that exists
in *this* environment.

```jsonc
{
  "customerId":    "{{fixture:seededCustomer.id}}",
  "apiKey":        "{{config:Orders:ApiKey}}",
  "correlationId": "{{runId}}",
  "requestedAt":   "{{utcNow}}"
}
```

**Resolution timing is part of the contract:**

| Token | Resolved | Cached |
|---|---|---|
| `{{runId}}` | Once per assembly run | Yes |
| `{{config:…}}` / `{{secret:…}}` | Per request, after configuration build | **No — see below** |
| `{{fixture:…}}` | After all `IAssemblyFixture` implementations complete | Yes |
| `{{utcNow}}` | Per request | No |

**`{{config:}}` and `{{secret:}}` are not cached, though the design intended them to be.** Measured
in v1-a by instrumenting `IConfiguration` with a counting wrapper: two `ResolvedBody` calls on one
resolver produced fresh reads for every token occurrence on both calls. Only `{{runId}}` is
genuinely fixed for the resolver's lifetime, bound in its constructor.

This is accepted rather than fixed. The reason the design separated cached from per-request tokens
was to stop validation resolving `{{config:}}` before configuration exists, and that still holds —
startup validation reads raw, unresolved values. The remaining cost, re-reading configuration per
request, is nil in practice because `IConfiguration` serves an in-memory dictionary once its
providers are built. A cache would buy no measurable time and would introduce a staleness question
nothing needs. Revisit if a provider ever makes reads expensive — a remote secret store, say.

`{{config:}}` and `{{secret:}}` are **how credentials stay out of committed fixtures.**
Generation warns on any literal value matching credential heuristics.

**`{{fixture:…}}` is live.** An `IAssemblyFixture` publishes a value with
`FixtureContext.Publish` (§13) during assembly initialisation, and every `{{fixture:…}}` token in
a fixture resolves against that published set once every registered fixture has run. Referencing
a key nothing published does not pass through as literal text and does not silently succeed — it
fails the same aggregated validation as an unfilled `TODO:` sentinel, naming the requested key and
listing every key that *was* published, so a typo is obvious rather than a mystery. Referential
integrity solved by hand — pointing a sentinel at data seeded some other way, or at a value
already known to exist in the target environment — is still the right call for anything an
assembly fixture does not seed.

### Environment overlays

`fixtures/create-order.json` deep-merged with `fixtures/{profile}/create-order.json`;
environment wins.

### Drift detection — one command mutates, and it is never `generate`

**`intest fixtures repair` is the only command that writes under `fixtures/`**, and it owns
three cases, not two:

| Case | What `repair` does |
|---|---|
| Fixture **file absent** for an operation that needs a request body | **Creates it**, composed by the tier precedence above (§10) and recording the tier in `$meta` |
| Fixture present, schema gained a required property | Adds it as a `TODO:` sentinel |
| Fixture present, property no longer in the schema | Flags it; never silently deletes hand-written data |

Creation belongs here because absence is the degenerate case of incomplete — a missing fixture
and an incomplete one are the same problem at different stages, and splitting them across two
commands would mean two things write under `fixtures/`. An earlier revision defined `repair` as
the second and third cases only, which left nothing in the design responsible for the first: a
fresh project with POST operations generated tests referencing fixtures no command created.

**`intest generate` validates and reports; it writes nothing under `fixtures/`.** A missing
fixture is reported as drift like any other:

```
Fixture drift (3):
  create-order.json   MISSING — no fixture for operation 'createOrder' (request body required)
  create-order.json   missing required property 'shippingMethod' (added in spec)
  update-order.json   property 'legacyRef' no longer in schema
Run `intest fixtures repair` to create and update fixtures.
```

Keeping `generate` read-only under `fixtures/` is what makes `--check` coherent, and it is why
the first run of a new project is a deliberate two-step: `generate`, then `repair`. `repair`
never overwrites an existing value — it only adds what is absent and flags what is stale, so
running it on a mature project cannot destroy hand-written data.

### Promotion — emits, does not write

`spec.source` points at a build artifact, which the next `dotnet build` overwrites. Writing
examples there would silently discard them.

**`intest fixtures promote` produces a paste-ready snippet and names the target file. It writes
nothing.**

| Producer | Snippet target |
|---|---|
| Swashbuckle | `ISchemaFilter` / `IOperationFilter`, XML `<example>`, annotations |
| Built-in | `AddOpenApiOperationTransformer` / schema transformer |
| NSwag | `SwaggerOperation` attributes, XML comments |

**Do not emit `WithOpenApi`** — deprecated in .NET 10 as `ASPDEPR002`, replaced by
`AddOpenApiOperationTransformer`.

Note a moving target for the built-in template: **.NET 10's `Microsoft.AspNetCore.OpenApi`
depends on Microsoft.OpenApi 2.x while ASP.NET Core 11 moves to 3.x**, where several formerly
concrete types became interfaces. The transformer snippets will need a version split within
v1's lifetime.

Every run reports the number, because visibility is what turns "we should add examples" into
practice:

```
Spec examples: 62 of 148 operations (42%)
```

**Response examples get the same treatment** — they give value assertions, not just schema
conformance.

---

## 11. Parallelization — consumer-owned

InTest does not own this and does not model it in `intest.json`. MSTest already exposes it properly.

### What InTest emits

**`AssemblyInfo.cs` is the single authoritative place**, scaffolded once at init, never
regenerated, team-owned:

```csharp
[assembly: DoNotParallelize]
```

Sequential matches **MSTest's actual runtime default** with no attribute present, and is the
safe start against a shared deployed environment. Teams change it by editing one line they
own.

The generated `.csproj` sets **neither** `MSTestParallelizeScope` nor
`MSTestParallelizeWorkers`. Those MSBuild properties (MSTest 4.3+) *generate* the assembly
attribute, so combining them with a hand-written one is a build break. *Measured:*

```
error CS0579: Duplicate 'Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize' attribute
```

pointing into generated `obj/…/AssemblyInfo.cs`. To convert that into something actionable,
the generated `.csproj` carries a guard target. *Measured working:*

```xml
<Target Name="InTestGuardParallelizeProperties" BeforeTargets="BeforeBuild"
        Condition="'$(MSTestParallelizeScope)' != '' or '$(MSTestParallelizeWorkers)' != ''">
  <Error Code="INTEST0001"
         Text="Parallelization intent is declared in AssemblyInfo.cs. Remove
               MSTestParallelizeScope/MSTestParallelizeWorkers from the project file and edit
               [assembly: Parallelize] or [assembly: DoNotParallelize] in AssemblyInfo.cs instead." />
</Target>
```

**Per operation**, where `mutates: true` (defaulted from verb — POST/PUT/PATCH/DELETE — and
overridable), emit `[DoNotParallelize]` on that test. Harmless while the assembly is
sequential; protective the moment someone enables parallelism. This is the slot
`[ResourceLock]` fills when 4.4 ships stable.

### What rev 2 got wrong here

Rev 2 argued that MSTEST0001 forces explicit intent, so "silence isn't an option." *Measured
on 4.3.3:* a project with a test class, no attribute and no property builds with **0
warnings**. MSTEST0030 fires as a warning in the same build, so analyzers are loaded —
MSTEST0001 simply is not surfaced at its default severity, and appears only when raised via
`.globalconfig`. It is not a forcing function and the design does not lean on it.

Rev 2's `.editorconfig` line `mstest_parallel_safety_mode = always` is removed — MSTEST0076
ships in 4.4.

Rev 2's keyed `SemaphoreSlim` map in `InTest.Runtime` stays cut. Hand-building a permanent
substitute for `[ResourceLock]` is waste.

### What the README documents

- Parallel tests collide on shared data unless each creates its own entities tagged with run
  ID + test name.
- **`Workers` is deliberately not set by InTest.** `[assembly: Parallelize(Scope =
  ExecutionScope.MethodLevel)]` with no `Workers` defaults to the agent's **logical processor
  count**. The day a team enables parallelism, concurrency against the shared environment is
  whatever hardware the pool hands them, and it changes when the pool changes. `Workers` is
  the dial if a gateway starts returning 429.
- **Cross-process is unsolvable at this layer.** Two concurrent PR pipelines against one shared environment do
  not coordinate regardless of any in-assembly setting.

---

## 12. Coverage report

Everything InTest did not cover, or covered less thoroughly than a full contract test, in one
report per run — human- and machine-readable.

```
Operations in spec: 148
Generated:          113
Skipped:             35
  multipart/form-data          8   (not supported in v1)
  application/xml              3   (not supported in v1)
  operator-skipped            24   (intest.json operations.*.skip)
Notes:
  untagged operations          4   (→ DefaultTests)
  synthesized operationIds    31   (spec has no operationId)
  inline response schemas     19   (bundled under synthesized keys)
  status-only contract tests   6   (no response schema declared — see §9)
  bodiless statuses           11   (204/205/304 — status-only by design, not a gap)
  auth tests gated on a second identity  12   (wrong-scope 403 cases; whether they skip is decided at run time)
  auth tests requiring an under-scoped second identity  9   (subset of the above; skip also needs the identity's declared Scopes — see §9)
  multiple version prefixes        /v1 (49 operations), /v2 (64 operations)
  unevaluatable keywords       2   (const ×1, if/then ×1 — see §9)
```

**Skips remove tests. Notes do not.** The distinction matters, because rev 3 originally had
`no response schema` under *Skipped*, which silently deleted every bodiless-204 operation from
the suite. Only two things cause a skip in v1: an unsupported content type, and an operator
writing `operations.*.skip`. Everything else is generated and noted.

Each note closes a silent-omission path:

- **`operator-skipped`** — `intest.json`'s `operations.*.skip`. Deliberately-skipped operations
  are precisely the ones that get forgotten, so they are reported like any other gap.
- **`inline response schemas`** — §9. Those operations *are* tested; the note makes the
  synthesized keys discoverable.
- **`status-only contract tests`** — §9. Generated and running, but asserting less than a full
  contract test. Fixable by adding a schema to the spec.
- **`bodiless statuses`** — §9. Listed for completeness and explicitly *not* a gap; 204/205/304
  have no body by definition.
- **`auth tests gated on a second identity`** — §9. Named "gated on", not "skipped for want
  of": the count is of generated wrong-scope 403 cases — the only ones that *require* a second
  identity to run at all. Whether one actually skips is decided at run time by
  `RequireMultipleIdentities`, against whatever `ITestTokenProvider` a project registers — the
  CLI writes this report long before that provider exists and cannot know that number. Without
  this line, gated tests would be indistinguishable from tests that were never generated.
- **`auth tests requiring an under-scoped second identity`** (F11) — §9. Narrower than the note
  above: of the generated wrong-scope 403 cases, how many belong to an operation that declares
  required scopes at all — the ones whose skip depends on the second identity's own declared
  `Scopes`, not just its presence. Like its sibling, not a skip count, for the same reason: the
  CLI cannot know at generation time what any project's provider will advertise.
- **`unevaluatable keywords`** — §9. The only remaining route to a false green, made visible.

The JSON form lets CI assert coverage has not silently dropped. It is also the v2 backlog,
derived from real specs rather than guessed at.

**There is no version selection in v1.** Rev 2 carried a `spec.version` field defaulting to
`"latest"` while simultaneously arguing that inferring "latest" from route prefixes was too
ambiguous to leave implicit — the default required exactly the inference the same paragraph
forbade. Both are removed.

v1 generates a test for **every operation in the document**. Where a document contains several
versions, their paths differ, so they produce distinct operations and distinct tests already;
nothing needs selecting. If InTest detects more than one version-looking path prefix it emits a
**note** in the coverage report — visibility without inference:

```
Notes:
  multiple version prefixes   /v1 (24 operations), /v2 (31 operations)
```

Selecting or splitting versions is a v2 feature, and it will require an explicit rule stated at
that time rather than a default that guesses.

---

## 13. Lifecycle

### Assembly scope

```csharp
[TestClass]
public static class TestHost
{
    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext ctx)
    {
        Configuration = BuildConfiguration(profile);
        RunId         = InTest.RunId.Create(Configuration);
        Fixtures      = FixtureStore.Load();              // fixtures/, before anything needs it
        Root          = BuildServiceProvider();           // TestStartup registrations
        Schemas       = await LoadSchemaBundleAsync();    // spec.json beside the DLL
        EnsureNoBaseUrlPrefixDuplication();                // fails fast, before any request
        await AwaitReadinessAsync(ctx);
        await RunFixturesAsync(ctx);                       // seeds, then builds the token resolver
        await ValidateFixturesAsync();                     // aggregated — §10
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup(TestContext ctx)
        => await DrainCleanupAsync();
}
```

**Order is load-bearing.** In full: profile and configuration, then the run ID, then the fixture
store, then the service provider — with the team's own `TestStartup` registrations already
composed in, so a fixture can take a constructor dependency on anything it registered,
`IHttpClientFactory` included — then the schema bundle, then a check that the configured base
URL does not repeat a path prefix the spec's operations already carry (failing here, naming both
halves, beats every request 404ing for a reason nobody can see), then readiness, then fixtures
(topologically ordered, `AppliesTo`-filtered), then the token resolver built from whatever
fixtures just published, and only then validation.

**Validation runs last because it depends on what seeding produces.** Seeding needs an
`HttpClient` and a service that has passed readiness, and `{{fixture:…}}` cannot be checked
until seeding has published the keys it resolves against — validation cannot run any earlier
than this without checking against information that does not exist yet (Appendix).

**On a dead API, only the readiness failure surfaces.** Readiness throws before fixtures run, so
the fixture-validation report — which would otherwise flag every sentinel and unresolved token —
is never built. This is deliberate: an unreachable service is the more actionable error to see
first. The cost is that anyone debugging fixtures against a service that happens to be down sees
only the readiness failure, not the fixture report, until the service is reachable again.

**Diagnostics from a *passing* `[AssemblyInitialize]` need `DisplayMessage`, not `WriteLine`
(§18, New findings).** VSTest buffers `TestContext.WriteLine`, `Console.Out`, and `Console.Error`
written during assembly initialisation into the result they would attach to, and only flushes
that buffer on failure — so all three are invisible on a passing run. `TestContext.DisplayMessage`
at `MessageLevel.Warning` escapes that buffering without failing the run, which is why the
fixture-validation report and `FixtureRunner`'s skip lines go through it, and why a drain failure
during `AssemblyCleanup` is additionally written to `Console.Error`.

Signatures must satisfy MSTEST0012/MSTEST0013. `TestContext` on `[AssemblyCleanup]` requires
3.8+, satisfied by the 4.3 floor.

**MSTest v4 throws when `TestContext.TestName` or `FullyQualifiedTestClassName` is read in
`AssemblyInitialize` or `ClassInitialize`.** Generated code does not, but this belongs in the
`IAssemblyFixture` docs — a team fixture reaching for it gets an exception that fails every
test with a message that does not say "setup broke."

### `IAssemblyFixture`

```csharp
public interface IAssemblyFixture
{
    Type[] DependsOn { get; }        // topologically sorted — NOT an int Order
    string[] AppliesTo { get; }      // profiles; empty = all
    Task InitializeAsync(FixtureContext ctx, CancellationToken ct);
}
```

Integer ordering is the thing everyone regrets — someone always needs to slot between 15 and
20.

**`AppliesTo` and `DependsOn` interact.** A fixture skipped because its `AppliesTo` excludes the
current profile also skips every fixture that transitively depends on it, and the skip log names
the dependency that caused it, not just the profile check. Running a dependent against state its
skipped dependency never created would be exactly the silent-wrong-state failure `AppliesTo`
exists to prevent, and a fixture that genuinely does not need its dependency's state should not
declare `DependsOn` in the first place. The rejected alternative was validating, inside
`FixtureGraph`, that a dependent's `AppliesTo` is no broader than its dependency's — that would
make `FixtureGraph` profile-aware, and it is deliberately a pure ordering function that knows
nothing about profiles; only the runner, which has both the order and the active profile, can
make this call.

**Cleanup is registration-based, not a symmetric second method.** Teardown is written next to
the thing that created it and drained in reverse:

```csharp
var tenant = await _api.CreateTenantAsync(ct);
ctx.Publish("seededTenant.id", tenant.Id);       // available to {{fixture:…}}
ctx.OnCleanup(() => _api.DeleteTenantAsync(tenant.Id));
```

Wrap fixture execution so failures surface clearly.

**Only `AddSingleton` is a supported registration for `IAssemblyFixture`.** A fixture registered
`AddScoped` or `AddTransient` that also implements `IDisposable` is disposed when
`AssemblyInit`'s own DI scope ends, while any `OnCleanup` closure it registered survives on the
`FixtureContext` until `AssemblyCleanup` runs — a disposed-object trap for anything that strays
from the scaffolded shape.

### Readiness

Post-deploy cold start is the single largest source of flaky gates.

- **`consecutiveSuccesses`** (default 2). During slot swaps and rolling deploys, a single 200
  can come from the old instance.
- **`expectVersion`** — assert the deployed build, not just liveness. Sourced from a pipeline
  variable via config.

**The probe path follows ordinary URI resolution**, and the distinction matters because health
endpoints conventionally sit at the host root while the API sits under a prefix:

| `readiness.path` | Resolves to |
|---|---|
| `health/ready` | `{baseUrl}/health/ready` — under the API prefix |
| `/health/ready` | `{origin}/health/ready` — the host root, and the scaffold's default |
| `https://other/health` | itself |

**A 404, 405, 410 or 501 on the probe is terminal, not retried.** Those mean the path is wrong,
and no amount of waiting fixes a route that does not exist. The v0 acceptance run spent the full
120 seconds discovering a misconfigured probe path that could have been reported in three.

Fails with `Service did not become ready within 120s (last response: 503)` — not 200 confusing
test failures. Opt-out per profile. Falls back to a configured lightweight GET where no health
endpoint exists.

**Readiness gating, not per-test retries.** `RetryAttribute` exists (3.8+) and InTest does not
emit it — retries hide real flakiness, and Microsoft's own guidance is to address the root
cause.

**Readiness probes on its own client — F10, measured.** An earlier draft attached `AuthHandler`
to the same named client (`InTestClients.Api`) that both the generated tests *and* the readiness
probe resolved, on the reasoning that one client was simpler than two. Measured against a
secured sample with its identity provider unreachable: the handler throws on every request
through that client, including the anonymous `/health/ready` probe that needed no token at all,
and the failure surfaces as

```
ReadinessTimeoutException: Service did not become ready within 120s
(last response: HttpRequestException)
```

— a dead identity server reported as a dead API, after a two-minute wait, with nothing in the
message naming a token, an identity provider, or auth at all. `TestHost.InitializeAsync`
therefore resolves a second named client, `InTestClients.Readiness`, registered with
`RunIdHandler` but never `AuthHandler`, and probes on that one instead. `RegisterInTestClients`
is the single seam both clients are registered through, so this cannot regress by one of the two
call sites drifting from the other. Guarded by
`InTestClientsTests.ReadinessProbeDoesNotRunApiClientHandlers` (a handler attached to
`InTestClients.Api` must never run for a readiness probe on `InTestClients.Readiness`) and, over
the wire, `GeneratedSuiteExecutionTests.ReadinessProbeSurvivesAThrowingApiHandler` (a throwing
handler on the API client fails the first *test* that hits it, not readiness, against a real
generated-and-built suite).

### Class scope

`[ClassInitialize]` / `[ClassCleanup]` generated per test class, delegating to an optional
team-implemented `IControllerFixture`. Not required for most classes.

- Base-class class-scope hooks need explicit
  `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` — class-scope hooks are not
  inherited by default the way test-scope ones are.
- **Do not specify `ClassCleanupBehavior`.** The enum is removed in MSTest v4; end-of-class is
  the only behaviour.

### Base class

Lives in `InTest.Runtime` — fixes ship by bumping a package, not regenerating.

```csharp
public abstract class ApiTestBase
{
    private IServiceScope _scope = null!;

    public TestContext TestContext { get; set; } = null!;

    protected IConfiguration Config      => TestHost.Configuration;
    protected IServiceProvider Services  => _scope.ServiceProvider;   // per-test SCOPE
    protected string RunId               => TestHost.RunId;
    protected string TestId              => InTestId.ForTest(TestHost.RunId, TestContext.TestDisplayName);
    protected HttpClient Client { get; private set; } = null!;

    [TestInitialize]
    public void ApiTestInitialize()
    {
        _scope = TestHost.Root.CreateScope();
        InTestAmbient.TestId.Value = TestId;                             // read by RunIdHandler
        Client = _scope.ServiceProvider
                       .GetRequiredService<IHttpClientFactory>()
                       .CreateClient(InTestClients.Api);
    }

    [TestCleanup]
    public void ApiTestCleanup() => _scope.Dispose();
}
```

- **`IServiceProvider`, not `IServiceCollection`.** The collection is registration-time and
  belongs in `TestStartup.cs`.
- **Per-test scope**, not the root provider — root-resolved scoped services become captive
  dependencies that surface under parallelism.
- `[TestInitialize]` **is** inherited (base first, then derived), so teams add their own
  without touching generated code.
- Keep `ApiTestBase` **abstract and free of `[TestMethod]`s** — base-class test methods get
  reflection-discovered and cause duplicate-discovery problems.

Teams insert their own layer via `project.testBaseClass`; it derives from `ApiTestBase`.
Scaffolded as a stub at init.

**Caution:** base classes in test projects become dumping grounds. `ApiTestBase` = **ambient
context only** (config, services, client, IDs, scope lifecycle). Domain helpers go in the
team's base class or extension methods.

### `ITestTokenProvider`

```csharp
public interface ITestTokenProvider
{
    /// Identities this provider can issue tokens for, in order. Index 0 is the default identity
    /// every ordinary case authenticates as; index 1, when present, is the "some other identity"
    /// the wrong-scope 403 case selects (v1-c decision 7). Empty or single-element gates that
    /// case off. Also the source of the coverage-report count. Each element carries its own
    /// declared TestIdentity.Scopes (F11), which RequireSecondaryIdentityLacks compares against
    /// an operation's required scopes to decide whether a wrong-scope 403 is still provable.
    IReadOnlyList<TestIdentity> Identities { get; }

    Task<string> GetTokenAsync(string audience, string? identity = null, CancellationToken cancellationToken = default);
}
```

The `identity` parameter is what the auth contract tests (§9) need — wrong scope. (The no-token
case sends no `Authorization` header at all and never reaches the provider.)

`IReadOnlyList`, not `IReadOnlyCollection`: the CLI generates test code long before an adopter
has written a provider, so generated code can never reference an identity by name — only by
position (decision 7). A case selects an identity by *slot*, never by name; nothing anywhere
emits a literal identity name into a plan or a template. This reshaped across two breaking
changes: `IReadOnlyCollection<string>` (what this shipped as) to `IReadOnlyList<string>`, made
while nothing outside this repository implemented the interface yet — the last point at which it
was free — and then, in F11, `IReadOnlyList<string>` to `IReadOnlyList<TestIdentity>`, to carry
each identity's own declared scopes so the wrong-scope 403 guard has something to compare
against. From the first published version onward, this ordering is a semver promise (§3), not an
implementation detail.

**`Identities` exists so the 403 gate is a property, not a probe.** Without it the only way to
discover whether a provider supports a second identity is to call `GetTokenAsync` with an
invented one and interpret the result — which would either throw, return the default token, or
succeed misleadingly, none of which is a reliable signal, and all of which are worse than the
day-one failure the gate exists to prevent.

**The gate is read at run time, not by `MemberCondition`.** `ApiTestBase.RequireMultipleIdentities()`
reads `Identities.Count` from the registered provider and calls `Assert.Inconclusive` with a
stated reason when it is below two — after `[AssemblyInitialize]` has genuinely run, which is
what makes this reliable. §9 records the measurement this replaces: a `MemberCondition` evaluates
*before* `[AssemblyInitialize]`, so it cannot see anything the DI container built, and the gated
test came back `Skipped` inside a run the console reported `Passed!` — auth testing silently
switched off with nothing surfacing. The coverage-report count of cases that *require* a second
identity falls out of the same `Identities` property, at generation time (§12).

**InTest ships exactly one implementation: a static-token provider**, whose `Identities`
returns a single-element collection. The 403 tests therefore gate off by construction — no
special case in the condition, no null check, no shipped-provider carve-out. Any team that
implements a second identity turns them on by returning more than one.

Auth is otherwise entirely the team's, and InTest takes no dependency on any identity library.
Rev 2 listed `DefaultAzureCredential`, which would have pulled `Azure.Identity` — an undeclared
dependency absent from §4's pinned table — into every consumer of `InTest.Runtime`, in a
project that must not assume Azure at all. Client-credentials, managed identity, mTLS and
everything else are documented samples in the README, not shipped code and not a fourth
package.

Consumed by `AuthHandler`.

---

## 14. Run identity and data hygiene

### Format

```
{prefix}-{yyyyMMddTHHmmss}Z-{8 hex}
tjay-20260816T142233Z-a3f91c2e
ci4471-20260816T090114Z-77b0de54
```

**Timestamp is UTC**, explicitly — the sweeper derives age from the ID alone, so no
`created_at` column is required on every seeded entity. Also sorts lexicographically and is
human-readable.

### `TestId` — corrected

Rev 2 defined `TestId = $"{RunId}-{TestContext.TestName}"`. *Measured:* `TestContext.TestName`
returns the **bare method name for every `[DataRow]` row** — two rows of the same method both
reported `TestName='NameUnderDataRow'`. Every variation of an operation would therefore share
one `TestId`, and `X-Test-Run-Id` could not locate a single failing row in App Insights, which
§14 calls the thing that "alone justifies the scheme."

**`TestContext.TestDisplayName`** returns the row's `DisplayName` (measured: `"row one"`), and
`TestContext.TestData` carries the row arguments if a stable key is preferred over the display
string. `TestId` derives from `TestDisplayName`.

This makes §9's "display names are mandatory" rule load-bearing. It is a deliberate, narrow
exception to §5's "anything volatile goes in display, never identity": the display name is
volatile in the *test-history* sense but stable within a run, which is all a correlation
header needs.

#### `TestId` has different constraints from `RunId`

These are two identifiers with two jobs, and rev 2 conflated them.

| | `RunId` | `TestId` |
|---|---|---|
| Goes into | Seeded entity names, email local-parts, external reference IDs | The `X-Test-Run-Id` header and failure messages only |
| Length cap | 40 (external `maxLength` limits) | 120 |
| Charset | lowercase alphanumeric + hyphen | **ASCII, mandatory** |

The ASCII rule is not stylistic. *Measured:* `HttpClient` **throws** on a non-ASCII header
value —

```
HttpRequestException: Request headers must contain only ASCII characters.
```

— so it fails loudly rather than corrupting, but it fails on *every request in that test*
with a message that says nothing about run IDs. And the default templates trigger it: §9's own
display-name example is `quantity = -1 → 400`, containing U+2192, and the string variation
catalog mandates emoji, RTL and combining-character cases by design.

`InTestId.ForTest(runId, displayName)` therefore:

1. Transliterates the display name to a lowercase ASCII slug; anything outside
   `[a-z0-9-]` collapses to a hyphen.
2. **Appends a short stable hash of the full original display name whenever the slug is lossy.**
   Without this, `notes = "😀"` and `notes = "אב"` both reduce to the same token and the
   correlation header stops distinguishing the very cases the catalog exists to test.
3. Truncates the slug, never the hash, to the length cap.

```
tjay-20260816T142233Z-a3f91c2e-quantity-1-400          (lossless)
tjay-20260816T142233Z-a3f91c2e-notes-h7f2a9            (lossy → hashed)
```

§14 calls the correlation header "the thing that alone justifies the scheme." Collision-freedom
is what makes that true.

### Prefix derivation

Automatic, with config override:

1. `TF_BUILD` set → Azure DevOps. Prefix from `BUILD_BUILDID`.
   *(`Build.BuildNumber` is a display string and can repeat — `v1.0.0`. `Build.BuildId` is the
   unique one.)*
2. `GITHUB_ACTIONS` set → GitHub Actions. Prefix from `GITHUB_RUN_ID`.
   *(Required because the project is public; without it a CI run looks like a developer run.)*
3. Generic `CI` env var set → prefix `ci`.
4. Otherwise → local. Prefix from OS username.
5. Config prefix overrides all of the above.

If the prefix is *required* rather than derived, half the teams leave the template default and
nothing is traceable.

### Constraints

- Cap total length (default 40) — run IDs land in entity names, email local-parts and external
  reference IDs with `maxLength` limits.
- Prefix charset: lowercase alphanumeric and hyphen only.
- Document the remaining budget for fixture authors.

### Propagation

- `X-Test-Run-Id: {TestId}` on every request via `RunIdHandler`.
- Written to `TestContext` in `AssemblyInitialize` → lands in `.trx` and the AzDO summary.
- Included in every contract-assertion failure message.

### Cleanup guarantees

`AssemblyCleanup` does not run on process crash, pipeline cancellation, or agent timeout. Plan
for leakage:

- Every cleanup action **idempotent** — deleting an already-deleted entity is a no-op.
- Everything created tagged with the run ID.
- **An out-of-band sweeper** removes anything older than a day.

**Non-prod only.** Integration tests target pre-production environments. A team that points this at
production owns the consequences.

---

## 15. Interfaces and assertions

The rule: **interface where you expect someone else to write an implementation you'll never
see. Template where you're choosing among implementations you ship.**

| Concern | Mechanism | Rationale |
|---|---|---|
| Auth token | `ITestTokenProvider` | Varies per environment and service |
| Test data | `ITestDataProvider` | Files, seeded DB, factory service — unpredictable |
| Assembly setup | `IAssemblyFixture` | Team-specific seeding |
| Class setup | `IControllerFixture` | Optional, per test class |
| HTTP invocation | **Template set** | A facade makes generated code read poorly |
| Assertions | **Template set** | See below |
| Contract assertions | Shared `ApiResponseAssertions` | Justified by message quality |
| Base URL / env | Config | It's string lookup |
| Schema validation | Shared `SchemaBundle` | One right way to do it |
| Parallelization | **Neither — consumer-owned** | §11 |

### The two assertion sets, and why they differ

Shouldly builds messages by **reading source text at runtime**. MSTest v4's `Assert` uses
**`[CallerArgumentExpression]`**, baked into the IL at compile time. *Measured*, same binary,
run with the source file present and then renamed away:

```
                             SOURCE PRESENT                     SOURCE ABSENT
MSTest  Assert.IsTrue        Assert.IsTrue(Map["boo"] > 5)      Assert.IsTrue(Map["boo"] > 5)
Shouldly block-bodied        Map["boo"] should be 2 but was 1   1 should be 2 but was not
Shouldly expression-bodied   public void Shouldly_Expression…   1 should be 2 but was not
                             BodiedMap["boo"] should be 2   ← garbled
```

Three consequences.

1. **`<DebugType>full</DebugType>` is not required and is not emitted.** Rev 2 set it on the
   authority of Shouldly's documentation. Portable PDBs — the SDK default — produced the
   correct Shouldly message on `net10.0`. The variable is source-file presence, not PDB type,
   and forcing `full` is a liability on Linux agents for no gain.
2. **Generated tests are always block-bodied with the assertion on its own line.** Shouldly on
   an expression-bodied method does not degrade — it produces an actively wrong message by
   splicing the method signature. This is a hard template rule, not a style preference.
3. **Shouldly is primary; MSTest `Assert` is the second set.** Shouldly gives better messages
   in the normal case (local runs, and CI that builds and tests in one job). MSTest `Assert`
   survives published-artifact runs unchanged. `project.assertions` is an array, defaulting
   to `["shouldly"]`; `intest assertions add` appends, never swaps, because hand-written
   assertions are never migrated.

**Why assertions are not behind an interface:** Shouldly reads the code before the `ShouldBe`
statement. Behind `IResponseAsserter.StatusShouldBe(...)`, the expression read is the
*wrapper's*, so you pay for Shouldly and get generic messages.

`ApiResponseAssertions` is justified on different grounds — contract checks need *custom*
messages no library provides, built from NJsonSchema's `Kind` and `Path`:

```
GET /api/orders/{id} → expected 200, got 503 (1,204ms)
Run:  tjay-20260816T142233Z-a3f91c2e-GetOrderById_Contract
Body: {"error":"upstream timeout"}
```

Because these messages are constructed rather than read from source, they are unaffected by
the source-presence problem above.

---

## 16. Testing InTest

1. **Golden-file tests.** Reference specs in, expected output byte-compared.
2. **Compile verification.** Every golden output must build. The real signal.
3. **Producer matrix.** The same API surface as produced by Swashbuckle, the built-in package
   and NSwag — covering absent, synthesized and `{Controller}_{Action}` operationIds.
4. **Round-trip on representative specs.** The project is public, so private specs
   cannot live in this repo. Two jobs: a public job over sanitised, checked-in specs
   covering the shapes that matter, and an internal pipeline running the same assertions
   against private specs.
5. **Determinism.** Generate twice, assert identical output. Catches ordinal-dependent naming
   and dictionary-ordering bugs. `RandomizeTestOrder` (4.3) is useful here.
6. **Frozen-axis tests.** Attempting a frozen change fails with the expected message.
7. **Orphan detection tests.** Rename an operation, assert config and fixture orphans are
   reported.
8. **Assertion-formatting test.** Assert that generated methods are block-bodied and that a
   Shouldly failure message contains the asserted expression. This guards the rule in §15 that
   is otherwise invisible until a failure message goes wrong in production.
9. **`TestId` ASCII and collision tests.** Feed the string variation catalog's own emoji, RTL
   and combining-character cases through `InTestId.ForTest` and assert every result is ASCII
   and every pair distinct. Then send one as a real header and assert no throw. Without this,
   §14's correlation guarantee is an untested claim.
10. **Schema-keyword report test.** A spec using each of the seven unevaluatable keywords must
    produce a coverage-report entry for each. This is the only thing standing between a 3.1
    spec and a false green.
11. **URL joining test.** Base URLs with and without a trailing slash, paths with and without
    a leading slash, asserting the resolved absolute URI. Cheap, and it guards a silent-wrong
    -route failure (§7).
12. **Fixture lifecycle test.** From an empty `fixtures/` directory and a spec containing a POST
    operation: `generate` reports the fixture as missing and writes nothing; `repair` creates it
    at the expected tier; a second `repair` is a no-op; and a `repair` run after a hand-edited
    value leaves that value untouched. This is the path that had no owner until it was traced
    end to end, and the no-overwrite assertion is what protects hand-written data.

---

## 17. Delivery

### v0 — internal milestone, not a release

Contract tests only. HttpClient only. JSON only. Fixture tiers 1 and 4 only. No latency
assertion. Pointed at one real API in a real pipeline. Two to three weeks. Throwaway quality
is fine.

#### `intest survey` — a shipped command, not a private exercise

Earlier drafts described a one-off parse pass over the maintainers' own specs whose results
would *decide* whether v1 capabilities existed. That is the wrong dependency for a tool other
organisations adopt: it would mean a public tool's feature set was determined by one spec
population that no adopter can see, and in two places it produced requirements InTest has no
standing to impose (§6, §9).

The survey is worth keeping, so it ships as a command any adopter can run against their own
specs before committing to InTest:

```
intest survey <spec-glob|url>
```

It accepts the same inputs as `spec.source` — a glob over local files, or a URL — because an
adopter evaluating InTest often has only a Swagger endpoint and no checked-out spec.

**It measures; it never gates.** Every capability in §2 exists regardless of what any survey
returns. What the numbers change is *prioritisation* for maintainers, and *expectations* for
adopters — an adopter learns before they start how much of their suite will run on synthesized
operationIds, how many fixtures will land in tier 4, and whether the schema-keyword report will
fire.

| Measure | Informs |
|---|---|
| % with `operationId` | How much of the suite runs on synthesized keys (§6) |
| Producer mix | Which promotion snippets get polished first (§10) |
| YAML vs. JSON | Whether `YamlReader` is on the hot path |
| Tags: single / multiple / none | Whether the default tag strategy suits this spec (§8) |
| % with response schemas | How many contract tests degrade to status-only (§9) |
| % with `security` declared | How many auth tests will be generated (§9) |
| % with request `example` | How much tier-4 fixture work adoption implies (§10) |
| % of schemas using `allOf` / `oneOf` / `discriminator` | Polymorphism exposure |
| % of specs that are OpenAPI 3.1 rather than 3.0 | Whether NJsonSchema's seven unevaluatable keywords can occur at all (§9) |
| Census of 2019-09/2020-12 keywords used | How often the keyword report would fire. Sustained non-trivial numbers across adopters are the signal to revisit the validator choice |
| % of response schemas inline rather than `components.schemas` | How much of the bundle runs on synthesized keys (§9) |

Running it over the maintainers' own specs during v0 is simply the first use of a shipped
feature, and gives the keyword-report and synthesis paths real input early.

Rev 2's check B (Shouldly message survival) is **resolved** — see §15. Rev 2's open question
about `TestContext.TestName` under `DataRow` is **resolved** — see §14.

Also to answer in v0:

- Does the two-stage `TestPlan` boundary hold, or do templates need things the plan doesn't
  carry?
- Does partial-class regeneration survive a real spec change?
- What does readiness actually look like against a real deploy — and does `consecutiveSuccesses`
  matter there?
- How large is the bundled schema document for the biggest real spec, and how long does
  `AssemblyInitialize` bundling take?

### v1 — ships

Everything in §2, plus the project surface a public repository needs. These are v1
deliverables, not documentation chores: the first external adopter meets them before they meet
any code.

| Artefact | Must state |
|---|---|
| `README.md` | What InTest is for, what it is **not** for, and §2's adoption requirements — so an evaluation ends in five minutes when it is the wrong fit |
| `docs/getting-started.md` | The end-to-end adoption path, from an existing API to a gate: survey, spec wiring, `init`, configuration, `generate`, fixtures, run, commit, CI. **Kept in step with this spec** — tracing it end to end is what exposed the unowned initial-fixture creation (§10), and it is the cheapest way to find the next gap of that kind |
| `CONTRIBUTING.md` | The dependency policy (§4), the semver contract (§3), and the rule that no capability is gated on any one spec population (§17) |
| `SECURITY.md` | Private reporting route, supported versions, and what is deliberate rather than a vulnerability — untrusted specs, real HTTP to configured hosts, `security` payloads off by default, best-effort cleanup |
| Issue and PR templates | A spec-issue template especially, since at design stage a careful reading is worth more than a patch |
| Release process | Documented in `CONTRIBUTING.md`, matching §3's compatibility contract |

v1 must land before anything ships externally.

### v2 backlog

Ordered. The first item is first everywhere else in this document and belongs at the top of the
one list that exists to be the backlog.

1. **xUnit and NUnit template sets.** The largest constraint on reach — MSTest is 21.7% of
   framework downloads (§18). §3's portability boundary exists to keep this additive rather
   than a rewrite; the sharpest coupling to break is `TestId` from `TestContext.TestDisplayName`.
2. **Flurl HTTP pack.** Requires first resolving how `ApiTestBase.Client` is typed — generic
   base, third package, or no client on the base (§3).
3. **Version selection.** An explicit rule, never inferred (§12).
4. **`[ResourceLock]` and `[DependsOn]`** when MSTest 4.4 ships stable. `[DependsOn]` also
   reopens the stateful-flow non-goal (§2).
5. **Non-JSON content types** — `multipart/form-data`, `x-www-form-urlencoded`, XML, binary.
6. **Multi-version projects** and **scenario-per-class layout**.
7. **Shouldly 5** when GA — removes the source-reading dependency entirely (§15).
8. **Microsoft.OpenApi transformer snippets for ASP.NET Core 11**, which moves to
   Microsoft.OpenApi 3.x while .NET 10 stays on 2.x (§10).
9. **WCF/SOAP**, client provided rather than generated.

---

## 18. Verification record

*Measured* = established by running code, not by reading docs. Where an SDK version is
load-bearing to a finding, the entry states it — the toolchain moves, so a version pinned
here would not.

### Corrected from rev 2

| Rev 2 claim | Rev 3 |
|---|---|
| Pin `Microsoft.OpenApi` 2.3.x | **Wrong.** All 2.x stable versions are deprecated with a vulnerability advisory, as are 3.0.0–3.5.3. Floor 3.5.4; use 3.10.0 |
| MSTEST0001 forces explicit parallelization intent | **Wrong.** *Measured:* silent at default severity on 4.3.3; only appears when raised via `.globalconfig` |
| `.editorconfig` `mstest_parallel_safety_mode = always` | **Wrong for 4.3.3.** MSTEST0073–0077 ship in 4.4 |
| Pin `ClassCleanupBehavior.EndOfClass` | **Wrong.** Enum removed in MSTest v4 |
| Shouldly requires `<DebugType>full</DebugType>` | **Wrong.** *Measured:* portable PDBs produce correct messages; source-file presence is the variable |
| `TestId` from `TestContext.TestName` | **Wrong for data-driven tests.** *Measured:* returns the bare method name for every `DataRow`. Use `TestDisplayName` |
| Scaffold `[assembly: DoNotParallelize]` *and* recommend the MSBuild property | **Build break.** *Measured:* `error CS0579: Duplicate 'DoNotParallelize' attribute`. AssemblyInfo.cs is authoritative; `INTEST0001` guards the property |
| Flurl primary, HttpClient may lag; two packs | **One pack.** `ApiTestBase.Client` cannot be typed for both from one package; shipping one removes the constraint. Flurl deferred to v2 — its last commit is 2025-01-01, last release 2024-01-17 |
| `spec.version` defaulting to `"latest"` | **Deleted.** The default required the route-prefix inference the same section forbade |
| `spec.hash` | **Deleted.** No stated writer, no stated failure mode, and undefined against a build artifact that changes every build |
| Shipped `DefaultAzureCredential` token provider | **Deleted.** Would pull an undeclared `Azure.Identity` dependency into every consumer. Static provider only; auth is the team's |
| Scaffolded runsettings declaring `profile` | **Commented out.** `<RunSettingsFilePath>` loads it unconditionally, which made `INTEST_PROFILE` unreachable |
| `--check` costs "one prerequisite, not two" | **Three.** API build (cross-repo: clone + build), pinned tool version, and a tool-version match check |
| Bundle only `components.schemas` | **Every response schema.** Inline schemas get synthesized `op:{id}:{status}:{mediaType}` keys, or contract tests silently degrade to status-code checks |
| Skip operations with no response schema | **Status-only contract test instead.** Skipping deleted every bodiless 204/205/304 operation from the suite, and discarded the status check that the inline-schema argument says has value |
| Auth tests cost "a multi-identity token provider (§13)" | **The cost is the team's.** InTest ships a static provider, so 401 tests always run and 403 tests are gated at run time by `RequireMultipleIdentities` (§9) with a coverage note — never red on day one for a capability InTest chose not to ship |
| `intest upgrade` referenced but undefined; no CLI inventory anywhere | **§5 command surface** — every command, what it writes, what it never writes, exit codes, and a stated exit-code convention |
| No command created the **initial** fixtures — `generate` is read-only under `fixtures/` and `repair` only amended existing files | **Resolved.** `repair` owns creation too; a missing fixture is reported as drift, and the first run of a project is a deliberate `generate` then `repair` (§10) |
| §2 claimed URL input, but every downstream mechanism assumed a local build artifact — MSBuild cannot copy from `https://` | **Resolved.** A URL source is snapshotted to a committed, generator-owned `spec.json` at generation time; `--check` compares the snapshot and never re-fetches (§9) |
| Architecture free to bake in MSTest | **Constrained.** MSTest is 21.7% of test-framework downloads (§18), so §3 requires the neutral layers to name no MSTest type, with the MSTest-specific surface enumerated. v1 still ships MSTest only |
| `ITestTokenProvider` had no way to advertise identities | **`Identities` property added.** A declared capability, not a probe: `Identities.Count` below two gates the wrong-scope 403 case off, read at run time by `ApiTestBase.RequireMultipleIdentities()` (§9, §13), not by `MemberCondition`. The shipped static provider returns one, so 403 tests gate off by construction |
| "only three commands write outside `Generated/`" | **False, and the wrong invariant.** `generate` writes `coverage-report.json`; `assertions add` edits `intest.json`. Restated as ownership: `generate` never writes `fixtures/` or a team-owned file |
| `--check` compared `Generated/` only | **Also compares `coverage-report.json`**, the one generated artefact tracking spec *shape* rather than templates |
| No exit code for tool failure | **`2` reserved.** Unparseable spec, missing `spec.source`, malformed `intest.json`, unhandled exception — so CI can tell a crash from fixture drift |
| .NET 10 LTS to 10 November 2028 | **14 November 2028** |
| "InTest does not run in the pipeline" alongside `--check` in CI | **Contradiction resolved** — generation never *writes* in the pipeline |
| Schema validation library | **Was unspecified.** NJsonSchema 11.6.1 |
| `Microsoft.NET.Test.Sdk` | **Was missing** from the dependency table |
| Assertion library additive in v2 | **Both ship in v1** — exercises the template seam instead of carrying it speculatively |

### Previously unverified, now resolved

| Claim | Verdict |
|---|---|
| `[ResourceLock]` lands in 4.4 | **Confirmed** — docs state planned for 4.4, preview-only until 4.4.0 |
| Bare `[assembly: Parallelize]` defaults to `ClassLevel` | **Confirmed** in the MSTEST0001 documentation |
| Stable `Microsoft.OpenApi` v3 exists | **Confirmed** — 3.10.0, released 2026-08-12, MIT, net8.0/netstandard2.0 |
| `TestContext.TestName` behaviour with `DataRow` | **Resolved** — see above |
| MSTEST0013 requires `[TestClass]` on the declaring class | Still unverified; `TestHost` carries it regardless |

### Still confirmed from rev 2

Swashbuckle omits `operationId` by default (deliberate since 4.0) · built-in package requires
`[EndpointName]` / `WithName` · NSwag auto-derives `{Controller}_{Action}` and ignores route
`Name` · `Microsoft.OpenApi.YamlReader` is a separate package · MSTEST0001 is not
inline-suppressible (reported at compilation level) · MSTest's runtime default is sequential ·
parallelization configurable via runsettings / testconfig.json / MSBuild properties · global
`TestTimeout` via runsettings · `MemberCondition` in 4.3 and 4.3.3 is stable · `TestContext` on
`AssemblyCleanup` from 3.8 · Shouldly 5 is preview · FluentAssertions v8 is commercial, 7.x
Apache-2.0 · .NET 8 and 9 EOL 10 November 2026 · `WithOpenApi` deprecated in .NET 10
(ASPDEPR002) · built-in package does not yet support YAML at build time.

### New findings

| Finding | Source |
|---|---|
| `[DependsOn]` also ships in MSTest 4.4 — a real test-ordering model | MSTest docs |
| MSTest v4 changed `TestCase.Id`, affecting AzDO failure-tracking | v3→v4 migration guide |
| MSTest v4 throws when `TestName` is read in `AssemblyInitialize`/`ClassInitialize` | v3→v4 migration guide |
| `TestContext.Properties` is now `IDictionary<string, object>` | v3→v4 migration guide |
| `dotnet test` on the .NET 10 SDK still defaults to VSTest mode | .NET CLI docs, *measured* |
| `JsonSchema.Net` is MIT under an Open Source Maintenance Fee (≥US$10k revenue) | Package licence |
| **NJsonSchema evaluates 27/27 OpenAPI 3.0 Schema Object keywords correctly**, `format: date-time` and `uuid` included | *Measured* |
| **NJsonSchema silently ignores 7 keywords** — `const`, `if`/`then`/`else`, `prefixItems`, `unevaluatedProperties`, `dependentSchemas`, `dependentRequired`, `contains`/`minContains`. All are 2019-09/2020-12, none legal in OpenAPI 3.0 | *Measured* |
| `JsonSchema.Net` rejects all 12 of the same bad instances | *Measured* |
| Corvus.Json is a build-time source generator — cannot validate a schema read at runtime | Package description |
| Newtonsoft.Json.Schema is commercially licensed above a free threshold; latest is `4.0.2-beta2` (prerelease) | nuget.org |
| Manatee.Json (the pre-`JsonSchema.Net` alternative) last published 2021-01-21 | nuget.org |
| **`HttpClient` throws on non-ASCII header values** — `Request headers must contain only ASCII characters` | *Measured* |
| **MSTest is 21.7% of test-framework downloads.** nuget.org totals on 2026-08-17: xunit 1,004,273,473 + xunit.v3 38,574,301 = 1,042,847,774 (47.4%) · NUnit 678,971,792 (30.9%) · MSTest.TestFramework 478,384,347 (**21.7%**). Downloads count restores rather than projects and inflate all three alike, so this is directional, not a project census — but it is the basis for §3's portability constraint and should not be asserted without it | *Measured* |
| **Microsoft.OpenApi 3.10.0 parses Swagger 2.0, OpenAPI 3.0, 3.1 and 3.2**, each detected correctly with zero diagnostics; `OpenApiSpecVersion` has exactly those four members | *Measured* |
| `new Uri(base, rel)` drops a base path segment in 3 of 4 forms | *Measured* |
| Factory-created `DelegatingHandler`s are not DI-scoped; `AsyncLocal` is required | *Measured* |
| Response schemas parse as `OpenApiSchemaReference`; must be bundled, not inlined | *Measured* |
| `SerializeAsV31` normalizes OpenAPI 3.0 `nullable: true` to `"type": ["null","string"]` | *Measured* |
| Shouldly produces a **garbled** message on expression-bodied test methods | *Measured* |
| MSTest v4 `Assert` messages are source-independent (`CallerArgumentExpression`) | *Measured* |
| MSBuild `Content` + `Link` + `CopyToOutputDirectory` survives `dotnet publish`; missing source is `MSB3030` | *Measured* |
| Swashbuckle 10.x requires Microsoft.OpenApi 2.3.0+ and still emits OpenAPI 3.0 by default | Swashbuckle v10 migration guide |
| Rev 2's provisional name `Jig` is taken on nuget.org (0.2.0–0.3.1) | nuget.org |
| `InTest`, `InTest.Cli`, `InTest.Runtime`, `InTest.Core` are all free on nuget.org; zero search hits | nuget.org, *measured* |
| GitHub org `intest` is **not** free — held by a personal account | GitHub API |
| **`TestContext.WriteLine`, `Console.Out`, and `Console.Error` written during a *passing* `[AssemblyInitialize]` reach no visible sink under VSTest** — not stdout, not stderr, not the `.trx`. Buffered into the `UnitTestResult` they would attach to; flushed only when a failure synthesises a result to carry them | *Measured*, VSTest via MSTest.TestAdapter 4.3.3, .NET 10 |
| **`TestContext.DisplayMessage` escapes that buffering**: `MessageLevel.Warning` reaches real stdout and the `.trx` without failing the run; `Informational` reaches the `.trx` only; `Error` fails the run | *Measured* |
| **`[AssemblyCleanup]` does not share `[AssemblyInitialize]`'s buffering** — its output reaches the `.trx` (attached to the last test result) and the console with `--logger "console;verbosity=detailed"` | *Measured* |

### Test-suite timing

**Earlier readings of ~1m30 total and a ~12.5s per-test floor for `GeneratedSuiteExecutionTests` are not reproducible and must not be used as a baseline.** Four repeated runs (n=11 each, `dotnet test --logger trx`, durations read from the `.trx`, not console wall-clock) on an idle machine:

| Run | Commit | Sum | min | median | max |
|---|---|---|---|---|---|
| 2 | `b833ab2` | 179.8s | 6.8s | 16.5s | 24.6s |
| 3 | `b833ab2` | 181.4s | 7.2s | 16.6s | 24.5s |
| 4 | `b833ab2` | 184.8s | 6.9s | 16.8s | 25.0s |
| 5 | `4814071` | 182.1s | 6.8s | 16.5s | 25.6s |

Range 179.8–184.8s, ±1.4%. Run 5 is at the earlier commit `4814071`; it sits mid-range, so no regression is evidenced between `4814071` and `b833ab2`. The distribution is stable and not flat, same shape in all four runs: nine tests cluster at 16.1–17.1s, one at ~6.9s, one at ~25s.

`GeneratedSuiteExecutionTests` is ~180s of Golden's ~206s of test time — 87% of the cost in 11 of 28 tests. Per-class breakdown at `b833ab2` (run 2): `GeneratedSuiteExecutionTests` n=11 sum 179.8s · `CompileVerificationTests` n=3 sum 13.9s · `ScaffoldCompileVerificationTests` n=1 sum 6.6s · `MSBuildEvaluationTests` n=2 sum 3.3s · `CliExitCodeTests` n=8 sum 2.5s · `GoldenFileTests` n=3 sum 0.1s. Golden's wall clock is 3m26–3m32 whether the assembly holds 20 tests or 28 — the `CliExitCodeTests` added by the parse-exit chip cost 2.5s in total.

Idle was established by measurement, not assumption: CPU-time deltas sampled across every `dotnet`/`MSBuild`/`VBCSCompiler`/`testhost` process over a 5-second window totalled 0.15 CPU-seconds (an earlier check: 0.02), on 22 logical processors.

*Measured on .NET SDK 10.0.400.*

A wall-clock total is not comparable to another without both a per-class breakdown **and repetition**. A per-class breakdown alone is not sufficient — one was available and a wrong conclusion was still drawn from a single run. Repetition is the part that would have caught it: four runs, not one, is what turns "±1.4%, no regression" from an assumption into a measurement.

---

## 19. Deliberately not built

- **Name-stability map in config.** A section growing one entry per operation, forever. Moving
  status out of identifiers (§5) captures most of the churn for zero config.
- **A `sharedSingleton` flag.** Speculative. `mutates` plus the documented cross-process limit
  covers what's real.
- **Schema abstraction in TestPlan for SOAP.** More abstraction on speculation about a v2 that
  may not happen.
- **`ISweepTarget` in v1.** Real burden on consumers. v1 documents the leakage risk instead.
- **A public template-set format.** No third-party authors exist. The planned `--emit-plan`
  (**not yet built** — see §3) is meant to cover the debugging need instead; until it ships, this
  argument rests on a command that does not exist yet, not on one that already covers the gap.
- **Per-test retries.** Hide real flakiness. Readiness gating addresses the actual cause.
- **A keyed lock map in `InTest.Runtime`.** `[ResourceLock]` ships in 4.4.
- **A InTest-owned parallelization control surface.** MSTest already exposes one.
- **Committing or embedding the spec.** MSBuild copies it to the output directory (§9).

---

## 20. Open items

1. **Reserve the NuGet IDs.** `InTest`, `InTest.Cli`, `InTest.Runtime` and `InTest.Core` were
   free on 2026-08-16 but are not reserved. Publish placeholder versions, or apply for the
   `InTest.` ID prefix, before announcing anything. **Blocking for release.**
2. **Where the maintainers' round-trip job lives** (§16 item 4), given private specs cannot
   enter a public repo.
3. **Governance load.** A public repo attracts issues, and MSTest-only plus `net10.0` (§2) will
   generate xUnit and downlevel-TFM requests immediately. Who triages, and what the answer is,
   should be decided before the repo is announced rather than in the first week of it.

### Not InTest's to decide — adopter concerns

Earlier drafts filed these as project open items. They are not: they vary per adopter, InTest
cannot resolve them centrally, and listing them as unresolved implied the tool was waiting on
answers it will never receive.

- **Fixture ownership.** The tier-4 backlog burns down only if someone owns it. InTest's
  contribution is making the gap visible — the spec-example percentage on every run (§10) and
  the coverage report (§12). *Recommended pattern, documented not enforced:* the team owning
  the API owns its fixtures. Who that is, is theirs to say.
- **Sweeper implementation.** Scheduled job, service, or database TTL — it depends entirely on
  what the fixtures seed. InTest's contribution is making leaked data identifiable and
  age-derivable from the run ID alone (§14). `ISweepTarget` remains deliberately unbuilt (§19).
- **Which environments exist and what they are called.** `staging`, `qa`, `uat` appear as
  examples only; profiles are free-form strings (§7).

### Closed

- **Licence — MIT.** Confirmed: `LICENSE` on `Dexom-GH/intest` is MIT, © 2026 Dexom-GH. It
  matches every pinned dependency (Microsoft.OpenApi, NJsonSchema, Shouldly, MSTest, Scriban,
  System.CommandLine), so InTest adds no licence surface of its own — which is the same test
  that excluded FluentAssertions and `JsonSchema.Net` (§4).
- **Hosting organisation — `Dexom-GH`.** `github.com/intest` is held by a personal account, so
  the repo lives at `github.com/Dexom-GH/intest`. Half of item 1 above; the NuGet IDs remain
  unreserved.
- **Tool ↔ runtime compatibility.** Answered by the semver contract in §3 rather than left as a
  question: majors move together, any CLI `N.y` with any runtime `N.x`, previous major
  supported 12 months.
- **Whether the spec survey gates v1 capabilities.** It does not. `intest survey` ships as a
  command adopters run against their own specs; it informs prioritisation and expectations,
  never feature inclusion (§17).

---

## Appendix — decisions and rationale

| Decision | Rationale |
|---|---|
| Own generator, not openapi-generator templates | Full output control; no JVM on agents; org-specific assertions |
| `net10.0`, no preview packages | .NET 8/9 EOL Nov 2026; preview churn is not worth the features |
| `Microsoft.OpenApi` 3.10.0, not 2.3.x | All 2.x stable versions are deprecated with a vulnerability advisory |
| Design for MSTest, xUnit and NUnit; ship MSTest | MSTest is 21.7% of test-framework downloads (§18), so baking it into the neutral layers would cap reach permanently. The assertion seam proved the pattern: build the boundary before the second implementation, and adding one costs nothing |
| Fixture sentinels keep failing, despite the adoption cost | A green suite asserting nothing is unrecoverable — nobody investigates a passing test. Aggregated messages, `intest survey`, and a fixture-free day-one subset make it navigable without weakening it |
| URL specs snapshotted, not fetched at build or check time | MSBuild cannot copy from a URL; a committed snapshot also gives a URL source the reviewable diff it otherwise lacks, and keeps `--check` hermetic |
| One HTTP pack in v1: HttpClient via `IHttpClientFactory` | `ApiTestBase.Client` cannot be typed for two packs from one package. Shipping one removes the constraint rather than working around it, and drops a template set plus two test dimensions |
| NJsonSchema despite 7 unevaluatable 2020-12 keywords | Measured 27/27 on the OpenAPI 3.0 vocabulary; all 7 gaps are illegal in 3.0. The only complete .NET alternative charges commercial users, and this project cannot require a paid licence. The keyword report makes the residual gap visible rather than silent |
| No shipped identity implementation | Auth is the team's. Shipping `DefaultAzureCredential` would add an undeclared dependency and an Azure assumption to a tool that must not have one |
| `TestId` ASCII-enforced and hash-disambiguated | `HttpClient` throws on non-ASCII headers, and the string variation catalog mandates emoji and RTL cases that would otherwise collide |
| NJsonSchema over JsonSchema.Net | Plain MIT with no licence surface; handles both OpenAPI dialects; instance-based, no global registry |
| Shouldly primary, MSTest `Assert` second | Shouldly reads source (better messages, fragile); MSTest uses `CallerArgumentExpression` (robust). Both ship, so the template seam is proven not speculative |
| No `<DebugType>full</DebugType>` | Measured unnecessary; source-file presence is the real variable |
| Generated tests block-bodied, assertion on its own line | Shouldly garbles messages on expression-bodied methods |
| Spec copied to output by MSBuild | Travels with `dotnet publish` into the gate stage; no committed second copy; no giant generated file |
| Schemas bundled with `definitions`, never inlined | Self-referential schemas; circular-reference resolution is the defect that deprecated Microsoft.OpenApi 2.x |
| `AssemblyInfo.cs` authoritative for parallelization | Team-owned, one place; MSBuild properties would duplicate the generated attribute and break the build |
| Sequential default | Matches MSTest's actual runtime default; shared deployed environment; rate limits are real |
| InTest does not set `Workers` | It is a throughput dial against someone else's quota; the team owns that call |
| Generated code committed | Spec changes reviewable as a PR diff |
| Generation never writes in the pipeline | Failures land on the PR where they're fixable |
| No fixture review flag | Bad data should fail; PR-time generation means the gate never sees it |
| `generate` never writes fixtures | Keeps `--check` coherent |
| `promote` emits, never writes | `spec.source` is a build artifact |
| Latency recorded, not asserted | Contradicts readiness gating otherwise |
| Status out of identifiers | Freezing the template doesn't freeze the rendered name |
| `TestId` from `TestDisplayName` | `TestName` is identical across every `DataRow` row |
| Weak-but-true default assertions | Guessed strict assertions get bulk-ignored |
| Base class in `InTest.Runtime` | Fixes ship without regeneration |
| Two packages, not three | `Cli`/`Core` split had no consumer |
| Fixture validation runs after seeding, not before | Seeding needs an `HttpClient` and a service that passed readiness; `{{fixture:…}}` cannot be checked until seeding has published the keys it resolves against. Cost: a dead API now surfaces only the readiness failure, since readiness throws before the fixture report is built |
| A fixture skipped by `AppliesTo` also skips its transitive dependents | Running a dependent against state a skipped dependency never built is the silent-wrong-state failure `AppliesTo` exists to prevent; a fixture that does not need that state should not declare the dependency. Kept out of `FixtureGraph`, which stays profile-agnostic, and left to the runner, which has both the order and the active profile |
| `IAssemblyFixture` registration: `AddSingleton` only | `AddScoped`/`AddTransient` plus `IDisposable` disposes the fixture when `AssemblyInit`'s DI scope ends, while its `OnCleanup` closure survives on `FixtureContext` until `AssemblyCleanup` — a disposed-object trap |
