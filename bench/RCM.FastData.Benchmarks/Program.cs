using System;
using System.IO;
using System.Collections.Generic;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace RCM.FastData.Benchmarks
{
    // BenchmarkDotNet runner.
    // Usage (from the terminal, NO debugger):
    //   dotnet run -c Release -f net48  -- --job medium --filter ".*N=10000$"
    //   dotnet run -c Release -f net8.0 -- --job medium --filter ".*N=10000$"
    // Each run produces a process on the declared runtime (net48 = .NET Framework 4.8,
    // net8.0 = .NET 8). Do not use --inprocess for the main comparison.
    //
    // Profiles (see BenchProfiles): Smoke=dry, Standard=medium, Full=long, Stress=verylong.
    //
    // Export: fixed here in code (markdown + csv + fulljson), because the CLI of
    // BenchmarkDotNet 0.15.8 accepts a single exporter per option (verified at runtime:
    // comma/space-separated lists are rejected).
    // If --artifacts is not explicit, <repo>/artifacts/bench is used.
    public static class Program
    {
        private static void Main(string[] args)
        {
            // Permanent diagnostic marker in the log: which runtime and exe are running.
            // NB: ImageRuntimeVersion is NOT a discriminator (it returns "v4.0.30319" on
            // both runtimes); reliable: the System.Object assembly.
            Console.WriteLine("[BENCH] RUNTIME=" + typeof(object).Assembly.FullName
                + " CORECLR=" + (AppContext.GetData("CORECLR") != null)
                + " EXE=" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);

            // BDN 0.15.8: the CLI accepts a single exporter per option; here the three
            // exports of the plan are fixed (markdown for the report, csv for the comparison
            // script, fulljson for the archive) via ManualConfig.
            var config = ManualConfig.CreateMinimumViable()
                .AddExporter(MarkdownExporter.Default)
                .AddExporter(CsvExporter.Default)
                .AddExporter(JsonExporter.Full);

            new BenchmarkSwitcher(typeof(Program).Assembly).Run(BuildArgs(args), config);
        }

        private static string[] BuildArgs(string[] args)
        {
            List<string> list = new List<string>(args);
            bool hasArtifacts = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--artifacts" || args[i].StartsWith("--artifacts=", StringComparison.OrdinalIgnoreCase))
                    hasArtifacts = true;
            }
            if (!hasArtifacts)
            {
                string repo = FindRepoRoot();
                if (repo != null)
                    list.Insert(0, "--artifacts=" + Path.Combine(repo, "artifacts", "bench"));
            }
            return list.ToArray();
        }

        // Walks up from BaseDirectory until RCM.FastData.sln is found (max 10 levels).
        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                if (string.IsNullOrEmpty(dir)) return null;
                if (File.Exists(Path.Combine(dir, "RCM.FastData.sln"))) return dir;
                var parent = new DirectoryInfo(dir).Parent;
                if (parent == null) return null;
                dir = parent.FullName;
            }
            return null;
        }
    }
}
