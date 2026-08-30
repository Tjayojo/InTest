# xUnit framework pack

**Status:** Design · Revision 2
**Date:** 2026-08-30
**Scope:** xUnit only. NUnit follows the same path afterwards and is explicitly out of scope here —
see §8 for what this design owes it.

**Baseline:** `main` at `038c06b`. **Nothing from PR #8** — that branch is shelved, and this design
takes no dependency on the unified call surface. Where the two would have interacted, §7 says so.

**Revision note — rev 2.** First review, and the reviewer **built a working `InTest.Runtime.xUnit`
in a clone and compiled it** rather than reasoning about it. The headline is that rev 1's central
architectural claim survived that test: an adapter over `ApiTestCore` compiled against the neutral
package **with zero edits** to `src/InTest.Runtime`, `Planning/`, or the four seams, and reflection
over all 40 exported neutral types found no name collisions with `namespace Xunit`. §6 stands.

Everything *around* that claim was wrong in ways that would have produced broken work. Rev 1
proposed a dependency that cannot be referenced, an `IRunDiagnostics` mapping whose sinks are silent
or null at the scope they are needed, and a test matrix on a harness that cannot execute an xUnit v3
project at all. It also restated Golden timing figures in the same document whose §9 forbids exactly
that. All are corrected below, and every xUnit API claim now carries how it was established.

---

## 1. Why, and what already exists

MSTest is **21.7%** of test-framework downloads against xUnit's 47.4% (§18 of the 2026-08-16 spec
records the figures and their limits). A team standardised on xUnit cannot adopt InTest at all, which
§2 names as "the single largest adoption barrier".

**That headline figure does not by itself justify this design, and rev 1 let it appear to.** §18
decomposes the 47.4% as `xunit` (v2) 1,004,273,473 plus `xunit.v3` 38,574,301. **v3 is roughly 1.8%
of the market the adoption-barrier argument invokes; about 96% of xUnit's share is v2.** `[v3-only]`
is still the right call and §2 argues it on its own terms — but the reach argument and the version
argument point at different populations, and this document must not blur them. The honest statement:
shipping v3-only reaches the smaller, growing half of xUnit today, and is the only version this
codebase can support without inventing a cancellation mechanism.

This is not a new decision. §17's backlog item 1 is exactly this work, and it prescribes three parts:

> (a) a second Scriban template set — `TemplateRenderer` still hardcodes `mstest-class.scriban`
> regardless of `project.framework`'s value; (b) `project.framework` actually selecting which
> template renders, rather than only being validated against the single supported value; and (c) a
> new adapter package (`InTest.Runtime.xUnit` …) built the same way `InTest.Runtime.MSTest` was.

**The boundary holds, and that is now measured rather than asserted.** `src/InTest.Runtime` is the
neutral package; no file in it may name `Microsoft.VisualStudio.TestTools.UnitTesting`, and that is
compiler-enforced by the absence of a test-framework reference. `InTest.Runtime.MSTest` is 2 files
and **259 lines** — the size of the thing being reproduced, not a rewrite.

Three of the four extracted seams land cleanly. **The fourth does not, and rev 1 got it wrong:**

| seam | xUnit equivalent | how established |
|---|---|---|
| run-settings profile (`string?`) | type unchanged, **but the capability is lost** — see `[profile-loses-its-first-source]` | read `InTestRun.ResolveProfile` |
| display name (`string?` to `BeginTest`) | `TestContext.Current.Test?.TestDisplayName` | reflection + passing runtime assertion |
| skip (reason `string?`, null = run) | `Assert.Skip(reason)` | compiled and run; trx outcome `NotExecuted` |
| `IRunDiagnostics` (`Note`/`Warn`) | **not** `SendDiagnosticMessage` — see `[warn-needs-a-real-sink]` | measured, all candidates probed |

---

## 2. Named decisions

### `[v3-only]` — xUnit v3, and the adapter references two packages, not one

**Rev 1 named `xunit.v3` as the dependency. A library cannot reference it.** Measured:

```
xunit.v3.core.mtp-v2.targets(15,5): error : xUnit.net v3 test projects must be executable
(set project property '<OutputType>Exe</OutputType>'). If this is not a test project,
reference xunit.v3.extensibility.core instead.
```

