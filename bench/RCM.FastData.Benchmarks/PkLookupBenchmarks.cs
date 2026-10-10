using System.Data;
using BenchmarkDotNet.Attributes;
using RCM.FastData;

namespace RCM.FastData.Benchmarks
{
    // PKLOOKUP family: point reading of a row by primary key.
    // Phase C (baseline): System.Data only (Rows.Find with PK set).
    // Original: point lookup API NOT supported (Rows.Find/Contains missing) ->
    //   marked "not supported" in the report, no methods here (immutable snapshot).
    // Fixed (Phase D): Rows.Find/Contains implemented -> PkFind_Fixed and PkContains_Fixed.
    [MemoryDiagnoser]
    public class PkLookupBenchmarks
    {
        [Params(1000, 10000, 100000)]
        public int N { get; set; }

        private DataTable sd;
        private FastDataTable fixedTable;
        private int targetId;

        [GlobalSetup]
        public void Setup()
        {
            sd = BenchTables.BuildSystemData(N);
            sd.PrimaryKey = new DataColumn[] { sd.Columns["Id"] };

            fixedTable = BenchTables.BuildFixed(N);
            fixedTable.PrimaryKey = new FastDataColumn[] { fixedTable.Columns["Id"] };

            targetId = N / 2; // existing Id (Id = i+1)
        }

        [Benchmark(Description = "SystemData: Rows.Find(PK) N={N}")]
        public object PkFind_SystemData() { return sd.Rows.Find(targetId)["Id"]; }

        [Benchmark(Description = "Fixed: Rows.Find(PK) N={N}")]
        public object PkFind_Fixed() { return fixedTable.Rows.Find(targetId)["Id"]; }

        [Benchmark(Description = "SystemData: Rows.Contains(PK) N={N}")]
        public bool PkContains_SystemData() { return sd.Rows.Contains(targetId); }

        [Benchmark(Description = "Fixed: Rows.Contains(PK) N={N}")]
        public bool PkContains_Fixed() { return fixedTable.Rows.Contains(targetId); }
    }
}
