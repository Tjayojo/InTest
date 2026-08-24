# NuGet publish readiness

**Status:** Implemented · Revision 9
**Date:** 2026-08-24
**Supersedes:** Revision 8 — §2's "Not established" note on whether nuget.org accepts a
`.snupkg` whose PDBs sit under `tools/` rather than `lib/` is resolved, not just narrowed: the
`0.1.0-preview.1` tag push exercised `release.yml` for real and nuget.org accepted both `.snupkg`
files without complaint. See "Implemented — revision 9" below. Revision 8's own change (the
trusted-publishing push itself) is otherwise unaffected and remains as verified there.

## Implemented — revision 9

**What changed from revision 8:** the first real tag push, `0.1.0-preview.1` on `35056b8`, ran
both `pack.yml` and `release.yml` on GitHub Actions for real — trigger firing, matrix fan-out,
cross-job artifact transfer, the OIDC exchange and the `nuget-release` environment gate all fired,
both jobs went green, and nuget.org accepted all four artifacts (`InTest.Cli`/`InTest.Runtime`,
`.nupkg` plus `.snupkg` each). That closes §2's open question below: nuget.org does accept a
`.snupkg` whose PDBs sit under `tools/` rather than `lib/` — there was no reason to expect
rejection, and there was, in fact, none. `docs/v0-acceptance.md`'s publish record has the full
account, including what this one run does and does not prove (one tag, one platform's runners,
prereleases only — not a claim that every future tag push or a stable-version tag behaves
identically). `CONTRIBUTING.md`'s Publishing checklist and "Branching and how a release is cut"
sections carry the maintained, operational updates for this; this section is the design record.

## Implemented — revision 8

