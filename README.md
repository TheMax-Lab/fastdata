# RCM.FastData

A fast in-memory data table for C# — a drop-in, `System.Data.DataTable`-friendly alternative,
verified against `System.Data` with differential tests and BenchmarkDotNet.

<p align="center">
  <img src="https://img.shields.io/badge/RCM.FastData-v1.0.0-blue?style=for-the-badge" alt="RCM.FastData v1.0.0">
  <a href="https://paypal.me/TheMaxLab">
    <img src="https://img.shields.io/badge/Support%20the%20Project-PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white" alt="Support the project with PayPal">
  </a>
<br/>
  <img alt="C#" src="https://img.shields.io/badge/C%23-.NET%204.8%20%7C%20.NET%208-7D81D8?logo=dotnet&logoColor=white">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue.svg"></a>
  <a href="https://github.com/TheMax-Lab/RCM.FastData/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/TheMax-Lab/RCM.FastData?style=flat"></a>
</p>

> If this project has made your life easier or saved you time, you can buy me a coffee to support ongoing development. Thank you sincerely!

---

## Overview

**RCM.FastData** is an in-memory data table library (`FastDataTable`, `FastDataRow`,
`FastDataColumn`, `FastDataSet`, `FastDataView`, relations, expression engine) with an API
modeled on `System.Data.DataTable`.

This repository is more than the library. It is the full **verification, correction and
optimization project** behind it:

- the **original implementation is preserved as an immutable snapshot**
  (`original-snapshot/2026-10-09/`, SHA-256 pinned) and is never modified;
- **12 confirmed defects (D1–D12)** in the original were reproduced with tests, fixed in
  `src/RCM.FastData`, and each fix is proven by unit + differential tests against
  `System.Data` behavior;
- the fixed/optimized version is benchmarked with **BenchmarkDotNet** against both
  `System.Data` and the immutable original.

Current status: **162/162 tests passing on both targets** (`net48`, `net8.0`), Release
configuration.

## Features

- `FastDataTable` / `FastDataRow` / `FastDataColumn` / `FastDataSet` / `FastDataView` with a
  `System.Data`-like API (internal `using` aliases make it drop-in friendly)
- `Compute(expr, filter)` aggregates: `SUM` / `AVG` / `MIN` / `MAX` / `COUNT` / `COUNT(*)`
  with `System.Data`-equivalent **typed precision** (the original computed everything in
  `double`, losing `decimal`/`long` precision — defect D2)
- `Select(filter)` with `=`, `<>`, `>`, `<`, `>=`, `<=`, `LIKE`, `IS [NOT] NULL`
  (System.Data `LIKE` semantics — which is *not* SQL semantics), plus a **per-column
  equality index** for single-clause `col = val` filters
- Primary key lookup: `Rows.Find(keys)` / `Rows.Contains(keys)` (composite keys supported)
- Relations with real foreign-key validation (`createConstraints` honored), child
  aggregation, `ImportRow` / `LoadDataRow` / `Rows.RemoveAt`
- Expression engine for computed columns and filters (`FastExpressionEngine`, AST-based)
- Multi-target: **.NET Framework 4.8** and **.NET 8.0**, same source
- 162 tests: 145 core (unit, P1 API, original known-defect) + 17 differential
  (same table built on `System.Data` and on FastData, results compared)

## Benchmark results

BenchmarkDotNet 0.15.8, out-of-process, Workstation GC, `Standard` profile
(10 warmup + 15 iterations, 2 invocations). Full 102-case tables:
[`docs/RAPPORTO-FINALE.md`](docs/RAPPORTO-FINALE.md) §7.

**net8.0, final run (102 cases, `EXIT=0`)** — representative numbers (`Mean`, µs):

| Case (N = 100,000) | System.Data | Original | Fixed | Speedup vs System.Data |
|---|---|---|---|---|
| Load (build table) | 87.35 ms | 35.08 ms | 40.82 ms | 2.14× |
| Select equality-int | 6.26 µs | 1915 µs | **0.774 µs** | 8.09× |
| Select equality-string | 421.6 µs | 2382 µs | **240.0 µs** | 1.76× |
| Select range | 44.91 ms | 2742 µs | **2622 µs** | 17.13× |
| Select compound | 50.70 ms | 2720 µs | **2562 µs** | 19.79× |
| Compute `COUNT(Id)` with filter | 42.55 ms | 2224 µs | **2764 µs** | 15.39× |
| `row["Name"]` read | 131.7 ns | 14.4 ns | **13.4 ns** | 9.85× |
| `Rows.Find(PK)` | 295.0 ns | n/a | **48.5 ns** | 6.08× |
| `Rows.Contains(PK)` | 273.6 ns | n/a | **37.0 ns** | 7.40× |

