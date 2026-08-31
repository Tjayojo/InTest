# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

InTest generates a committed, owned MSTest or xUnit project that exercises a **deployed** API over
real HTTP, from its OpenAPI document. Four shipped packages (`InTest.Cli`, `InTest.Runtime`,
`InTest.Runtime.MSTest`, `InTest.Runtime.xUnit`), four sample APIs used as fixtures, five test
suites — the fifth, `InTest.Runtime.XUnit.Tests`, exists because the two adapters declare the same
types in the same namespace and cannot share a compilation with `InTest.Runtime.Tests`.
`InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` are published to nuget.org (prerelease, via
`release.yml`'s trusted-publishing push) — build from source for anything past that tag. Neither
`InTest.Runtime.MSTest` nor `InTest.Runtime.xUnit` exists on nuget.org at that tag; `examples/`
still pins `InTest.Runtime` there for that reason.

`init`, `generate`, `fixtures repair`, `generate --check` and `upgrade` work end to end.
A URL `spec.source` also works: `generate` fetches it and writes a committed `spec.json`
snapshot (§9), which `generate --check` and `fixtures repair` then read instead of the network.
`survey`, `fixtures promote`, `assertions add`, `generate --emit-plan`, variation tests and YAML
input do **not** exist yet — do not assume they do. YAML is unbuilt from a file *and* from a URL;
so is §9's build-time copy of the spec to the output directory (`init` scaffolds the
`<InTestSpecSource>` property, but nothing consumes it and the runtime reads
`Generated/spec-schemas.json`).

## Commands

```bash
dotnet build InTest.sln
# `dotnet test InTest.sln` now fails outright, not just incompletely: with both an MSTest and an
# xunit.v3 project in the solution, the xUnit project errors on the VSTest target while MSTest
# still runs and prints `Passed!`, and the command exits 1 (measured). Run each suite
# individually instead — `.github/workflows/build-and-test.yml`'s `fast` job lists all five,
# including `InTest.Runtime.XUnit.Tests`, which is not `dotnet test`-able at all and instead runs
# as `dotnet tests/InTest.Runtime.XUnit.Tests/bin/Debug/net10.0/InTest.Runtime.XUnit.Tests.dll`
# after a plain `dotnet build` of that project (the platform apphost does not exist on Linux).
dotnet test tests/InTest.Cli.Tests                       # one suite
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~CSharpLiteralTests"

dotnet run --project src/InTest.Cli -- init --name Orders.ApiTests --spec ../orders.json
dotnet run --project src/InTest.Cli -- generate --project <dir>
dotnet run --project src/InTest.Cli -- fixtures repair --project <dir>
```

Golden file update (writes the **source** copy, then asserts `Inconclusive` — rebuild and re-run
without the variable to actually verify):

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

`InTest.Golden.Tests` shells out to `dotnet build` and `dotnet test` on scaffolded temp projects
and runs generated suites against an in-process HTTP stub. It is slow and it is the only suite
that proves generated code both compiles *and* runs — do not skip it when changing the template,
the renderer, or the scaffold. **Measured locally, 2026-08-26, two consecutive runs: ~3m49s–3m50s
warm — but ~9m43s from a *fresh worktree*, and both numbers are real.** The gap is the whole reason
this figure needs a qualifier rather than a value: the temp projects these tests scaffold each run a
real `dotnet build`, so a cold NuGet cache and cold obj/bin pay full restore-and-compile cost on
every one of them, while a warm repeat reuses all of it. Quote which one you measured, or the next
person reasonably concludes the doc is wrong.
This is a budget, not a fact: every `CompileVerificationTests` (and most `GeneratedSuiteExecutionTests`)
case shells out to a real `dotnet build` (some also `dotnet test`) on a freshly scaffolded temp
project, so the suite's wall-clock time grows roughly linearly with the number of generated-code
shapes under test — it has no fixed ceiling the way an in-process suite would. It has grown before:
this doc quoted ~90–107s at one point, then ~3m9s–3m17s after that was corrected, and now
~3m49s–3m50s, each step tracking real cases added (this branch alone added three new
`CompileVerificationTests` cases and substantially grew `GeneratedSuiteExecutionTests`). Expect the
next reader's own measurement to be higher still if more shapes have been added since — treat
whatever figure is quoted here as a floor to size a timeout against, not a number to assert against.
A tool's default command timeout (commonly ~2 minutes) cuts this off mid-flight, which reads as a
hang rather than a slow-but-healthy run; pass an explicit timeout well past the figure above (see
the `golden` CI figure below for how much further it can run under load) rather than shortening the
command or assuming it stalled.

Running the sample APIs requires specific environment variables (ports, issuer/authority pairing,
`ASPNETCORE_ENVIRONMENT=Development`). See `samples/README.md`; getting them wrong produces
500s or silent 404s rather than an obvious failure.

