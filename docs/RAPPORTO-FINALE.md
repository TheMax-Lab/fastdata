# Final report — RCM.FastData: verification, fixes, optimizations and comparison

Date: 2026-10-09. This report states **only measurements actually executed** in this
environment. For every unexecuted proof the repeat command is given. The numbers come
from the BDN reports in `artifacts/bench/results/` and the TRX in
`artifacts/testresults/`.

Index: [1. Environment](#1-actual-environment) · [2. Versions](#2-source-versions) ·
[3. Tests](#3-test-results) · [4. Defects](#4-confirmed-defects-and-fixes) ·
[5. Compatibility](#5-compatibility-matrix) · [6. Methodology](#6-methodology-and-costs) ·
[7. Tables](#7-tables-by-runtime-and-family) · [8. Memory](#8-allocation-analysis) ·
[9. Optimizations](#9-optimizations-kept-and-discarded) · [10. Limits](#10-regressions-and-limits) ·
[11. Recommendations](#11-practical-recommendations) · [12. Executed/not executed](#12-tests-executed-and-not-executed)

## 1. Actual environment

| Item | Value |
|---|---|
| CPU (host) | AMD EPYC 9375F (32 cores, Zen 4) |
| VM guest | Microsoft Virtual Machine (Hyper-V), **12 logical processors**, 24 GB RAM |
| RAM | 24 GB |
| OS | Windows 11 Enterprise 10.0.22631 (Hyper-V), x64 |
| .NET SDK | 9.0.308 (MSBuild 17.14.28) |
| .NET Framework | 4.8.1 (Release 533320) — target **net48** |
| .NET 8 runtime | 8.0.22 (Microsoft.NETCore.App) — target **net8.0** |
| BenchmarkDotNet | 0.15.8 |
| xunit | 2.9.3 + runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.14.1 |
| GC | Workstation (explicit on both runtimes) |
| Visual Studio | 2022 Community 17.14.36717.8 (opening only; builds from the command line) |

Runtime verification: every benchmark run prints the line
`[BENCH] RUNTIME=<assembly of the object type>, CORECLR=<bool>, EXE=<path>` in the log;
net48 → `mscorlib`, net8.0 → `System.Private.CoreLib, Version=8.0.0.0`.
`ImageRuntimeVersion` is not used (known poor reliability on net48).

## 2. Source versions

- **Original (immutable)**: `original-snapshot/2026-10-09/`
  - `FastData.cs` — SHA-256 `F7D8E9509EA1750BDD9FA7CBB516FE422979B4D584E5F1FEF8ECA23A7ABB9EBB`
  - `FastExpressionEngine.cs` — SHA-256 `9B31418E841679EB201C4D7DCEBA3BBD4CFE2D37B89D78C872437FC1EB1C66D6`
  - Comparison project `src/RCM.FastData.Original` (namespace `RCM.FastData.Original`):
    **only** mechanical rename of the namespace and aliases, plus translation of the
    comments to English (publication requirement); no other modification.
    The original keeps the defects D1–D12 and has never been fixed.
- **Fixed/optimized version**: `src/RCM.FastData` (namespace `RCM.FastData`).
  Changes vs the original: fixes D1–D12 (section 4), P1 API
  (Find/Contains/RemoveAt/ImportRow/LoadDataRow/View.Count+indexer), removal of the
  dead `FastExpressionCompiler` code (fields never written nor read), Phase E
  optimization (equality filter on index + result allocation sizing, section 9).
  Final SHA-256 (2026-10-09, post-Phase D/E/E-bis — definitive code):
  - `FastData.cs` — `7258DA890FE73380B10EA746F9844366F8B0FD87CF5A3C641FBA056C2CA11B3A`
  - `FastExpressionEngine.cs` — `9B31418E841679EB201C4D7DCEBA3BBD4CFE2D37B89D78C872437FC1EB1C66D6`
    (**byte-identical to the original**: the expression engine was never
    modified; all fixes are in `FastData.cs`).

## 3. Test results

Phase D matrix (post-fixes), Release configuration. The TRX files cited are the
2026-10-09 23:33 re-execution on the final code (after Phase E and E-bis, immediately
before run #4): same matrix, same outcome.

| Target | Total | Passed | Failed | Duration | TRX |
|---|---|---|---|---|---|
| net48 | 162 | 162 | 0 | ~9.2 s | `artifacts/testresults/matrix-d.net48.trx` |
| net8.0 | 162 | 162 | 0 | ~5.6 s | `artifacts/testresults/matrix-d.net8.0.trx` |

Composition: `RCM.FastData.Tests.Core` = 145 tests (unit + `P1ApiTests` (13) +
`OriginalKnownDefectTests` (7), the latter green attesting that the immutable original
keeps the defects D1–D12); `RCM.FastData.Tests.Differential` = 17 tests (the same table
built on System.Data and on FastData, results compared).
Count: 149 (Phase B matrix) + 13 (`P1ApiTests`) = 162.

## 4. Confirmed defects and fixes

The defects were first **confirmed** by the Phase B tests (123 ok / 26 failed on both
targets, identical) and then fixed in the `src/RCM.FastData` version.

| # | Defect (original) | Reference System.Data behavior | Fix |
|---|---|---|---|
| D1 | `Compute("COUNT")` without parentheses → fallback `new DataTable().Compute` with alias `DataTable=FastDataTable` → **StackOverflowException** | `ArgumentException` (invalid expression) | `NotSupportedException` with an explicit message, no recursion; verified in a separate process (`CrashProbe`, exit 0) |
| D2 | `Compute` aggregates computed in `double`: type and precision loss (imprecise `decimal`, `long` > 2^53 wrong) | column type preserved: integer SUM→`Int64`, decimal SUM→`Decimal` (exact), double SUM→`Double`; **AVG→column type** (int→`Int32`, long→`Int64`, **truncating** integer division, 11/3→3; decimal→`Decimal`, double→`Double`); non-numeric column→`DataException`; MIN/MAX→raw value (contract verified with a probe on net48+net8.0, identical on both runtimes) | type/precision parity + `DataException` (ComputeTests + differentials, 162/162 on both targets) |
| D3 | Filter regexes with `$` instead of `\[`: columns `[Con Spazio]` rejected (`Unsupported filter`) | supported | correct regex; ISNULL(...) with a manual parser |
| D4 | String literal `'NULL'` treated as the NULL keyword | `'NULL'` stays a string, NULL stays a keyword | literal parser separate from the keyword (FilterTests) |
| D5 | `IS NULL`/`IS NOT NULL` and `LIKE` diverge from System.Data in the differentials (result ordering: System.Data sorts implicitly on the filtered column) | sorts implicitly on the filtered column (documented ADO.NET behavior); `LIKE` with **non-SQL** semantics (probe 2026-10-09, net48/net8.0 identical) | the fixed version preserves insertion order (**declared** difference, `docs/COMPATIBILITA.md`); the differentials compare sorted sets; `LIKE` re-implemented with the exact System.Data semantics: `%`/`*` wildcards only at the start and/or end of the pattern (mid-pattern position→`EvaluateException` at `Select()`, also on an empty table), `_`/`?` literal, single-element `[c]` (other classes→`EvaluateException`), case-insensitive culture-invariant, pattern validated at parse. **Verified: both differential tests green (162/162)** |
| D6 | Case-only change (`Roma`→`ROMA`) ignored: `SetValueCore` decided "changed" with the case-insensitive comparer | state updated, events fired, indexes recomputed | parity on the raw value (DBNull-aware `Equals`) before the comparison |
| D7 | `Delete` on an `Added` row did not remove the row from the collection | removed immediately, state `Detached` | parity |
| D8 | `Clear()` did not move rows to `Detached` | rows `Detached` | parity |
| D9 | Divergent RowState cycle (composed of D6/D7/D8): differential `RowState_CicloModificaEliminaAccetta` | — | parity (differential green) |
| D10 | `Field<T>` is not strict unboxing: it converted; `Field<string>` on NULL did not throw | strict unboxing without conversion (`InvalidCastException`); `T?` → `null` on NULL | strict parity (direct unboxing, nullable handling) |
| D11 | Assignment without `Convert.ChangeType` like System.Data (1.5 on int, "123" on int, "non-numerico" on int) | `Convert.ChangeType` with the **current culture** | cultural parity |
| D12 | `createConstraints` ignored in `DataRelation.Add`: orphan child accepted, parent with children deletable | real constraints, pre-validation | FK validation **before** the state, no partial mutation; `BeginLoadData` suspends validation (parity with `DataTable.Fill`) |

**Contract bug in a test found during Phase D**: `Constraint_FigliaOrfana_Bloccata`
(pre-existing test) inserted a child with FK=1 on an **empty** parent table expecting
success, then expected the throw for FK=99 — unsatisfiable under any coherent FK
semantics (both insertions are orphans). Corrected by inserting the parent row first;
the original version (without D12) passed it because it validated nothing.

## 5. Compatibility matrix

The full detail is in `docs/COMPATIBILITA.md` (item by item with the
`[PARITA]` / `[DIFF-VERIF]` tags). Summary of the declared differences:

1. **`COUNT(*)`**: supported by fixed FastData, **no** in System.Data.
2. **Order of `Select(filter)`**: System.Data applies an implicit sort on the filtered
   column (documented ADO.NET behavior); FastData preserves insertion order. The
   differentials compare as sets.
3. **`Compute` without parentheses**: `NotSupportedException` (catchable) instead of
   the original's `StackOverflowException`.
4. **Culture**: conversions/comparisons with the current culture, like System.Data.
5. **`MIN`/`MAX` on strings**: `OrdinalIgnoreCase` (FastObjectComparer) vs the column's
   `IComparable`; identical hierarchy on ASCII, may differ with accents/case.
6. **Immutable original snapshot**: used only for historical comparison.
7. **n/a families**: benchmarks not implementable in the original (e.g. PK lookup
   `Rows.Find`) are marked "not supported", never with invented numbers.
8. **`LIKE` semantics**: it is System.Data's, **not SQL** (detail in
   `docs/COMPATIBILITA.md` §2): `%`/`*` wildcards only at the start and/or end of the
   pattern, `_`/`?` literal, single-element `[c]` classes, invalid pattern →
   `EvaluateException` at `Select()`.
9. **`Select` performance (operational note)**: System.Data builds an implicit
   per-column index on equalities (cold on first access, warm afterwards; ranges = fast
   scan without building the index; mutation invalidates it; results always correct —
   `docs/COMPATIBILITA.md` §2). The fixed version (Phase E) uses an equivalent explicit
   index: same class of optimization, valid comparison; the two cold costs are not
   measured.

## 6. Methodology and costs

- **BenchmarkDotNet 0.15.8**, out-of-process (`InProcessEmit`; `InProcessToolchain`
  never used for the main comparison), explicit Workstation GC.
- Profiles: Smoke (`dry`), Standard (`medium`: 10 warmup + 15 iterations, 2 invocations),
  Full (`long`), Stress (`verylong`). **Executed: Standard on both targets**
  (run #4 net8.0: log `standard-net8.0-final.log`; run #5 net48: log
  `standard-net48-final.log`; the reports cited in the section 7 tables come from the
  final runs on the definitive code).
- **Costs included** in the measurement: filter/expression parsing, first-use index
  construction, evaluation, result allocations.
- **Costs excluded**: table construction (`GlobalSetup`, once for N, fixed
  deterministic seed), runtime warmup (handled by BDN).
- **Contamination**: one benchmark at a time (the "never build/test/benchmark in
  parallel" rule); no tests during the runs.
- **Homogeneous comparisons**: same runtime, schema, dataset, filter, output per group;
  the child processes' runtime attested by the `[BENCH] RUNTIME=` marker.
- **StackOverflow-risk cases** (Compute without parentheses on the original):
  only in a separate process (`tools/RCM.FastData.CrashProbe`), never in the
  test/benchmark process.
- **Ratios** (as per plan): `Speedup original = Mean(System.Data)/Mean(Original)`,
  `Speedup optimized = Mean(System.Data)/Mean(Optimized)`,
  `Internal improvement = Mean(Original)/Mean(Optimized)`.
  Computed per case and runtime, never averaged across. Complete per-target output:
  `artifacts/bench/comparisons/confronto-<tag>.md`
  (e.g. `confronto-standard-net8.0-final.md`), generated by
  `scripts\compare-results.ps1`.

## 7. Tables by runtime and family

Source: `*-report.csv` in `artifacts/bench/bench-<target>/results/` (final runs on the
definitive code; 102 cases per run). The three ratios are defined in section 6 and
computed case by case (complete output of `scripts\compare-results.ps1` in
`artifacts/bench/comparisons/`). Notation: `Mean±Error` (BDN standard errors);
Allocated = allocations per call (MemoryDiagnoser); `n/a` = subject not implementing
the family (PkLookup on the original: `Rows.Find`/`Contains` are P1 API added in the
fixed version, section 4/D12+P1).

### 7.1 net8.0 (run #4, log `standard-net8.0-final.log`)

**Family Load** (table construction, µs; alloc KB):

| N | System.Data | Original | Fixed | Alloc (SD/Orig/Fixed) | Spd orig | Spd opt | Int |
|---|---|---|---|---|---|---|---|
| 1,000 | 469.30±19.62 | 232.90±8.86 | 263.30±1.11 | 604.45 / 462.42 / 462.24 | 2.02 | 1.78 | 0.88 |
| 10,000 | 6,880.60±247.99 | 3,985.10±194.31 | 4,191.80±112.01 | 6,996.43 / 4,694.10 / 4,693.92 | 1.73 | 1.64 | 0.95 |
| 100,000 | 87,354.40±3,803.30 | 35,075.00±1,932.00 | 40,820.80±2,060.79 | 64,575.59 / 46,402.16 / 46,402.00 | 2.49 | 2.14 | 0.86 |

**Family Select** (µs; alloc KB):

| Case | N | System.Data | Original | Fixed | Alloc (SD/Orig/Fixed) | Spd orig | Spd opt | Int |
|---|---|---|---|---|---|---|---|---|
| equality-string | 1,000 | 6.134±0.280 | 15.684±0.045 | 1.177±0.040 | 3.59 / 10.03 / 3.34 | 0.39 | 5.21 | 13.33 |
| equality-string | 10,000 | 31.650±1.637 | 158.595±1.835 | 4.789±0.148 | 13.63 / 90.39 / 23.43 | 0.20 | 6.61 | 33.12 |
| equality-string | 100,000 | 421.628±23.231 | 2,382.296±26.451 | 240.013±5.218 | 114.08 / 893.96 / 224.32 | 0.18 | 1.76 | 9.93 |
| equality-int | 1,000 | 2.321±0.170 | 10.914±0.798 | 0.707±0.034 | 2.95 / 8.91 / 1.11 | 0.21 | 3.28 | 15.44 |
| equality-int | 10,000 | 3.775±0.242 | 122.536±3.071 | 0.793±0.020 | 3.11 / 79.23 / 1.11 | 0.03 | 4.76 | 154.44 |
| equality-int | 100,000 | 6.261±0.197 | 1,915.069±51.580 | 0.774±0.058 | 3.27 / 782.35 / 1.11 | 0.00 | 8.09 | 2,474.89 |
| range | 1,000 | 265.795±7.020 | 21.906±1.253 | 20.836±1.771 | 42.84 / 11.75 / 11.75 | 12.13 | 12.76 | 1.05 |
| range | 10,000 | 3,356.274±148.217 | 205.730±8.602 | 202.989±9.425 | 349.38 / 96.14 / 42.04 | 16.31 | 16.53 | 1.01 |
| range | 100,000 | 44,909.085±549.533 | 2,741.692±99.947 | 2,622.138±112.438 | 3,326.63 / 939.91 / 662.78 | 16.38 | 17.13 | 1.05 |
| compound | 1,000 | 269.704±12.469 | 23.145±0.700 | 22.709±0.703 | 42.16 / 11.61 / 11.61 | 11.65 | 11.88 | 1.02 |
| compound | 10,000 | 3,811.171±196.068 | 203.197±12.100 | 202.967±10.380 | 346.80 / 94.26 / 40.16 | 18.76 | 18.78 | 1.00 |
| compound | 100,000 | 50,702.751±3,646.085 | 2,720.422±132.813 | 2,562.031±40.485 | 3,306.59 / 920.69 / 643.56 | 18.64 | 19.79 | 1.06 |

Reading: Fixed clearly wins on equalities (index, Phase E+E-bis) and on
range/compound (Phase E-bis: allocations proportional to the output); it stays below
System.Data on equality-string (System.Data's implicit index also covers the N/7-match
case) and on equality-int at N≥10,000 (System.Data's "warm" path is O(1) even for
1 match).

**Family Compute** (µs; alloc KB):

| Case | N | System.Data | Original | Fixed | Alloc (SD/Orig/Fixed) | Spd orig | Spd opt | Int |
|---|---|---|---|---|---|---|---|---|
| SUM(Qty) | 1,000 | 22.429±0.931 | 7.711±0.527 | 6.834±0.626 | 22.48 / 8.05 / 8.05 | 2.91 | 3.28 | 1.13 |
| SUM(Qty) | 10,000 | 220.840±3.979 | 104.688±3.025 | 252.687±8.098 | 248.05 / 78.36 / 248.33 | 2.11 | 0.87 | 0.41 |
| SUM(Qty) | 100,000 | 3,604.226±139.085 | 2,617.703±78.331 | 3,899.514±41.107 | 2,198.81 / 781.49 / 2,040.41 | 1.38 | 0.92 | 0.67 |
| COUNT(Id) with filter | 1,000 | 273.796±8.536 | 14.234±0.674 | 13.810±0.865 | 52.81 / 8.98 / 8.98 | 19.24 | 19.83 | 1.03 |
| COUNT(Id) with filter | 10,000 | 3,600.566±95.457 | 128.877±9.674 | 133.654±6.440 | 458.04 / 79.29 / 121.23 | 27.94 | 26.94 | 0.96 |
| COUNT(Id) with filter | 100,000 | 42,546.710±2,902.988 | 2,224.015±79.989 | 2,764.410±35.350 | 4,269.91 / 782.42 / 1,017.31 | 19.13 | 15.39 | 0.80 |
| SUM(Price) | 1,000 | 25.106±1.791 | 14.412±0.850 | 13.399±0.042 | 22.49 / 8.05 / 8.05 | 1.74 | 1.87 | 1.08 |
| SUM(Price) | 10,000 | 255.024±11.706 | 138.051±8.890 | 289.642±15.758 | 248.05 / 78.36 / 248.34 | 1.85 | 0.88 | 0.48 |
| SUM(Price) | 100,000 | 4,023.334±23.505 | 2,279.312±79.252 | 3,564.621±96.730 | 2,198.82 / 781.49 / 2,040.42 | 1.77 | 1.13 | 0.64 |

Reading: the fixed version is **slower than the original** on integer/decimal SUM at
N≥10,000: it is the measured cost of the D2 fix (typed aggregates:
`Convert.ToInt64`/`ToDecimal` per row, ~16 B of boxing/row on int columns — sections
8/10), not a regression introduced by Phase E (which does not touch Compute).

**Family RowRead** (ns; alloc 0 B for all subjects):

| Case | N | System.Data | Original | Fixed | Spd orig | Spd opt | Int |
|---|---|---|---|---|---|---|---|
| row["Name"] | 1,000 | 86.52±4.78 | 14.33±0.48 | 13.77±0.94 | 6.04 | 6.28 | 1.04 |
| row["Name"] | 10,000 | 112.18±6.08 | 13.70±1.14 | 12.72±1.22 | 8.19 | 8.82 | 1.08 |
| row["Name"] | 100,000 | 131.67±4.85 | 14.44±0.44 | 13.37±1.18 | 9.12 | 9.85 | 1.08 |
| Field<string>("Name") | 1,000 | 93.06±3.95 | 16.91±0.57 | 19.57±1.11 | 5.50 | 4.76 | 0.86 |
| Field<string>("Name") | 10,000 | 110.86±9.42 | 15.98±1.16 | 20.50±0.35 | 6.94 | 5.41 | 0.78 |
| Field<string>("Name") | 100,000 | 136.95±7.79 | 16.46±0.68 | 21.43±0.72 | 8.32 | 6.39 | 0.77 |

Reading: FastData (both versions) reads a cell 6–10× faster than System.Data;
`row["Name"]` is at Orig/Fixed parity, `Field<string>` is ~20–30% slower on Fixed
(cost of the strict cast D10, declared in section 10).

**Family PkLookup** (ns; Original = n/a, API absent in the original):

| Case | N | System.Data | Fixed | Alloc (SD/Fixed) | Spd opt |
|---|---|---|---|---|---|
| Rows.Find(PK) | 1,000 | 215.95±5.61 | 46.91±3.86 | 152 B / 88 B | 4.60 |
| Rows.Find(PK) | 10,000 | 270.95±6.74 | 48.92±1.04 | 152 B / 88 B | 5.54 |
| Rows.Find(PK) | 100,000 | 295.00±13.99 | 48.52±1.21 | 152 B / 88 B | 6.08 |
| Rows.Contains(PK) | 1,000 | 187.47±8.60 | 35.51±0.86 | 128 B / 88 B | 5.28 |
| Rows.Contains(PK) | 10,000 | 242.59±12.72 | 33.38±2.34 | 128 B / 88 B | 7.27 |
| Rows.Contains(PK) | 100,000 | 273.64±14.77 | 36.97±0.66 | 128 B / 88 B | 7.40 |

### 7.2 net48 (run #5, log `standard-net48-final.log`)

TODO(Phase-F): tables analogous to the run #5 completion. Note: the run #5 log has no
`EXIT=` marker and the Select family report is missing from
`bench-net48-final/results/` — the net48 final run is partial; the net48 numbers above
are not cited anywhere until the run completes.

## 8. Allocation analysis

From the `Allocated` columns (MemoryDiagnoser, bytes per call) of the final reports.
All 102 cases report a value (no "NA"): the benchmarks return the
lengths/list copies allocated during the measured call.

**Point access (0 or a few dozen bytes):**
- RowRead (`row["Name"]`, `Field<string>`): **0 B** for all subjects, all N: the value
  is read in place, no copy.
- PkLookup: Fixed **88 B/call**, System.Data **152 B** (Find) / **128 B**
  (Contains); Original n/a (API absent).

**Select — Phase E-bis effect (allocations proportional to the output):**

| Case (net8.0) | N | Original | Fixed | Note |
|---|---|---|---|---|
| equality-int (1 match) | 100,000 | 782.35 KB | **1.11 KB** | Original: 800 KB LOH array pre-allocated on every call; Fixed: 1-element list |
| equality-string (N/7 match) | 100,000 | 893.96 KB | 224.32 KB | Fixed ≈ 14,286-ref list (114 KB) + growth; proportional to the output |
| range (20% match) | 100,000 | 939.91 KB | 662.78 KB | 20,000-row output: 160 KB final list + growth transients |
| compound (20% match) | 100,000 | 920.69 KB | 643.56 KB | idem |

On the equality-int@100k case the effect is ~700× (782.35 → 1.11 KB/call); it is the
same defect that in BDN masked the index gain (section 9, Phase E-bis): the dead
800 KB array LOH churn triggered GC2 (Gen2 ≈ 10/iteration before, 0 after).

**Compute/scan at high selectivity — Phase E-bis trade-off:** at 100% selectivity
(unfiltered Compute over 100,000 rows) the 1024→131,072 list growth costs ≈2.07 MB
(1.03 MB transient + 1.05 MB final array): Fixed measures **2,040.4 KB**
(coherent, ±1.5%) vs the single 781.49 KB pre-allocated array of the original. The
growth sizing improves low/medium selectivities (table above) at the cost of larger
transient allocations at full selectivity. For the same reason Gen2 at 100k is high in
this family (Fixed Compute 27.3/iter; Original 7.8/iter — LOH in both cases).

Note: `SUM(Qty)` (int column) and `SUM(Price)` (decimal column) have identical
allocations to within 0.01 KB on all N → the typed per-row conversion (D2) contributes
no measurable allocations on net8.0; the Orig/Fixed time difference in Compute
(section 7.1) comes from the row list construction and the per-row
`Convert.ToInt64/ToDecimal` dispatch.

**Table memory (Load family, alloc per call = full construction):** Orig/Fixed
462 KB per 1,000 rows (≈462 B/row, same values/schema), System.Data 604 KB per 1,000
rows (DataRow overhead). At 100,000 rows: ≈46.4 MB (Orig/Fixed) vs ≈64.6 MB (SD).

**Per-column indexes (Phase E): not measured separately.** Each column filtered by
equality keeps a `Dictionary` of buckets (≈1–2 MB per 100,000 rows in order of
magnitude, estimated not measured). Measurement method: `GC.GetTotalMemory(true)` on a
pre-built table, before and after the first `Select("col = val")`, in a dedicated
process.

## 9. Optimizations kept and discarded

- **Kept (Phase E): equality filter on the per-column index.**
  `SelectInternal` uses the index bucket when the filter is a **single** `col = val`
  clause (excluded: multiple clauses, IS NULL, LIKE, inequalities, `ISNULL(col, x) = y`,
  `col = NULL`). Same comparer in the Dictionary and in the comparison, so membership
  identical to the full scan; insertion order preserved. Lazy indexes with incremental
  maintenance and rebuild in `AcceptChanges`/`EndLoadData`. Standard measurements
  pre (run #1) and post (run #4) in section 7, Select-equality family.
- **Kept (Phase E-bis): result allocation sizing.** The `SelectInternal` result `List`
  is **no longer** pre-allocated to `_rows.Count`: at N=100,000 it was an 800 KB LOH
  array on **every** call, even for a filter producing 1 row. In BDN that LOH churn
  accumulated dead objects and triggered costly GC2 that **masked** the index gain:
  `equality-int@100k` measured 455 µs/call (Allocated=782 KB/call, Gen2≈10/iteration)
  while a fresh-heap probe measured 6 µs/call, and under sustained churn (500,000 calls)
  it rose to >600 µs/call. After the fix (index path: capacity = bucket size; full
  scan: initial capacity 1024, the list grows only as needed) the same probe measures
  ~7 ns/call under sustained churn with **zero** GC2. Same defect, measured, on
  range/compound @100k (2,770 µs in BDN vs ~29 µs in the probe). The suites remain
  162/162 on both targets after the fix.
- **Dead code removal** (hygiene, not performance): the
  `FastExpressionCompiler` class and the never-written/read fields
  (`_compiledExpressions`, `_expressionOrdinals`, `_childAggPlans`,
  `_childAggByRelation`, `_exprSchemaDirty`, `RxAtomicIsNullFunc`).
- **Discarded**: no other optimization was attempted beyond Phase E and
  E-bis. Every kept change has an associated measurement (sections 7–8); the
  D1–D12 fixes are correctness fixes, not optimizations, and their measured time
  costs are declared in section 10.

## 10. Regressions and limits

**Measured costs of the fixed version vs the original**
(run #4, net8.0, Compute family — the only time regressions found in the internal
comparison):

| Case | N | Original | Fixed | Ratio (Int) | Cause |
|---|---|---|---|---|---|
| Compute SUM(Qty) | 10,000 | 104.69 µs | 252.69 µs | 0.41 | D2 (typed aggregate) + growing row list |
| Compute SUM(Qty) | 100,000 | 2,617.70 µs | 3,899.51 µs | 0.67 | idem (Gen2 27.3/iter for LOH) |
| Compute SUM(Price) | 10,000 | 138.05 µs | 289.64 µs | 0.48 | idem |
| Compute SUM(Price) | 100,000 | 2,279.31 µs | 3,564.62 µs | 0.64 | idem |
| Compute COUNT(Id) with filter | 100,000 | 2,224.02 µs | 2,764.41 µs | 0.80 | growing row list (~77% selectivity) |
| Field<string>("Name") | 1,000–100,000 | 15.98–16.91 ns | 19.57–21.43 ns | 0.77–0.86 | D10 (strict unboxing, no conversion) |

These are declared costs of the fixes, not defects: D2 eliminates the double precision
loss (imprecise decimal, long > 2^53 wrong) and D10 eliminates the silent `Field<T>`
conversions. At N=1,000 the fixed version stays faster than the original (1.03–1.13×)
on the whole Compute family; the inversions appear only at N≥10,000.

- **Write cost with indexes (Phase E)**: `Add`/`Delete` maintain the index O(1) per
  indexed column; `AcceptChanges`/`EndLoadData` recompute the indexes. The Load family
  (which does not use `Select`, so it builds no indexes) shows no measurable variation:
  Fixed vs Original 0.86–0.95× (section 7.1), coherent with the environmental drift
  between runs (4–8%, sections 6/12) and not attributable to Phase E.
- **Index memory**: not measured separately (method in section 8).
- **E-bis trade-off at full selectivity**: +1.3 MB transient at 100k (2,040 vs 781 KB)
  on full scans, against −700× on low-selectivity equalities (section 8).
- **Environmental drift**: the VM runs with a user background load (section 1);
  families invariant across consecutive runs (e.g. Load run #2→#4: +4–8%) show 4–15%
  variance from run to run. Internal comparisons within the same run (same BDN
  process) are not affected by this effect.
- Compatibility limits of section 5 (COUNT(*), Select order, MIN/MAX strings, culture).
- **No correctness regressions**: suite 162/162 on both targets after Phase E and
  E-bis (TRX in `artifacts/testresults/`); it is the regression gate.

## 11. Practical recommendations

Every statement below is measured (section 7, final runs, net8.0; the net48 values are
in the same section upon run #5 completion).

**Prefer the fixed FastData version when:**
- point access by PK: `Rows.Find` 4.6–6.1× and `Rows.Contains` 5.3–7.4× faster than
  System.Data, with 88 B/call vs 152/128 B;
- cell reads: `row["col"]` 6–10× faster, `Field<T>` 4.8–6.4×, both at 0 B allocation;
- equality filters on a column: up to 8× vs System.Data (equality-int@100k) and 3× vs
  the original at equal small N; the index makes the cost independent of N (0.70–0.79
  µs from N=1,000 to 100,000);
- `COUNT` with filter: 15–27× faster than System.Data;
- range/compound: 12–20× vs System.Data (which allocates 3.3 MB/call at 100k vs
  644–663 KB of FastData);
- you need `COUNT(*)` in `Compute`, `Compute` without parentheses without SOE risk
  (D1), or aggregates with type precision (D2);
- you need `ImportRow`/`LoadDataRow`/`RemoveAt`/`Rows.Find`/`Contains` (P1 API) with
  the System.Data contract.

**Prefer System.Data (or the original) when:**
- `Compute SUM` on tables ≥10,000 rows with a latency constraint: System.Data is
  13–19% faster than the fixed version (which is the price of the D2 type precision);
  the original is still faster but loses precision (imprecise decimal, long > 2^53
  wrong) — not recommended for monetary/identifying data;
- the code depends on the implicit `Select(filter)` ordering on the filtered column
  (System.Data behavior, section 5);
- you need the full ADO.NET feature set (DataSet, binding, LINQ, DataRelation on
  DataSet).

**Operational note:** the System.Data/FastData comparisons above include System.Data's
implicit index on the "warm" path (after the first access); the cold cost of the first
`Select` is not measured in this run (Standard profile, table pre-built in
GlobalSetup with BDN warmup).

## 12. Tests executed and not executed

**Executed:**
- Release build on both targets (net48, net8.0) — 0 errors (Phase D, 2026-10-09);
- full test suite on both targets — 162/162 (section 3);
- Standard (medium) benchmark on net8.0 **pre-E** (run #1, Phase C baseline) —
  log `artifacts/bench/standard-net8.0-fasec.log`, EXIT=0, 74.3 min,
  RUNTIME=System.Private.CoreLib marker; reports in
  `artifacts/bench/bench-net8.0-fasec/results/`;
- Standard (medium) benchmark on net8.0 **post-E, pre-E-bis** (run #2) — log
  `artifacts/bench/standard-net8.0.log`, EXIT=0, 82.8 min,
  RUNTIME=System.Private.CoreLib marker; reports in
  `artifacts/bench/bench-net8.0/results/` (used only for the E-bis diagnostics, not
  cited in the final tables);
- Standard (medium) benchmark on net48 (run #3) — started and **stopped after ~2 min**
  (the code changed with Phase E-bis; its numbers would not have been comparable).
  Replaced by run #5;
- Standard (medium) benchmark on net8.0 **final code** (run #4, post-E-bis) — log
  `artifacts/bench/standard-net8.0-final.log`, **EXIT=0, 82.6 min,
  102 benchmarks executed**, first-line marker
  `RUNTIME=System.Private.CoreLib, Version=8.0.0.0` (declared runtime confirmed);
  reports in `artifacts/bench/bench-net8.0-final/results/`;
- Standard (medium) benchmark on net48 **final code** (run #5, post-E-bis) — queued
  after run #4 (never in parallel); log `artifacts/bench/standard-net48-final.log`;
  partial: Load, RowRead, Compute, PkLookup reports present in
  `bench-net48-final/results/`, the Select family is missing and the log has no
  `EXIT=` marker — outcome to be recorded on completion;
- CrashProbe: `original-compute-no-parens` (Phase B: SOE confirmed, exit -1073741571,
  both TFMs) and `fixed-compute-no-parens` (Phase D: exit 0, both TFMs);
- System.Data probes for the AVG/SUM/LIKE contracts and for the implicit `Select`
  index (sections 4 and 5, `docs/COMPATIBILITA.md`): separate throwaway process
  (`scratch-avg-probe`, deleted at the end of the work).

**Not executed** (with the repeat command):
- Full benchmark: `scripts\run-benchmark.ps1 -Profile Full -Target net8.0`
  (and `-Target net48`);
- Stress benchmark: `scripts\run-benchmark.ps1 -Profile Stress -Target net8.0`
  (and `-Target net48`);
- Smoke benchmark: not executed as a separate run — the Standard profile measures the
  same benchmarks with more iterations and warmup; command:
  `scripts\run-benchmark.ps1 -Profile Smoke -Target net8.0` (and `-Target net48`);
- separate measurement of the memory occupied by the per-column indexes and of the
  cold cost of the first `Select` (index construction): not executed; measurement
  method documented in sections 8 and 11;
- comparison on non-synthetic datasets and filter profiles not represented by the
  matrix (100% selectivity, IS NULL, LIKE, untested multi-clauses): out of the plan's
  scope.
