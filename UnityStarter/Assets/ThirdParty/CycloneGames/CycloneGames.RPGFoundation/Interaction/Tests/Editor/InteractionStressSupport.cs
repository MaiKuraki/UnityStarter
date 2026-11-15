using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Shared measurement plumbing for the interaction stress suite. Every probe here has a
    /// known-answer path: a broken measurement reports a mismatch instead of a clean zero.
    /// </summary>
    internal static class StressPaths
    {
        public const string CoreHeader =
            "scenario,seed,scale,iterations,ops,throughput_ops_per_s,p50_us,p95_us,p99_us,alloc_bytes_per_op,gc0_delta,accepted,rejected,retained_entries,peak_working_set_mb,note";

        public const string GridHeader =
            "scenario,seed,scale,cell_size,radius,distribution,update_ratio,detector_count,iterations,ops,throughput_ops_per_s,p50_us,p95_us,p99_us,alloc_bytes_per_op,gc0_delta,occupied_cells,avg_cell_load,slot_capacity,avg_results,oracle_checks,oracle_mismatch,managed_heap_mb,note";

        public const string DetectorHeader =
            "scenario,seed,interactables,detectors,radius,max_interactables,cycles,cycles_per_s,p50_us,p95_us,p99_us,candidates_mean,nearby_mean,alloc_bytes_per_cycle,managed_heap_mb,note";

        public const string SoakHeader =
            "window,ops_per_s,p50_us,p95_us,p99_us,managed_heap_mb,item_count,slot_capacity,occupied_cells,gen0,ensure_failures";

        public const string SoakSummaryHeader =
            "scenario,iterations,scale,window_count,elapsed_s,first_window_ops_per_s,steady_window_ops_per_s,worst_window_ops_per_s,steady_decay_ratio,bytes_per_iteration,gen0_total,slot_capacity_start,slot_capacity_end,slot_capacity_growth,churn_inserts,churn_removes,validate_structure";

        public const string FuzzHeader =
            "iteration,operation,live_count,slot_capacity,occupied_cells,oracle_checks,oracle_mismatch,structure_valid";

        private static string s_evidenceDirectory;

        public static string EvidenceDirectory
        {
            get
            {
                if (s_evidenceDirectory != null) return s_evidenceDirectory;

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string directory = Path.Combine(projectRoot, "deliverables", "review", "evidence", "interaction");
                Directory.CreateDirectory(directory);
                s_evidenceDirectory = directory;
                return directory;
            }
        }
    }

    internal static class StressEnvironment
    {
        public static long ManagedHeapMb => (long)(GC.GetTotalMemory(false) / (1024.0 * 1024.0));

        /// <summary>Working set is unavailable inside the Editor sandbox; use managed heap instead.</summary>
        public static int WorkingSetMb => -1;
    }

    internal sealed class StressCsvWriter : IDisposable
    {
        private static readonly HashSet<string> s_openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly StreamWriter _writer;
        private readonly string _path;

        public StressCsvWriter(string fileName, string header)
        {
            _path = ReservePath(Path.Combine(StressPaths.EvidenceDirectory, fileName));
            _writer = new StreamWriter(_path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _writer.WriteLine(header);
        }

        /// <summary>
        /// Several scenarios intentionally share one logical evidence file. Windows refuses a second
        /// writer on the same path while the first is open, so a concurrent request gets a numbered
        /// sibling instead of failing the test.
        /// </summary>
        private static string ReservePath(string path)
        {
            lock (s_openPaths)
            {
                string candidate = path;
                int suffix = 1;
                while (s_openPaths.Contains(candidate))
                {
                    string directory = Path.GetDirectoryName(path) ?? ".";
                    string name = Path.GetFileNameWithoutExtension(path);
                    string extension = Path.GetExtension(path);
                    candidate = Path.Combine(directory, name + "_" + suffix.ToString(CultureInfo.InvariantCulture) + extension);
                    suffix++;
                }

                s_openPaths.Add(candidate);
                return candidate;
            }
        }

        public void Row(params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) _writer.Write(',');
                _writer.Write(cells[i] ?? string.Empty);
            }

            _writer.WriteLine();
        }

        public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Num(double value, int decimals) =>
            value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public void Dispose()
        {
            _writer.Dispose();
            lock (s_openPaths)
            {
                s_openPaths.Remove(_path);
            }
        }
    }

    /// <summary>
    /// Fixed-bucket latency histogram. Bucket width is chosen once so that a whole run costs one
    /// array increment per sample instead of a sort.
    /// </summary>
    internal sealed class LatencyHistogram
    {
        private readonly long[] _buckets;
        private readonly long _bucketTicks;
        private long _count;
        private long _overflow;

        public LatencyHistogram(long bucketTicks = 10L, int bucketCount = 8192)
        {
            _bucketTicks = bucketTicks > 0 ? bucketTicks : 10L;
            _buckets = new long[bucketCount > 0 ? bucketCount : 8192];
        }

        public long Count => _count;
        public long OverflowCount => _overflow;

        public void AddTicks(long ticks)
        {
            _count++;
            if (ticks < 0) ticks = 0;

            long index = ticks / _bucketTicks;
            if (index >= _buckets.Length)
            {
                _overflow++;
                index = _buckets.Length - 1;
            }

            _buckets[index]++;
        }

        /// <summary>Returns the requested percentile in microseconds, or 0 when no sample exists.</summary>
        public double PercentileUs(double percentile)
        {
            if (_count == 0) return 0.0;

            long target = (long)Math.Ceiling(_count * percentile);
            if (target < 1) target = 1;

            long seen = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                seen += _buckets[i];
                if (seen >= target)
                {
                    return TicksToNs((i + 1) * _bucketTicks) / 1000.0;
                }
            }

            return TicksToNs(_buckets.Length * _bucketTicks) / 1000.0;
        }

        public void Reset()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            _count = 0;
            _overflow = 0;
        }

        public static double TicksToNs(long ticks) => ticks * (1_000_000_000.0 / Stopwatch.Frequency);
    }

    internal readonly struct AllocationProbe
    {
        public readonly long RawDeltaBytes;
        public readonly int Iterations;
        public readonly int ForcedCollectionDelta;

        public AllocationProbe(long rawDeltaBytes, int iterations, int forcedCollectionDelta)
        {
            RawDeltaBytes = rawDeltaBytes;
            Iterations = iterations;
            ForcedCollectionDelta = forcedCollectionDelta;
        }

        public double BytesPerOperation => Iterations <= 0 ? 0.0 : RawDeltaBytes / (double)Iterations;

        /// <summary>
        /// Foreground collections forced by the probe itself. This is not an allocation signal:
        /// the meter forces a collection before sampling the baseline, so this value is always
        /// at least one regardless of what the measured body allocates.
        /// </summary>
        public int Gen0Collections => ForcedCollectionDelta;
    }

    internal static class AllocationMeter
    {
        /// <summary>Payload written by the known-answer probe. Any deviation means the probe is broken.</summary>
        private const int KnownAllocationBytes = 64 * 1024;

        /// <summary>
        /// Measures managed allocation across an action. The forcing collection happens BEFORE the
        /// baseline snapshot, so the returned GC delta describes only work the action performed.
        /// </summary>
        public static AllocationProbe Measure(int iterations, Action body)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int gen0Before = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(true);
            gen0Before = GC.CollectionCount(0);

            body();

            long after = GC.GetTotalMemory(false);
            int gen0After = GC.CollectionCount(0);

            return new AllocationProbe(after - before, iterations, gen0After - gen0Before);
        }

        /// <summary>
        /// Verifies the meter can see a known 64 KiB payload. Returns the observed delta so callers
        /// can assert on it; a meter that reports zero here cannot be trusted anywhere else.
        /// </summary>
        public static long MeasureKnownAllocation()
        {
            byte[] payload = Array.Empty<byte>();
            AllocationProbe probe = Measure(1, () => payload = new byte[KnownAllocationBytes]);
            GC.KeepAlive(payload);
            return probe.RawDeltaBytes;
        }
    }

    /// <summary>Deterministic xorshift128+ generator so every scenario is reproducible from its seed.</summary>
    internal sealed class DeterministicRng
    {
        private ulong _s0;
        private ulong _s1;

        public DeterministicRng(ulong seed)
        {
            _s0 = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            _s1 = _s0 ^ 0xD1B54A32D192ED03UL;
            if (_s1 == 0) _s1 = 0x94D049BB133111EBUL;
        }

        public ulong NextUInt64()
        {
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        public int NextInt(int exclusiveMax) => exclusiveMax <= 0 ? 0 : (int)(NextUInt64() % (ulong)exclusiveMax);

        public float NextFloat(float min, float max) => min + (float)(NextDouble() * (max - min));
    }

    /// <summary>Deterministic source used by the Core stress scenarios.</summary>
    internal sealed class StressRandom
    {
        private readonly DeterministicRng _rng;

        public StressRandom(ulong seed)
        {
            _rng = new DeterministicRng(seed);
        }

        public ulong NextUInt64() => _rng.NextUInt64();
        public double NextDouble() => _rng.NextDouble();
        public int NextInt(int exclusiveMax) => _rng.NextInt(exclusiveMax);
    }
}
