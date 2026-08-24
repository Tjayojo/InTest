# Trunk-based versioning and prerelease

**Status:** plan, rev 5. Nothing built yet.

**Revision note — rev 5.** `[publish-stays-manual]` is **superseded by NuGet Trusted Publishing
(OIDC)**. Built separately, in the release-workflow task that made this plan's own deferral
obsolete: `.github/workflows/release.yml`, tag-triggered, publishes both packages to nuget.org
using `NuGet/login` to exchange a GitHub OIDC token for a short-lived API key — no
`NUGET_API_KEY` secret exists anywhere in this repository. See `[publish-stays-manual]`'s own
section below for the corrected record, in the style this document already uses for a superseded
decision (`[versionprefix-not-version]`, rev 3): nothing in it is an instruction any more, and it
is kept because the *reasoning that went wrong* is worth having on file, not because the
conclusion still holds.

**Revision note — rev 4.** Reviewed; four blocking findings, all corrected here. The review
reproduced `[version-from-git]`'s table and the shallow-clone silence independently, so those stand.
What did not: a rev-2 leftover in Task 3 asked to verify a guard against a property that no longer
exists; the `[shallow-clone-is-a-defect]` guard turned out to be **unbuildable as specified**;
Task 2's acceptance signal was order-dependent and named a test that has nothing to rewrite; and the
superseded `[versionprefix-not-version]` still carried two live instructions, one of which is now
factually wrong.

**Revision note — rev 3.** The owner asked whether this matches how Microsoft does it. Researched
against guidance, the .NET team's own repositories, and measurement. Two answers changed the plan:
the branching model and the property shape **do** match .NET practice, but the counter does not and
**cannot** — .NET's comes from Arcade's `OfficialBuildId`, an internal Azure DevOps input. And
`versioning.md` says a CI build number belongs in the **`AssemblyFileVersion` revision**, not in the
prerelease suffix, which dissolves the question rev 2 was agonising over. `[versionprefix-not-version]`
is **superseded by `[version-from-git]`** — its measurements stand and are kept. Two new decisions:
`[version-from-git]` and `[shallow-clone-is-a-defect]`. `[tag-is-the-release]` gained the
artifact-versus-publishable distinction.

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
- **`InitCommand.cs:232` hardcodes** `<PackageReference Include="InTest.Runtime" Version="0.1.0" />`.
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

`main` is protected, continuous, and always buildable. **Every merge produces a versioned
artifact; every tag produces a publishable one.**

Rev 1 said "every merge produces a prerelease", implying publication. That is wrong for two
measured reasons. A height-bearing version signals *"should not be released"* — MinVer's own stated
semantics — and **nuget.org versions are permanent and undeletable**, so publishing one per merge
burns the version space of a package that has not shipped once. When a preview should be
installable, tag it: `git tag 0.1.0-preview.1`. That is more faithful to this decision, not less.

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

### `[versionprefix-not-version]` — **superseded by `[version-from-git]`**

> **Nothing in this section is an instruction any more.** Rev 4: two directives survived here and
> both are now void — "the suffix's counter still needs deciding, Task 2 decides" (there is no
> counter under `[version-from-git]`), and "write these two facts into `CliVersion.cs`". **The
> second is now false**: under MinVer `AssemblyVersion` is `{Major}.0.0.0` — measured `0.0.0.0`,
> not `0.1.0.0` — so an implementer following it would write a comment that contradicts the build.
> Read this section as a record, never as work.
>
> Kept rather than deleted: its measurements are still true of *`VersionPrefix`*, and it is what
> the .NET team actually does — `dotnet/runtime`, `dotnet/aspnetcore` and `dotnet/sdk` all hold
> `VersionPrefix` (or `MajorVersion`/`MinorVersion`) plus `PreReleaseVersionLabel` and
> `PreReleaseVersionIteration` in `eng/Versions.props`. It is superseded because it leaves the
> **counter** unsolved, not because it is wrong. Read it for the silent-failure measurement below,
> which still governs: anything that sets `<Version>` explicitly makes `-p:VersionSuffix` a no-op
> with zero warnings.

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

