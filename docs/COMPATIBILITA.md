# Compatibility — RCM.FastData (fixed version) vs System.Data

Date: 2026-10-09. The original version (immutable snapshot) is NOT compatible by
definition: its P0 defects (D1–D12 in `docs/PIANO.md`) are documented in
`MATRICE-INIZIALE.md` and pinned by the tests in
`tests/.../OriginalKnownDefectTests.cs`. This document compares the **fixed version**
(`src/RCM.FastData`) with `System.Data.DataTable`/`DataView`.

Every entry is tagged:
- **[DIFF-VERIF]** = difference verified with differential tests (the `DifferentialTests`
  suite) or with a probe in a separate process (`RCM.FastData.CrashProbe`);
- **[PARITA]** = behavior parity verified by the tests.

## 1. Aggregate `Compute(expr, filter)`

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| `SUM` on an integer column (int/long/...) | `Int64` (no precision loss) | `long` (parity) | [PARITA] |
| `SUM` on a `double` column | `Double` | `double` (parity) | [PARITA] |
| `SUM` on a `decimal` column | exact `decimal` | exact `decimal` (the original computed in `double`: defect D2) | [PARITA] |
| `SUM` on a non-numeric column (string/bool/DateTime/object) | `DataException` ("Invalid usage of aggregate function Sum() and Type: …") | `System.Data.DataException` (parity; the original computed in `double`: defect D2) | [PARITA] |
| `AVG` on an integer column | the **column type**: int→`Int32`, long→`Int64`, with **truncating** integer division (11/3 → 3) | parity (the original returned `double`: defect D2) | [PARITA] |
| `AVG` on a `decimal` / `double` column | `Decimal` / `Double` | parity (defect D2) | [PARITA] |
| `AVG` on a non-numeric column | `DataException` ("Invalid usage of aggregate function Mean() and Type: …") | `System.Data.DataException` (parity) | [PARITA] |
| `MIN`/`MAX` | column type (raw value) | column type (raw value) | [PARITA] |
| `COUNT(column)` | `int`, counts non-NULL values | `int`, counts non-NULL values | [PARITA] |
| `COUNT(*)` | **not supported** (throws) | supported → row count (with the filter applied) | [DIFF-VERIF] |
| Empty set | `COUNT` → 0, others → `DBNull` | `COUNT` → 0, others → `DBNull` | [PARITA] |
| Parenthesis-less expression (e.g. `"COUNT"`) | throws (`ArgumentException`) | throws `NotSupportedException` with an explicit message (the original: **StackOverflowException** — defect D1, verified in a separate process) | [DIFF-VERIF] |

`MIN`/`MAX` comparison on strings: `System.Data` uses the column's `IComparable`
comparison; FastData uses `FastObjectComparer` (strings: `OrdinalIgnoreCase`). For
ASCII-only string sets the ordering coincides; for strings with accents or case-only
differences the ordering may differ — the differential tests use datasets where the
results coincide.

## 2. Filters `Select(filter)`

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| Operators `= <> > < >= <= LIKE IS [NOT] NULL` | supported | supported | [PARITA] |
| `IS NULL` / `IS NOT NULL` on `[Con spazio]` columns | supported | supported (the original threw: defect D5) | [PARITA] |
| `LIKE` — wildcards (**non-SQL semantics**) | `%`/`*` wildcard **only at the start and/or end** of the pattern (valid edge sequences: `'a%%'`, `'a*%'`); mid-pattern `%`/`*` → `System.Data.EvaluateException` | parity (probe net48/net8.0, 2026-10-09) | [PARITA] |
| `LIKE` — `_` and `?` | **literal** characters (not wildcards as in SQL) | parity (probe: `'R___'` → 0 rows on both) | [PARITA] |
| `LIKE` — square brackets | `[c]` (exactly one character) = that character (`'A[l]pha'` matches `Alpha`); empty, multi-character, `[^c]`, `[a-b]` → `EvaluateException` | parity | [PARITA] |
| `LIKE` — invalid pattern | `EvaluateException` at the `Select()` call (validation at parse, **even on an empty table**) | parity (FastData validates at parse, same timing) | [PARITA] |
| `LIKE` — sensitivity | case-insensitive, culture-invariant | parity | [PARITA] |
| **Result ordering** | System.Data applies an **implicit sort on the filtered column** (documented ADO.NET behavior) | FastData **preserves insertion order** of the rows | [DIFF-VERIF] |
| `Deleted` rows | excluded | excluded | [PARITA] |

> **Operational note (System.Data):** for a filter on a single column, the `DataView`
> returns the rows sorted by that column, not in `DataTable` order. Differential tests
> that compare filtered sets do so on sorted sets (same content, normalized order), not
> by positional correspondence.

> **Operational note (System.Data, performance):** a `Select` with a single-column
> equality (`col = val`) builds, on first access (cold: O(N) scan, ~0.5 µs/row at
> N = 100,000), an **implicit per-column index**, used by later equality filters on the
> same column (O(1) + O(matched rows)). Later range filters (`>`, `<`, …) do a fast full
> scan (~40–50 ns/row) **without** building the index: an equality after a column warmed
> only by ranges goes cold again. Every row mutation invalidates the index. Results are
> always correct (it is not a stale cache). Verified with probes on net48 and net8.0
> (2026-10-09), identical behavior. Consequence for the benchmarks: with tables built
> once in `[GlobalSetup]` and the same filter repeated per iteration, the System.Data
> `Select` equality numbers measure the **warm** path (the cold cost falls in the
> discarded warm-up iterations). The fixed FastData version (Phase E) uses an equivalent
> explicit index on single-clause `col = val` filters: the two implementations are the
> same class of optimization and the comparison remains valid, but the two cold costs
> are not measured.

