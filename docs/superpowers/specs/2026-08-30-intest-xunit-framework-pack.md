# xUnit framework pack

**Status:** Design · Revision 4
**Date:** 2026-08-30
**Scope:** xUnit only. NUnit follows the same path afterwards and is explicitly out of scope here —
see §8 for what this design owes it.

**Baseline:** `main` at `038c06b`. **Nothing from PR #8** — that branch is shelved, and this design
takes no dependency on the unified call surface. Where the two would have interacted, §7 says so.

**Revision note — rev 4.** Third review, again by building — a third independent adapter, and
fourteen of the document's "verified/measured" claims re-established, **all of which reproduced**,
most character-for-character. The labels are trustworthy. Every defect this round was in something
stated **flatly and unlabelled, sitting next to a correct measurement** — which is the failure mode
a document like this has left once its measured claims are sound.

- **The parallelism attribute rev 3 prescribed does not compile.**
  `CollectionBehavior(DisableTestParallelization = true)` is obsolete-**as-error** in 4.0.0. This was
  the document's own single named "correctness issue", and its fix did not build.
- **§4's matrix omitted `GoldenFileTests` entirely** — and with it the second golden expectation file
  a second template requires.
- Four smaller overreaches corrected, and `[frozen-axis-becomes-reachable]` added: opening
  `project.framework` makes a frozen axis reachable that **nothing in the CLI enforces**, and rev 3
  concluded there was no work there.

**Revision note — rev 3.** Second review, again by building rather than reading. **Rev 2's
claim-labelling held** — every §3 entry the reviewer probed reproduced, several character-for-character,
and §6's architectural claim survived a second independent build. The reviewer also ported the golden
expectation file mechanically and compiled it clean against a real adapter, so "a template set plus an
adapter" is now measured twice by two people.

The defects clustered in exactly the two sections a plan's first tasks would be written from:

- **The harness recipe rev 2 called "measured working" could not be reproduced** — it produces
  `Zero tests ran`, exit 5. The one-sentence *aside* is the path that works. Rev 2's emphasis was
  inverted, and would have put task 1 on the failing path. `[harness-port-comes-first]` is rewritten.
- **The port was attributed to the wrong cause and is roughly four times larger** than rev 2 said.
- **§4 excluded the one test that compiles the raw scaffold**, so nothing would ever have compiled the
  xUnit scaffold at all.
- **Nothing said how a user selects xUnit.** That is public CLI surface under §3's semver rule, not a
  plan task — `[framework-is-an-init-flag]` now decides it.

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

**The policy outcome, recorded rather than assigned** (checked 2026-08-30): all three packages are
**Apache-2.0, listed, with no deprecation notice and no vulnerability advisories**, published
2026-08-15, owner `xunit`. `CONTRIBUTING.md` names deprecation and vulnerability metadata as the
specific thing to check, so the answer belongs here rather than an instruction to go and look.

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
| `AddWarning` at assembly scope | refused, and **silently** — the call returns without throwing, and the message *"Attempted to log a test warning message while not running a test (pipeline stage = TestAssemblyExecution)"* only appears under `-diagnostics`. An implementer reading "refused" as "throws" would write a guard that never fires |
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

**This is a harness port, and it must be costed before the matrix question in §4 is even
answerable.**

**The cause is `dotnet test`'s VSTest default, not a logger flag.** Rev 2 blamed
`--logger "trx;LogFileName=results.trx"` and named five call sites. Measured: plain
`dotnet test "$_root" --no-build --nologo`, with no logger argument at all, produces the identical
error on an xunit.v3 4.0.0 project:

```
error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
on .NET 10 SDK and later.
```

So this hits **every** `dotnet test` shell-out in `GeneratedSuiteExecutionTests`, not the five that
happen to pass a logger.

**Use the built executable directly. Do not use `dotnet test`.**

```
./bin/Debug/net10.0/<project>.exe -result-trx out.trx
```

Verified: writes a real trx, reports `NotExecuted` for a skipped case with the reason in `<StdOut>`,
exits 1 on failure and 0 on success. **No `global.json`, no extra package, no opt-in.**

**Rev 2 prescribed the opposite and it does not work.** Its four-part recipe —
`<OutputType>Exe</OutputType>` plus a `global.json` carrying
`{"test":{"runner":"Microsoft.Testing.Platform"}}` plus a `Microsoft.Testing.Extensions.TrxReport`
reference plus `dotnet test <proj> -- --report-trx --report-trx-filename r.trx` — produces
`Zero tests ran`, exit code 5. Reproduced on SDK **10.0.400, 10.0.303 and 10.0.111**, with
`--project`, and with `TestingPlatformDotnetTestSupport=true`.

