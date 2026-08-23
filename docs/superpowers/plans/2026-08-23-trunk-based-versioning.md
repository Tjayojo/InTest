# Trunk-based versioning and prerelease

**Status:** plan, rev 2. Nothing built yet.

**Revision note — rev 2.** Rev 1 named two risks in its self-review and left them as assertions.
Both have now been **measured**, and one result inverts what rev 1 hoped for: NuGet selects the
*lowest* satisfying version, so leaving the scaffolded prerelease reference alone is measurably
harmful rather than harmless. `[prerelease-reference-migration]` is a new decision section.
`[versionprefix-not-version]` gained a table and a much stronger argument than rev 1 gave it.

**Spec:** `docs/superpowers/specs/2026-08-23-nuget-publish-readiness-design.md` §7 — which currently
records a **superseded** decision (`develop`/`main`) and is corrected by Task 0 here.

**Prerequisite:** base commit `4bbfae9` on `main`. **658 passing — Architecture 8, Cli 410,
Runtime 205, Golden 35.** Measure it yourself; `main` has other sessions on it.

**REQUIRED SUB-SKILL: `superpowers:subagent-driven-development`.** Each task is executed by a fresh
subagent and reviewed before the next begins.

InTest must publish a prerelease before a stable `0.1.0` — versioning.md's "DO include a prerelease
suffix when releasing a nonstable package", and the repo's own README calls this "v0. Working, but
early". The owner considered `develop`/`main` and chose **trunk-based with tag-driven releases**
instead: `main` is continuous, tags are releases.

---

## What was verified before writing this

Each was read in source or run. **Where something is unproven it says so.**

- **`Directory.Build.props:3-5` pins `<Version>0.1.0</Version>`, and its comment says why:**
  *"The scaffolded project pins InTest.Runtime 0.1.0, so the packages must pack as 0.1.0; the SDK
  default of 1.0.0 would make the scaffolded restore fail."* **The causality runs backwards** — the
  product's version exists to serve a hardcoded literal in a string template.
- **`InitCommand.cs:227` hardcodes** `<PackageReference Include="InTest.Runtime" Version="0.1.0" />`.
- **`InitCommand.cs:139` already writes `CliVersion.Current`** into `intestVersion`, twelve lines
  from the hardcoded literal. The value is in hand at the point the `.csproj` string is built.
- **`CliVersion.cs:47` strips the informational version at the first `+` only**, so a build label is
  discarded and a prerelease label survives (`1.0.0-rc.1+sha` → `1.0.0-rc.1`). Confirmed during
  v1-e Task 1 by building at `-p:Version=1.0.0-rc.1`: `init` wrote it, `generate` read it back.
- **`PackageVersionCouplingTests` compares the scaffold's `InTest.Runtime` literal against
  `Directory.Build.props`' `<Version>`** — a comparison that ceases to exist once the literal does.
- **A fast prerelease channel does *not* cause routine exit 4s.** The v1-e plan's `[exact-match]`
  section settles this: `.config/dotnet-tools.json` pins the CLI version and CI runs
  `dotnet tool restore`, so **CI runs the pinned version by construction** and versions differ only
  when someone bumps the pin — which `upgrade` does alongside `intestVersion`. Spec revision 4
  claimed adopters would "hit exit 4 routinely"; that is wrong and Task 0 retracts it.
- **CI triggers on `push: [main]` and `pull_request`.** No `develop` branch exists.

**Unproven, and no task here assumes otherwise:** whether nuget.org accepts a `.snupkg` whose PDBs
sit under `tools/` rather than `lib/`, and whether packing works on Linux — packing has only ever
been exercised on Windows. Both are recorded in the readiness spec; neither blocks this plan.

---

## Named decisions

### `[tag-is-the-release]` — one branch; the tag carries the release signal

`main` is protected, continuous, and always buildable. Every merge produces a **prerelease**. A tag
produces a **release**.

