using System;
using System.Collections.Generic;
using System.Globalization;

namespace RCM.FastData.Shared
{
    // Common deterministic generator for tests and benchmarks (fixed seed).
    // No dependency on the library: produces only object[] values.
    public static class BenchData
    {
        public const int Seed = 20261009;

        // "Narrow" schema (~10 columns)
        public const string ColId = "Id";
        public const string ColCode = "Code";
        public const string ColName = "Name";
        public const string ColCity = "City";
        public const string ColQty = "Qty";
        public const string ColPrice = "Price";
        public const string ColScore = "Score";
        public const string ColBig = "Big";
        public const string ColDate = "Date";
        public const string ColFlag = "Flag";

        public static readonly string[] NarrowColumns =
        {
            ColId, ColCode, ColName, ColCity, ColQty,
            ColPrice, ColScore, ColBig, ColDate, ColFlag
        };

        public static readonly Type[] NarrowTypes =
        {
            typeof(int), typeof(string), typeof(string), typeof(string), typeof(int),
            typeof(decimal), typeof(double), typeof(long), typeof(DateTime), typeof(bool)
        };

        private static readonly string[] Names =
        {
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "O'Brien", "Alfa Uno"
        };
        private static readonly string[] Cities = { "Roma", "Milano", "Torino", "Napoli" };

        private static readonly DateTime DateBase = new DateTime(2020, 1, 1, 9, 30, 0, DateTimeKind.Unspecified);

        // Value (i, col) of the narrow schema. DBNull for controlled NULLs.
        public static object Value(int i, int colOrdinal)
        {
            switch (colOrdinal)
            {
                case 0: return i + 1;                                  // Unique Id
                case 1: return "C" + (i % 7);                          // Repeated Code (7 values)
                case 2: return Names[i % Names.Length];                // Repeated Name
                case 3: return i % 10 == 5 ? (object)DBNull.Value : Cities[i % Cities.Length];
                case 4: return i % 13 == 0 ? (object)DBNull.Value : (object)(i % 50);
                case 5: return (decimal)(i % 100) * 1.25m;             // Price decimal
                case 6: return (double)(i % 200) * 0.5;                // Score double
                case 7:
                    if (i == 0) return 9007199254740993L;              // 2^53+1
                    if (i == 1) return 9007199254740991L;              // 2^53-1
                    if (i == 2) return 9007199254740992L;              // 2^53
                    return i * 1000L;                                  // Big long
                case 8: return DateBase.AddDays(i % 366);
                case 9: return i % 2 == 0;
                default: throw new ArgumentOutOfRangeException("colOrdinal");
            }
        }

        // "Wide" schema (~32 columns) for stress: S00..S31
        public static string WideColumnName(int i) { return "S" + i.ToString("00", CultureInfo.InvariantCulture); }
        public static readonly int WideColumnCount = 32;

        public static Type WideColumnType(int i)
        {
            switch (i % 4)
            {
                case 0: return typeof(int);
                case 1: return typeof(string);
                case 2: return typeof(decimal);
                default: return typeof(double);
            }
        }

        public static object WideValue(int row, int col)
        {
            switch (col % 4)
            {
                case 0: return (row + col) % 1000;
                case 1: return "W" + col + "_" + (row % 17);
                case 2: return (decimal)((row + col) % 1000) * 0.5m;
                default: return (double)(row + col) * 0.125;
            }
        }
    }
}