**One qualification, because it changes what an implementer should conclude.** An MSTest 4.3.3
control project with `EnableMSTestRunner` under the same `global.json` *also* reports
`Zero tests ran`. So what is broken is `dotnet test`'s Microsoft.Testing.Platform handshake **on this
machine**, not anything xUnit-specific — plain VSTest `dotnet test` works here fine. That recipe may
well work elsewhere. It does not matter: the direct-exe path works everywhere and needs no opt-in, so
there is no reason to depend on the one that is environment-sensitive. **Do not spend implementation
time trying to make `dotnet test` work.**

**The port is roughly four times larger than rev 2 implied.** Three things change, not one:

- **Every** `dotnet test` shell-out becomes a direct-exe invocation, not the five with loggers.
- **Twelve call sites pass `--filter "FullyQualifiedName~…"`.** The direct runner rejects it
  (`error: unknown option: --filter`); the equivalent is `-filterVSTest "<same query>"`, verified
  working. Mechanical, but nothing would have told an implementer to look.
- **Console output format changes wholesale**, so every `test.Output.ShouldContain(...)` assertion
  needs re-checking against real xUnit runner output rather than assumed to survive.

`scripts/ci/assert-trx-results.ps1` rests on the same VSTest assumption (`:14`, `:22` parse
`dotnet test --logger trx` output) and is in scope.

**`scripts/local-e2e-test.ps1` does not** — rev 3 named it and was wrong. It runs `dotnet build` on
the scaffold (`:431`) and its own header states three times that `dotnet test` is *"Deliberately out
of scope"* (`:61`, `:76`, `:81`). It still needs changes for the fourth package and `--framework`,
but not this one.

**A fourth thing changes, and it is not optional.** A solution containing both an MSTest project and
an xunit.v3 project fails `dotnet test <sln>` with exit 1 — the xUnit project errors on the VSTest
target while the MSTest project runs and prints `Passed!`. That breaks `CLAUDE.md`'s documented
`dotnet test InTest.sln  # all four suites`, and it means the fifth suite **cannot** simply be
another `dotnet test <csproj>` step in CI's `fast` job (`build-and-test.yml:105-119`). Those knock-on
changes follow from the frameworks coexisting in one solution, not merely from a new suite existing.

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

(**40** lines, which is **20** unique diagnostic sites — MSBuild prints each twice.) So the xUnit adapter's internals need a **new, separate, xUnit-based test
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

**Every case body changes in one specific way, and it is not optional.** `Xunit.TestContext` has no
*static* `CancellationToken`, so all five sites become `TestContext.Current.CancellationToken`.
Confirmed: the golden expectation file ported with exactly that substitution compiles clean against a
real adapter under `TreatWarningsAsErrors=true`.

**Two templates, not one with conditionals.** A single template branching on framework inside every
block would be harder to read and would put a third framework's concerns back into one file. The
MSTest template is **121 lines** and the xUnit one will be mostly identical; that duplication is the
cheaper half of the trade.

**`TemplateEscapingGuardTests` must run against both.** It parses the template source and classifies
each `tc.<name>` by quote parity (the `LoadEmbeddedTemplate("mstest-class.scriban")` call is at
`:97`),
mechanically enforcing one of the three text-safety rules `CLAUDE.md` calls non-negotiable. A second
template the guard does not read has **no text-safety enforcement at all**. This is the single most
important test change in the work, and it is easy to miss precisely because nothing fails when it is
missed.

### `[config-opens-by-one-value]` — `project.framework` accepts `"xunit"`

`ConfigLoader.RequireSupportedFramework` refuses anything but the ordinal-exact lowercase `"mstest"`.
It gains `"xunit"` on the same terms. Its doc records that defaulting was considered and rejected;
that reasoning is unchanged.

### `[framework-is-an-init-flag]` — how a user actually picks xUnit

Rev 2 described the reader (`[config-opens-by-one-value]`) and the output
(`[scaffold-per-framework]`) and **never said how the value gets set**. Today `init` takes
`--project`, `--name`, `--spec` and `--client-lockfile` (`Program.cs:52-56`) and writes
`"framework": "mstest"` as a literal (`InitCommand.cs:418`).

**`init` gains `--framework <mstest|xunit>`, defaulting to `mstest`.**

This is not a detail a plan can settle task-by-task, which is why it is a named decision. §3 places
"the CLI's commands, flags and exit codes" under semver, and §5's command-surface table is the
contract — the same table the 2026-08-16 spec records as having gone stale three times from exactly
this kind of omission. So:

- **§5's command table changes in the same commit**, not afterwards — and note its **Writes**
  column is framework-dependent too, not just its flag list: it names `*.runsettings`, which has no
  purpose under xUnit.
