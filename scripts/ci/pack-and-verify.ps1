<#
.SYNOPSIS
    Packs InTest.Cli, InTest.Runtime, InTest.Runtime.MSTest, InTest.Runtime.xUnit and
    InTest.Runtime.NUnit at whatever version this checkout's git history resolves (a real merge
    commit on main, or an exact tag), then verifies the five artifacts agree with each other, with
    what the packed InTest.Cli actually scaffolds, and -- since the runtime-framework split -- with
    each other's declared package dependencies.

.DESCRIPTION
    Task 3 of docs/superpowers/plans/2026-08-23-trunk-based-versioning.md ("CI produces the
    versions"), extended by the NuGet-trusted-publishing task with a fourth check, further
    extended by the runtime-framework-split task (InTest.Runtime split into a neutral package and
    an InTest.Runtime.MSTest adapter, Task 10) with a fifth and sixth, further still by the
    xUnit-framework-pack task (Task 9), which added InTest.Runtime.xUnit as a second adapter
    sibling of InTest.Runtime.MSTest and folded it into every check below rather than adding a
    parallel, un-verified fourth package, and further still by the NUnit-framework-pack task
    (Task 7 of docs/superpowers/plans/2026-08-31-intest-nunit-framework-pack.md), which added
    InTest.Runtime.NUnit as a third adapter sibling the same way. Six things are proven here that a
    green `dotnet pack` alone does not prove -- each now spans every adapter present, not just
    InTest.Runtime.MSTest:

    1. [scaffold-reads-itself] (Task 1) end to end against a *packed* build, not just a `dotnet
       run` build the way scripts/local-e2e-test.ps1 already covers it: the InTest.Runtime.MSTest
       PackageReference a freshly-packed InTest.Cli scaffolds must name the exact version that was
       actually packed for InTest.Runtime.MSTest -- not merely "a" version, not the running
       assembly's version by assumption, but the same string found by unzipping the .nuspec that
       shipped in this run's own InTest.Runtime.MSTest.*.nupkg. A drift here would mean a generated
       project's very first restore fails, silently, the moment it left this repository. (Before
       the runtime-framework split this compared against InTest.Runtime, which the scaffold
       referenced directly; the scaffold now references the MSTest adapter instead, so this is
       what changed -- see point 6 below for the check that the *neutral* package still carries no
       framework coupling.)
    2. All five packages resolve to the *same* version from the same commit. [version-from-git]
       configures MinVer identically for all five projects via the repo-root Directory.Build.props,
       so this should always hold -- checking it is cheap insurance against that configuration ever
       being split or overridden per-project without anyone noticing. It is also the guard against
       an adapter silently packing at the SDK's default 1.0.0 because someone dropped its `MinVer`
       PackageReference: MinVer contributes nothing and fails nothing when absent -- it just stops
       contributing a version -- so only this equality check catches that regression.
    3. [tag-is-the-release]: when this script is told which tag triggered the build
       (-ExpectedTag), the packed version must equal that tag exactly. Equality to the bare tag is
       the whole check -- MinVer only appends a ".<height>" suffix to commits that are *not* an
       exact tag match (measured in the plan's own table), so an exact-match failure and a
       height-leak failure are the same failure here, not two things to check separately.
    4. The packaged *contents* look right, not merely that packing exited 0. The readiness spec's
       §8 (CONTRIBUTING.md's "Publishing checklist", "Verify, then tag" step, as revision 7 numbered
       it before trusted publishing renumbered the list) describes this as a human unzipping both
       .nupkg before pushing; that human step is fine as designed for a manual `dotnet nuget push`,
       but the release workflow this script now also runs under (.github/workflows/release.yml)
       publishes automatically on a tag push, with no human in that gap to catch a regression.
       `Assert-PackageArtifactContents` below is the automated substitute for that specific check,
       not a replacement for the rest of that step (which still includes an actual
       `dotnet tool install` + `intest --help` smoke test that stays manual).
       It confirms: `README.md` and `icon.png` present in all five packages; `THIRD-PARTY-NOTICES.md`
       present in InTest.Cli (it bundles third-party DLLs -- readiness spec §5) and *absent* from
       InTest.Runtime and every adapter (none of them does, and packing it there would be a
       copy-paste regression, not a feature); and a non-empty `<repository … commit="…">` in every
       nuspec, confirming Source Link actually stamped a commit rather than emitting an empty
       attribute (readiness spec §2).
    5. InTest.Runtime's own nuspec declares no dependency on any test framework
       (`Assert-NoTestFrameworkDependency`) -- id matching `MSTest.*`, `xunit*`, `NUnit*` or
       `Microsoft.NET.Test.Sdk`. This is the entire point of the runtime-framework split: a team
       consuming only InTest.Runtime (say, to build an xUnit or NUnit adapter of their own) must
       never receive MSTest as a transitive dependency, and vice versa. `InTest.Architecture.Tests`
       already enforces framework-neutrality at the *source* level (no file under `Neutral/` may
       name `Microsoft.VisualStudio.TestTools.UnitTesting`), but a source-level check cannot see a
       leak introduced purely through project/package references -- only the packed dependency
       graph can, which is what this check reads.
    6. Each adapter's nuspec declares a dependency on InTest.Runtime whose version's lower bound
       equals the *packed* neutral version exactly (`Assert-AdapterDependsOnExactNeutralVersion`),
       and also declares its own test framework as a dependency -- a positive control (MSTest.TestFramework
       for InTest.Runtime.MSTest, xunit.v3.extensibility.core for InTest.Runtime.xUnit, NUnit for
       InTest.Runtime.NUnit -- the last one is InTest.Runtime.NUnit's actual PackageReference, not a
       second package the way xUnit's extensibility.core/assert split needs, per [one-package]), so
       the InTest.Runtime check above cannot pass vacuously because nothing was packed as a
       dependency at all (e.g. if this script's dependency-node parsing missed the nuspec's actual
       shape).

    Deliberately does NOT push anywhere. [publish-stays-manual] governed this script's own history
    -- no NuGet ID was reserved and the API key was the owner's alone -- but that premise has since
    been superseded (see docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's
    [publish-stays-manual] section and this task's own report): NuGet Trusted Publishing removed
    the API-key blocker, and .github/workflows/release.yml now performs the actual
    `dotnet nuget push`, using this script's pack step as its own -- but that push happens entirely
    in that workflow's separate `publish` job, never in this script. This script itself still never
    invokes `dotnet nuget push` and still cannot: it has no OIDC token, no API key, and no
    knowledge of which job is calling it. Whether nuget.org actually accepts what this script packs
    (including the .snupkg-under-tools/ question the readiness spec flags as open) is proven only
    by release.yml's real push, not by anything in this file.

    Packs from the real checkout in place (github.workspace), unlike
    scripts/local-e2e-test.ps1, which deliberately copies src/ to a non-git location first so it
    can redirect NUGET_PACKAGES and stamp a collision-proof local-only version. Neither concern
    applies to a CI runner: the runner's NuGet cache is thrown away after the job, and the whole
    point here is proving what a *real* checkout resolves to, not a synthetic version that can
    never collide with a release.

.PARAMETER RepoRoot
    Root of the InTest checkout.

.PARAMETER OutputDir
    Where the five .nupkg files are written. Must be outside RepoRoot so packing cannot dirty the
    working tree -- the workflow passes runner.temp, the same isolation scripts/ci/dogfood.ps1
    already uses for its own scaffolds.

.PARAMETER ScaffoldRoot
    Directory to scaffold the verification project under. Must be outside RepoRoot, for the same
    reason as OutputDir.

.PARAMETER Spec
    OpenAPI document passed to `intest init` for the scaffold-verification step. Defaults to
    samples/Catalog.Api/Catalog.Api.json -- already exercised by scripts/ci/dogfood.ps1, so it is
    known-good rather than a spec invented for this script alone.

.PARAMETER ExpectedTag
    When non-empty, asserts all five packed versions equal this value exactly (see point 3
    above). Pass the empty string (the default) for a plain merge-to-main build, where no such
    assertion applies -- [tag-is-the-release] only promises an exact, height-free version on a
    tagged build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$RepoRoot,
    [Parameter(Mandatory = $true)] [string]$OutputDir,
    [Parameter(Mandatory = $true)] [string]$ScaffoldRoot,
    [string]$Spec = '',
    [string]$ExpectedTag = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

if ([string]::IsNullOrWhiteSpace($Spec)) {
    $Spec = Join-Path $RepoRoot 'samples' 'Catalog.Api' 'Catalog.Api.json'
}
if (-not (Test-Path -LiteralPath $Spec)) {
    throw "Spec not found: $Spec"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
New-Item -ItemType Directory -Force -Path $ScaffoldRoot | Out-Null
$ScaffoldRoot = (Resolve-Path -LiteralPath $ScaffoldRoot).Path

$CliProject = Join-Path $RepoRoot 'src' 'InTest.Cli' 'InTest.Cli.csproj'
$RuntimeProject = Join-Path $RepoRoot 'src' 'InTest.Runtime' 'InTest.Runtime.csproj'
$MSTestProject = Join-Path $RepoRoot 'src' 'InTest.Runtime.MSTest' 'InTest.Runtime.MSTest.csproj'
$XUnitProject = Join-Path $RepoRoot 'src' 'InTest.Runtime.xUnit' 'InTest.Runtime.xUnit.csproj'
$NUnitProject = Join-Path $RepoRoot 'src' 'InTest.Runtime.NUnit' 'InTest.Runtime.NUnit.csproj'

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory)] [string]$StepName,
        [Parameter(Mandatory)] [string[]]$Arguments
    )
    Write-Host ''
    Write-Host "=== $StepName ===" -ForegroundColor Cyan
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Step '$StepName' exited $LASTEXITCODE"
    }
}