`InTest.Runtime.xUnit` is a library, so it references **`xunit.v3.extensibility.core`** and
**`xunit.v3.assert`** — the second because `Assert.Skip` is otherwise `error CS0103: The name
'Assert' does not exist in the current context`. Both are **4.0.0 stable**, owned by `xunit`
(nuget.org, 2026-08-30: extensibility.core 59,956,404 downloads; assert 48,986,896).

**The dependency policy must be run against those two**, not against `xunit.v3` — rev 1 checked
metadata for a package the shipped artifact never references. `xunit.v3` is still the right
reference for a *generated adopter project*, which is executable.

**v3 rather than v2 is forced, not preferred.** Generated bodies pass a cancellation token to five
sites in `mstest-class.scriban` (lines 93, 96, 107, 113, 116). v3 supplies
`TestContext.Current.CancellationToken`; **v2 has no equivalent**, so a v2 pack would have to invent
a mechanism, thread it through generated code, and defend it — for a version whose successor is
already stable. If v2 is ever wanted it is a separate decision with its own cost.

### `[warn-needs-a-real-sink]` — the diagnostics mapping rev 1 proposed is silent

`IRunDiagnostics.Warn` is contractually *"Must reach the operator even when the run passes and exits
0"*. Rev 1 mapped it to `TestContext.SendDiagnosticMessage`. Measured against real xunit.v3 4.0.0,
every part of that mapping fails:

| candidate | measured behaviour |
|---|---|
| `SendDiagnosticMessage` | prints **nothing** by default; needs `-diagnostics` on the command line |
| `TestContext.Current.TestOutputHelper` at assembly scope | **null** (`pipelineStage=TestAssemblyExecution; outputHelperNull=True`) |
| `AddWarning` at assembly scope | explicitly refused: *"Attempted to log a test warning message while not running a test"* |
| `Console.WriteLine` | **reached the operator on a passing default run** |

So the mapping is **`Console.WriteLine` at assembly scope, and `AddWarning` / `ITestOutputHelper`
inside a running test** — and the adapter must know which scope it is in. A sink requiring an
operator to remember `-diagnostics` does not satisfy "must reach the operator".

**An existing test guards this, which is why it matters beyond correctness.**
`GeneratedSuiteExecutionTests.ValidationReportWithAProblemSurfacesOnAPassingRun` (`:1574-1577`)
asserts the aggregated report reaches process output on a passing run. Rev 1's mapping fails it;
`AddWarning` alone also fails it. The MSTest side records evidence for its own
`DisplayMessage(MessageLevel.Warning, …)` choice; the xUnit side owes the same record.

### `[harness-port-comes-first]` — the Golden suite cannot run an xUnit v3 project today

**Rev 1 treated §4 as a matrix-sizing question. It is a harness port, and the port must be costed
before the matrix question is even answerable.**

Every `GeneratedSuiteExecutionTests` shell-out uses `--logger "trx;LogFileName=results.trx"`
(`:512`, `:583`, `:640`, `:734`, `:803`). On the .NET 10 SDK against xunit.v3 4.0.0 that does not
degrade — it errors:

```
error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
on .NET 10 SDK and later.
```

The measured working recipe needs four changes together: `<OutputType>Exe</OutputType>`; a
`global.json` carrying `{"test":{"runner":"Microsoft.Testing.Platform"}}`; a
`Microsoft.Testing.Extensions.TrxReport` reference; and `dotnet test <proj> -- --report-trx
--report-trx-filename r.trx`. Running the built executable directly with `-result-trx <file>` also
works and needs no opt-in — **weigh those two**, because the second avoids the `global.json`
entirely.

`scripts/ci/assert-trx-results.ps1` and `scripts/local-e2e-test.ps1` rest on the same VSTest
assumption and are in scope.

### `[snapshot-at-call-time]` — never cache `TestContext.Current`

xUnit's documentation states `TestContext.Current` is a "moment in time" snapshot to be used
immediately, not stored. The adapter reads it at each use.

This is the same hazard MSTest has from the other direction: MSTest replaces the
`CancellationTokenSource` behind `TestContext.CancellationToken` per test (that is how `[Timeout]`
works), so a cached token goes stale there too. **Two frameworks, two mechanisms, same rule.**

**Its static type is `ITestContext`, not `TestContext`** — adapter fields must be typed accordingly.
Rev 1 implied otherwise; established by reflection.