- **A bad value exits `2`**, matching §5's "an argument was refused" convention — not `1`, which
  means work outstanding.
- **Defaulting to `mstest` here is not the defaulting `ConfigLoader` refuses.** That refusal is about
  `intest.json` never carrying an implicit framework; a CLI flag with a documented default writes an
  *explicit* value into the file, which is the behaviour that rule exists to guarantee.
- **`upgrade` and `generate --check` need no framework awareness** — both read the value from
  `intest.json`, which is where it now always is.

### `[frozen-axis-becomes-reachable]` — opening `project.framework` makes an unenforced promise observable

§5 makes the test framework a **frozen axis** and promises *"Attempting to change a frozen axis fails
with a real error"*. That promise is currently unreachable for the framework only because v1 ships
one value.

**Nothing in the CLI enforces it.** A grep across `src/InTest.Cli` finds no frozen-axis enforcement
at all — the sole hit is a doc comment at `ConfigLoader.cs:270`. So the day `"xunit"` is accepted, an
adopter can edit one string in `intest.json`, run `generate`, and receive a wholesale-rewritten
`Generated/` targeting a framework their `.csproj`, `AssemblyInfo.cs` and `.runsettings` do not
match. §5's promise becomes false in an observable way.

**Rev 3 reached the opposite conclusion** — *"`upgrade` and `generate --check` need no framework
awareness — both read the value from `intest.json`"* — which is true of *reading* and silent on
*changing*, and which a plan would faithfully encode as "no task here".

**Decision: `generate` detects the mismatch and refuses.** The detection already has a precedent to
copy rather than invent: `UpgradeCommand.DetectRuntimeReferenceMismatch` compares `intest.json`
against the adapter `PackageReference` in the `.csproj`. The same comparison answers this — a project
whose config says `"xunit"` while its `.csproj` references `InTest.Runtime.MSTest` has changed a
frozen axis, and that is detectable without recording any new state.

**The exit code is the implementer's to confirm against §5's table**, not to assume: a refused
configuration is most likely `2`, but `generate --check` already uses `4` for a version mismatch and
the two should not disagree by accident.

**This debt predates this design** — identifier naming is also frozen and also unenforced — and
fixing that generally is out of scope. What is in scope is not *shipping* the first reachable case
with nothing behind it.

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
  **against a deployed API**, silently reversing a deliberate decision that §11 rests on, and
  that §2's constraints table names as one of the three models that are not interchangeable between
  frameworks. The analogue is:

  ```csharp
  [assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
  ```

  **Not `CollectionBehavior(DisableTestParallelization = true)`, which rev 3 prescribed and which
  does not compile:**

  ```
  error CS0619: 'CollectionBehaviorAttribute.DisableTestParallelization' is obsolete:
  'Please set ParallelizationAttribute.Mode instead. This property will be removed in the next
  major version.'
  ```

  `ObsoleteAttribute.IsError` is `True`, so this is a hard error regardless of
  `TreatWarningsAsErrors`; `MaxParallelThreads` and `ParallelAlgorithm` on that attribute are
  obsolete-as-error too. Verified compiling, with runner banner `parallel mode = none`. **Note the
  namespaces — neither is where a reader would guess:** `ParallelizationAttribute` is in `Xunit.v3`,
  `ParallelMode` in `Xunit.Sdk` (asm `xunit.v3.common`).

  `[Fact(DisableParallelization = true)]` **is** correct and is not obsolete — it carries
  `tc.mutates`.

### `[profile-loses-its-first-source]` — the run-settings profile has no xUnit equivalent

The MSTest scaffold writes `{projectName}.runsettings` (`:623-634`) carrying
`<TestRunParameters><Parameter name="profile" …>`, read by `TestHost.ProfileFromRunSettings` and
ranked **first** in `InTestRun.ResolveProfile` (`InTestRun.cs:586-590`: runsettings →
`INTEST_PROFILE` → config default → `"local"`).

**Correction to rev 3's framing: the delta is smaller than stated.** The `<Parameter name="profile" …>`
line is **commented out by design** (`InitCommand.cs:629`) — the canonical spec records why:
*"Commented out. `<RunSettingsFilePath>` loads it unconditionally, which made `INTEST_PROFILE`
unreachable."* So `INTEST_PROFILE` is already the primary mechanism for MSTest adopters out of the
box. **xUnit loses an opt-in, not a default**, and `docs/getting-started.md` must not assert a
difference that mostly is not there.