A second long-lived branch would encode the same fact the tag already encodes, and two sources of
truth for "is this released?" can disagree — a commit on `main` that has not been tagged is neither
clearly released nor clearly not.

`develop`/`main` earns its cost when a shipped release must be patched while the next is developed.
**There are zero shipped releases.** `CONTRIBUTING.md:388` does commit to supporting the previous
major for 12 months, so that need is real later — served then by cutting `release/N.x` **on demand**,
not by maintaining a permanent branch against a need that does not yet exist.

Rejected: `develop`/`main`. Cost is concrete and falls on one maintainer — two merges per change,
the CI matrix doubling from 6 job/platform combinations to 12, Dependabot retargeting, and branch
protection configured twice.

> The honest tradeoff, recorded so nobody rediscovers it as a surprise: under this model `main` is
> *continuous*, not "the released thing". The last tag is the released thing and `main` runs ahead
> of it. That is the property being traded away.

### `[scaffold-reads-itself]` — the scaffold emits `CliVersion.Current`, not a literal

This is the defect that blocks prerelease, and fixing it is the whole point of the plan.

A CLI built as `0.1.0-preview.N` currently scaffolds a project referencing `InTest.Runtime`
**0.1.0** — a version that will not exist on nuget.org until the first stable release. The generated
project cannot restore. A shipped tool producing unusable output is the `[paired]` shape this
repository has now hit nine times.

`CliVersion.Current` is already written to `intestVersion` twelve lines away. Emitting it into the
`.csproj` string makes the scaffold **self-consistent by construction**: whatever version the CLI
was built as, that is what it references, with no literal to drift.

Note this is *stricter* than §3's compatibility contract, which permits any CLI `N.y` with any
runtime `N.x`. Exact is safe within that contract and removes a hand-maintained value; a looser
floor would be defensible but buys nothing here.

**It also inverts the dependency in `Directory.Build.props`' comment the right way round.** Today the
product version is pinned to serve the template literal. After this, the template follows the
product version.

### `[versionprefix-not-version]` — compose the version, do not overwrite it

`<Version>` set explicitly **overrides** MSBuild's `VersionPrefix` + `VersionSuffix` composition, so
the suffix mechanism has to replace it rather than sit alongside it:

```xml
<VersionPrefix>0.1.0</VersionPrefix>
```

Locally, with no suffix, `Version` evaluates to `0.1.0` — unchanged from today. In CI on `main`,
`-p:VersionSuffix=preview.<n>` yields `0.1.0-preview.<n>`. On a tag, no suffix, so `0.1.0`.

Rejected: injecting a whole `-p:Version=` string from CI. It works, but it makes the shipped version
differ from the file every test reads, which is precisely the split that makes
`PackageVersionCouplingTests` ambiguous about which value it is guarding.

**Measured, on SDK 10.0.400, both packages** — rev 1 asserted this composition; here it is:

| Case | `Version` | `PackageVersion` | `AssemblyVersion` | `InformationalVersion` | nuspec | `CliVersion.Read()` |
|---|---|---|---|---|---|---|
| Today, `<Version>0.1.0</Version>` | `0.1.0` | `0.1.0` | `0.1.0.0` | `0.1.0` | `0.1.0` | `0.1.0` |
| `<VersionPrefix>`, no suffix | `0.1.0` | `0.1.0` | `0.1.0.0` | `0.1.0` | `0.1.0` | `0.1.0` |
| `<VersionPrefix>` + `-p:VersionSuffix=preview.7` | `0.1.0-preview.7` | `0.1.0-preview.7` | `0.1.0.0` | `0.1.0-preview.7` | `0.1.0-preview.7` | `0.1.0-preview.7` |

Row 2 is **byte-identical to row 1 on every column** — the no-regression case holds. `InTest.Cli`
and `InTest.Runtime` produced identical numbers throughout; there is no tool/library difference.

