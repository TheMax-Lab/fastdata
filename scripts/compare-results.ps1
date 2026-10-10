#requires -Version 5.1
<#
.SYNOPSIS
    Compares the BenchmarkDotNet CSV reports and computes the three plan ratios.

.PARAMETER ResultsDir
    Directory with the *-report.csv files (default: <repo>\artifacts\bench\results).

.PARAMETER Tag
    Execution label (e.g. "standard-net8.0"): becomes the output file name
    confronto-<Tag>.md. Default: "latest".

.DESCRIPTION
    The BDN reports (one line per benchmark) have the format:
        Method;Job;...;N;Mean;Error;Allocated
    where Method = '<Subject>: <case>' with Subject in {SystemData, Original, Fixed}
    and N the matrix parameter. For each case (description + N) the script computes:

        Speedup originale    = Mean(System.Data) / Mean(Original)
        Speedup ottimizzata  = Mean(System.Data) / Mean(Fixed)
        Miglioramento interno = Mean(Original)   / Mean(Fixed)

    An "n/a" value indicates that the subject is not implemented for that family
    (e.g. PkLookup on Original: the original library has no Rows.Find).
    Output: markdown table on stdout and on <repo>\artifacts\bench\comparisons\confronto-<Tag>.md.

    Exit code 0 = at least one complete comparison; 1 = no report found;
    2 = unparsable reports (no useful line).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\compare-results.ps1 -Tag standard-net8.0
#>
[CmdletBinding()]
param(
    [string]$ResultsDir = "",
    [string]$Tag = "latest"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if ([string]::IsNullOrWhiteSpace($ResultsDir)) {
    $ResultsDir = Join-Path $root "artifacts\bench\results"
}
if (-not (Test-Path $ResultsDir)) {
    Write-Host "ERRORE: directory risultati non trovata: $ResultsDir"
    exit 1
}

$files = Get-ChildItem $ResultsDir -Filter "*-report.csv" -ErrorAction SilentlyContinue
if ($null -eq $files -or $files.Count -eq 0) {
    Write-Host "ERRORE: nessun file *-report.csv in $ResultsDir (prima: run-benchmark.ps1)"
    exit 1
}

# BDN units: Mean in ns/us/ms/s, Allocated in bytes (B/KB/MB/GB/TB, 10^3).
# NB: BDN writes microseconds with the Greek mu U+03BC; values with thousands
# are quoted in the CSV and imported with a comma ("6,880.6 μs").
$timeUnits = @{ "ns" = 1.0; "us" = 1e3; "ms" = 1e6; "s" = 1e9 }
$memUnits  = @{ "b" = 1.0; "kb" = 1e3; "mb" = 1e6; "gb" = 1e9; "tb" = 1e12 }

# NB: the mu characters are built at runtime with [char]0xNNNN: this script
# must stay pure ASCII, otherwise PS 5.1 reads it as ANSI (without BOM)
# and the accents/mu in the regex get corrupted at parse time.
$greekMu = [string][char]0x03BC
$microSign = [string][char]0x00B5

function Convert-BdnValue([string]$text, [hashtable]$units) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $t = $text.Trim()
    if ($t -eq "NA" -or $t -eq "-") { return $null }
    $t = $t -replace ",", ""  # thousands separator
    $t = $t -replace $greekMu, "u"   # microseconds -> us
    $t = $t -replace $microSign, "u"
    $m = [regex]::Match($t, '^([0-9]+(?:\.[0-9]+)?)\s*([a-zA-Z]*)$')
    if (-not $m.Success) { return $null }
    $num = [double]$m.Groups[1].Value
    $u = $m.Groups[2].Value.ToLowerInvariant()
    $mult = $units[$u]
    if ($null -eq $mult) { return $null }
    return $num * $mult
}

# case key -> @{ SystemData=ns; Original=ns; Fixed=ns; SystemDataAlloc=B; ... }
$cases = [ordered]@{}
$parsed = 0

foreach ($f in $files) {
    $rows = Import-Csv $f.FullName -Delimiter ";"
    foreach ($r in $rows) {
        if ([string]::IsNullOrWhiteSpace($r.Method)) { continue }

        $method = $r.Method.Trim().Trim("'").Trim()
        $idx = $method.IndexOf(": ")
        if ($idx -lt 0) { continue }
        $subject = $method.Substring(0, $idx).Trim()
        $case = $method.Substring($idx + 2).Trim()

        $key = $case
        if ($r.PSObject.Properties["N"] -and -not [string]::IsNullOrWhiteSpace($r.N)) {
            $key = $case + " [N=" + $r.N + "]"
        }

        $meanNs = Convert-BdnValue $r.Mean $timeUnits
        $allocB = Convert-BdnValue $r.Allocated $memUnits
        if ($null -eq $meanNs -and $null -eq $allocB) { continue }
        $parsed++

        if (-not $cases.Contains($key)) { $cases[$key] = @{} }
        $slot = $cases[$key]
        if ($null -ne $meanNs) { $slot[$subject + "_mean"] = [double]$meanNs }
        if ($null -ne $allocB) { $slot[$subject + "_alloc"] = [double]$allocB }
    }
}