### `[version-from-git]` — MinVer derives the version; there is no counter to decide

**Measured on SDK 10.0.400**, MinVer 7.0.0 with `MinVerMinimumMajorMinor=0.1`,
`MinVerDefaultPreReleaseIdentifiers=preview.0`, `MinVerAutoIncrement=minor`:

| Situation | Version |
|---|---|
| untagged commits on `main` | `0.1.0-preview.0.1`, `0.1.0-preview.0.2`, … |
| exactly on tag `0.1.0` | **`0.1.0`** |
| commit after that tag | `0.2.0-preview.0.1` |
| tagged `0.2.0-preview.1` | `0.2.0-preview.1` |

That is exactly the three behaviours `[tag-is-the-release]` specifies, **with no CI logic and no
counter to choose.** It deletes rev 2's Task 2 rather than implementing it.

`[scaffold-reads-itself]` is unaffected: MinVer stamps `InformationalVersion` as
`0.2.0-preview.0.1+<sha>`, and `CliVersion.cs:47` strips at the first `+` — the case v1-e Task 1
already proved at `1.0.0-rc.1`.

**Why not Nerdbank.GitVersioning**, despite the stronger pedigree — it is owned by the `dotnet` org,
MIT, actively maintained, used across `microsoft/vs-*`, and **named in Microsoft's own
`AssemblyVersionAttribute` documentation** as *"a better approach… derive the assembly or file
version from the `HEAD` commit SHA"*. It is version-**file**-driven: a release is cut by *committing*
a change to `version.json`, and `publicReleaseRefSpec` makes the version depend on which branch you
are on. Both re-introduce exactly what `[tag-is-the-release]` was chosen to eliminate. That is a
structural conflict with the model, not a preference — and it is the one place this plan knowingly
declines a Microsoft-named tool. If the owner later weights failure-loudness above model fit (see
`[shallow-clone-is-a-defect]`), NBGV is the defensible other answer.

**Why not keep hand-rolling.** Every counter candidate is flawed, and rev 2 named only one of the
flaws. `github.run_number` **does not exist outside GitHub Actions**, so no local build, fork, or
future migration can reproduce a version CI produced — and its reset-on-rename behaviour is
community-reported, *not* documented, so rev 2 should not have stated it flatly. A date stamp needs
a builds-per-day revision that Arcade's pipeline supplies and GitHub Actions does not. Commit height
is what MinVer already implements. Hand-rolling ends in writing MinVer, worse.

> **Two guidance items this plan knowingly declines**, both `CONSIDER` rather than `DO`:
> *"only including a major version in the AssemblyVersion"* — MinVer gives `{Major}.0.0.0`, which
> satisfies it for free; and *"including a continuous integration build number as the
> `AssemblyFileVersion` **revision**"*, which remains unmet under any scheme here. Both exist chiefly
> to reduce .NET Framework binding-redirect pain; InTest is not strong-named and targets `net10.0`.
> Record the decline rather than leaving it to look like an oversight.

**Prerelease label:** `preview`, dotted. Measured with NuGet's own comparer: `preview.9 < preview.10`
is **true**, `preview9 < preview10` is **false** — the dot is load-bearing. `ci` and `dev` are
reserved by Arcade for unpublished builds; `beta` sorts *before* `preview`, so moving from preview
to beta later is impossible. Build metadata cannot be the counter: `0.1.0-preview.1+abc` and
`+zzz` compare **equal**, and NuGet strips it on publish.

### `[shallow-clone-is-a-defect]` — MinVer's one bad failure mode, bought back with a guard

**In a shallow clone MinVer sees no tags, computes height 0, and silently produces
`0.1.0-preview.0` for every commit — no warning, no error.** Verified: our workflow has **three
`actions/checkout` call sites and zero `fetch-depth` settings**, so all three are the default
depth-1 clone. Adopting MinVer today would hit this on the first run.

That behaviour is this repository's named anti-pattern, verbatim from `CLAUDE.md`: *"Never
substitute plausible defaults that let a suite pass while asserting nothing."* NBGV hard-errors here
instead, with an accurate message, and that is genuinely better engineering.