**The strongest argument for this decision is what the current mechanism does when you try to use
it.** With `<Version>` set explicitly, `-p:VersionSuffix=preview.7` is *accepted*, `VersionSuffix`
evaluates, and `Version` stays `0.1.0` — **0 warnings, 0 errors, build succeeded.** CI would ship a
package stamped `0.1.0` while believing it shipped a preview. That silent failure, not tidiness, is
the cost of not making this change.

**Two facts worth writing into the code, both measured:**

- `AssemblyVersion` and `FileVersion` stay `0.1.0.0` for **every** prerelease of `0.1.0` — assembly
  identity cannot distinguish `preview.6` from `preview.7`. And the derivation rule is not the
  obvious one: it is **`$(Version)` with the label stripped and padded**, not "from `VersionPrefix`"
  — building at `-p:Version=9.9.9-x` with `VersionPrefix` still `0.1.0` yields `AssemblyVersion`
  `9.9.9.0`.
- **`CliVersion` reads the one property of the seven that carries the label.** Reading
  `Assembly.GetName().Version` instead would collapse every prerelease to `0.1.0` and make
  `[exact-match]` permanently blind. `CliVersion.cs` should say so.

**The suffix's counter still needs deciding, not defaulting.** `github.run_number` is monotonic and
sorts correctly under SemVer's numeric-identifier rule (`preview.9` < `preview.10`), but it resets
if the workflow is renamed or recreated. Alternatives are a commit count or a date stamp. Task 2
decides and records why.

### `[prerelease-reference-migration]` — `upgrade` detects and reports; it does not rewrite

Rev 1's self-review hoped NuGet resolution would make this harmless. **Measured against real local
feeds, it does not.**

| Feed holds | Reference | Resolves to |
|---|---|---|
| `0.1.0-preview.7` **and** `0.1.0` | `0.1.0-preview.7` | **`0.1.0-preview.7`** |
| `0.1.0` only | `0.1.0-preview.7` | `0.1.0` + warning NU1603 |
| `0.1.0-preview.7` only | `0.1.0` | **NU1102 — restore fails** |
| both | `0.1.0-*` | `0.1.0` |

**NuGet selects the lowest satisfying version**, so a scaffolded `Version="0.1.0-preview.7"` keeps
resolving the prerelease after stable ships — indefinitely, with a green build and zero warnings.
And `upgrade` never opens the `.csproj`: verified end to end, it bumped `intest.json` and the tool
pin to `0.1.0` and left the reference at `0.1.0-preview.7`, reporting success. `generate --check`
compares only `intestVersion` against `CliVersion.Current` (`GenerateCommand.cs:170`), so **no
InTest command can observe the runtime's version at all.** An adopter who believes they moved to
stable runs a green suite against prerelease runtime bits.

**Rejected, each measured rather than argued:**

- *Leave it — NuGet handles it.* False. Row 1 above.
- *Scaffold the stable base version instead of the running one.* NU1102, restore fails during the
  entire prerelease window — reproducing the exact defect `[scaffold-reads-itself]` removes.
- *Scaffold a floating `0.1.0-*`.* Genuinely self-migrating — measured, it resolves the prerelease
  while only prereleases exist and jumps to stable the moment one does, with no `upgrade` change at
  all. Rejected because it makes a committed test project's restore non-deterministic and silently
  floating, which is the opposite of what `[exact-match]` is built on. Recorded because it is the
  option a reviewer will propose.

**Decided: `upgrade` reads the scaffolded `.csproj`, and if the `InTest.Runtime` reference differs
from `CliVersion.Current`, appends a line naming the file, the current value and the exact
replacement.** Pure read, no ownership crossing, and a failed match means "say nothing extra"
rather than "corrupt a build".