# Opens a .nupkg once and returns both things every check below needs from it: the full list of
# zip entry names (to check a file is/isn't present at the package root) and the parsed .nuspec
# XML (to check <version> and <repository commit="...">). Factored out of what used to be
# Get-NuspecVersion's own zip-open block so that block does not get copy-pasted a second time for
# Assert-PackageArtifactContents -- this script already has one incident on record
# (CLAUDE.md: "Re-deriving is the recurring defect in this codebase -- don't") of near-identical
# logic drifting apart across two call sites.
function Get-NupkgManifest {
    param([Parameter(Mandatory)] [string]$NupkgPath)

    $zip = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })

        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "No .nuspec entry found inside $NupkgPath"
        }
        $stream = $nuspecEntry.Open()
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            try {
                $nuspecContent = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
    } finally {
        $zip.Dispose()
    }

    # PowerShell's [xml] adapter resolves .package.metadata.version (and every other element used
    # below) by local name regardless of the nuspec's default xmlns -- confirmed by direct use
    # here, not assumed; a namespace-blind XPath would be the alternative if this ever stopped
    # working.
    [xml]$nuspecXml = $nuspecContent
    return [pscustomobject]@{
        EntryNames = $entryNames
        NuspecXml  = $nuspecXml
    }
}

# Selects a packed .nupkg by its nuspec <id> rather than by filename glob. Filename-glob
# selection (the previous approach, 'InTest.Runtime.*.nupkg') is dangerous now that a third
# package exists whose id itself extends the neutral package's id with a dot:
# 'InTest.Runtime.MSTest.<version>.nupkg' also matches that glob. It happened to select the right
# file only by alphabetical accident (Select-Object -First 1 on a sorted listing put
# 'InTest.Runtime.0...' ahead of 'InTest.Runtime.MSTest...' because '0' sorts before 'M') -- an
# accident, not a guarantee, and one this task's own review flagged as newly dangerous rather than
# merely untidy. Reading every .nupkg's own <id> and matching on that is unambiguous regardless of
# how the package ids relate to each other as strings. Reuses Get-NupkgManifest (one zip-open per
# candidate) rather than a second, parallel zip-reading block -- see Get-NupkgManifest's own
# comment for why that duplication has already bitten this codebase once.
function Get-NupkgById {
    param(
        [Parameter(Mandatory)] [string]$OutputDir,
        [Parameter(Mandatory)] [string]$PackageId
    )

    $found = @()
    foreach ($candidate in Get-ChildItem -LiteralPath $OutputDir -Filter '*.nupkg') {
        $manifest = Get-NupkgManifest -NupkgPath $candidate.FullName
        if ($manifest.NuspecXml.package.metadata.id -eq $PackageId) {
            $found += [pscustomobject]@{
                Path     = $candidate.FullName
                Manifest = $manifest
            }
        }
    }

    if ($found.Count -eq 0) {
        throw "No .nupkg in $OutputDir has a nuspec <id> of '$PackageId'."
    }
    if ($found.Count -gt 1) {
        throw "More than one .nupkg in $OutputDir declares nuspec <id> '$PackageId': $(($found | ForEach-Object { $_.Path }) -join ', ')."
    }
    return $found[0]
}

