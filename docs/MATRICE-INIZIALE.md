# Initial compatibility matrix — Phase B

Date: 2026-10-09. Status: **Phase B completed** (solution builds, correctness suite in place,
incompatibility matrix measured on both targets).

## Build and test results

| Target | Build | Total tests | Passed | Failed | Duration |
|---|---|---|---|---|---|
| net48 (.NET Framework 4.8.9339.0) | 0 errors | 149 | 123 | 26 | ~10 s |
| net8.0 (.NET 8.0.22) | 0 errors | 149 | 123 | 26 | ~8.6 s |

The 26 failures are **exactly** identical between the two targets (verified by comparing the
lists). They are all known P0 defects of the library that the suite pins as expected
`System.Data` behavior: they were fixed in Phase D. No failure is a false-positive test.

Logs: `artifacts/logs/matrix-b-final2.net48.log`, `artifacts/logs/matrix-b-final2.net8.0.log`.

## Suite composition (149 tests)

- **Core** (unit tests of the `RCM.FastData` library): schema/columns, rows and RowState,
  types and `Field<T>`, filters, Compute, views, relations and child aggregation,
  merge/clone/copy, expression engine, original known defects
  (`OriginalKnownDefectTests`, must PASS while the original stays defective).
- **Differential** (`RCM.FastData.Tests.Differential`): the same table built on both
  System.Data and FastData, results compared (Select, Compute, Field, RowState,
  cultures it-IT/en-US/invariant).
- **CrashProbe** (separate process): the two StackOverflowException-risk scenarios never
  run in the test host.

## The 26 failures by P0 defect (all fixed in Phase D)

| # | Defect | Failing tests |
|---|---|---|
| D1 | **Compute without parentheses → StackOverflow** (fallback `new DataTable().Compute` with alias `DataTable=FastDataTable`) | `ComputeTests.EspressioneSenzaParentesi_LanciaESeNonCrolla_InProcessoSeparato` (probe: SOE, exit -1073741571, on both TFMs) |
| D2 | **Compute aggregates in double** (type and precision loss; System.Data preserves the column type) | `ComputeTests.SumInt_RitornaLong_ComeSystemData`, `SumLongOltreDueAlQuadratoCinquantatresimo`, `SumDecimal_PrecisioneEsatta`, `MinMax`, `MinMaxStringhe`, `ComputeSullExpressionColumn`; `DifferentialTests.Compute_TipiEValori`, `Compute_DecimalELong_TipiEValori`, `Compute_Filtro` |
| D3 | **Filter regex with `$` instead of `\[`** (bracketed columns not supported) | `FilterTests.ColonneFraParentesiQuadre_NeiFiltri` ("Unsupported filter: [Con Spazio] = 1") |
| D4 | **String literal `'NULL'` treated as the NULL keyword** | `FilterTests.NULLLetterale_VersoStringaNull` |
| D5 | **IS NULL / IS NOT NULL and LIKE diverge from System.Data** | `DifferentialTests.Select_IsNull`, `Select_Like` |
| D6 | **Case-only value change ignored** (case-insensitive comparer in `SetValueCore`) | `RowStateTests.AssignCasoDiverso_CambiaStato_EAggiornaEspressioni` |
| D7 | **Delete on an Added row does not remove the row** (System.Data removes it immediately, state Detached) | `RowStateTests.DeleteSuRigaAdded_RimuoveImmediatamente_ComeSystemData` |
| D8 | **ClearRows does not move rows to Detached** | `RowStateTests.Clear_RimuoveTutteEStatoDetached_ComeSystemData` |
| D9 | **Divergent RowState cycle** (composed of D6/D7/D8) | `DifferentialTests.RowState_CicloModificaEliminaAccetta` |
| D10 | **`Field<T>` is not strict unboxing like System.Data** (it converts; `Field<string>` on NULL does not throw) | `TypesAndFieldTests.FieldDouble_DaInt_StrictComeSystemData`, `FieldString_DaInt_StrictComeSystemData`, `FieldNullable_SuDBNull`; `DifferentialTests.Field_TipoEsatto_StrictComeSystemData` |
| D11 | **Assignment without `Convert.ChangeType` like System.Data** (1.5 on int, "123" on int, "non-numerico" on int) | `TypesAndFieldTests.AssegnazioneRispettaTipoColonna`, `ConversioneSullAssegnazione_ComeSystemData` |
| D12 | **`createConstraints` ignored** (orphan child accepted, parent with children deletable) | `RelationAndChildAggTests.Constraint_FigliaOrfana_Bloccata`, `Constraint_CancellaGenitoreConFigli_Bloccata` |

