using System;
using RCM.FastData.Tests.Tools;
using Xunit;
using OriginalFastDataTable = RCM.FastData.Original.FastDataTable;
using OriginalFastDataSet = RCM.FastData.Original.FastDataSet;
// NB: inside namespace RCM.FastData.Tests.* the simple name "DataRowState" resolves
// to the correct library (RCM.FastData), not the original: it is always qualified here.
using OState = RCM.FastData.Original.DataRowState;

namespace RCM.FastData.Tests.Core
{
    /// <summary>
    /// Pins the known defects of the ORIGINAL (namespace RCM.FastData.Original,
    /// immutable snapshot). These tests PASS while the original remains defective:
    /// they document the wrong behavior. The correct library (RCM.FastData) has the
    /// mirror tests with the expected behavior.
    /// </summary>
    public class OriginalKnownDefectTests
    {
        [Fact]
        public void Compute_SumDecimal_PerdePrecisione()
        {
            var ft = new OriginalFastDataTable();
            ft.Columns.Add("D", typeof(decimal));
            ft.Rows.Add(0.1m);
            ft.Rows.Add(0.2m);
            ft.Rows.Add(0.3m);

            object res = ft.Compute("SUM(D)", null);

            // defect: aggregation in double, returned type double, 0.1+0.2+0.3 != 0.6
            Assert.IsType<double>(res);
            Assert.NotEqual(0.6d, (double)res);
            Assert.Equal(0.1d + 0.2d + 0.3d, (double)res);
        }

        [Fact]
        public void Filtri_ColonneFraParentesiQuadre_Rotati()
        {
            var ft = new OriginalFastDataTable();
            ft.Columns.Add("Con Spazio", typeof(int));
            ft.Rows.Add(1);

            RCM.FastData.Original.FastDataRow[] rows;
            try
            {
                rows = ft.Select("[Con Spazio] = 1");
            }
            catch (Exception)
            {
                return; // throwing is already the defect (regex "$" instead of "\[")
            }

            // if it doesn't throw, then the filter is still inoperative
            foreach (var r in rows)
                Assert.NotEqual(1, (int)r["Con Spazio"]);
        }

        [Fact]
        public void AssegnazioneDifferenteSoloPerCase_Ignorata()
        {
            var ft = new OriginalFastDataTable();
            ft.Columns.Add("City", typeof(string));
            var g = ft.Columns.Add("Greet", typeof(string));
            g.Expression = "City + '!'";

            var r = ft.Rows.Add("Roma");
            ft.AcceptChanges();
            r["City"] = "ROMA";

            // defect: case-insensitive comparer in SetValueCore -> no change
            Assert.Equal(OState.Unchanged, r.RowState);
            Assert.Equal("Roma!", r["Greet"]);
        }

        // NB: the suspected P0 "Field<T> Nullable broken" was DISPROVEN by the test:
        // Convert.ChangeType(value, typeof(T), Inv) handles Nullable types in
        // .NET Framework 4.8 and .NET 8. There is no defect to fix.

        [Fact]
        public void Clear_NonPassaRigheADetached()
        {
            var ft = new OriginalFastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Rows.Add(1);
            ft.AcceptChanges();
            var held = ft.Rows[0];

            ft.Rows.Clear();

            // defect: System.Data puts rows in Detached; the original does not
            Assert.NotEqual(OState.Detached, held.RowState);
            Assert.Equal(OState.Unchanged, held.RowState);
        }

        [Fact]
        public void DeleteSuRigaAdded_NonRimuoveImmediatamente()
        {
            var ft = new OriginalFastDataTable();
            ft.Columns.Add("A", typeof(int));
            var r = ft.Rows.Add(1);

            r.Delete();

            // defect: System.Data removes the Added row immediately and puts it in Detached
            Assert.Equal(1, ft.Rows.Count);
            Assert.Equal(OState.Deleted, r.RowState);
        }

        [Fact]
        public void Constraint_FigliaOrfana_NonBloccata()
        {
            var ds = new OriginalFastDataSet();
            var parent = new OriginalFastDataTable("P");
            parent.Columns.Add("Id", typeof(int));
            ds.Tables.Add(parent);
            var child = new OriginalFastDataTable("C");
            child.Columns.Add("Id", typeof(int));
            child.Columns.Add("Pid", typeof(int));
            ds.Tables.Add(child);
            ds.Relations.Add("Rel", parent.Columns["Id"], child.Columns["Pid"]); // createConstraints=true

            // defect: createConstraints is ignored -> the orphaned child is accepted
            var orphan = child.Rows.Add(1, 99);
            Assert.NotNull(orphan);
        }

        [Fact]
        public void Compute_SenzaParentesi_StackOverflow_InProcessoSeparato()
        {
            // Critical P0: Compute("COUNT") (without parentheses) falls into the fallback
            // `new DataTable().Compute(...)` with alias DataTable=FastDataTable ->
            // infinite recursion -> StackOverflowException -> process crash.
            // Verified in a separate process (CrashProbe): the test host cannot
            // survive an SOE.
            int exit = CrashProbeRunner.Run("original-compute-soe");
            Assert.NotEqual(0, exit); // the process crashes (SOE exit code)
        }
    }
}
