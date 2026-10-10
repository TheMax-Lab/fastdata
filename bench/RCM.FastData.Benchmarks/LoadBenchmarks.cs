using System.Data;
using BenchmarkDotNet.Attributes;

namespace RCM.FastData.Benchmarks
{
    // LOAD family: building a table from scratch (N rows, narrow schema).
    // Each invocation rebuilds the entire table (BeginLoadData + Rows.Add x N),
    // so it measures the complete loading cost, not just the marginal cost.
    [MemoryDiagnoser]
    public class LoadBenchmarks
    {
        [Params(1000, 10000, 100000)]
        public int N { get; set; }

        [Benchmark(Description = "SystemData: Load N={N}")]
        public int Load_SystemData()
        {
            DataTable dt = BenchTables.BuildSystemData(N);
            return dt.Rows.Count;
        }

        [Benchmark(Description = "Original: Load N={N}")]
        public int Load_Original()
        {
            RCM.FastData.Original.FastDataTable ft = BenchTables.BuildOriginal(N);
            return ft.Rows.Count;
        }

        [Benchmark(Description = "Fixed: Load N={N}")]
        public int Load_Fixed()
        {
            RCM.FastData.FastDataTable ft = BenchTables.BuildFixed(N);
            return ft.Rows.Count;
        }
    }
}