function Get-NuspecVersion {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Label
    )

    $version = $Manifest.NuspecXml.package.metadata.version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read <version> from the .nuspec for $Label"
    }
    return $version
}

# Returns a nuspec's <dependency> nodes as a flat list, regardless of whether `dotnet pack` wrote
# them grouped by target framework (<dependencies><group targetFramework="..."><dependency .../>
# </group></dependencies>, what this script's own real-pack verification during this task actually
# observed for both InTest.Runtime and InTest.Runtime.MSTest) or ungrouped
# (<dependencies><dependency .../></dependencies>, the shape a single-TFM package can also produce
# depending on SDK version -- not assumed here, handled either way so this does not silently see
# zero dependencies against a shape it didn't anticipate). Returns an empty array, never $null, so
# callers can enumerate the result unconditionally.
function Get-NuspecDependencies {
    param([Parameter(Mandatory)] $NuspecXml)

    $dependenciesNode = $NuspecXml.package.metadata.dependencies
    if (-not $dependenciesNode) {
        return @()
    }
    if ($dependenciesNode.group) {
        return @($dependenciesNode.group | ForEach-Object { $_.dependency } | Where-Object { $_ })
    }
    if ($dependenciesNode.dependency) {
        return @($dependenciesNode.dependency)
    }
    return @()
}

