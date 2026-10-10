using System;
using System.Data;
using RCM.FastData;
using Xunit;
using DataRowState = RCM.FastData.DataRowState;

namespace RCM.FastData.Tests.Core
{
    // Phase D (P1): tests for the APIs added to the correct library:
    // Rows.Find/Contains (single and composite PK), Rows.RemoveAt, ImportRow,
    // LoadDataRow, FastDataView.Count/indexer. The Phase B suite (149 tests)
    // predates these APIs.
    public class P1ApiTests
    {
        private static FastDataTable WithPk(int rows = 5)
        {
            var ft = new FastDataTable();
            var id = ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));
            ft.PrimaryKey = new[] { id };
            for (int i = 1; i <= rows; i++) ft.Rows.Add(i, "v" + i);
            return ft;
        }

        [Fact]
        public void Find_RitornaRigaPerChiave()
        {
            var ft = WithPk();
            var r = ft.Rows.Find(3);
            Assert.NotNull(r);
            Assert.Equal(3, r["Id"]);
            Assert.Equal("v3", r["V"]);
        }

        [Fact]
        public void Find_ChiaveAssente_RitornaNulla()
        {
            var ft = WithPk();
            Assert.Null(ft.Rows.Find(99));
        }

        [Fact]
        public void Find_SenzaPK_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Rows.Add(1);
            Assert.Throws<InvalidOperationException>(() => ft.Rows.Find(1));
        }

        [Fact]
        public void Find_NumeroChiaviDiverso_Lancia()
        {
            var ft = WithPk();
            Assert.Throws<ArgumentException>(() => ft.Rows.Find(1, 2));
        }

        [Fact]
        public void Find_PkComposita()
        {
            var ft = new FastDataTable();
            var a = ft.Columns.Add("A", typeof(int));
            var b = ft.Columns.Add("B", typeof(string));
            ft.Columns.Add("V", typeof(int));
            ft.PrimaryKey = new[] { a, b };
            ft.Rows.Add(1, "x", 10);
            ft.Rows.Add(1, "y", 20);
            ft.Rows.Add(2, "x", 30);

            var r = ft.Rows.Find(1, "y");
            Assert.NotNull(r);
            Assert.Equal(20, r["V"]);
            Assert.Null(ft.Rows.Find(1, "z"));
            Assert.Throws<ArgumentException>(() => ft.Rows.Find(1));
        }

        [Fact]
        public void Contains_RifletteFind()
        {
            var ft = WithPk();
            Assert.True(ft.Rows.Contains(1));
            Assert.False(ft.Rows.Contains(99));
        }

        [Fact]
        public void RemoveAt_RimuoveImmediatamente()
        {
            var ft = WithPk();
            var r = ft.Rows[2]; // Id = 3
            ft.Rows.RemoveAt(2);
            Assert.Equal(4, ft.Rows.Count);
            Assert.Equal(DataRowState.Detached, r.RowState);
            Assert.False(ft.Rows.Contains(3));
        }

        [Fact]
        public void RemoveAt_FuoriRange_Lancia()
        {
            var ft = WithPk();
            Assert.Throws<ArgumentOutOfRangeException>(() => ft.Rows.RemoveAt(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => ft.Rows.RemoveAt(-1));
        }

        [Fact]
        public void ImportRow_CopiaPerNomeESaltaEspressioni()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "Id * 2";
            ft.Rows.Add(1, "a");

            var src = new FastDataTable();
            src.Columns.Add("Id", typeof(int));
            src.Columns.Add("V", typeof(string));
            src.Columns.Add("Extra", typeof(string)); // absent in the destination
            src.Rows.Add(7, "z", "x");

            var r = ft.ImportRow(src.Rows[0]);
            Assert.Equal(2, ft.Rows.Count);
            Assert.Equal(7, r["Id"]);
            Assert.Equal("z", r["V"]);
            Assert.Equal(14, r["E"]); // expression column recomputed
            Assert.Equal(DataRowState.Added, r.RowState);
        }

        [Fact]
        public void LoadDataRow_StatoRichiesto()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));

            var r1 = ft.LoadDataRow(new object[] { 1, "a" }, DataRowState.Added);
            Assert.Equal(DataRowState.Added, r1.RowState);

            var r2 = ft.LoadDataRow(new object[] { 2, "b" }, DataRowState.Unchanged);
            Assert.Equal(DataRowState.Unchanged, r2.RowState);
            Assert.Equal(2, ft.Rows.Count);
        }

        [Fact]
        public void LoadDataRow_LunghezzaESStatoInvalidi_Lanciano()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));

            Assert.Throws<ArgumentException>(() => ft.LoadDataRow(new object[] { 1 }, DataRowState.Added));
            Assert.Throws<ArgumentException>(() => ft.LoadDataRow(new object[] { 1, "a" }, DataRowState.Modified));
        }

        [Fact]
        public void View_CountEIndicizzatore_RiflettonoFiltroESort()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));
            for (int i = 1; i <= 4; i++) ft.Rows.Add(i, i % 2 == 0 ? "E" : "D");
            ft.AcceptChanges();

            var v = ft.DefaultView;
            v.RowFilter = "V = 'E'";
            v.Sort = "Id DESC";
            Assert.Equal(2, v.Count);
            Assert.Equal(4, v[0]["Id"]);
            Assert.Equal(2, v[1]["Id"]);
            Assert.Throws<ArgumentOutOfRangeException>(() => { var x = v[2]; });
        }
    }
}
