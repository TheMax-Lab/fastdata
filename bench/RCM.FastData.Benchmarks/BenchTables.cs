using System;
using System.Data;
using RCM.FastData.Shared;

namespace RCM.FastData.Benchmarks
{
    // Deterministic construction of the benchmark tables: same schema (narrow, 10 columns),
    // same values (BenchData, fixed seed), same row order for the three subjects:
    // System.Data, original FastData (immutable snapshot) and fixed/to-be-optimized FastData.
    // Each builder uses the canonical loading API of the library (BeginLoadData/EndLoadData).
    public static class BenchTables
    {
        public const int Size1K = 1000;
        public const int Size10K = 10000;
        public const int Size100K = 100000;

        public static DataTable BuildSystemData(int n)
        {
            DataTable dt = new DataTable("T");
            for (int c = 0; c < BenchData.NarrowColumns.Length; c++)
                dt.Columns.Add(BenchData.NarrowColumns[c], BenchData.NarrowTypes[c]);

            dt.BeginLoadData();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    object[] v = new object[BenchData.NarrowColumns.Length];
                    for (int c = 0; c < v.Length; c++) v[c] = BenchData.Value(i, c);
                    dt.Rows.Add(v);
                }
            }
            finally { dt.EndLoadData(); }
            return dt;
        }

        public static RCM.FastData.FastDataTable BuildFixed(int n)
        {
            RCM.FastData.FastDataTable ft = new RCM.FastData.FastDataTable("T");
            for (int c = 0; c < BenchData.NarrowColumns.Length; c++)
                ft.Columns.Add(BenchData.NarrowColumns[c], BenchData.NarrowTypes[c]);

            ft.BeginLoadData();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    object[] v = new object[BenchData.NarrowColumns.Length];
                    for (int c = 0; c < v.Length; c++) v[c] = BenchData.Value(i, c);
                    ft.Rows.Add(v);
                }
            }
            finally { ft.EndLoadData(); }
            return ft;
        }

        public static RCM.FastData.Original.FastDataTable BuildOriginal(int n)
        {
            RCM.FastData.Original.FastDataTable ft = new RCM.FastData.Original.FastDataTable();
            for (int c = 0; c < BenchData.NarrowColumns.Length; c++)
                ft.Columns.Add(BenchData.NarrowColumns[c], BenchData.NarrowTypes[c]);

            ft.BeginLoadData();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    object[] v = new object[BenchData.NarrowColumns.Length];
                    for (int c = 0; c < v.Length; c++) v[c] = BenchData.Value(i, c);
                    ft.Rows.Add(v);
                }
            }
            finally { ft.EndLoadData(); }
            return ft;
        }
    }
}
