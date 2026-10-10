using System;
using System.Runtime.InteropServices;
using Xunit;

namespace RCM.FastData.Tests.Core
{
    /// <summary>
    /// Diagnostics: checks which runtime the test host is running on. Important because
    /// ImageRuntimeVersion returns "4.0.30319" even on .NET Core, so it is not
    /// a reliable discriminator (it was used in CrashProbeRunner to pick the probe's TFM:
    /// on a CoreCLR host the choice is wrong).
    /// </summary>
    public class RuntimeHostTests
    {
        [Fact]
        public void MostraRuntime()
        {
            bool coreclr = AppContext.GetData("CORECLR") != null;
            Console.WriteLine("[TESTHOST] " + RuntimeInformation.FrameworkDescription
                + " coreclr=" + coreclr
                + " base=" + AppContext.BaseDirectory);
            Assert.True(true);
        }
    }
}
