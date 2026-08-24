<#
.SYNOPSIS
    Packs InTest.Cli and InTest.Runtime at whatever version this checkout's git history resolves
    (a real merge commit on main, or an exact tag), then verifies the two artifacts agree with
    each other and with what the packed InTest.Cli actually scaffolds.

.DESCRIPTION
    Task 3 of docs/superpowers/plans/2026-08-23-trunk-based-versioning.md ("CI produces the
    versions"), extended by the NuGet-trusted-publishing task with a fourth check. Four things are
    proven here that a green `dotnet pack` alone does not prove:

    1. [scaffold-reads-itself] (Task 1) end to end against a *packed* build, not just a `dotnet
       run` build the way scripts/local-e2e-test.ps1 already covers it: the InTest.Runtime
       PackageReference a freshly-packed InTest.Cli scaffolds must name the exact version that was
       actually packed for InTest.Runtime -- not merely "a" version, not the running assembly's
       version by assumption, but the same string found by unzipping the .nuspec that shipped in
       this run's own InTest.Runtime.*.nupkg. A drift here would mean a generated project's very
       first restore fails, silently, the moment it left this repository.
    2. Both packages resolve to the *same* version from the same commit. [version-from-git]
       configures MinVer identically for both projects via the repo-root Directory.Build.props, so
       this should always hold -- checking it is cheap insurance against that configuration ever
       being split or overridden per-project without anyone noticing.
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
       It confirms: `README.md` and `icon.png` present in both packages; `THIRD-PARTY-NOTICES.md`
       present in InTest.Cli (it bundles third-party DLLs -- readiness spec §5) and *absent* from
       InTest.Runtime (it does not, and packing it there would be a copy-paste regression, not a
       feature); and a non-empty `<repository … commit="…">` in both nuspecs, confirming Source
       Link actually stamped a commit rather than emitting an empty attribute (readiness spec §2).

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
    Where the two .nupkg files are written. Must be outside RepoRoot so packing cannot dirty the
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
    When non-empty, asserts both packed versions equal this value exactly (see point 3 above).
    Pass the empty string (the default) for a plain merge-to-main build, where no such assertion
    applies -- [tag-is-the-release] only promises an exact, height-free version on a tagged build.
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

function Get-NuspecVersion {
    param([Parameter(Mandatory)] [string]$NupkgPath)

    $manifest = Get-NupkgManifest -NupkgPath $NupkgPath
    $version = $manifest.NuspecXml.package.metadata.version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read <version> from the .nuspec inside $NupkgPath"
    }
    return $version
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
        [Parameter(Mandatory)] [string]$Label,
        [string[]]$RequireEntries = @(),
        [string[]]$ForbidEntries = @()
    )

    $manifest = Get-NupkgManifest -NupkgPath $NupkgPath

    foreach ($required in $RequireEntries) {
        if ($manifest.EntryNames -notcontains $required) {
            throw "[$Label] artifact assertion failed: expected '$required' at the package root inside $NupkgPath, but it was not found. Entries present: $($manifest.EntryNames -join ', ')"
        }
    }

    foreach ($forbidden in $ForbidEntries) {
        if ($manifest.EntryNames -contains $forbidden) {
            throw "[$Label] artifact assertion failed: '$forbidden' was found inside $NupkgPath, but this package must NOT bundle it -- see the readiness spec §5 for why."
        }
    }

    $repositoryNode = $manifest.NuspecXml.package.metadata.repository
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

function Get-ScaffoldedRuntimeVersion {
    param([Parameter(Mandatory)] [string]$CsprojPath)

    $content = Get-Content -Raw -LiteralPath $CsprojPath
    $match = [regex]::Match($content, 'Include="InTest\.Runtime"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        throw "Could not find an InTest.Runtime PackageReference inside $CsprojPath"
    }
    return $match.Groups[1].Value
}

# ---- Step 1 (pack): no -p:MinVerVersionOverride, unlike scripts/local-e2e-test.ps1. That
# script's whole point is a version that can never collide with a real release; this script's
# whole point is the opposite -- prove what this checkout's real git history resolves to.
Invoke-Dotnet -StepName 'pack InTest.Cli' -Arguments @('pack', $CliProject, '-c', 'Release', '-o', $OutputDir)
Invoke-Dotnet -StepName 'pack InTest.Runtime' -Arguments @('pack', $RuntimeProject, '-c', 'Release', '-o', $OutputDir)

$cliNupkg = Get-ChildItem -LiteralPath $OutputDir -Filter 'InTest.Cli.*.nupkg' | Select-Object -First 1
$runtimeNupkg = Get-ChildItem -LiteralPath $OutputDir -Filter 'InTest.Runtime.*.nupkg' | Select-Object -First 1
if (-not $cliNupkg) {
    throw "No InTest.Cli.*.nupkg produced in $OutputDir"
}
if (-not $runtimeNupkg) {
    throw "No InTest.Runtime.*.nupkg produced in $OutputDir"
}

# ---- Step 3 (verify, do not assume): read each artifact's actual .nuspec version rather than
# trusting the filename or the exit code of `dotnet pack`.
$cliVersion = Get-NuspecVersion -NupkgPath $cliNupkg.FullName
$runtimeVersion = Get-NuspecVersion -NupkgPath $runtimeNupkg.FullName

Write-Host ''
Write-Host "InTest.Cli nuspec version:     $cliVersion"
Write-Host "InTest.Runtime nuspec version: $runtimeVersion"

if ($cliVersion -ne $runtimeVersion) {
    throw "InTest.Cli and InTest.Runtime packed at different versions ('$cliVersion' vs '$runtimeVersion') -- MinVer should derive an identical version for both from the same commit and the same Directory.Build.props configuration."
}

# ---- Step 3b (verify, do not assume -- the CONTRIBUTING.md "Publishing checklist" substitute described in
# .DESCRIPTION point 4): unzip both .nupkg and confirm the packaged contents, not merely that
# `dotnet pack` exited 0.
Assert-PackageArtifactContents -NupkgPath $cliNupkg.FullName -Label 'InTest.Cli' `
    -RequireEntries @('README.md', 'icon.png', 'THIRD-PARTY-NOTICES.md')

Assert-PackageArtifactContents -NupkgPath $runtimeNupkg.FullName -Label 'InTest.Runtime' `
    -RequireEntries @('README.md', 'icon.png') `
    -ForbidEntries @('THIRD-PARTY-NOTICES.md')

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

$scaffoldedRuntimeVersion = Get-ScaffoldedRuntimeVersion -CsprojPath $scaffoldedCsproj
Write-Host "Scaffolded InTest.Runtime PackageReference version: $scaffoldedRuntimeVersion"

if ($scaffoldedRuntimeVersion -ne $runtimeVersion) {
    throw "[scaffold-reads-itself] verification failed: the scaffolded PackageReference (Version=`"$scaffoldedRuntimeVersion`") does not match the InTest.Runtime package actually packed (nuspec version `"$runtimeVersion`")."
}

Write-Host "Confirmed: the scaffolded InTest.Runtime reference agrees with the packed nuspec version." -ForegroundColor Green

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
    Write-Host "Confirmed: both packages were packed at exactly '$ExpectedTag', with no prerelease height." -ForegroundColor Green
}

Write-Host ''
Write-Host "Pack-and-verify complete. InTest.Cli $cliVersion / InTest.Runtime $runtimeVersion" -ForegroundColor Green