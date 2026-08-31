# Cross-framework example projects — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** commit an example project for every (spec × framework) pair — Catalog and Orders under
MSTest, xUnit and NUnit — so the generated output of all three frameworks sits side by side in the
repository and is checked by CI.

**Architecture:** four new scaffolds beside the two that exist, each referencing the **published**
`0.1.0-preview.2` adapter packages, plus a CI job that builds every example and runs
`generate --check` on it.

**Tech Stack:** .NET 10, the published `InTest.Cli` 0.1.0-preview.2, MSTest / xunit.v3 / NUnit.

---

## Why this is not redundant with the Golden suite

`InTest.Golden.Tests` already pins byte-for-byte rendering for all three frameworks. It proves the
**templates** are right. It does not prove the **published packages** are, because every Golden
project substitutes a `ProjectReference` for the adapter's `PackageReference`.

Nothing committed today references the published `InTest.Runtime.xUnit` or `InTest.Runtime.NUnit`
at all. That is the gap this closes, and it is why these examples must resolve the adapters from
nuget.org rather than from `src/`.

Division of labour, worth stating once so neither guard drifts into the other's job:

| | proves | resolves adapters from |
|---|---|---|
| `InTest.Golden.Tests` | template rendering, byte-exact | `ProjectReference` |
| `examples/` + its CI job | the adopter path against published packages | nuget.org |

## The comparison this makes possible

`Generated/` **is committed** in every example (`Generated/*.g.cs`, `spec-paths.json`,
`spec-schemas.json`). With all six examples present, the generated output for one spec under three
frameworks is diffable directly in the tree.

**The expected diff between two frameworks of the same spec is exactly the substitution table and
nothing else** — the `using`, the class/method attributes, and the cancellation-token expression.
Anything else differing means a template re-derived a planner verdict rather than interpolating it,
which `CLAUDE.md` names as the recurring defect in this codebase. Task 5 asserts this.

## Naming

New directories take a framework suffix; the two existing MSTest examples keep their current names.

- `examples/Catalog.ApiTests` (MSTest, exists) · `Catalog.ApiTests.XUnit` · `Catalog.ApiTests.NUnit`
- `examples/Orders.ApiTests` (MSTest, exists) · `Orders.ApiTests.XUnit` · `Orders.ApiTests.NUnit`

The asymmetry is deliberate: renaming the existing two would churn every reference in
`docs/getting-started.md`, `docs/v0-acceptance.md` and `README.md`, and break external links, to buy
nothing but symmetry. `.XUnit` (capital X) matches the existing `InTest.Runtime.XUnit.Tests`
project, even though the *package* id is `InTest.Runtime.xUnit`.

## Facts established before writing this plan — do not re-derive

- `init --framework` accepts `mstest` (default), `xunit`, `nunit`.
- Existing examples set `spec.source` to `../../samples/<Api>/<Api>.json`.
- Fixture values are **framework-independent** — the same JSON works under all three, which is what
  makes the cross-framework diff meaningful.
- Catalog has **8** fixtures; Orders has **5**.
- `ExampleProjectVersionMarkerTests` discovers any directory under `examples/` containing an
  `intest.json`, so new examples are covered by it automatically with no test change.
- CI currently touches `examples/` **not at all**; `dogfood` runs against `samples/`.
- The published CLI is pinned per-example in `.config/dotnet-tools.json`.

---

## Task 1: Catalog under xUnit

**Files:** Create `examples/Catalog.ApiTests.XUnit/`

- [ ] **Step 1: Scaffold**

```bash
dotnet tool install -g InTest.Cli --version 0.1.0-preview.2
intest init --project examples/Catalog.ApiTests.XUnit --name Catalog.ApiTests.XUnit \
  --framework xunit --spec ../../samples/Catalog.Api/Catalog.Api.json
```

- [ ] **Step 2: Carry over the configuration that is not framework-specific**

Copy from `examples/Catalog.ApiTests`, unchanged: every file under `fixtures/` (all 8),
`appsettings.json`, `appsettings.staging.json`. Do **not** copy `TestStartup.cs`, the test-base
class, `AssemblyInfo.cs`, the `.csproj` or the `.runsettings` — `init` scaffolds those per
framework and they legitimately differ.

