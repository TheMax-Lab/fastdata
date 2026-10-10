using System;
using System.Data;
using RCM.FastData;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class ExpressionEngineTests
    {
        [Fact]
        public void AritmeticaDiBase()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Columns.Add("B", typeof(int));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "(A + B) * 2 - 1";
            ft.Rows.Add(3, 4);
            Assert.Equal(13, ft.Rows[0]["E"]);
        }

        [Fact]
        public void ConcatenazioneStringhe()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(string));
            ft.Columns.Add("B", typeof(string));
            var e = ft.Columns.Add("E", typeof(string));
            e.Expression = "A + ' ' + B";
            ft.Rows.Add("Fast", "Data");
            Assert.Equal("Fast Data", ft.Rows[0]["E"]);
        }

        [Fact]
        public void CatenaccioTopologico()
        {
            // B depends on A, C depends on B: automatic computation order
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var b = ft.Columns.Add("B", typeof(int));
            var c = ft.Columns.Add("C", typeof(int));
            b.Expression = "A * 2";
            c.Expression = "B + 1";
            ft.Rows.Add(10);
            Assert.Equal(20, ft.Rows[0]["B"]);
            Assert.Equal(21, ft.Rows[0]["C"]);
        }

        [Fact]
        public void RicampionamentoSuModifica()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "A * 10";
            var r = ft.Rows.Add(1);
            Assert.Equal(10, r["E"]);
            r["A"] = 2;
            Assert.Equal(20, r["E"]);
        }

        [Fact]
        public void FunzioniIsnullEIf()
        {
            // ISNULL in the engine takes 2 arguments: ISNULL(expr, default)
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var e = ft.Columns.Add("E", typeof(string));
            e.Expression = "IIF(ISNULL(A, 0) = 0, 'vuoto', 'pieno')";
            var r1 = ft.Rows.Add(5);
            Assert.Equal("pieno", r1["E"]);
            var r2 = ft.Rows.Add(DBNull.Value);
            Assert.Equal("vuoto", r2["E"]);
        }

        [Fact]
        public void FunzioniStringaELunghezza()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("S", typeof(string));
            var u = ft.Columns.Add("U", typeof(string));
            var l = ft.Columns.Add("L", typeof(int));
            u.Expression = "UPPER(S)";
            l.Expression = "LEN(S)";
            ft.Rows.Add("ab");
            Assert.Equal("AB", ft.Rows[0]["U"]);
            Assert.Equal(2, ft.Rows[0]["L"]);
        }

        [Fact]
        public void FunzioneRound()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("D", typeof(double));
            var e = ft.Columns.Add("E", typeof(double));
            e.Expression = "ROUND(D, 1)";
            ft.Rows.Add(1.26d);
            Assert.Equal(1.3d, ft.Rows[0]["E"]);
        }

        [Fact]
        public void ScrivereSuColonnaEspressione_Lancia()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "A * 2";
            var r = ft.Rows.Add(1);
            Assert.Throws<InvalidOperationException>(() => r["E"] = 99);
        }

        [Fact]
        public void PropagazioneNULL()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "A + 1";
            var r = ft.Rows.Add(DBNull.Value);
            Assert.Equal(DBNull.Value, r["E"]);
        }

        [Fact]
        public void EspressioneCiclica_RilevataAlPrimoCalcolo()
        {
            // the engine validates at the first computation (lazy): the B<->C cycle throws in
            // the topological sort (no hang: Kahn), at the moment the value is needed
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var b = ft.Columns.Add("B", typeof(int));
            var c = ft.Columns.Add("C", typeof(int));
            b.Expression = "C + 1";
            c.Expression = "B + 1";
            Assert.ThrowsAny<Exception>(() => ft.Rows.Add(5));
        }

        [Fact]
        public void RiferimentoColonnaAssente_LanciaAlPrimoCalcolo()
        {
            // lazy validation: the missing reference is detected at the first computation
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var e = ft.Columns.Add("E", typeof(int));
            e.Expression = "Assente * 2";
            Assert.ThrowsAny<Exception>(() => ft.Rows.Add(5));
        }
    }
}
