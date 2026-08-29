# xUnit framework pack

**Status:** Design · Revision 1
**Date:** 2026-08-30
**Scope:** xUnit only. NUnit follows the same path afterwards and is explicitly out of scope here —
see §8 for what this design owes it.

**Baseline:** `main` at `038c06b`. **Nothing from PR #8** — that branch is shelved, and this design
takes no dependency on the unified call surface. Where the two would have interacted, §7 says so.

---

## 1. Why, and what already exists

MSTest is **21.7%** of test-framework downloads against xUnit's 47.4% (§18 of the 2026-08-16 spec
records the figures and their limits). A team standardised on xUnit cannot adopt InTest at all, which
§2 names as "the single largest adoption barrier".

This is not a new decision. §17's backlog item 1 is exactly this work, and it prescribes three parts:

> (a) a second Scriban template set — `TemplateRenderer` still hardcodes `mstest-class.scriban`
> regardless of `project.framework`'s value; (b) `project.framework` actually selecting which
> template renders, rather than only being validated against the single supported value; and (c) a
> new adapter package (`InTest.Runtime.xUnit` …) built the same way `InTest.Runtime.MSTest` was.

**The architecture was built for this and the boundary already holds.** `src/InTest.Runtime` is the
neutral package; no file in it may name `Microsoft.VisualStudio.TestTools.UnitTesting`, and that is
compiler-enforced by the absence of a test-framework reference. `InTest.Runtime.MSTest` is 2 files
and **259 lines total** — that is the size of the thing being reproduced, not a rewrite.

Four seams were extracted for precisely this moment and each one lands:

| seam | xUnit equivalent |
|---|---|
| `IRunDiagnostics` (`Note`/`Warn`) | `ITestOutputHelper` + `TestContext.SendDiagnosticMessage` |
| run-settings profile (`string?`) | unchanged — neutral already |
| display name (`string?` to `BeginTest`) | `TestContext.Current.Test?.…` (see §3, one unverified detail) |
| skip (reason `string?`, null = run) | `Assert.Skip(reason)` |

The skip seam is the clearest vindication: the neutral layer returns a reason and the adapter turns
it into a call. MSTest's is `Assert.Inconclusive`; xUnit's is `Assert.Skip`. Nothing in the neutral
layer changes.

---

## 2. Named decisions

### `[v3-only]` — xUnit v3, and v2 is not a fallback

**`xunit.v3` 4.0.0, stable, 41,817,377 downloads, owned by `xunit`** (checked on nuget.org
2026-08-30). Satisfies the dependency policy's no-prerelease rule.

**This is forced, not preferred.** Generated test bodies pass a cancellation token to
`Client.SendAsync` and to every `ApiResponseAssertions` call — five sites in
`mstest-class.scriban` today. xUnit v3 supplies `TestContext.Current.CancellationToken`. **xUnit v2
has no equivalent**, so a v2 pack would have to invent a mechanism, thread it through generated code,
and defend it — for a version whose successor is already stable. If v2 support is ever wanted it is a
separate decision with its own cost, not a variation of this one.

*(Had PR #8 landed, its pull seam would have removed all five token sites from the template and made
this less pointed. It did not, so the token stays in generated code and v3 is what makes that
writable.)*

### `[snapshot-at-call-time]` — never cache `TestContext.Current`

xUnit's own documentation states `TestContext.Current` is a "moment in time" snapshot and must be
used immediately rather than stored. So the xUnit adapter reads it at each use — in the generated
body for the token, and inside the per-test callback for the display name.

This is the same hazard MSTest has from the other direction: MSTest replaces the
`CancellationTokenSource` behind `TestContext.CancellationToken` per test (that is how `[Timeout]`
works), so a cached token goes stale there too. **Two frameworks, two mechanisms, same rule** — which
is a good sign the rule belongs in the design rather than in one adapter's comments.

### `[adapter-mirrors-mstest]` — same shape, same namespace, no new abstraction

`src/InTest.Runtime.xUnit`, built exactly as `InTest.Runtime.MSTest` was:

- depends on `InTest.Runtime` at the **exact same version**, plus `xunit.v3`
- declares its types in `namespace InTest.Runtime` — so an adopter switching frameworks changes a
  `PackageReference` id, never a `using` or a type name
- supplies its own `IRunDiagnostics` implementation against xUnit's output mechanism
- contains a `TestHost` facade and an `ApiTestBase`, mirroring the MSTest pair

**No shared "framework abstraction" layer is introduced.** Two adapters implementing the same four
seams is the design working; a third package sitting between them to hold what they have in common
would be an abstraction invented for its second use, and the seams already are that abstraction.

### `[lifecycle-is-the-real-difference]` — where the two adapters genuinely diverge

The seams cover data. Lifecycle is where the frameworks actually differ, and this is the part with no
existing precedent in the codebase:

