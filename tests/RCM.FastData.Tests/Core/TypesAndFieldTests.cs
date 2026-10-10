using System;
using System.Data;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class TypesAndFieldTests
    {
        [Fact]
        public void FieldInt_StessoTipo()
        {
            var ft = Builders.BuildFd();
            var r = ft.Rows[0];
            Assert.Equal(1, r.Field<int>("Id"));
            Assert.Equal(1, r.Field<int>(0));
        }

        [Fact]
        public void FieldInt_SuDBNull()
        {
            var ft = Builders.BuildFd();
            // i=5 (Id 6) has City NULL
            var r = ft.Rows[5];
            Assert.Equal(DBNull.Value, r["City"]);
            Assert.Equal(6, r.Field<int>("Id"));
        }

        [Fact]
        public void FieldDouble_DaInt_StrictComeSystemData()
        {
            // System.Data Field<T> does strict unboxing: double on an int column -> InvalidCastException
            var ft = Builders.BuildFd();
            Assert.Throws<InvalidCastException>(() => ft.Rows[4].Field<double>("Id"));
        }

        [Fact]
        public void FieldString_DaInt_StrictComeSystemData()
        {
            // like System.Data: Field<string> on an int column throws, does not convert
            var ft = Builders.BuildFd();
            Assert.Throws<InvalidCastException>(() => ft.Rows[4].Field<string>("Id"));
        }

        [Fact]
        public void FieldNullable_StessoTipo()
        {
            // P0: the original uses Convert.ChangeType(value, typeof(T)) which does not support Nullable
            var ft = Builders.BuildFd();
            Assert.Equal(5, ft.Rows[4].Field<int?>("Id"));
        }

        [Fact]
        public void FieldNullable_SuDBNull()
        {
            var ft = Builders.BuildFd();
            // non-nullable T on NULL -> InvalidCastException (like System.Data)
            Assert.Throws<InvalidCastException>(() => ft.Rows[5].Field<string>("City"));
            // nullable T on NULL -> null (like System.Data)
            Assert.Null(ft.Rows[0].Field<int?>("Qty"));
        }

        [Fact]
        public void FieldNullableDouble()
        {
            var ft = Builders.BuildFd();
            Assert.Equal(0.5d, ft.Rows[1].Field<double?>("Score"));
        }

        [Fact]
        public void Decimal_NessunaPerditaNellaLettura()
        {
            var ft = Builders.BuildTypedFd();
            Assert.Equal(0.1m, ft.Rows[0].Field<decimal>("D"));
            Assert.Equal(0.2m, ft.Rows[1].Field<decimal>("D"));
        }

        [Fact]
        public void LongOltre2Al53_NeiFiltri()
        {
            var ft = Builders.BuildTypedFd();
            var rows = ft.Select("L = 9007199254740993");
            Assert.Single(rows);
            Assert.Equal(9007199254740993L, rows[0]["L"]);
        }

        [Fact]
        public void DoubleNan_EInfinity_NeiFiltri()
        {
            // Semantics verified on System.Data (net48 and net8.0): the comparison uses
            // IComparable and double.NaN.CompareTo(x) == -1 for every non-NaN x, so
            // "NaN is smaller than everything". +Inf is larger than everything.
            // (probe: Select("X < 100") -> [NaN, 1.5]; "X > 0" -> [1.5, +Inf] on System.Data)
            var ft = Builders.BuildTypedFd();
            var one = ft.Select("X = 1.5");
            Assert.Single(one);
            Assert.Equal(1.5d, one[0]["X"]);

            // NaN does not pass "X > 0" (NaN > 0 is false), +Inf is not in (0,10)
            var inRange = ft.Select("X > 0 AND X < 10");
            Assert.Single(inRange);
            Assert.Equal(1.5d, inRange[0]["X"]);

            // NaN "is smaller than 100": included, like System.Data
            var less = ft.Select("X < 100");
            Assert.Equal(2, less.Length);
            var xs = System.Linq.Enumerable.ToList(less).ConvertAll(r => (double)r["X"]);
            Assert.Contains(double.NaN, xs);
            Assert.Contains(1.5d, xs);

            var greater = ft.Select("X > 0");
            Assert.Equal(2, greater.Length);
        }

        [Fact]
        public void Bool_NeiFiltriEAggregati()
        {
            var ft = Builders.BuildFd();
            Assert.Equal(100, (int)ft.Compute("COUNT(*)", "Flag = True"));
        }

        [Fact]
        public void AssegnazioneRispettaTipoColonna()
        {
            // FastDataColumn.DataType must be respected in assignments
            var ft = new FastDataTable();
            var col = ft.Columns.Add("N", typeof(int));
            var r = ft.NewRow();
            Assert.ThrowsAny<Exception>(() => r[0] = "non-numerico");
        }

        [Fact]
        public void ConversioneSullAssegnazione_ComeSystemData()
        {
            // System.Data converts on assignment (Convert.ChangeType with current culture):
            // 1.5d on an int column -> 2; "123" on int -> 123; not convertible -> exception.
            var ft = new FastDataTable();
            ft.Columns.Add("N", typeof(int));
            ft.Columns.Add("D", typeof(double));
            ft.Columns.Add("S", typeof(string));
            var r = ft.NewRow();
            r["N"] = 1.5d;
            Assert.Equal(2, r["N"]); // Convert.ToDouble->ToInt32: midpoint away from zero
            r["N"] = "123";
            Assert.Equal(123, r["N"]);
            Assert.ThrowsAny<Exception>(() => r["N"] = "non-numerico");
            r["D"] = 1;
            Assert.Equal(1.0d, r["D"]);
            r["S"] = 7;
            Assert.Equal("7", r["S"]);
        }
    }
}