> The ownership comparison rev 1 would have reached for — `.gitattributes` — is the wrong one. The
> closer precedent is `upgrade`'s own Decision 1: surgical single-value replacement inside
> adopter-owned `intest.json` and `.config/dotnet-tools.json`, deliberately refusing to
> reparse-and-rewrite. Rewriting one attribute is the same *kind* of crossing. **The material
> difference is blast radius, not ownership**: a `.csproj` is the adopter's build, and a matcher
> that is good enough on the scaffold InTest emits can misfire on a real one — attributes reordered,
> `VersionOverride`, central package management, the reference moved to `Directory.Packages.props`.
> Promoting detection to a rewrite is a small step **once the matcher has seen real projects**;
> doing it first ships an untested XML surgeon into adopter builds.

**This mitigates, it does not close.** The durable fix is for `generate --check` to learn the
runtime's version — a change to `[exact-match]` in the spec, out of scope here, and the reason this
decision is a report rather than a guarantee.

### `[publish-stays-manual]` — this plan produces correct versions, it does not publish

Consistent with the CI plan's `[publish-before-release-machinery]`, which deferred the release job
because it would be forbidden from its own purpose: no NuGet IDs are reserved and the API key is the
owner's.

This plan makes CI produce **correctly versioned artifacts** and proves they are correct. Wiring
`dotnet nuget push` remains deferred until there is something to publish to.

---

## Tasks

### Task 0: Correct the spec

- [ ] **Step 1:** Spec §7 records `develop`/`main` as decided. It is superseded. Rewrite it for
      `[tag-is-the-release]` **in the style that section already uses** — record the superseded
      decision and why it changed, do not silently replace it.
- [ ] **Step 2: Retract the exit-4 claim.** §7 says adopters on a prerelease channel "hit exit 4
      frequently and run `upgrade` to clear it". That is wrong: the tool pin means CI runs the
      pinned version by construction, so a fast-moving channel changes nothing. Retract it
      explicitly rather than deleting it quietly — it is the kind of plausible-but-wrong claim this
      repository keeps having to catch.
- [ ] **Step 3:** §7's "what this touches" list assumes `develop`. Rewrite against the real model.

### Task 1: `[scaffold-reads-itself]`

- [ ] **Step 1:** `InitCommand.cs:227` emits `CliVersion.Current` instead of `0.1.0`. Decide
      deliberately how the value reaches the template string and whether anything must escape it.
- [ ] **Step 2: Rewrite `PackageVersionCouplingTests`' `InTest.Runtime` case — do not delete it.**
      There is no literal left to compare, but the guard is what would have caught this defect.
      Replace it with an assertion that the scaffold emits the running version, and **prove it
      discriminates** by building at a version other than `0.1.0` and confirming the scaffolded
      output follows. The MSTest and `Microsoft.NET.Test.Sdk` cases are unaffected — leave them.
- [ ] **Step 3:** `Directory.Build.props`' comment now describes the old causality. Correct it.
- [ ] **Step 4: Prove the whole thing end to end.** Build the CLI at a prerelease version, scaffold
      a project with it, and confirm the generated `.csproj` references that same version.
      `scripts/local-e2e-test.ps1` is the natural harness — **measured to still pass end to end**
      against a `VersionPrefix` clone, all nine steps. Note its csproj-patch step
      (`scripts/local-e2e-test.ps1:328-331`) becomes **redundant** once the scaffold emits the
      running version; remove it deliberately rather than leaving a second mechanism doing the same
      job.
- [ ] **Step 5:** Implement `[prerelease-reference-migration]`'s detect-and-report in `upgrade`, and
      prove it fires: scaffold with a prerelease CLI, upgrade with a stable one, confirm the message
      names the file and the exact edit. Confirm a non-matching `.csproj` produces silence, not a
      crash.

### Task 2: `[versionprefix-not-version]`