# A `dotnet pack`-emitted ProjectReference dependency version can appear either as a plain version
# string (what this script's own real-pack run during this task actually produced, e.g.
# "0.1.0-preview.1.11") or as a NuGet version-range string (e.g. "[0.2.0-preview.0.3, )") --
# observed in other InTest tasks and not to be assumed away here. A raw string comparison against
# an expected plain version fails on the range form even though the range's lower bound is
# correct, so this extracts just the lower bound: strip the enclosing bracket/paren, if any, and
# take the text before the first comma.
function Get-DependencyLowerBound {
    param([Parameter(Mandatory)] [string]$VersionSpec)

    $trimmed = $VersionSpec.Trim()
    if ($trimmed.Length -ge 2 -and ($trimmed[0] -eq '[' -or $trimmed[0] -eq '(')) {
        $inner = $trimmed.Substring(1, $trimmed.Length - 2)
        return ($inner -split ',')[0].Trim()
    }
    return $trimmed
}

# The nuget-publish-readiness task's artifact-content check (see .DESCRIPTION point 4 above).
# -RequireEntries are exact, case-sensitive root-level file names -- confirmed against a real pack
# during this task that `dotnet pack` writes README.md, icon.png and THIRD-PARTY-NOTICES.md at the
# package root with exactly that casing, not nested under any subfolder, so an exact string match
# against the zip's own entry names is enough; no path normalization is needed. -ForbidEntries is
# the same check inverted, for the one file that must NOT appear (THIRD-PARTY-NOTICES.md on
# InTest.Runtime -- readiness spec §5: that package redistributes nothing third-party, so packing
# the notices file there would be a copy-paste mistake, not a feature).
function Assert-PackageArtifactContents {
    param(
        [Parameter(Mandatory)] [string]$NupkgPath,
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Label,
        [string[]]$RequireEntries = @(),
        [string[]]$ForbidEntries = @()
    )

    foreach ($required in $RequireEntries) {
        if ($Manifest.EntryNames -notcontains $required) {
            throw "[$Label] artifact assertion failed: expected '$required' at the package root inside $NupkgPath, but it was not found. Entries present: $($Manifest.EntryNames -join ', ')"
        }
    }

    foreach ($forbidden in $ForbidEntries) {
        if ($Manifest.EntryNames -contains $forbidden) {
            throw "[$Label] artifact assertion failed: '$forbidden' was found inside $NupkgPath, but this package must NOT bundle it -- see the readiness spec §5 for why."
        }
    }

    $repositoryNode = $Manifest.NuspecXml.package.metadata.repository
    if (-not $repositoryNode) {
        throw "[$Label] artifact assertion failed: the .nuspec inside $NupkgPath has no <repository> element at all -- Source Link should populate one automatically with no package reference needed (readiness spec §2)."
    }
    $commit = $repositoryNode.commit
    if ([string]::IsNullOrWhiteSpace($commit)) {
        throw "[$Label] artifact assertion failed: the .nuspec inside $NupkgPath has a <repository> element whose commit attribute is empty or missing -- Source Link should stamp the exact commit SHA this package was built from (readiness spec §2)."
    }

    $requireDesc = if ($RequireEntries) { $RequireEntries -join ', ' } else { '(none)' }
    $forbidDesc = if ($ForbidEntries) { "; confirmed absent: $($ForbidEntries -join ', ')" } else { '' }
    Write-Host "[$Label] artifact contents verified -- present: $requireDesc$forbidDesc; repository commit '$commit'." -ForegroundColor Green
}

