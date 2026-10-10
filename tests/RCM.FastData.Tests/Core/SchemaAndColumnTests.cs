using System;
using System.Data;
using RCM.FastData;
using Xunit;
using DataRowState = RCM.FastData.DataRowState;

namespace RCM.FastData.Tests.Core
{
    public class SchemaAndColumnTests
    {
        [Fact]
        public void Tabella_Vuota_NessunaColonna()
        {
            var ft = new FastDataTable("T");
            Assert.Equal(0, ft.Columns.Count);
            Assert.Equal(0, ft.Rows.Count);
            Assert.Equal("T", ft.TableName);
        }

        [Fact]
        public void AggiuntaColonne_OrdinaliEOrdini()
        {
            var ft = new FastDataTable();
            var a = ft.Columns.Add("A", typeof(int));
            var b = ft.Columns.Add("B", typeof(string));
            Assert.Equal(0, a.Ordinal);
            Assert.Equal(1, b.Ordinal);
            Assert.Same(a, ft.Columns[0]);
            Assert.Same(b, ft.Columns["b"]); // case-insensitive like System.Data
        }

        [Fact]
        public void ColonnaDuplicata_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            Assert.Throws<ArgumentException>(() => ft.Columns.Add("A", typeof(string)));
            Assert.Throws<ArgumentException>(() => ft.Columns.Add("a", typeof(string)));
        }

        [Fact]
        public void ColonnaNonTrovata_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            Assert.Throws<ArgumentException>(() => ft.Columns["B"]);
        }

        [Fact]
        public void ColonneConSpaziENomiComplessi()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Con Spazio", typeof(int));
            ft.Columns.Add("Con.Apostrofo'", typeof(string));
            ft.Columns.Add("Con-Parentesi()", typeof(bool));
            Assert.True(ft.Columns.Contains("Con Spazio"));
            Assert.True(ft.Columns.Contains("con.apostrofo'"));
            Assert.True(ft.Columns.Contains("Con-Parentesi()"));
        }

        [Fact]
        public void RenameColonnaAttaccata_Lancia()
        {
            var ft = new FastDataTable();
            var a = ft.Columns.Add("A", typeof(int));
            Assert.Throws<InvalidOperationException>(() => a.ColumnName = "B");
        }

        [Fact]
        public void CambiareDataTypeSuColonnaAttaccata_Lancia()
        {
            var ft = new FastDataTable();
            var a = ft.Columns.Add("A", typeof(int));
            Assert.Throws<InvalidOperationException>(() => a.DataType = typeof(string));
        }

        [Fact]
        public void DataSet_NomeTabellaDuplicato_Lancia()
        {
            var ds = new FastDataSet();
            var t1 = new FastDataTable("T");
            ds.Tables.Add(t1);
            Assert.Throws<ArgumentException>(() => ds.Tables.Add(new FastDataTable("T")));
            Assert.Same(t1, ds.Tables["t"]);
        }

        [Fact]
        public void TabellaInAltroDataSet_NonPuoiEssereAggiunta()
        {
            var ds1 = new FastDataSet();
            var ds2 = new FastDataSet();
            var t = new FastDataTable("T");
            ds1.Tables.Add(t);
            Assert.Throws<InvalidOperationException>(() => ds2.Tables.Add(t));
        }

        [Fact]
        public void TabellaAggiuntaADataset_NonRinomabile()
        {
            var ds = new FastDataSet();
            var t = new FastDataTable("T");
            ds.Tables.Add(t);
            Assert.Throws<InvalidOperationException>(() => t.TableName = "U");
        }

        [Fact]
        public void PrimaryKey_DeveAppartenereATabella()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var other = new FastDataTable();
            other.Columns.Add("B", typeof(int));
            Assert.Throws<ArgumentException>(() => ft.PrimaryKey = new[] { other.Columns["B"] });
        }

        [Fact]
        public void NewRow_ValoriInizialiDBNull()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Columns.Add("B", typeof(string));
            var r = ft.NewRow();
            Assert.Equal(DBNull.Value, r[0]);
            Assert.Equal(DBNull.Value, r[1]);
            Assert.Equal(DataRowState.Detached, r.RowState);
        }

        [Fact]
        public void RigaCreaPrimaDiNuovaColonna_AttaccoEspande()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.NewRow();
            r[0] = 7;
            ft.Columns.Add("B", typeof(string)); // schema changes after NewRow
            ft.Rows.Add(r);
            Assert.Equal(2, r.ItemArray.Length);
            Assert.Equal(7, r[0]);
            Assert.Equal(DBNull.Value, r[1]);
        }

        [Fact]
        public void RowsAddParams_EccessoValoriTronca()
        {
            // FastData behavior (extension): excess values are ignored.
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.Rows.Add(1, 2, 3);
            Assert.Equal(1, r[0]);
        }

        [Fact]
        public void ItemArray_LengthDiversa_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Columns.Add("B", typeof(int));
            var r = ft.NewRow();
            Assert.Throws<ArgumentException>(() => r.ItemArray = new object[] { 1 });
        }
    }
}
