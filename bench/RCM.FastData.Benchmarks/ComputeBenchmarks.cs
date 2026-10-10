using System.Data;
using BenchmarkDotNet.Attributes;

namespace RCM.FastData.Benchmarks
{
    // COMPUTE family: aggregations on a pre-built table.
    // Attention to workload parity: only expressions supported by BOTH
    // APIs are used (COUNT(*) is NOT supported by System.Data.Compute: verified in Phase B,
    // SyntaxErrorException). The correctness of types/results is covered by the
    // differential tests, here only the performance is measured.
    [MemoryDiagnoser]
    public class ComputeBenchmarks
    {
        [Params(1000, 10000, 100000)]
        public int N { get; set; }

        private DataTable sd;
        private RCM.FastData.Original.FastDataTable original;
        private RCM.FastData.FastDataTable fixedTable;
        private string fHalf;

        [GlobalSetup]
        public void Setup()
        {
            sd = BenchTables.BuildSystemData(N);
            original = BenchTables.BuildOriginal(N);
            fixedTable = BenchTables.BuildFixed(N);

            fHalf = "Id <= " + (N / 2);
        }

        [Benchmark(Description = "SystemData: Compute SUM(Qty) N={N}")]
        public object ComputeSum_SystemData() { return sd.Compute("SUM(Qty)", null); }

        [Benchmark(Description = "Original: Compute SUM(Qty) N={N}")]
        public object ComputeSum_Original() { return original.Compute("SUM(Qty)", null); }

        [Benchmark(Description = "Fixed: Compute SUM(Qty) N={N}")]
        public object ComputeSum_Fixed() { return fixedTable.Compute("SUM(Qty)", null); }

        [Benchmark(Description = "SystemData: Compute COUNT(Id) with filter N={N}")]
        public object ComputeCountFiltered_SystemData() { return sd.Compute("COUNT(Id)", fHalf); }

        [Benchmark(Description = "Original: Compute COUNT(Id) with filter N={N}")]
        public object ComputeCountFiltered_Original() { return original.Compute("COUNT(Id)", fHalf); }

        [Benchmark(Description = "Fixed: Compute COUNT(Id) with filter N={N}")]
        public object ComputeCountFiltered_Fixed() { return fixedTable.Compute("COUNT(Id)", fHalf); }

        [Benchmark(Description = "SystemData: Compute SUM(Price) N={N}")]
        public object ComputeSumPrice_SystemData() { return sd.Compute("SUM(Price)", null); }

        [Benchmark(Description = "Original: Compute SUM(Price) N={N}")]
        public object ComputeSumPrice_Original() { return original.Compute("SUM(Price)", null); }

        [Benchmark(Description = "Fixed: Compute SUM(Price) N={N}")]
        public object ComputeSumPrice_Fixed() { return fixedTable.Compute("SUM(Price)", null); }
    }
}