# Check 5 from .DESCRIPTION: InTest.Runtime must carry no test-framework dependency, transitive or
# otherwise -- the packed nuspec is the only layer that can see a leak introduced purely through
# package/project references, since InTest.Architecture.Tests's "no Neutral/ file may name
# Microsoft.VisualStudio.TestTools.UnitTesting" rule only ever sees source text.
function Assert-NoTestFrameworkDependency {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Label
    )

    $forbiddenPatterns = @('^MSTest\.', '^xunit', '^NUnit', '^Microsoft\.NET\.Test\.Sdk$')
    $dependencies = Get-NuspecDependencies -NuspecXml $Manifest.NuspecXml

    $leaked = @()
    foreach ($dependency in $dependencies) {
        foreach ($pattern in $forbiddenPatterns) {
            if ($dependency.id -match $pattern) {
                $leaked += $dependency.id
                break
            }
        }
    }

    if ($leaked.Count -gt 0) {
        throw "[$Label] test-framework leak: the nuspec declares a dependency on $($leaked -join ', ') -- InTest.Runtime must stay test-framework-neutral (CLAUDE.md: 'No file under Neutral/ may name Microsoft.VisualStudio.TestTools.UnitTesting') so a team using xUnit/NUnit never receives MSTest transitively, and vice versa."
    }

    Write-Host "[$Label] confirmed no MSTest/xUnit/NUnit/Microsoft.NET.Test.Sdk dependency in the nuspec (dependencies present: $((@($dependencies | ForEach-Object { $_.id })) -join ', '))." -ForegroundColor Green
}

# Check 6 from .DESCRIPTION: each adapter's nuspec must depend on InTest.Runtime at exactly the
# version that was actually packed alongside it (lower bound, to tolerate the ProjectReference
# range form -- see Get-DependencyLowerBound), and must also depend on its own test framework as a
# positive control, so the InTest.Runtime check above cannot pass vacuously because dependency
# parsing silently found nothing at all. -PositiveControlPackageId is MSTest.TestFramework for
# InTest.Runtime.MSTest and xunit.v3.extensibility.core for InTest.Runtime.xUnit -- the same shape
# of check, parameterised over which adapter is being verified, rather than a second near-identical
# function (CLAUDE.md: "Re-deriving is the recurring defect in this codebase -- don't").
function Assert-AdapterDependsOnExactNeutralVersion {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [string]$ExpectedNeutralVersion,
        [Parameter(Mandatory)] [string]$PositiveControlPackageId
    )

    $dependencies = Get-NuspecDependencies -NuspecXml $Manifest.NuspecXml

    $runtimeDependency = $dependencies | Where-Object { $_.id -eq 'InTest.Runtime' } | Select-Object -First 1
    if (-not $runtimeDependency) {
        throw "[$Label] artifact assertion failed: no dependency on InTest.Runtime found in the nuspec -- the ProjectReference to ../InTest.Runtime/InTest.Runtime.csproj should have produced one automatically."
    }
    $lowerBound = Get-DependencyLowerBound -VersionSpec $runtimeDependency.version
    if ($lowerBound -ne $ExpectedNeutralVersion) {
        throw "[$Label] artifact assertion failed: depends on InTest.Runtime version '$($runtimeDependency.version)' (lower bound '$lowerBound'), expected exactly '$ExpectedNeutralVersion' -- the InTest.Runtime version actually packed in this same run. A drift here means an adopter installing $Label could resolve a different InTest.Runtime than the one packed alongside it."
    }

    $positiveControlDependency = $dependencies | Where-Object { $_.id -eq $PositiveControlPackageId } | Select-Object -First 1
    if (-not $positiveControlDependency) {
        throw "[$Label] artifact assertion failed: no dependency on $PositiveControlPackageId found in the nuspec -- this is the positive control for the InTest.Runtime dependency check above; its absence means dependency parsing itself found nothing, not that the version check passed legitimately."
    }

    Write-Host "[$Label] confirmed InTest.Runtime dependency lower bound '$lowerBound' matches the packed neutral version exactly, and $PositiveControlPackageId dependency present (positive control)." -ForegroundColor Green
}

