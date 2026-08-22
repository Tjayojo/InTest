# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

InTest generates a committed, owned MSTest project that exercises a **deployed** API over real
HTTP, from its OpenAPI document. Two shipped packages (`InTest.Cli`, `InTest.Runtime`), four
sample APIs used as fixtures, four test suites. Nothing is published to NuGet; build from source.

`init`, `generate`, `fixtures repair`, `generate --check` and `upgrade` work end to end.
`survey`, `fixtures promote`, `assertions add`, `generate --emit-plan`, variation tests and YAML
input do **not** exist yet — do not assume they do.

## Commands

```bash
dotnet build InTest.sln
dotnet test  InTest.sln                                  # all four suites
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
the renderer, or the scaffold.

Running the sample APIs requires specific environment variables (ports, issuer/authority pairing,
`ASPNETCORE_ENVIRONMENT=Development`). See `samples/README.md`; getting them wrong produces
500s or silent 404s rather than an obvious failure.

## Build configuration

- Central package management: **all** versions live in `Directory.Packages.props`. A
  `PackageReference` with an inline `Version` in a project file is a build error.
- `Directory.Build.props` sets `net10.0`, nullable, `TreatWarningsAsErrors=true`, and pins
  `Version` to `0.1.0` — the scaffold emits `InTest.Runtime 0.1.0`, so the SDK's default of
  `1.0.0` would break every scaffolded restore.
- **Package versions are duplicated by design in three places** and must be changed together:
  `Directory.Packages.props`, the scaffolded `.csproj` string in `InitCommand.cs`, and the
  hand-written test project in `CompileVerificationTests.cs`.

## Architecture

### The generation pipeline (`src/InTest.Cli`)

`SpecLoader` -> `TestPlanBuilder` -> `TemplateRenderer` -> files under `Generated/`.

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
| `fixtures/` | `fixtures repair` only | `generate` — it only *reports* drift |
| everything else | the adopting team | InTest, with one narrow exception: `upgrade` writes `.gitattributes` if the project does not already have one, never overwriting an existing one |

`generate` detects fixture drift **before** writing anything and exits `1`. Exit codes are public
API: `0` ok, `1` work outstanding, `2` tool error, `3` already initialised (`init` only),
`4` tool/config version mismatch (`generate --check` only).

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

### Runtime (`src/InTest.Runtime`)

Split into `Neutral/` and `MSTest/`. **No file under `Neutral/` may name
`Microsoft.VisualStudio.TestTools.UnitTesting`** — `InTest.Architecture.Tests` enforces this at
source level. This is what keeps xUnit/NUnit additive rather than a rewrite (§3). Anything
framework-coupled goes under `MSTest/`.

`MSTest/TestHost` is the assembly-scope composition root: configuration, DI, schema bundle, run
id, profile, fixture store, readiness probe, and the one `FixtureValidation.Report` every
`ApiTestBase.RequireFixture` consults. Generated projects delegate `[AssemblyInitialize]` to it
and add their own registrations through `TestHost.ConfigureServices`.

`MSTest/ApiTestBase` is the generated classes' base. Auth cases use `UseIdentity(IdentitySlot)`
plus the guards `RequireMultipleIdentities` and `RequireSecondaryIdentityLacks` — the latter
*skips* a wrong-scope 403 case when the secondary identity genuinely holds the scope, rather than
asserting a 403 the API is correct not to return.

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

MSTest only. Test project TFM is `net10.0` (independent of the API's). Real HTTP against a
deployed target — no mocking, no in-memory host, no stateful CRUD flow ordering.
