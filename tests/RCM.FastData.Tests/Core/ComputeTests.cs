using System;
using System.Data;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class ComputeTests
    {
        private static FastDataTable Build() { return Builders.BuildFd(); }

        [Fact]
        public void CountAsterisco()
        {
            Assert.Equal(Builders.TestRows, (int)Build().Compute("COUNT(*)", null));
        }

        [Fact]
        public void CountColonna_SaltaNULL()
        {
            int expected = 0;
            for (int i = 0; i < Builders.TestRows; i++) if (i % 13 != 0) expected++;
            Assert.Equal(expected, (int)Build().Compute("COUNT(Qty)", null));
        }

        [Fact]
        public void CountConFiltro()
        {
            Assert.Equal(10, (int)Build().Compute("COUNT(*)", "Id <= 10"));
        }

        [Fact]
        public void SumInt_RitornaLong_ComeSystemData()
        {
            // System.Data: SUM on an int32 column returns Int64 (verified on the target).
            object res = Build().Compute("SUM(Id)", null);
            Assert.IsType<long>(res);
            long expected = 0;
            for (int i = 1; i <= Builders.TestRows; i++) expected += i;
            Assert.Equal(expected, (long)res);
        }

        [Fact]
        public void SumDecimal_PrecisioneEsatta()
        {
            // P0: the original computes in double -> 0.1 + 0.2 != 0.3
            var ft = Builders.BuildTypedFd();
            object res = ft.Compute("SUM(D)", null);
            Assert.IsType<decimal>(res);
            Assert.Equal(0.6m, (decimal)res);
        }

        [Fact]
        public void SumLongOltreDueAlQuadratoCinquantatresimo()
        {
            // P0: long > 2^53: in double the values collide
            var ft = Builders.BuildTypedFd();
            object res = ft.Compute("SUM(L)", null);
            Assert.IsType<long>(res);
            Assert.Equal(9007199254740993L + 9007199254740991L + 9007199254740992L, (long)res);
        }

        [Fact]
        public void AvgInt_RitornaIntComeSystemData()
        {
            // Phase D (D2, probe on System.Data): AVG on an int32 column -> Int32 with
            // truncating integer division: 1..200 -> 20100/200 = 100.5 -> 100.
            object res = Build().Compute("AVG(Id)", null);
            Assert.IsType<int>(res);
            Assert.Equal(100, (int)res);
        }

        [Fact]
        public void MinMax()
        {
            Assert.Equal(1, (int)Build().Compute("MIN(Id)", null));
            Assert.Equal(Builders.TestRows, (int)Build().Compute("MAX(Id)", null));
        }

        [Fact]
        public void MinMaxStringhe()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("S", typeof(string));
            ft.Rows.Add("beta");
            ft.Rows.Add("Alpha");
            ft.Rows.Add("GAMMA");
            Assert.Equal("Alpha", (string)ft.Compute("MIN(S)", null));
            Assert.Equal("GAMMA", (string)ft.Compute("MAX(S)", null));
        }

        [Fact]
        public void TabellaVuota_ConteZero_AggregatiDBNull()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            Assert.Equal(0, (int)ft.Compute("COUNT(*)", null));
            Assert.Equal(0, (int)ft.Compute("COUNT(A)", null));
            Assert.Equal(DBNull.Value, ft.Compute("SUM(A)", null));
            Assert.Equal(DBNull.Value, ft.Compute("AVG(A)", null));
            Assert.Equal(DBNull.Value, ft.Compute("MIN(A)", null));
            Assert.Equal(DBNull.Value, ft.Compute("MAX(A)", null));
        }

        [Fact]
        public void ColonnaTuttiNULL()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Rows.Add(DBNull.Value);
            ft.Rows.Add(DBNull.Value);
            Assert.Equal(0, (int)ft.Compute("COUNT(A)", null));
            Assert.Equal(2, (int)ft.Compute("COUNT(*)", null));
            Assert.Equal(DBNull.Value, ft.Compute("SUM(A)", null));
            Assert.Equal(DBNull.Value, ft.Compute("AVG(A)", null));
        }

        [Fact]
        public void FiltroSenzaMatch()
        {
            Assert.Equal(0, (int)Build().Compute("COUNT(*)", "Id > 9999"));
            Assert.Equal(DBNull.Value, Build().Compute("SUM(Qty)", "Id > 9999"));
        }

        [Fact]
        public void ArgomentoConParentesiQuadreESpazi()
        {
            Assert.Equal(Build().Compute("SUM(Qty)", null), Build().Compute("SUM( [Qty] )", null));
        }

        [Fact]
        public void FunzioneNonSupportata_Lancia()
        {
            Assert.Throws<NotSupportedException>(() => Build().Compute("STDEV(Qty)", null));
        }

        [Fact]
        public void EspressioneSenzaParentesi_LanciaESeNonCrolla_InProcessoSeparato()
        {
            // P0: the original falls into infinite recursion (StackOverflow, process crashing)
            // due to the fallback `new DataTable().Compute(...)` with alias DataTable=FastDataTable.
            // Contract: NotSupportedException, never a process crash.
            // Verified in a separate process: in-process an SOE would kill the test host.
            int exit = RCM.FastData.Tests.Tools.CrashProbeRunner.Run("fixed-compute-no-parens");
            Assert.Equal(0, exit);
        }

        [Fact]
        public void EspressioneConParentesiMaFunzioneSconosciuta_LanciaInProcesso()
        {
            // safe in-process case: unknown function WITH parentheses -> NotSupportedException
            Assert.ThrowsAny<NotSupportedException>(() => Build().Compute("STDEV(Qty)", null));
        }

        [Fact]
        public void ComputeSullExpressionColumn()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("A", typeof(int));
            var tot = ft.Columns.Add("Tot", typeof(int));
            tot.Expression = "A * 2";
            ft.Rows.Add(1);
            ft.Rows.Add(2);
            ft.Rows.Add(3);
            // Phase D (D2): SUM on an integer column -> Int64, like System.Data
            // (the original test's (int) cast was a contract of the original library).
            Assert.Equal(12L, ft.Compute("SUM(Tot)", null));
        }
    }
}