**What changed from revision 7:** `docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`'s
`[publish-stays-manual]` is now superseded — see that section's own record for the full argument.
In short: NuGet Trusted Publishing binds a nuget.org *package owner* to a workflow file name and
(the control this repository relies on) a GitHub Environment, via a short-lived OIDC-exchanged API
key that is never stored as a secret. That removed the premise §1 and §7 both stated as fact
("nobody runs `dotnet nuget push` as part of this change, and no CI pipeline gains the ability
to") — a CI pipeline now does gain that ability, deliberately, in a workflow file kept separate
from every other one in this repository specifically so that ability is not incidentally granted
anywhere else. §7's "Explicitly out of scope: CI publish workflow" bullet is retracted by this
revision, not silently — the reasoning that produced it (§9's `[publish-before-release-machinery]`
premises, recorded in the versioning plan) is kept there as a record of what changed and why, per
this document's own established style for a superseded decision.

**§8's checklist below is rewritten** to describe the trusted-publishing flow: creating the
nuget.org policy (Repository Owner, Repository, Workflow File name `release.yml`, Environment
`nuget-release`, Package owner), creating the matching GitHub Environment, and where the one
remaining human verification step now sits given that the push itself is automatic — see §8's own
text for the full sequence. `CONTRIBUTING.md`'s "Publishing checklist" section carries the
maintained, operational copy of this same checklist; §8 here is the design record of why each step
exists, not a second source of truth to keep in sync by hand indefinitely — if the two drift,
`CONTRIBUTING.md` is the one to trust, since it is what a contributor actually follows.

**§9's open `ContinuousIntegrationBuild` question is resolved, not just narrowed, by this change.**
§9 asked whether to set `ContinuousIntegrationBuild=true` explicitly for a release pack or accept
non-deterministic local release builds; both options existed only because §1 assumed the release
pack was always a human's local `dotnet pack`. Once `release.yml` performs the release pack inside
GitHub Actions, `Directory.Build.props`'s existing
`<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>`
applies automatically — `GITHUB_ACTIONS` is set to `"true"` for every step of every job on every
GitHub-hosted (and self-hosted) runner, unconditionally, so the pack that actually ships is
deterministic by construction, with no explicit `-p:ContinuousIntegrationBuild=true` needed and
nothing left to decide. §9's two-options framing still applies, unchanged, to the one path that
remains genuinely local and human-run: packing by hand before a tag exists, to run the
`intest --help` smoke test in "Verify, then tag" below — that local pack is exactly the
non-deterministic case §9 described, and choosing to accept that for a pre-release smoke artifact
(never the one actually pushed) is the honest position, not an oversight.

## Implemented — revision 7

Landed in one change: `Directory.Build.props` (§2 symbol/SourceLink properties, §6
`EnablePackageValidation`), `src/InTest.Cli/InTest.Cli.csproj` and
`src/InTest.Runtime/InTest.Runtime.csproj` (§3 `Description`/`PackageTags`, §4
`PackageReadmeFile` + `None` item, §5 the `THIRD-PARTY-NOTICES.md` `None` item on `InTest.Cli`
only), `src/InTest.Cli/README.md` and `src/InTest.Runtime/README.md` (new, §4),
`THIRD-PARTY-NOTICES.md` (§5, `Microsoft.OpenApi` corrected to `3.10.2`), `.gitattributes` (§10,
`*.png binary` / `*.svg text eol=crlf` — `eol=crlf` because the repo's convention flipped after
this spec's earlier revisions, per §10's own note), and the new "Publishing checklist" subsection
in `CONTRIBUTING.md` (§8), placed after "Branching and how a release is cut" rather than
immediately after "Releases" since it assumes a release has already been cut.

`scripts/local-e2e-test.ps1` needed **two** further fixes beyond the one §1 already records, both
the identical failure mode: the scratch copy mirrors `src/InTest.Cli`, `src/InTest.Runtime` and a
short list of repo-root files each project reaches by a `../../`-relative path, and every file
newly referenced that way has to be added to that list or `dotnet pack` fails outright.
`THIRD-PARTY-NOTICES.md` was the second one, failing `NU5019: File not found` until added
alongside `assets/`. Reproduced, fixed, reproduced clean — same pattern as §1's `assets/` fix.

**Verified, not assumed:**
- `dotnet build InTest.sln -c Release` and `dotnet test InTest.sln -c Release`: all four suites
  green (732 passing — up from the 658 §11 cites, from unrelated work landed on `main` since).
- `pwsh scripts/local-e2e-test.ps1`: full adoption path green, including the pack step that
  exercises every property/item added here.
- **§11's "unzip both `.nupkg` and assert contents" was performed for real**, not deferred to
  first publish: a throwaway `dotnet pack` of both projects (own `NUGET_PACKAGES` redirect, never
  restored, never touches the real global cache) followed by `unzip` on both `.nupkg`, confirming
  by direct inspection: `InTest.Cli`'s package root contains `icon.png`, `README.md`, and
  `THIRD-PARTY-NOTICES.md` alongside `tools/`; `InTest.Runtime`'s contains `icon.png` and
  `README.md` alongside `lib/`; both nuspecs carry a populated
  `<repository type="git" url="https://github.com/Tjayojo/intest.git" commit="…">` — confirming §2's
  "Source Link already works with no reference" claim at the packaged-artifact level, not just in
  principle. The "package is missing a readme" `dotnet pack` warning that appeared before
  `PackageReadmeFile` was wired is gone.
- §7's scaffold defect and §9's deterministic-build question were **not** touched by this change —
  both were already resolved separately (the trunk-based-versioning plan, and this change's own
  `CONTRIBUTING.md` checklist step 5 documenting the `-p:ContinuousIntegrationBuild=true` fallback
  for an ad hoc local release pack).

**Not done, and out of scope for this change specifically:** the checklist items that require an
actual publish (ID reservation, nuget.org account hygiene, the real `dotnet nuget push`, flipping
`README.md`'s "Status: v0" callout, and the baseline-version step for the release after the
first) — all still exactly where §8 leaves them, waiting on a human to run them. — §8's publishing checklist still assumed a manual local `dotnet
pack` as the only way to get an artifact. By the time this revision was written, the versioning
plan's Task 3 (`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`) had shipped
`.github/workflows/pack.yml`, which already packs and verifies both projects in CI on every merge
to `main` and every tag — unpublished, but a real artifact, not a hypothetical one. §8 is
rewritten below to make CI-then-download the ordinary path and a manual `dotnet pack` the
fallback, without claiming anything is published or that `dotnet tool restore` works from a bare
clone — neither is true. Revision 4's supersession of `develop`/`main` (below) and revision 2's
errors (below) are unchanged from revision 5.

**Revision 4:** §7 recorded *"decided: prerelease from `develop`, release from
`main`"* as settled. The owner subsequently chose trunk-based versioning with tag-driven releases
instead — one branch, `main`, continuous; a tag marks a release; `release/N.x` cut on demand only
when an old major needs servicing
(`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`, `[tag-is-the-release]`). §7 is
rewritten for that below, in the style it already used for revision 3. Revision 2's errors are
recorded below. Revision 1 deferred `PackageIcon`; revision 2 brought it in scope; it has since
**shipped**.

## What revision 4 got wrong

Two things, both inside §7, both surfaced by the versioning plan rather than by re-reading this
spec.

1. **The `develop`/`main` decision recorded as settled was itself reconsidered before this spec's
   ink dried.** See the Supersedes note above and §7 below for the replacement,
   `[tag-is-the-release]`.
2. **§7's "what this touches" list claimed a prerelease channel would make adopters "hit exit 4
   frequently and run `upgrade` to clear it."** That is wrong, independent of which branching model
   ships: `.config/dotnet-tools.json` pins the CLI version and CI runs `dotnet tool restore`, so CI
   runs the pinned version by construction, and versions differ only when someone bumps the pin —
   which `upgrade` does alongside `intestVersion` (v1-e plan, `[exact-match]`). A fast-moving
   channel changes nothing about how often exit 4 fires. Retracted in §7 below rather than quietly
   dropped.

## What revision 2 got wrong

Recorded rather than quietly corrected, because the reasoning that produced an error is the
reasoning most likely to recur. Every item below was established by running or by reading SDK
source, not by re-reading the spec.

1. **§9 was already implemented when revision 2 was written, and following it fails the build.**
   HEAD `e484b38` *is* the icon commit. Adding the `None Include` a second time produces
   `NU5118: File 'icon.png' is not added because the package already contains file '\icon.png'`,
   which `TreatWarningsAsErrors=true` promotes to a **hard pack failure**. Reproduced.
2. **The central SourceLink premise is inverted on .NET 10.** The SDK ships SourceLink in-box, and
   `Microsoft.NET.Sdk.SourceLink.props:17` reads *"Suppress implicit SourceLink inclusion if any
   Microsoft.SourceLink package is referenced."* Adding the `PackageReference` **disables** what is
   already working. Measured: a clone at HEAD with no reference and none of §2's properties already
   emits a complete Source Link map and `<repository type="git" commit="…">`. Revision 2's
   "entirely absent today" was false.
3. **Package validation can never run on `InTest.Cli`.** `Microsoft.NET.PackTool.targets:47`
   hard-sets `EnablePackageValidation=false` for tool packages. Confirmed by reading the SDK and by
   `dotnet pack -v diag`.
4. **Prerelease versioning was never raised** — not as a gap, not as out of scope — despite
   versioning.md being cited as read in full. §7 now decides it.
5. **`THIRD-PARTY-NOTICES.md` appeared nowhere**, though the repository already carries the file
   *and* the statement of the obligation it discharges.
6. **Two citation errors**, the defect class `CONTRIBUTING.md` already names. Corrected in place.
7. **Section numbering skipped §8.** Renumbered.

## 1. Purpose

Bring `InTest.Cli` and `InTest.Runtime` up to Microsoft's [.NET library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/)
before the first real `dotnet nuget push`. This is a metadata/tooling readiness pass, not the
publish itself — nobody runs `dotnet nuget push` as part of this change, and no CI pipeline gains
the ability to.

Findings cite the specific guidance article each change satisfies. **Ten** articles were read
(get-started, cross-platform-targeting, strong-naming, nuget, dependencies, sourcelink,
publish-nuget-package, versioning, breaking-changes, nuget-package-compatibility-rules) and checked
against the repo's actual state. Revision 2 said "nine" and listed ten.

### Already landed — no action

- **`PackageIcon` and the icon assets (was §9).** `Directory.Build.props:25` sets
  `PackageIcon`; both packable csprojs carry `<None Include="../../assets/icon.png" Pack="true" />`;
  `assets/icon.png` (64×64, RGBA, transparent — satisfying nuget.md) and `assets/icon.svg` are
  committed. `scripts/local-e2e-test.ps1` was also patched to copy `assets/` into its scratch tree,
  without which packing fails. **Do not re-add any of this.**

### Already compliant — no action

- MIT `LICENSE` + `PackageLicenseExpression=MIT` on both packages.
- `net10.0`-only targeting satisfies cross-platform-targeting.md's "DO start with `net8.0` or
  later". Strong naming is N/A — strong-naming.md is explicit it has no benefit on .NET Core/5+.
  `net10.0`-only is a non-negotiable v1 constraint (`CLAUDE.md`), not revisited here.
- `Directory.Packages.props` uses plain minimum versions throughout — not exact (`[3.10.2]`) or
  upper-bounded — matching dependencies.md's trio of rules. Confirmed by reading the file.
- `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl` set centrally in
  `Directory.Build.props` and inherited by both packages.
- **Source Link already works**, in-box, with no reference and no configuration — see §2.
- SemVer and breaking-change policy documented (`CONTRIBUTING.md` "Releases").

### Real gaps this change closes

1. **No symbol package, and untracked sources not embedded.** sourcelink.md's "CONSIDER publishing
   symbol files". `IncludeSymbols`, `SymbolPackageFormat` and `EmbedUntrackedSources` are absent.
   Source Link itself is **not** a gap (§2).
2. **Missing metadata.** `InTest.Cli` has no `Description`; neither package has `PackageTags`;
   neither sets `Title` (it defaults to `PackageId`, so impact is near zero, but nuget.md's core
   table is walked item by item here and it belongs in the list).
3. **No package-page README.**
4. **No package validation on `InTest.Runtime`** — and it is *impossible* on `InTest.Cli` (§6).
5. **`THIRD-PARTY-NOTICES.md` is not packed**, and it records a stale dependency version (§5).
6. **`assets/` is unpinned in `.gitattributes`** (§10).

### Explicitly out of scope

- **CI publish workflow.** ~~`dotnet pack` / `dotnet nuget push` stays manual and local. See §9,
  which records what that costs.~~ **Superseded, revision 8:** a CI publish workflow now exists —
  `.github/workflows/release.yml`, built once NuGet Trusted Publishing removed the premise that
  made this out of scope (no safe place for the owner's API key to live in CI). See "Implemented —
  revision 8" above and `[publish-stays-manual]`'s own superseded record in the versioning plan for
  the full argument. Struck through rather than deleted, per this document's own convention for
  recording what changed and why, not just what is true now.
- **NuGet.org account security** (2FA, email-on-publish) — account settings, nothing in the repo
  can satisfy them. In the §8 checklist as a one-time manual step.
- **Root README's "Status: v0" callout** (`README.md:12-41` — revision 2 said 12-38) is accurate
  today. Flipping it is a publish-time edit, captured in §8.
- **`PublicApiAnalyzers` / `PublicAPI.Shipped.txt`.** Revision 2 rejected these *because* package
  validation covers the same risk. That reason is wrong for `InTest.Cli` (§6). The conclusion still
  holds, for a different reason: the CLI's semver surface per `CONTRIBUTING.md` is commands, flags
  and exit codes, which ApiCompat cannot check anyway.

## 2. Source Link — already working, do not add the package

**Do not add `Microsoft.SourceLink.GitHub`.** The .NET 10 SDK imports it implicitly, and
`Microsoft.NET.Sdk.SourceLink.props:17` suppresses that import the moment any
`Microsoft.SourceLink.*` package is referenced. Adding the reference therefore replaces a working
in-box mechanism with an out-of-band 8.0.0 copy, adds a Dependabot-tracked dependency for something
the SDK ships, and runs against both `CONTRIBUTING.md`'s dependency policy and dependencies.md's
"DO review your .NET library for unnecessary dependencies".

> **The verification trap, recorded so nobody repeats it.** A scratch clone whose `origin` is a
> local filesystem path cannot be recognised by SourceLink.GitHub, so Source Link looks absent
> there. That is what made revision 2's "entirely absent today" seem confirmed. Verify against a
> clone whose `origin` is the real GitHub URL.

Add to `Directory.Build.props`, alongside the existing centrally-set metadata:

```xml
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
```

`PublishRepositoryUrl` is **not** set: `RepositoryUrl` is already hand-set in
`Directory.Build.props`, so it would be a no-op. `RepositoryType` is not hand-set either — Source
Link emits `type="git"` itself, confirmed in both packed nuspecs.

**`snupkg` over embedded PDBs, with the rationale corrected.** For `InTest.Runtime` the separate
symbol package keeps the main package small. **For `InTest.Cli` it does not**: tool packing uses
`dotnet publish` output, which includes `InTest.Cli.pdb` under `tools/net10.0/any/` regardless, so
the `.snupkg` duplicates a file that ships either way. Revision 2 claimed the size benefit for both.
Keep `IncludeSymbols` on both anyway — a 22 KB duplicate is cheaper than a second divergent
configuration — but do not claim a benefit the tool package does not get.

> **Resolved by revision 9:** nuget.org accepts a snupkg whose PDBs sit under `tools/` rather
> than `lib/`. symbol-packages-snupkg.md's stated constraints (extension, portable PDB, compiler
> version) are all satisfied and it does not require `lib/`, which is why rejection was never
> expected — and the `0.1.0-preview.1` push confirmed it for real: both packages' `.snupkg`
> files were pushed and accepted. See "Implemented — revision 9" above.

## 3. Package metadata

`src/InTest.Cli/InTest.Cli.csproj` gains:

```xml
<Description>Generates a committed, owned MSTest project that exercises a deployed API over real HTTP, from its OpenAPI document.</Description>
<PackageTags>testing;mstest;openapi;api-testing;dotnet-tool;test-generation</PackageTags>
```

`src/InTest.Runtime/InTest.Runtime.csproj` gains:

```xml
<PackageTags>testing;mstest;openapi;api-testing;test-runtime</PackageTags>
```

(`InTest.Runtime` already has a `Description`.) Tag wording is a judgment call, not
guidance-mandated. `Title` is left unset on both — it defaults to `PackageId`, which is what we
would set it to.

## 4. Per-package READMEs

New `src/InTest.Cli/README.md` and `src/InTest.Runtime/README.md`, each short and package-specific
rather than a copy of the root README: what the package is, one-line install/usage, and a link to
the repo and `docs/getting-started.md`.

**Every link must be an absolute `https://github.com/Tjayojo/intest/…` URL.** Package READMEs
render on nuget.org with no repository context, so a relative link is permanently dead — and the
README ships inside the `.nupkg` forever, with no mechanism to fix it later.

Written neutrally, with no "not published yet" language, for the same immutability reason: today's
true statement becomes permanently wrong the day the package goes live.

Wired on both projects:

```xml
<PackageReadmeFile>README.md</PackageReadmeFile>
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

Confirmed by packing: README and icon both work for a `PackAsTool` package, not just a library.

## 5. Third-party notices — a licence obligation, not tidiness

`InTest.Cli` sets `PackAsTool=true`, so the package **bundles** `Microsoft.OpenApi.dll`,
`NJsonSchema.dll`, `NJsonSchema.Annotations.dll`, `Namotion.Reflection.dll`, `Newtonsoft.Json.dll`,
`Scriban.dll` (**BSD-2-Clause**), `System.CommandLine.dll` and 13 satellite assemblies — verified by
unzipping the built package. MIT and BSD-2-Clause both require their notice to accompany binary
redistribution, and `PackageLicenseExpression=MIT` covers InTest's own code only.

`THIRD-PARTY-NOTICES.md` exists at the repo root and states this obligation. It is not packed.

**Fix the file first, then pack it.** It records `Microsoft.OpenApi 3.10.0`; `Directory.Packages.props`
now pins **3.10.2** after the Dependabot bump. Packing as-is freezes a wrong version into an
immutable artifact.

Add to `src/InTest.Cli/InTest.Cli.csproj` **only** — `InTest.Runtime`'s package contains just its
own DLL and declares its dependencies for NuGet to resolve, so nothing is redistributed:

```xml
<None Include="../../THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="\" />
```

## 6. Package validation

Add to `Directory.Build.props`:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
```

**This affects `InTest.Runtime` only.** `Microsoft.NET.PackTool.targets:47` hard-sets
`EnablePackageValidation=false` for tool packages, with the comment *"Tool packages are not
library/API-providing packages, so they should not participate in API Compat validation at all."*
Confirmed by reading the SDK and by `dotnet pack -v diag`: validation runs for the Runtime and does
not run for the CLI. No property can turn it on. Revision 2's "future-proofs… the moment either
becomes true" was wrong for the CLI, as is any post-0.1.0 baseline step aimed at it.

Harmless on the Runtime today — single TFM, no published baseline to diff — and it begins enforcing
once one exists. `PackageValidationBaselineVersion` is deliberately not set: the baseline check
needs a *previously published* version. §8 captures it for the release after the first.

## 7. Versioning — decided: trunk-based, tag-driven releases (supersedes `develop`/`main`)

> **This section's revision-4 decision is superseded.** Revision 4 recorded *"decided: prerelease
> from `develop`, release from `main`"*. The owner reconsidered and chose trunk-based versioning
> with tag-driven releases instead, argued and measured in full in
> `docs/superpowers/plans/2026-08-23-trunk-based-versioning.md` (`[tag-is-the-release]`). Recorded
> here rather than silently replaced — the same style this section already used for revision 3's
> "was an open question, now decided", and the style the versioning plan's own revision notes use.
>
> **Why the change, argued at length in the plan and only summarised here:** a second long-lived
> branch encodes the same fact a tag already encodes — a commit on `main` that has not been tagged
> is neither clearly released nor clearly not, so `develop` and a tag become two sources of truth
> that can disagree. `develop`/`main` earns its cost once a shipped release must be patched while
> the next one is developed; **there are zero shipped releases**, so that need is not live yet. It
> is real later — `CONTRIBUTING.md`'s 12-month previous-major support commitment guarantees it —
> and is served then by cutting `release/N.x` **on demand**, not by maintaining a permanent branch
> against a need that has not arrived. Verified against practice, not only argued: `dotnet/runtime`
> is trunk-based with `release/*` cut on demand, and carries no `develop` branch.

versioning.md: **"DO include a prerelease suffix when releasing a nonstable package."** The repo's
own words agree — `README.md` says "v0. Working, but early", four commands are unbuilt, and
`docs/v0-acceptance.md` records that `generate --check` and `upgrade` have no multi-sample
acceptance run.

**The decision:** one branch, `main`, protected and continuous. Every merge to it produces a
versioned artifact — not a *published* one; a merge alone never reaches nuget.org. A tag matching
a SemVer shape is what marks a release, and — since revision 8 — a tag is also what actually
*publishes* it: `.github/workflows/release.yml` pushes both packages automatically once a tag
lands, via NuGet Trusted Publishing rather than the manual step `[publish-stays-manual]` used to
require (that decision is superseded; see its own record in the versioning plan). `git tag
0.1.0-preview.1` for a publishable preview, `git tag 0.1.0` for the first stable release. **Every
merge produces a versioned artifact; every tag produces a published one.** `release/N.x`
branches exist only once an old major needs servicing while `main` has moved past it, and are cut
on demand — not maintained continuously alongside `main`.

The honest tradeoff, carried over from the plan rather than re-argued here: under this model
`main` is *continuous*, not "the released thing". The last tag is the released thing, and `main`
runs ahead of it.

**How the version itself is produced is also decided, and it is not a hand-rolled suffix.** There
is no CI-injected `-p:VersionSuffix` and no counter for CI to choose. MinVer
(`[version-from-git]` in the versioning plan) derives `Version` directly from git tags and commit
height: untagged commits on `main` get `0.1.0-preview.0.<height>`; a commit exactly on a tag
`0.1.0` gets `0.1.0` with no height; a commit after that tag gets `0.2.0-preview.0.<height>`
(minor auto-increment). `CliVersion.cs:47`'s first-`+`-only strip still applies unchanged —
confirmed during v1-e Task 1 by building at `-p:Version=1.0.0-rc.1`, and MinVer stamps
`InformationalVersion` the same shape. Anywhere else this document implies a hand-rolled,
CI-injected suffix mechanism, that implication is superseded by this paragraph.

### The defect this decision exposes, which blocks it

`InitCommand.cs:232` **hardcodes** the scaffolded reference:

```xml
<PackageReference Include="InTest.Runtime" Version="0.1.0" />
```

A prerelease CLI built as `0.1.0-preview.N` would therefore scaffold a project referencing
`InTest.Runtime` **0.1.0** — a version that does not exist on nuget.org until the first stable
release. **The generated project cannot restore.** That is a shipped tool producing unusable
output, and it is the `[paired]` shape this repository has now hit nine times: a documented path
with no reachable fix.

**The scaffold must emit the running CLI's own version rather than a literal.** `CliVersion.Current`
is already the value `init` writes to `intestVersion` (`InitCommand.cs:204`), so the data is in
hand at the point the `.csproj` string is built.

Note this is *stricter* than §3's compatibility contract, which permits any CLI `N.y` with any
runtime `N.x`. Emitting the exact version is safe within that contract and removes a hand-maintained
literal; a looser floor would be defensible but is not required.

### What this touches beyond the scaffold

Rewritten against the real model — each item below was re-verified against the repository as it
stands today (2026-08-23), not carried forward from the `develop`/`main` draft:

- **`PackageVersionCouplingTests` compares the scaffold's literal against `Directory.Build.props`'
  `<Version>`.** Unaffected by the branching change, still real: once the scaffold emits
  `CliVersion.Current`, there is no literal to compare and the guard's `InTest.Runtime` case needs
  rewriting against the new mechanism. **Do not delete it** — it is what would have caught this
  defect. (Versioning plan, Task 1 Step 2 and Task 2 Step 2.)
- **The suffix mechanism this list used to ask for does not exist to build.** Superseded by
  `[version-from-git]` above: MinVer reads git tags and history directly, so there is no per-build
  suffix to inject from CI and no counter to choose.
- **`develop` does not need creating.** Re-verified today (`git branch -a`): it still does not
  exist, and under this decision it never will — there is exactly one long-lived branch.
- **CI triggers need no change on this account.** Re-verified today
  (`.github/workflows/build-and-test.yml`): triggers remain `push: [main]` and `pull_request`, with
  no `develop` job to add. (The versioning plan's Task 3 adds a tag trigger for release-artifact
  production — a genuinely new trigger, but not the `develop` one this list used to ask for.)
- **Dependabot needs no `target-branch` change on this account.** Re-verified today
  (`.github/dependabot.yml`): it sets no `target-branch`, so it already opens PRs against the
  default branch, `main` — exactly where trunk-based wants them. There was never a `develop` to
  retarget away from.
- **Retracted — the exit-4 claim was wrong, independent of which branching model ships.** This
  list previously said: *"`--check`'s `[exact-match]` exits 4 on any version difference... A
  prerelease channel that moves on every merge means adopters tracking it hit exit 4 frequently
  and run `upgrade` to clear it."* **That is false.** `.config/dotnet-tools.json` pins the CLI
  version and CI runs `dotnet tool restore`, so CI runs the pinned version by construction —
  versions differ only when someone bumps the pin, and `upgrade` bumps it alongside `intestVersion`
  in the same edit (v1-e plan, `[exact-match]`). A fast-moving prerelease channel does not change
  how often exit 4 fires; the claim's premise — that the channel's speed matters here — was wrong
  from the start. Recorded rather than quietly dropped, per this repo's own convention for a
  plausible-but-wrong claim (see "What revision 4 got wrong" above).

### Scope

This section records the decision and its consequences. **Implementing it — the tag trigger, the
MinVer wiring, the scaffold fix, the on-demand `release/N.x` process — is not part of this
readiness pass.** `docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`'s Tasks 1 through 4
are where that happens; this readiness pass is package metadata, not the workflow change. What
*is* in scope here: the scaffold defect above must be fixed before any prerelease is published,
because publishing a CLI that scaffolds unrestorable projects is worse than not publishing.

## 8. `CONTRIBUTING.md`: publishing checklist

New subsection between "## Releases" and "## Testing against a local build". **Revision 8:
rewritten again.** Revisions 5–7 assumed the checklist's final step was always a human running
`dotnet nuget push` by hand; that assumption is gone now that `.github/workflows/release.yml`
performs the push automatically on a tag push, using NuGet Trusted Publishing (OIDC) — see
"Implemented — revision 8" above. `CONTRIBUTING.md`'s "Publishing checklist" section is the
maintained, operational copy of this list; what follows here is the shape of it and the reasoning,
not a second copy to keep byte-for-byte in sync.

**One-time setup**, before the first tag push can succeed at all:

1. §7 is decided and implemented — trunk-based, tag-driven releases, MinVer-derived versions, the
   scaffold defect fixed and guarded by `PackageVersionCouplingTests`. Nothing to do here beyond
   "tests are green."
2. Reserve the `InTest.` **ID prefix** with NuGet (nuget.md's "CONSIDER choosing a package name
   with a prefix that meets NuGet's prefix reservation criteria"). Unrelated to trusted publishing
   — the policy binds an *owner*, not a reserved ID — so this step protects the prefix from being
   claimed by someone else, nothing more.
3. One-time nuget.org account hygiene: Microsoft account sign-in, two-factor authentication,
   "email me when a package is published".
4. **Create the nuget.org Trusted Publishing policy**: Repository Owner, Repository, **Workflow
   File as the bare file name `release.yml`**, **Environment `nuget-release`**, and a Package owner
   selection. This is a gradual nuget.org rollout — **not yet confirmed available on this
   account.** Every field must match `release.yml` exactly, especially Environment: it is what
   makes the policy resistant to someone editing the workflow file and pushing a tag on their own
   branch (see that file's header comment, "The security shape").
5. **Create the matching `nuget-release` GitHub Environment** in this repository's settings, and
   strongly consider **required reviewers** on it — with the push itself automated, this is the
   only remaining place a human can be made to look before an irreversible action, unless "Verify,
   then tag" below (step 8) is followed manually every time.
6. **Add a `NUGET_TRUSTED_PUBLISHING_USER` variable** (not a secret) naming the nuget.org profile
   the policy names as owner. `release.yml` reads it as
   `vars.NUGET_TRUSTED_PUBLISHING_USER`.

**Per release:**

7. **Clear the local NuGet cache** (`dotnet nuget locals global-packages --clear`, or delete
   `~/.nuget/packages/intest.*`) before testing anything from this release. NuGet caches by exact
   version and never re-fetches, so a stale entry silently shadows the real package regardless of
   where the fresh one came from.
8. **Verify, then tag.** Let `.github/workflows/pack.yml` pack and verify the merge to `main` you
   intend to release — this now runs the same artifact-content assertions `release.yml`'s own pack
   job will run (`scripts/ci/pack-and-verify.ps1`'s `Assert-PackageArtifactContents`: `README.md`,
   `icon.png`, `THIRD-PARTY-NOTICES.md` presence/absence, a non-empty `<repository …
   commit="…">`). Download that artifact and do the one check nothing automated performs:
   `dotnet tool install --global --add-source <dir> InTest.Cli --version <v>` and run
   `intest --help`. Only then tag the commit and push the tag. This ordering is what puts a human
   verification step *before* the irreversible push under an otherwise-automated flow, using an
   artifact `pack.yml` already produced from the same commit — it does not require intercepting
   `release.yml`'s own pack job mid-run.
9. `release.yml` packs and pushes both `.nupkg` and `.snupkg` automatically. **This remains
   entirely unexercised as of this task** — see this task's own report for exactly what was and
   was not tested locally. Confirm the run went green and the version appears on nuget.org; this is
   also the first real confirmation of whether nuget.org accepts a `.snupkg` whose PDBs sit under
   `tools/` rather than `lib/` (§2).
10. Flip `README.md`'s "Status: v0 … nothing published yet" callout (`README.md:12-41`).
11. For the release *after* the first: set `PackageValidationBaselineVersion` on `InTest.Runtime`
    only (§6).

## 9. Deterministic builds — the cost of publishing locally

sourcelink.md's third recommendation is "CONSIDER enabling deterministic builds".
`ContinuousIntegrationBuild` is gated on `GITHUB_ACTIONS` so that local builds stay reproducible in
the way local builds should be — but publishing is deliberately manual and local (§1), so **the
only builds that ever ship are the ones without it**, carrying the maintainer's absolute paths in
the Source Link map.

Revision 2 set the gate and did not notice the consequence. Two honest options: set
`ContinuousIntegrationBuild=true` explicitly for the release pack (§8's "Get the artifact" step, as
it existed through revision 7), or record that non-deterministic local release builds are
acceptable for now. **Do not leave it implied.**

> **Revision 8: resolved, not just narrowed, for the artifact that actually ships.** §1's premise
> — "publishing is deliberately manual and local" — is superseded (see "Implemented — revision 8"
> above). `release.yml`'s `pack` job runs inside GitHub Actions, where `GITHUB_ACTIONS` is always
> `"true"`, so `Directory.Build.props`'s existing `ContinuousIntegrationBuild` condition fires
> automatically for the pack that is actually pushed — no explicit
> `-p:ContinuousIntegrationBuild=true` needed, and nothing left to decide for that artifact. The
> two-options framing above still governs the one pack that remains genuinely local and human-run:
> the pre-tag smoke-test pack in §8 step 8, which is deliberately never the artifact that gets
> pushed. Accepting non-determinism there, for a throwaway verification build, is the position this
> document takes — it was always the honest one; it just used to also apply, wrongly, to the
> artifact that shipped.

## 10. `.gitattributes` — the new assets are unpinned

`git check-attr` reports `assets/icon.png` and `assets/icon.svg` as `text: auto`, in a repository
whose `.gitattributes` explicitly pins `*.g.cs.txt`, `*.scriban` and `*.cs` because their bytes are
data — a rule earned by a `windows-latest` CI failure days ago.

Measured: the **PNG is byte-identical** across an `autocrlf=true` clone, because Git's binary
heuristic sees the NULs and skips conversion. So the packaged icon is safe *by accident*, not by
the repo's stated convention. The **SVG genuinely differs** between CRLF and LF clones — it is not
packed, but it is the master the PNG is rendered from, so regenerating on a CRLF checkout starts
from different bytes.

Add `*.png binary` and `*.svg text eol=crlf`. **Note the repo flipped its line-ending
convention after revision 3 was written** — `.gitattributes` is now `* text=auto eol=crlf` with
`*.cs text eol=crlf`, so pinning the SVG to LF would now be the odd one out. The requirement is
that the byte is *pinned*, not which byte it is.

## 11. Verification

- `dotnet build InTest.sln` and `dotnet test InTest.sln` — all four suites, currently 658 passing.
- `pwsh scripts/local-e2e-test.ps1` — the repo's sanctioned pack-and-restore path
  (`CONTRIBUTING.md`, "Testing against a local build"). Confirms the new `PackageReadmeFile`,
  `None` items and notices file do not break packing, and that a scaffolded restore still succeeds.
- Unzip both `.nupkg` and assert contents, per §8 step 8 (revision 8 renumbered this from step 6;
  the same assertions now also run automatically in CI via
  `scripts/ci/pack-and-verify.ps1`'s `Assert-PackageArtifactContents`, proven this task by
  fabricating five corrupted copies of a real pack — a missing `README.md`, a missing `icon.png`,
  a missing `THIRD-PARTY-NOTICES.md` on `InTest.Cli`, an extra one on `InTest.Runtime`, and an
  emptied `<repository commit="">` — and confirming each one throws).

> **Packing has only ever been exercised on Windows.** CI's three jobs (`fast`, `golden`,
> `dogfood`) do not pack, and the only pack path is a PowerShell script outside CI. If the first
> publish is done from Windows this is moot; if not, `PackagePath="\"` and the `None` items should
> be exercised on Linux first. Recorded rather than assumed either way.

## 12. Files touched

- `Directory.Build.props` — `EmbedUntrackedSources`, `IncludeSymbols`, `SymbolPackageFormat`,
  `ContinuousIntegrationBuild`, `EnablePackageValidation`.
- `src/InTest.Cli/InTest.Cli.csproj` — `Description`, `PackageTags`, `PackageReadmeFile`, README
  `None` item, `THIRD-PARTY-NOTICES.md` `None` item.
- `src/InTest.Runtime/InTest.Runtime.csproj` — `PackageTags`, `PackageReadmeFile`, README `None`
  item.
- `src/InTest.Cli/README.md`, `src/InTest.Runtime/README.md` — new.
- `THIRD-PARTY-NOTICES.md` — correct `Microsoft.OpenApi` to 3.10.2.
- `.gitattributes` — `*.png binary`, `*.svg text eol=crlf` (matching the repo's current
  convention — see §10).
- `CONTRIBUTING.md` — new "Publishing checklist" subsection.

**Not touched, contrary to revision 2:** `Directory.Packages.props` (no SourceLink entry — §2),
and nothing icon-related (already landed — §1).
