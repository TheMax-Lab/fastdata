using System;
using System.Data;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using RCM.FastData.Shared;
using Xunit;

namespace RCM.FastData.Tests.Differential
{
    /// <summary>
    /// Differentials: same table, same input, behavior comparison
    /// System.Data (DataTable) vs correct FastDataTable.
    /// </summary>
    public class DifferentialTests
    {
        private static int[] SdIds(string filter, string sort = null)
        {
            DataRow[] rows = string.IsNullOrEmpty(filter)
                ? Builders.BuildSd().Select(null, sort)
                : Builders.BuildSd().Select(filter, sort);
            int[] ids = new int[rows.Length];
            for (int i = 0; i < rows.Length; i++) ids[i] = (int)rows[i][BenchData.ColId];
            return ids;
        }

        private static int[] FdIds(string filter, string sort = null)
        {
            var rows = string.IsNullOrEmpty(filter)
                ? Builders.BuildFd().Select(null, sort)
                : Builders.BuildFd().Select(filter, sort);
            int[] ids = new int[rows.Length];
            for (int i = 0; i < rows.Length; i++) ids[i] = (int)rows[i][BenchData.ColId];
            return ids;
        }

        private static void SameValue(object expected, object actual)
        {
            if (expected is double && actual is double)
                Assert.Equal((double)expected, (double)actual, 8);
            else
                Assert.Equal(expected, actual);
        }

        private static void AssertSameCompute(string expression)
        {
            object sd = Builders.BuildSd().Compute(expression, null);
            object fd = Builders.BuildFd().Compute(expression, null);
            Assert.True(sd.GetType() == fd.GetType(),
                "Tipo: " + expression + " -> System.Data:" + sd.GetType().Name + " FastData:" + fd.GetType().Name);
            SameValue(sd, fd);
        }

        [Fact]
        public void Select_Intervallo()
        {
            Assert.Equal(SdIds("Id > 10 AND Id < 15"), FdIds("Id > 10 AND Id < 15"));
        }

        [Fact]
        public void Select_StringaCaseInsensitive()
        {
            Assert.Equal(SdIds("City = 'roma'"), FdIds("City = 'roma'"));
            Assert.Equal(SdIds("City = 'Roma'"), FdIds("City = 'Roma'"));
        }

        // Phase D: these two tests used positional Assert.Equal, but System.Data
        // implicitly sorts the result of Select(filter) on the filtered column
        // (documented ADO.NET behavior), while FastData preserves the insertion
        // order (declared difference in docs/COMPATIBILITA.md). The differential
        // contract is the SET of rows, as already established by the file comments:
        // the Ids are compared sorted.
        [Fact]
        public void Select_Like()
        {
            // Phase D (D5): System.Data LIKE semantics (probe 2026-10-09):
            // % /* wildcards only at the edges, _ literal, [c] = single character.
            AssertSameIdSet("Name LIKE 'Al%'");
            AssertSameIdSet("City LIKE 'R___'");    // _ literal: 0 rows on both
            AssertSameIdSet("City LIKE 'R%'");
            AssertSameIdSet("City LIKE '%oma'");
            AssertSameIdSet("City LIKE 'R*'");
            AssertSameIdSet("Name LIKE 'A[l]pha'"); // [c] = single character
        }

        [Fact]
        public void Select_IsNull()
        {
            AssertSameIdSet("City IS NULL");
            AssertSameIdSet("City IS NOT NULL");
        }

        private static void AssertSameIdSet(string filter)
        {
            int[] a = SdIds(filter);
            int[] b = FdIds(filter);
            Array.Sort(a);
            Array.Sort(b);
            Assert.Equal(a, b);
        }

