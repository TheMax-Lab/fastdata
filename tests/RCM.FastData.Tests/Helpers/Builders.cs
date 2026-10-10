using System;
using System.Data;
using RCM.FastData;
using RCM.FastData.Shared;

namespace RCM.FastData.Tests.Helpers
{
    // Builds the same table (narrow, N rows, deterministic data) on both
    // System.Data and FastData, for the differential tests.
    public static class Builders
    {
        public const int TestRows = 200;

        public static DataTable BuildSd(int n = TestRows)
        {
            DataTable dt = new DataTable("T");
            for (int c = 0; c < BenchData.NarrowColumns.Length; c++)
                dt.Columns.Add(BenchData.NarrowColumns[c], BenchData.NarrowTypes[c]);

            for (int i = 0; i < n; i++)
            {
                object[] v = new object[BenchData.NarrowColumns.Length];
                for (int c = 0; c < v.Length; c++) v[c] = BenchData.Value(i, c);
                dt.Rows.Add(v);
            }
            return dt;
        }

        public static FastDataTable BuildFd(int n = TestRows)
        {
            FastDataTable ft = new FastDataTable("T");
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
            finally
            {
                ft.EndLoadData();
            }
            return ft;
        }

        // Table dedicated to hard types: precise decimal, long beyond 2^53, double with NaN/Inf.
        public static FastDataTable BuildTypedFd()
        {
            FastDataTable ft = new FastDataTable("Typed");
            ft.Columns.Add("D", typeof(decimal));
            ft.Columns.Add("L", typeof(long));
            ft.Columns.Add("X", typeof(double));

            ft.Rows.Add(0.1m, 9007199254740993L, double.NaN);     // D0 L0 X0
            ft.Rows.Add(0.2m, 9007199254740991L, double.PositiveInfinity);
            ft.Rows.Add(0.3m, 9007199254740992L, 1.5d);
            ft.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value); // all-NULL row
            return ft;
        }

        public static DataTable BuildTypedSd()
        {
            DataTable dt = new DataTable("Typed");
            dt.Columns.Add("D", typeof(decimal));
            dt.Columns.Add("L", typeof(long));
            dt.Columns.Add("X", typeof(double));

            dt.Rows.Add(0.1m, 9007199254740993L, double.NaN);
            dt.Rows.Add(0.2m, 9007199254740991L, double.PositiveInfinity);
            dt.Rows.Add(0.3m, 9007199254740992L, 1.5d);
            dt.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value);
            return dt;
        }
    }
}
