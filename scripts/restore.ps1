#requires -Version 5.1
<#
.SYNOPSIS
    Restore of the NuGet packages of the RCM.FastData.sln solution.

.DESCRIPTION
    Exit code 0 = success; exit code != 0 = exit code of dotnet restore.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\restore.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root "RCM.FastData.sln"

if (-not (Test-Path $sln)) {
    Write-Host "ERRORE: soluzione non trovata: $sln"
    exit 2
}

Push-Location $root
try {
    dotnet restore $sln
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -eq 0) {
    Write-Host "Restore completato con successo."
} else {
    Write-Host ("Restore FALLITO (exit {0})." -f $code)
}
exit $code
