# Operational plan — RCM.FastData (original version vs System.Data)

Date: 2026-10-09. Primary sources: the files in `original-snapshot/2026-10-09/`
(local root copies `FastData.cs` / `FastExpressionEngine.cs` existed at the start of the
project but are excluded from publication — the snapshot is the canonical reference).

## Verified environment

| Item | Value |
|---|---|
| CPU | VM guest: **12 logical processors** (host: AMD EPYC 9375F, 32 cores — the initial "32 core" entry was the host spec) |
| RAM | 24 GB |
| OS | Windows 11 Enterprise 10.0.22631 (Hyper-V), x64 |
| Background load | active user services (mysqld, Edge/WebView2, Office, Teams, Adobe): ~100% CPU in use; the BDN process is still served on ~1 core (validated with drift checks on invariant families, ±10-15% within 1σ) |
| .NET SDK | 9.0.308 (MSBuild 17.14.28) |
| .NET Framework | 4.8.1 (Release 533320) — target **net48**, installed runtime 4.8.1 |
| .NET 8 runtime | 8.0.22 (Microsoft.NETCore.App) |
| Visual Studio | 2022 Community 17.14.36717.8 |
| Reference assemblies v4.8 | present + package `Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3` |
| BenchmarkDotNet | **0.15.8** (lib: netstandard2.0, net6.0, net8.0 — supports both runtimes) |
| xunit | 2.9.3 + runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.14.1 |
| GC | Workstation (explicit in the benchmarks on both runtimes) |

Note: git was not reliable on this volume (UNC junction, "dubious ownership"): source
versioning was tracked with SHA-256 hashes in
`original-snapshot/2026-10-09/SHA256.txt`.

## Original snapshot (immutable)

- `original-snapshot/2026-10-09/FastData.cs` — SHA256 `F7D8E9509EA1750BDD9FA7CBB516FE422979B4D584E5F1FEF8ECA23A7ABB9EBB`
- `original-snapshot/2026-10-09/FastExpressionEngine.cs` — SHA256 `9B31418E841679EB201C4D7DCEBA3BBD4CFE2D37B89D78C872437FC1EB1C66D6`

Transformations applied to the comparison copy (project `RCM.FastData.Original`):
mechanical rename of the namespace `RCM.FastData` → `RCM.FastData.Original` and of the
corresponding `using ... = RCM.FastData.FastDataX` aliases, plus translation of the
comments to English (publication requirement). No other modification to the code:
behavior and structure stay identical to the snapshot, and the byte-identical reference
remains `original-snapshot/`.

## Structure

```
RCM.FastData.sln
src/RCM.FastData            net48;net8.0  — corrected/optimized version (same namespace RCM.FastData)
src/RCM.FastData.Original   net48;net8.0  — original snapshot (namespace RCM.FastData.Original)
shared/BenchData.cs         net48;net8.0  — dataset builder shared by tests and benchmarks
tests/RCM.FastData.Tests    net48;net8.0  — unit, differential, regression
bench/RCM.FastData.Benchmarks net48;net8.0 — BenchmarkDotNet 0.15.8
scripts/                    PowerShell: env, build, test, benchmark, comparison
docs/                       compatibility, methodology, final report
```

## Suspected defects identified at inspection (to be confirmed with tests)

1. **Compute: recursion/StackOverflow** — the fallback `new DataTable().Compute(...)` uses
   the alias `DataTable = FastDataTable`: a parenthesis-less expression (e.g. `COUNT`)
   recursively calls itself forever.
2. **Filter regex with `$` instead of `\[`** — `RxIsNull`/`RxAtomic`: the bracketed-column
   `[...]` branch does not match (in .NET a `$` mid-pattern is a literal character).
   Columns `[Con spazio]` in filters: `NotSupportedException` instead of being supported.