| | MSTest | xUnit v3 |
|---|---|---|
| assembly setup | `[AssemblyInitialize]` static method → `TestHost` | `[assembly: AssemblyFixture(typeof(T))]` |
| per-test setup | `[TestInitialize]` → `BeginTest` | constructor, or `IAsyncLifetime.InitializeAsync` |
| per-test teardown | `[TestCleanup]` → `EndTest` | `IAsyncLifetime.DisposeAsync` / `IDisposable` |
| class marker | `[TestClass]` | *(none)* |
| test marker | `[TestMethod]` | `[Fact]` |
| category | `[TestCategory("x")]` | `[Trait("Category", "x")]` |
| description | `[Description("x")]` | `[Fact(DisplayName = "x")]` |

`InitializeAsync`/`DisposeAsync` are genuinely better suited to `BeginTest`/`EndTest` than MSTest's
attributes, because both are async and `InTestRun` work is async. That is a small win, not a reason
to change the MSTest side.

### `[framework-selects-template]` — selection at construction, one template per framework

`TemplateRenderer.cs:10` currently reads:

```csharp
private readonly Template _classTemplate = Template.Parse(LoadEmbedded("mstest-class.scriban"));
```

A field initialiser with the filename baked in. It becomes a constructor-time selection keyed on
`project.framework`, with the framework value reaching `TemplateRenderer` from the already-loaded
config.

**Two templates, not one template with conditionals.** A single `mstest-or-xunit.scriban` branching
on framework inside every block would make both harder to read and would put a third framework's
concerns into the same file again. The templates are ~107 lines each and mostly identical, and that
duplication is the cheaper half of the trade — the same reasoning `[adapter-mirrors-mstest]` applies
to the packages.

**`TemplateEscapingGuardTests` must run against both.** It parses the template source and classifies
each `tc.<name>` by quote parity, mechanically enforcing one of the three text-safety rules
`CLAUDE.md` calls non-negotiable. A second template that the guard does not read is a second template
with no text-safety enforcement at all. **This is the single most important test change in the
work** — it is easy to miss precisely because nothing fails when it is missed.

### `[config-opens-by-one-value]` — `project.framework` accepts `"xunit"`

`ConfigLoader.RequireSupportedFramework` currently refuses anything but the ordinal-exact lowercase
`"mstest"`. It gains `"xunit"` on the same terms. Its doc records that defaulting was considered and
rejected; that reasoning is unchanged and the refusal message keeps naming what *is* supported.

### `[scaffold-per-framework]` — `init` emits a different project

`InitCommand` hardcodes five third-party `PackageReference`s plus `InTest.Runtime.MSTest`
(`InitCommand.cs:470-479`). An xUnit project needs `xunit.v3` and its runner instead of the three
MSTest packages, plus `InTest.Runtime.xUnit`.

It also needs a **different assembly-setup file**: MSTest scaffolds a class with an
`[AssemblyInitialize]` static method delegating to `TestHost`; xUnit needs an `AssemblyFixture` class
plus the assembly-level attribute. This is scaffold content, not generated output — it lands in the
adopter's own tree and they own it thereafter.

---

## 3. The one unverified detail

`ApiTestCore.BeginTest` takes a `string?` display name. MSTest's adapter passes
`TestContext.TestDisplayName`, deliberately not `TestContext.TestName` — `TestName` returns the bare
method name for every `[DataRow]`, so all variations of one operation would share one id.

xUnit's equivalent is reached through `TestContext.Current.Test`, whose signature is confirmed as
`public ITest? Test { get; }` (nullable — the docs are explicit that callers must null-check). **What
is not confirmed is whether the display-name property on `ITest` is `DisplayName` or
`TestDisplayName`.** Two sources disagreed and neither was the `ITest` API page itself.

**This is recorded as unverified rather than guessed.** It is a ten-second check with the package
referenced, and the implementer confirms it against the real assembly before writing the adapter. It
is called out here only so nobody takes a plausible-looking name from this document as established.

---

## 4. The cost that needs a decision: the Golden matrix

`InTest.Golden.Tests` is the only suite proving generated code both **compiles and runs**. It holds
**50 tests** across `CompileVerificationTests` (6 methods), `GeneratedSuiteExecutionTests` (23) and
the golden-file comparison, and it runs in **~3m30s locally** and **~4m18s on CI's Windows runner**.

`CLAUDE.md` is explicit about why that number is a budget and not a fact:

> every `CompileVerificationTests` (and most `GeneratedSuiteExecutionTests`) case shells out to a
> real `dotnet build` (some also `dotnet test`) on a freshly scaffolded temp project, so the suite's
> wall-clock time grows roughly linearly with the number of generated-code shapes under test — it has
> no fixed ceiling the way an in-process suite would.

**A second framework is a second axis on that matrix.** Running every existing shape under xUnit as
well would roughly double the shell-out count. That is an estimate from the structure, not a
measurement — but the direction is not in doubt, and the suite is already the slowest thing in CI.

**`[matrix-stays-representative]` — the proposed answer.** Do not run every shape under both
frameworks. Run under xUnit the shapes whose *rendering* differs — the class and method attributes,
the lifecycle wiring, the skip path, and one auth case — and keep the long tail (hostile spec text,
integer path parameters, the NSwag convention call, query composition) MSTest-only, because those
exercise `TestPlanBuilder` and `TemplateRenderer` logic that is framework-independent by
construction.

