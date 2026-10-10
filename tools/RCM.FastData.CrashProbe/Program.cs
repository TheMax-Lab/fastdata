using System;

namespace RCM.FastData.CrashProbe
{
    /// <summary>
    /// Separate process for scenarios at risk of StackOverflowException (an SOE is not
    /// catchable and would kill the test or benchmark host).
    ///
    /// Scenarios:
    ///   original-compute-soe      ORIGINAL FastDataTable: Compute("COUNT") without parentheses
    ///                             -> recursive fallback -> expected StackOverflow.
    ///   fixed-compute-no-parens   CORRECT FastDataTable: Compute("COUNT") without parentheses
    ///                             -> expected NotSupportedException, exit 0.
    ///
    /// Exit code: 0 = expected behavior verified; 1 = unexpected exception;
    ///            2 = expected behavior NOT verified; 64 = usage;
    ///            other (e.g. -2146233072 / 134) = process crash (SOE).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Diagnostic marker: which runtime the process is running on. The FullName of the
            // System.Object assembly is the reliable discriminator: mscorlib on .NET Framework,
            // System.Private.CoreLib on .NET Core (ImageRuntimeVersion is not: it returns
            // "4.0.30319" on both runtimes).
            Console.WriteLine("RUNTIME=" + typeof(object).Assembly.FullName
                + (Environment.Is64BitProcess ? " x64" : " x86")
                + " CORECLR=" + (AppContext.GetData("CORECLR") != null)
                + " EXE=" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            string scenario = args.Length > 0 ? args[0] : string.Empty;
            try
            {
                switch (scenario)
                {
                    case "original-compute-soe":
                        ComputeSenzaParentesiOriginale();
                        Console.WriteLine("NO-CRASH: l'originale NON è in StackOverflow (inesplicito)");
                        return 2;

                    case "fixed-compute-no-parens":
                        if (ComputeSenzaParentesiCorretto())
                        {
                            Console.WriteLine("OK: NotSupportedException come da contratto");
                            return 0;
                        }
                        Console.WriteLine("NO-EXCEPTION: la libreria corretta non ha lanciato (inesplicito)");
                        return 2;

                    default:
                        Console.WriteLine("Usage: RCM.FastData.CrashProbe.exe <original-compute-soe|fixed-compute-no-parens>");
                        return 64;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:" + ex.GetType().Name + ":" + ex.Message);
                return 1;
            }
        }

        private static void ComputeSenzaParentesiOriginale()
        {
            var ft = new RCM.FastData.Original.FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Rows.Add(1);
            ft.Compute("COUNT", null); // expected: infinite recursion -> SOE
        }

        private static bool ComputeSenzaParentesiCorretto()
        {
            var ft = new RCM.FastData.FastDataTable();
            ft.Columns.Add("A", typeof(int));
            ft.Rows.Add(1);
            try
            {
                object res = ft.Compute("COUNT", null);
                Console.WriteLine("RETURNED:" + res);
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }
    }
}
