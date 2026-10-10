using System;
using System.Linq;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using RCM.FastData.Shared;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class SortTests
    {
        private static FastDataTable Build() { return Builders.BuildFd(); }

        [Fact]
        public void OrdineCrescenteChiaveUnivoca()
        {
            var ids = Build().Select(null, "Id").Select(r => (int)r[BenchData.ColId]);
            Assert.Equal(Enumerable.Range(1, Builders.TestRows), ids);
        }

        [Fact]
        public void OrdineDecrescente()
        {
            var ids = Build().Select(null, "Id DESC").Select(r => (int)r[BenchData.ColId]);
            Assert.Equal(Enumerable.Range(1, Builders.TestRows).Reverse(), ids);
        }

        [Fact]
        public void Multicolonna()
        {
            var ids = Build().Select(null, "Code, Id DESC")
                .Select(r => r[BenchData.ColCode] + "-" + r[BenchData.ColId]);
            // for each Code (C0..C6), Ids in descending order
            string[] expected = new string[Builders.TestRows];
            for (int i = 0; i < expected.Length; i++) expected[i] = null;
            var byCode = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (var r in Build().Select(null))
            {
                string code = (string)r[BenchData.ColCode];
                int id = (int)r[BenchData.ColId];
                if (!byCode.ContainsKey(code)) byCode[code] = new System.Collections.Generic.List<int>();
                byCode[code].Add(id);
            }
            int pos = 0;
            foreach (var code in byCode.Keys.OrderBy(k => k))
            {
                foreach (var id in byCode[code].OrderByDescending(x => x))
                    expected[pos++] = code + "-" + id;
            }
            Assert.Equal(expected, ids.ToArray());
        }

        [Fact]
        public void SortConFiltro()
        {
            var ids = Build().Select("Id < 10", "Id DESC").Select(r => (int)r[BenchData.ColId]);
            Assert.Equal(new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, ids);
        }

        [Fact]
        public void SortColonnaAssente_Lancia()
        {
            Assert.ThrowsAny<Exception>(() => Build().Select(null, "Assente ASC"));
        }

        [Fact]
        public void SortStringhe_CaseInsensitive()
        {
            // like System.Data with the default collation (case-insensitive)
            var ft = new FastDataTable();
            ft.Columns.Add("S", typeof(string));
            ft.Columns.Add("K", typeof(int));
            ft.Rows.Add("beta", 1);
            ft.Rows.Add("Alpha", 2);
            ft.Rows.Add("GAMMA", 3);
            var order = ft.Select(null, "S").Select(r => (string)r[0]).ToArray();
            Assert.Equal(new[] { "Alpha", "beta", "GAMMA" }, order);
        }

        [Fact]
        public void TabellaVuotaESingolaRiga()
        {
            var empty = new FastDataTable();
            empty.Columns.Add("A", typeof(int));
            Assert.Empty(empty.Select(null, "A"));

            var one = new FastDataTable();
            one.Columns.Add("A", typeof(int));
            one.Rows.Add(5);
            Assert.Single(one.Select("A = 5", "A DESC"));
        }
    }
}
