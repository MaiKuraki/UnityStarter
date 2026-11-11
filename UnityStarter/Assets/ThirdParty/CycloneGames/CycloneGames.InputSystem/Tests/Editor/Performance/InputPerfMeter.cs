using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    internal readonly struct TimingStats
    {
        internal TimingStats(int count, double min, double median, double mean, double p95, double p99, double max, double stdDev)
        {
            Count = count;
            MinUs = min;
            MedianUs = median;
            MeanUs = mean;
            P95Us = p95;
            P99Us = p99;
            MaxUs = max;
            StdDevUs = stdDev;
        }

        internal int Count { get; }
        internal double MinUs { get; }
        internal double MedianUs { get; }
        internal double MeanUs { get; }
        internal double P95Us { get; }
        internal double P99Us { get; }
        internal double MaxUs { get; }
        internal double StdDevUs { get; }
    }

    /// <summary>
    /// Timing and allocation primitives shared by every measurement fixture.
    /// </summary>
    internal static class PerfMeter
    {
        internal static readonly double TicksToMicroseconds = 1_000_000d / Stopwatch.Frequency;

        /// <summary>
        /// Runs <paramref name="sampleCount"/> samples, each containing <paramref name="callsPerSample"/> body
        /// invocations, and returns per-invocation microseconds. Batching keeps Stopwatch resolution and call
        /// overhead below the measured work per sample.
        /// </summary>
        internal static double[] SamplePerCallMicroseconds(int sampleCount, int callsPerSample, Action body)
        {
            var samples = new double[sampleCount];
            for (int sample = 0; sample < sampleCount; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int call = 0; call < callsPerSample; call++) body();
                long elapsed = Stopwatch.GetTimestamp() - start;
                samples[sample] = elapsed * TicksToMicroseconds / callsPerSample;
            }

            return samples;
        }

        internal static TimingStats Summarize(double[] samples)
        {
            if (samples == null || samples.Length == 0) return default;

            var sorted = new double[samples.Length];
            Array.Copy(samples, sorted, samples.Length);
            Array.Sort(sorted);

            double sum = 0d;
            for (int i = 0; i < samples.Length; i++) sum += samples[i];
            double mean = sum / samples.Length;

            double variance = 0d;
            for (int i = 0; i < samples.Length; i++)
            {
                double delta = samples[i] - mean;
                variance += delta * delta;
            }

            double stdDev = samples.Length > 1 ? Math.Sqrt(variance / (samples.Length - 1)) : 0d;

            return new TimingStats(
                samples.Length,
                sorted[0],
                Percentile(sorted, 0.50d),
                mean,
                Percentile(sorted, 0.95d),
                Percentile(sorted, 0.99d),
                sorted[sorted.Length - 1],
                stdDev);
        }

        internal static double Percentile(double[] sortedAscending, double percentile)
        {
            if (sortedAscending.Length == 0) return 0d;
            if (sortedAscending.Length == 1) return sortedAscending[0];
            double rank = percentile * (sortedAscending.Length - 1);
            int low = (int)Math.Floor(rank);
            int high = (int)Math.Ceiling(rank);
            if (low == high) return sortedAscending[low];
            double weight = rank - low;
            return sortedAscending[low] * (1d - weight) + sortedAscending[high] * weight;
        }

        internal static long ManagedHeapBytes()
        {
            return GC.GetTotalMemory(false);
        }

        internal static long LiveHeapBytes()
        {
            return GC.GetTotalMemory(true);
        }

        internal static int TotalCollections()
        {
            return GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        }

        internal static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    /// <summary>
    /// Selects a trustworthy managed-allocation instrument at run time. The Mono editor host does not
    /// guarantee that every allocation counter advances, so each candidate is calibrated against a known
    /// garbage volume before it is used for evidence.
    /// </summary>
    internal static class AllocationInstrument
    {
        internal enum Kind
        {
            None,
            PerThreadCounter,
            TotalAllocatedCounter,
            FrameRecorder,
            HeapWindow
        }

        internal const int CalibrationObjectSize = 1024;
        internal const int CalibrationObjectCount = 256;
        internal const long CalibrationExpectedBytes = (long)CalibrationObjectSize * CalibrationObjectCount;

        private static readonly List<string> Log = new List<string>();
        private static bool _initialized;
        private static Kind _selected = Kind.HeapWindow;
        private static double _perThreadError;
        private static double _totalAllocatedError;
        private static double _heapError;
        private static long _perThreadAbsolute;
        private static long _totalAllocatedAbsolute;
        private static MethodInfo _totalAllocatedMethod;
        private static ProfilerRecorder _frameRecorder;
        private static bool _frameRecorderValid;

        internal static Kind Selected
        {
            get
            {
                Initialize();
                return _selected;
            }
        }

        internal static IReadOnlyList<string> CalibrationLog
        {
            get
            {
                Initialize();
                return Log;
            }
        }

        internal static double PerThreadError => _perThreadError;
        internal static double TotalAllocatedError => _totalAllocatedError;
        internal static double HeapError => _heapError;
        internal static long PerThreadAbsolute => _perThreadAbsolute;
        internal static long TotalAllocatedAbsolute => _totalAllocatedAbsolute;
        internal static bool FrameRecorderValid => _frameRecorderValid;
        internal static long FrameRecorderLastValue => _frameRecorderValid ? _frameRecorder.LastValue : -1L;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _totalAllocatedMethod = typeof(GC).GetMethod(
                "GetTotalAllocatedBytes",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);

            _perThreadAbsolute = GC.GetAllocatedBytesForCurrentThread();
            _totalAllocatedAbsolute = ReadTotalAllocated();

            // Warm both counters so that first-call bookkeeping is not attributed to the calibration window.
            AllocateGarbage(4);
            _perThreadAbsolute = GC.GetAllocatedBytesForCurrentThread();
            _totalAllocatedAbsolute = ReadTotalAllocated();

            long perThreadBefore = GC.GetAllocatedBytesForCurrentThread();
            long totalBefore = ReadTotalAllocated();
            AllocateGarbage(CalibrationObjectCount);
            long perThreadDelta = GC.GetAllocatedBytesForCurrentThread() - perThreadBefore;
            long totalDelta = ReadTotalAllocated() - totalBefore;

            _perThreadError = RelativeError(perThreadDelta, CalibrationExpectedBytes);
            _totalAllocatedError = _totalAllocatedMethod == null
                ? double.PositiveInfinity
                : RelativeError(totalDelta, CalibrationExpectedBytes);

            // The heap-delta path is calibrated with a larger volume because it is the fallback ruler.
            PerfMeter.ForceFullCollection();
            long heapBefore = GC.GetTotalMemory(false);
            AllocateGarbage(CalibrationObjectCount * 8);
            long heapDelta = GC.GetTotalMemory(false) - heapBefore;
            _heapError = RelativeError(heapDelta, CalibrationExpectedBytes * 8);

            Log.Add("calibration.expectedBytes=" + CalibrationExpectedBytes);
            Log.Add("calibration.perThreadDelta=" + perThreadDelta);
            Log.Add("calibration.perThreadRelativeError=" + PerfOutput.Num(_perThreadError, 6));
            Log.Add("calibration.totalAllocatedSupported=" + (_totalAllocatedMethod != null ? "true" : "false"));
            Log.Add("calibration.totalAllocatedDelta=" + totalDelta);
            Log.Add("calibration.totalAllocatedRelativeError=" + PerfOutput.Num(_totalAllocatedError, 6));
            Log.Add("calibration.heapExpectedBytes=" + (CalibrationExpectedBytes * 8));
            Log.Add("calibration.heapDelta=" + heapDelta);
            Log.Add("calibration.heapRelativeError=" + PerfOutput.Num(_heapError, 6));
            Log.Add("calibration.nurseryBudgetBytes=" + NurseryBudget.ThresholdBytes);

            if (_perThreadError <= 0.10d)
            {
                _selected = Kind.PerThreadCounter;
            }
            else if (_totalAllocatedError <= 0.10d)
            {
                _selected = Kind.TotalAllocatedCounter;
            }
            else
            {
                _selected = Kind.HeapWindow;
            }

            Log.Add("calibration.selected=" + _selected);
        }

        /// <summary>
        /// Enables the Unity profiler and starts the frame allocation recorder. The recorder returns zero while
        /// the profiler is disabled, so enabling it is part of the measurement contract.
        /// </summary>
        internal static bool TryStartFrameRecorder()
        {
            if (_frameRecorderValid) return true;
            try
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                _frameRecorder = ProfilerRecorder.StartNew(
                    ProfilerCategory.Memory,
                    "GC Allocated In Frame",
                    4096);
                _frameRecorderValid = _frameRecorder.Valid;
            }
            catch (Exception exception)
            {
                Log.Add("frameRecorder.exception=" + exception.GetType().Name);
                _frameRecorderValid = false;
            }

            Log.Add("frameRecorder.valid=" + _frameRecorderValid);
            return _frameRecorderValid;
        }

        internal static long FrameRecorderSampleSum()
        {
            if (!_frameRecorderValid) return -1L;
            long sum = 0;
            int count = _frameRecorder.Count;
            for (int i = 0; i < count; i++) sum += _frameRecorder.GetSample(i).Value;
            return sum;
        }

        internal static int FrameRecorderSampleCount => _frameRecorderValid ? _frameRecorder.Count : -1;

        internal static void ResetFrameRecorderSamples()
        {
            if (!_frameRecorderValid) return;
            _frameRecorder.Reset();
        }

        internal static void StopFrameRecorder()
        {
            if (!_frameRecorderValid) return;
            _frameRecorder.Dispose();
            _frameRecorderValid = false;
        }

        internal static long Capture()
        {
            Initialize();
            switch (_selected)
            {
                case Kind.PerThreadCounter:
                    return GC.GetAllocatedBytesForCurrentThread();
                case Kind.TotalAllocatedCounter:
                    return ReadTotalAllocated();
                default:
                    return GC.GetTotalMemory(false);
            }
        }

        private static readonly string[] CandidateRecorderNames =
        {
            "GC Allocated In Frame",
            "GC Reserved Memory",
            "Total Used Memory",
            "System Used Memory",
            "Mono Used Size",
            "Mono Heap Size"
        };

        /// <summary>
        /// Reports which well known allocation and managed memory statistics can be recorded on this host, so
        /// that the report can justify the recorder name it relies on instead of assuming availability.
        /// </summary>
        internal static List<string> ProbeRecorderNames()
        {
            var results = new List<string>();
            for (int i = 0; i < CandidateRecorderNames.Length; i++)
            {
                string name = CandidateRecorderNames[i];
                bool valid = false;
                try
                {
                    using (ProfilerRecorder probe = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name))
                    {
                        valid = probe.Valid;
                    }
                }
                catch (Exception)
                {
                    valid = false;
                }

                results.Add(name + "=" + (valid ? "valid" : "invalid"));
            }

            return results;
        }

        private static long ReadTotalAllocated()
        {
            if (_totalAllocatedMethod == null) return -1L;
            try
            {
                return (long)_totalAllocatedMethod.Invoke(null, new object[] { false });
            }
            catch (Exception)
            {
                _totalAllocatedMethod = null;
                return -1L;
            }
        }

        private static void AllocateGarbage(int count)
        {
            object[] sink = new object[count];
            for (int i = 0; i < count; i++) sink[i] = new byte[CalibrationObjectSize];
            GC.KeepAlive(sink);
        }

        private static double RelativeError(long actual, long expected)
        {
            if (actual < 0 || expected <= 0) return double.PositiveInfinity;
            return Math.Abs(actual - expected) / (double)expected;
        }
    }

    /// <summary>
    /// Measures the effective garbage budget of one generation-zero collection. Because this host exposes no
    /// working allocation counter, the generated garbage volume between collections is used as a ruler: a body
    /// that runs many times without triggering a collection allocated less than the budget divided by the
    /// iteration count.
    /// </summary>
    internal static class NurseryBudget
    {
        private const int PayloadBytes = 1024;
        private const int MaxProbeObjects = 1_048_576;

        private static long _thresholdBytes;
        private static bool _measured;

        internal static long ThresholdBytes
        {
            get
            {
                if (!_measured) Measure();
                return _thresholdBytes;
            }
        }

        internal static void Measure()
        {
            PerfMeter.ForceFullCollection();
            int baseline = GC.CollectionCount(0);
            long allocated = 0;
            int index = 0;
            while (index < MaxProbeObjects)
            {
                var payload = new byte[PayloadBytes];
                payload[0] = 1;
                GC.KeepAlive(payload);
                allocated += PayloadBytes + 24;
                index++;

                if ((index & 0xFF) == 0 && GC.CollectionCount(0) != baseline)
                {
                    break;
                }
            }

            if (GC.CollectionCount(0) == baseline)
            {
                // The probe never reached a collection. Report the full probe volume as a lower bound so that
                // callers treat the estimate as unusable instead of trusting an optimistic number.
                _thresholdBytes = 0;
            }
            else
            {
                _thresholdBytes = allocated;
            }

            _measured = true;
        }

        internal readonly struct Bound
        {
            internal Bound(long thresholdBytes, int iterations, int gen0Delta, int gen1Delta, int gen2Delta)
            {
                ThresholdBytes = thresholdBytes;
                Iterations = iterations;
                Gen0Delta = gen0Delta;
                Gen1Delta = gen1Delta;
                Gen2Delta = gen2Delta;
            }

            internal long ThresholdBytes { get; }
            internal int Iterations { get; }
            internal int Gen0Delta { get; }
            internal int Gen1Delta { get; }
            internal int Gen2Delta { get; }
            internal bool NoCollectionObserved => Gen0Delta == 0 && Gen1Delta == 0 && Gen2Delta == 0;

            /// <summary>
            /// Upper bound for bytes allocated per iteration. Only meaningful when no collection occurred.
            /// </summary>
            internal double BytesPerIterationUpperBound =>
                Iterations == 0 || ThresholdBytes <= 0 ? double.NaN : ThresholdBytes / (double)Iterations;
        }

        /// <summary>
        /// Runs <paramref name="body"/> for <paramref name="iterations"/> invocations after a warmup and a full
        /// collection, then reports whether the run stayed inside one garbage budget.
        /// </summary>
        internal static Bound Run(Action body, int iterations, int warmupIterations = 512)
        {
            for (int i = 0; i < warmupIterations; i++) body();
            PerfMeter.ForceFullCollection();

            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            for (int i = 0; i < iterations; i++) body();

            return new Bound(
                ThresholdBytes,
                iterations,
                GC.CollectionCount(0) - gen0,
                GC.CollectionCount(1) - gen1,
                GC.CollectionCount(2) - gen2);
        }
    }

    /// <summary>
    /// Managed allocation measurement with explicit rejection of windows that overlap a garbage collection.
    /// Window length is chosen from a pilot run so that a window stays well inside one garbage budget, which
    /// keeps most windows clean even for bodies that allocate on every call.
    /// </summary>
    internal static class AllocationSample
    {
        internal readonly struct Result
        {
            internal Result(
                double medianBytesPerIteration,
                double minBytesPerIteration,
                double maxBytesPerIteration,
                int cleanWindows,
                int dirtyWindows,
                int totalIterations,
                int collectionDelta,
                AllocationInstrument.Kind kind,
                long nurseryBudgetBytes)
            {
                MedianBytesPerIteration = medianBytesPerIteration;
                MinBytesPerIteration = minBytesPerIteration;
                MaxBytesPerIteration = maxBytesPerIteration;
                CleanWindows = cleanWindows;
                DirtyWindows = dirtyWindows;
                TotalIterations = totalIterations;
                CollectionDelta = collectionDelta;
                Kind = kind;
                NurseryBudgetBytes = nurseryBudgetBytes;
            }

            internal double MedianBytesPerIteration { get; }
            internal double MinBytesPerIteration { get; }
            internal double MaxBytesPerIteration { get; }
            internal int CleanWindows { get; }
            internal int DirtyWindows { get; }
            internal int TotalIterations { get; }
            internal int CollectionDelta { get; }
            internal AllocationInstrument.Kind Kind { get; }
            internal long NurseryBudgetBytes { get; }

            /// <summary>
            /// True when no collection occurred and the measurement therefore produced a hard byte figure.
            /// </summary>
            internal bool IsExact => CollectionDelta == 0;
        }

        internal static Result Measure(Action body, int iterations, int warmupIterations = 512)
        {
            AllocationInstrument.Initialize();
            long budget = NurseryBudget.ThresholdBytes;

            for (int i = 0; i < warmupIterations; i++) body();
            PerfMeter.ForceFullCollection();

            int windowIterations = ChooseWindow(body, budget);

            var perWindow = new List<double>(Math.Max(4, iterations / Math.Max(1, windowIterations)));
            int dirty = 0;
            int measuredIterations = 0;
            int collectionsBefore = PerfMeter.TotalCollections();

            int remaining = iterations;
            while (remaining > 0)
            {
                int window = Math.Min(windowIterations, remaining);
                int collectionsAtWindowStart = PerfMeter.TotalCollections();
                long before = AllocationInstrument.Capture();
                for (int i = 0; i < window; i++) body();
                long after = AllocationInstrument.Capture();
                int collectionsAtWindowEnd = PerfMeter.TotalCollections();

                long delta = after - before;
                if (collectionsAtWindowEnd != collectionsAtWindowStart || delta < 0)
                {
                    dirty++;
                }
                else
                {
                    perWindow.Add(delta / (double)window);
                    measuredIterations += window;
                }

                remaining -= window;
            }

            double median = 0d;
            double min = 0d;
            double max = 0d;
            if (perWindow.Count > 0)
            {
                perWindow.Sort();
                min = perWindow[0];
                max = perWindow[perWindow.Count - 1];
                median = PerfMeter.Percentile(perWindow.ToArray(), 0.50d);
            }

            return new Result(
                median,
                min,
                max,
                perWindow.Count,
                dirty,
                measuredIterations,
                PerfMeter.TotalCollections() - collectionsBefore,
                AllocationInstrument.Selected,
                budget);
        }

        /// <summary>
        /// Picks a window length whose expected allocation stays below half of one garbage budget.
        /// </summary>
        private static int ChooseWindow(Action body, long budgetBytes)
        {
            const int PilotIterations = 64;
            const int MinimumWindow = 8;
            const int MaximumWindow = 4096;
            if (budgetBytes <= 0) return 512;

            PerfMeter.ForceFullCollection();
            int collectionsBefore = PerfMeter.TotalCollections();
            long before = AllocationInstrument.Capture();
            for (int i = 0; i < PilotIterations; i++) body();
            long after = AllocationInstrument.Capture();
            int collectionsAfter = PerfMeter.TotalCollections();

            if (collectionsAfter != collectionsBefore) return MinimumWindow;

            long delta = after - before;
            if (delta <= 0) return MaximumWindow;

            double perIteration = delta / (double)PilotIterations;
            double target = budgetBytes * 0.5d / perIteration;
            if (target < MinimumWindow) return MinimumWindow;
            if (target > MaximumWindow) return MaximumWindow;
            return (int)target;
        }
    }
}