Exercising Phase 8 of `docs/getting-started.md` (`dotnet tool restore`, `generate --check`,
`upgrade`) against the **published** `0.1.0-preview.1` now works from a bare clone with no local
feed at all. Testing an **unpublished** change still needs a local pack-and-restore — never
improvise this by hand, NuGet's global package cache never invalidates a locally-built version
number (see CONTRIBUTING.md's "Testing against a local build"). Use:

```bash
pwsh scripts/local-e2e-test.ps1
```

CI (`.github/workflows/build-and-test.yml`, push to `main` and every pull request, matrixed
`ubuntu-latest`/`windows-latest`) runs the commands above split across three jobs: `fast`
(Architecture + Cli + Runtime; a prior measurement recorded ~33.5–35.5s cold-cache, but CI itself
has since measured **~1m26s–1m46s** — trust the CI figure over the cold-cache one, since it is what
actually gates a PR), `golden` (Golden alone, kept in its own parallel job so it cannot delay
`fast`'s verdict — CI last measured **~3m6s/3m12s on ubuntu-latest and ~4m14s/4m18s on
windows-latest**, from before this branch's own `CompileVerificationTests`/`GeneratedSuiteExecutionTests`
growth landed, so treat those as stale in the same direction and by roughly the same proportion as
the local figure above grew — expect CI to have climbed too, not just the local number; re-measure
from an actual CI run rather than assuming), and `dogfood`
(`scripts/ci/dogfood.ps1`: `init` → `generate` → `fixtures repair` → `generate` →
`generate --check` against the three sample specs under `samples/`, no live API — static only).
Reproduce `fast`/`golden` locally with the `dotnet test` invocations above; reproduce `dogfood`
locally with `pwsh scripts/ci/dogfood.ps1 -RepoRoot . -ScaffoldRoot <dir-outside-the-checkout>
-CliDll <path-to-built-InTest.Cli.dll>`. `scripts/ci/assert-trx-results.ps1` then checks each
`.trx` actually reports executed tests for the right assembly, so a suite silently matching
nothing cannot read as green. Every third-party action the workflow uses is pinned by commit
SHA — see CONTRIBUTING.md's dependency policy.

## Build configuration

- Central package management: **all** versions live in `Directory.Packages.props`. A
  `PackageReference` with an inline `Version` in a project file is a build error.
- `Directory.Build.props` sets `net10.0`, nullable, and `TreatWarningsAsErrors=true`. It carries
  no `<Version>` element — MinVer (build-time only, `PrivateAssets="all"`) derives `Version`,
  `PackageVersion`, `AssemblyVersion` and `InformationalVersion` from git tags and commit height
  instead (`[version-from-git]`,
  `docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`). `Directory.Build.props` also
  carries `InTestEnsureNotShallowClone`, a build target that fails loudly if the checkout is a
  shallow git clone — MinVer would otherwise silently compute a plausible-looking but wrong
  version there. See `CONTRIBUTING.md`'s "Branching and how a release is cut" for the full
  explanation of both.
- The scaffold's `InTest.Runtime.MSTest` reference is **not** a hardcoded literal — `InitCommand.cs`
  interpolates `CliVersion.Current` (`[scaffold-reads-itself]`, same plan), so whatever version the
  running CLI was built as is exactly what a freshly scaffolded project references. This is the
  adapter package, not the neutral one: a generated project references `InTest.Runtime.MSTest`
  directly and gets `InTest.Runtime` transitively, at the exact same version, through the adapter's
  own dependency on it. `intest upgrade` reads a scaffolded `.csproj` and *reports* (never
  rewrites) when that reference has drifted from the running CLI's version.
- **Third-party package versions are still duplicated by design in three places** and must be
  changed together: `Directory.Packages.props`, the scaffolded `.csproj` string in
  `InitCommand.cs`, and the hand-written test project in `CompileVerificationTests.cs`.
  `InTest.Architecture.Tests`' `PackageVersionCouplingTests` enforces this mechanically — it fails,
  by package name with both versions and both files, if a hardcoded version in either scaffold
  site disagrees with `Directory.Packages.props`. `InTest.Runtime.MSTest` is checked separately
  from this three-way rule, not as a fourth member of it: it has no `Directory.Packages.props`
  entry at all (it is InTest's own version, not a third-party one), so `PackageVersionCouplingTests`
  instead confirms the scaffold's source text still interpolates `CliVersion.Current` rather than
  any literal, plus a behavioral test that actually scaffolds a project and compares the emitted
  reference against `CliVersion.Current` directly.
- `.github/dependabot.yml` proposes weekly version bumps to `Directory.Packages.props` and to the
  SHA-pinned actions in `.github/workflows/build-and-test.yml`. It only ever edits
  `Directory.Packages.props`, so a bump to `MSTest.TestFramework`, `MSTest.TestAdapter`,
  `Microsoft.NET.Test.Sdk`, `MSTest.Analyzers` or `Shouldly` is expected to fail
  `PackageVersionCouplingTests` — that is the guard working as designed, not a broken config. See
  CONTRIBUTING.md's "Automated dependency updates" section for what to do with such a PR and for
  what the config can and cannot enforce against the dependency policy below.

## Architecture

### The generation pipeline (`src/InTest.Cli`)

`SpecLoader` -> `TestPlanBuilder` -> `TemplateRenderer` -> files under `Generated/`.

`Spec/` splits three ways, and the split is deliberate: `SpecLoader` turns *text* into an
`OpenApiDocument` and knows nothing about where the text came from; `SpecFetcher` owns HTTP policy
(timeout, size cap, status and content-type handling) for a URL source; `SpecSnapshot` owns the
committed `spec.json` — its name, its bytes, and the reprint that makes `--check` stable. Do not
fold fetching back into the loader: parsing and transport are different concerns with different
failure vocabularies.

- **`Planning/`** is the single source of truth. `TestPlanBuilder.Build` decides which operations
  produce cases, which are skipped (with a reason), and which get a non-removing coverage *note*.
  `TestCasePlan` deliberately **carries** verdicts computed elsewhere (`NeedsFixture`,
  `HasRequestBody` from `FixtureComposer`; `RequiredScopes` from the spec's `security`) rather
  than letting downstream code re-derive them. Re-deriving is the recurring defect in this
  codebase — don't.
- **`Rendering/`** — one Scriban template, `Templates/mstest-class.scriban`. This is the only
  place MSTest code shape is decided.
- **`Coverage/CoverageReport`** emits `coverage-report.json` next to the project. It is
  committed (explicitly un-ignored in `.gitignore`) and its JSON shape is covered by semver.

### Ownership boundaries

| Directory | Written by | Never touched by |
|---|---|---|
| `Generated/` | `generate` (deleted and rewritten wholesale) | humans |
| `spec.json` | `generate`, when `spec.source` is a URL — the committed snapshot (§9) | humans; `fixtures repair` and `--check` only *read* it |
| `fixtures/` | `fixtures repair` only | `generate` — it only *reports* drift |
| everything else | the adopting team | InTest, with one narrow exception: `upgrade` writes `.gitattributes` if the project does not already have one, never overwriting an existing one |

`generate` detects fixture drift **before** writing any generated *output* and exits `1`. The one
deliberate exception is `spec.json`, written as soon as a fetched document parses and therefore
before the drift gate — it is the materialized *input*, not output, and writing it later
deadlocks the drift/repair cycle. `[snapshot-is-input]` in
`docs/superpowers/plans/2026-08-24-intest-url-spec-source.md` is the canonical explanation, with
the worked loop; `GenerateCommand.ResolveSpecAsync` points at it.

Exit codes are public API: `0` ok, `1` work outstanding, `2` tool error, `3` already initialised
(`init` only), `4` tool/config version mismatch (`generate --check` only).

### Three separate text-safety rules — keep them separate

They look mergeable and are not. Merging them has been reasoned through and rejected:

- `Naming/CSharpLiteral.Escape` — authority is the C# grammar. Applied to **every** spec-derived
  value the template quotes. Model fields carrying its output are suffixed `_literal`;
  `TemplateEscapingGuardTests` enforces that naming against the template source mechanically.
- `Naming/CSharpIdentifier.TryValidateDottedName` — authority is C# declaration syntax. Values
  like `rootNamespace` and `testBaseClass` reach the template as *declarations*, so no escaping
  can save them; they are refused up front in `InitCommand`/`GenerateCommand`.
- `Fixtures/FixtureDocument.TryValidateOperationKey` — authority is the filesystem. Gated on
  `NeedsFixture`, because a key only becomes a filename when a fixture is written for it. The
  canonical explanation lives in `TestPlanBuilder.Build`; other sites point at it.

### Runtime (`src/InTest.Runtime`, `src/InTest.Runtime.MSTest`)

Two projects, not one, and not subfolders of one. `src/InTest.Runtime` is the neutral package;
**no file in it may name `Microsoft.VisualStudio.TestTools.UnitTesting`**, and it has no
`PackageReference` to any test framework, so the check is compiler-enforced by construction — no
MSTest reference means no implicit `global using Microsoft.VisualStudio.TestTools.UnitTesting`
either. `InTest.Architecture.Tests`' `NeutralityTests` (a csproj guard) and `pack-and-verify.ps1`
(a packed-nuspec guard, checking the shipped dependency list rather than the project file) both
still check it, because the compiler can only catch a *reference*, not a regression in either
guard file itself. This is what keeps xUnit/NUnit additive rather than a rewrite (§3). Anything
framework-coupled lives in `src/InTest.Runtime.MSTest`, which depends on `InTest.Runtime` at the
exact same version plus `MSTest.TestFramework`. **Both projects declare their types in the same
`namespace InTest.Runtime`** — an adopter migrating from a hypothetical all-in-one package changes
only the `PackageReference` id, never a `using` or a type name in their own source; `intest
upgrade` detects the old package id and reports it.

`InTest.Runtime.MSTest`'s `TestHost` is a thin facade over `InTest.Runtime`'s `InTestRun`, the
actual assembly-scope composition root: configuration, DI, schema bundle, run id, profile, fixture
store, readiness probe, and the one `FixtureValidation.Report` every `ApiTestBase.RequireFixture`
consults. Generated projects delegate `[AssemblyInitialize]` to `TestHost` and add their own
registrations through `TestHost.ConfigureServices`; `TestHost` cannot itself live in the neutral
assembly because it names `TestContext`, which `[TypeForwardedTo]` cannot bridge past a rename.

`InTest.Runtime.MSTest`'s `ApiTestBase` is the generated classes' base, and is itself a thin
adapter over `InTest.Runtime`'s `ApiTestCore`, which holds the actual scope-containment logic. Auth
cases use `UseIdentity(IdentitySlot)` plus the guards `RequireMultipleIdentities` and
`RequireSecondaryIdentityLacks` — the latter *skips* a wrong-scope 403 case when the secondary
identity genuinely holds the scope, rather than asserting a 403 the API is correct not to return.

Four seams were extracted from the pre-split code so this boundary could exist without a rewrite,
each deliberately the plainest shape that does the job rather than a speculative interface:
`IRunDiagnostics` (`Note`/`Warn`, states intent rather than MSTest's `DisplayMessage(MessageLevel,
…)` mechanism — replaced `ContextTextWriter`), the run-settings profile (a plain `string?`, not an
`IRunSettings` interface), the test display name (a plain `string?` passed to
`ApiTestCore.BeginTest`, deliberately not the `ITestIdentity` the design spec's §3 once
prescribed — see that section for the rejected-alternative reasoning), and skip (the neutral layer
returns a reason `string?`, null meaning "run"; the MSTest adapter turns a non-null reason into
`Assert.Inconclusive` — xUnit's `Assert.Skip` and NUnit's `Assert.Ignore` drop straight in).

`project.framework` in `intest.json` is read and validated (`ConfigLoader.RequireSupportedFramework`)
— required, and only the exact lowercase `"mstest"` is accepted; anything else is refused as "not
supported yet", naming §3's roadmap. `TemplateRenderer` still hardcodes `mstest-class.scriban`
regardless of the value — the config now carries `project.framework` correctly, but nothing yet
*branches* on it to select a template.

## Working conventions

- **The spec is the source of truth**, not the code:
  `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`. Section references
  (§3, §12, §16) throughout the code point there. Update it in the same change when behaviour
  changes, and update `docs/getting-started.md` when the adoption path changes.
- **One canonical explanation, pointers elsewhere.** When the same reasoning must appear twice,
  one copy is authoritative and the other references it. Same for values: `CoverageReport`
  matches against `TestPlanBuilder.NoPathParameterNoteReason` rather than a copied literal.
- **Comments explain why, at length, with evidence.** This codebase deliberately records rejected
  alternatives and how a claim was established ("confirmed by direct experiment" vs "the docs
  say"). Match that density; do not strip it.
- **New plans in `docs/superpowers/plans/` name decisions with slugs** (`[containment]`,
  `[descriptor]`), never numbers. Do **not** retrofit slugs onto closed plans — v1-a, v1-b and
  v1-c stay numbered. `CONTRIBUTING.md`'s "Writing plans" section is the canonical explanation of
  why; this is the summary.
- **Dependency policy is hard**: no preview/prerelease, permissive licences only, no assumed
  vendor SDK, and check nuget.org deprecation/vulnerability metadata. `CONTRIBUTING.md`'s
  "Dependency policy" section is the canonical explanation, including the specific packages this
  ruled out; this is the summary.
- **Fail loudly.** Missing fixture data becomes an obvious `TODO:` sentinel and a red test. Never
  substitute plausible defaults that let a suite pass while asserting nothing.

## Constraints that are not negotiable in v1

MSTest or xUnit v3, chosen with `init --framework` and frozen per project — NUnit is not
supported yet. Test project TFM is `net10.0` (independent of the API's). Real HTTP against a
deployed target — no mocking, no in-memory host, no stateful CRUD flow ordering.
