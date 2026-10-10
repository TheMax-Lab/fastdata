using System.Data;
using BenchmarkDotNet.Attributes;

namespace RCM.FastData.Benchmarks
{
    // ROWREAD family: reading single values from an existing row (pre-built
    // table). Two accesses: row["Col"] indexer and Field<string>("Col").
    // The row read is always N/2 (Id = N/2+1), identical for the three subjects.
    [MemoryDiagnoser]
    public class RowReadBenchmarks
    {
        [Params(1000, 10000, 100000)]
        public int N { get; set; }

        private DataTable sd;
        private RCM.FastData.Original.FastDataTable original;
        private RCM.FastData.FastDataTable fixedTable;
        private int mid;

        [GlobalSetup]
        public void Setup()
        {
            sd = BenchTables.BuildSystemData(N);
            original = BenchTables.BuildOriginal(N);
            fixedTable = BenchTables.BuildFixed(N);
            mid = N / 2;
        }

        [Benchmark(Description = "SystemData: row[\"Name\"] N={N}")]
        public object RowReadIndexer_SystemData() { return sd.Rows[mid]["Name"]; }

        [Benchmark(Description = "Original: row[\"Name\"] N={N}")]
        public object RowReadIndexer_Original() { return original.Rows[mid]["Name"]; }

        [Benchmark(Description = "Fixed: row[\"Name\"] N={N}")]
        public object RowReadIndexer_Fixed() { return fixedTable.Rows[mid]["Name"]; }

        [Benchmark(Description = "SystemData: Field<string>(\"Name\") N={N}")]
        public string RowReadField_SystemData() { return sd.Rows[mid].Field<string>("Name"); }

        [Benchmark(Description = "Original: Field<string>(\"Name\") N={N}")]
        public string RowReadField_Original() { return original.Rows[mid].Field<string>("Name"); }

        [Benchmark(Description = "Fixed: Field<string>(\"Name\") N={N}")]
        public string RowReadField_Fixed() { return fixedTable.Rows[mid].Field<string>("Name"); }
    }
}
