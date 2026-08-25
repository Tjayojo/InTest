# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). `InTest.Cli` and
`InTest.Runtime` ship together and their majors move together — see `CONTRIBUTING.md`'s
"Releases" section for the exact compatibility contract, and its "Changelog" section for what
goes in `Unreleased` and when it moves to a version heading.

## [Unreleased]

### Changed

- **Breaking:** `InTest.Runtime` split into two packages — the framework-neutral `InTest.Runtime`
  (no test-framework dependency) and a new `InTest.Runtime.MSTest` adapter (`TestHost`,
  `ApiTestBase`, and the `MSTest.TestFramework` dependency) — so that a future xUnit or NUnit
  adapter never pulls MSTest in transitively, and vice versa. A generated project now references
  `InTest.Runtime.MSTest` instead of `InTest.Runtime`. **Migration:** change the
  `PackageReference` id in your `.csproj` from `InTest.Runtime` to `InTest.Runtime.MSTest`; both
  packages declare their types in the same `namespace InTest.Runtime`, so no source change is
  needed. `intest upgrade` detects the old package id and reports it.

## [0.1.0-preview.1] - 2026-08-24

First published prerelease of `InTest.Cli` and `InTest.Runtime`, pushed to nuget.org via NuGet
Trusted Publishing (OIDC) — no stored API key, a short-lived key exchanged from a GitHub-issued
OIDC token at push time. See `docs/v0-acceptance.md`'s publish record for what was verified about
the push itself, and the rest of that document for what was verified about the tool.

### Added

- `intest init` scaffolds a committed, owned MSTest test project from an OpenAPI document:
  `intest.json`, the `.csproj`, `AssemblyInfo.cs`, `TestStartup.cs`, a test-base class,
  `appsettings*.json`, a `<Name>.runsettings`, a local tool manifest
  (`.config/dotnet-tools.json`) and `.gitattributes`. Refuses to overwrite an existing project
  (exit `3`).
- `intest generate` builds a `TestPlan` from the OpenAPI document and renders one MSTest class per
  tag under `Generated/`, plus `spec-paths.json`, `spec-schemas.json` and `coverage-report.json`.
  Detects missing or stale fixtures and exits non-zero *before* writing anything, rather than
  emitting a suite that cannot send a request it needs to.
- `intest fixtures repair` creates and maintains fixtures under `fixtures/` — request bodies and
  path/query parameters — using the spec's `example`/`default` where one exists and an obvious
  `TODO:` sentinel otherwise. Never overwrites a value a human already wrote; flags a property
  that left the schema instead of silently dropping it.
- `intest generate --check` compares `Generated/` and `coverage-report.json` against a fresh run
  without writing anything, for CI. Exit codes: `0` identical, `1` output or a fixture has
  drifted, `2` tool error, `4` the running tool's version doesn't match `intestVersion` in
  `intest.json` (checked before comparing output, so a tool upgrade is never mistaken for spec
  drift).
- `intest upgrade` regenerates against the running tool version, then bumps `intestVersion` and
  the `.config/dotnet-tools.json` pin together, so the version change and its output change land
  in one reviewable commit instead of arriving disguised as spec drift. Scaffolds
  `.gitattributes` for a project that predates it.
- URL `spec.source`: `generate` fetches an anonymously-reachable OpenAPI document and writes it
  into the project as a committed `spec.json` snapshot; `generate --check` and `fixtures repair`
  read that snapshot and never open a socket, so CI stays hermetic (§9). JSON only — a URL
  serving YAML is refused by name rather than failing as a parse error.
- Fixture tokens: `{{config:...}}`, `{{runId}}`, `{{utcNow}}`, and `{{fixture:...}}` resolved from
  a registered `IAssemblyFixture`. `FixtureGraph` orders fixtures by `DependsOn`; cleanup
  registered with `ctx.OnCleanup` drains in reverse on `AssemblyCleanup`, best-effort.
- Auth test generation: a no-token 401 case and a wrong-scope 403 case for every operation with
  declared `security`, selecting identities by slot (`IdentitySlot.Default`/`Secondary`) and
  gated on each identity's declared `Scopes`. `RequireMultipleIdentities` and
  `RequireSecondaryIdentityLacks` skip a case the configured identities cannot actually prove,
  with a stated reason, instead of asserting a 403 the API is correct not to return.
- Declared-error contract tests (404s) generated from the spec's declared responses.
- Runtime (`InTest.Runtime`): `TestHost` as the assembly-scope composition root (configuration,
  DI, schema bundle, run id, profile, fixture store, readiness probe); `ApiTestBase`; response
  schema validation from the bundled spec; contract assertions with method/URL/expected-vs-actual/
  elapsed-time/run-id/body diagnostics; ASCII, collision-free test identifiers (transliterated and
  hashed when lossy); an `X-Test-Run-Id` correlation header on every request; readiness gating
  that requires two consecutive successes and short-circuits on a terminal status (404/405/410/501)
  instead of burning the full timeout.
- `coverage-report.json`: committed and semver-covered. Reports skipped operations with a reason,
  operations running on a synthesized `operationId`, status-only tests, and auth cases gated on a
  second identity.
- MinVer-derived versioning from git tags and commit height, with a shallow-clone guard
  (`InTestEnsureNotShallowClone`) that fails the build rather than silently producing a
  wrong-but-plausible version from a shallow checkout.
- `.gitattributes` scaffolding that pins `Generated/`, `coverage-report.json` and
  `fixtures/**/*.json` to CRLF, so `core.autocrlf=input` cannot flatten them to LF and fail
  `generate --check` on every line after a fresh checkout.
- Four sample APIs used as this project's own fixtures and acceptance-run targets:
  `Catalog.Api` (built-in `Microsoft.AspNetCore.OpenApi`, OpenAPI 3.1), `Orders.Api` (Swashbuckle,
  OpenAPI 3.0, Duende auth), `Inventory.Api` (NSwag, OpenAPI 3.0), and `Identity.Server`.
- NuGet publish-readiness metadata: symbol packages (`.snupkg`), package validation, per-package
  READMEs, `THIRD-PARTY-NOTICES.md`, and a package icon.

### Not included in this release

Stated directly, because an enumeration that silently goes stale is this codebase's own recurring
defect: `intest survey`, `intest fixtures promote`, `intest assertions add`,
`intest generate --emit-plan`, variation tests, and YAML input (from a file or a URL alike) do
not exist yet. xUnit and NUnit are not supported — MSTest only. See `README.md`'s status banner
and `docs/getting-started.md` for what each gap means in practice.

[Unreleased]: https://github.com/Tjayojo/intest/compare/0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/Tjayojo/intest/releases/tag/0.1.0-preview.1