# The scaffold references InTest.Runtime.MSTest, not InTest.Runtime directly, since the
# runtime-framework split -- InitCommand.cs's scaffolded .csproj carries
# `<PackageReference Include="InTest.Runtime.MSTest" Version="{CliVersion.Current}" />`. The regex
# is anchored so it does not also match the plain "InTest.Runtime" prefix of that same string.
function Get-ScaffoldedMSTestAdapterVersion {
    param([Parameter(Mandatory)] [string]$CsprojPath)

    $content = Get-Content -Raw -LiteralPath $CsprojPath
    $match = [regex]::Match($content, 'Include="InTest\.Runtime\.MSTest"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        throw "Could not find an InTest.Runtime.MSTest PackageReference inside $CsprojPath"
    }
    return $match.Groups[1].Value
}

# ---- Step 1 (pack): no -p:MinVerVersionOverride, unlike scripts/local-e2e-test.ps1. That
# script's whole point is a version that can never collide with a real release; this script's
# whole point is the opposite -- prove what this checkout's real git history resolves to.
Invoke-Dotnet -StepName 'pack InTest.Cli' -Arguments @('pack', $CliProject, '-c', 'Release', '-o', $OutputDir)
Invoke-Dotnet -StepName 'pack InTest.Runtime' -Arguments @('pack', $RuntimeProject, '-c', 'Release', '-o', $OutputDir)
Invoke-Dotnet -StepName 'pack InTest.Runtime.MSTest' -Arguments @('pack', $MSTestProject, '-c', 'Release', '-o', $OutputDir)
Invoke-Dotnet -StepName 'pack InTest.Runtime.xUnit' -Arguments @('pack', $XUnitProject, '-c', 'Release', '-o', $OutputDir)
Invoke-Dotnet -StepName 'pack InTest.Runtime.NUnit' -Arguments @('pack', $NUnitProject, '-c', 'Release', '-o', $OutputDir)

# ---- Step 2 (select, by nuspec <id> -- see Get-NupkgById's own comment for why a filename glob
# is no longer safe now that a third package's id extends the neutral package's id with a dot).
$cliPackage = Get-NupkgById -OutputDir $OutputDir -PackageId 'InTest.Cli'
$runtimePackage = Get-NupkgById -OutputDir $OutputDir -PackageId 'InTest.Runtime'
$mstestPackage = Get-NupkgById -OutputDir $OutputDir -PackageId 'InTest.Runtime.MSTest'
$xunitPackage = Get-NupkgById -OutputDir $OutputDir -PackageId 'InTest.Runtime.xUnit'
$nunitPackage = Get-NupkgById -OutputDir $OutputDir -PackageId 'InTest.Runtime.NUnit'

# ---- Step 3 (verify, do not assume): read each artifact's actual .nuspec version rather than
# trusting the filename or the exit code of `dotnet pack`.
$cliVersion = Get-NuspecVersion -Manifest $cliPackage.Manifest -Label 'InTest.Cli'
$runtimeVersion = Get-NuspecVersion -Manifest $runtimePackage.Manifest -Label 'InTest.Runtime'
$mstestVersion = Get-NuspecVersion -Manifest $mstestPackage.Manifest -Label 'InTest.Runtime.MSTest'
$xunitVersion = Get-NuspecVersion -Manifest $xunitPackage.Manifest -Label 'InTest.Runtime.xUnit'
$nunitVersion = Get-NuspecVersion -Manifest $nunitPackage.Manifest -Label 'InTest.Runtime.NUnit'

