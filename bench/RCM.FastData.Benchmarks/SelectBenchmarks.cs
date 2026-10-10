using System.Data;
using BenchmarkDotNet.Attributes;

namespace RCM.FastData.Benchmarks
{
    // SELECT family: filters on a table pre-built in GlobalSetup (once per N).
    // The filters use only integer and string literals (independent of culture).
    // The numeric filters are proportional to N (range ~20% of rows) so the
    // workload stays representative at every size.
    [MemoryDiagnoser]
    public class SelectBenchmarks
    {
        [Params(1000, 10000, 100000)]
        public int N { get; set; }

        private DataTable sd;
        private RCM.FastData.Original.FastDataTable original;
        private RCM.FastData.FastDataTable fixedTable;
        private string fEqInt, fRange, fCompound;

        [GlobalSetup]
        public void Setup()
        {
            sd = BenchTables.BuildSystemData(N);
            original = BenchTables.BuildOriginal(N);
            fixedTable = BenchTables.BuildFixed(N);

            fEqInt = "Id = " + (N / 2);
            fRange = "Id > " + (N * 4 / 10) + " AND Id < " + (N * 6 / 10);
            fCompound = "City = 'Roma' AND Qty > 10";
        }

        [Benchmark(Description = "SystemData: Select equality-string N={N}")]
        public int SelectEqString_SystemData() { return sd.Select("Code = 'C3'").Length; }

        [Benchmark(Description = "Original: Select equality-string N={N}")]
        public int SelectEqString_Original() { return original.Select("Code = 'C3'").Length; }

        [Benchmark(Description = "Fixed: Select equality-string N={N}")]
        public int SelectEqString_Fixed() { return fixedTable.Select("Code = 'C3'").Length; }

        [Benchmark(Description = "SystemData: Select equality-int N={N}")]
        public int SelectEqInt_SystemData() { return sd.Select(fEqInt).Length; }

        [Benchmark(Description = "Original: Select equality-int N={N}")]
        public int SelectEqInt_Original() { return original.Select(fEqInt).Length; }

        [Benchmark(Description = "Fixed: Select equality-int N={N}")]
        public int SelectEqInt_Fixed() { return fixedTable.Select(fEqInt).Length; }

        [Benchmark(Description = "SystemData: Select range N={N}")]
        public int SelectRange_SystemData() { return sd.Select(fRange).Length; }

        [Benchmark(Description = "Original: Select range N={N}")]
        public int SelectRange_Original() { return original.Select(fRange).Length; }

        [Benchmark(Description = "Fixed: Select range N={N}")]
        public int SelectRange_Fixed() { return fixedTable.Select(fRange).Length; }

        [Benchmark(Description = "SystemData: Select compound N={N}")]
        public int SelectCompound_SystemData() { return sd.Select(fCompound).Length; }

        [Benchmark(Description = "Original: Select compound N={N}")]
        public int SelectCompound_Original() { return original.Select(fCompound).Length; }

        [Benchmark(Description = "Fixed: Select compound N={N}")]
        public int SelectCompound_Fixed() { return fixedTable.Select(fCompound).Length; }
    }
}
