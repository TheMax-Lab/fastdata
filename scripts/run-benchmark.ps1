#requires -Version 5.1
<#
.SYNOPSIS
    Runs the BenchmarkDotNet matrix (profile Smoke/Standard/Full/Stress) on a target.

.PARAMETER Profile
    Smoke (dry), Standard (medium, default), Full (long), Stress (verylong).

.PARAMETER Target
    net48 or net8.0 (default: net8.0).

.PARAMETER Filter
    Optional BDN glob on the benchmarks (default: "*", all).

.PARAMETER Artifacts
    BDN working directory (default: <repo>\artifacts\bench).

.DESCRIPTION
    The runner (bench\RCM.FastData.Benchmarks) uses ManualConfig with three exporters
    (markdown + csv + fulljson) that write to -Artifacts (default
    <repo>\artifacts\bench), passed to the runner via --artifacts. The execution is
    OUT-OF-PROCESS (toolchain InProcessEmit:
    the child process is precompiled per job, NOT InProcessToolchain) and prints the
    marker [BENCH] RUNTIME=... to verify the runtime actually in execution.

    The full log goes to artifacts\bench\<profile>-<target>.log and ends with the
    line "EXIT=<code> DURATION=<minutes>min".

    Exit code 0 = benchmark completed; 1 = failure; 2 = input error.
    NB: only one benchmark at a time (CPU contention = contaminated measurements).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-benchmark.ps1 -Profile Smoke -Target net8.0
    powershell -ExecutionPolicy Bypass -File scripts\run-benchmark.ps1 -Profile Standard -Target net48
#>
[CmdletBinding()]
param(
    [string]$Profile = "Standard",
    [string]$Target = "net8.0",
    [string]$Filter = "*",
    [string]$Artifacts = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$benchDir = Join-Path $root "bench\RCM.FastData.Benchmarks"

if (-not (Test-Path $benchDir)) {
    Write-Host "ERRORE: progetto benchmark non trovato: $benchDir"
    exit 2
}

$presets = @{ "Smoke" = "dry"; "Standard" = "medium"; "Full" = "long"; "Stress" = "verylong" }
if (-not $presets.ContainsKey($Profile)) {
    Write-Host ("ERRORE: profilo sconosciuto '{0}' (validi: {1})." -f $Profile, ($presets.Keys -join ", "))
    exit 2
}
$preset = $presets[$Profile]

if ($Target -ne "net48" -and $Target -ne "net8.0") {
    Write-Host ("ERRORE: target sconosciuto '{0}' (validi: net48, net8.0)." -f $Target)
    exit 2
}

if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    $Artifacts = Join-Path $root "artifacts\bench"
}
if (-not (Test-Path $Artifacts)) {
    New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null
}

$logFile = Join-Path $Artifacts ("{0}-{1}.log" -f $Profile.ToLowerInvariant(), $Target)

Push-Location $benchDir
try {
    Write-Host ("== build -c Release -f {0} (progetto benchmark) ==" -f $Target)
    dotnet build -c Release -f $Target
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERRORE: build del progetto benchmark fallita."
        exit 1
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Host ("== benchmark profilo {0} (preset {1}) su {2} -> log: {3} ==" -f $Profile, $preset, $Target, $logFile)
    dotnet run -c Release -f $Target -- --job $preset --filter $Filter --artifacts $Artifacts 2>&1 | Out-File $logFile -Encoding utf8
    $code = $LASTEXITCODE
    $sw.Stop()

    "EXIT=$code DURATION=$([math]::Round($sw.Elapsed.TotalMinutes, 1))min" | Out-File $logFile -Append -Encoding utf8
}
finally {
    Pop-Location
}

if ($code -eq 0) {
    Write-Host ("Benchmark completato (exit 0, {0} min). Risultati in {1}\results\" -f [math]::Round($sw.Elapsed.TotalMinutes, 1), $Artifacts)
    exit 0
}
Write-Host ("Benchmark FALLITO (exit {0}). Log: {1}" -f $code, $logFile)
exit 1
