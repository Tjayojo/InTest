# NuGet publish readiness

**Status:** Design · Revision 1
**Date:** 2026-08-23

## 1. Purpose

Bring `InTest.Cli` and `InTest.Runtime` up to Microsoft's [.NET library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/)
before the first real `dotnet nuget push`. This is a metadata/tooling readiness pass, not the
publish itself — nobody runs `dotnet nuget push` as part of this change, and no CI pipeline gains
the ability to.

Findings below cite the specific guidance article each change satisfies. All nine articles under
the guidance index were read in full on 2026-08-23 (get-started, cross-platform-targeting,
strong-naming, nuget, dependencies, sourcelink, publish-nuget-package, versioning,
breaking-changes, nuget-package-compatibility-rules) and checked against the repo's actual current
state, not assumed.

### Already compliant — no action

- MIT `LICENSE` + `PackageLicenseExpression=MIT` on both packages (nuget.md's "every package on
  NuGet.org should provide" table).
- `net10.0`-only targeting satisfies cross-platform-targeting.md's "DO start with `net8.0` or
  later for new libraries." Strong naming and multi-targeting guidance is N/A: both packages
  target .NET (Core)/5+ only, and strong-naming.md is explicit that strong naming has no benefit
  there. `net10.0`-only is also a stated non-negotiable constraint (CLAUDE.md), not something this
  change revisits.
- `Directory.Packages.props` uses plain minimum versions throughout (e.g. `Version="3.10.2"`), not
  exact (`[3.10.2]`) or upper-bounded (`[3.10.2,4.0)`) ranges — exactly what dependencies.md's "DO
  NOT have package references with no minimum version" / "AVOID... exact version" / "AVOID...
  upper limit" trio is checking for. Confirmed by reading the file, not assumed.
- `Authors`, `Copyright`, `PackageProjectUrl`, `RepositoryUrl` already set centrally in
  `Directory.Build.props` and inherited by both packages.
- SemVer and breaking-change policy already documented (`CONTRIBUTING.md` "Releases" section).

### Real gaps this change closes

1. **No SourceLink, no symbol package.** sourcelink.md: "CONSIDER using Source Link" and
   "CONSIDER publishing symbol files." Entirely absent today — no `Microsoft.SourceLink.*`
   reference, no `PublishRepositoryUrl`/`EmbedUntrackedSources`/`ContinuousIntegrationBuild`, no
   `IncludeSymbols`/`SymbolPackageFormat`.
2. **Missing core metadata.** nuget.md's metadata table: `InTest.Cli` has no `Description`;
   neither package has `PackageTags`. (`RepositoryType` is *also* in that table, but SourceLink
   sets it automatically once wired — see §3 below — so it is deliberately not hand-set.)
3. **No package-page README.** Not an explicit Do/Consider in the fetched articles, but the
   nuget.md metadata table's `Description` guidance and general NuGet.org practice both point at
   it, and the repo already has the raw material (root README).
4. **No package validation.** nuget-package-compatibility-rules.md: "CONSIDER enabling Package
   Validation... to automatically detect binary breaking changes between releases."

### Explicitly out of scope (asked and answered)

- **PackageIcon.** No logo exists yet; the user intends to design one separately via Claude
  Design. Not blocking — publish-nuget-package.md doesn't require an icon, and it can be added in
  a later release without being a breaking change. A separate follow-up can pick this up once a
  logo exists.
- **CI publish workflow.** No `.github/workflows/publish.yml`. The actual `dotnet pack` /
  `dotnet nuget push` stays a manual, local step for now, matching the project's existing
  posture (`CONTRIBUTING.md`'s `scripts/local-e2e-test.ps1` rule already treats local
  pack/restore as something to route through tooling deliberately, not hand-roll).
- **NuGet.org account security** (2FA, email-on-publish, Microsoft account sign-in) —
  publish-nuget-package.md's "DO enable two-factor authentication" etc. are account-settings
  actions on nuget.org itself. Nothing in the repo can satisfy these; noted in the new publishing
  checklist (§6) as a manual one-time step for the user.
- **Root README's "Status: v0 — nothing published yet" callout** (`README.md:12-38`) is accurate
  right now and stays as-is. Flipping it is a publish-time edit, not a readiness edit — captured
  as a checklist step in §6 instead of changed prematurely.
- **`PublicApiAnalyzers` / `PublicAPI.Shipped.txt`.** Not mentioned in any fetched guidance
  article; package validation (§5) already covers the compatibility risk it would address. Adding
  a second, overlapping mechanism is unjustified scope.

## 2. SourceLink and symbols

Add to `src/InTest.Cli/InTest.Cli.csproj` and `src/InTest.Runtime/InTest.Runtime.csproj` only —
not repo-wide — because SourceLink and symbol packaging are meaningless for the six projects that
already set `IsPackable=false`:

```xml
<PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
```

with the version added to `Directory.Packages.props`.

Add to `Directory.Build.props`, alongside the existing centrally-set package metadata (same
rationale already documented there: "both packed projects want the same values"):

```xml
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
```

`snupkg` over embedded PDBs per sourcelink.md/nuget.md's explicit tradeoff: embedding grows the
main package ~30%; a separate symbol package keeps `InTest.Cli`/`InTest.Runtime` small and pushes
the cost onto the (rare) debugging session instead of every restore. `RepositoryType` is not
hand-set: sourcelink.md states Source Link "automatically adds `RepositoryUrl` and
`RepositoryType` metadata," and the repo already hand-sets `RepositoryUrl` in
`Directory.Build.props` — SourceLink is compatible with a pre-set `RepositoryUrl` and fills in
`RepositoryType=git` itself, so hand-setting it too would just be a second place to keep in sync
for no benefit.

`ContinuousIntegrationBuild` is gated on `GITHUB_ACTIONS` (the repo's actual, only CI) rather than
set unconditionally, because forcing it locally makes local `dotnet build`/`dotnet pack` output
non-reproducible in ways that are only correct for the CI environment.

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

(`InTest.Runtime` already has a `Description`.) Tag wording is a judgment call, not guidance-
mandated — flag on review if different tags read better.

## 4. Per-package READMEs

New `src/InTest.Cli/README.md` and `src/InTest.Runtime/README.md`, each short (package-specific,
not a copy of the root README), covering: what the package is, one-line install/usage, and a link
back to the root repo README and `docs/getting-started.md` for full documentation. Written
neutrally — no "not published yet" language — because unlike the root README, package READMEs
ship inside the `.nupkg` forever once published; today's true statement becomes a permanently
wrong one the day the package actually goes live, with no mechanism to force an edit at that
moment. This was the user's explicit call over reusing the root README or skipping
`PackageReadmeFile` entirely.

Wired via, on both projects:

```xml
<PackageReadmeFile>README.md</PackageReadmeFile>
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

## 5. Package validation

Add to `Directory.Build.props`:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
```

Harmless today (single TFM, no prior published version to diff against — the compatible-framework
check and the baseline-diff check both need inputs that don't exist yet), but future-proofs: it
starts enforcing the moment either becomes true. `PackageValidationBaselineVersion` is
deliberately **not** set now — nuget-package-compatibility-rules.md's baseline check needs a
*previously published* version to diff against, which cannot exist before the first publish.
Setting a baseline is captured as a checklist step for the release *after* 0.1.0 (§6), not now.

## 6. `CONTRIBUTING.md`: publishing checklist

New subsection inserted between the existing "## Releases" and "## Testing against a local build"
sections, documenting the manual steps the *actual* first publish requires — none of which this
change performs:

- Flip `README.md`'s "Status: v0... nothing published yet" callout (`README.md:12-38`) to reflect
  reality.
- One-time NuGet.org account hygiene: sign in with a Microsoft account, enable two-factor
  authentication, enable "email me when a package is published" (publish-nuget-package.md).
- `dotnet pack -c Release` each package, `dotnet nuget push` both `.nupkg` and `.snupkg`.
- Starting with the release *after* 0.1.0: add `PackageValidationBaselineVersion` (§5) pointing at
  the prior shipped version, so `EnablePackageValidation` starts catching binary breaks.

## 7. Verification

- `dotnet build InTest.sln` and `dotnet test InTest.sln` (all four suites) — confirms the new
  MSBuild properties and package references don't break the existing build.
- `pwsh scripts/local-e2e-test.ps1` — this is the repo's sanctioned way to actually pack and
  restore locally (`CONTRIBUTING.md` "Testing against a local build"); running it confirms the new
  `PackageReadmeFile`/`None Include` entries and SourceLink reference don't break packing
  (a missing README file, for instance, fails `dotnet pack` outright) and that the scaffolded
  restore still succeeds end to end. Raw `dotnet pack` is not run by hand, per that section's
  explicit rule.

## 8. Files touched

- `Directory.Build.props` — SourceLink properties, `EnablePackageValidation`.
- `Directory.Packages.props` — `Microsoft.SourceLink.GitHub` version entry.
- `src/InTest.Cli/InTest.Cli.csproj` — `Description`, `PackageTags`, `PackageReadmeFile`,
  SourceLink `PackageReference`.
- `src/InTest.Runtime/InTest.Runtime.csproj` — `PackageTags`, `PackageReadmeFile`, SourceLink
  `PackageReference`.
- `src/InTest.Cli/README.md`, `src/InTest.Runtime/README.md` — new.
- `CONTRIBUTING.md` — new "Publishing checklist" subsection.
