# Path-item-level parameters — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** read `pathItem.Parameters` and merge it with `operation.Parameters`, closing
[issue #7](https://github.com/Tjayojo/intest/issues/7) — a live correctness bug on the raw-HTTP
path.

**Architecture:** one canonical merge, computed where `pathItem` is already in scope, passed to
every site that reads parameters today rather than re-derived at each.

**Tech Stack:** .NET 10, Microsoft.OpenApi 3.10.2.

---

## The bug, reproduced before planning

Two structurally identical operations differing only in where `id` is declared:

| operation | `id` declared on | fixture created | `generate` |
|---|---|---|---|
| `getGadget` | the operation | `getGadget.json` | exits 1 until filled |
| `getWidget` | the **path item** | **none** | **exits 0** |

Both generate the same call — `InTestUrl.Build("/widgets/{id}", FixtureParameter("getWidget", "id"))`
— but `getWidget`'s fixture was never created and `fixtures repair` reports "Created 1 fixture(s)".

**The severity is the silence.** `generate` exits **0**, so the drift gate that exists to catch a
missing fixture never fires: the project compiles, `generate --check` passes, CI is green, and the
suite fails only against a live API with a fixture-lookup error pointing nowhere near the spec.

## Blast radius — smaller than the issue assumed, measured

The issue warns this "changes committed fixtures, which changes generated output and golden files".
**Checked: no spec under `samples/`, `tests/**/Specs/` or `examples/` declares path-item-level
parameters.** So for this repository's corpus the change is inert — no golden churn, no committed
fixture changes, no example regeneration.

Two consequences:

- Risk is far lower than the issue's scope note implies.
- **New tests cannot lean on existing specs.** Every test below needs a spec that declares the
  shape, which is what stops the new coverage being vacuous.

It remains a behaviour change for an adopter whose spec uses the shape — `fixtures repair` will
start creating an entry that was previously absent. That earns a `CHANGELOG.md` note.

## `[effective-parameters]` — where the merge lives

OpenAPI 3.x: a path-item parameter applies to every operation beneath it, and an operation-level
parameter **overrides** it when **both `name` and `in` match**. Matching on `name` alone is wrong —
`{id}` in `path` and `id` in `query` are different parameters and both may be declared.

`CLAUDE.md` names re-derivation as this codebase's recurring defect, and `TestCasePlan` already
carries verdicts computed elsewhere rather than letting downstream code recompute them. So:

**Compute the effective list once, per operation, where `pathItem` is in scope, and pass it down.**
Do **not** add a `pathItem` argument to six call sites so each can merge again.

`TestPlanBuilder.Build` already iterates `document.Paths` → `pathItem` → `pathItem.Operations`
(lines 63–65), so `pathItem` is in scope at the only place that matters. `FixtureComposer`'s
`NeedsFixture` and `Compose` take an `OpenApiOperation` and must instead receive the effective
parameters.

Read sites to convert — all currently `operation.Parameters`:

| file | lines |
|---|---|
| `Planning/TestPlanBuilder.cs` | 625, 636, 668, 771 |
| `Fixtures/FixtureComposer.cs` | 33, 46 |

Callers of `FixtureComposer.Compose` that also need the merge: `Commands/FixturesRepairCommand.cs:104`
and `Commands/GenerateCommand.cs:830`. Both iterate paths, so `pathItem` is reachable there too —
confirm before changing, and if either does not have it in scope, **stop and report** rather than
threading a parameter through unrelated layers.

---

## Task 1: The merge, and every site that reads parameters

**Files:**
- Create: `src/InTest.Cli/Planning/EffectiveParameters.cs` (or the nearest existing home — see Step 1)
- Modify: `src/InTest.Cli/Planning/TestPlanBuilder.cs`, `src/InTest.Cli/Fixtures/FixtureComposer.cs`,
  `src/InTest.Cli/Commands/FixturesRepairCommand.cs`, `src/InTest.Cli/Commands/GenerateCommand.cs`
- Test: `tests/InTest.Cli.Tests/` — the suites already covering these types

- [ ] **Step 1: Decide the home, then write the failing tests**

Before creating a new file, check whether an existing type is the natural owner (`TestPlanBuilder`
has private helpers; `FixtureComposer` is static). Prefer an existing home if one fits; a new
single-purpose static class is fine if not. Say which you chose and why.

Write tests first, against a spec fixture declaring the shape:

- a path-item parameter alone is merged in
- an operation-level parameter **overrides** a path-item one with the same `name` **and** `in`
- a path-item `{id}` in `path` and an operation `id` in `query` **both** survive — they are
  different parameters
- ordering is deterministic (this codebase pins generated output byte-for-byte; an unstable order
  would churn golden files)

- [ ] **Step 2: Run them, confirm they fail**

- [ ] **Step 3: Implement the merge and convert the read sites**

Operation entries win on `(name, in)`. Everything else keeps its current behaviour exactly.

- [ ] **Step 4: Verify**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests
dotnet test tests/InTest.Architecture.Tests
dotnet test tests/InTest.Runtime.Tests
```

**Do NOT run `InTest.Golden.Tests`** here — over 7 minutes, and the Bash tool caps at 600s so it
reads as a hang. Task 3 owns it.

- [ ] **Step 5: Reproduce the original bug, and confirm it is gone**

Use the repro spec from Task 2's fixture. Before: `fixtures repair` creates nothing for the
path-item operation and `generate` exits 0. After: the fixture is created and `generate` behaves
exactly as it does for the operation-level twin.

Paste both outputs into your report. **This is the acceptance criterion for the whole task** — the
unit tests prove the merge, this proves the bug is fixed.

- [ ] **Step 6: Commit**

```bash
git commit -m "fix: merge path-item-level parameters into every operation that inherits them"
```

## Task 2: The typed-client gate stops firing

**Depends on Task 1.**

`4a864c6` made the typed-client path fail closed for exactly this shape: a placeholder with no
matching `in: path` entry resolves to an untypable kind, convention derivation is withheld, and a
note naming `[path-item-parameters]` lands in `coverage-report.json`.

That gate stood in for the fix. With the fix in, it must stop firing for these specs.

- [ ] **Step 1:** Find the gate and its note text (`Planning/ClientCallPlanner.cs`, around lines
  222 and 291, plus wherever the note string lives).
- [ ] **Step 2:** With a client-configured project on a path-item spec, confirm convention
  derivation now succeeds and the note is **absent** from `coverage-report.json`.
- [ ] **Step 3:** Decide, and justify in your report: does the gate's *code* stay as a guard for
  genuinely undeclared parameters, or is it now dead? **Do not delete it just because the note
  stopped appearing** — the comment at `TestPlanBuilder.cs:672-689` distinguishes "no schema, but
  this method is the parameter's operation" from "this method never saw the declaration". The first
  case still exists. If it stays, update its comment, which currently says the fix is out of scope.
- [ ] **Step 4:** Commit.

## Task 3: Golden coverage

**Depends on Task 1.** Owns `tests/InTest.Golden.Tests/` exclusively.

- [ ] **Step 1:** Add a spec under the Golden suite's `Specs/` declaring a path-item parameter
  inherited by at least two operations (a `GET` and a `DELETE` on `/things/{id}` is the shape the
  issue names as common).
- [ ] **Step 2:** Add a case that **compiles and runs** the generated project against the
  in-process stub — not just a rendering assertion. The bug was a runtime failure; a golden-file
  comparison alone would not have caught it.
- [ ] **Step 3:** Run the full Golden suite. It takes **7m37s+**; the Bash tool caps at 600s, so run
  it in the **background** and collect the result rather than raising the timeout.
- [ ] **Step 4:** Confirm the three existing golden expectation files are **unchanged** — no
  current spec uses path-item parameters, so a diff there means the merge altered behaviour for
  specs that do not declare the shape, which is a defect. **Stop and report if they moved.**
- [ ] **Step 5:** Commit.

## Task 4: Changelog and docs

**Depends on Tasks 1–3.**

- [ ] **Step 1:** `CHANGELOG.md` — a `Fixed` entry under `Unreleased`. State the adopter-visible
  behaviour change plainly: on a spec using path-item parameters, `fixtures repair` now creates an
  entry it previously omitted, and a suite that failed at runtime now works. Name the silent-exit-0
  symptom so someone recognises it.
- [ ] **Step 2:** Update the `[path-item-parameters]` comments at `TestPlanBuilder.cs:672-689` and
  `ClientCallPlanner.cs:222,291`. All three currently assert that nothing reads
  `pathItem.Parameters`. That becomes false.
- [ ] **Step 3:** Check `docs/getting-started.md` and `README.md` for any claim this changes.
- [ ] **Step 4:** Commit.
