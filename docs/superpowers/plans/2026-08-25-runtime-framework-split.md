# Runtime framework split — `InTest.Runtime` → neutral package + `InTest.Runtime.MSTest` adapter

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `InTest.Runtime` from handing every consumer `MSTest.TestFramework` whether they
asked for it or not. §3 of the design spec has always required the architecture to keep xUnit and
NUnit additive rather than a rewrite — "one namespace in v1, one package per framework when a
second ships" — but until this plan, that boundary existed only as an internal `Neutral/`/`MSTest/`
folder split inside a single assembly that still shipped `MSTest.TestFramework` as an unconditional
dependency. A team building an xUnit adapter today would have received MSTest transitively for
nothing. This plan makes the boundary a package boundary: `InTest.Runtime` becomes genuinely
neutral (no test-framework `PackageReference` at all), and a new `InTest.Runtime.MSTest` package
carries the MSTest-specific surface. Three packages ship where two did.

**Architecture:** The split is two moves layered on top of each other, and keeping them distinct
matters for reviewing the diff:

1. **Extract, same assembly.** Pull the MSTest-coupled logic in `TestHost` and `ApiTestBase` apart
   from the logic that does not actually need MSTest, without moving any files yet. This produces
   `InTestRun` (neutral composition root, replacing what used to live directly on `TestHost`) and
   `ApiTestCore` (neutral base class, replacing what used to live directly on `ApiTestBase`), each
   in `src/InTest.Runtime/`, with `TestHost` and `ApiTestBase` becoming thin facades that still
   compile a generated project's scaffolded `TestStartup.cs` unchanged. Four seams make this
   extraction possible without naming a test framework in the neutral half: `IRunDiagnostics`
   (`[intent-not-mechanism]`), a plain `string?` for the run-settings profile, a plain `string?`
   for the resolved test display name, and a plain `string?` skip reason (`[skip-is-a-reason]`).
2. **Split, into two projects.** Once the extraction leaves nothing MSTest-shaped in
   `src/InTest.Runtime/` outside `TestHost.cs` and `ApiTestBase.cs`, move those two files into a
   new project, `src/InTest.Runtime.MSTest/`, which `ProjectReference`s `InTest.Runtime` (the SDK
   turns that into an ordinary nuspec `<dependency>` on pack) and adds
   `PackageReference Include="MSTest.TestFramework"`. Both projects declare
   `namespace InTest.Runtime` (`[shared-namespace]`), so the split is invisible to a generated
   project's own source — only the scaffolded `.csproj`'s `PackageReference` id changes.

Two guard layers make the neutral half's neutrality mechanical rather than a promise:
`InTest.Architecture.Tests`' `NeutralityTests` (a source-level csproj/reference check, so a stray
`PackageReference` to a test framework fails the build immediately) and
`scripts/ci/pack-and-verify.ps1` (a packed-nuspec check, so a leak that only shows up after
packing — a transitive dependency the csproj check cannot see — still fails CI). Neither alone is
enough: the csproj check cannot see what actually gets packed, and the nuspec check only runs in
the `dogfood`/release pipeline, not on every `dotnet build`.

**Tech Stack:** .NET 10 / C#, MSBuild `ProjectReference`→nuspec `<dependency>` translation (no new
tooling), MSTest 4.3.3 (unchanged), central package management (`Directory.Packages.props`).

**Prerequisite:** a green `dotnet test InTest.sln` on this branch before starting. Measure the
baseline yourself — do not trust a number recorded here, since `main` moves under contributors on
this repository.

---

## Decisions

Named with slugs per `CONTRIBUTING.md`'s "Writing plans" — insertion and reordering cannot break a
slug, and unlike a task number, a slug survives being quoted from a completely different file
years later. Two of the four below are quoted verbatim from source comments already shipped in
this branch (`UpgradeCommand.cs`, `ApiTestBase.cs`, `TestHost.cs`, `IRunDiagnostics.cs`) — this
document is the canonical explanation those comments point at, so its slugs match theirs exactly
rather than being renamed for this write-up's convenience.

### `[runtime-adapter-split]` — a package boundary, not a folder convention

The pre-existing `Neutral/`/`MSTest/` split inside one `InTest.Runtime` assembly recorded intent
but enforced nothing a consumer could observe: restoring `InTest.Runtime` pulled in
`MSTest.TestFramework` regardless of which half of the folder tree a given file lived under.
`NeutralityTests` already stopped a *neutral-folder* file from naming an MSTest type, but nothing
stopped the **project** from depending on MSTest at all — the guard checked source, not the
shipped package.