if ($parsed -eq 0) {
    Write-Host "ERRORE: nessun record parsabile nei report (formato inaspettato)."
    exit 2
}

# ---------- table ----------
# invariant formatting (the -f operator would use the local culture:
# on it-IT the decimals would come with a comma, "2.474,89").
$invCulture = [System.Globalization.CultureInfo]::InvariantCulture

function FmtMs($ns) {
    if ($null -eq $ns) { return "n/a" }
    return [string]::Format($invCulture, "{0:N4}", ([double]$ns / 1e6))
}
function FmtRatio($a, $b) {
    if ($null -eq $a -or $null -eq $b) { return "n/a" }
    $a = [double]$a
    $b = [double]$b
    if ($b -eq 0) { return "n/a" }
    return [string]::Format($invCulture, "{0:N2}", ($a / $b))
}
function FmtAlloc($b) {
    if ($null -eq $b) { return "n/a" }
    $b = [double]$b
    if ($b -ge 1e9) { return [string]::Format($invCulture, "{0:N2} GB", ($b / 1e9)) }
    if ($b -ge 1e6) { return [string]::Format($invCulture, "{0:N2} MB", ($b / 1e6)) }
    if ($b -ge 1e3) { return [string]::Format($invCulture, "{0:N1} KB", ($b / 1e3)) }
    return [string]::Format($invCulture, "{0:N0} B", $b)
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Confronto benchmark - RCM.FastData")
$lines.Add("")
$lines.Add("Tag: **$Tag** | Generato: $(Get-Date -Format "yyyy-MM-dd HH:mm") da ``compare-results.ps1``")
$lines.Add("Fonti: `$ResultsDir` = $ResultsDir")
$lines.Add("")
$lines.Add("Definizioni: **Speedup originale** = Mean(System.Data)/Mean(Original);")
$lines.Add("**Speedup ottimizzata** = Mean(System.Data)/Mean(Fixed); **Miglioramento interno** = Mean(Original)/Mean(Fixed).")
$lines.Add("> 1 = FastData piu' veloce di System.Data; < 1 = piu' lenta. ``n/a`` = soggetto non implementato per la famiglia.")
$lines.Add("")
$lines.Add("| Caso | System.Data (ms) | Original (ms) | Fixed (ms) | Speedup originale | Speedup ottimizzata | Miglioramento interno | Allocated SD (Orig, Fixed) |")
$lines.Add("|---|---|---|---|---|---|---|---|")

$complete = 0
$sumLogRatioOpt = 0.0
$nRatioOpt = 0

foreach ($key in $cases.Keys) {
    $s = $cases[$key]
    $sd = $s["SystemData_mean"]
    $og = $s["Original_mean"]
    $fx = $s["Fixed_mean"]

    $ratioOpt = FmtRatio $sd $fx
    if ($ratioOpt -ne "n/a") {
        $sumLogRatioOpt += [math]::Log([double]$ratioOpt)
        $nRatioOpt++
    }

    if ($ratioOpt -ne "n/a" -and (FmtRatio $sd $og) -ne "n/a") { $complete++ }

    $allocs = ("{0} ({1}, {2})" -f (FmtAlloc $s["SystemData_alloc"]), (FmtAlloc $s["Original_alloc"]), (FmtAlloc $s["Fixed_alloc"]))
    # NB: the formatting happens at the assignment level, not inside
    # Add(...): in the parentheses of a method call the commas separate
    # the method arguments, so -f would receive only the first argument.
    $row = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f `
        $key, `
        (FmtMs $sd), (FmtMs $og), (FmtMs $fx), `
        (FmtRatio $sd $og), $ratioOpt, (FmtRatio $og $fx), `
        $allocs
    $lines.Add($row)
}

$lines.Add("")
$totLine = "Casi totali: {0}; confronti completi (SD+Orig+Fixed): {1}." -f $cases.Count, $complete
$lines.Add($totLine)
if ($nRatioOpt -gt 0) {
    $geo = [math]::Exp($sumLogRatioOpt / $nRatioOpt)
    $geoLine = [string]::Format($invCulture, "Media geometrica del ratio 'Speedup ottimizzata' (SD/Fixed): **{0:N2}** su {1} casi.", $geo, $nRatioOpt)
    $lines.Add($geoLine)
}
$lines.Add("")

$out = $lines -join "`r`n"
Write-Host $out

$outDir = Join-Path $root "artifacts\bench\comparisons"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
$outFile = Join-Path $outDir ("confronto-{0}.md" -f $Tag)
Set-Content -Path $outFile -Value $out -Encoding utf8
Write-Host ("Report salvato in: {0}" -f $outFile)

exit 0
