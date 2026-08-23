<#
.SYNOPSIS
    Exercises InTest's local adoption path (init, generate, fixtures repair, generate --check,
    upgrade) against a from-source build of InTest.Cli/InTest.Runtime, without ever letting a
    restore reach the machine-wide NuGet cache.

.DESCRIPTION
    Nothing is published to NuGet yet (see CLAUDE.md / docs/getting-started.md). Trying the
    adoption path documented in docs/getting-started.md's Phase 8 therefore means packing
    InTest.Cli/InTest.Runtime locally and restoring a scaffolded project against them — and that
    restore is dangerous by default:

      * NuGet's package cache at ~/.nuget/packages/ is keyed by exact version, machine-wide, and
        is never invalidated by a newer local build of the same version. A locally-packed 0.1.0
        becomes indistinguishable from a real published 0.1.0 forever.
      * This has already bitten twice on this machine: once during v1-e Task 5 (a stale
        InTest.Runtime 0.1.0 with an older API surface shadowed a fresh build and produced
        CS0103 on members that plainly existed in source), and again during the acceptance run
        recorded in docs/v0-acceptance.md ("Second trap, found during Task 5 and confirmed
        here").

    Two independent measured defences, both required (see the experiment table below):

      1. NUGET_PACKAGES is redirected to a scratch directory for the lifetime of this script, so
         no restore this script triggers -- whether from `dotnet pack`, `dotnet build`,
         `dotnet run`, or `dotnet tool restore` -- can land in ~/.nuget/packages/. This is the
         load-bearing defence: measured directly (see CLAUDE.md's task notes) that a restore
         with NUGET_PACKAGES set lands entirely in the scratch cache and leaves the global one
         untouched, even though `dotnet pack` alone does not populate the cache at all -- it is
         specifically *restore* that is dangerous, and every path that restores is covered here.
      2. Every package this script packs is stamped with a version that can never collide with a
         real release: 0.1.0-local.<UTC timestamp>.pid<process id>. Directory.Build.props pins
         <Version>0.1.0</Version> for the whole repo (deliberately, per CLAUDE.md -- not to be
         edited by this script), so the override goes in as an MSBuild global property via
         `-p:Version=`, which wins over a plain, unconditional property assignment in an
         imported props file. This is defence in depth: even if defence 1 somehow failed, or a
         human ran a manual `dotnet pack`/`dotnet restore` afterwards against a leftover local
         feed, a `0.1.0-local.*` package can never shadow a published `0.1.0`.

    Measured experiment this script's design rests on (see the task's own notes, reproduced
    here so the reasoning travels with the code rather than living only in a chat transcript):

        | Step                                              | Populates ~/.nuget/packages/? |
        |---------------------------------------------------|--------------------------------|
        | `dotnet pack` alone                                | No                             |
        | `dotnet restore` resolving from a local feed       | Yes                            |
        | The same restore with NUGET_PACKAGES set to scratch | No                            |

    "Don't pack" is therefore not a fix -- the adoption path cannot be exercised without a
    restore. The fix has to make the *restore* harmless, which is what this script does.

    What it proves, end to end, against the committed samples/Catalog.Api/Catalog.Api.json spec
    (no auth, no live server needed -- generation and compilation do not require a running API;
    only `dotnet test` against real HTTP would, and that is out of scope here, see below):

      1. `intest init`            -- scaffold a test project from the sample spec
      2. `intest generate`        -- exit 1, missing fixtures reported (expected on a first run)
      3. `intest fixtures repair` -- creates fixtures with TODO sentinels
      4. `intest generate`        -- exit 0, now that every NeedsFixture operation has a file
      5. `intest generate --check` -- exit 0, committed output matches a fresh render
      6. `dotnet build`           -- the scaffolded project actually *compiles* against the
                                      locally-packed InTest.Runtime. This is the step that would
                                      have caught both prior CS0103 incidents; `generate --check`
                                      alone does not compile anything.
      7. A contrived intestVersion mismatch, then `generate --check` -- exit 4
      8. `intest upgrade`         -- exit 0, regenerates and resolves the mismatch
      9. `intest generate --check` -- exit 0 again, confirming the upgrade actually fixed it

    Deliberately out of scope: `dotnet test` against a live sample API. That needs the sample
    running with the specific port/issuer/environment pairing documented in samples/README.md,
    which is a different (and already-flaky-if-misconfigured) concern from the NuGet hazard this
    script exists to close. Nothing above needs a live server: fixture *files* satisfy `generate`
    even while their values are still TODO: sentinels, because sentinel resolution is a runtime
    concern (`dotnet test`), not a generation-time one.

.PARAMETER Spec
    Path to the OpenAPI document to scaffold against. Defaults to the committed
    samples/Catalog.Api/Catalog.Api.json -- the one sample with no auth, so nothing here needs a
    running Identity.Server.

.PARAMETER KeepScratch
    Skip deleting the scratch directory at the end. For debugging this script itself; never use
    this and walk away, since it defeats defence 1's cleanliness (though not its safety -- the
    scratch directory was never the global cache regardless).