`n/a` = not implemented in that subject (PK lookup did not exist in the original);
it is a marker, never a measurement.

**Memory** (per call, `MemoryDiagnoser`):

| Case | System.Data | Original | Fixed |
|---|---|---|---|
| Cell read (`row["col"]`, `Field<T>`) | 0 B | 0 B | 0 B |
| `Rows.Find` / `Rows.Contains` | 152 / 128 B | n/a | **88 B** |
| Select equality-int (1 match of 100,000) | 3.27 KB | 782.35 KB | **1.11 KB** |
| Table memory (per 1,000 rows) | 604.45 KB | 462.42 KB | 462.24 KB |

Honest caveats, as documented in the final report:

- the fixed version is **slower than the original** on typed `SUM` at N ≥ 10,000 — the
  measured cost of the D2 precision fix (declared, not a regression);
- System.Data `Select` numbers measure its *warm* path (it builds an implicit per-column
  equality index on first access; the cold cost is not measured);
- the **net48 final run is partial** (Load, RowRead, Compute, PkLookup completed; the
  Select family was interrupted — see [`docs/RAPPORTO-FINALE.md`](docs/RAPPORTO-FINALE.md) §7.2/§12).

## Compatibility with System.Data

The corrected version is behavior-compatible with `System.Data.DataTable`/`DataView` for
everything tested — 162/162 tests, including 17 differential tests. Declared differences
(full matrix with `[PARITA]` / `[DIFF-VERIF]` tags in
[`docs/COMPATIBILITA.md`](docs/COMPATIBILITA.md)):

1. `COUNT(*)` is **supported** by FastData, not by `System.Data`.
2. `Select(filter)` preserves **insertion order**; `System.Data` applies an implicit sort
   on the filtered column (documented ADO.NET behavior).
3. `Compute` without parentheses throws a catchable `NotSupportedException`
   (the original crashed with `StackOverflowException` — defect D1).
4. `MIN`/`MAX` on strings compare `OrdinalIgnoreCase` vs the column's `IComparable`
   (identical ordering on ASCII, may differ with accents/case).
5. `LIKE` is `System.Data` semantics, **not SQL**: `%`/`*` wildcards only at the start
   and/or end of the pattern, `_`/`?` are literal, `[c]` is a single character class.
6. Conversions use the **current culture**, like `System.Data`.

## Requirements

- .NET SDK (9.0.308 tested) with MSBuild
- .NET 8 runtime (target `net8.0`)
- .NET Framework 4.8 (target `net48`); reference assemblies are restored automatically
  from NuGet (`Microsoft.NETFramework.ReferenceAssemblies.net48`)
- Windows PowerShell 5.1 (for the helper scripts)

## Quick start (PowerShell 5.1)

```powershell
# environment check (SDK, runtimes, reference assemblies, BDN version)
powershell -ExecutionPolicy Bypass -File scripts\check-env.ps1

# restore / build
powershell -ExecutionPolicy Bypass -File scripts\restore.ps1
powershell -ExecutionPolicy Bypass -File scripts\build.ps1                      # both targets
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Target net8.0       # single target

# tests (unit + differential + regression; TRX in artifacts\testresults)
powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1
powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1 -Target net8.0 -Filter "FullyQualifiedName~ComputeTests"

# benchmarks (profiles: Smoke=dry, Standard=medium, Full=long, Stress=verylong)
powershell -ExecutionPolicy Bypass -File scripts\run-benchmark.ps1 -Profile Smoke    -Target net8.0
powershell -ExecutionPolicy Bypass -File scripts\run-benchmark.ps1 -Profile Standard -Target net8.0
powershell -ExecutionPolicy Bypass -File scripts\run-benchmark.ps1 -Profile Standard -Target net48

# comparison (the three plan ratios: speedup vs original, speedup vs System.Data, internal improvement)
powershell -ExecutionPolicy Bypass -File scripts\compare-results.ps1 -Tag standard-net8.0
```

**Operational rule:** never run build/test/benchmark in parallel (CPU contention
contaminates measurements). One benchmark at a time.