Splitting into two projects makes the folder convention load-bearing: `InTest.Runtime.csproj` has
no `PackageReference` to `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, or
`Microsoft.NET.Test.Sdk` at all, so there is no implicit `global using
Microsoft.VisualStudio.TestTools.UnitTesting` for a file under it to accidentally rely on, and no
transitive MSTest dependency for `pack-and-verify.ps1`'s nuspec check to catch as a regression. A
consumer who references `InTest.Runtime` directly — to build their own xUnit or NUnit adapter, say
— receives *only* the packages `InTest.Runtime.csproj` itself declares: `NJsonSchema` and four
`Microsoft.Extensions.*` packages. Nothing test-framework-shaped rides along.

**Rejected: ship one package, with `MSTest.TestFramework` as an optional/deferred dependency
somehow.** NuGet has no "optional PackageReference" mechanism that stops a dependency from being
installed on restore — every `PackageReference` in a `.csproj` restores unconditionally.
Conditioning the reference on an MSBuild property would require every consumer to opt out
explicitly (opt-in-by-default is exactly the problem this plan closes) and would not change what
the packed `.nuspec` declares, which is the artifact `pack-and-verify.ps1` and a real consumer's
restore both actually see. Two packages is the only mechanism NuGet offers for "install this only
if you asked for it."

**Rejected: name the adapter package `InTest.Runtime.Adapters.MSTest` or similar, anticipating a
shared `Adapters` namespace root for future frameworks.** No second adapter exists yet, and a
namespace segment that exists only to leave room for siblings that may never ship is exactly the
speculative abstraction this codebase's conventions warn against (CLAUDE.md: "Nothing
framework-specific is abstracted speculatively"). `InTest.Runtime.MSTest` names what the package
*is* today; a future `InTest.Runtime.xUnit` needs no shared parent to exist beside it.

### `[shared-namespace]` — both packages declare `namespace InTest.Runtime`

An adopter migrating from the pre-split all-in-one package to the split shape changes exactly one
line: the `PackageReference` id, from `InTest.Runtime` to `InTest.Runtime.MSTest`. No `using`
changes, no type is renamed, no generated source changes, because `InTest.Runtime.MSTest`'s
`TestHost` and `ApiTestBase` live in the same `namespace InTest.Runtime` as everything in the
neutral package. `intest upgrade` detects a scaffolded `.csproj` still pinning the bare
`InTest.Runtime` id — the pre-split shape, which still builds today only because the neutral
package alone no longer carries `TestHost`/`ApiTestBase`, so it would fail to compile the moment
any generated test class needed them — and reports the one-line fix
(`UpgradeCommand.DetectLegacyRuntimeReferenceMigration`), never rewriting the file itself, the same
detect-and-report posture `[prerelease-reference-migration]`
(`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`) already established for a
different `.csproj` drift.

**Rejected: separate namespaces (`InTest.Runtime` neutral, `InTest.Runtime.MSTest` adapter),
matching each package's own name.** This is the more "conventional" .NET shape — a package's
default namespace usually matches its id — and was rejected precisely because it is
conventional at the cost of a real migration: every generated project's `using InTest.Runtime;`
would need a second `using InTest.Runtime.MSTest;`, and `TestStartup.cs`'s `TestHost`/`ApiTestBase`
references would need qualifying or a new `using`. That is a source-level migration for every
existing adopter, not a `PackageReference` edit — disproportionate to what actually changed,
which is packaging, not the API surface.

### `[intent-not-mechanism]` — `IRunDiagnostics.Note`/`Warn`, not a level enum or a `TextWriter`

The neutral layer (`FixtureRunner` today) needs to report progress and warnings without knowing
how any given framework surfaces messages. `IRunDiagnostics` (`src/InTest.Runtime/IRunDiagnostics.cs`)
has exactly two members — `Note(string)` for routine, droppable progress and `Warn(string)` for
something that must reach the operator even on a passing, exit-0 run — because those are the two
things a *caller* means, independent of mechanism. `InTest.Runtime.MSTest`'s `TestHost` implements
it with a nested `TestContextDiagnostics`: `Note` forwards to `TestContext.WriteLine`, `Warn` to
`TestContext.DisplayMessage(MessageLevel.Warning, …)` — the one mapping confirmed by direct probe
to survive a *passing* `[AssemblyInitialize]` under this project's actual runner (VSTest via
MSTest.TestAdapter, not Microsoft.Testing.Platform). This replaces the pre-split
`ContextTextWriter`, which was handed out typed as `TextWriter` even though only `WriteLine` was
ever overridden — every other `TextWriter` member silently no-opped, a trap for a future caller
that `IRunDiagnostics` removes by construction: there is no member left to accidentally leave
unimplemented.

Three shapes were considered and rejected — recorded at length in `IRunDiagnostics`'s own doc
comment, summarized here:

- **`Action<SomeLevelEnum, string>`** — this is MSTest's own `DisplayMessage(MessageLevel, string)`
  with the parameters renamed, not an abstraction over it. It leaks MSTest's own level taxonomy
  (more levels than the neutral layer needs) into a layer that must not know MSTest exists.
- **Two separate `Action<string>` delegates** (`onNote`, `onWarn`) — carries the same two pieces of
  information, but an `Action<string>` parameter has no name once assigned to a local, so a caller
  wiring `RunAsync(..., warn, note, ...)` instead of `RunAsync(..., note, warn, ...)` transposes
  them and the compiler cannot catch it. A two-method interface cannot be transposed the same way.
- **`TextWriter`** — structurally cannot express two severity levels; every `TextWriter` is one
  undifferentiated stream. Grafting a level onto it (a `WriteLine` convention, a prefix marker)
  invents a private protocol instead of stating the two levels directly — and is the shape being
  replaced (`ContextTextWriter`, above).

### `[skip-is-a-reason]` — the neutral layer returns `string?`; the adapter turns it into its own skip mechanism

`ApiTestCore.MultipleIdentitiesSkipReason()` and `ApiTestCore.SecondaryIdentityScopeSkipReason(...)`
return `null` to mean "run the test" and a non-null `string` to mean "skip it, and here is why."
`ApiTestBase.RequireMultipleIdentities()` / `RequireSecondaryIdentityLacks(...)` are the entire
MSTest-specific contribution: `if (reason is { } r) Assert.Inconclusive(r);`. Nothing about *why*
a case skips, or the logic that decides it, depends on MSTest — only the two-line translation from
"here is a reason" to "here is how MSTest expresses skipping" does.

This is deliberately the plainest shape that could carry the decision, chosen the same way
`IRunDiagnostics` was: a reason is data a caller can act on however its own framework prefers.
xUnit's `Assert.Skip(reason)` and NUnit's `Assert.Ignore(reason)` both accept the identical
`string` this method already returns — a future adapter's equivalent method is a comparably short
translation, not a new decision.

**Rejected: an enum or bespoke `SkipResult` type carrying a reason plus a category.** No caller
today distinguishes *kinds* of skip — "no second identity registered" and "second identity already
holds the scope" both mean the same thing to every consumer that has been built: skip, and show
the operator why. A type that distinguishes categories nothing consumes yet is exactly the
speculative addition this codebase's conventions rule out; `string?` is upgradeable to a richer
type the day something other than "display it" needs to happen with the reason.

**Rejected: throw a dedicated `SkipException` and let the adapter catch it.** Throwing to signal
routine, expected control flow (a large fraction of auth cases are *designed* to skip when only one
identity is configured) makes every call site that might skip need a try/catch for what is not
actually exceptional — and an uncaught `SkipException` reads, to a future maintainer, as a bug
report rather than a designed outcome. A nullable return makes "no reason to skip" the ordinary,
zero-ceremony path.

---

## What does not change

- **The compatibility contract.** §3's semver rule — majors move together, any CLI `N.y` accepts
  any runtime `N.x` — now covers `InTest.Runtime.MSTest` as well as `InTest.Runtime`; nothing about
  the rule itself changes, only the count of packages it applies to.
- **`ApiTestBase`'s public surface.** `RequireFixture`, `FixtureBody`, `Client`, `TestId`,
  `Schemas`, `UseIdentity`, `RequireMultipleIdentities`, `RequireSecondaryIdentityLacks` — every
  member a generated test class or a team's own base class calls — keeps its exact signature. A
  generated project's source does not need to change at all; only its `.csproj` does
  (`[shared-namespace]`).
- **`TestStartup.cs`'s calls.** `TestHost.InitializeAsync(context)`, `TestHost.CleanupAsync(context)`,
  and assigning `TestHost.ConfigureServices` all keep compiling unchanged — `TestHost` is a facade
  precisely so this stays true.
- **`project.framework`'s frozen-per-project status.** Still frozen (§5) — this plan does not make
  a suite migratable between frameworks in place. It does make the value **read and validated**
  (`ConfigLoader.RequireSupportedFramework`) rather than accepted-and-ignored, which is new, but
  orthogonal to frozen-ness.
- **`TemplateRenderer`.** Still hardcodes `mstest-class.scriban` regardless of `project.framework`'s
  value. The config now carries the value correctly; nothing yet branches on it.
- **`examples/`.** Both example projects keep referencing the neutral `InTest.Runtime` package
  directly, pinned to the published `0.1.0-preview.1`, because `InTest.Runtime.MSTest` does not
  exist on nuget.org at that tag. Migrating them is blocked on a future publish, not on anything in
  this plan.

---

## Task 1: Extract the neutral composition root — `InTestRun`

**Files:** `src/InTest.Runtime/InTestRun.cs` (new), `src/InTest.Runtime/TestHost.cs` (becomes a
facade, still in this project at this point in the plan).

- [ ] **Step 1:** Move `TestHost`'s state (`Configuration`, `Root`, `Schemas`, `RunIdValue`,
      `Profile`, `Fixtures`, `FixtureValidationReport`, `FixtureTokens`, `ConfigureServices`) and
      its `InitializeAsync`/`CleanupAsync` bodies onto a new static class, `InTestRun`, named to
      match the existing neutral static/ambient family (`InTestAmbient`, `InTestId`,
      `InTestClients`, `InTestUrl`, `InTestIdentities`) rather than after "TestHost" — that name is
      reserved for the MSTest-shaped facade.
- [ ] **Step 2:** `InTestRun.InitializeAsync` takes `string? profileFromRunSettings` and
      `IRunDiagnostics diagnostics` in place of `TestContext` — see Task 2 for `IRunDiagnostics`
      itself, built alongside this step since `InitializeAsync`'s body needs it immediately
      (`diagnostics.Note(...)` for the run-id line).
- [ ] **Step 3:** `TestHost` becomes a facade: every public member forwards to `InTestRun`'s
      equivalent. `TestHost.InitializeAsync(TestContext, CancellationToken)` adapts `TestContext`
      to the two neutral parameters and calls `InTestRun.InitializeAsync`.
- [ ] **Step 4:** Internal members (`TokenProvider`, `RetainedFixtureContext`) move to `InTestRun`
      and are **not** forwarded on `TestHost` — `InTest.Runtime.Tests` reaches them directly on
      `InTestRun` via the existing `InternalsVisibleTo`, and no forwarder is needed while both
      classes still live in the same assembly. Comment this as deliberate preparation for Task 6,
      where the two classes stop sharing an assembly and an internal forwarder would need a new
      `InternalsVisibleTo` grant between the two shipped packages — exactly what the split exists
      to avoid.
- [ ] **Step 5:** `dotnet test tests/InTest.Runtime.Tests` green, no behavior change from a caller's
      perspective — this step is a pure extraction.

## Task 2: `IRunDiagnostics` — the diagnostics seam

**Files:** `src/InTest.Runtime/IRunDiagnostics.cs` (new), `src/InTest.Runtime/FixtureRunner.cs`
(edit), `src/InTest.Runtime/TestHost.cs` (edit — the `TestContextDiagnostics` nested
implementation), `tests/InTest.Runtime.Tests/*` (new/edited coverage).

- [ ] **Step 1:** Write `IRunDiagnostics` per `[intent-not-mechanism]` above — `Note(string)`,
      `Warn(string)`, doc comment recording the three rejected shapes.
- [ ] **Step 2:** `FixtureRunner.RunAsync` takes `IRunDiagnostics diagnostics` in place of its prior
      `TextWriter log` parameter. Every call site that logged fixture skips/warnings switches to
      `diagnostics.Note`/`diagnostics.Warn` by what it actually means, not by what it used to write.
- [ ] **Step 3:** `TestHost` gains a nested `internal sealed class TestContextDiagnostics(TestContext
      context) : IRunDiagnostics` — `Note` → `context.WriteLine`, `Warn` →
      `context.DisplayMessage(MessageLevel.Warning, …)`. Confirm by direct probe (not by reading
      MSTest's docs) that `Warn` survives a *passing* `[AssemblyInitialize]` under this project's
      actual runner and `Note` does not need to, matching `FixtureRunner`'s own severity contract.
- [ ] **Step 4:** Tests: `FixtureRunner` tests inject a test-double `IRunDiagnostics` and assert on
      calls recorded, replacing whatever assertions previously read a `TextWriter`'s buffered
      output. `TestContextDiagnostics` gets its own direct test via `InTest.Runtime.Tests`'
      `InternalsVisibleTo`.

## Task 3: Extract the neutral base class — `ApiTestCore`

**Files:** `src/InTest.Runtime/ApiTestCore.cs` (new), `src/InTest.Runtime/ApiTestBase.cs` (becomes
a facade, still in this project at this point in the plan).

- [ ] **Step 1:** Move `ApiTestBase`'s scope-containment logic — the DI scope, `TestId`, the
      ambient identity, `Client`, `RequireFixture`, `FixtureBody`, `UseIdentity`,
      `MultipleIdentitiesSkipReason`, `SecondaryIdentityScopeSkipReason` — onto a new abstract
      class, `ApiTestCore`. Deliberately not `InTestApiTestCore` or similar: an `InTest`-prefixed
      name in this codebase marks a static ambient/utility type (see the family Task 1 lists);
      `ApiTestCore` is an instantiable base class, so borrowing the prefix would blur a naming
      signal that currently carries real information.
- [ ] **Step 2:** `BeginTest` takes a plain `string? testDisplayName` parameter instead of reading
      `TestContext.TestDisplayName` itself — the seam that lets §3's design-spec row (rewritten in
      Task 7 below) retire the `ITestIdentity` interface it used to prescribe. Document the
      behavior change this introduces: reading `TestId` outside a running test now throws
      `InvalidOperationException` with a message naming the actual rule broken, rather than the
      prior bare `NullReferenceException` from reading through to MSTest's own
      `TestContext.TestDisplayName` getter.
- [ ] **Step 3:** `ApiTestBase` becomes a thin adapter: `[TestInitialize] ApiTestInitialize()` calls
      `BeginTest(TestContext.TestDisplayName)` — never `TestContext.TestName`, which collapses
      every `[DataRow]` variation of one operation onto the same bare method name and would give
      them all one `TestId`. `[TestCleanup] ApiTestCleanup()` calls `EndTest()`.
      `RequireMultipleIdentities`/`RequireSecondaryIdentityLacks` become the two-line
      `MultipleIdentitiesSkipReason`/`SecondaryIdentityScopeSkipReason` → `Assert.Inconclusive`
      translations `[skip-is-a-reason]` describes.
- [ ] **Step 4:** `dotnet test tests/InTest.Runtime.Tests` green; no behavior change for a generated
      project except the one documented in Step 2.

## Task 4: The run-settings profile seam

**Files:** `src/InTest.Runtime/InTestRun.cs` (`ResolveProfile`), `src/InTest.Runtime/TestHost.cs`
(`ProfileFromRunSettings`).

- [ ] **Step 1:** `InTestRun.InitializeAsync` takes `string? profileFromRunSettings` — a plain
      string, deliberately not an `IRunSettings` interface. An interface would need a second
      implementation with genuinely different behavior to earn its keep, and none exists: every
      caller of `InTestRun.InitializeAsync` has exactly one fact to contribute — the resolved
      profile string, however its own framework represents "no value."
- [ ] **Step 2:** `TestHost.ProfileFromRunSettings(TestContext)` maps MSTest's run-settings
      `"profile"` property to that `string?`, with the one behavior-preservation trap called out in
      its own doc comment: an *empty* string must map to `null`, not pass through, because
      `InTestRun.ResolveProfile`'s precedence chain treats any non-null value as "run-settings
      wins" with no further check — an unmapped empty string would silently become the pinned
      profile instead of falling through to `INTEST_PROFILE` the way an absent property already
      does. This is a fact about MSTest's runsettings XML representation of "no value," not
      something the neutral precedence chain has any business knowing, so it lives in the adapter.
- [ ] **Step 3:** `internal`, not `private`, so `InTest.Runtime.Tests` can exercise the mapping
      directly rather than only through the full weight of `InitializeAsync`.

## Task 5: `project.framework` — read and validated

**Files:** `src/InTest.Cli/Configuration/ConfigLoader.cs`,
`tests/InTest.Cli.Tests/ConfigLoaderTests.cs`.

- [ ] **Step 1:** `project.framework` becomes a *required* field, read with the same
      `RequireString` helper `rootNamespace`/`testBaseClass` already use — not the optional path
      `intestVersion` uses, and not defaulted to `"mstest"` when absent. Defaulting would be
      exactly the plausible-default `CLAUDE.md`'s "Fail loudly" rule forbids: correct only until a
      second framework ships, at which point every adopter who never wrote the key silently
      depends on a default they never chose.
- [ ] **Step 2:** `RequireSupportedFramework` refuses anything other than the exact lowercase
      `"mstest"`, naming §3's "designed for three, ships one" in the refusal rather than a bare
      "unsupported value" message.
- [ ] **Step 3:** Confirm no shipped config breaks: `InitCommand` has always written
      `"framework": "mstest"` into every scaffold, and both `examples/*/intest.json` already
      declare it — making the key required breaks no config this repository ships.
- [ ] **Step 4:** Tests: a config with no `project.framework` refuses; one with an unsupported
      value (including a differently-cased `"MSTest"`) refuses by name; `"mstest"` loads and is
      carried on `LoadedConfig`.

## Task 6: Physical package split — `InTest.Runtime.MSTest`

**Files:** `src/InTest.Runtime.MSTest/InTest.Runtime.MSTest.csproj` (new),
`src/InTest.Runtime.MSTest/TestHost.cs` (moved from `src/InTest.Runtime/`),
`src/InTest.Runtime.MSTest/ApiTestBase.cs` (moved), `src/InTest.Runtime/InTest.Runtime.csproj`
(edit — remove `MSTest.TestFramework`), `src/InTest.Cli/Commands/InitCommand.cs` (scaffold now
emits `InTest.Runtime.MSTest`), `InTest.sln`, `tests/InTest.Runtime.Tests/InTest.Runtime.Tests.csproj`
(add the second `ProjectReference`).

- [ ] **Step 1:** Create `src/InTest.Runtime.MSTest/InTest.Runtime.MSTest.csproj`:
      `<RootNamespace>InTest.Runtime</RootNamespace>` (belt-and-suspenders — both files already
      declare `namespace InTest.Runtime;` explicitly, so compilation does not depend on this
      setting; it exists so the SDK default namespace is never left as a trap for the next file
      added without an explicit declaration), `ProjectReference` to `../InTest.Runtime/InTest.Runtime.csproj`,
      `PackageReference` to `MSTest.TestFramework` (version from `Directory.Packages.props`, plus
      `MinVer` `PrivateAssets="all"` — not optional here, since a missing `MinVer` reference packs
      silently at the SDK's default `1.0.0` with nothing failing the build to say so).
- [ ] **Step 2:** Move `TestHost.cs` and `ApiTestBase.cs` into the new project, unedited beyond
      their `namespace` line (already `InTest.Runtime`, so no change there either). Their own doc
      comments (written in Tasks 1 and 3 above) already describe this move as "Task 6"; leave that
      wording — it now points at this task.
- [ ] **Step 3:** `InTest.Runtime.csproj` drops `MSTest.TestFramework` entirely. Confirm the project
      still builds — nothing under `src/InTest.Runtime/` should reference `TestHost`/`ApiTestBase`
      any more; if something does, that reference belongs in the adapter, not the neutral layer.
- [ ] **Step 4:** `InitCommand.cs`'s scaffold string changes its one `PackageReference` from
      `InTest.Runtime` to `InTest.Runtime.MSTest`, still interpolating `CliVersion.Current`
      (`[scaffold-reads-itself]`, unaffected by this plan) — the SDK turns the new project's
      `ProjectReference` into an ordinary nuspec `<dependency id="InTest.Runtime">` automatically,
      so nothing downstream (the template, `testBaseClass`) needs to know two packages are
      involved.
- [ ] **Step 5:** `tests/InTest.Runtime.Tests` adds a second explicit `ProjectReference` to
      `InTest.Runtime.MSTest` alongside its existing one to `InTest.Runtime` — explicit rather than
      relying on the transitive reference alone, so it is clear this suite tests `InTest.Runtime`'s
      own types directly and not only what the adapter happens to expose.
- [ ] **Step 6:** `dotnet build InTest.sln` clean; `dotnet test InTest.sln` — all four suites green.

## Task 7: Guards — `NeutralityTests` and `pack-and-verify.ps1`

**Files:** `tests/InTest.Architecture.Tests/NeutralityTests.cs`, `scripts/ci/pack-and-verify.ps1`.

- [ ] **Step 1:** `NeutralityTests` gains a csproj-level assertion that `InTest.Runtime.csproj`
      declares no `PackageReference` to `MSTest.TestFramework`, `MSTest.TestAdapter`,
      `MSTest.Analyzers`, or `Microsoft.NET.Test.Sdk` — source-level, so it fails on the next
      `dotnet build` a stray reference is added, before anything is ever packed.
- [ ] **Step 2:** A second `NeutralityTests` case (or a sibling test) asserts
      `InTest.Runtime.MSTest.csproj` *does* declare `MSTest.TestFramework` as a `PackageReference`
      — a positive control, so the neutral-package assertion above cannot pass vacuously because
      the whole adapter project silently stopped existing.
- [ ] **Step 3:** `pack-and-verify.ps1` packs all three projects, selects each `.nupkg` by its
      nuspec `<id>` (not a filename glob — `InTest.Runtime.*.nupkg` now also matches
      `InTest.Runtime.MSTest.*.nupkg`), and asserts: `InTest.Runtime`'s nuspec declares no
      dependency matching an MSTest/xUnit/NUnit/`Microsoft.NET.Test.Sdk` pattern; the three
      packages all pack at the same MinVer-derived version; `InTest.Runtime.MSTest`'s nuspec
      declares a dependency on `InTest.Runtime` whose version lower bound equals the neutral
      package's own packed version exactly, and *also* declares `MSTest.TestFramework` — the
      positive control that keeps the `InTest.Runtime` check from passing vacuously because
      dependency parsing found nothing at all.
- [ ] **Step 4:** Run the script locally and confirm each assertion actually fails when the
      condition it guards is deliberately broken (drop the `MSTest.TestFramework` reference from
      the adapter project; add a fake dependency to the neutral one) — a guard that has not been
      seen to fail is decoration, the same standard `[shallow-clone-is-a-defect]`
      (`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`) held its own guard to.

## Task 8: `UpgradeCommand` — detect the pre-split package id

**Files:** `src/InTest.Cli/Commands/UpgradeCommand.cs`, `tests/InTest.Cli.Tests/UpgradeCommandTests.cs`.

- [ ] **Step 1:** Extend the existing `[prerelease-reference-migration]` detector
      (`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`) with a second, narrower
      check: when the adapter-shaped `PackageReference` pattern (`InTest.Runtime.MSTest`) finds no
      match, look for the legacy bare `InTest.Runtime` reference shape instead. A match there means
      a project scaffolded before this split still pins the old id.
- [ ] **Step 2:** Report a `NOTE:` naming the file, the pinned version, and the exact fix — change
      the `PackageReference` id to `InTest.Runtime.MSTest`, and say explicitly that no source
      change is needed (`[shared-namespace]`) so an adopter does not go looking for a migration
      that does not exist. Never rewrite the file — same detect-and-report posture as the sibling
      check, for the same reason: a matcher good enough on the scaffold `init` writes can misfire
      on a real, reformatted adopter project, and a misfiring XML edit inside someone's build is
      worse than a message they have to act on by hand.
- [ ] **Step 3:** Zero or more than one match on either pattern is silence, not a crash or a guess
      — consistent with the sibling detector's own stated reasoning.
- [ ] **Step 4:** Tests: a scaffold-shaped `.csproj` pinning the legacy `InTest.Runtime` id produces
      the note naming both the file and the fix; a `.csproj` already on `InTest.Runtime.MSTest`
      produces nothing from this check; a reformatted `.csproj` matching neither pattern produces
      silence rather than a false positive.

## Task 9: Golden suite speed — `ProcessRunner`'s node-reuse deadlock

**Files:** `tests/InTest.Golden.Tests/ProcessRunner.cs`.

Found during this plan's own verification pass, not part of the neutral/adapter split itself, but
landed alongside it because it blocked iterating on Golden at all: every shelled-out `dotnet
build`/`dotnet test` in this assembly was taking the full MSBuild node-reuse idle timeout (15
minutes) to return, even though the child process itself exited in seconds.

- [ ] **Step 1:** Diagnose: MSBuild spawns persistent worker nodes with `/nodeReuse:true` by
      default; those nodes inherit the redirected stdout/stderr handles `ProcessStartInfo` creates
      for the parent `dotnet` process and outlive it. The write end of the pipe stays open after
      the child exits, so `ReadToEndAsync` never sees EOF and the calling method blocks until the
      orphaned nodes eventually time out on their own. Confirm by observation, not inference:
      during a stall, the only child of the test host process is a bare `conhost.exe` — the
      `dotnet` child has already exited.
- [ ] **Step 2:** Fix: set `MSBUILDDISABLENODEREUSE=1` on the child process's environment, scoped
      per-invocation inside `ProcessRunner.RunAsync` rather than for the whole test assembly or CI
      workflow — a fact about how *these specific* redirected child builds must run, kept where it
      cannot be silently lost by an unrelated edit to a `.runsettings` file or workflow YAML.
- [ ] **Step 3:** While fixing this, also drain stdout and stderr **concurrently**
      (`Task.WhenAll(stdoutTask, stderrTask)`) rather than sequentially. Awaiting stdout first is a
      second, independent deadlock: it does not return until the child closes stdout (normally at
      exit), so a child that fills stderr's OS pipe buffer first (~4 KB on Windows) blocks forever
      on its own stderr write and never exits. `WaitForExitAsync` is awaited last, after both reads
      complete — on .NET it additionally waits for the redirected streams to reach EOF, so awaiting
      it before the reads reintroduces the same hazard from the other direction.
- [ ] **Step 4:** Measure before/after. This fix alone took the Golden suite from roughly 5m17s to
      about 125s.

## Task 10: Documentation

**Files:** `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`, `CLAUDE.md`,
`CONTRIBUTING.md`, `docs/getting-started.md`, `README.md`, `src/InTest.Runtime/README.md`,
`src/InTest.Runtime.MSTest/README.md`, `THIRD-PARTY-NOTICES.md`, `CHANGELOG.md`, this plan.

Per `CLAUDE.md`'s working conventions, the spec is updated in the same change as the behavior, and
`docs/getting-started.md` whenever the adoption path changes — both apply here, since a generated
project's `PackageReference` id is exactly an adoption-path change.

- [ ] **Step 1:** §3 of the design spec — rewrite "Framework portability" to say the boundary is
      now real (§3's own "one package per framework" line, previously aspirational, is now true),
      name the actual types (`InTestRun`, `ApiTestCore`, `IRunDiagnostics` neutral;
      `TestHost`, `ApiTestBase`, `TestContextDiagnostics` MSTest-specific), and rewrite the
      `TestId`/`ITestIdentity` row to record what was built (a plain `string?` at `BeginTest`) with
      `ITestIdentity` kept as the recorded rejected alternative — never silently deleted.
- [ ] **Step 2:** §5's `project.framework` row — note it is now read and required, not merely
      accepted.
- [ ] **Step 3:** §17's v2 backlog — the `TestId` coupling this plan closes is no longer "the
      sharpest coupling to break"; restate what genuinely remains (a second template set,
      `project.framework` actually selecting one, a second adapter package).
- [ ] **Step 4:** The appendix decision table's MSTest/xUnit/NUnit row — note the boundary is now
      built, not merely designed.
- [ ] **Step 5:** `CLAUDE.md` — the "Runtime" section's `Neutral/`/`MSTest/` folder description
      becomes two projects; "two shipped packages" becomes three.
- [ ] **Step 6:** `CONTRIBUTING.md` — anywhere counting packages, the publishing checklist, the
      package-version coupling description if it names files that moved.
- [ ] **Step 7:** `docs/getting-started.md` — the scaffold-file table and anywhere naming the
      `InTest.Runtime` `PackageReference` directly.
- [ ] **Step 8:** `README.md` and both package `README.md`s — the neutral package's README points
      readers at the adapter package as what a project actually references.
- [ ] **Step 9:** `THIRD-PARTY-NOTICES.md` — split the old `InTest.Runtime` dependency section in
      two, moving `MSTest.TestFramework` into a new `InTest.Runtime.MSTest` section.
- [ ] **Step 10:** `CHANGELOG.md` — a `Breaking` entry under `Unreleased`, one line for the
      migration: change the `PackageReference` id, no source change needed.
- [ ] **Step 11:** This plan document itself.

---

## Verification

- [ ] `dotnet build InTest.sln` — clean, no new warnings (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test InTest.sln` — all four suites green. Measured on this branch after all tasks
      above: Architecture **12**, Cli **491**, Runtime **213**, Golden **35**.
- [ ] `pwsh scripts/ci/pack-and-verify.ps1` (Task 7's guard) — confirms the packed shape, not just
      the source-level one.
- [ ] `git status` — confirm `examples/` is untouched; those two projects intentionally still pin
      the neutral `InTest.Runtime` package at the published `0.1.0-preview.1`, since
      `InTest.Runtime.MSTest` does not exist on nuget.org yet.