Fixtures are framework-independent. If a fixture needs editing to work under xUnit, **stop and
report** — that would mean fixture resolution is framework-coupled, which contradicts the design.

- [ ] **Step 3: Generate and verify**

```bash
cd examples/Catalog.ApiTests.XUnit && dotnet tool restore && dotnet intest generate
dotnet build
dotnet intest generate --check     # expect exit 0
```

`generate --check` exiting non-zero means committed output disagrees with a fresh render. Do not
commit in that state.

- [ ] **Step 4: Confirm it resolves the published adapter**

```bash
dotnet list package --include-transitive
```

Expect `InTest.Runtime.xUnit 0.1.0-preview.2` top-level and **`InTest.Runtime 0.1.0-preview.2`**
transitive. A `ProjectReference` anywhere in this project defeats the entire point of the task.

- [ ] **Step 5: Commit**

```bash
git add examples/Catalog.ApiTests.XUnit
git commit -m "examples: Catalog under xUnit"
```

## Task 2: Catalog under NUnit

Identical to Task 1 with `--framework nunit`, directory `examples/Catalog.ApiTests.NUnit`, name
`Catalog.ApiTests.NUnit`, expecting `InTest.Runtime.NUnit 0.1.0-preview.2` in Step 4.

Commit message: `examples: Catalog under NUnit`

## Task 3: Orders under xUnit

**Files:** Create `examples/Orders.ApiTests.XUnit/`

As Task 1, with `--framework xunit`, the Orders spec
(`../../samples/Orders.Api/Orders.Api.json`), and Orders' **5** fixtures — plus one addition:

- [ ] **Step 2b: The token provider**

Copy `examples/Orders.ApiTests/OrdersTokenProvider.cs` **verbatim**. It has zero MSTest dependency
and compiled unmodified under both xUnit and NUnit during the framework-pack acceptance run
(`docs/v0-acceptance.md`), so a change here is a finding, not a fix.

Register it the way `examples/Orders.ApiTests/TestStartup.cs` does, adapted to the shape `init`
scaffolds for xUnit. Do not invent an interface.

Commit message: `examples: Orders under xUnit`

## Task 4: Orders under NUnit

As Task 3 with `--framework nunit`, directory `examples/Orders.ApiTests.NUnit`. NUnit's lifecycle
hook is a `[SetUpFixture]` with `[OneTimeSetUp]` — `init` scaffolds it; fill it in rather than
restructuring it.

Commit message: `examples: Orders under NUnit`

## Task 5: The cross-framework assertion, CI, and docs

**Depends on Tasks 1-4.** Do not start until all four are merged.

- [ ] **Step 1: Diff the generated output across frameworks and record what differs**

For each spec, diff the MSTest example's `Generated/*.g.cs` against its xUnit and NUnit siblings.

The **only** differences may be: the `using`, the class attribute, the method attributes, and the
cancellation-token expression. Anything else — a changed URL-building call, different role gating,
a different fixture lookup — means a template re-derived a planner verdict instead of interpolating
it. **Stop and report; do not edit a generated file to make the diff clean.**

Record the observed diff in the plan's Task 5 section as evidence.

- [ ] **Step 2: CI**

Add a job to `.github/workflows/build-and-test.yml` that, for **every** directory under `examples/`
containing an `intest.json`:

```bash
dotnet tool restore
dotnet intest generate --check     # must exit 0
dotnet build
```

Discover the directories rather than listing them, so a seventh example is covered automatically —
the same anti-vacuity reasoning `ExampleProjectVersionMarkerTests` applies. Fail loudly if zero
directories are discovered.

This job resolves the **published** packages from nuget.org, so it runs the adopter path rather
than the local build. That is deliberate: Golden already covers the local CLI's rendering.

- [ ] **Step 3: Prove the CI job can fail**

Change one committed generated file, confirm `generate --check` exits non-zero and the job would
fail, then revert. A CI job never observed failing is not yet a guard.

- [ ] **Step 4: Docs**

`README.md` and `docs/getting-started.md` reference `examples/` — update them to say all three
frameworks are represented. `CLAUDE.md`'s ownership table and repository description mention the
examples; keep them accurate. State in one place that examples exercise the published packages
while Golden exercises the templates, and point the other mentions at it rather than restating.

- [ ] **Step 5: Commit**

```bash
git commit -m "ci: build and check every example project"
```
