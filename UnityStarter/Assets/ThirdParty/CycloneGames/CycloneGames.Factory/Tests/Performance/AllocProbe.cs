using System;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Measures managed allocation on the calling thread, with a self-check: some hosts report a constant
    /// 0 from <c>GC.GetAllocatedBytesForCurrentThread()</c> (Unity 2022.3 Editor on Mono does), and
    /// without the self-check a "zero allocation" assertion would pass vacuously. A host that cannot
    /// measure is reported as skipped instead of passing.
    /// </summary>
    internal static class AllocProbe
    {
        private const string HostUnusable =
            "GC.GetAllocatedBytesForCurrentThread is not measurable in this host (the probe reports a "
            + "constant 0). Skipping the byte-level zero-allocation assertion for ";

        private static int _usable = -1; // -1 unknown, 0 no, 1 yes

        internal static bool IsMeasurable()
        {
            if (_usable < 0)
            {
                _usable = Probe() ? 1 : 0;
            }

            return _usable == 1;
        }

        /// <summary>
        /// Runs <paramref name="body"/> and reports the managed bytes it allocated on this thread.
        /// Returns false when the counter is not measurable, in which case the caller must skip rather
        /// than assert.
        /// </summary>
        internal static bool TryMeasure(Action body, out long allocated)
        {
            allocated = 0;

            if (!IsMeasurable())
            {
                body();
                return false;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return true;
        }

        internal static void SkipUnmeasurable(string path)
        {
            Assert.Ignore(HostUnusable + path + ".");
        }

        internal static void NoteUnmeasurable(string path)
        {
            TestContext.WriteLine(
                "[Factory Alloc] " + path + ": byte counter unusable in this host; "
                + "the TrackingCapacity assertion below is the GC-independent judgement.");
        }

        private static bool Probe()
        {
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] payload = new byte[64 * 1024];
                payload[0] = 1;
                GC.KeepAlive(payload);
                return GC.GetAllocatedBytesForCurrentThread() - before > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
