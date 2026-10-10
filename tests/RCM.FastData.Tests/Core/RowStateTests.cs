using System;
using System.Data;
using System.Linq;
using RCM.FastData;
using Xunit;
using DataRowState = RCM.FastData.DataRowState;

namespace RCM.FastData.Tests.Core
{
    public class RowStateTests
    {
        private static FastDataTable Build()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            for (int i = 0; i < 4; i++) ft.Rows.Add(i);
            ft.AcceptChanges();
            return ft;
        }

        [Fact]
        public void CicloCompleto_Stati()
        {
            var ft = Build();
            var r = ft.Rows[0];
            Assert.Equal(DataRowState.Unchanged, r.RowState);

            r[0] = 99;
            Assert.Equal(DataRowState.Modified, r.RowState);

            r.Delete();
            Assert.Equal(DataRowState.Deleted, r.RowState);
            // the row stays in the collection until AcceptChanges (like System.Data)
            Assert.Equal(4, ft.Rows.Count);

            ft.AcceptChanges();
            Assert.Equal(3, ft.Rows.Count);
            Assert.Equal(DataRowState.Deleted, r.RowState); // the retained reference stays Deleted
        }

        [Fact]
        public void DeleteSuRigaAdded_RimuoveImmediatamente_ComeSystemData()
        {
            // System.Data: Delete on an Added row -> removed from the table, state Detached.
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.NewRow();
            r[0] = 1;
            ft.Rows.Add(r);
            Assert.Equal(1, ft.Rows.Count);
            Assert.Equal(DataRowState.Added, r.RowState);

            r.Delete();

            Assert.Equal(0, ft.Rows.Count);
            Assert.Equal(DataRowState.Detached, r.RowState);
        }

        [Fact]
        public void DoubleDelete_Silenzioso()
        {
            // FastData extension: second Delete is a no-op (System.Data would throw).
            var ft = Build();
            var r = ft.Rows[0];
            r.Delete();
            r.Delete(); // must not throw
            Assert.Equal(DataRowState.Deleted, r.RowState);
        }

        [Fact]
        public void DeleteSuDetached_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.NewRow();
            Assert.Throws<InvalidOperationException>(() => r.Delete());
        }

        [Fact]
        public void ModificaSuRigaDeleted_Lancia()
        {
            var ft = Build();
            var r = ft.Rows[0];
            r.Delete();
            Assert.Throws<InvalidOperationException>(() => r[0] = 5);
        }

        [Fact]
        public void AcceptChanges_RigheAddedDiventanoUnchanged()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.NewRow();
            r[0] = 1;
            ft.Rows.Add(r);
            Assert.Equal(DataRowState.Added, r.RowState);
            ft.AcceptChanges();
            Assert.Equal(DataRowState.Unchanged, r.RowState);
        }

        [Fact]
        public void Clear_RimuoveTutteEStatoDetached_ComeSystemData()
        {
            // System.Data: Clear -> existing rows become Detached.
            var ft = Build();
            var held = ft.Rows[0];
            ft.Rows.Clear();
            Assert.Equal(0, ft.Rows.Count);
            Assert.Equal(DataRowState.Detached, held.RowState);
        }

        [Fact]
        public void RigheDeletedEscluseDaSelectEAggregati()
        {
            var ft = Build();
            ft.Rows[0].Delete();
            Assert.Equal(3, ft.Select().Length);
            Assert.Equal(3, (int)ft.Compute("COUNT(*)", null));
        }

        [Fact]
        public void RigheDeletedIncluseNellaEnumerazione()
        {
            // System.Data: AsEnumerable/Enumerator include Deleted rows.
            var ft = Build();
            ft.Rows[0].Delete();
            Assert.Equal(4, ft.AsEnumerable().Count());
        }

        [Fact]
        public void AssignIdentico_NonCambiaStato()
        {
            var ft = Build();
            var r = ft.Rows[0];
            r[0] = 0; // same value
            Assert.Equal(DataRowState.Unchanged, r.RowState);
        }

        [Fact]
        public void AssignCasoDiverso_CambiaStato_EAggiornaEspressioni()
        {
            // P0: "Roma" -> "ROMA" must not be ignored by the case-insensitive comparer
            // used to detect value changes in SetValueCore.
            var ft = new FastDataTable();
            ft.Columns.Add("City", typeof(string));
            var g = ft.Columns.Add("Greet", typeof(string));
            g.Expression = "City + '!'";

            var r = ft.Rows.Add("Roma");
            ft.AcceptChanges();
            Assert.Equal("Roma!", r["Greet"]);

            r["City"] = "ROMA";

            Assert.Equal(DataRowState.Modified, r.RowState);
            Assert.Equal("ROMA!", r["Greet"]);
        }

        [Fact]
        public void DataSetAcceptChanges_PropagatoATutteLeTabelle()
        {
            var ds = new FastDataSet();
            var t1 = new FastDataTable("T1");
            t1.Columns.Add("A", typeof(int));
            var t2 = new FastDataTable("T2");
            t2.Columns.Add("B", typeof(int));
            ds.Tables.Add(t1);
            ds.Tables.Add(t2);
            t1.Rows.Add(1);
            t2.Rows.Add(2);

            ds.AcceptChanges();

            Assert.Equal(DataRowState.Unchanged, t1.Rows[0].RowState);
            Assert.Equal(DataRowState.Unchanged, t2.Rows[0].RowState);
        }
    }
}