**This must be decided explicitly and recorded, not left to whoever writes the tests.** A matrix that
silently doubles is how a 3m30s suite becomes a 12-minute one that people start skipping; a matrix
that silently *doesn't* cover the second framework is how xUnit ships broken. `CLAUDE.md` already
warns that the figure has grown from ~90s to ~3m49s across three corrections — this is the change
most likely to cause the next one, and it should predict its own number rather than discover it.

**Open question for the reviewer:** should the CI `golden` job split per framework so the two run in
parallel rather than in sequence? It is already a separate job from `fast` for exactly this reason.

---

## 5. Release shape

`0.1.0-preview.1` published **two** packages. `0.1.0-preview.2` already had to publish **three** —
`InTest.Runtime.MSTest` has never shipped, and a scaffolded project breaks if it is missing. This
work makes it **four**.

Two things follow, and both are release-blocking rather than nice-to-have:

- **`InTest.Runtime.xUnit` needs to be publishable.** Trusted publishing is scoped to `InTest.*`, so
  a new id under that prefix should be covered — **verify this before the release rather than
  discovering it during one**, since the publish is the step with no undo.
- **`PackageVersionCouplingTests` grows.** Third-party versions are duplicated by design in three
  places — `Directory.Packages.props`, the scaffolded `.csproj` string in `InitCommand.cs`, and the
  hand-written test project in `CompileVerificationTests.cs` — and that guard fails by package name
  with both versions and both files when they disagree. xUnit's packages join that rule. The
  `InTest.Runtime.xUnit` reference itself is checked the *other* way, like the MSTest adapter: it has
  no `Directory.Packages.props` entry because it is InTest's own version, so the guard confirms the
  scaffold interpolates `CliVersion.Current` rather than a literal.

---

## 6. What does not change

Stated because a reader could reasonably expect otherwise:

- **`src/InTest.Runtime`.** If a type there would have to change to support xUnit, it is in the wrong
  layer — §3's practical rule, and `NeutralityTests` plus `pack-and-verify.ps1` both enforce it. This
  design changes nothing in the neutral package. **If implementation finds it must, that is a
  finding worth stopping for, not a small edit.**
- **`Planning/`.** `TestPlanBuilder` decides which operations produce cases, which are skipped, and
  what each parameter resolves to. None of that is framework-aware and none of it becomes so —
  `TestPlan` "must not carry MSTest attribute names" (§9), and it does not.
- **Role gating.** `emits_fixture_lookup = c.Role == CaseRole.Success` is a planner decision the
  template only interpolates. The xUnit template must not re-derive it — re-deriving verdicts the
  plan already carries is, per `CLAUDE.md`, "the recurring defect in this codebase".
- **`project.framework` stays frozen per project** (§5). A suite cannot be migrated in place, and
  nothing here changes that.

---

## 7. Interaction with the shelved work

PR #8 (unified call surface) and its stacked `http-status-code` branch are shelved. Two honest notes
so a future reader is not misled:

- **If #8 is ever revived, the xUnit template pays for it twice.** #8 removes all five
  `TestContext.CancellationToken` sites from the MSTest template and replaces the send/assert
  ceremony with a single call. Whatever the xUnit template emits for those bodies would need the same
  consolidation, and the xUnit adapter would need the pull-seam override.
- **This design does not lean on that.** Nothing here assumes the call surface changes, and none of
  its decisions become wrong if it never does.

---

## 8. What this owes NUnit

Out of scope, but the design should not paint it into a corner:

- NUnit's equivalents are known and unproblematic: `[TestFixture]`/`[Test]`, `[Category]`,
  `[Description]`, `SetUpFixture` with `[OneTimeSetUp]`, and `Assert.Ignore` for the skip seam.
- **The real test of this design is whether NUnit costs a template plus an adapter and nothing
  else.** If adding xUnit requires touching the neutral package, `Planning/`, or the seams, then the
  boundary is not where §3 claims it is — and NUnit would pay the same cost again. That is the
  question to hold the implementation to.
- NUnit's cancellation story is **not researched here** and must not be assumed to mirror xUnit v3's.

---

## 9. Verification

- `CompileVerificationTests` and `GeneratedSuiteExecutionTests` for the xUnit shapes chosen under
  `[matrix-stays-representative]` — these are the only proof generated xUnit code compiles and runs.
- `TemplateEscapingGuardTests` **against both templates** (see `[framework-selects-template]`).
- `NeutralityTests` and `pack-and-verify.ps1` unchanged and still passing — they are what prove the
  neutral package stayed neutral.
- `PackageVersionCouplingTests` extended, and confirmed to fail when a version disagrees.
- A generated xUnit suite run against the live Orders sample, matching the MSTest acceptance run's
  discipline: **assert per-assembly counts and skip reasons, not the total**.

**Do not restate a Golden timing figure in this document.** Read it from `CLAUDE.md` at the time you
run it and pass a timeout well past it. Three copies of that number have gone stale already.
