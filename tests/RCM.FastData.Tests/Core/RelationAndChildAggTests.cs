using System;
using System.Data;
using System.Linq;
using RCM.FastData;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class RelationAndChildAggTests
    {
        private sealed class Fixture
        {
            public FastDataSet Ds;
            public FastDataTable Parent;
            public FastDataTable Child;
            public FastDataRelation Rel;

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Ds = new FastDataSet();

                f.Parent = new FastDataTable("Parent");
                f.Parent.Columns.Add("Id", typeof(int));
                f.Parent.Columns.Add("Name", typeof(string));
                f.Ds.Tables.Add(f.Parent);

                f.Child = new FastDataTable("Child");
                f.Child.Columns.Add("Id", typeof(int));
                f.Child.Columns.Add("ParentId", typeof(int));
                f.Child.Columns.Add("Amt", typeof(decimal));
                f.Ds.Tables.Add(f.Child);

                f.Rel = f.Ds.Relations.Add("RelPC", f.Parent.Columns["Id"], f.Child.Columns["ParentId"]);
                return f;
            }
        }

        [Fact]
        public void GetChildRows_FiltraPerChiave()
        {
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            f.Parent.Rows.Add(2, "p2");
            f.Child.Rows.Add(10, 1, 1.5m);
            f.Child.Rows.Add(11, 1, 2.5m);
            f.Child.Rows.Add(12, 2, 3.5m);

            var children = f.Parent.Rows[0].GetChildRows("RelPC");
            Assert.Equal(2, children.Length);
            Assert.All(children, c => Assert.Equal(1, c["ParentId"]));
            Assert.Single(f.Parent.Rows[1].GetChildRows("RelPC"));
        }

        [Fact]
        public void GetChildRows_AggiornatoDaInserimento()
        {
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            f.Child.Rows.Add(10, 1, 1m);
            Assert.Single(f.Parent.Rows[0].GetChildRows("RelPC"));
            f.Child.Rows.Add(11, 1, 2m);
            Assert.Equal(2, f.Parent.Rows[0].GetChildRows("RelPC").Length);
        }

        [Fact]
        public void GetChildRows_AggiornatoDaCambioChiave()
        {
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            f.Parent.Rows.Add(2, "p2");
            var c = f.Child.Rows.Add(10, 1, 1m);
            Assert.Single(f.Parent.Rows[0].GetChildRows("RelPC"));
            c["ParentId"] = 2;
            Assert.Empty(f.Parent.Rows[0].GetChildRows("RelPC"));
            Assert.Single(f.Parent.Rows[1].GetChildRows("RelPC"));
        }

        [Fact]
        public void GetChildRows_AggiornatoDaEliminazione()
        {
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            var c = f.Child.Rows.Add(10, 1, 1m);
            Assert.Single(f.Parent.Rows[0].GetChildRows("RelPC"));
            c.Delete();
            Assert.Empty(f.Parent.Rows[0].GetChildRows("RelPC"));
        }

        [Fact]
        public void AggregatoChild_Sum_DecimalEsatto()
        {
            var f = Fixture.Create();
            var tot = f.Parent.Columns.Add("Total", typeof(decimal));
            tot.Expression = "SUM(Child(RelPC).Amt)";

            f.Parent.Rows.Add(1, "p1");
            f.Child.Rows.Add(10, 1, 0.1m);
            f.Child.Rows.Add(11, 1, 0.2m);

            Assert.Equal(0.3m, f.Parent.Rows[0]["Total"]);
        }

        [Fact]
        public void AggregatoChild_Sum_AggiornatoDynamicamente()
        {
            var f = Fixture.Create();
            var tot = f.Parent.Columns.Add("Total", typeof(decimal));
            tot.Expression = "SUM(Child(RelPC).Amt)";

            f.Parent.Rows.Add(1, "p1");
            f.Child.Rows.Add(10, 1, 1m);
            Assert.Equal(1m, f.Parent.Rows[0]["Total"]);

            var c = f.Child.Rows.Add(11, 1, 2m);
            Assert.Equal(3m, f.Parent.Rows[0]["Total"]);

            c["Amt"] = 5m;
            Assert.Equal(6m, f.Parent.Rows[0]["Total"]);

            c.Delete();
            Assert.Equal(1m, f.Parent.Rows[0]["Total"]);
        }

        [Fact]
        public void AggregatoChild_CountESenzaFigli()
        {
            var f = Fixture.Create();
            var n = f.Parent.Columns.Add("N", typeof(int));
            n.Expression = "COUNT(Child(RelPC).Amt)";

            f.Parent.Rows.Add(1, "p1");
            f.Parent.Rows.Add(2, "p2");
            Assert.Equal(0, f.Parent.Rows[0]["N"]);

            f.Child.Rows.Add(10, 1, 1m);
            f.Child.Rows.Add(11, 1, 2m);
            Assert.Equal(2, f.Parent.Rows[0]["N"]);
            Assert.Equal(0, f.Parent.Rows[1]["N"]);
        }

        [Fact]
        public void ChildFuoriDaAggregato_LanciaAlPrimoCalcolo()
        {
            // lazy validation: Child(...) outside SUM/AVG/MIN/MAX/COUNT is
            // rejected when the column value is to be computed
            var f = Fixture.Create();
            var c = f.Parent.Columns.Add("Bad", typeof(decimal));
            c.Expression = "Child(RelPC).Amt"; // not inside SUM/COUNT
            Assert.ThrowsAny<Exception>(() => f.Parent.Rows.Add(1, "p1"));
        }

        [Fact]
        public void Relazioni_StessoNome_Lancia()
        {
            var f = Fixture.Create();
            Assert.ThrowsAny<Exception>(() =>
                f.Ds.Relations.Add("RelPC", f.Parent.Columns["Id"], f.Child.Columns["Id"]));
        }

        [Fact]
        public void Constraint_FigliaOrfana_Bloccata()
        {
            // P0: createConstraints=true (default) must prevent children without an existing parent.
            // (Corrected in Phase D: the parent row Id=1 is missing; without it the first
            //  insertion would itself be an orphan and no coherent constraint -
            //  including System.Data's - would let both rows through.
            //  The "parent before child" pattern is the one used by all the other tests.)
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            f.Child.Rows.Add(10, 1, 1m);
            Assert.ThrowsAny<Exception>(() => f.Child.Rows.Add(11, 99, 1m));
        }

        [Fact]
        public void Constraint_CancellaGenitoreConFigli_Bloccata()
        {
            // P0: with createConstraints=true, deleting a parent with children must throw.
            var f = Fixture.Create();
            f.Parent.Rows.Add(1, "p1");
            f.Child.Rows.Add(10, 1, 1m);
            f.Parent.AcceptChanges();
            Assert.ThrowsAny<Exception>(() => f.Parent.Rows[0].Delete());
        }
    }
}