### `[adapter-mirrors-mstest]` — same namespace, and that decision costs a fifth test project

`src/InTest.Runtime.xUnit`, built as `InTest.Runtime.MSTest` was: depending on `InTest.Runtime` at
the exact same version plus the two xUnit packages above, declaring its types in **`namespace
InTest.Runtime`** so an adopter switching frameworks changes a `PackageReference` id and never a
`using` or a type name.

**That decision has a consequence rev 1 did not state.** Same namespace plus same type names means
the two adapters cannot coexist in one compilation. Measured, after adding both references to
`tests/InTest.Runtime.Tests`:

```
error CS0433: The type 'ApiTestBase' exists in both 'InTest.Runtime.MSTest' and 'InTest.Runtime.xUnit'
error CS0433: The type 'TestHost' exists in both …
```

(12+ occurrences.) So the xUnit adapter's internals need a **new, separate, xUnit-based test
project** — a fifth suite, with knock-on changes to CI's `fast` job and `assert-trx-results.ps1`.
That is the real price of the same-namespace choice; the choice is still right, and the price belongs
here rather than in an implementer's surprise.

**No shared "framework abstraction" layer is introduced.** Two adapters implementing the same seams
is the design working; a third package between them would be an abstraction invented for its second
use, and the seams already are that abstraction.

**Open: whether `TestHost` should mirror at all.** MSTest's `TestHost` exists *specifically* to adapt
`TestContext` into `InTestRun` — a job that evaporates under xUnit, where the assembly fixture object
is itself the lifecycle hook and `TestContext.Current` is ambient. The reviewer's working adapter saw
`TestHost` degrade to a near-empty passthrough. An `IAsyncLifetime` assembly-fixture base class may
be the more honest shape. **Decide before implementation; do not mirror by default.**

### `[lifecycle-is-the-real-difference]` — where the two adapters genuinely diverge

| | MSTest | xUnit v3 |
|---|---|---|
| assembly setup | `[AssemblyInitialize]` static → `TestHost` | `[assembly: AssemblyFixture(typeof(T))]`; a generic `AssemblyFixtureAttribute<T>` also exists |
| per-test setup | `[TestInitialize]` → `BeginTest` | constructor, or `IAsyncLifetime.InitializeAsync` |
| per-test teardown | `[TestCleanup]` → `EndTest` | `IAsyncDisposable.DisposeAsync` — **not** a member of `IAsyncLifetime`, which declares only `ValueTask InitializeAsync()` and inherits `IAsyncDisposable` (rev 1 stated the v2 shape) |
| class marker | `[TestClass]` | *(none)* |
| test marker | `[TestMethod]` | `[Fact]` |
| category | `[TestCategory("x")]` | `[Trait("Category", "x")]` |
| description | `[Description("x")]` | `[Fact(DisplayName = "x")]` — **see `[display-name-is-not-metadata]`** |
| **parallelism** | `[assembly: DoNotParallelize]` plus an MSBuild guard target | **parallel by default** — see `[scaffold-per-framework]` |

### `[display-name-is-not-metadata]` — `[Description]` and `DisplayName` are not equivalent

Two rows of that table collide, and rev 1 did not notice. In MSTest, `[Description]` is orthogonal
metadata: measured against MSTest 4.3.3 on .NET 10, a case carrying
`[Description("Given Orders, when getOrderById, then 200")]` still reports
`TestDisplayName=[GetOrderById_Contract]`. In xUnit, `DisplayName` **is** the display name —
`TestContext.Current.Test?.TestDisplayName` returns it verbatim.

`ApiTestCore.BeginTest` feeds that string to `InTestId.ForTest`, which slugs it into the `TestId`
**that travels in an HTTP header**. So the same operation's correlation id would differ between
frameworks: `…-getorderbyid-contract` under MSTest versus
`…-given-orders-when-getorderbyid-then-200` under xUnit.

**This is a wire-visible cross-framework divergence and must be decided deliberately**, not fall out
of an attribute mapping. Two defensible answers: emit `[Fact]` with no `DisplayName` and carry the
description in a `[Trait]`, keeping ids aligned; or accept the divergence and document it. Either is
fine; silence is not.

### `[framework-selects-template]` — selection at construction, one template per framework