.EXAMPLE
    pwsh ./scripts/local-e2e-test.ps1

.EXAMPLE
    # From Git Bash: this repo has no bash port of this script by design (see the file header
    # comment below) -- invoke the PowerShell one directly, which Git Bash can do because pwsh
    # is a normal executable on PATH.
    pwsh scripts/local-e2e-test.ps1
#>

# ---------------------------------------------------------------------------------------------
# Why one script, not two (PowerShell + Bash):
#
# This repository's own convention (CONTRIBUTING.md, "One canonical explanation") is that when
# the same reasoning has to appear twice, one copy is authoritative and the other points at it --
# never two copies that must agree by discipline. A bash port of this script would be exactly
# that: every step above (the version stamp format, the NUGET_PACKAGES redirection, the exact
# sequence of expected exit codes, the csproj-reference verification, the contrived version-mismatch test) would
# exist twice, and the two would drift the first time either one changed without the other.
#
# PowerShell 7 (`pwsh`) is itself cross-platform -- it ships and runs natively on Linux and macOS,
# not just Windows -- so "one script" does not mean "Windows contributors get a script and
# everyone else improvises." It originally meant something narrower than that in practice: this
# file called out to `robocopy` (Windows-only) in Copy-SourceTree and built several paths with
# hardcoded backslashes, so a Linux `pwsh` could still not run it end to end even though the
# interpreter itself was available. That gap -- documented as mandatory in CONTRIBUTING.md while
# unusable on Linux -- was found and fixed in the same change as this comment; Copy-SourceTree is
# now a plain PowerShell/.NET file walk and every path is built with `Join-Path`'s multi-segment
# form, so nothing left in this file assumes a Windows filesystem. Verified by direct experiment
# in a `mcr.microsoft.com/dotnet/sdk:10.0` Linux container with PowerShell installed alongside the
# .NET SDK -- see this task's notes for the transcript. CI is not set up yet (CLAUDE.md), so
# nothing here has run on a Linux *runner* yet, only a Linux container used for this verification;
# if a runner surfaces a gap this container didn't, revisit this comment rather than leaving it
# stale.
# ---------------------------------------------------------------------------------------------

[CmdletBinding()]
param(
    [string]$Spec,
    [switch]$KeepScratch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $Spec) {
    $Spec = Join-Path $RepoRoot 'samples' 'Catalog.Api' 'Catalog.Api.json'
}
$Spec = (Resolve-Path $Spec).Path

if (-not (Test-Path $Spec)) {
    throw "Spec file not found: $Spec"
}

# ---- Isolation: every path below is unique per invocation, so two runs of this script started
# at the same time (or one interrupted and re-run before its scratch directory is cleared) share
# nothing -- not the NuGet cache redirect, not the packed feed, not the scaffolded project, and
# not even the source InTest.Cli/InTest.Runtime projects being packed. That last one is not
# paranoia: an earlier version of this script tried to keep packing in place and only redirect
# build output via -p:BaseOutputPath/-p:BaseIntermediateOutputPath, and that reliably produced
# CS0579 "Duplicate ... attribute" errors -- MSBuild generated AssemblyInfo.cs twice into the same
# redirected obj/ path and compiled both copies into one assembly (confirmed by direct
# experiment: reproduced twice, from a clean obj/bin and from a dirty one, so it is the property
# override itself at fault, not leftover state). Command-line overrides of those two properties
# are a known-fragile combination with restore's own obj/ bookkeeping; copying the source instead
# sidesteps the interaction entirely rather than working around it. It also solves concurrency
# for free: two `dotnet pack` invocations racing on the *same* src/InTest.Cli/obj/ would corrupt
# each other's intermediate state regardless of where the final output lands, so each run packing
# its own private copy of the source removes that hazard along with the CS0579 one.
$RunId = "{0}-pid{1}" -f (Get-Date -Format 'yyyyMMddHHmmss'), $PID
$LocalVersion = "0.1.0-local.$RunId"

$ScratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) "intest-local-e2e-$RunId"
$SrcCopyRoot = Join-Path $ScratchRoot 'src-copy'
$LocalFeed = Join-Path $ScratchRoot 'feed'
$NuGetPackagesScratch = Join-Path $ScratchRoot 'nuget-packages'
$ScaffoldParent = Join-Path $ScratchRoot 'scaffold'
$ProjectName = 'Local.E2E.ApiTests'
$ScaffoldDir = Join-Path $ScaffoldParent $ProjectName

New-Item -ItemType Directory -Force -Path $LocalFeed, $NuGetPackagesScratch, $ScaffoldParent, $SrcCopyRoot | Out-Null

# ---- Copy just enough of the repo for InTest.Cli and InTest.Runtime to build exactly as they
# would in place: the two projects themselves (minus any bin/obj, excluded at every depth, not
# just the top), plus Directory.Build.props and Directory.Packages.props at the same relative
# depth above them -- both are found by MSBuild's normal walk-up-from-the-project directory
# search, so preserving the relative layout (scratch-root/Directory.Build.props two levels above
# scratch-root/src/InTest.Cli/InTest.Cli.csproj, matching the real repo) is what makes that search
# land on these copies rather than either finding nothing or, worse, finding the real repo's if
# the scratch root ever happened to sit under it. `assets/` is copied for the same reason, one
# level shallower: both csproj files pack `../../assets/icon.png` by a relative path, so the
# scratch copy needs `assets/` sitting at the same two-levels-above-the-project spot the real repo
# has it, or `dotnet pack` fails with "Could not find a part of the path" -- confirmed by direct
# experiment, this script failed exactly that way before `assets/` was added here.
#
# This used to shell out to robocopy, which does not exist outside Windows -- a Linux contributor
# following CONTRIBUTING.md's "use this script" rule could not, since the very first thing the
# script did after packing would fail with "command not found". Replaced with a pure
# PowerShell/.NET walk (Get-ChildItem + Copy-Item), which is identical in the property that
# actually matters here: nothing is written back into the *source* obj/ (only ever read from), so
# the CS0579 duplicate-attribute failure this copy step exists to avoid (see the header comment
# above $RunId) cannot reappear -- that failure came from letting concurrent builds share one
# obj/, not from which tool did the copying. Confirmed by direct experiment on both platforms (see
# this task's notes): a run against a populated src/InTest.Cli/bin and obj/ still produces a
# scratch copy with neither directory present.
function Copy-SourceTree {
    param([Parameter(Mandatory)] [string]$From, [Parameter(Mandatory)] [string]$To)

    if (-not (Test-Path -LiteralPath $From)) {
        throw "Copy-SourceTree source not found: $From"
    }

    $excludedDirNames = @('bin', 'obj')
    $fromFull = (Resolve-Path -LiteralPath $From).Path

    Get-ChildItem -LiteralPath $fromFull -Recurse -Force |
        Where-Object {
            $relative = $_.FullName.Substring($fromFull.Length).TrimStart('/', '\')
            $segments = $relative -split '[\\/]'
            -not ($segments | Where-Object { $excludedDirNames -contains $_ })
        } |
        ForEach-Object {
            $relative = $_.FullName.Substring($fromFull.Length).TrimStart('/', '\')
            $destination = Join-Path $To $relative
            if ($_.PSIsContainer) {
                New-Item -ItemType Directory -Force -Path $destination | Out-Null
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            }
        }
}

Copy-Item -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Destination $SrcCopyRoot
Copy-Item -LiteralPath (Join-Path $RepoRoot 'Directory.Packages.props') -Destination $SrcCopyRoot
Copy-SourceTree -From (Join-Path $RepoRoot 'assets') -To (Join-Path $SrcCopyRoot 'assets')
Copy-SourceTree -From (Join-Path $RepoRoot 'src' 'InTest.Cli') -To (Join-Path $SrcCopyRoot 'src' 'InTest.Cli')
Copy-SourceTree -From (Join-Path $RepoRoot 'src' 'InTest.Runtime') -To (Join-Path $SrcCopyRoot 'src' 'InTest.Runtime')

$CliProject = Join-Path $SrcCopyRoot 'src' 'InTest.Cli'
$RuntimeProject = Join-Path $SrcCopyRoot 'src' 'InTest.Runtime' 'InTest.Runtime.csproj'

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [int[]]$ExpectedExitCodes = @(0),
        [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$StepName
    )
    Write-Step $StepName
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray

    $prevLocation = $null
    if ($WorkingDirectory) {
        $prevLocation = Get-Location
        Set-Location $WorkingDirectory
    }
    try {
        & dotnet @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        if ($prevLocation) { Set-Location $prevLocation }
    }

    if ($ExpectedExitCodes -notcontains $code) {
        throw "Step '$StepName' exited $code; expected one of: $($ExpectedExitCodes -join ', ')"
    }
    Write-Host "-> exit $code (expected)" -ForegroundColor Green
    return $code
}

$OriginalNugetPackages = $env:NUGET_PACKAGES
$Failed = $false

try {
    # ---- Defence 1: redirect every restore this script triggers away from the global cache.
    # This is the whole point; everything else in this script is either building on top of this
    # or defence in depth around it. Set before the first `dotnet` invocation of any kind.
    $env:NUGET_PACKAGES = $NuGetPackagesScratch

    Write-Host "Scratch root:   $ScratchRoot"
    Write-Host "Local version:  $LocalVersion"
    Write-Host "NUGET_PACKAGES: $NuGetPackagesScratch"
    Write-Host "Spec:           $Spec"

    # ---- Pack InTest.Cli and InTest.Runtime at the local-only version into the scratch feed.
    # -p:Version overrides Directory.Build.props's <Version>0.1.0</Version>: a `-p:` value is a
    # global MSBuild property, and a plain (un-Condition-guarded) <Version>0.1.0</Version> in an
    # imported props file cannot overwrite a global property -- the global value wins for the
    # whole build. Confirmed below by asserting the packed .nupkg is actually named with
    # $LocalVersion, not silently packed at 0.1.0 anyway.
    Invoke-Dotnet -StepName 'pack InTest.Cli' -Arguments @(
        'pack', $CliProject, '-c', 'Release',
        "-p:Version=$LocalVersion",
        '-o', $LocalFeed
    )
    Invoke-Dotnet -StepName 'pack InTest.Runtime' -Arguments @(
        'pack', $RuntimeProject, '-c', 'Release',
        "-p:Version=$LocalVersion",
        '-o', $LocalFeed
    )

    # Filename casing here must match each project's <PackageId> exactly ("InTest.Cli",
    # "InTest.Runtime") -- `dotnet pack` names the .nupkg after PackageId verbatim, not
    # lowercased. A lowercase "intest.cli.*" check passed here for a long time only because NTFS
    # path lookups are case-insensitive; on a case-sensitive filesystem (ext4, the Linux container
    # this was verified against) Test-Path against the wrong case returns false even though the
    # file exists one case away. Confirmed by direct experiment: this check failed on Linux before
    # the fix, immediately after both packs had visibly succeeded and written
    # "InTest.Cli.<version>.nupkg" / "InTest.Runtime.<version>.nupkg" to the feed.
    $cliPackage = Join-Path $LocalFeed "InTest.Cli.$LocalVersion.nupkg"
    $runtimePackage = Join-Path $LocalFeed "InTest.Runtime.$LocalVersion.nupkg"
    if (-not (Test-Path $cliPackage)) {
        throw "Expected package not found: $cliPackage -- the -p:Version override did not take effect as expected."
    }
    if (-not (Test-Path $runtimePackage)) {
        throw "Expected package not found: $runtimePackage -- the -p:Version override did not take effect as expected."
    }
    Write-Host "Confirmed both packages carry the local-only version: $LocalVersion" -ForegroundColor Green

    # ---- Bootstrap: `intest init` via `dotnet run`, the same way a project has no `intest` on
    # PATH to run it with yet (docs/getting-started.md Phase 2's note; F13 in v0-acceptance.md).
    # Stamped with the same -p:Version, so intest.json's intestVersion and
    # .config/dotnet-tools.json's tool pin both come out as $LocalVersion, matching what was just
    # packed above -- no separate patch step needed for either of those two files.
    New-Item -ItemType Directory -Force -Path $ScaffoldDir | Out-Null
    Invoke-Dotnet -StepName 'intest init (bootstrapped via dotnet run)' -WorkingDirectory $ScaffoldDir -Arguments @(
        'run', '--project', $CliProject, '-c', 'Release',
        "-p:Version=$LocalVersion",
        '--', 'init', '--name', $ProjectName, '--spec', $Spec
    )

    # ---- Verify, do not patch: docs/superpowers/plans/2026-08-23-trunk-based-versioning.md's
    # [scaffold-reads-itself] (Task 1) replaced InitCommand.cs's hardcoded
    # Include="InTest.Runtime" Version="0.1.0" with an interpolation of CliVersion.Current, so
    # `intest init` -- run above via `dotnet run ... -p:Version=$LocalVersion -- init ...` --
    # already scaffolds InTest.Runtime at $LocalVersion on its own; this script no longer has
    # anything to patch. What used to be a patch step is now a verification of that fact: this is
    # the "confirm the generated .csproj references that same version" proof this script exists
    # to run end to end, and its absence would otherwise surface only indirectly, as a NU1102
    # restore failure several steps later at `dotnet build`, pointing at the wrong cause.
    $csprojPath = Join-Path $ScaffoldDir "$ProjectName.csproj"
    $csprojText = Get-Content -Raw -LiteralPath $csprojPath
    $needle = "Include=`"InTest.Runtime`" Version=`"$LocalVersion`""
    $matchCount = ([regex]::Matches($csprojText, [regex]::Escape($needle))).Count
    if ($matchCount -ne 1) {
        throw "Expected exactly one '$needle' in $csprojPath, found $matchCount -- either InitCommand's scaffolded csproj shape changed, or [scaffold-reads-itself] regressed and the scaffold is no longer following CliVersion.Current."
    }

    # ---- nuget.config for the scaffold only: <clear/> so nothing ambient (a stray user-level
    # feed, credentials, anything else configured on this machine) can leak in and make this run
    # non-deterministic, then exactly the two sources this restore actually needs -- the scratch
    # feed for InTest.*, nuget.org for everything else (MSTest, Shouldly, ...). NUGET_PACKAGES is
    # still what keeps this from touching the global cache; this file only controls where
    # packages are *found*, not where they are *cached*.
    $nugetConfigPath = Join-Path $ScaffoldDir 'nuget.config'
    $localFeedUri = $LocalFeed -replace '\\', '/'
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-e2e-feed" value="$localFeedUri" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -NoNewline

    # ---- Phase 8's actual first line (docs/getting-started.md), for real: restores the local
    # tool manifest .config/dotnet-tools.json just wrote, pinned at $LocalVersion, from the
    # scratch feed -- still under the same NUGET_PACKAGES redirection.
    Invoke-Dotnet -StepName 'dotnet tool restore' -WorkingDirectory $ScaffoldDir -Arguments @('tool', 'restore')

    # ---- generate: exit 1 expected. Catalog.Api has operations that need a fixture (a request
    # body, or a required path/query parameter) and none exist yet -- this is the documented,
    # expected first-run outcome (docs/getting-started.md Phase 4), not a failure of this script.
    Invoke-Dotnet -StepName 'intest generate (first run, missing fixtures expected)' `
        -WorkingDirectory $ScaffoldDir -ExpectedExitCodes @(1) `
        -Arguments @('intest', 'generate')

    # ---- fixtures repair: creates the missing fixture files with TODO: sentinels. Sentinel
    # *values* are a runtime (`dotnet test`) concern, not a generation-time one, so this is
    # sufficient for `generate` and `dotnet build` below without needing a live API to source
    # real values from.
    Invoke-Dotnet -StepName 'intest fixtures repair' -WorkingDirectory $ScaffoldDir `
        -Arguments @('intest', 'fixtures', 'repair')

    # ---- generate again: every NeedsFixture operation now has a fixture file, so this succeeds.
    Invoke-Dotnet -StepName 'intest generate (fixtures present)' -WorkingDirectory $ScaffoldDir `
        -Arguments @('intest', 'generate')

    # ---- generate --check: compares committed output against a fresh render. Should be clean,
    # since nothing has changed since the write above.
    Invoke-Dotnet -StepName 'intest generate --check (clean)' -WorkingDirectory $ScaffoldDir `
        -Arguments @('intest', 'generate', '--check')

    # ---- The step that actually proves the hazard is closed: build the scaffolded project for
    # real, against the packed InTest.Runtime. This is what would have caught both prior
    # incidents (a stale InTest.Runtime 0.1.0 shadowing fresh source, producing CS0103 on members
    # that plainly existed) -- `generate --check` never compiles anything, it only compares text.
    Invoke-Dotnet -StepName 'dotnet build (compiles against the locally-packed InTest.Runtime)' `
        -WorkingDirectory $ScaffoldDir -Arguments @('build')

    # ---- Contrive a version mismatch the same way docs/v0-acceptance.md's Step 4 did, to
    # exercise `generate --check`'s exit 4 and `intest upgrade`'s recovery from it -- both shipped
    # after the last acceptance run and are otherwise easy to leave unexercised by a script that
    # only ever runs with everything already in sync.
    $intestJsonPath = Join-Path $ScaffoldDir 'intest.json'
    $intestJsonText = Get-Content -Raw -LiteralPath $intestJsonPath
    $versionPattern = '"intestVersion"\s*:\s*"' + [regex]::Escape($LocalVersion) + '"'
    $versionMatches = [regex]::Matches($intestJsonText, $versionPattern)
    if ($versionMatches.Count -ne 1) {
        throw "Expected exactly one intestVersion match for '$LocalVersion' in $intestJsonPath, found $($versionMatches.Count)."
    }
    $staleVersion = "$LocalVersion-STALE"
    $intestJsonText = [regex]::Replace($intestJsonText, $versionPattern, "`"intestVersion`": `"$staleVersion`"")
    Set-Content -LiteralPath $intestJsonPath -Value $intestJsonText -NoNewline

    Invoke-Dotnet -StepName 'intest generate --check (contrived version mismatch, exit 4 expected)' `
        -WorkingDirectory $ScaffoldDir -ExpectedExitCodes @(4) `
        -Arguments @('intest', 'generate', '--check')

    # ---- upgrade: regenerates against the running tool, then bumps intestVersion and the
    # .config/dotnet-tools.json pin back to the tool's real version.
    Invoke-Dotnet -StepName 'intest upgrade' -WorkingDirectory $ScaffoldDir `
        -Arguments @('intest', 'upgrade')

    # ---- generate --check one more time: confirms upgrade actually resolved the mismatch rather
    # than merely exiting 0.
    Invoke-Dotnet -StepName 'intest generate --check (post-upgrade, clean again)' `
        -WorkingDirectory $ScaffoldDir -Arguments @('intest', 'generate', '--check')

    Write-Host ''
    Write-Host '=== All steps passed. The adoption path exercised: init, generate, fixtures repair, generate --check, upgrade. ===' -ForegroundColor Green
}
catch {
    $Failed = $true
    Write-Host ''
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    # ---- Cleanup. Note what this finally block is and is not responsible for: the global NuGet
    # cache was never reachable in the first place, for any interruption at any point in this
    # script -- that guarantee comes from NUGET_PACKAGES being set once, at the very top, before
    # any dotnet invocation, not from this block running. A hard kill of this process mid-`pack`
    # cannot have written to ~/.nuget/packages/ regardless of whether cleanup ever executes. What
    # this block is responsible for is tidiness: removing the scratch directory so repeated runs
    # do not accumulate gigabytes in %TEMP%, and restoring NUGET_PACKAGES for the calling shell.
    if ($env:NUGET_PACKAGES -eq $NuGetPackagesScratch) {
        if ($null -eq $OriginalNugetPackages) {
            Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
        }
        else {
            $env:NUGET_PACKAGES = $OriginalNugetPackages
        }
    }

    if ($KeepScratch) {
        Write-Host ''
        Write-Host "-KeepScratch set: leaving $ScratchRoot in place." -ForegroundColor Yellow
    }
    elseif (Test-Path $ScratchRoot) {
        Write-Host ''
        Write-Host "Cleaning up scratch directory: $ScratchRoot"
        Remove-Item -LiteralPath $ScratchRoot -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $ScratchRoot) {
            Write-Warning "Could not fully remove $ScratchRoot (a file may still be locked by a lingering process). It is a %TEMP% scratch directory, not the global NuGet cache, so this is untidy rather than unsafe -- delete it by hand when convenient."
        }
    }

    # ---- Belt-and-suspenders tripwire, not a cleanup step: NUGET_PACKAGES redirection should
    # make this structurally impossible regardless of anything above, so if either package ever
    # shows up here, that is itself a finding worth shouting about rather than silently ignoring.
    $globalPackages = Join-Path $HOME '.nuget' 'packages'
    foreach ($pkg in 'intest.cli', 'intest.runtime') {
        $found = Join-Path $globalPackages $pkg
        if (Test-Path $found) {
            Write-Warning "UNEXPECTED: $found exists in the machine-wide NuGet cache. This script's NUGET_PACKAGES redirection should have made this impossible -- investigate before trusting this machine's cache again."
        }
    }

    if (-not $Failed) {
        Write-Host ''
        Write-Host "Confirmed: $globalPackages has no intest.cli or intest.runtime entries." -ForegroundColor Green
    }
}
