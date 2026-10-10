using System;
using System.Data;
using System.Linq;
using System.Threading;
using RCM.FastData;
using RCM.FastData.Tests.Helpers;
using RCM.FastData.Shared;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    public class FilterTests
    {
        private static FastDataTable Build() { return Builders.BuildFd(); }
        private static int[] Ids(string filter) { return Build().Select(filter).Select(r => (int)r[BenchData.ColId]).ToArray(); }

        [Fact]
        public void UguaglianzaInt()
        {
            Assert.Equal(new[] { 5 }, Ids("Id = 5"));
        }

        [Fact]
        public void UguaglianzaStringa_CaseInsensitive()
        {
            // System.Data uses a case-insensitive collation by default
            Assert.Equal(Ids("City = 'Roma'"), Ids("City = 'roma'"));
            Assert.NotEmpty(Ids("City = 'Roma'"));
        }

        [Fact]
        public void UguaglianzaDecimal()
        {
            var ids = Ids("Price = 1.25");
            Assert.NotEmpty(ids);
            // 1.25 = (i % 100 == 1) * 1.25
            int n = 0;
            for (int i = 0; i < Builders.TestRows; i++) if (i % 100 == 1) n++;
            Assert.Equal(n, ids.Length);
        }

        [Fact]
        public void UguaglianzaBool_LetteraleTrue()
        {
            var ids = Ids("Flag = True");
            Assert.NotEmpty(ids);
            Assert.All(ids, id => Assert.True((id - 1) % 2 == 0));
        }

        [Fact]
        public void UguaglianzaDateTime_ConVincette()
        {
            var target = new DateTime(2020, 1, 1, 9, 30, 0).AddMonths(3);
            var ids = Ids("Date = #" + target.ToString("yyyy-MM-dd HH:mm") + "#");
            Assert.NotEmpty(ids);
            Assert.All(ids, id => Assert.Equal(target, new DateTime(2020, 1, 1, 9, 30, 0).AddDays((id - 1) % 366)));
        }

        [Fact]
        public void IntervalliECombinazioni()
        {
            var a = Ids("Id > 10 AND Id < 15");
            Assert.Equal(new[] { 11, 12, 13, 14 }, a);

            var b = Ids("Id = 1 OR Id = 2");
            Assert.Equal(new[] { 1, 2 }, b);

            var c = Ids("NOT (Id = 1)");
            Assert.Equal(199, c.Length);
            Assert.DoesNotContain(1, c);
        }

        [Fact]
        public void PrecedenzaANDprimaOR()
        {
            // (A AND B) OR C
            var ids = Ids("Id = 1 AND Id = 2 OR Id = 3");
            Assert.Equal(new[] { 3 }, ids);
        }

        [Fact]
        public void ParentesiDoppie()
        {
            Assert.Equal(Ids("Id = 5"), Ids("((Id = 5))"));
        }

        [Fact]
        public void IsNull_EIsNotNull()
        {
            var nullIds = Ids("City IS NULL");
            Assert.NotEmpty(nullIds);
            Assert.All(nullIds, id => Assert.True((id - 1) % 10 == 5));

            var notNull = Ids("City IS NOT NULL");
            Assert.Equal(Builders.TestRows - nullIds.Length, notNull.Length);
        }

        [Fact]
        public void NotIsNull_Equivalente()
        {
            Assert.Equal(Ids("City IS NOT NULL"), Ids("NOT (City IS NULL)"));
        }

        [Fact]
        public void NULLNeiConfronti_SempreFalso()
        {
            // System.Data: comparisons with NULL are unknown -> excluded
            var ids = Ids("City <> 'Roma'");
            Assert.All(ids, id => Assert.False((id - 1) % 10 == 5)); // no NULL included
        }

        [Fact]
        public void Like_Pattern()
        {
            // Phase D (D5): LIKE semantics = System.Data contract (probe 2026-10-09,
            // net48 and net8.0 identical) — NOT SQL semantics:
            // % and * wildcards only at start/end, _ ? literals, [c] = single character.
            Assert.NotEmpty(Ids("Name LIKE 'Al%'"));
            Assert.Empty(Ids("Name LIKE 'Zx%'"));
            Assert.NotEmpty(Ids("City LIKE 'R%'"));
            Assert.NotEmpty(Ids("City LIKE '%oma'"));   // wildcard at the start of the pattern
            Assert.NotEmpty(Ids("City LIKE 'R*'"));     // * wildcard at the end of the pattern
            Assert.Empty(Ids("City LIKE 'R___'"));      // _ literal, not a wildcard
            Assert.Empty(Ids("Name LIKE 'Al?%'"));      // ? literal, not a wildcard
            Assert.NotEmpty(Ids("Name LIKE 'A[l]pha'")); // [c] = single character: Alpha
            Assert.Empty(Ids("Name LIKE 'Alx[h]a'"));    // valid class, no match
            // wildcard sequences at the end of the pattern: valid (probe on System.Data)
            Assert.NotEmpty(Ids("Name LIKE 'Al*%'"));
            Assert.NotEmpty(Ids("Name LIKE 'Al%%'"));
        }

        [Fact]
        public void Like_PatternNonValido_LanciaEvaluateException()
        {
            // Phase D (D5): System.Data throws EvaluateException at the time of
            // Select() (validation at parse, even on an empty table) for:
            // %/* in middle position, non-single-element [...] classes.
            Assert.Throws<EvaluateException>(() => Ids("City LIKE 'R%__'"));
            Assert.Throws<EvaluateException>(() => Ids("Name LIKE 'Al[h]p%x'")); // [c] ok, % in the middle
            Assert.Throws<EvaluateException>(() => Ids("Name LIKE 'Al[h]a*x'")); // class + * in the middle
            Assert.Throws<EvaluateException>(() => Ids("Name LIKE 'Al[hp]%'"));
            Assert.Throws<EvaluateException>(() => Ids("Name LIKE 'Al[-p]%'"));
            Assert.Throws<EvaluateException>(() => Ids("Name LIKE 'Al[h-p]%'"));

            // same validation even on an empty table (like System.Data)
            var vuota = new FastDataTable();
            vuota.Columns.Add("S", typeof(string));
            Assert.Throws<EvaluateException>(() => vuota.Select("S LIKE 'a%b'"));
        }

        [Fact]
        public void Like_Numerico()
        {
            // LIKE on a numeric column: comparison on the string representation
            var ids = Ids("Qty LIKE '1%'");
            Assert.NotEmpty(ids);
        }

        [Fact]
        public void ColonneFraParentesiQuadre_NeiFiltri()
        {
            // P0: the original has a Regex pattern with "$" instead of "\[": not supported.
            var ft = new FastDataTable();
            ft.Columns.Add("Con Spazio", typeof(int));
            ft.Columns.Add("AND", typeof(int)); // name == reserved word
            ft.Rows.Add(1, 10);
            ft.Rows.Add(2, 20);

            Assert.Equal(new[] { 1 }, ft.Select("[Con Spazio] = 1").Select(r => (int)r[0]));
            Assert.Equal(new[] { 2 }, ft.Select("[AND] = 20").Select(r => (int)r["Con Spazio"]));
            Assert.Equal(new[] { 2 }, ft.Select("Con Spazio > 1").Select(r => (int)r[0]));
        }

        [Fact]
        public void ApostrofiNeiLetterali()
        {
            var ids = Ids("Name = 'O''Brien'");
            Assert.NotEmpty(ids);
            Assert.All(ids, id => Assert.True((id - 1) % 8 == 6));
        }

        [Fact]
        public void NULLLetterale_VersoStringaNull()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("S", typeof(string));
            ft.Rows.Add("NULL");   // literal string
            ft.Rows.Add(DBNull.Value); // real NULL

            var nullRows = ft.Select("S IS NULL");
            Assert.Single(nullRows);
            Assert.Equal(DBNull.Value, nullRows[0][0]);
            var asString = ft.Select("S = 'NULL'");
            Assert.Single(asString);
            Assert.Equal("NULL", asString[0][0]);
        }

        [Fact]
        public void NumeriNegativi()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("N", typeof(int));
            ft.Rows.Add(-5);
            ft.Rows.Add(5);
            Assert.Equal(new[] { -5 }, ft.Select("N = -5").Select(r => (int)r[0]));
            Assert.Equal(new[] { -5 }, ft.Select("N < 0").Select(r => (int)r[0]));
        }

        [Fact]
        public void NotazioneScientifica()
        {
            var ft = new FastDataTable();
            ft.Columns.Add("D", typeof(double));
            ft.Rows.Add(150.0d);
            ft.Rows.Add(1.0d);
            Assert.Equal(new[] { 150.0d }, ft.Select("D = 1.5E2").Select(r => (double)r[0]));
        }

        [Theory]
        [InlineData("it-IT")]
        [InlineData("en-US")]
        [InlineData("")]
        public void LetteraliNumerici_IndipendentiDallaCultura(string culture)
        {
            using (new CultureScope(culture))
            {
                var ft = new FastDataTable();
                ft.Columns.Add("D", typeof(double));
                ft.Columns.Add("M", typeof(decimal));
                ft.Rows.Add(0.5d, 1.25m);
                ft.Rows.Add(0.25d, 2.5m);

                Assert.Single(ft.Select("D = 0.5"));
                Assert.Single(ft.Select("M = 1.25"));
            }
        }

        [Fact]
        public void EspressioniNonSupportate_Lanciano()
        {
            var ft = Build();
            Assert.ThrowsAny<Exception>(() => ft.Select("Id ="));
            Assert.ThrowsAny<Exception>(() => ft.Select("= 5"));
            Assert.ThrowsAny<Exception>(() => ft.Select("ColonnaAssente = 1"));
            Assert.ThrowsAny<Exception>(() => ft.Select("Id LIKE"));
        }

        [Fact]
        public void FiltroVuoto_RitornaTutteLeRighe()
        {
            Assert.Equal(Builders.TestRows, Build().Select("").Length);
            Assert.Equal(Builders.TestRows, Build().Select(null).Length);
        }
    }
}
