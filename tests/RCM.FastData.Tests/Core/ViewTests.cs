using System;
using System.Linq;
using RCM.FastData;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class ViewTests
    {
        private static FastDataTable Build()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("City", typeof(string));
            for (int i = 1; i <= 6; i++)
                ft.Rows.Add(i, i % 2 == 0 ? "Roma" : "Milano");
            ft.AcceptChanges();
            return ft;
        }

        [Fact]
        public void DefaultView_SenzaFiltro_MostraTutte()
        {
            var ft = Build();
            Assert.Equal(6, ft.DefaultView.Count(r => true));
        }

        [Fact]
        public void RowFilter_Sort()
        {
            var ft = Build();
            var v = ft.DefaultView;
            v.RowFilter = "City = 'Roma'";
            v.Sort = "Id DESC";
            var ids = v.Select(r => (int)r["Id"]).ToArray();
            Assert.Equal(new[] { 6, 4, 2 }, ids);
        }

        [Fact]
        public void VistaLive_RifletteModifiche()
        {
            var ft = Build();
            var v = ft.DefaultView;
            v.RowFilter = "Id < 10";
            ft.Rows[0]["Id"] = 100; // drops out of the filter
            Assert.DoesNotContain(v, r => (int)r["Id"] == 100);
            Assert.Equal(5, v.Count(r => true));
        }

        [Fact]
        public void VistaLive_RigheDeletedEscluse()
        {
            var ft = Build();
            var v = ft.DefaultView;
            ft.Rows[0].Delete();
            Assert.Equal(5, v.Count(r => true));
        }

        [Fact]
        public void ToTable_RispecchiaFiltroESort()
        {
            var ft = Build();
            var v = ft.DefaultView;
            v.RowFilter = "Id > 2";
            v.Sort = "Id";
            var t = v.ToTable();
            Assert.Equal(4, t.Rows.Count);
            Assert.Equal(3, (int)t.Rows[0]["Id"]);
            // the returned table is detached and Unchanged
            Assert.Equal(DataRowState.Unchanged, t.Rows[0].RowState);
        }

        [Fact]
        public void ToTable_ConColonneSpecifiche()
        {
            // P1: ToTable(string[] columnNames) overload like System.Data.DataView
            var ft = Build();
            var v = ft.DefaultView;
            v.RowFilter = "Id <= 2";
            var t = v.ToTable(new[] { "Id" });
            Assert.Single(t.Columns);
            Assert.Equal("Id", t.Columns[0].ColumnName);
            Assert.Equal(2, t.Rows.Count);
        }
    }
}
