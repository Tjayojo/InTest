# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). `InTest.Cli` and
`InTest.Runtime` ship together and their majors move together — see `CONTRIBUTING.md`'s
"Releases" section for the exact compatibility contract, and its "Changelog" section for what
goes in `Unreleased` and when it moves to a version heading.

## [Unreleased]

## [0.1.0-preview.2] - 2026-08-31

Adds xUnit v3 and NUnit as supported test frameworks, bringing the published package count to
five. `InTest.Runtime.MSTest`, `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit` are all published
here for the first time; `InTest.Cli` and `InTest.Runtime` move from `0.1.0-preview.1`.

### Added

- **xUnit v3 and NUnit are now supported test frameworks**, chosen with `intest init --framework
  xunit` / `--framework nunit` and frozen per project. Two new adapter packages ship alongside the
  MSTest one — `InTest.Runtime.xUnit` (depending on `InTest.Runtime` at the exact same
  version plus `xunit.v3.extensibility.core` and `xunit.v3.assert`) and `InTest.Runtime.NUnit`
  (plus `NUnit` alone, which needs no equivalent split). All three adapters declare their types in
  the same `namespace InTest.Runtime`, so the only things that differ between a generated MSTest,
  xUnit and NUnit project are the `PackageReference` id and the framework's own attributes —
  never a `using`, never a type name. `InTest.Runtime` itself gained nothing and lost nothing: the
  design spec's §3 promised a second framework would cost "a template set plus an adapter package"
  and no change to the neutral runtime, and that held for both additions, each verified by an empty
  `git diff src/InTest.Runtime/`. **Migration:** none — existing MSTest projects are
  untouched, and a suite still cannot be migrated between frameworks in place. See
  `docs/superpowers/specs/2026-08-30-intest-xunit-framework-pack.md` and
  `docs/superpowers/plans/2026-08-31-intest-nunit-framework-pack.md`.
- Both new adapters were run **live against real deployed APIs**, not only against generated-code
  fixtures. Each reproduces the MSTest figures exactly (Catalog 13 of 13; Orders 20 passed, 0
  failed, 4 skipped), with all four skips bottoming out in `RequireSecondaryIdentityLacks` across
  three different skip mechanisms (`Assert.Inconclusive`, `Assert.Skip`, `Assert.Ignore`) while the
  three write-scope 403 cases run and pass — so the skip decision is per-operation, not a
  blanket avoidance. `docs/v0-acceptance.md`'s framework-pack record has the full account,
  including two findings that are environmental rather than product defects.

- Opt-in invocation through a team's own pre-generated API client (Kiota, NSwag, or Refit): a new
  `client` section in `intest.json` (`{ "kind": "kiota", "typeName": "..." }`) routes qualifying
  generated `Success` cases through `ApiClient<TClient>()` instead of a hand-built
  `HttpRequestMessage`, while raw-bytes schema validation, expected-status assertions and every
  other case kind (declared-error, auth) work exactly as before — a `DelegatingHandler` buffers
  and captures the response before the typed client deserializes it, so the client changes only
  *how* a request is issued, never what a generated test asserts. Convention-derivation covers
  Kiota unconditionally, and NSwag once the operation's `operationId` is present and contains no
  `_` — both measured directly against real generator output, not assumed. `client-map.json` lets
  an adopter override or supply the call expression for any operation convention-derivation does
  not reach (query parameters, a request body, an NSwag operation whose `operationId` doesn't
  qualify, or any Refit operation at all — Refit gets no convention, permanently, since its method
  naming is never spec-derivable). This is entirely additive and opt-in: a project with no `client`
  section produces byte-identical output to one built without
  this feature at all. **Migration:** none required. See
  `docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md` for the design and
  `docs/getting-started.md`'s "Typed client invocation (opt-in)" for the three concrete client
  registrations, including the one requirement that matters most — construct the client over
  `IHttpClientFactory.CreateClient(InTestClients.Api)`, or it silently loses capture, auth and the
  run-id header.
- `intest init --client-lockfile <path>`, mutually exclusive with `--spec`, for a team that owns a
  generated client but not the OpenAPI document it came from: recovers `spec.source` from a Kiota
  `kiota-lock.json`'s `descriptionLocation` (a local path or a URL), and — where the lockfile also
  names `clientClassName`/`clientNamespaceName` — scaffolds a working `client` section too, so the
  common case needs no hand-editing. A required field missing, renamed, blank or wrong-typed fails
  loudly, naming the field, rather than silently scaffolding an empty `spec.source`. NSwag's own
  config was measured (`nswag new`, NSwag 14.7.1) and deliberately not supported: its `className`
  is a naming template under NSwag's default generation mode, not a concrete type name. See
  `docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md`'s `[lockfile-recovery]` for
  the measured detail and `docs/getting-started.md`'s "Typed client invocation (opt-in)" for the
  worked example.
- `ApiTestCore.BeginTest` gained a second, optional-in-effect `IRunDiagnostics` parameter
  (`[warn-on-swallowed-exception]`): a client-routed case whose `client-map.json` override issues
  more than one call, where an earlier call already captured a response and a later one throws,
  now reports that discarded exception's type and message through `Warn` instead of losing it
  silently — the exact gap a reviewer raised, since the captured response can still satisfy the
  test's own assertion and the run otherwise exits 0. `ApiTestBase.ApiTestInitialize` already calls
  the two-argument form with a real per-test sink, so every case this repository ships gets the
  warning for free. This is additive, not a break, at the `InTest.Runtime` package boundary a
  third-party adapter sits on: the pre-existing one-argument
  `BeginTest(string?)` survives as a compatibility overload, so a caller built against
  `0.1.0-preview.1` keeps compiling and keeps running with the exact old silent-discard behaviour,
  unchanged, until it migrates to the two-argument form to start receiving the warning.
  **Migration:** none required; call the two-argument overload with your own `IRunDiagnostics` to
  start seeing a swallowed second-call exception reported.

### Changed

- `project.framework` in `intest.json` is now validated against three accepted values —
  `"mstest"`, `"xunit"` and `"nunit"` — and `TemplateRenderer` selects one of three
  Scriban templates from it at construction time. Previously only `"mstest"` was accepted and the
  value was read but never branched on. **Migration:** none; existing `intest.json` files already
  say `"mstest"`.
- **Breaking:** `InTest.Runtime` split into two packages — the framework-neutral `InTest.Runtime`
  (no test-framework dependency) and a new `InTest.Runtime.MSTest` adapter (`TestHost`,
  `ApiTestBase`, and the `MSTest.TestFramework` dependency) — so that the xUnit and NUnit
  adapters shipped in this same version never pull MSTest in transitively, and vice versa. A generated project now references
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

[Unreleased]: https://github.com/Tjayojo/intest/compare/0.1.0-preview.2...HEAD
[0.1.0-preview.2]: https://github.com/Tjayojo/intest/releases/tag/0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/Tjayojo/intest/releases/tag/0.1.0-preview.1