## 3. Value writing and conversions

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| Assignment `row[col] = value` of a different type | `Convert.ChangeType` with the **current culture** (e.g. `1.5d` → `int` = `2` on it-IT/en-US) | `Convert.ChangeType` with the current culture (parity; the original always converted: defect D11) | [PARITA] |
| `null` value | stored as `DBNull` | stored as `DBNull` | [PARITA] |
| Case-only change (`Roma` → `ROMA`) | updates state/indexes and fires events | the fixed version updates it (the original **ignored** it: defect D6) | [PARITA] |
| `NaN` in `IComparable` comparisons | `NaN` < everything | same comparison (`FastObjectComparer`) | [PARITA] |

## 4. `Field<T>`

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| Exact type present | returns the value | returns the value | [PARITA] |
| Value `DBNull`, `T` not nullable | `InvalidCastException` | `InvalidCastException` | [PARITA] |
| Value `DBNull`, `T = int?` | `null` | `null` (the original failed: defect D10) | [PARITA] |
| Runtime type different from `T` (e.g. `string` in `Field<int>`) | `InvalidCastException` (**no conversion**) | `InvalidCastException` (strict parity: the fixed version does **not** convert, like System.Data) | [PARITA] |

## 5. Row lifecycle

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| `Delete` on an `Unchanged`/`Modified` row | state `Deleted`, removed at `AcceptChanges` | parity (state `Deleted`, `AcceptChanges` removes and rebuilds the indexes) | [PARITA] |
| `Delete` on an `Added` row | removed **immediately** from the collection (state `Detached`) | parity (the original left it in the list: defect D7) | [PARITA] |
| `Clear()` | rows removed; the `DataRow` objects end up `Detached` | parity (the original kept the old states: defect D8) | [PARITA] |
| `AcceptChanges` | `Deleted` → removed, `Added`/`Modified` → `Unchanged` | parity (D9 verified: `Deleted` rows removed, index rebuild, dirty flags on relations) | [PARITA] |

## 6. Constraints (relations)

| Item | System.Data | Fixed FastData | Status |
|---|---|---|---|
| `DataRelation` with `CreateConstraints = true`: child without an existing parent | blocked | blocked (validation **before** the state, no partial mutation; the original ignored the flag: defect D12) | [PARITA] |
| `CreateConstraints = true`: deleting a parent with children | blocked | blocked | [PARITA] |
| Bulk loading (`BeginLoadData`/`EndLoadData`, parity with `DataTable.Fill`) | FK constraints do not apply during the fill | parity (FK validation is suspended while the child table is loading) | [PARITA] |
| Duplicated relation name | throws | throws | [PARITA] |

## 7. API present in fixed FastData and absent/different in System.Data

- `Rows.Find(keys)` and `Rows.Contains(keys)`: **same contract** as `System.Data`
  (PK required, composite keys supported). The original did not have them (P1).
- `Rows.RemoveAt(index)`: immediate removal (like `DataRowCollection.RemoveAt`).
- `ImportRow(row)` / `LoadDataRow(values, state)`: parity of name and purpose with
  `DataTable.ImportRow` / `LoadDataRow`.
- `FastDataView.Count` and indexer `view[i]`: the System.Data `DataView` exposes them
  only via `IEnumerable`/`IEnumerable`; here they are explicit.
- `ToTable` with a column projection: in System.Data that is `DataTable.Clone` + copy;
  here the projection is explicit.

## 8. Declared compatibility limits

1. **`COUNT(*)`**: FastData supports it, System.Data does not. Anyone migrating FROM
   System.Data loses nothing; anyone comparing `Compute` output must use `COUNT(col)`
   for parity.
2. **`Select` order**: FastData preserves insertion order; System.Data sorts implicitly
   on the filtered column. Code depending on the System.Data order must declare an
   explicit sort (also recommended in System.Data).
3. **`Compute` without parentheses**: fixed FastData throws `NotSupportedException`
   (catchable) instead of the original's `StackOverflowException`.
4. **Culture**: local conversions and comparisons use the process's current culture,
   like System.Data; the tests cover it-IT, en-US and invariant.
5. **Original snapshot**: `RCM.FastData.Original` is **immutable** (SHA-256 in
   `original-snapshot/2026-10-09/SHA256.txt`) and keeps the defects D1–D12: use it only
   for historical comparison, never as a dependency. (For publication, its comments were
   translated to English; behavior and structure remain identical to the snapshot.)
6. Benchmark families not implementable in the original (e.g. PK lookup, which requires
   `Rows.Find`) are marked "not supported" in the reports and no numbers are compared:
   the "n/a" marker is not a measurement.
7. **`LIKE` semantics**: it is System.Data semantics, **not SQL** (details in §2):
   `%`/`*` wildcards only at the start and/or end of the pattern, `_`/`?` literal,
   single-element `[c]` classes, invalid patterns → `EvaluateException` at `Select()`.
   Code coming from a SQL context and expecting `_` as a wildcard will behave
   differently here **and** in System.Data.
