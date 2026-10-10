using System;
using System.Linq;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class MergeCloneCopyTests
    {
        private static FastDataTable WithPk()
        {
            var ft = new FastDataTable();
            var id = ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));
            ft.PrimaryKey = new[] { id };
            return ft;
        }

        [Fact]
        public void Clone_CopiaSchemaESenzaRighe()
        {
            var ft = Builders.BuildFd();
            var c = ft.Clone();
            Assert.Equal(ft.Columns.Count, c.Columns.Count);
            Assert.Equal(0, c.Rows.Count);
            for (int i = 0; i < ft.Columns.Count; i++)
                Assert.Equal(ft.Columns[i].DataType, c.Columns[i].DataType);
        }

        [Fact]
        public void Clone_MantieneChiavePrimaria()
        {
            var ft = WithPk();
            ft.Rows.Add(1, "a");
            var c = ft.Clone();
            Assert.NotNull(c.PrimaryKey);
            Assert.Single(c.PrimaryKey);
            Assert.Equal("Id", c.PrimaryKey[0].ColumnName);
        }

        [Fact]
        public void Clone_MantieneColonneEspressioni()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var t = ft.Columns.Add("Tot", typeof(int));
            t.Expression = "A * 2";
            ft.Rows.Add(21);
            var c = ft.Clone();
            Assert.False(string.IsNullOrWhiteSpace(c.Columns["Tot"].Expression));
        }

        [Fact]
        public void MergeSenzaPK_AggiungeRighe()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));
            ft.Rows.Add(1, "a");
            ft.Rows.Add(2, "b");

            var src = new FastDataTable();
            src.Columns.Add("Id", typeof(int));
            src.Columns.Add("V", typeof(string));
            src.Rows.Add(3, "c");
            src.Rows.Add(4, "d");

            ft.Merge(src);
            Assert.Equal(4, ft.Rows.Count);
        }

        [Fact]
        public void MergeConPK_AggiorneRigheEsistenti()
        {
            var ft = WithPk();
            ft.Rows.Add(1, "vecchio");
            ft.Rows.Add(2, "b");

            var src = WithPk();
            src.Rows.Add(1, "nuovo");
            src.Rows.Add(3, "c");

            ft.Merge(src);

            Assert.Equal(3, ft.Rows.Count);
            var r1 = ft.Select("Id = 1")[0];
            Assert.Equal("nuovo", r1["V"]);
        }

        [Fact]
        public void Merge_SincronizzaSchemaColonneMananti()
        {
            // with PK: row Id=1 already exists -> upsert (without PK, Merge appends)
            var ft = WithPk();
            ft.Rows.Add(1, "a");

            var src = new FastDataTable();
            src.Columns.Add("Id", typeof(int));
            src.Columns.Add("V", typeof(string));
            src.Columns.Add("Extra", typeof(string));
            src.PrimaryKey = new[] { src.Columns["Id"] };
            src.Rows.Add(1, "a", "ex");

            ft.Merge(src);
            Assert.True(ft.Columns.Contains("Extra"));
            Assert.Single(ft.Rows);
            Assert.Equal("ex", ft.Rows[0]["Extra"]);
        }

        [Fact]
        public void Merge_NonScriveSuColonneEspressioni()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var t = ft.Columns.Add("Tot", typeof(int));
            t.Expression = "A * 2";
            ft.Rows.Add(10);
            Assert.Equal(20, ft.Rows[0]["Tot"]);

            var src = new FastDataTable();
            src.Columns.Add("A", typeof(int));
            src.Columns.Add("Tot", typeof(int)); // explicit value in the source
            src.Rows.Add(10, 999);

            ft.Merge(src);
            // the Tot column stays computed: 10 * 2
            Assert.Equal(20, ft.Rows[0]["Tot"]);
        }

        [Fact]
        public void Merge_IgnoresRigheDeletedSorgente()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("Id", typeof(int));
            ft.Columns.Add("V", typeof(string));

            var src = new FastDataTable();
            src.Columns.Add("Id", typeof(int));
            src.Columns.Add("V", typeof(string));
            src.Rows.Add(1, "keep");
            var dead = src.Rows.Add(2, "dead");
            dead.Delete();

            ft.Merge(src);
            Assert.Equal(1, ft.Rows.Count);
            Assert.Equal(1, ft.Rows[0]["Id"]);
        }

        [Fact]
        public void Copy_CopiaProfondaRigheESchema()
        {
            // P1: deep copy (Clone + rows). System.Data's Clone is schema only.
            var ft = WithPk();
            ft.Rows.Add(1, "a");
            ft.Rows.Add(2, "b");
            ft.AcceptChanges();

            var c = ft.Copy();
            Assert.Equal(2, c.Rows.Count);
            Assert.Equal("a", c.Rows[0]["V"]);
            Assert.NotNull(c.PrimaryKey);

            // independence: modifying the original does not touch the copy
            ft.Rows[0]["V"] = "mutato";
            Assert.Equal("a", c.Rows[0]["V"]);
        }
    }
}