3. **Case-only value change ignored** — `FastDataRow.SetValueCore` uses
   `FastObjectComparer` (case-insensitive on strings) to decide whether the value changed:
   `Roma`→`ROMA` updates neither state nor indexes.
4. **Compute in double** — SUM/AVG/MIN/MAX computed in `double`: `decimal` precision loss,
   `long` > 2^53 wrong (System.Data preserves the column type).
5. **`Field<T>` with `Nullable<T>`** — `Convert.ChangeType` does not support nullable
   types: `Field<int?>` fails on non-null values.
6. **`ClearRows` does not reset RowState** — rows stay `Added`/`Unchanged` and cannot be
   re-added (System.Data leaves them `Detached`).
7. **`createConstraints` silently ignored** in `FastDataRelationCollection.Add`.
8. **`FastExpressionCompiler` dead** — never called; the real engine is the interpreted AST
   (`FastExpressionEngine`). Static cache `TableName|expr` is risky (ordinals of different
   schemas). Dead fields: `_compiledExpressions`, `_expressionOrdinals`, `_childAggPlans`,
   `_childAggByRelation`, `_exprSchemaDirty`, `RxAtomicIsNullFunc`.
9. **Select always full-scans** — the `_indexes` are not used by `SelectInternal`.
10. **Missing API (P1)**: `Select()` without a filter, `Rows.Find`/`Contains`, `Copy`,
    `ImportRow`/`LoadDataRow`, `Count`/indexer on views, `ToTable` with projection,
    `Remove` by index.

## Phases

- **A** (done): analysis, environment, snapshot, plan.
- **B** (done, 2026-10-09): solution building on both targets, test suite (149 tests:
  123 pass / 26 failures = the known P0s, identical on net48 and net8.0), initial matrix
  in `docs/MATRICE-INIZIALE.md`.
- **C** (done + E validation in progress): baseline benchmark, Standard profile
  (`--job medium --filter "*"`, out-of-process InProcessEmit, `[BENCH] RUNTIME=` marker
  to verify the real runtime). Run #1 (net8.0, **pre-E**) completed:
  `artifacts/bench/bench-net8.0-fasec/` + log `standard-net8.0-fasec.log` (EXIT=0,
  74.3 min, marker RUNTIME=System.Private.CoreLib). Run #2 (net8.0, **post-E**,
  pre-E-bis) completed: `artifacts/bench/bench-net8.0/` + log `standard-net8.0.log`
  (EXIT=0, 82.8 min, marker RUNTIME=System.Private.CoreLib). Run #3 (net48) started and
  **stopped after ~2 min**: the code changed with Phase E-bis (below), so its numbers
  would not have been comparable; the definitive net48 run is run #5. Per-target export
  in `artifacts/bench/bench-<target>/results/` (md + csv + full.json per class; the run
  uses `--artifacts=artifacts\bench\bench-<target>` so the two runtimes' reports are not
  mixed). Run #4 (net8.0, **final code**, `bench-net8.0-final/`) and run #5 (net48,
  **final code**, `bench-net48-final/`) in sequence.