**So buy the loudness back.** Set `fetch-depth: 0` on all three checkout steps, **and** add a guard
that fails when the resolved version lacks height it should have — the same shape as
`NeutralityTests`, `JsonWritingOptionsGuardTests` and `PackageVersionCouplingTests`. The
`fetch-depth` alone is not enough: it is one line in one file that a future edit can silently drop,
which is precisely the class of regression this repo builds guards for.

**The guard is harder to write than it looks, and rev 3 understated it.** Measured under this
plan's own configuration, three different situations produce the **same version string**:

| Situation | `MINVER1001`? | Resolved version |
|---|---|---|
| shallow clone (`--depth 1`) | **no diagnostic at all** | `0.1.0-preview.0` |
| outside a git repository | yes, a warning | `0.1.0-preview.0` |
| legitimate untagged root commit | no | `0.1.0-preview.0` |

(Rev 3 said the non-git fallback was `0.0.0-alpha.0`; that is MinVer's raw default —
`MinVerMinimumMajorMinor` lifts it to `0.1.0-preview.0`. Corrected.)

So **a guard keyed on the version value cannot work**: it would fail `scripts/local-e2e-test.ps1`,
which packs from a non-git copy by design. And **a guard keyed on `MINVER1001` misses the shallow
clone entirely**, which is the case that matters most. MinVer exposes no height property, so
discriminating requires asking git directly — for example, asserting that a repository is present
*and* that tags are visible, and failing when it is a repository with no visible tags.

**Decide the guard's mechanism before Task 2 runs, not during it.** The self-review's warning is
literal: if this guard is skipped or written so it cannot fail, this plan makes the repository worse
than the hand-rolled counter it replaced.

`MINVER1001` itself is **measured to stay a warning under `TreatWarningsAsErrors`**, because it is
an MSBuild task warning rather than a compiler one — so it will not stop a build on its own.

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

### `[publish-stays-manual]` — **superseded by NuGet Trusted Publishing (OIDC)**

> **Nothing in this section is an instruction any more.** Rev 5: a separate task built
> `.github/workflows/release.yml`, which performs the actual `dotnet nuget push` this section
> deferred, gated behind a GitHub Environment rather than a stored secret. Kept rather than
> deleted, the same reason `[versionprefix-not-version]` was kept above: the reasoning that
> produced this decision is worth having on file, because two of its three premises turned out to
> be wrong in ways worth not re-arguing from scratch later.
>
> This section's original argument rested on three premises, each examined below on its own
> terms:
>
> - **"No NuGet IDs are reserved."** This was **never actually a blocker**, and treating it as one
>   was a reasoning error independent of trusted publishing — ID reservation is orthogonal to
>   whether CI *can* push. Nothing about nuget.org requires a package ID to pre-exist before the
>   first `dotnet nuget push` that names it; the first push simply claims the ID, whether that push
>   comes from a human's laptop or from CI. This premise should not have stopped a release job even
>   under the old API-key model.
> - **"The API key is the owner's and cannot live in CI."** **Dissolved, not merely mitigated.**
>   Trusted publishing does not solve the problem of getting the owner's key into CI safely — it
>   removes the key from the equation entirely. nuget.org's trusted-publishing policy binds a
>   *package owner* (a nuget.org account), matched against claims GitHub itself puts in a
>   short-lived OIDC token — repository, workflow file name, and (the control this repository
>   relies on) a GitHub Environment. `NuGet/login` exchanges that token for an API key that lives
>   for about an hour and is never written to a secret store. There is no long-lived credential for
>   "cannot live in CI" to be true or false about.
> - **"Machinery with no consumer rots before first use."** **Still valid, and still the strongest
>   argument in this section** — it just now argues for the opposite timing conclusion than rev 1–4
>   drew from it. It was correct to *not* build a release job back when nothing was ready to
>   publish; it is equally correct to build one *now*, at the point where `[scaffold-reads-itself]`,
>   `[version-from-git]` and the readiness spec's metadata pass have all landed and a real tag push
>   is imminent. Building it earlier would have risked exactly the rot this premise warns about;
>   building it now, with a concrete first release as the consumer, is the same principle applied
>   at the right moment rather than a reversal of it.
>
> Consistent with the CI plan's `[publish-before-release-machinery]`, which deferred the release
> job for the identical reason this section gave — that document should be read alongside this one
> as carrying the same superseded premise, not re-corrected separately.

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

