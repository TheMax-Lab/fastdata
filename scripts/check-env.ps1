#requires -Version 5.1
<#
.SYNOPSIS
    Verifies the environment required by the plan (Phase F): .NET SDK, .NET 8 runtime,
    .NET Framework 4.8 + reference assemblies.

.DESCRIPTION
    Exit code 0 = complete environment (all requirements present).
    Exit code 1 = at least one requirement missing (missing lines marked [MISSING]).
    The script does not modify anything.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\check-env.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$missing = 0

function Mark($ok, $label, $detail) {
    if ($ok) {
        Write-Host ("  [OK]      {0,-38} {1}" -f $label, $detail)
    } else {
        Write-Host ("  [MISSING] {0,-38} {1}" -f $label, $detail)
        $script:missing++
    }
}

Write-Host "== Ambiente RCM.FastData =="

# 1. dotnet SDK
$sdk = $null
try { $sdk = (dotnet --version 2>$null) } catch { }
$sdkDetail = if ([string]::IsNullOrWhiteSpace($sdk)) { "non trovato" } else { $sdk.Trim() }
Mark (-not [string]::IsNullOrWhiteSpace($sdk)) "dotnet SDK" $sdkDetail

# 2. .NET 8 runtime (for the net8.0 target)
$runtimes = dotnet --list-runtimes 2>$null
$hasNet8 = $runtimes | Where-Object { $_ -match "Microsoft.NETCore.App 8\." }
Mark ($null -ne $hasNet8) ".NET 8 runtime" ($(if ($hasNet8) { ($hasNet8 | Select-Object -First 1).Trim() } else { "non trovato" }))

# 3. .NET Framework 4.8 (for the net48 target)
$ndp = $null
try {
    $ndp = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -ErrorAction Stop
} catch { }
$hasNetFx48 = ($null -ne $ndp) -and ($ndp.Release -ge 528040)
Mark $hasNetFx48 ".NET Framework 4.8" ($(if ($ndp) { "Release " + $ndp.Release } else { "chiave registro non trovata" }))

# 4. .NET Framework v4.8 reference assemblies (net48 compilation outside VS)
$refAsm = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll"
$refAsm48 = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"
Mark ((Test-Path $refAsm) -and (Test-Path $refAsm48)) "RefAssemblies v4.8" ($(if (Test-Path $refAsm48) { "presenti" } else { "assenti (necessario il pacchetto Microsoft.NETFramework.ReferenceAssemblies.net48)" }))

# 5. BenchmarkDotNet in the benchmark project
$bdnCsproj = Join-Path $PSScriptRoot "..\bench\RCM.FastData.Benchmarks\RCM.FastData.Benchmarks.csproj"
$hasBdn = (Test-Path $bdnCsproj) -and ((Get-Content $bdnCsproj -Raw) -match 'BenchmarkDotNet[^"]*"Version="([^"]+)"')
$bdnVer = if ($hasBdn) { $Matches[1] } else { "?" }
Mark $hasBdn "BenchmarkDotNet (bench)" "versione $bdnVer nel csproj"

Write-Host ""
if ($missing -eq 0) {
    Write-Host "Esito: AMBIENTE COMPLETO (0 requisiti mancanti)."
    exit 0
}
Write-Host ("Esito: {0} requisito/i mancante/i." -f $missing)
exit 1