- **D** (sources complete, verification in progress): fixes D1–D12 in
  `src/RCM.FastData/FastData.cs` (Compute without parentheses → NotSupportedException;
  aggregate types at System.Data parity; case-only change detected; ClearRows → Detached;
  Field<T> nullable and strict ICE; Delete Added → immediate removal; AcceptChanges
  already fixed; ParseLiteral NULL; real FK pre-validation for createConstraints) +
  implemented P1: `Rows.Find`/`Contains` (composite PK too), `Rows.RemoveAt`,
  `ImportRow`, `LoadDataRow`, `FastDataView.Count`/indexer. Removed dead
  fields/class (`FastExpressionCompiler`). **Completed** (2026-10-09): full suite
  **162/162** on both targets, Release (trx in `artifacts/testresults/matrix-d.net48.trx`
  and `matrix-d.net8.0.trx`); CrashProbe `fixed-compute-no-parens` exit 0 on both TFMs.
  Added `P1ApiTests.cs` (13 tests on the P1 API). Contracts verified with System.Data
  probes (net48 + net8.0, 2026-10-09, identical behavior on both runtimes):
  - **AVG**: returns the **column type** (int→`Int32`, long→`Int64` with **truncating**
    integer division, 11/3 → 3; decimal→`Decimal`; double→`Double`); NULL excluded from
    the count; empty/all-NULL sets → `DBNull`; non-numeric column → `DataException`.
  - **SUM**: int/long→`Int64`, decimal→`Decimal` (exact), double→`Double`; non-numeric
    column → `DataException` (the original computed in `double`: D2).
  - **LIKE (D5)**: **non-SQL semantics** — `%`/`*` wildcards only at the start and/or
    end of the pattern (mid-pattern → `EvaluateException` at `Select()`, even on an
    empty table); `_`/`?` **literal**; `[c]` single-element (any other `[...]` form
    invalid); case-insensitive culture-invariant. FastData re-implemented at exact
    parity, with pattern validation at parse.
  - **System.Data `Select` (performance)**: implicit per-column index on equalities
    (cold O(N) ~0.5 µs/row @100k on first access; warm O(1)+O(matches); ranges = fast
    scan ~40–50 ns/row without building the index; mutation invalidates the index;
    results always correct). Consequence: the Select-equality numbers of run #1 measure
    the **warm** path of System.Data.
  Corrected test contracts (documented in the report):
  - `Constraint_FigliaOrfana_Bloccata`: the parent row was missing (contract
    unsatisfiable under any coherent FK semantics);
  - `Select_Like`/`Select_IsNull` (differentials): from positional comparison to set
    comparison (System.Data sorts implicitly on the filtered column; declared difference
    in COMPATIBILITA.md).
- **E** (implemented, validation in progress with run #4/#5): the single optimization —
  `SelectInternal` uses the per-column index when the filter is a **single** `col = val`
  clause (excluded: multiple clauses, IS NULL/LIKE/inequalities, `ISNULL(col,x)=y`,
  `col = NULL`). Same comparer (FastObjectComparer) in Dictionary and comparison,
  insertion order preserved, Deleted rows filtered out.
  - **E-bis** (added after run #2): the result `List` must not be pre-allocated to
    `_rows.Count` (at N=100,000 = an 800 KB LOH array on **every** call, even with 1
    match). In BDN the accumulated LOH churn triggered costly GC2: `equality-int@100k`
    Fixed measured 455 µs/call (Allocated=782 KB/call, Gen2=10.25/iter) vs 6 µs/call
    with a fresh-heap probe — the index gain was masked. Fix: on the index path capacity
    = bucket size; on the full scan initial capacity 1024 (same defect, measured on
    range/compound @100k: 2,770 µs in BDN vs 28 µs in the probe). Suite 162/162 on both
    targets after both fixes.
  It will be kept only if the Standard benchmark (Select equality cases) shows a gain;
  otherwise it will be declared "not validated". Definitive validation: comparison of
  run #1 (`bench-net8.0-fasec`, pre-E) vs run #4 (`bench-net8.0-final`, final code) on
  the Select-equality cases (N = 1000/10000/100000) + check of no regressions on the
  Load/RowRead/Compute/PkLookup families (code unchanged).
- **F**: scripts (done: check-env, restore, build, run-tests, run-benchmark,
  compare-results), README, exports (md/csv/json), final report with environment,
  results, and repeat commands.

## Rules

- No benchmark reports unmeasured speed; families not implementable in the original are
  marked "not supported".
- Homogeneous comparisons: same runtime, schema, dataset (fixed seed), filter, output.
- The possibly-StackOverflow case (Compute without parentheses) runs in a **separate
  process**, never in the main test/benchmark process.
- Smoke measurements are not conclusive proof; the report only states what was actually
  executed.
