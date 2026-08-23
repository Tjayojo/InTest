<#
.SYNOPSIS
    Fails loudly if a test project silently ran zero tests.

.DESCRIPTION
    docs/superpowers/plans/2026-08-22-intest-ci.md, Task 2 Step 3: CI must assert the suites
    actually ran, not just that `dotnet test` exited 0. Exit code 0 does not imply anything ran —
    a wrong path, a typo'd filter, or (per the two source comments this reasoning points at,
    tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs:1183 and
    tests/InTest.Cli.Tests/GenerateCheckCommandTests.cs:351) the generic `dotnet test` hazard of
    matching nothing and reporting success anyway, would all look identical to a real green run in
    the Actions UI: a checkmark next to a step that produced no evidence at all.

    The plan's preferred mechanism, adopted here, is to parse the .trx `dotnet test --logger trx`
    already writes and assert both that each expected assembly is named inside it and that it
    reports more than zero executed tests — deliberately not exact per-assembly counts, which
    would turn every legitimate test addition into a required two-place edit (the workflow file
    and the test project) for no safety this weaker check does not already provide. See the plan's
    Task 2 Step 3 note for the full argument.

    Each caller (the "fast" job and the "golden" job in .github/workflows/build-and-test.yml) sets
    `--logger "trx;LogFileName=<AssemblyName>.trx"` explicitly per project, so this script does not
    need to guess a generated filename (dotnet test's default naming is timestamp-based and
    disambiguates collisions with a bracketed suffix — confirmed by direct experiment, not by
    reading the docs, while designing this check: `dotnet test InTest.sln --logger trx` on this
    machine produced `..._net10.0.trx` and `..._net10.0[1].trx` for two projects that finished in
    the same second, with nothing in either filename identifying which project it belongs to).
    Relying on the caller-chosen filename to locate the file, then verifying the assembly identity
    recorded *inside* it, catches both failure shapes: no file at all (the project never ran), and
    a file present but empty or mismatched (stale content, or dotnet test quietly matching the
    wrong project).

.PARAMETER ResultsDirectory
    Directory containing one `<AssemblyName>.trx` per expected assembly.

.PARAMETER ExpectedAssembly
    Assembly names (without `.dll` or `.trx`) that must each have a .trx with more than zero
    executed tests, and whose own content must reference a DLL of that name.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string[]]$ExpectedAssembly
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($assembly in $ExpectedAssembly) {
    $trxPath = Join-Path $ResultsDirectory "$assembly.trx"

    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        $failures.Add("$assembly - no .trx found at $trxPath (the test project did not run, or --logger's LogFileName was not honoured)")
        continue
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw

    # The .trx root element is namespaced (xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"),
    # so a plain SelectSingleNode XPath without a namespace manager silently matches nothing.
    # Confirmed by direct experiment while writing this script: an unqualified
    # "//ResultSummary/Counters" against a real .trx from this repo returned $null.
    $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $counters = $trx.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    if ($null -eq $counters) {
        $failures.Add("$assembly - $trxPath has no <ResultSummary><Counters> element; not a well-formed VSTest .trx")
        continue
    }

    $total = [int]$counters.total
    if ($total -le 0) {
        $failures.Add("$assembly - $trxPath reports total=$total (dotnet test exited without error but executed nothing)")
        continue
    }

    # Belt-and-suspenders: dotnet test's own exit code already fails the job on a real test
    # failure, so this branch should be unreachable in practice. It is checked anyway because the
    # cost is one XPath query and the alternative — a step reordering or an exit-code swallowed by
    # a wrapping script — silently turns this assertion into the only thing standing between a red
    # suite and a green checkmark.
    $failedCount = [int]$counters.failed
    $errorCount = [int]$counters.error
    if ($failedCount -gt 0 -or $errorCount -gt 0) {
        $failures.Add("$assembly - $trxPath reports failed=$failedCount error=$errorCount (dotnet test should already have failed the job on this)")
        continue
    }

    $codeBaseNodes = $trx.SelectNodes('//t:TestMethod/@codeBase', $ns)
    $expectedDll = "$assembly.dll"
    $matchesAssembly = $false
    foreach ($node in $codeBaseNodes) {
        if ($node.Value.EndsWith($expectedDll, [System.StringComparison]::OrdinalIgnoreCase)) {
            $matchesAssembly = $true
            break
        }
    }
    if (-not $matchesAssembly) {
        $failures.Add("$assembly - $trxPath contains no <TestMethod codeBase=...> ending in $expectedDll (file present but content does not identify this assembly)")
        continue
    }

    Write-Host "OK: $assembly - $total test(s) executed, 0 failed, 0 errored ($trxPath)"
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host '::error::One or more expected test assemblies did not verifiably run:'
    foreach ($failure in $failures) {
        Write-Host "::error::  $failure"
    }
    exit 1
}

Write-Host ''
Write-Host "All $($ExpectedAssembly.Count) expected assembl$(if ($ExpectedAssembly.Count -eq 1) { 'y' } else { 'ies' }) verifiably ran."
