#requires -Version 5.1
<#
.SYNOPSIS
    Runs the RCM.FastData test suite (xunit) for the requested targets.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Target
    net48, net8.0, or "all" (default = all).

.PARAMETER Filter
    Optional VSTest filter (e.g. "FullyQualifiedName~ComputeTests").

.DESCRIPTION
    Compiles if needed and runs dotnet test for each target. The trx reports
    are saved in artifacts\testresults\matrix-d.<target>.trx.
    Exit code 0 = all tests passed on all targets; 1 = at least one
    failure; 2 = input error.

    NB: do not run in parallel with a benchmark (CPU contention =
    contaminated measurements).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1
    powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1 -Target net8.0 -Filter "FullyQualifiedName~ComputeTests"
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Target = "all",
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root "RCM.FastData.sln"
$testResults = Join-Path $root "artifacts\testresults"

if (-not (Test-Path $sln)) {
    Write-Host "ERRORE: soluzione non trovata: $sln"
    exit 2
}

$valid = @("net48", "net8.0")
$targets = if ($Target -eq "all") { $valid } else { @($Target) }
foreach ($t in $targets) {
    if ($valid -notcontains $t) {
        Write-Host ("ERRORE: target sconosciuto '{0}' (validi: net48, net8.0, all)." -f $t)
        exit 2
    }
}

if (-not (Test-Path $testResults)) {
    New-Item -ItemType Directory -Path $testResults -Force | Out-Null
}

Push-Location $root
try {
    $failed = 0
    foreach ($t in $targets) {
        $trx = "matrix-d.{0}.trx" -f $t
        $logger = "trx;LogFileName=$trx"
        $testArgs = @("test", $sln, "-c", $Configuration, "-f", $t,
                      "--logger", $logger, "--results-directory", $testResults)
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $testArgs += @("--filter", $Filter)
        }

        Write-Host ("== test -c {0} -f {1} ==" -f $Configuration, $t)
        dotnet @testArgs
        $code = $LASTEXITCODE
        if ($code -eq 0) {
            Write-Host ("   OK   {0}" -f $t)
        } else {
            Write-Host ("   FAIL {0} (exit {1})" -f $t, $code)
            $failed++
        }
        Write-Host ""
    }
}
finally {
    Pop-Location
}

if ($failed -eq 0) {
    Write-Host "Test completati: tutti i target superati."
    exit 0
}
Write-Host ("Test COMPLETATI CON FALLIMENTI: {0} target." -f $failed)
exit 1