- [ ] **Step 1:** `InitCommand.cs:232` emits `CliVersion.Current` instead of `0.1.0`. Decide
      deliberately how the value reaches the template string and whether anything must escape it.
- [ ] **Step 2: Rewrite `PackageVersionCouplingTests`' `InTest.Runtime` case — do not delete it.**
      There is no literal left to compare, but the guard is what would have caught this defect.
      Replace it with an assertion that the scaffold emits the running version, and **prove it
      discriminates** by building at a version other than `0.1.0` and confirming the scaffolded
      output follows. The MSTest and `Microsoft.NET.Test.Sdk` cases are unaffected — leave them.
- [ ] **Step 3:** `Directory.Build.props`' comment now describes the old causality. Correct it.
- [ ] **Step 4: Prove the whole thing end to end.** Build the CLI at a prerelease version, scaffold
      a project with it, and confirm the generated `.csproj` references that same version.
      `scripts/local-e2e-test.ps1` is the natural harness. It was measured to pass end to end
      against a **`VersionPrefix`** clone — *not* against the MinVer shape actually being built, and
      Task 2 Step 5 expects `MINVER1001` to fire there by construction. **Treat it as unproven for
      this design** and re-measure. Note its csproj-patch step
      (`scripts/local-e2e-test.ps1:328-331`) becomes **redundant** once the scaffold emits the
      running version; remove it deliberately rather than leaving a second mechanism doing the same
      job.
- [ ] **Step 5:** Implement `[prerelease-reference-migration]`'s detect-and-report in `upgrade`, and
      prove it fires: scaffold with a prerelease CLI, upgrade with a stable one, confirm the message
      names the file and the exact edit. Confirm a non-matching `.csproj` produces silence, not a
      crash.

### Task 2: `[version-from-git]` and `[shallow-clone-is-a-defect]`

- [ ] **Step 1:** Add MinVer as a build-time-only reference (`PrivateAssets="all"`), version pinned
      in `Directory.Packages.props`, and remove `<Version>` from `Directory.Build.props`. **Confirm
      nothing ships**: unzip both packages and check the nuspec dependency group is empty. Measured
      to be so, but verify rather than inherit the claim.
- [ ] **Step 2:** Configure it for this repo's shape — minimum major/minor, default prerelease
      identifiers, auto-increment — and confirm the four rows of `[version-from-git]`'s table
      against a real tag in a scratch clone.
      **Expect exactly two failures** in `PackageVersionCouplingTests`
      (`InitCommandScaffoldVersionsMatchTheCenter`, `CompileVerificationTestsScaffoldVersionsMatchTheCenter`).
      Both fail for one reason: `ReadRuntimeSelfVersion` (`PackageVersionCouplingTests.cs:117-128`)
      asserts `<Version>` exists in `Directory.Build.props`, and Step 1 removes it. That is the
      **acceptance signal**, not a surprise.
      **Fix them in this task, not Task 1** — rev 3 said "Task 1 Step 2 resolves them", which is
      wrong twice over: Tasks run 0→1→2, so `<Version>` is still present during Task 1 and both
      tests stay green there; and `CompileVerificationTests.cs` references `InTest.Runtime` by
      **`ProjectReference`, not `PackageReference`** (`CompileVerificationTests.cs:45`), so it has no
      version literal for Task 1 to rewrite at all. `ReadRuntimeSelfVersion` needs a new source of
      truth under MinVer — decide what, and keep the guard's other cases intact.
- [ ] **Step 3:** `fetch-depth: 0` on all three `actions/checkout` steps, **and** the guard from
      `[shallow-clone-is-a-defect]`. **Prove the guard fires** by building from a `--depth 1` clone
      and confirming it fails rather than producing `0.1.0-preview.0` quietly. A guard that has not
      been seen to fail is decoration.