Counterpart: `OriginalKnownDefectTests` (7 tests, all PASS on both targets) documents the
same defects on the immutable original and stays green as long as the original is not
touched.

## Verified behaviors (System.Data ground truth, measured in this environment)

- `SUM` on an int32 column → **Int64**; `SUM(decimal)` → **decimal** (exact, 0.1+0.2+0.3=0.6);
  `SUM` on a non-numeric column → **DataException** (Phase D retro-correction, probe
  2026-10-09); `AVG` → **column type** with truncating integer division for integers
  (11/3 → 3; int→Int32, long→Int64, decimal→decimal, double→double), non-numeric →
  DataException (Phase D retro-correction, probe on net48+net8.0); `MIN`/`MAX` → column
  type; `COUNT` → int. Empty table: COUNT=0, other aggregates = DBNull.
- **System.Data `LIKE`: non-SQL semantics** (Phase D retro-correction, probe 2026-10-09,
  net48/net8.0 identical): `%`/`*` wildcards only at the start and/or end of the pattern
  (mid-pattern position → `EvaluateException` at `Select()`, even on an empty table),
  `_`/`?` literal, `[c]` single-element, any other `[...]` class → `EvaluateException`,
  case-insensitive culture-invariant.
- System.Data `Compute` does **not** support `COUNT(*)` (SyntaxErrorException): excluded
  from the differential tests (rule: no unverified assumption).
- System.Data `Field<T>`: **strict unboxing**, no conversion
  (`Field<string>` on an int column → InvalidCastException; `Field<int?>` on NULL → null;
  `Field<string>` on NULL → InvalidCastException).
- System.Data assignment: `Convert.ChangeType` with the current culture (1.5d on int → 2;
  "123" on int → 123; "non-numerico" → exception).
- **Special doubles in filters** (verified on net48 AND net8.0, with a dedicated probe):
  the comparison uses `IComparable`; `double.NaN.CompareTo(x) == -1` for every non-NaN x
  on both runtimes, so **NaN is "smaller than everything"** and +Inf "greater than
  everything". System.Data behaves **identically** (e.g. `X < 100` includes the NaN row
  on both systems). The library was already at parity here: the tests pin it.
- **Documented partiality note**: System.Data applies an **implicit sort on the filtered
  column** (DataView side effect) — `Select("X > 0")` does not return table row order.
  FastData preserves row order. The differentials therefore compare row sets (sorted),
  and the difference is documented here.
- Longs beyond 2^53 in filters: preserved (no loss through double) — already at parity.
- **Refuted**: the suspected P0 "`Field<T>` with `Nullable<T>` broken" **does not
  exist**: `Convert.ChangeType(value, typeof(T))` handles Nullable types on both
  .NET Framework 4.8 and .NET 8. Documented in `OriginalKnownDefectTests` (comment) —
  no defect test for it.

## CrashProbe infrastructure (separate processes for StackOverflow)

- `tools/RCM.FastData.CrashProbe` (net48;net8.0): scenarios `original-compute-soe`
  (expected: crash) and `fixed-compute-no-parens` (expected: NotSupportedException, exit 0
  — fails until the D1 fix is applied).
- Full stdout/stderr dump of the probe to `artifacts/logs/probe-<scenario>.*.txt`;
  runtime marker (`RUNTIME=<FullName of System.Object> EXE=<path>`) on stdout.
- Streams are read in parallel (`Task.Run`): a sequential `ReadToEnd` would deadlock on
  the ~200 KB SOE dump that saturates the pipe buffer.
- **Bug found and fixed in `CrashProbeRunner`**: the TFM selection used
  `ImageRuntimeVersion.StartsWith("4.")`, but `ImageRuntimeVersion` returns
  `"v4.0.30319"` **on both runtimes** → the condition was always false and the "net48"
  tests ran the probe on .NET 8. Fixed with the `System.Object` assembly
  (mscorlib vs System.Private.CoreLib), verified with the markers in both final
  matrices (`matrix-b-final2.*.log`).

## Environment notes relevant to the following phases

- `ImageRuntimeVersion` must never be used as a runtime discriminator (see above);
  reliable: `Assembly.FullName` or `RuntimeInformation.FrameworkDescription`.
- `AppContext.BaseDirectory` has no trailing slash on net48, it does on net8.0 (relative
  paths in the runner handle both forms via `Path.GetFullPath`).
- SOE dump on .NET Framework: only "Process is terminated due to StackOverflowException.";
  on .NET 8: "Stack overflow." + stack (with "Repeat N times" for pure recursion).
  The dump of a corrupted stack may contain spurious types/assemblies
  (`System.__Canon`) — do not interpret them as runtime evidence.
