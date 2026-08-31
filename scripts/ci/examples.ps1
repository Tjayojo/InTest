<#
.SYNOPSIS
    For every committed example project under examples/, restores the pinned InTest.Cli tool,
    confirms the committed Generated/ output still matches a fresh render, and builds the project.

.DESCRIPTION
    docs/superpowers/plans/2026-08-31-intest-cross-framework-examples.md, Task 5 Step 2: with all
    six examples in place (Catalog and Orders, each under MSTest, xUnit and NUnit), nothing in CI
    exercised any of them -- `dogfood` (scripts/ci/dogfood.ps1) drives `generate`/`--check` against
    samples/, not examples/, and deliberately never builds what it scaffolds; InTest.Golden.Tests
    proves the templates render correctly, but every Golden project substitutes a `ProjectReference`
    for the adapter's `PackageReference`, so it cannot prove the *published* InTest.Runtime.MSTest /
    InTest.Runtime.xUnit / InTest.Runtime.NUnit packages on nuget.org still work. This script closes
    that gap: it is the only place in CI that restores an example's own local tool manifest
    (`.config/dotnet-tools.json`, pinning `intest.cli`) and its own `PackageReference`s from
    nuget.org, then builds the result. See the plan's "Why this is not redundant with the Golden
    suite" section for the full division of labour; CLAUDE.md's "What this is" section is the
    canonical statement and points back here rather than repeating it.

    This proves the *adopter* path -- a team that ran `dotnet tool install -g InTest.Cli` and
    `init`, and whose generated project references the adapter packages exactly as `init` scaffolds
    them -- reproduces the same generated output that is committed today. It does **not** catch a
    regression in an *unreleased* CLI change: every example is pinned to a published version
    (`0.1.0-preview.2` at the time this script was written), so a template edit that has not yet
    been tagged and published cannot show up here. That is deliberate, not a gap to close later --
    InTest.Golden.Tests, which builds against `src/` via `ProjectReference`, already owns proving an
    unreleased template change compiles and runs; duplicating that here would mean two suites
    racing to be the one that actually catches a template regression, with no way to tell from a red
    run which one meant it.

    Directories are discovered, not listed, for the identical anti-vacuity reason
    ExampleProjectVersionMarkerTests.ExampleProjectDirectories() gives for doing the same thing: a
    seventh example added later must be covered with no change to this script, and a script that
    silently walks zero directories would pass for the wrong reason, not because nothing is wrong.
    A directory qualifies by carrying its own intest.json, exactly as that test's discovery does --
    examples/Directory.Packages.props sits alongside these directories as a file, not a directory,
    so it is excluded by construction rather than needing its own filter.

    Each qualifying directory gets, in order, exactly the three commands docs/getting-started.md
    documents for a CI check (README.md's "Using it" section, `generate --check`'s own line):

        dotnet tool restore              -- resolves the pinned intest.cli from nuget.org
        dotnet intest generate --check   -- must exit 0; a non-zero exit means the committed
                                             Generated/*.g.cs, spec-paths.json or spec-schemas.json
                                             disagrees with what the published CLI renders today
        dotnet build                     -- proves the committed output actually compiles against
                                             the published adapter package, not just that the text
                                             matches

    All three run for every directory before this script fails the run, rather than stopping at
    the first failure, so one broken example does not hide a second, unrelated one in the same CI
    run -- the same reasoning ExampleProjectVersionMarkerTests' own offenders list follows.

.PARAMETER RepoRoot
    Root of the InTest checkout. Used to locate examples/.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

$examplesRoot = Join-Path $RepoRoot 'examples'

# Same discovery rule as ExampleProjectVersionMarkerTests.ExampleProjectDirectories(): any
# immediate subdirectory of examples/ that carries its own intest.json. Deliberately not a
# hardcoded list -- see this script's own header.
$exampleDirs = Get-ChildItem -Path $examplesRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'intest.json') } |
    Sort-Object -Property Name

# Anti-vacuity, same reasoning as ExampleProjectVersionMarkerTests and PackageVersionCouplingTests:
# a CI step that silently checks zero examples is green for the wrong reason. Either examples/ was
# reorganised and this script's discovery no longer matches its shape, or every committed example
# was removed -- either way this must not pass silently.
if ($exampleDirs.Count -eq 0) {
    Write-Host "::error::No directories under '$examplesRoot' contain an intest.json -- either examples/ was reorganised and this script's discovery no longer matches its shape, or every committed example was removed. Either way this must not pass silently."
    exit 1
}

Write-Host "Discovered $($exampleDirs.Count) example project(s):"
foreach ($dir in $exampleDirs) {
    Write-Host " - $($dir.Name)"
}

# Collected across every directory rather than exiting on the first failure, so one broken example
# cannot hide a second, unrelated one in the same CI run.
$failures = @()

foreach ($dir in $exampleDirs) {
    Write-Host ""
    Write-Host "=== $($dir.Name) ==="
    Push-Location $dir.FullName
    try {
        Write-Host "> dotnet tool restore"
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            $failures += "$($dir.Name): dotnet tool restore exited $LASTEXITCODE"
            continue
        }

        Write-Host "> dotnet intest generate --check"
        dotnet intest generate --check
        if ($LASTEXITCODE -ne 0) {
            $failures += "$($dir.Name): dotnet intest generate --check exited $LASTEXITCODE (committed Generated/ output disagrees with a fresh render from the published CLI)"
            continue
        }

        Write-Host "> dotnet build"
        dotnet build
        if ($LASTEXITCODE -ne 0) {
            $failures += "$($dir.Name): dotnet build exited $LASTEXITCODE"
            continue
        }
    }
    finally {
        Pop-Location
    }
}

if ($failures.Count -gt 0) {
    Write-Host "::error::One or more example projects failed:"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host ""
Write-Host "All $($exampleDirs.Count) example project(s) passed generate --check and dotnet build."