`TemplateRenderer.cs:10` currently reads:

```csharp
private readonly Template _classTemplate = Template.Parse(LoadEmbedded("mstest-class.scriban"));
```

A field initialiser with the filename baked in. It becomes a constructor-time selection keyed on
`project.framework`, with the value reaching `TemplateRenderer` from the already-loaded config.

**Two templates, not one with conditionals.** A single template branching on framework inside every
block would be harder to read and would put a third framework's concerns back into one file. The
MSTest template is **121 lines** and the xUnit one will be mostly identical; that duplication is the
cheaper half of the trade.

**`TemplateEscapingGuardTests` must run against both.** It parses the template source and classifies
each `tc.<name>` by quote parity (`:96`, over `LoadEmbeddedTemplate("mstest-class.scriban")`),
mechanically enforcing one of the three text-safety rules `CLAUDE.md` calls non-negotiable. A second
template the guard does not read has **no text-safety enforcement at all**. This is the single most
important test change in the work, and it is easy to miss precisely because nothing fails when it is
missed.

### `[config-opens-by-one-value]` — `project.framework` accepts `"xunit"`

`ConfigLoader.RequireSupportedFramework` refuses anything but the ordinal-exact lowercase `"mstest"`.
It gains `"xunit"` on the same terms. Its doc records that defaulting was considered and rejected;
that reasoning is unchanged.

### `[scaffold-per-framework]` — `init` emits a materially different project

`init` writes 11 files (`InitCommand.cs:410-637`). Rev 1 named only the package references and the
assembly-setup file. The real set:

- **`<OutputType>Exe</OutputType>`** — a hard build error without it.
- **Packages** — `xunit.v3` and a runner in place of the three MSTest packages, plus
  `InTest.Runtime.xUnit`.
- **Assembly setup** — an `AssemblyFixture` class plus the assembly-level attribute, replacing the
  `[AssemblyInitialize]` static method.