        private static string[] SdX(string filter)
        {
            DataRow[] rows = Builders.BuildTypedSd().Select(filter);
            string[] xs = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                xs[i] = rows[i].IsNull("X")
                    ? "NULL"
                    : ((double)rows[i]["X"]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return xs;
        }

        private static string[] FdX(string filter)
        {
            var rows = Builders.BuildTypedFd().Select(filter);
            string[] xs = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                xs[i] = (rows[i]["X"] == DBNull.Value)
                    ? "NULL"
                    : ((double)rows[i]["X"]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return xs;
        }

        // System.Data applies an implicit sort on the filtered column (side effect
        // of DataView): the result is NOT in row order. FastData preserves the order.
        // The differential contract is the SET of rows: they are compared sorted.
        // (Difference documented in docs/COMPATIBILITA.md)
        private static void AssertSameXSet(string filter)
        {
            string[] a = SdX(filter);
            string[] b = FdX(filter);
            Array.Sort(a);
            Array.Sort(b);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Select_DoubleNan_EInfinity_ComeSystemData()
        {
            // double semantics in filters (verified on net48 and net8.0): the comparison
            // uses IComparable, so NaN is "smaller than everything" (NaN.CompareTo(x) == -1)
            // and +Inf is "larger than everything". Same behavior on System.Data.
            AssertSameXSet("X < 100");
            AssertSameXSet("X > 0 AND X < 10");
            AssertSameXSet("X > 0");
            AssertSameXSet("X <> 1.5");
        }

        [Fact]
        public void Select_EOrNot()
        {
            Assert.Equal(SdIds("Id = 1 OR Id = 2"), FdIds("Id = 1 OR Id = 2"));
            Assert.Equal(SdIds("NOT (Id = 1)"), FdIds("NOT (Id = 1)"));
        }

        [Fact]
        public void Sort_Multicolonna()
        {
            Assert.Equal(SdIds(null, "Code, Id DESC"), FdIds(null, "Code, Id DESC"));
            Assert.Equal(SdIds("Id < 20", "Id DESC"), FdIds("Id < 20", "Id DESC"));
        }

        [Fact]
        public void Compute_TipiEValori()
        {
            // NB: System.Data Compute does NOT support "COUNT(*)": only
            // column arguments are compared (prompt rule: no COUNT(*) on the differentials).
            AssertSameCompute("COUNT(Id)");
            AssertSameCompute("COUNT(Qty)");
            AssertSameCompute("SUM(Id)");
            AssertSameCompute("SUM(Qty)");
            AssertSameCompute("AVG(Id)");
            AssertSameCompute("MIN(Id)");
            AssertSameCompute("MAX(Id)");
        }

        [Fact]
        public void Compute_DecimalELong_TipiEValori()
        {
            object sd = Builders.BuildTypedSd().Compute("SUM(D)", null);
            object fd = Builders.BuildTypedFd().Compute("SUM(D)", null);
            Assert.True(sd.GetType() == fd.GetType(),
                "Tipo SUM(D): System.Data:" + sd.GetType().Name + " FastData:" + fd.GetType().Name);
            SameValue(sd, fd);
            Assert.Equal(0.6m, (decimal)fd);

            sd = Builders.BuildTypedSd().Compute("SUM(L)", null);
            fd = Builders.BuildTypedFd().Compute("SUM(L)", null);
            Assert.True(sd.GetType() == fd.GetType(),
                "Tipo SUM(L): System.Data:" + sd.GetType().Name + " FastData:" + fd.GetType().Name);
            SameValue(sd, fd);
        }

        [Fact]
        public void Compute_Filtro()
        {
            AssertSameComputeFiltered("COUNT(Id)", "Id <= 10");
            AssertSameComputeFiltered("SUM(Id)", "Id <= 10");
        }

        private static void AssertSameComputeFiltered(string expression, string filter)
        {
            object sd = Builders.BuildSd().Compute(expression, filter);
            object fd = Builders.BuildFd().Compute(expression, filter);
            Assert.True(sd.GetType() == fd.GetType(), "Tipo: " + expression);
            SameValue(sd, fd);
        }

        [Fact]
        public void RowState_CicloModificaEliminaAccetta()
        {
            var sd = Builders.BuildSd();
            var fd = Builders.BuildFd();

            // the two DataRowState enums (System.Data and RCM.FastData) have different values
            // for Modified/Deleted: comparison by the name of the state
            sd.Rows[0][0] = 99;
            fd.Rows[0][0] = 99;
            Assert.Equal(sd.Rows[0].RowState.ToString(), fd.Rows[0].RowState.ToString());

            var heldSd = sd.Rows[1];
            var heldFd = fd.Rows[1];
            heldSd.Delete();
            heldFd.Delete();
            Assert.Equal(sd.Rows.Count, fd.Rows.Count);

            sd.AcceptChanges();
            fd.AcceptChanges();
            Assert.Equal(sd.Rows.Count, fd.Rows.Count);
            Assert.Equal(heldSd.RowState.ToString(), heldFd.RowState.ToString());
            Assert.Equal(sd.Rows[0].RowState.ToString(), fd.Rows[0].RowState.ToString());
        }

        [Fact]
        public void Field_TipoEsatto()
        {
            var sd = Builders.BuildSd();
            var fd = Builders.BuildFd();

            Assert.Equal((int)sd.Rows[4][BenchData.ColId], fd.Rows[4].Field<int>(BenchData.ColId));
            Assert.Equal(sd.Rows[4].Field<string>(BenchData.ColName), fd.Rows[4].Field<string>(BenchData.ColName));
            Assert.Equal(sd.Rows[4].Field<double>("Score"), fd.Rows[4].Field<double>("Score"));
        }

        [Fact]
        public void Field_TipoEsatto_StrictComeSystemData()
        {
            // System.Data Field<T> does strict unboxing (no conversion):
            // Field<string> on an int column throws InvalidCastException even in SD.
            var sd = Builders.BuildSd();
            var fd = Builders.BuildFd();
            Assert.Throws<InvalidCastException>(() => sd.Rows[4].Field<string>(BenchData.ColId));
            Assert.Throws<InvalidCastException>(() => fd.Rows[4].Field<string>(BenchData.ColId));
        }

        [Fact]
        public void Field_Nullable()
        {
            var sd = Builders.BuildSd();
            var fd = Builders.BuildFd();

            // row 0: Qty NULL (i % 13 == 0)
            Assert.Equal(sd.Rows[0].Field<int?>("Qty"), fd.Rows[0].Field<int?>("Qty"));
            // row 1: Qty = 1
            Assert.Equal(sd.Rows[1].Field<int?>("Qty"), fd.Rows[1].Field<int?>("Qty"));
        }

        [Theory]
        [InlineData("it-IT")]
        [InlineData("en-US")]
        [InlineData("")]
        public void LetteraliNumerici_StessoComportamentoInOgniCultura(string culture)
        {
            using (new CultureScope(culture))
            {
                Assert.Equal(SdIds("Score = 0.5").Length, FdIds("Score = 0.5").Length);
                Assert.Equal(SdIds("Price = 1.25").Length, FdIds("Price = 1.25").Length);
            }
        }
    }
}
