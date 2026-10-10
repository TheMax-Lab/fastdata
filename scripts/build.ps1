#requires -Version 5.1
<#
.SYNOPSIS
    Build of the RCM.FastData.sln solution for the requested targets.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Target
    net48, net8.0, or "all" (default = all, in this order).

.DESCRIPTION
    Compiles the solution (all projects: correct library, immutable original
    snapshot, tests, benchmarks, probe) for each requested target.
    Exit code 0 = all targets compiled without errors; 1 = at least one failure;
    2 = input error (unknown target).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build.ps1
    powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Configuration Debug -Target net8.0
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root "RCM.FastData.sln"

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

Push-Location $root
try {
    $failed = 0
    foreach ($t in $targets) {
        Write-Host ("== build -c {0} -f {1} ==" -f $Configuration, $t)
        dotnet build $sln -c $Configuration -f $t
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
    Write-Host ("Build completata: {0} target compilato/i senza errori." -f $targets.Count)
    exit 0
}
Write-Host ("Build COMPLETATA CON ERRORI: {0} target fallito/i." -f $failed)
exit 1
