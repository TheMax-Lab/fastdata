using System;
using System.Globalization;
using System.Threading;

namespace RCM.FastData.Tests.Helpers
{
    // Runs the action with CultureInfo.CurrentCulture set and restores it.
    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _old;

        public CultureScope(string name)
        {
            _old = CultureInfo.CurrentCulture;
            CultureInfo ci = string.IsNullOrEmpty(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = ci;
            Thread.CurrentThread.CurrentCulture = ci;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _old;
            Thread.CurrentThread.CurrentCulture = _old;
        }
    }
}