## Repository structure

```
RCM.FastData.sln
├── src/RCM.FastData/            the corrected/optimized library (v1.0.0)
├── src/RCM.FastData.Original/   the original as a comparison project (v0.0.0-original)
├── original-snapshot/2026-10-09/  immutable byte-identical original + SHA-256
├── shared/BenchData.cs          shared test/benchmark dataset builder
├── tests/RCM.FastData.Tests/    162 xunit tests (Core + Differential)
├── bench/RCM.FastData.Benchmarks/  BenchmarkDotNet runner (out-of-process)
├── tools/RCM.FastData.CrashProbe/  separate process for StackOverflow-risk cases
├── scripts/                     PowerShell 5.1: env, build, test, benchmark, compare
└── docs/                        plan, initial matrix, compatibility, final report
```

## Documentation

| File | Contents |
|---|---|
| [`docs/PIANO.md`](docs/PIANO.md) | operational plan, phases A–F, defects D1–D12, rules |
| [`docs/MATRICE-INIZIALE.md`](docs/MATRICE-INIZIALE.md) | initial test matrix (149 tests: 123 ok / 26 failures = the P0 defects), verified `System.Data` ground truth |
| [`docs/COMPATIBILITA.md`](docs/COMPATIBILITA.md) | parity and declared differences, fixed version vs `System.Data` |
| [`docs/RAPPORTO-FINALE.md`](docs/RAPPORTO-FINALE.md) | final report: environment, measured results, repeat commands |

## Artifacts layout

| Path | Contents |
|---|---|
| `artifacts/bench/bench-<target>/results/` | BDN reports per benchmark class per target: `*-report.md`, `*-report.csv`, `*-report.full.json` (the two targets keep separate runs so ratios are never mixed; a run uses `--artifacts=artifacts\bench\bench-<target>`) |
| `artifacts/bench/<profile>-<target>.log` | full run log (ends with `EXIT=<code> DURATION=<min>min`) |
| `artifacts/bench/comparisons/confronto-<tag>.md` | markdown table of the three ratios per case |
| `artifacts/testresults/matrix-d.<target>.trx` | test report per target |
| `original-snapshot/2026-10-09/` | immutable original snapshot + SHA-256 |

## How to read the results

- Benchmarks report **only measurements actually executed**; a case not implemented in a
  subject (e.g. PK lookup in the original, which has no `Rows.Find`) is marked **n/a** —
  that is a marker, not a measurement.
- The `[BENCH] RUNTIME=...` marker in each log attests the runtime actually executing in
  the child process (net48 → `mscorlib`; net8.0 → `System.Private.CoreLib`).
- `StackOverflow`-risk cases (`Compute` without parentheses on the original) run only in
  a **separate process** (`tools/RCM.FastData.CrashProbe`), never in the test/benchmark
  process.

---

## ❤️ Support the Project

If RCM.FastData is useful to you, consider supporting the project.

This project is developed and maintained as an open-source initiative. Contributions help support:

- New benchmark scenarios and environments
- Improvements to the verification and differential-test infrastructure
- Better documentation and reports
- Compatibility testing
- Bug fixes
- New features for the library

### ☕ Support via PayPal

<p align="center">
  <a href="https://paypal.me/TheMaxLab">
    <img src="https://img.shields.io/badge/☕%20Support%20TheMax--Lab%20via%20PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white" alt="Support TheMax-Lab via PayPal">
  </a>
</p>

<p align="center">
  <strong>Every contribution is appreciated and helps keep the project growing.</strong>
</p>

Support is completely voluntary and does not affect the availability or functionality of the software.

You can also support the project by:

- ⭐ Giving the repository a Star
- 🐛 Reporting issues
- 💡 Suggesting new benchmarks or features
- 🔧 Contributing improvements
- 📖 Improving documentation
- 📢 Sharing the project with other .NET developers

---

## Contributing

Contributions are welcome via issues and pull requests. Please keep the operational rules:
the original snapshot stays immutable, comparisons stay homogeneous (same runtime, schema,
dataset, filter), and no benchmark reports an unmeasured number.

## Topics

`c#` · `dotnet` · `data-table` · `in-memory` · `database` · `benchmark` · `benchmarkdotnet` · `performance` · `xunit` · `fastdata`

## License

Licensed under the [MIT License](LICENSE).

<p align="center"><strong>Fast in-memory data tables — verified against System.Data.</strong></p>
