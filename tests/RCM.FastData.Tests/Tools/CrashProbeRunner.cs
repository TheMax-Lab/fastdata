using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RCM.FastData.Tests.Tools
{
    /// <summary>
    /// Launched the CrashProbe project (separate process) for scenarios at risk of
    /// StackOverflowException: an SOE cannot be caught and would kill the test host.
    /// </summary>
    public static class CrashProbeRunner
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        public static int Run(string scenario)
        {
            string exe = ProbeExePath();
            if (!File.Exists(exe))
                throw new FileNotFoundException("CrashProbe non trovato: " + exe +
                    " (build prima: dotnet build tests/RCM.FastData.Tests)");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + scenario + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                // Parallel reading: the StackOverflow dump on stderr (~200 KB)
                // fills the pipe buffer and blocks the process; a sequential ReadToEnd
                // on stdout would deadlock.
                Task<string> stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd());
                Task<string> stderrTask = Task.Run(() => p.StandardError.ReadToEnd());

                bool exited = p.WaitForExit((int)Timeout.TotalMilliseconds);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("CrashProbe timeout: " + scenario);
                }

                string stdout = stdoutTask.Wait(TimeSpan.FromSeconds(10)) ? stdoutTask.Result : "(n/d)";
                string stderr = stderrTask.Wait(TimeSpan.FromSeconds(10)) ? stderrTask.Result : "(n/d)";

                // the full dump goes to file (for the report and diagnostics);
                // only an excerpt is printed on the console
                try
                {
                    string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "logs");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "probe-" + scenario + ".err.txt"), stderr);
                    File.WriteAllText(Path.Combine(dir, "probe-" + scenario + ".out.txt"), stdout);

                    if (Environment.GetEnvironmentVariable("CRASHPROBE_DEBUG") == "1")
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                        File.WriteAllText(Path.Combine(dir, "probe-env.txt"), sb.ToString());
                    }
                }
                catch { }

                string errShort = stderr.Trim();
                if (errShort.Length > 200) errShort = errShort.Substring(0, 200) + " [...]";
                Console.WriteLine("[CrashProbe:" + scenario + "] exit=" + p.ExitCode +
                    " out=" + stdout.Trim() + " err=" + errShort.Replace("\r", " ").Replace("\n", " | "));
                return p.ExitCode;
            }
        }

        private static string ProbeExePath()
        {
            // output: tests/RCM.FastData.Tests/bin/<cfg>/<tfm>/
            // probe:  tools/RCM.FastData.CrashProbe/bin/<cfg>/<tfm>/
            // NB: ImageRuntimeVersion is NOT a discriminator: it returns "v4.0.30319"
            // both on .NET Framework and on .NET Core (compatibility behavior).
            // Reliable: the assembly of System.Object (mscorlib vs System.Private.CoreLib).
            bool isNetFx = typeof(object).Assembly.FullName.IndexOf("mscorlib", StringComparison.Ordinal) >= 0;
            string tfm = isNetFx ? "net48" : "net8.0";

            // The configuration (Debug/Release) is deduced from the test project output:
            // the ProjectReference (ReferenceOutputAssembly=false) builds the probe
            // in the SAME configuration as the tests. A fixed "Debug" path picked a
            // stale probe when the tests ran in Release.
            string baseDir = AppContext.BaseDirectory;
            string cfg = baseDir.IndexOf("\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0
                || baseDir.IndexOf("/Debug/", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Debug" : "Release";

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "tools", "RCM.FastData.CrashProbe", "bin", cfg, tfm,
                "RCM.FastData.CrashProbe.exe"));
        }
    }
}