Write-Host ''
Write-Host "InTest.Cli nuspec version:            $cliVersion"
Write-Host "InTest.Runtime nuspec version:         $runtimeVersion"
Write-Host "InTest.Runtime.MSTest nuspec version:  $mstestVersion"
Write-Host "InTest.Runtime.xUnit nuspec version:   $xunitVersion"
Write-Host "InTest.Runtime.NUnit nuspec version:    $nunitVersion"

# Five-way equality: also the guard against any adapter silently packing at the SDK's default
# 1.0.0 because someone dropped its `MinVer` PackageReference -- MinVer contributes nothing and
# fails nothing when absent, so only this check catches that regression (see .DESCRIPTION point 2).
if ($cliVersion -ne $runtimeVersion -or $cliVersion -ne $mstestVersion -or $cliVersion -ne $xunitVersion -or $cliVersion -ne $nunitVersion) {
    throw "InTest.Cli, InTest.Runtime, InTest.Runtime.MSTest, InTest.Runtime.xUnit and InTest.Runtime.NUnit did not all pack at the same version ('$cliVersion' / '$runtimeVersion' / '$mstestVersion' / '$xunitVersion' / '$nunitVersion') -- MinVer should derive an identical version for all five from the same commit and the same Directory.Build.props configuration."
}

# ---- Step 3b (verify, do not assume -- the CONTRIBUTING.md "Publishing checklist" substitute described in
# .DESCRIPTION point 4): unzip each .nupkg and confirm the packaged contents, not merely that
# `dotnet pack` exited 0.
Assert-PackageArtifactContents -NupkgPath $cliPackage.Path -Manifest $cliPackage.Manifest -Label 'InTest.Cli' `
    -RequireEntries @('README.md', 'icon.png', 'THIRD-PARTY-NOTICES.md')

Assert-PackageArtifactContents -NupkgPath $runtimePackage.Path -Manifest $runtimePackage.Manifest -Label 'InTest.Runtime' `
    -RequireEntries @('README.md', 'icon.png') `
    -ForbidEntries @('THIRD-PARTY-NOTICES.md')

Assert-PackageArtifactContents -NupkgPath $mstestPackage.Path -Manifest $mstestPackage.Manifest -Label 'InTest.Runtime.MSTest' `
    -RequireEntries @('README.md', 'icon.png') `
    -ForbidEntries @('THIRD-PARTY-NOTICES.md')

Assert-PackageArtifactContents -NupkgPath $xunitPackage.Path -Manifest $xunitPackage.Manifest -Label 'InTest.Runtime.xUnit' `
    -RequireEntries @('README.md', 'icon.png') `
    -ForbidEntries @('THIRD-PARTY-NOTICES.md')

Assert-PackageArtifactContents -NupkgPath $nunitPackage.Path -Manifest $nunitPackage.Manifest -Label 'InTest.Runtime.NUnit' `
    -RequireEntries @('README.md', 'icon.png') `
    -ForbidEntries @('THIRD-PARTY-NOTICES.md')

# ---- Step 3c (the runtime-framework-split acceptance gate -- .DESCRIPTION points 5 and 6): the
# neutral package must carry no test-framework dependency, and each adapter must depend on exactly
# the neutral version that was packed alongside it in this same run.
Assert-NoTestFrameworkDependency -Manifest $runtimePackage.Manifest -Label 'InTest.Runtime'

Assert-AdapterDependsOnExactNeutralVersion -Manifest $mstestPackage.Manifest -Label 'InTest.Runtime.MSTest' `
    -ExpectedNeutralVersion $runtimeVersion -PositiveControlPackageId 'MSTest.TestFramework'

Assert-AdapterDependsOnExactNeutralVersion -Manifest $xunitPackage.Manifest -Label 'InTest.Runtime.xUnit' `
    -ExpectedNeutralVersion $runtimeVersion -PositiveControlPackageId 'xunit.v3.extensibility.core'

# NUnit's positive control is 'NUnit' itself, not a second package the way xUnit's
# extensibility.core/assert split needs -- [one-package] measured that NUnit alone compiles an
# ordinary class library, so InTest.Runtime.NUnit's only PackageReference besides InTest.Runtime
# is NUnit (NUnit3TestAdapter is a generated-project-only reference, scaffolded by InitCommand.cs,
# not a dependency of the adapter package itself).
Assert-AdapterDependsOnExactNeutralVersion -Manifest $nunitPackage.Manifest -Label 'InTest.Runtime.NUnit' `
    -ExpectedNeutralVersion $runtimeVersion -PositiveControlPackageId 'NUnit'