- [ ] **Step 4:** Confirm `CliVersion.Read()` returns the label intact through the full path —
      build, pack, `init`, read `intestVersion` back. v1-e Task 1 proved the mechanism at
      `1.0.0-rc.1`. Note MinVer alone was **measured to emit no `+<sha>` at all**, with or without a
      remote — so there may be nothing to strip. Harmless either way, since `CliVersion.cs:47`
      handles both, but do not assert a `+` is present.
- [ ] **Step 5:** Run `scripts/local-e2e-test.ps1`. It packs from a **non-git copy**, so
      `MINVER1001` fires there by construction. Decide deliberately whether that fallback is
      acceptable for the local harness or whether the script should pass an explicit version, and
      record which.

### Task 3: CI produces the versions

- [ ] **Step 1:** On merge to `main`, produce prerelease-versioned artifacts. Reuse the existing
      workflow's action pins (`[pin-actions-by-sha]`) and matrix conventions.
- [ ] **Step 2:** On a tag, produce release-versioned artifacts. **Do not push to nuget.org**
      (`[publish-stays-manual]`), and record which step is therefore unexercised — the discipline
      v1-e Task 6 Step 1 applied to `dotnet tool restore`.
- [ ] **Step 3: Verify the produced artifacts**, do not assume. Unzip and confirm the nuspec version
      and the scaffolded reference agree. **Packing has only ever run on Windows** — this is the
      first time CI would pack, so exercise both platforms or say plainly which was not.
- [ ] **Step 4: Assert the artifact matches the tag.** Rev 3 asked to "tag a commit whose version
      disagrees with `VersionPrefix` and confirm CI refuses" — **impossible as written**: Task 2
      removes `<Version>` and nothing introduces `VersionPrefix`, and MinVer accepts *any* tag
      (measured: tagging `0.0.5` under `MinVerMinimumMajorMinor=0.1` yields `0.0.5`, no warning).
      There is no mismatch to refuse.
      The real check is the converse: on a tagged build, assert the produced `.nupkg` version equals
      the tag exactly, and that it carries **no** prerelease height. Prove it by tagging a scratch
      commit and inspecting the artifact.

### Task 4: Documentation

- [ ] `CONTRIBUTING.md` — the branching model, what a merge produces, what a tag produces, and how
      to cut a release. `CLAUDE.md` if the commands change.
- [ ] The readiness spec's §8 checklist assumes a manual local pack; reconcile it with CI-produced
      artifacts.
- [ ] Record the two declined `CONSIDER` items from `versioning.md` (major-only `AssemblyVersion`,
      CI build number in the `AssemblyFileVersion` revision) where a future reader will find them —
      declining guidance deliberately is fine; leaving it looking like an oversight is not.

---

## Self-review

**Rev 1's two risks are measured and decided** — see the superseded
`[versionprefix-not-version]`'s table and `[prerelease-reference-migration]`. Rev 1 asserted the
version composition worked before measuring it, the shape that produced `[major-only]` in the v1-e
plan and had to be retracted; rev 2 measured first, which both confirmed the mechanism and produced
a better argument than rev 1 had.

**Rev 3's lesson is different and worth naming.** Rev 2's mechanism was correct, matched what the
.NET team actually does, and was measured — and was still the wrong thing to build, because a tool
already solved the part it left open. Being right about a mechanism is not the same as that
mechanism being worth writing. Nothing in rev 1 or rev 2 would have surfaced that; only asking what
the ecosystem already does did.

**Where a reviewer should push now.**

`[version-from-git]` takes a dependency where a Microsoft-named alternative exists and is declined
on model fit. That is a defensible call and it is argued, but it is the decision most exposed to a
reviewer disagreeing about which property matters more — failure-loudness or model fit.
`[shallow-clone-is-a-defect]` is the mitigation, and it is only as good as the guard Task 2 Step 3
builds. **If that guard is skipped or written so it cannot fail, this plan has made the repository
worse**, because MinVer's silent wrong version is more dangerous than a hand-rolled counter that is
merely inelegant.

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
