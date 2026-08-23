<#
.SYNOPSIS
    Runs InTest's generator against the three sample OpenAPI specs, end to end, and confirms
    `generate --check` reports clean.

.DESCRIPTION
    `[dogfood]` (docs/superpowers/plans/2026-08-22-intest-ci.md, Task 3): the three sample specs
    under samples/ (Catalog.Api.json, Inventory.Api.json, Orders.Api.json) are the only
    real-world-shaped, hand-authored, multi-producer OpenAPI documents in this repository, and no
    automated test runs the generator against any of them. InTest.Golden.Tests exercises
    Specs/orders.json, Specs/hostile-text.json and inline string specs, and touches samples/
    nowhere.  CliExitCodeTests.CheckFlagIsWiredThroughToGenerate already exercises the
    init -> generate -> --check -> mutate -> --check shape through the real CLI, but on a trivial
    inline spec. What this script adds is coverage of the documents, not the flow.

    `samples/Identity.Server` is a Duende provider with no OpenAPI document -- three specs are
    exercised here, not four. (An earlier revision of the CI plan said "four sample APIs",
    conflating sample *directories* with sample *specs"; CLAUDE.md's own "four sample APIs" is
    true of directories and is where that slip came from.)

    Deliberately does NOT build the scaffolded projects (no `dotnet build`, no NuGet restore of
    them): `generate` and `--check` are static and read only the spec and the committed
    Generated/ output. Proving a scaffold actually *compiles* against a real InTest.Runtime is
    InTest.Golden.Tests's job (CLAUDE.md: "the only suite that proves generated code both
    compiles and runs"), not this script's. Starting the sample APIs over real HTTP is out of
    scope for the same reason the plan gives: that needs the port/issuer/environment pairing
    samples/README.md documents, where getting it wrong produces 500s and silent 404s rather than
    an obvious failure -- exactly the kind of flake the most-run job in this repository should not
    carry.

    The sequence per spec, exactly as README.md:106-113 documents it and as the plan's Task 3
    Step 1 specifies -- rev 1 of that plan asked for exit 0 from the first `generate`; the
    reviewer ran it and found the tool does not do that:

        init (0) -> generate (1, fixtures missing) -> fixtures repair (0) -> generate (0)
            -> generate --check (0)

    Exit 1 from the first `generate` is the designed outcome, not a failure: every sample
    declares at least one operation that needs a fixture (a request body, or a required
    path/query parameter), and a fresh scaffold has none yet. `fixtures repair` writes them with
    `TODO:` sentinels; sentinel *values* are a `dotnet test`-time concern (a live API to source
    real values from), not a generation-time one, so `generate` succeeds afterward without one --
    confirmed already by scripts/local-e2e-test.ps1, which exercises the identical claim against
    Catalog.Api.json.

    Every scaffold is written under -ScaffoldRoot, which the caller must point outside the git
    checkout (the workflow passes runner.temp) -- so nothing this script does can dirty the
    working tree by construction. The workflow still asserts that with a plain
    `git status --porcelain` after this script returns (Task 3 Step 3), rather than trusting the
    isolation silently.

.PARAMETER RepoRoot
    Root of the InTest checkout. Used to locate the three sample specs.

.PARAMETER ScaffoldRoot
    Directory to scaffold test projects under. Must be outside $RepoRoot.

.PARAMETER CliDll
    Path to an already-built InTest.Cli.dll. Built once by the caller (the workflow's own build
    step), not by this script: 5 commands x 3 specs would otherwise mean 15 separate `dotnet run`
    builds of the identical binary, paying for MSBuild evaluation 15 times over for no new
    information -- `dotnet <dll>` runs the same code `dotnet run` would, once already built.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$RepoRoot,
    [Parameter(Mandatory = $true)] [string]$ScaffoldRoot,
    [Parameter(Mandatory = $true)] [string]$CliDll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$CliDll = (Resolve-Path -LiteralPath $CliDll).Path

New-Item -ItemType Directory -Force -Path $ScaffoldRoot | Out-Null
$ScaffoldRoot = (Resolve-Path -LiteralPath $ScaffoldRoot).Path

$Specs = @(
    @{ Name = 'Catalog';   Json = Join-Path $RepoRoot 'samples' 'Catalog.Api'   'Catalog.Api.json' },
    @{ Name = 'Inventory'; Json = Join-Path $RepoRoot 'samples' 'Inventory.Api' 'Inventory.Api.json' },
    @{ Name = 'Orders';    Json = Join-Path $RepoRoot 'samples' 'Orders.Api'    'Orders.Api.json' }
)

function Invoke-Intest {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [int[]]$ExpectedExitCodes,
        [Parameter(Mandatory)] [string]$StepName
    )
    Write-Host ''
    Write-Host "=== $StepName ===" -ForegroundColor Cyan
    Write-Host "dotnet $CliDll $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet $CliDll @Arguments
    $code = $LASTEXITCODE
    if ($ExpectedExitCodes -notcontains $code) {
        throw "Step '$StepName' exited $code; expected one of: $($ExpectedExitCodes -join ', ')"
    }
    Write-Host "-> exit $code (expected)" -ForegroundColor Green
}

foreach ($spec in $Specs) {
    if (-not (Test-Path -LiteralPath $spec.Json)) {
        throw "Spec not found: $($spec.Json)"
    }

    # A rooted --spec path is passed unchanged through GenerateCommand's
    # Path.Combine(projectRoot, config.SpecSource) on both platforms (.NET's Path.Combine
    # discards the first argument whenever the second is rooted), so the absolute samples/ path
    # resolves correctly regardless of where -ScaffoldRoot lands -- no relative-path arithmetic
    # between the scaffold (outside the checkout) and the spec (inside it) is needed.
    $projectName = "$($spec.Name).Dogfood"
    $projectDir = Join-Path $ScaffoldRoot $projectName

    Invoke-Intest -StepName "$($spec.Name): init" -ExpectedExitCodes @(0) -Arguments @(
        'init', '--project', $projectDir, '--name', $projectName, '--spec', $spec.Json
    )

    Invoke-Intest -StepName "$($spec.Name): generate (fixtures missing, exit 1 expected)" -ExpectedExitCodes @(1) -Arguments @(
        'generate', '--project', $projectDir
    )

    Invoke-Intest -StepName "$($spec.Name): fixtures repair" -ExpectedExitCodes @(0) -Arguments @(
        'fixtures', 'repair', '--project', $projectDir
    )

    Invoke-Intest -StepName "$($spec.Name): generate" -ExpectedExitCodes @(0) -Arguments @(
        'generate', '--project', $projectDir
    )

    Invoke-Intest -StepName "$($spec.Name): generate --check" -ExpectedExitCodes @(0) -Arguments @(
        'generate', '--project', $projectDir, '--check'
    )
}

Write-Host ''
Write-Host "All three sample specs: init (0), generate (1), fixtures repair (0), generate (0), generate --check (0)." -ForegroundColor Green