$cliDll = Join-Path $RepoRoot 'src' 'InTest.Cli' 'bin' 'Release' 'net10.0' 'InTest.Cli.dll'
if (-not (Test-Path -LiteralPath $cliDll)) {
    throw "Expected build output not found: $cliDll -- 'dotnet pack' should have built this as part of packing InTest.Cli."
}

$scaffoldDir = Join-Path $ScaffoldRoot 'PackVerify'
New-Item -ItemType Directory -Force -Path $scaffoldDir | Out-Null

Invoke-Dotnet -StepName 'intest init (scaffold verification, against the just-packed InTest.Cli.dll)' -Arguments @(
    $cliDll, 'init', '--project', $scaffoldDir, '--name', 'PackVerify', '--spec', $Spec
)

$scaffoldedCsproj = Join-Path $scaffoldDir 'PackVerify.csproj'
if (-not (Test-Path -LiteralPath $scaffoldedCsproj)) {
    throw "Expected scaffolded project file not found: $scaffoldedCsproj"
}

$scaffoldedMSTestAdapterVersion = Get-ScaffoldedMSTestAdapterVersion -CsprojPath $scaffoldedCsproj
Write-Host "Scaffolded InTest.Runtime.MSTest PackageReference version: $scaffoldedMSTestAdapterVersion"

if ($scaffoldedMSTestAdapterVersion -ne $mstestVersion) {
    throw "[scaffold-reads-itself] verification failed: the scaffolded PackageReference (Version=`"$scaffoldedMSTestAdapterVersion`") does not match the InTest.Runtime.MSTest package actually packed (nuspec version `"$mstestVersion`")."
}

Write-Host "Confirmed: the scaffolded InTest.Runtime.MSTest reference agrees with the packed nuspec version." -ForegroundColor Green

# ---- Step 4 (tag builds only): the artifact must equal the tag exactly.
if (-not [string]::IsNullOrWhiteSpace($ExpectedTag)) {
    Write-Host ''
    Write-Host "=== Tag assertion: this build was triggered by tag '$ExpectedTag' ===" -ForegroundColor Cyan
    if ($cliVersion -ne $ExpectedTag) {
        throw "Tag mismatch: InTest.Cli packed as '$cliVersion', expected exactly '$ExpectedTag' (the pushed tag). [tag-is-the-release] requires a tagged build's artifact to carry no prerelease height."
    }
    if ($runtimeVersion -ne $ExpectedTag) {
        throw "Tag mismatch: InTest.Runtime packed as '$runtimeVersion', expected exactly '$ExpectedTag' (the pushed tag). [tag-is-the-release] requires a tagged build's artifact to carry no prerelease height."
    }
    if ($mstestVersion -ne $ExpectedTag) {
        throw "Tag mismatch: InTest.Runtime.MSTest packed as '$mstestVersion', expected exactly '$ExpectedTag' (the pushed tag). [tag-is-the-release] requires a tagged build's artifact to carry no prerelease height."
    }
    if ($xunitVersion -ne $ExpectedTag) {
        throw "Tag mismatch: InTest.Runtime.xUnit packed as '$xunitVersion', expected exactly '$ExpectedTag' (the pushed tag). [tag-is-the-release] requires a tagged build's artifact to carry no prerelease height."
    }
    if ($nunitVersion -ne $ExpectedTag) {
        throw "Tag mismatch: InTest.Runtime.NUnit packed as '$nunitVersion', expected exactly '$ExpectedTag' (the pushed tag). [tag-is-the-release] requires a tagged build's artifact to carry no prerelease height."
    }
    Write-Host "Confirmed: all five packages were packed at exactly '$ExpectedTag', with no prerelease height." -ForegroundColor Green
}

Write-Host ''
Write-Host "Pack-and-verify complete. InTest.Cli $cliVersion / InTest.Runtime $runtimeVersion / InTest.Runtime.MSTest $mstestVersion / InTest.Runtime.xUnit $xunitVersion / InTest.Runtime.NUnit $nunitVersion" -ForegroundColor Green