using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Measures managed allocation on the calling thread, with a self-check: some hosts report a
    /// constant 0 from <c>GC.GetAllocatedBytesForCurrentThread()</c>, and without the self-check the
    /// "zero allocation" assertions would pass vacuously. A host that cannot measure is reported
    /// honestly (the test is skipped) instead of silently passing.
    /// </summary>
    internal static class AllocProbe
    {
        private const string HostUnusable =
            "GC.GetAllocatedBytesForCurrentThread is not measurable in this host (the allocation probe "
            + "reports a constant 0 — see AllocProbe). Skipping the byte-level zero-allocation assertion for ";

        private static int _usable = -1; // -1 unknown, 0 no, 1 yes

        /// <summary>
        /// True when the runtime's cumulative allocation counter actually moves. Probed once, by forcing a
        /// known allocation and checking the counter saw it.
        /// </summary>
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
        /// Returns false when the counter is not measurable in this host, in which case the caller must
        /// skip rather than assert (the probe's own allocation is made before the measurement window).
        /// </summary>
        internal static bool TryMeasure(Action body, out long allocated)
        {
            allocated = 0;

            // Probe first: its sample allocation must fall outside the measured window.
            bool measurable = IsMeasurable();
            if (!measurable)
            {
                body();
                return false;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return true;
        }

        /// <summary>Skips the current test, honestly, when this host cannot measure allocation.</summary>
        internal static void SkipUnmeasurable(string path)
        {
            Assert.Ignore(HostUnusable + path + ".");
        }

        /// <summary>Notes (without failing) that the byte counter is unusable and other evidence is used.</summary>
        internal static void NoteUnmeasurable(string path)
        {
            TestContext.WriteLine(
                "[EventBus Alloc] " + path + ": byte counter unusable in this host; "
                + "the handle-recycling count below is the GC-independent judgement.");
        }

        private static bool Probe()
        {
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] payload = new byte[64 * 1024];
                payload[0] = 1; // keep the allocation alive
                GC.KeepAlive(payload);
                return GC.GetAllocatedBytesForCurrentThread() - before > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Reference-identity comparer: subscription handles compare by reference.</summary>
        internal sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