- **Parallelism, and this one is a correctness issue.** The MSTest scaffold pins
  `[assembly: DoNotParallelize]` (`InitCommand.cs:509`) plus an MSBuild guard target (`:495-499`).
  **xUnit v3 parallelizes by default** — measured banner `parallel mode = collections [22 threads]`,
  and a two-class probe reached `MAXCONCURRENT=2`. A scaffolded xUnit suite would run concurrently
  **against a deployed API**, silently reversing a deliberate decision that §11 and §106 both rest
  on. The analogue is `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, and
  `[Fact(DisableParallelization = true)]` exists in 4.0.0 to carry `tc.mutates`.

### `[profile-loses-its-first-source]` — the run-settings profile has no xUnit equivalent

The MSTest scaffold writes `{projectName}.runsettings` (`:623-634`) carrying
`<TestRunParameters><Parameter name="profile" …>`, read by `TestHost.ProfileFromRunSettings` and
ranked **first** in `InTestRun.ResolveProfile` (`InTestRun.cs:586-590`: runsettings →
`INTEST_PROFILE` → config default → `"local"`).

xUnit v3 / Microsoft.Testing.Platform has no runsettings equivalent, so the xUnit adapter passes
`null` and **the highest-precedence profile mechanism does not exist for xUnit adopters.** Rev 1's
seam table called this "unchanged — neutral already", which is true of the *type* and misleading
about the *capability*. `INTEST_PROFILE` becomes the primary mechanism there, and
`docs/getting-started.md` must say so for xUnit projects rather than leaving an adopter to discover
that a documented switch does nothing.

---

## 3. xUnit API claims and how each was established

Rev 1 flagged one unknown. The review resolved it and found five more that should have carried the
same marking. Every claim this document relies on:

| claim | status |
|---|---|
| `ITest`'s display name is **`TestDisplayName`** (on `Xunit.Sdk.ITestMetadata`, asm `xunit.v3.common`) | **verified** — reflection + passing runtime assertion |
| `TestContext.Current` returns **`ITestContext`** | **verified** — reflection |
| `ITest? Test { get; }` is nullable | **verified** — `NullabilityInfoContext` |
| `IAsyncLifetime` declares only `InitializeAsync`; inherits `IAsyncDisposable` | **verified** |
| `Assert.Skip` exists; trx outcome `NotExecuted`; **reason lands in `<StdOut>`, not `<Message>`** | **verified** — relevant to §9's "assert skip reasons" |
| `AssemblyFixtureAttribute` and generic `AssemblyFixtureAttribute<T>` exist; work with `IAsyncLifetime`; no collision with the neutral `InTest.Runtime.IAssemblyFixture` | **verified** |
| `ITestOutputHelper` exists in `Xunit` but is **null outside a running test** | **verified** |
| `TestContext.Current.CancellationToken` | **verified** — compiled and run |
| `xunit.v3` download count | **unverified offline** — §18 recorded 38,574,301 on 2026-08-17 |

---

## 4. The Golden matrix

**Read `[harness-port-comes-first]` before this section.** Sizing the matrix is meaningless until the
harness can execute one xUnit project.

`InTest.Golden.Tests` is the only suite proving generated code both **compiles and runs**. It holds
**50 tests**, but they do not all scale with a framework axis, and rev 1's breakdown was wrong:
`CompileVerificationTests` (6) and `GeneratedSuiteExecutionTests` (23) shell out to real `dotnet
build` / `dotnet test`; `CliExitCodeTests` (14), `MSBuildEvaluationTests` (2) and
`ScaffoldCompileVerificationTests` (1) do not. **Only the first 29 grow.**

`CLAUDE.md` is explicit that this is a budget, not a fact — wall-clock grows roughly linearly with
the number of generated-code shapes and has no fixed ceiling. **Take the current figures from
`CLAUDE.md` when you plan this; do not copy them here.** Rev 1 restated two, violating this
document's own §9, and one of them matched nothing in `CLAUDE.md` at all.

**`[matrix-stays-representative]` — the answer, with rev 1's subset corrected.** Run under xUnit the
shapes whose *rendering* or *runtime behaviour* differs; keep the framework-independent long tail
MSTest-only. Rev 1 named "class and method attributes, lifecycle wiring, the skip path, and one auth
case" and was wrong by three:

- **A client-routed case.** `mstest-class.scriban:84-96` is a distinct body shape — `ApiClient<T>()`,
  two `catch … when` filters, `ShouldMatchCapturedContractAsync` — and carries 2 of the 5
  `TestContext.CancellationToken` sites. Those sites are the *entire justification* for `[v3-only]`;
  omitting them would mean never compiling the thing the version decision was made for.
- **`ValidationReportWithAProblemSurfacesOnAPassingRun`.** The only test proving the `Warn` contract
  — exactly what `[warn-needs-a-real-sink]` shows the obvious mapping breaks.
- **Hostile spec text.** The *escaping* is framework-independent (`CSharpLiteral`), but under xUnit
  hostile text lands in `[Fact(DisplayName = …)]`, and per `[display-name-is-not-metadata]` that
  value flows into an HTTP header via `InTestId`. `Slugify` very likely contains it — but the blast
  radius genuinely differs between frameworks, so "framework-independent by construction" is the
  wrong reason to omit it.

Genuinely safe to keep MSTest-only: integer path parameters, the NSwag convention call, query
composition — all `TestPlanBuilder` / `TemplateRenderer` logic.

**CI: split the `golden` job per framework** so the two run in parallel. It is already separate from
`fast` for this reason, and a single job running both sequentially is the shape that produces the
next stale timing figure.

---

## 5. Release shape

`0.1.0-preview.1` published **two** packages, and it is the only tag that exists — rev 1's
past-tense claim that preview.2 "already had to publish three" was wrong. The **next** release must
publish `InTest.Runtime.MSTest` for the first time (a scaffolded project breaks without it), and this
work makes that **four**.

Three consequences, all release-blocking:

- **Trusted publishing already covers a new id, and rev 1 gave the wrong reason.** `release.yml:13-18`
  records that the policy binds a *package owner*, not a package ID — *"There is no package-ID
  field"* — which is why it works for unclaimed ids. Rev 1's "scoped to `InTest.*`" would send an
  implementer to re-verify a settled question. **The real unchecked risk is different:**
  `CONTRIBUTING.md` records ID-prefix reservation as a human step nobody has performed, so the
  `InTest.` prefix is **unreserved** and `InTest.Runtime.xUnit` could be squatted before first push.
- **`pack-and-verify.ps1` packs three projects by explicit path** and hardcodes an
  `MSTest.TestFramework` positive control (`:386-391`). `release.yml:206` and `pack.yml` name the
  three packages explicitly. A fourth shipped package none of them knows about **ships unverified**.
- **`PackageVersionCouplingTests` grows.** Third-party versions are duplicated by design across
  `Directory.Packages.props`, the scaffolded `.csproj` string in `InitCommand.cs`, and the
  hand-written project in `CompileVerificationTests.cs`. xUnit's packages join that rule. The
  `InTest.Runtime.xUnit` reference itself is checked the other way, like the MSTest adapter — no
  `Directory.Packages.props` entry, so the guard confirms the scaffold interpolates
  `CliVersion.Current` rather than a literal.

---

## 6. What does not change — and this is now measured

The reviewer built a real adapter over `ApiTestCore` and compiled it against the neutral package
**with zero edits** to any of the following. Reflection over all 40 exported neutral types found no
collision with `namespace Xunit`.

- **`src/InTest.Runtime`.** If a type there would have to change to support xUnit, it is in the wrong
  layer — §3's practical rule, enforced by `NeutralityTests` and `pack-and-verify.ps1`.
- **`Planning/`.** `TestPlan` "must not carry MSTest attribute names" (§9), and it does not.
- **Role gating.** `emits_fixture_lookup = c.Role == CaseRole.Success` (`TemplateRenderer.cs:145`) is
  a planner decision the template only interpolates. The xUnit template must not re-derive it —
  re-deriving verdicts the plan already carries is, per `CLAUDE.md`, "the recurring defect in this
  codebase".
- **`project.framework` stays frozen per project** (§5).

**One correction to rev 1's version of this section.** It listed `NeutralityTests` and
`pack-and-verify.ps1` as "unchanged and still passing". They will pass — **vacuously**.
`NeutralityTests.AdapterPackageDeclaresItsTestFramework` (`:229-260`) hardcodes the
`InTest.Runtime.MSTest` csproj path, so nothing would check that `InTest.Runtime.xUnit` is genuinely
an adapter for the neutral project. Both need extending, and "still passing" was the wrong thing to
promise.

---

## 7. Interaction with the shelved work

PR #8 (unified call surface) and its stacked `http-status-code` branch are shelved.

- **If #8 is ever revived, the xUnit template pays for it twice.** #8 removes all five
  `TestContext.CancellationToken` sites and replaces the send/assert ceremony with a single call. The
  xUnit template would need the same consolidation and the adapter a pull-seam override.
- **This design does not lean on that.** No decision here becomes wrong if #8 never returns.

---

## 8. What this owes NUnit

Out of scope, but the design should not paint it into a corner:

- NUnit's equivalents are known: `[TestFixture]`/`[Test]`, `[Category]`, `[Description]`,
  `SetUpFixture` with `[OneTimeSetUp]`, `Assert.Ignore` for the skip seam.
- **The real test of this design is whether NUnit costs a template plus an adapter and nothing
  else.** xUnit has now proven the *neutral* boundary holds. What it also proved is that the
  surrounding scaffolding — harness, packaging scripts, parallelism defaults, a per-adapter test
  project — is where a second framework actually costs. NUnit will pay that part again, and the
  implementer should build those seams once rather than twice.
- NUnit's cancellation and parallelism defaults are **not researched here** and must not be assumed
  to mirror xUnit v3's.

---

## 9. Verification

- The harness port (`[harness-port-comes-first]`) proven by one generated xUnit project building and
  running under it — before any matrix work.
- `CompileVerificationTests` and `GeneratedSuiteExecutionTests` for the corrected xUnit subset in §4.
- `TemplateEscapingGuardTests` **against both templates**.
- `NeutralityTests` and `pack-and-verify.ps1` **extended** to cover the fourth package, and confirmed
  to fail if it is not an adapter / not packed.
- `PackageVersionCouplingTests` extended, and confirmed to fail when a version disagrees.
- A generated xUnit suite against the live Orders sample, matching the MSTest acceptance run's
  discipline: **assert per-assembly counts and skip reasons, not the total** — noting that xUnit puts
  a skip reason in `<StdOut>` rather than `<Message>`.

**Do not restate a Golden timing figure in this document.** Read it from `CLAUDE.md` when you run it
and pass a timeout well past it. Rev 1 restated two and created the fourth stale copy this rule
exists to prevent.
