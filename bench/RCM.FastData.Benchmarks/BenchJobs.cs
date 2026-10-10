namespace RCM.FastData.Benchmarks
{
    // Execution profiles of the plan -> BenchmarkDotNet 0.15.8 job presets (--job <value>).
    // In BDN 0.15.8 the --job option resolves ONLY the built-in presets (verified at runtime:
    // "Available options are: default, dry, short, medium, long, verylong"): custom jobs
    // defined in a user assembly are NOT selectable from the command line.
    //
    // Preset parameters (verified via reflection on the assembly of package 0.15.8):
    //   dry      warmup 1  + measured 1,   1 launch  (RunStrategy ColdStart)
    //   short    warmup 3  + measured 3,   1 launch
    //   medium   warmup 10 + measured 15,  2 launches
    //   long     warmup 15 + measured 100, 3 launches
    //   verylong warmup 30 + measured 500, 4 launches
    //
    // The sub-microsecond benchmarks (RowRead) are handled by BDN with automatic batching
    // of the invocations inside each iteration: the preset iteration count
    // stays the same for all benchmarks (homogeneous comparison).
    // Same GC Workstation (csproj) and same schema/data for all profiles.
    //
    //   Smoke    = --job dry      (quick technical check of all families)
    //   Standard = --job medium   (representative comparison; used in Phases C/F)
    //   Full     = --job long     (extended matrix, can be run explicitly)
    //   Stress   = --job verylong (long run / stability)
    public static class BenchProfiles
    {
        public const string Smoke = "dry";
        public const string Standard = "medium";
        public const string Full = "long";
        public const string Stress = "verylong";
    }
}