- [ ] **Step 1:** Replace `<Version>` with `<VersionPrefix>`. Confirm `dotnet build` with no suffix
      still yields exactly `0.1.0` — assembly informational version *and* packed nuspec.
      **Expect exactly two failures** in `PackageVersionCouplingTests`
      (`InitCommandScaffoldVersionsMatchTheCenter`, `CompileVerificationTestsScaffoldVersionsMatchTheCenter`),
      whose `ReadRuntimeSelfVersion` matches `<Version>([^<]+)</Version>`. That is the **acceptance
      signal** for the swap, not a surprise — the guard failing loudly is it working. Task 1 Step 2
      is what resolves them.
- [ ] **Step 2:** Decide the suffix counter and record why, per `[versionprefix-not-version]`.
- [ ] **Step 3:** Confirm `CliVersion.Read()` returns the prerelease label intact through the full
      path — build, pack, `init`, read `intestVersion` back. v1-e Task 1 proved the mechanism at
      `1.0.0-rc.1`; prove it again for the format actually chosen.

### Task 3: CI produces the versions

- [ ] **Step 1:** On merge to `main`, produce prerelease-versioned artifacts. Reuse the existing
      workflow's action pins (`[pin-actions-by-sha]`) and matrix conventions.
- [ ] **Step 2:** On a tag, produce release-versioned artifacts. **Do not push to nuget.org**
      (`[publish-stays-manual]`), and record which step is therefore unexercised — the discipline
      v1-e Task 6 Step 1 applied to `dotnet tool restore`.
- [ ] **Step 3: Verify the produced artifacts**, do not assume. Unzip and confirm the nuspec version
      and the scaffolded reference agree. **Packing has only ever run on Windows** — this is the
      first time CI would pack, so exercise both platforms or say plainly which was not.
- [ ] **Step 4:** Prove the tag path can fail: tag a commit whose version disagrees with
      `VersionPrefix` and confirm CI refuses rather than publishing a mismatch.

### Task 4: Documentation

- [ ] `CONTRIBUTING.md` — the branching model, what a merge produces, what a tag produces, and how
      to cut a release. `CLAUDE.md` if the commands change.
- [ ] The readiness spec's §8 checklist assumes a manual local pack; reconcile it with CI-produced
      artifacts.

---

## Self-review

**Rev 1's two risks are now measured and decided** — see `[versionprefix-not-version]`'s table and
`[prerelease-reference-migration]`. Rev 1 asserted the version composition worked before measuring
it, which is the shape that produced `[major-only]` in the v1-e plan and had to be retracted; this
time the measurement came first and confirmed the fix while producing a *better* argument for it
than rev 1 had.

**Where a reviewer should push now.**

`[prerelease-reference-migration]` chooses detection over rewriting, which leaves the adopter one
manual edit and **does not help someone who ignores the message**. That is a deliberate trade — a
misfiring regex inside an adopter's build is worse than a message they skipped — but it is a real
limitation and a reviewer may reasonably argue the rewrite should ship first. The counter-argument
is evidence-based rather than principled: InTest has never read an adopter's `.csproj`.

**The durable gap, named rather than closed:** no InTest command can observe the runtime package's
version. `generate --check` compares `intestVersion` to `CliVersion.Current` and nothing else, so
runtime drift is structurally invisible. Closing it means teaching `[exact-match]` about the
runtime — a spec change, out of scope here, and worth deciding before the prerelease channel has
adopters on it.

**One thing measured that belongs to the readiness spec, not this plan:** neither `dotnet pack`
invocation produced a `.snupkg` in this configuration. The readiness spec plans for `IncludeSymbols`
and `SymbolPackageFormat`; whoever implements that should confirm symbols are actually produced
rather than assuming the properties suffice.

**Unmeasured, and it matters operationally:** whether an *unlisted* prerelease still satisfies a
floor that names it. "Unlist the previews once stable ships" is the obvious lever and it cannot be
confirmed without a real nuget.org account. Inferred, not measured — do not rely on it.

**What this plan does not do.** It does not publish, does not reserve NuGet IDs, and does not close
the Phase 8 gap where `dotnet tool restore` cannot resolve `intest.cli` from a bare clone. All three
remain blocked on the owner.
