#if CYCLONEGAMES_HAS_LITMOTION
using System;
using System.Collections;
using System.Globalization;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Determines which allocation counter is trustworthy in this host before any GC claim is made.
    /// Allocates a known 1 MB array and reports what each counter observes.
    /// </summary>
    public sealed class AllocationProbeTests
    {
        [UnityTest]
        public IEnumerator ProbeCounters_WithKnownAllocation()
        {
            const int bytes = 1024 * 1024;

            yield return null;
            yield return null;

            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            long totalBefore = GC.GetTotalMemory(false);

            ProfilerRecorder recorder = default;
            bool recorderStarted = false;
            try
            {
                recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                recorderStarted = recorder.Valid;
            }
            catch (Exception exception)
            {
                Debug.Log($"[alloc-probe] recorder unavailable: {exception.GetType().Name}");
            }

            yield return null;

            byte[] payload = new byte[bytes];
            payload[0] = 1;
            payload[bytes - 1] = 1;

            yield return null;

            long threadAfter = GC.GetAllocatedBytesForCurrentThread();
            long totalAfter = GC.GetTotalMemory(false);
            long recorderValue = recorderStarted ? recorder.LastValue : -1;

            if (recorderStarted)
            {
                recorder.Dispose();
            }

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[alloc-probe] expected={0} threadDelta={1} totalDelta={2} recorderLast={3}",
                bytes,
                threadAfter - threadBefore,
                totalAfter - totalBefore,
                recorderValue));

            GC.KeepAlive(payload);
        }

        [UnityTest]
        public IEnumerator ProbeCounters_WithNoAllocation()
        {
            yield return null;
            yield return null;

            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            long totalBefore = GC.GetTotalMemory(false);

            int accumulator = 0;
            for (int i = 0; i < 1000; i++)
            {
                accumulator += i;
            }

            long threadAfter = GC.GetAllocatedBytesForCurrentThread();
            long totalAfter = GC.GetTotalMemory(false);

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[alloc-probe] no-alloc threadDelta={0} totalDelta={1} accumulator={2}",
                threadAfter - threadBefore,
                totalAfter - totalBefore,
                accumulator));
        }
    }
}
#endif