**That same file carries a second capability rev 2 missed, and this half stands:**
`<MSTest><TestTimeout>60000</TestTimeout></MSTest>`. xUnit's `[Fact(Timeout = …)]` is per-test, not a
global default, so there is no scaffold-level equivalent. Name it, decide it, document it — the same
treatment as the profile.

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
| Inside `IAsyncLifetime.InitializeAsync`, `TestContext.Current.Test` is non-null, `TestDisplayName` is populated and `TestOutputHelper` is non-null; `DisposeAsync` runs on pass, fail **and** skip | **verified** — load-bearing, since this is where the adapter must call `BeginTest`/`EndTest` |
| `xunit.v3` download count | **unverified offline** — §18 recorded 38,574,301 on 2026-08-17 |

---

## 4. The Golden matrix

**Read `[harness-port-comes-first]` before this section.** Sizing the matrix is meaningless until the
harness can execute one xUnit project.

`InTest.Golden.Tests` is the only suite proving generated code both **compiles and runs**. It holds
**50 tests**, but they do not all scale with a framework axis, and rev 1's breakdown was wrong:
`CompileVerificationTests` (6), `GeneratedSuiteExecutionTests` (23) **and
`ScaffoldCompileVerificationTests` (1)** shell out to a real `dotnet build`; `CliExitCodeTests` (15
discovered — 14 methods, one carrying two `[DataRow]`s) does not, and `MSBuildEvaluationTests` (2)
shells out only to `dotnet msbuild -getProperty`.

**`GoldenFileTests` (3) is the class rev 3 forgot, and it is the one with a deliverable attached.**
`OutputMatchesTheGoldenFile`, `GenerationIsDeterministic` and `EveryCaseIsCategorizedContract` pin the
template's byte-for-byte output against `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt`.
**A second template needs a second expectation file** — checked in, and regenerated by the same
`INTEST_UPDATE_GOLDEN=1` path. Rev 3 accounted for 47 of 50 discovered tests and twice insisted the
previous breakdown was wrong; this is the third correction to the same paragraph, which is reason to
take the count from `--list-tests` rather than from any prose, including this.

**33 grow, not 29** — the 30 shell-outs plus `GoldenFileTests`' three.

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
- **Hostile spec text.** The *escaping* is framework-independent (`CSharpLiteral`), and
  `InTestId.Slugify` (`InTestId.cs:43-86`) **definitively** contains it — it reduces to `[a-z0-9-]`,
  caps at 120 characters and hash-suffixes on non-ASCII loss. Rev 2 hedged with "very likely"; the
  answer is forty lines of reading away and is now stated. The case still belongs in the xUnit
  subset, but for the correct reason: under xUnit the hostile text becomes the trx `testName`
  attribute and the runner's console line, **both of which the Golden harness itself parses**.

- **`ScaffoldCompileVerificationTests`.** It calls `InitCommand.Run` and builds the raw scaffold —
  **the only test in the repository that compiles scaffold output at all**. That scaffold is exactly
  what `[scaffold-per-framework]` changes materially: `<OutputType>Exe</OutputType>` (which this
  document calls "a hard build error without it"), the `AssemblyFixture` class and assembly
  attribute, `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, and a different
  package set. **Under rev 2's matrix nothing would ever have compiled the xUnit scaffold.** Every
  one of those items is a hard build failure that this test would catch — **except the parallelism
  attribute, which it cannot.** A missing `[assembly: Parallelization(...)]` compiles perfectly; a
  build-only test is blind to it. The MSTest analogue is guarded by a text assertion in a different
  suite entirely — `tests/InTest.Cli.Tests/InitCommandTests.cs:357`,
  `ShouldContain("[assembly: DoNotParallelize]")` — and the xUnit scaffold needs its counterpart
  there. Rev 3 called parallelism "a correctness issue" and then assigned it to a test that cannot
  see it.

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
- **A second golden expectation file for the xUnit template**, checked in and regenerated through the
  same `INTEST_UPDATE_GOLDEN=1` path — `GoldenFileTests` is what pins byte-for-byte output, and rev 3
  omitted it from both §4 and this list.
- **A scaffold text assertion for the xUnit parallelism attribute**, beside
  `InitCommandTests.cs:357`'s MSTest one — no build-only test can catch its absence.
- `NeutralityTests` and `pack-and-verify.ps1` **extended** to cover the fourth package, and confirmed
  to fail if it is not an adapter / not packed.
- `PackageVersionCouplingTests` extended, and confirmed to fail when a version disagrees.
- A generated xUnit suite against the live Orders sample, matching the MSTest acceptance run's
  discipline: **assert per-assembly counts and skip reasons, not the total** — noting that xUnit puts
  a skip reason in `<StdOut>` rather than `<Message>`.

**Do not restate a Golden timing figure in this document.** Read it from `CLAUDE.md` when you run it
and pass a timeout well past it. Rev 1 restated two and created the fourth stale copy this rule
exists to prevent.
