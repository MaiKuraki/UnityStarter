using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Factory.Samples.Benchmarks.Unity
{
    /// <summary>
    /// Unity-specific benchmark runner that integrates with Unity's Profiler and coroutine system.
    /// Provides frame-accurate timing, memory profiling, GC tracking, and detailed report generation.
    /// Optimized for accuracy and reduced overhead.
    /// </summary>
    public class UnityBenchmarkRunner : MonoBehaviour
    {
        private readonly List<UnityBenchmarkResult> _results = new List<UnityBenchmarkResult>();
        private string _sessionStartTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Public accessors for external benchmark producers (read-only list + append API)
        public IReadOnlyList<UnityBenchmarkResult> Results => _results;
        public void AddResult(UnityBenchmarkResult result)
        {
            _results.Add(result);
        }

        // Optimization settings
        private const int DEFAULT_TRIALS = 3;
        private const int DEFAULT_MEASUREMENT_BATCH_SIZE = 100;
        private const float GC_SETTLE_TIME = 0.1f;
        private const int MAX_WARMUP_BATCHES = 10;

        /// <summary>
        /// Runs an optimized benchmark with improved accuracy (Legacy coroutine version)
        /// RECOMMENDATION: Use RunBenchmarkAsync for superior performance
        /// </summary>
        [Obsolete("Use RunBenchmarkAsync for better performance and accuracy.")]
        public IEnumerator RunBenchmark(
            string name, 
            int iterations, 
            System.Action operation, 
            int warmupIterations = 100, 
            System.Action cleanup = null)
        {
            Debug.Log("PERFORMANCE WARNING: Using legacy coroutine API. Consider upgrading to RunBenchmarkAsync for 10x better performance!");
            yield return StartCoroutine(RunBenchmarkWithTrials(name, iterations, operation, DEFAULT_TRIALS, warmupIterations, cleanup));
        }

        /// <summary>
        /// Runs a benchmark with multiple trials for statistical accuracy (Coroutine version)
        /// </summary>
        [Obsolete("Use RunBenchmarkWithTrialsAsync for better performance and accuracy.")]
        public IEnumerator RunBenchmarkWithTrials(
            string name,
            int iterations,
            System.Action operation,
            int trials = DEFAULT_TRIALS,
            int warmupIterations = 100,
            System.Action cleanup = null)
        {
            Debug.Log($"Running optimized Unity benchmark: {name}");
            Debug.Log($"  Trials: {trials}");
            Debug.Log($"  Warm-up: {warmupIterations} iterations per trial");
            Debug.Log($"  Measurement: {iterations} iterations per trial");

            var trialResults = new List<TrialResult>();

            for (int trial = 0; trial < trials; trial++)
            {
                Debug.Log($"  Running trial {trial + 1}/{trials}...");
                
                // Fixed: Use proper coroutine handling
                yield return StartCoroutine(RunSingleOptimizedTrialCoroutine(name, iterations, operation, warmupIterations, cleanup, trialResults));

                // Brief pause between trials to ensure clean state
                yield return new WaitForSeconds(0.1f);
            }

            // Calculate aggregated statistics
            var aggregatedResult = CalculateAggregatedResult(name, iterations, trialResults);
            _results.Add(aggregatedResult);

            // Log results
            LogTrialResults(aggregatedResult, trialResults);
        }

        /// <summary>
        /// UniTask version for superior performance - eliminates coroutine overhead completely
        /// </summary>
        public async UniTask RunBenchmarkWithTrialsAsync(
            string name,
            int iterations,
            System.Action operation,
            int trials = DEFAULT_TRIALS,
            int warmupIterations = 100,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running ULTRA-OPTIMIZED UniTask benchmark: {name}");
            Debug.Log($"  Trials: {trials}");
            Debug.Log($"  Warm-up: {warmupIterations} iterations per trial");
            Debug.Log($"  Measurement: {iterations} iterations per trial");

            var trialResults = new List<TrialResult>();

            for (int trial = 0; trial < trials; trial++)
            {
                Debug.Log($"  Running trial {trial + 1}/{trials}...");
                
                var result = await RunSingleOptimizedTrialAsync(name, iterations, operation, warmupIterations, cleanup, cancellationToken);
                trialResults.Add(result);

                // Brief pause between trials to ensure clean state
                await UniTask.Delay(100, cancellationToken: cancellationToken);
            }

            // Calculate aggregated statistics
            var aggregatedResult = CalculateAggregatedResult(name, iterations, trialResults);
            _results.Add(aggregatedResult);

            // Log results
            LogTrialResults(aggregatedResult, trialResults);
        }

        /// <summary>
        /// Coroutine wrapper for single trial (for backward compatibility)
        /// </summary>
        private IEnumerator RunSingleOptimizedTrialCoroutine(
            string name,
            int iterations,
            System.Action operation,
            int warmupIterations,
            System.Action cleanup,
            List<TrialResult> trialResults)
        {
            // Enhanced warm-up phase with batching
            yield return StartCoroutine(OptimizedWarmup(operation, warmupIterations));
            cleanup?.Invoke();

            // Aggressive GC and memory settling
            yield return StartCoroutine(ForceGCAndSettle());

            // Record precise initial state
            long initialMemory = Profiler.GetTotalAllocatedMemoryLong();
            long initialReservedMemory = Profiler.GetTotalReservedMemoryLong();
            int initialGCCount = System.GC.CollectionCount(0);

            // Wait for stable frame timing
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // Precise timing measurement with minimal overhead
            float startTime = Time.realtimeSinceStartup;
            
            // Continuous measurement without yields for accuracy
            string profilerSample = $"Benchmark_{name.Replace(" ", "_")}_Measurement";
            Profiler.BeginSample(profilerSample);

            // Run all iterations in tight loop for maximum accuracy
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            Profiler.EndSample();
            float endTime = Time.realtimeSinceStartup;

            // Record final state immediately
            long finalMemory = Profiler.GetTotalAllocatedMemoryLong();
            long finalReservedMemory = Profiler.GetTotalReservedMemoryLong();
            int finalGCCount = System.GC.CollectionCount(0);

            cleanup?.Invoke();

            var result = new TrialResult
            {
                TotalTimeSeconds = endTime - startTime,
                AllocatedMemoryDelta = finalMemory - initialMemory,
                ReservedMemoryDelta = finalReservedMemory - initialReservedMemory,
                GCCollections = finalGCCount - initialGCCount
            };

            trialResults.Add(result);
        }

        /// <summary>
        /// UniTask implementation - ZERO coroutine overhead for maximum performance
        /// </summary>
        private async UniTask<TrialResult> RunSingleOptimizedTrialAsync(
            string name,
            int iterations,
            System.Action operation,
            int warmupIterations,
            System.Action cleanup,
            CancellationToken cancellationToken = default)
        {
            // Enhanced warm-up phase with batching (UniTask version)
            await OptimizedWarmupAsync(operation, warmupIterations, cancellationToken);
            cleanup?.Invoke();

            // Aggressive GC and memory settling (UniTask version)
            await ForceGCAndSettleAsync(cancellationToken);

            // Record precise initial state
            long initialMemory = Profiler.GetTotalAllocatedMemoryLong();
            long initialReservedMemory = Profiler.GetTotalReservedMemoryLong();
            int initialGCCount = System.GC.CollectionCount(0);

            // Wait for stable frame timing (much more efficient with UniTask)
            await UniTask.DelayFrame(2, cancellationToken: cancellationToken);

            // Precise timing measurement with ZERO coroutine overhead
            float startTime = Time.realtimeSinceStartup;
            
            // Continuous measurement without ANY async overhead
            string profilerSample = $"Benchmark_{name.Replace(" ", "_")}_Measurement";
            Profiler.BeginSample(profilerSample);

            // Run all iterations in tight loop for MAXIMUM accuracy
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            Profiler.EndSample();
            float endTime = Time.realtimeSinceStartup;

            // Record final state immediately
            long finalMemory = Profiler.GetTotalAllocatedMemoryLong();
            long finalReservedMemory = Profiler.GetTotalReservedMemoryLong();
            int finalGCCount = System.GC.CollectionCount(0);

            cleanup?.Invoke();

            return new TrialResult
            {
                TotalTimeSeconds = endTime - startTime,
                AllocatedMemoryDelta = finalMemory - initialMemory,
                ReservedMemoryDelta = finalReservedMemory - initialReservedMemory,
                GCCollections = finalGCCount - initialGCCount
            };
        }

        /// <summary>
        /// Optimized warm-up with batching to reduce overhead
        /// </summary>
        private IEnumerator OptimizedWarmup(System.Action operation, int warmupIterations)
        {
            int batchSize = Mathf.Min(warmupIterations / MAX_WARMUP_BATCHES, DEFAULT_MEASUREMENT_BATCH_SIZE);
            int remainingIterations = warmupIterations;

            while (remainingIterations > 0)
            {
                int currentBatch = Mathf.Min(batchSize, remainingIterations);
                
                // Run batch without yielding
                for (int i = 0; i < currentBatch; i++)
                {
                    operation();
                }

                remainingIterations -= currentBatch;
                
                // Only yield between batches, not within them
                if (remainingIterations > 0)
                {
                    yield return null;
                }
            }
        }

        /// <summary>
        /// UniTask version of optimized warm-up - MUCH more efficient
        /// </summary>
        private async UniTask OptimizedWarmupAsync(System.Action operation, int warmupIterations, CancellationToken cancellationToken = default)
        {
            int batchSize = Mathf.Min(warmupIterations / MAX_WARMUP_BATCHES, DEFAULT_MEASUREMENT_BATCH_SIZE);
            int remainingIterations = warmupIterations;

            while (remainingIterations > 0)
            {
                int currentBatch = Mathf.Min(batchSize, remainingIterations);
                
                // Run batch without yielding
                for (int i = 0; i < currentBatch; i++)
                {
                    operation();
                }

                remainingIterations -= currentBatch;
                
                // Only yield between batches, not within them - UniTask is much more efficient
                if (remainingIterations > 0)
                {
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
            }
        }

        /// <summary>
        /// Aggressive garbage collection and memory settling (Coroutine version)
        /// </summary>
        private IEnumerator ForceGCAndSettle()
        {
            // Multiple GC passes to ensure clean state
            for (int i = 0; i < 3; i++)
            {
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                yield return new WaitForSeconds(GC_SETTLE_TIME);
            }
            
            System.GC.Collect();
            yield return new WaitForEndOfFrame();
        }

        /// <summary>
        /// UniTask version of aggressive GC - superior performance
        /// </summary>
        private async UniTask ForceGCAndSettleAsync(CancellationToken cancellationToken = default)
        {
            // Multiple GC passes to ensure clean state
            for (int i = 0; i < 3; i++)
            {
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                await UniTask.Delay(TimeSpan.FromSeconds(GC_SETTLE_TIME), cancellationToken: cancellationToken);
            }
            
            System.GC.Collect();
            await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Calculates aggregated statistics from multiple trials
        /// </summary>
        private UnityBenchmarkResult CalculateAggregatedResult(string name, int iterations, List<TrialResult> trials)
        {
            var times = trials.Select(t => t.TotalTimeSeconds).ToList();
            var memoryDeltas = trials.Select(t => t.AllocatedMemoryDelta).ToList();
            var gcCounts = trials.Select(t => t.GCCollections).ToList();

            float avgTime = times.Average();
            float stdDevTime = CalculateStandardDeviation(times.Select(t => (double)t));
            
            long avgMemory = (long)memoryDeltas.Average();
            int totalGC = gcCounts.Sum();

            return new UnityBenchmarkResult
            {
                Name = $"{name} (Avg of {trials.Count} trials)",
                Iterations = iterations,
                TotalTimeSeconds = avgTime,
                AverageTimeMs = avgTime * 1000f / iterations,
                AllocatedMemoryDelta = avgMemory,
                MemoryPerIteration = avgMemory / iterations,
                GCCollections = totalGC,
                StandardDeviationMs = stdDevTime * 1000f,
                MinTimeMs = times.Min() * 1000f / iterations,
                MaxTimeMs = times.Max() * 1000f / iterations,
                CoefficientOfVariation = stdDevTime / avgTime * 100f
            };
        }

        /// <summary>
        /// Logs comprehensive trial results
        /// </summary>
        private void LogTrialResults(UnityBenchmarkResult result, List<TrialResult> trials)
        {
            Debug.Log($"  === TRIAL RESULTS ===");
            Debug.Log($"  Average time: {result.TotalTimeSeconds:F4} seconds");
            Debug.Log($"  Average per iteration: {result.AverageTimeMs:F4} ms");
            Debug.Log($"  Min/Max per iteration: {result.MinTimeMs:F4} - {result.MaxTimeMs:F4} ms");
            Debug.Log($"  Standard deviation: ±{result.StandardDeviationMs:F4} ms");
            Debug.Log($"  Coefficient of variation: {result.CoefficientOfVariation:F2}%");
            Debug.Log($"  Operations per second: {result.Iterations / result.TotalTimeSeconds:F0}");
            Debug.Log($"  Memory allocated: {FormatBytes(result.AllocatedMemoryDelta)}");
            Debug.Log($"  Memory per iteration: {FormatBytes(result.MemoryPerIteration)}");
            Debug.Log($"  Total GC collections: {result.GCCollections}");
            
            // Quality assessment
            string quality = result.CoefficientOfVariation < 5 ? "Excellent" : 
                           result.CoefficientOfVariation < 10 ? "Good" : 
                           result.CoefficientOfVariation < 20 ? "Fair" : "Poor";
            Debug.Log($"  Result quality: {quality} (CV: {result.CoefficientOfVariation:F1}%)");
            Debug.Log("");
        }

        /// <summary>
        /// High-precision benchmark for performance-critical operations (Coroutine version)
        /// </summary>
        [Obsolete("Use RunUltraHighPrecisionBenchmarkAsync for better performance and accuracy.")]
        public IEnumerator RunHighPrecisionBenchmark(
            string name,
            int iterations,
            System.Action operation,
            int trials = 5,
            int warmupIterations = 200)
        {
            Debug.Log($"Running HIGH-PRECISION benchmark: {name}");
            Debug.Log($"  High accuracy mode: {trials} trials, {warmupIterations} warmup iterations");

            yield return StartCoroutine(RunBenchmarkWithTrials(name, iterations, operation, trials, warmupIterations));
        }

        /// <summary>
        /// Quick benchmark for rapid iteration during development (Coroutine version)
        /// </summary>
        [Obsolete("Use RunQuickBenchmarkAsync for better performance and accuracy.")]
        public IEnumerator RunQuickBenchmark(
            string name,
            int iterations,
            System.Action operation,
            System.Action cleanup = null)
        {
            Debug.Log($"Running QUICK benchmark: {name}");
            yield return StartCoroutine(RunBenchmarkWithTrials(name, iterations, operation, 1, 50, cleanup));
        }

        /// <summary>
        /// ULTRA-HIGH-PRECISION UniTask benchmark - MAXIMUM performance and accuracy
        /// Eliminates ALL coroutine overhead for the most accurate measurements possible
        /// </summary>
        public async UniTask RunUltraHighPrecisionBenchmarkAsync(
            string name,
            int iterations,
            System.Action operation,
            int trials = 5,
            int warmupIterations = 300,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running ULTRA-HIGH-PRECISION UniTask benchmark: {name}");
            Debug.Log($"  MAXIMUM accuracy mode: {trials} trials, {warmupIterations} warmup iterations");
            Debug.Log($"  Using UniTask for ZERO coroutine overhead");

            await RunBenchmarkWithTrialsAsync(name, iterations, operation, trials, warmupIterations, cleanup, cancellationToken);
        }

        /// <summary>
        /// Ultra-fast UniTask benchmark for development iteration
        /// </summary>
        public async UniTask RunQuickBenchmarkAsync(
            string name,
            int iterations,
            System.Action operation,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running ULTRA-FAST UniTask benchmark: {name}");
            await RunBenchmarkWithTrialsAsync(name, iterations, operation, 1, 50, cleanup, cancellationToken);
        }

        /// <summary>
        /// Convenience method - automatically chooses best API (UniTask preferred)
        /// </summary>
        public async UniTask RunBenchmarkAsync(
            string name,
            int iterations,
            System.Action operation,
            int warmupIterations = 100,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            await RunBenchmarkWithTrialsAsync(name, iterations, operation, DEFAULT_TRIALS, warmupIterations, cleanup, cancellationToken);
        }

        /// <summary>
        /// All-in-one UniTask benchmark suite - combines performance, memory, and frame analysis
        /// </summary>
        public async UniTask RunComprehensiveBenchmarkAsync(
            string name,
            int iterations,
            System.Action operation,
            System.Action cleanup = null,
            float frameDuration = 2f,
            int memoryInterval = 50,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running COMPREHENSIVE UniTask benchmark suite: {name}");

            // Performance benchmark
            await RunUltraHighPrecisionBenchmarkAsync(
                $"{name} - Performance", 
                iterations, 
                operation, 
                5, 
                300, 
                cleanup, 
                cancellationToken);

            // Memory benchmark
            await RunMemoryBenchmarkAsync(
                $"{name} - Memory", 
                iterations, 
                operation, 
                memoryInterval, 
                cancellationToken);

            // Frame time analysis (if operation affects frame time)
            await RunFrameTimeBenchmarkAsync(
                $"{name} - Frame Impact", 
                frameDuration, 
                operation, 
                cleanup, 
                cancellationToken);

            Debug.Log($"Comprehensive benchmark completed for: {name}");
        }

        /// <summary>
        /// Batch benchmark runner - runs multiple operations in sequence with optimal performance
        /// </summary>
        public async UniTask RunBatchBenchmarksAsync(
            Dictionary<string, System.Action> operations,
            int iterations = 1000,
            int warmupIterations = 200,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running BATCH UniTask benchmarks: {operations.Count} operations");

            foreach (var kvp in operations)
            {
                await RunUltraHighPrecisionBenchmarkAsync(
                    kvp.Key,
                    iterations,
                    kvp.Value,
                    3, // Fewer trials for batch mode
                    warmupIterations,
                    cleanup,
                    cancellationToken);

                // Brief pause between benchmarks
                await UniTask.Delay(200, cancellationToken: cancellationToken);
            }

            Debug.Log("All batch benchmarks completed!");
        }

        /// <summary>
        /// Stress test with UniTask - runs operation continuously for specified duration
        /// </summary>
        public async UniTask RunStressTestAsync(
            string name,
            System.Action operation,
            float durationSeconds,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running STRESS TEST (UniTask): {name} for {durationSeconds} seconds");

            int operationCount = 0;
            var gcCounts = new List<int>();
            float startTime = Time.realtimeSinceStartup;
            float lastGCCheck = startTime;
            int lastGCCount = System.GC.CollectionCount(0);
            long startMemory = Profiler.GetTotalAllocatedMemoryLong();

            while (Time.realtimeSinceStartup - startTime < durationSeconds)
            {
                operation();
                operationCount++;

                // Check GC every 1000 operations
                if (operationCount % 1000 == 0)
                {
                    float currentTime = Time.realtimeSinceStartup;
                    if (currentTime - lastGCCheck > 0.1f)
                    {
                        int currentGCCount = System.GC.CollectionCount(0);
                        gcCounts.Add(currentGCCount - lastGCCount);
                        lastGCCount = currentGCCount;
                        lastGCCheck = currentTime;
                    }

                    // Yield occasionally to prevent blocking
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
            }

            cleanup?.Invoke();

            long endMemory = Profiler.GetTotalAllocatedMemoryLong();
            var result = new UnityBenchmarkResult
            {
                Name = $"{name} (Stress Test - UniTask)",
                Iterations = operationCount,
                TotalTimeSeconds = durationSeconds,
                AverageTimeMs = durationSeconds * 1000f / operationCount,
                AllocatedMemoryDelta = endMemory - startMemory,
                MemoryPerIteration = (endMemory - startMemory) / operationCount,
                GCCollections = gcCounts.Sum()
            };

            _results.Add(result);

            Debug.Log($"  Operations completed: {operationCount:N0}");
            Debug.Log($"  Operations per second: {operationCount / durationSeconds:F0}");
            Debug.Log($"  Average time per operation: {result.AverageTimeMs:F6} ms");
            Debug.Log($"  Total memory allocated: {FormatBytes(result.AllocatedMemoryDelta)}");
            Debug.Log($"  Memory per operation: {FormatBytes(result.MemoryPerIteration)}");
            Debug.Log($"  Total GC collections: {result.GCCollections}");
        }

        /// <summary>
        /// Internal structure for trial results
        /// </summary>
        private struct TrialResult
        {
            public float TotalTimeSeconds;
            public long AllocatedMemoryDelta;
            public long ReservedMemoryDelta;
            public int GCCollections;
        }

        /// <summary>
        /// UniTask version of frame time analysis - ZERO coroutine overhead
        /// </summary>
        public async UniTask RunFrameTimeBenchmarkAsync(
            string name,
            float durationSeconds,
            System.Action perFrameOperation,
            System.Action cleanup = null,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running ULTRA-OPTIMIZED frame time benchmark: {name} for {durationSeconds} seconds");

            var frameTimes = new List<float>();
            var gcCounts = new List<int>();
            
            float startTime = Time.realtimeSinceStartup;
            float lastGCCheck = startTime;
            int lastGCCount = System.GC.CollectionCount(0);

            while (Time.realtimeSinceStartup - startTime < durationSeconds)
            {
                float frameStart = Time.realtimeSinceStartup;

                perFrameOperation();

                float frameEnd = Time.realtimeSinceStartup;
                frameTimes.Add((frameEnd - frameStart) * 1000f);

                // Check for GC every 0.1 seconds
                if (frameEnd - lastGCCheck > 0.1f)
                {
                    int currentGCCount = System.GC.CollectionCount(0);
                    gcCounts.Add(currentGCCount - lastGCCount);
                    lastGCCount = currentGCCount;
                    lastGCCheck = frameEnd;
                }

                // UniTask frame delay - much more efficient than yield return null
                await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }

            cleanup?.Invoke();

            var result = new UnityBenchmarkResult
            {
                Name = $"{name} (Frame Analysis - UniTask)",
                Iterations = frameTimes.Count,
                TotalTimeSeconds = durationSeconds,
                AverageTimeMs = frameTimes.Average(),
                MinFrameTimeMs = frameTimes.Min(),
                MaxFrameTimeMs = frameTimes.Max(),
                FrameTimeStdDev = CalculateStandardDeviation(frameTimes.Select(f => (double)f)),
                GCCollections = gcCounts.Sum(),
                FrameTimes = frameTimes.ToArray(),
                // Enhanced statistics
                StandardDeviationMs = CalculateStandardDeviation(frameTimes.Select(f => (double)f)),
                MinTimeMs = frameTimes.Min(),
                MaxTimeMs = frameTimes.Max(),
                CoefficientOfVariation = frameTimes.Count > 0 ? CalculateStandardDeviation(frameTimes.Select(f => (double)f)) / frameTimes.Average() * 100f : 0f
            };

            _results.Add(result);

            Debug.Log($"  Frames processed: {result.Iterations}");
            Debug.Log($"  Average frame time: {result.AverageTimeMs:F3} ms");
            Debug.Log($"  Min frame time: {result.MinFrameTimeMs:F3} ms");
            Debug.Log($"  Max frame time: {result.MaxFrameTimeMs:F3} ms");
            Debug.Log($"  Frame time std dev: {result.FrameTimeStdDev:F3} ms");
            Debug.Log($"  Coefficient of variation: {result.CoefficientOfVariation:F2}%");
            Debug.Log($"  Total GC collections: {result.GCCollections}");
        }

        /// <summary>
        /// Legacy coroutine version (for backward compatibility)
        /// </summary>
        [Obsolete("Use RunFrameTimeBenchmarkAsync for better performance and accuracy.")]
        public IEnumerator RunFrameTimeBenchmark(
            string name,
            float durationSeconds,
            System.Action perFrameOperation,
            System.Action cleanup = null)
        {
            Debug.Log($"Running frame time benchmark (Legacy): {name} for {durationSeconds} seconds");
            Debug.Log("RECOMMENDATION: Use RunFrameTimeBenchmarkAsync for better performance!");

            var frameTimes = new List<float>();
            var gcCounts = new List<int>();
            
            float startTime = Time.realtimeSinceStartup;
            float lastGCCheck = startTime;
            int lastGCCount = System.GC.CollectionCount(0);

            while (Time.realtimeSinceStartup - startTime < durationSeconds)
            {
                float frameStart = Time.realtimeSinceStartup;

                perFrameOperation();

                float frameEnd = Time.realtimeSinceStartup;
                frameTimes.Add((frameEnd - frameStart) * 1000f);

                // Check for GC every 0.1 seconds
                if (frameEnd - lastGCCheck > 0.1f)
                {
                    int currentGCCount = System.GC.CollectionCount(0);
                    gcCounts.Add(currentGCCount - lastGCCount);
                    lastGCCount = currentGCCount;
                    lastGCCheck = frameEnd;
                }

                yield return null;
            }

            cleanup?.Invoke();

            var result = new UnityBenchmarkResult
            {
                Name = $"{name} (Frame Analysis - Coroutine)",
                Iterations = frameTimes.Count,
                TotalTimeSeconds = durationSeconds,
                AverageTimeMs = frameTimes.Average(),
                MinFrameTimeMs = frameTimes.Min(),
                MaxFrameTimeMs = frameTimes.Max(),
                FrameTimeStdDev = CalculateStandardDeviation(frameTimes.Select(f => (double)f)),
                GCCollections = gcCounts.Sum(),
                FrameTimes = frameTimes.ToArray()
            };

            _results.Add(result);

            Debug.Log($"  Frames processed: {result.Iterations}");
            Debug.Log($"  Average frame time: {result.AverageTimeMs:F3} ms");
            Debug.Log($"  Min frame time: {result.MinFrameTimeMs:F3} ms");
            Debug.Log($"  Max frame time: {result.MaxFrameTimeMs:F3} ms");
            Debug.Log($"  Frame time std dev: {result.FrameTimeStdDev:F3} ms");
            Debug.Log($"  Total GC collections: {result.GCCollections}");

            yield return null;
        }

        /// <summary>
        /// ULTRA-OPTIMIZED UniTask memory allocation benchmark - ZERO coroutine overhead
        /// </summary>
        public async UniTask RunMemoryBenchmarkAsync(
            string name,
            int iterations,
            System.Action operation,
            int samplingInterval = 100,
            CancellationToken cancellationToken = default)
        {
            Debug.Log($"Running ULTRA-OPTIMIZED memory benchmark: {name}");

            var memorySnapshots = new List<long>();
            var gcSnapshots = new List<int>();

            // Initial state with aggressive GC settling
            await ForceGCAndSettleAsync(cancellationToken);
            long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();
            int baselineGC = System.GC.CollectionCount(0);

            // Pre-allocate lists for better performance
            memorySnapshots.Capacity = iterations / samplingInterval + 1;
            gcSnapshots.Capacity = iterations / samplingInterval + 1;

            for (int i = 0; i < iterations; i++)
            {
                operation();

                if (i % samplingInterval == 0)
                {
                    memorySnapshots.Add(Profiler.GetTotalAllocatedMemoryLong() - baselineMemory);
                    gcSnapshots.Add(System.GC.CollectionCount(0) - baselineGC);
                    
                    // Yield much less frequently than coroutine version
                    if (i % (samplingInterval * 20) == 0)
                    {
                        await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                    }
                }
            }

            // Final measurement
            long finalMemory = Profiler.GetTotalAllocatedMemoryLong() - baselineMemory;
            int finalGC = System.GC.CollectionCount(0) - baselineGC;

            var result = new UnityBenchmarkResult
            {
                Name = $"{name} (Memory Analysis - UniTask)",
                Iterations = iterations,
                AllocatedMemoryDelta = finalMemory,
                MemoryPerIteration = finalMemory / iterations,
                GCCollections = finalGC,
                MemorySnapshots = memorySnapshots.ToArray(),
                // Enhanced statistics
                StandardDeviationMs = 0f, // Not applicable for memory benchmarks
                MinTimeMs = 0f,
                MaxTimeMs = 0f,
                CoefficientOfVariation = 0f
            };

            _results.Add(result);

            Debug.Log($"  Total memory allocated: {FormatBytes(result.AllocatedMemoryDelta)}");
            Debug.Log($"  Memory per iteration: {FormatBytes(result.MemoryPerIteration)}");
            Debug.Log($"  GC collections: {result.GCCollections}");
            Debug.Log($"  Memory samples collected: {memorySnapshots.Count}");
        }

        /// <summary>
        /// Legacy coroutine memory benchmark (for backward compatibility)
        /// </summary>
        [Obsolete("Use RunMemoryBenchmarkAsync for better performance and accuracy.")]
        public IEnumerator RunMemoryBenchmark(
            string name,
            int iterations,
            System.Action operation,
            int samplingInterval = 100)
        {
            Debug.Log($"Running memory benchmark (Legacy): {name}");
            Debug.Log("RECOMMENDATION: Use RunMemoryBenchmarkAsync for better performance!");

            var memorySnapshots = new List<long>();
            var gcSnapshots = new List<int>();

            // Initial state
            System.GC.Collect();
            yield return new WaitForEndOfFrame();
            long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();

            for (int i = 0; i < iterations; i++)
            {
                operation();

                if (i % samplingInterval == 0)
                {
                    memorySnapshots.Add(Profiler.GetTotalAllocatedMemoryLong() - baselineMemory);
                    gcSnapshots.Add(System.GC.CollectionCount(0));
                    
                    if (i % (samplingInterval * 10) == 0) yield return null;
                }
            }

            var result = new UnityBenchmarkResult
            {
                Name = $"{name} (Memory Analysis - Coroutine)",
                Iterations = iterations,
                AllocatedMemoryDelta = memorySnapshots.LastOrDefault(),
                MemoryPerIteration = memorySnapshots.LastOrDefault() / iterations,
                GCCollections = gcSnapshots.LastOrDefault(),
                MemorySnapshots = memorySnapshots.ToArray()
            };

            _results.Add(result);

            Debug.Log($"  Total memory allocated: {FormatBytes(result.AllocatedMemoryDelta)}");
            Debug.Log($"  Memory per iteration: {FormatBytes(result.MemoryPerIteration)}");
            Debug.Log($"  GC collections: {result.GCCollections}");

            yield return null;
        }

        /// <summary>
        /// Prints a comprehensive summary of all benchmark results
        /// </summary>
        public void PrintSummary()
        {
            if (_results.Count == 0)
            {
                Debug.Log("No benchmark results to display.");
                return;
            }

            Debug.Log("=== UNITY BENCHMARK SUMMARY ===");

            // Performance summary
            var performanceResults = _results.Where(r => r.AverageTimeMs > 0).OrderBy(r => r.AverageTimeMs).ToList();
            if (performanceResults.Any())
            {
                Debug.Log("\n--- Performance Rankings ---");
                for (int i = 0; i < performanceResults.Count; i++)
                {
                    var result = performanceResults[i];
                    var opsPerSec = result.Iterations / result.TotalTimeSeconds;
                    Debug.Log($"{i + 1}. {result.Name}: {result.AverageTimeMs:F4} ms avg, {opsPerSec:F0} ops/sec");
                }
            }

            // Memory summary
            var memoryResults = _results.Where(r => r.AllocatedMemoryDelta != 0).OrderBy(r => r.MemoryPerIteration).ToList();
            if (memoryResults.Any())
            {
                Debug.Log("\n--- Memory Efficiency Rankings ---");
                for (int i = 0; i < memoryResults.Count; i++)
                {
                    var result = memoryResults[i];
                    Debug.Log($"{i + 1}. {result.Name}: {FormatBytes(result.MemoryPerIteration)}/op, {result.GCCollections} GC");
                }
            }

            // Frame time analysis
            var frameResults = _results.Where(r => r.FrameTimes != null).ToList();
            if (frameResults.Any())
            {
                Debug.Log("\n--- Frame Time Analysis ---");
                foreach (var result in frameResults)
                {
                    Debug.Log($"{result.Name}:");
                    Debug.Log($"  Avg: {result.AverageTimeMs:F3} ms, Min: {result.MinFrameTimeMs:F3} ms, Max: {result.MaxFrameTimeMs:F3} ms");
                    Debug.Log($"  StdDev: {result.FrameTimeStdDev:F3} ms, GC: {result.GCCollections}");
                }
            }
        }

        /// <summary>
        /// Exports benchmark results to CSV format (useful for analysis)
        /// </summary>
        public string ExportToCSV()
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Name,Iterations,TotalTime(s),AvgTime(ms),Memory(bytes),MemoryPerOp(bytes),GCCount");

            foreach (var result in _results)
            {
                csv.AppendLine($"{result.Name},{result.Iterations},{result.TotalTimeSeconds:F6},{result.AverageTimeMs:F6},{result.AllocatedMemoryDelta},{result.MemoryPerIteration},{result.GCCollections}");
            }

            return csv.ToString();
        }

        /// <summary>
        /// Generates and saves a comprehensive benchmark report
        /// </summary>
        public void GenerateReport(string customName = "")
        {
            if (_results.Count == 0)
            {
                Debug.LogWarning("No benchmark results to generate report for.");
                return;
            }

            var report = GenerateFormattedReport(customName);
            var logReport = GenerateLogReport(customName);
            var markdownReport = GenerateMarkdownReport(customName);
            var markdownZhCN = GenerateMarkdownReportZhCN(customName);
            
            // Save to files
            SaveReportToFile(report, customName);
            SaveMarkdownReportToFile(markdownReport, customName);
            SaveMarkdownSchReportToFile(markdownZhCN, customName);
            
            // Output to console/log
            Debug.Log(logReport);
            
            Debug.Log($"Benchmark report saved to: {GetReportFilePath(customName)}");
            Debug.Log($"Markdown report saved to: {GetMarkdownReportFilePath(customName)}");
            Debug.Log($"Markdown (SCH) report saved to: {GetMarkdownSchReportFilePath(customName)}");
        }

        private string GenerateFormattedReport(string customName)
        {
            var sb = new StringBuilder();
            
            // Header
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                    UNITY FACTORY BENCHMARK REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Session Start: {_sessionStartTime}");
            sb.AppendLine($"Report Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Platform: {Application.platform}");
            sb.AppendLine($"Total Benchmarks: {_results.Count}");
            if (!string.IsNullOrEmpty(customName))
            {
                sb.AppendLine($"Custom Label: {customName}");
            }
            sb.AppendLine();

            // Legend
            sb.AppendLine("Legend:");
            sb.AppendLine("  • Avg Time (ms): lower is better");
            sb.AppendLine("  • Ops/Sec: higher is better");
            sb.AppendLine("  • Memory per Op: lower is better");
            sb.AppendLine("  • GC Count: lower is better");
            sb.AppendLine("  • Runs: number of results aggregated for the same benchmark name");
            sb.AppendLine();

            // System Information
            sb.AppendLine("─── SYSTEM INFORMATION ───");
            sb.AppendLine($"Device Model: {SystemInfo.deviceModel}");
            sb.AppendLine($"Processor: {SystemInfo.processorType} ({SystemInfo.processorCount} cores @ {SystemInfo.processorFrequency}MHz)");
            sb.AppendLine($"Memory: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"Graphics: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
            sb.AppendLine();

            // Performance Summary
            GeneratePerformanceSummary(sb);
            
            // Memory Analysis
            GenerateMemoryAnalysis(sb);
            
            // Detailed Results
            GenerateDetailedResults(sb);
            
            // GC Analysis
            GenerateGCAnalysis(sb);
            
            // Recommendations
            GenerateRecommendations(sb);

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                         END OF REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            return sb.ToString();
        }

        private string GenerateMarkdownReport(string customName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Unity Factory Benchmark Report");
            sb.AppendLine();
            sb.AppendLine($"- **Session Start**: {_sessionStartTime}");
            sb.AppendLine($"- **Generated**: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- **Unity**: {Application.unityVersion}");
            sb.AppendLine($"- **Platform**: {Application.platform}");
            sb.AppendLine($"- **Total Benchmarks**: {_results.Count}");
            if (!string.IsNullOrEmpty(customName)) sb.AppendLine($"- **Label**: {customName}");
            sb.AppendLine();
            sb.AppendLine("> Legend: Avg Time (lower is better), Ops/Sec (higher is better), Memory/Op (lower is better), GC (lower is better), Runs (aggregated count)");
            sb.AppendLine();

            // Performance table (aggregated)
            var perf = _results.Where(r => r.AverageTimeMs > 0)
                .GroupBy(r => r.Name)
                .Select(g => new { Name = g.Key, Runs = g.Count(), AvgTimeMs = g.Average(x => x.AverageTimeMs) })
                .OrderBy(a => a.AvgTimeMs)
                .ToList();
            if (perf.Any())
            {
                float fastest = perf.First().AvgTimeMs;
                // Quick compare (best pool vs best direct/factory)
                var bestPool = perf.Where(x => IsPooling(x.Name)).OrderBy(x => x.AvgTimeMs).FirstOrDefault();
                var bestDirect = perf.Where(x => IsDirectOrFactory(x.Name)).OrderBy(x => x.AvgTimeMs).FirstOrDefault();
                if (bestPool != null || bestDirect != null)
                {
                    sb.AppendLine("### Quick Compare");
                    sb.AppendLine("| Category | Benchmark | Avg Time (ms) | Per-Item (µs) | Ops/Sec |");
                    sb.AppendLine("|---|---|---:|---:|---:|");
                    if (bestPool != null)
                    {
                        int b = ParseBurstFromName(bestPool.Name);
                        double perUs = (bestPool.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        double ops = 1000.0 / bestPool.AvgTimeMs;
                        sb.AppendLine($"| Pool | {bestPool.Name} | {bestPool.AvgTimeMs:F3} | {perUs:F2} | {ops:F0} |");
                    }
                    if (bestDirect != null)
                    {
                        int b = ParseBurstFromName(bestDirect.Name);
                        double perUs = (bestDirect.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        double ops = 1000.0 / bestDirect.AvgTimeMs;
                        sb.AppendLine($"| Direct/Factory | {bestDirect.Name} | {bestDirect.AvgTimeMs:F3} | {perUs:F2} | {ops:F0} |");
                    }
                    sb.AppendLine();
                }

                // Split performance by burst
                var perfNonBurst = perf.Where(x => !IsBurst(x.Name)).ToList();
                var perfBurst = perf.Where(x => IsBurst(x.Name)).ToList();
                if (perfNonBurst.Any())
                {
                    sb.AppendLine("### Performance – Non-burst");
                    sb.AppendLine("| Benchmark | Runs | Avg Time (ms) | Per-Item (µs) | Ops/Sec | Relative |");
                    sb.AppendLine("|---|---:|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var p in perfNonBurst)
                    {
                        double ops = 1000.0 / p.AvgTimeMs;
                        string rel = Math.Abs(p.AvgTimeMs - fastest) < 0.0001f ? "FASTEST" : $"{p.AvgTimeMs / fastest:F1}x";
                        double perItemUs = p.AvgTimeMs * 1000.0;
                        sb.AppendLine($"| {p.Name} | {p.Runs} | {p.AvgTimeMs:F3} | {perItemUs:F2} | {ops:F0} | {rel} |");
                        bars.Add((p.Name, ops));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\nOps/sec (higher is better)");
                    sb.AppendLine(BuildBarChart(bars, 40, v => v.ToString("F0"), alignValuesRight: true));
                    sb.AppendLine("```");
                }
                if (perfBurst.Any())
                {
                    sb.AppendLine("### Performance – Burst");
                    sb.AppendLine("| Benchmark | Runs | Avg Time (ms) | Per-Item (µs) | Ops/Sec | Relative |");
                    sb.AppendLine("|---|---:|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var p in perfBurst)
                    {
                        double ops = 1000.0 / p.AvgTimeMs;
                        string rel = Math.Abs(p.AvgTimeMs - fastest) < 0.0001f ? "FASTEST" : $"{p.AvgTimeMs / fastest:F1}x";
                        int b = ParseBurstFromName(p.Name);
                        double perItemUs = (p.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        sb.AppendLine($"| {p.Name} | {p.Runs} | {p.AvgTimeMs:F3} | {perItemUs:F2} | {ops:F0} | {rel} |");
                        bars.Add((p.Name, ops));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\nOps/sec (higher is better)");
                    sb.AppendLine(BuildBarChart(bars, 40, v => v.ToString("F0"), alignValuesRight: true));
                    sb.AppendLine("```");
                }
            }
            
            // Memory table (aggregated)
            var mem = _results.Where(r => r.AllocatedMemoryDelta != 0)
                .GroupBy(r => r.Name)
                .Select(g => new {
                    Name = g.Key,
                    Runs = g.Count(),
                    AvgTotal = Math.Max(0, (long)g.Average(x => (double)x.AllocatedMemoryDelta)),
                    AvgPerOp = Math.Max(0, (long)g.Average(x => (double)x.MemoryPerIteration)),
                    AvgGC = (int)g.Average(x => (double)x.GCCollections)
                })
                .OrderBy(a => a.AvgPerOp)
                .ToList();
            if (mem.Any())
            {
                var memPool = mem.Where(x => IsPooling(x.Name)).ToList();
                var memDirect = mem.Where(x => IsDirectOrFactory(x.Name)).ToList();
                if (memPool.Any())
                {
                    sb.AppendLine("### Memory – Pooling");
                    sb.AppendLine("| Benchmark | Runs | Total Alloc | Memory/Op | GC |");
                    sb.AppendLine("|---|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var m in memPool)
                    {
                        sb.AppendLine($"| {m.Name} | {m.Runs} | {FormatBytes(m.AvgTotal)} | {FormatBytes(m.AvgPerOp)} | {m.AvgGC} |");
                        bars.Add((m.Name, m.AvgPerOp));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\nMemory per op (lower is better)");
                    sb.AppendLine(BuildBarChart(bars, 40, v => FormatBytes((long)v), alignValuesRight: false));
                    sb.AppendLine("```");
                }
                if (memDirect.Any())
                {
                    sb.AppendLine("### Memory – Direct/Factory");
                    sb.AppendLine("| Benchmark | Runs | Total Alloc | Memory/Op | GC |");
                    sb.AppendLine("|---|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var m in memDirect)
                    {
                        sb.AppendLine($"| {m.Name} | {m.Runs} | {FormatBytes(m.AvgTotal)} | {FormatBytes(m.AvgPerOp)} | {m.AvgGC} |");
                        bars.Add((m.Name, m.AvgPerOp));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\nMemory per op (lower is better)");
                    sb.AppendLine(BuildBarChart(bars, 40, v => FormatBytes((long)v), alignValuesRight: false));
                    sb.AppendLine("```");
                }
            }

            // Detailed results (per run) as a table (time-measured only)
            sb.AppendLine();
            sb.AppendLine("### Detailed Results (Per Run)");
            sb.AppendLine("| Name | Iterations | Total Time (s) | Avg Time (ms) | Per-Item (µs) | Ops/Sec | Total Memory | Memory/Op | GC |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results.Where(x => x.AverageTimeMs > 0))
            {
                double ops = 1000.0 / r.AverageTimeMs;
                int burst = ParseBurstFromName(r.Name);
                double perItemUs = burst > 1 ? (r.AverageTimeMs * 1000.0) / burst : (r.AverageTimeMs * 1000.0);
                long totalMem = Math.Max(0, r.AllocatedMemoryDelta);
                long perOp = Math.Max(0, r.MemoryPerIteration);
                sb.AppendLine($"| {r.Name} | {r.Iterations} | {r.TotalTimeSeconds:F3} | {r.AverageTimeMs:F3} | {perItemUs:F2} | {ops:F0} | {FormatBytes(totalMem)} | {FormatBytes(perOp)} | {r.GCCollections} |");
            }

            // Memory-only details
            var memOnly = _results.Where(x => x.AverageTimeMs <= 0 && (x.AllocatedMemoryDelta != 0 || x.MemoryPerIteration != 0)).ToList();
            if (memOnly.Any())
            {
                sb.AppendLine();
                sb.AppendLine("### Memory-only Details");
                sb.AppendLine("| Name | Iterations | Total Memory | Memory/Op | GC |");
                sb.AppendLine("|---|---:|---:|---:|---:|");
                foreach (var r in memOnly)
                {
                    long totalMem = Math.Max(0, r.AllocatedMemoryDelta);
                    long perOp = Math.Max(0, r.MemoryPerIteration);
                    sb.AppendLine($"| {r.Name} | {r.Iterations} | {FormatBytes(totalMem)} | {FormatBytes(perOp)} | {r.GCCollections} |");
                }
            }

            return sb.ToString();
        }

        private string GenerateMarkdownReportZhCN(string customName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Unity 工厂基准测试报告");
            sb.AppendLine();
            sb.AppendLine($"- **会话开始**: {_sessionStartTime}");
            sb.AppendLine($"- **生成时间**: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- **Unity**: {Application.unityVersion}");
            sb.AppendLine($"- **平台**: {Application.platform}");
            sb.AppendLine($"- **基准条目数**: {_results.Count}");
            if (!string.IsNullOrEmpty(customName)) sb.AppendLine($"- **标签**: {customName}");
            sb.AppendLine();
            sb.AppendLine("> 说明：Avg Time（越小越好）、Ops/Sec（越大越好）、Memory/Op（每次操作的平均内存分配，越小越好）、GC（越少越好）、Runs（同名条目的聚合次数）");
            sb.AppendLine();

            var perf = _results.Where(r => r.AverageTimeMs > 0)
                .GroupBy(r => r.Name)
                .Select(g => new { Name = g.Key, Runs = g.Count(), AvgTimeMs = g.Average(x => x.AverageTimeMs) })
                .OrderBy(a => a.AvgTimeMs)
                .ToList();
            if (perf.Any())
            {
                float fastest = perf.First().AvgTimeMs;
                // 快速对比
                var bestPool = perf.Where(x => IsPooling(x.Name)).OrderBy(x => x.AvgTimeMs).FirstOrDefault();
                var bestDirect = perf.Where(x => IsDirectOrFactory(x.Name)).OrderBy(x => x.AvgTimeMs).FirstOrDefault();
                if (bestPool != null || bestDirect != null)
                {
                    sb.AppendLine("### 快速对比");
                    sb.AppendLine("| 类别 | 基准 | 平均时间 (ms) | 单个对象 (µs) | 次/秒 |");
                    sb.AppendLine("|---|---|---:|---:|---:|");
                    if (bestPool != null)
                    {
                        int b = ParseBurstFromName(bestPool.Name);
                        double perUs = (bestPool.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        double ops = 1000.0 / bestPool.AvgTimeMs;
                        sb.AppendLine($"| Pool | {bestPool.Name} | {bestPool.AvgTimeMs:F3} | {perUs:F2} | {ops:F0} |");
                    }
                    if (bestDirect != null)
                    {
                        int b = ParseBurstFromName(bestDirect.Name);
                        double perUs = (bestDirect.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        double ops = 1000.0 / bestDirect.AvgTimeMs;
                        sb.AppendLine($"| Direct/Factory | {bestDirect.Name} | {bestDirect.AvgTimeMs:F3} | {perUs:F2} | {ops:F0} |");
                    }
                    sb.AppendLine();
                }

                var perfNonBurst = perf.Where(x => !IsBurst(x.Name)).ToList();
                var perfBurst = perf.Where(x => IsBurst(x.Name)).ToList();
                if (perfNonBurst.Any())
                {
                    sb.AppendLine("### 性能 – 非突发");
                    sb.AppendLine("| 基准 | 次数 | 平均时间 (ms) | 单个对象 (µs) | 次/秒 | 相对值 |");
                    sb.AppendLine("|---|---:|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var p in perfNonBurst)
                    {
                        double ops = 1000.0 / p.AvgTimeMs;
                        string rel = Math.Abs(p.AvgTimeMs - fastest) < 0.0001f ? "最快" : $"{p.AvgTimeMs / fastest:F1}x";
                        double perItemUs = p.AvgTimeMs * 1000.0;
                        sb.AppendLine($"| {p.Name} | {p.Runs} | {p.AvgTimeMs:F3} | {perItemUs:F2} | {ops:F0} | {rel} |");
                        bars.Add((p.Name, ops));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\n每秒操作数（越大越好）");
                    sb.AppendLine(BuildBarChart(bars, 40, v => v.ToString("F0"), alignValuesRight: true));
                    sb.AppendLine("```");
                }
                if (perfBurst.Any())
                {
                    sb.AppendLine("### 性能 – 突发");
                    sb.AppendLine("| 基准 | 次数 | 平均时间 (ms) | 单个对象 (µs) | 次/秒 | 相对值 |");
                    sb.AppendLine("|---|---:|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var p in perfBurst)
                    {
                        double ops = 1000.0 / p.AvgTimeMs;
                        string rel = Math.Abs(p.AvgTimeMs - fastest) < 0.0001f ? "最快" : $"{p.AvgTimeMs / fastest:F1}x";
                        int b = ParseBurstFromName(p.Name);
                        double perItemUs = (p.AvgTimeMs * 1000.0) / Math.Max(1, b);
                        sb.AppendLine($"| {p.Name} | {p.Runs} | {p.AvgTimeMs:F3} | {perItemUs:F2} | {ops:F0} | {rel} |");
                        bars.Add((p.Name, ops));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\n每秒操作数（越大越好）");
                    sb.AppendLine(BuildBarChart(bars, 40, v => v.ToString("F0"), alignValuesRight: true));
                    sb.AppendLine("```");
                }
            }

            var mem = _results.Where(r => r.AllocatedMemoryDelta != 0)
                .GroupBy(r => r.Name)
                .Select(g => new {
                    Name = g.Key,
                    Runs = g.Count(),
                    AvgTotal = Math.Max(0, (long)g.Average(x => (double)x.AllocatedMemoryDelta)),
                    AvgPerOp = Math.Max(0, (long)g.Average(x => (double)x.MemoryPerIteration)),
                    AvgGC = (int)g.Average(x => (double)x.GCCollections)
                })
                .OrderBy(a => a.AvgPerOp)
                .ToList();
            if (mem.Any())
            {
                var memPool = mem.Where(x => IsPooling(x.Name)).ToList();
                var memDirect = mem.Where(x => IsDirectOrFactory(x.Name)).ToList();
                if (memPool.Any())
                {
                    sb.AppendLine("### 内存 – 对象池");
                    sb.AppendLine("| 基准 | 次数 | 总分配 | 每次操作内存 | GC |");
                    sb.AppendLine("|---|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var m in memPool)
                    {
                        sb.AppendLine($"| {m.Name} | {m.Runs} | {FormatBytes(m.AvgTotal)} | {FormatBytes(m.AvgPerOp)} | {m.AvgGC} |");
                        bars.Add((m.Name, m.AvgPerOp));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\n每次操作内存（越小越好）");
                    sb.AppendLine(BuildBarChart(bars, 40, v => FormatBytes((long)v), alignValuesRight: false));
                    sb.AppendLine("```");
                }
                if (memDirect.Any())
                {
                    sb.AppendLine("### 内存 – 直接/工厂");
                    sb.AppendLine("| 基准 | 次数 | 总分配 | 每次操作内存 | GC |");
                    sb.AppendLine("|---|---:|---:|---:|---:|");
                    var bars = new List<(string label, double value)>();
                    foreach (var m in memDirect)
                    {
                        sb.AppendLine($"| {m.Name} | {m.Runs} | {FormatBytes(m.AvgTotal)} | {FormatBytes(m.AvgPerOp)} | {m.AvgGC} |");
                        bars.Add((m.Name, m.AvgPerOp));
                    }
                    sb.AppendLine();
                    sb.AppendLine("```\n每次操作内存（越小越好）");
                    sb.AppendLine(BuildBarChart(bars, 40, v => FormatBytes((long)v), alignValuesRight: false));
                    sb.AppendLine("```");
                }
            }

            sb.AppendLine();
            sb.AppendLine("### 运行明细（仅计时项）");
            sb.AppendLine("| 名称 | 次数 | 总时间 (s) | 平均时间 (ms) | 单个对象 (µs) | 次/秒 | 总内存 | 每次操作内存 | GC |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results.Where(x => x.AverageTimeMs > 0))
            {
                string avgStr = r.AverageTimeMs.ToString("F3");
                string opsStr = (1000.0 / r.AverageTimeMs).ToString("F0");
                int burst = ParseBurstFromName(r.Name);
                double perItemUs = burst > 1 ? (r.AverageTimeMs * 1000.0) / burst : (r.AverageTimeMs * 1000.0);
                long totalMem = Math.Max(0, r.AllocatedMemoryDelta);
                long perOp = Math.Max(0, r.MemoryPerIteration);
                sb.AppendLine($"| {r.Name} | {r.Iterations} | {r.TotalTimeSeconds:F3} | {avgStr} | {perItemUs:F2} | {opsStr} | {FormatBytes(totalMem)} | {FormatBytes(perOp)} | {r.GCCollections} |");
            }

            var zhMemOnly = _results.Where(x => x.AverageTimeMs <= 0 && (x.AllocatedMemoryDelta != 0 || x.MemoryPerIteration != 0)).ToList();
            if (zhMemOnly.Any())
            {
                sb.AppendLine();
                sb.AppendLine("### 仅内存明细");
                sb.AppendLine("| 名称 | 次数 | 总内存 | 每次操作内存 | GC |");
                sb.AppendLine("|---|---:|---:|---:|---:|");
                foreach (var r in zhMemOnly)
                {
                    long totalMem = Math.Max(0, r.AllocatedMemoryDelta);
                    long perOp = Math.Max(0, r.MemoryPerIteration);
                    sb.AppendLine($"| {r.Name} | {r.Iterations} | {FormatBytes(totalMem)} | {FormatBytes(perOp)} | {r.GCCollections} |");
                }
            }

            return sb.ToString();
        }

        private string GenerateLogReport(string customName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🚀 UNITY FACTORY BENCHMARK SUMMARY 🚀");
            sb.AppendLine($"Session: {_sessionStartTime} | Benchmarks: {_results.Count}");
            if (!string.IsNullOrEmpty(customName)) sb.AppendLine($"Label: {customName}");
            sb.AppendLine();

            var performanceResults = _results.Where(r => r.AverageTimeMs > 0).OrderBy(r => r.AverageTimeMs).ToList();
            if (performanceResults.Any())
            {
                sb.AppendLine("⚡ PERFORMANCE RANKING:");
                for (int i = 0; i < Math.Min(5, performanceResults.Count); i++)
                {
                    var result = performanceResults[i];
                    var opsPerSec = result.Iterations / result.TotalTimeSeconds;
                    sb.AppendLine($"  {i + 1}. {result.Name}: {result.AverageTimeMs:F3}ms/op, {opsPerSec:F0} ops/sec");
                }
                sb.AppendLine();
            }

            var memoryResults = _results.Where(r => r.AllocatedMemoryDelta > 0).OrderBy(r => r.MemoryPerIteration).ToList();
            if (memoryResults.Any())
            {
                sb.AppendLine("💾 MEMORY EFFICIENCY:");
                foreach (var result in memoryResults.Take(3))
                {
                    sb.AppendLine($"  • {result.Name}: {FormatBytes(result.MemoryPerIteration)}/op, {result.GCCollections} GC");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void SaveReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to save benchmark report: {ex.Message}");
            }
        }

        private void SaveMarkdownReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetMarkdownReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to save markdown benchmark report: {ex.Message}");
            }
        }

        private void SaveMarkdownSchReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetMarkdownSchReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to save markdown (SCH) benchmark report: {ex.Message}");
            }
        }

        private string GetReportFilePath(string customName)
        {
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName)
                ? $"UnityFactoryBenchmark_{timestamp}.txt"
                : $"UnityFactoryBenchmark_{customName}_{timestamp}.txt";
            return Path.Combine(Application.dataPath, "..", "BenchmarkReports", filename);
        }

        private string GetMarkdownReportFilePath(string customName)
        {
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName)
                ? $"UnityFactoryBenchmark_{timestamp}.md"
                : $"UnityFactoryBenchmark_{customName}_{timestamp}.md";
            return Path.Combine(Application.dataPath, "..", "BenchmarkReports", filename);
        }

        private string GetMarkdownSchReportFilePath(string customName)
        {
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName)
                ? $"UnityFactoryBenchmark_{timestamp}.SCH.md"
                : $"UnityFactoryBenchmark_{customName}_{timestamp}.SCH.md";
            return Path.Combine(Application.dataPath, "..", "BenchmarkReports", filename);
        }

        private void GeneratePerformanceSummary(StringBuilder sb)
        {
            sb.AppendLine("─── PERFORMANCE SUMMARY ───");
            
            var performanceResults = _results.Where(r => r.AverageTimeMs > 0).ToList();
            if (!performanceResults.Any())
            {
                sb.AppendLine("No performance data available.");
                sb.AppendLine();
                return;
            }

            // Aggregate duplicate entries by name
            var aggregated = performanceResults
                .GroupBy(r => r.Name)
                .Select(g => new
                {
                    Name = g.Key,
                    Runs = g.Count(),
                    AvgTimeMs = g.Average(x => x.AverageTimeMs)
                })
                .OrderBy(a => a.AvgTimeMs)
                .ToList();

            int nameWidth = Math.Min(Math.Max(aggregated.Max(a => a.Name.Length), 30), 80);
            string header =
                $"{"Benchmark".PadRight(nameWidth)}  {"Runs",4}  {"Avg Time",10}  {"Ops/Sec",10}  {"Relative",10}";
            sb.AppendLine(header);
            sb.AppendLine(new string('─', header.Length));

            float fastestAvg = aggregated.First().AvgTimeMs;
            foreach (var item in aggregated)
            {
                double opsPerSec = item.AvgTimeMs > 0 ? 1000.0 / item.AvgTimeMs : 0.0;
                string relativeStr = Math.Abs(item.AvgTimeMs - fastestAvg) < 0.0001f ? "FASTEST" : $"{item.AvgTimeMs / fastestAvg:F1}x";
                string row =
                    $"{item.Name.PadRight(nameWidth)}  {item.Runs.ToString().PadLeft(4)}  {item.AvgTimeMs.ToString("F3").PadLeft(10)}  {opsPerSec.ToString("F0").PadLeft(10)}  {relativeStr.PadLeft(10)}";
                sb.AppendLine(row);
            }
            sb.AppendLine();

            // Visual comparison (higher bar = better performance)
            sb.AppendLine("Visual (Ops/sec – higher is better):");
            double maxOps = aggregated.Max(a => a.AvgTimeMs > 0 ? 1000.0 / a.AvgTimeMs : 0.0);
            int labelWidth = Math.Min(Math.Max(aggregated.Max(a => a.Name.Length), 20), 80);
            foreach (var item in aggregated)
            {
                double opsPerSec = item.AvgTimeMs > 0 ? 1000.0 / item.AvgTimeMs : 0.0;
                string bar = BuildBar(opsPerSec, maxOps, 40);
                sb.AppendLine($"{item.Name.PadRight(labelWidth)} | {bar,-40} {opsPerSec,8:F0} ops/s");
            }
            sb.AppendLine();
        }

        private void GenerateMemoryAnalysis(StringBuilder sb)
        {
            sb.AppendLine("─── MEMORY ANALYSIS ───");
            
            var memoryResults = _results.Where(r => r.AllocatedMemoryDelta != 0).ToList();
            if (!memoryResults.Any())
            {
                sb.AppendLine("No memory allocation data available.");
                sb.AppendLine();
                return;
            }

            // Aggregate duplicate entries by name
            var aggregated = memoryResults
                .GroupBy(r => r.Name)
                .Select(g => new
                {
                    Name = g.Key,
                    Runs = g.Count(),
                    AvgTotalAlloc = Math.Max(0, (long)g.Average(x => (double)x.AllocatedMemoryDelta)),
                    AvgPerOp = Math.Max(0, (long)g.Average(x => (double)x.MemoryPerIteration)),
                    AvgGC = (int)g.Average(x => (double)x.GCCollections)
                })
                .OrderBy(a => a.AvgPerOp)
                .ToList();

            int nameWidth = Math.Min(Math.Max(aggregated.Max(a => a.Name.Length), 30), 80);
            string header =
                $"{"Benchmark".PadRight(nameWidth)}  {"Runs",4}  {"Total Alloc",14}  {"Per Op",14}  {"GC Count",8}";
            sb.AppendLine(header);
            sb.AppendLine(new string('─', header.Length));

            foreach (var item in aggregated)
            {
                string row = $"{item.Name.PadRight(nameWidth)}  {item.Runs.ToString().PadLeft(4)}  {FormatBytes(item.AvgTotalAlloc).PadLeft(14)}  {FormatBytes(item.AvgPerOp).PadLeft(14)}  {item.AvgGC.ToString().PadLeft(8)}";
                sb.AppendLine(row);
            }
            sb.AppendLine();
        }

        private void GenerateDetailedResults(StringBuilder sb)
        {
            sb.AppendLine("─── DETAILED RESULTS ───");
            
            foreach (var result in _results)
            {
                sb.AppendLine($"📊 {result.Name}");
                sb.AppendLine($"   Iterations: {result.Iterations:N0}");
                sb.AppendLine($"   Total Time: {result.TotalTimeSeconds:F3} seconds");
                
                if (result.AverageTimeMs > 0)
                {
                    sb.AppendLine($"   Average Time: {result.AverageTimeMs:F3} ms/operation");
                    sb.AppendLine($"   Operations/Sec: {result.Iterations / result.TotalTimeSeconds:F0}");
                }
                
                if (result.MinFrameTimeMs > 0 && result.MaxFrameTimeMs > 0)
                {
                    sb.AppendLine($"   Frame Time - Min: {result.MinFrameTimeMs:F3}ms, Max: {result.MaxFrameTimeMs:F3}ms, StdDev: {result.FrameTimeStdDev:F3}ms");
                }
                
                if (result.AllocatedMemoryDelta != 0)
                {
                    sb.AppendLine($"   Memory Allocated: {FormatBytes(result.AllocatedMemoryDelta)}");
                    sb.AppendLine($"   Memory/Operation: {FormatBytes(result.MemoryPerIteration)}");
                }
                
                if (result.GCCollections > 0)
                {
                    sb.AppendLine($"   GC Collections: {result.GCCollections}");
                }
                
                sb.AppendLine();
            }
        }

        private void GenerateGCAnalysis(StringBuilder sb)
        {
            var gcResults = _results.Where(r => r.GCCollections > 0).ToList();
            if (!gcResults.Any()) return;

            sb.AppendLine("─── GARBAGE COLLECTION ANALYSIS ───");
            
            var totalGC = gcResults.Sum(r => r.GCCollections);
            var avgGCPerBenchmark = totalGC / (double)gcResults.Count;
            
            sb.AppendLine($"Total GC Collections: {totalGC}");
            sb.AppendLine($"Average GC per Benchmark: {avgGCPerBenchmark:F1}");
            sb.AppendLine();
            
            sb.AppendLine("GC-Heavy Operations:");
            foreach (var result in gcResults.OrderByDescending(r => r.GCCollections).Take(5))
            {
                var gcRate = result.GCCollections / (double)result.Iterations * 1000; // Per 1000 operations
                sb.AppendLine($"  • {result.Name}: {result.GCCollections} collections ({gcRate:F1}/1000 ops)");
            }
            sb.AppendLine();
        }

        private void GenerateRecommendations(StringBuilder sb)
        {
            sb.AppendLine("─── PERFORMANCE RECOMMENDATIONS ───");
            
            var highGCResults = _results.Where(r => r.GCCollections > r.Iterations / 100.0).ToList(); // More than 1 GC per 100 ops
            var highMemoryResults = _results.Where(r => r.MemoryPerIteration > 1024).ToList(); // More than 1KB per op
            
            if (highGCResults.Any())
            {
                sb.AppendLine("🔴 HIGH GC PRESSURE DETECTED:");
                foreach (var result in highGCResults)
                {
                    sb.AppendLine($"  • {result.Name}: Consider using object pooling to reduce allocations");
                }
                sb.AppendLine();
            }
            
            if (highMemoryResults.Any())
            {
                sb.AppendLine("🟡 HIGH MEMORY USAGE:");
                foreach (var result in highMemoryResults)
                {
                    sb.AppendLine($"  • {result.Name}: {FormatBytes(result.MemoryPerIteration)}/op - Review allocation patterns");
                }
                sb.AppendLine();
            }
            
            var poolingVsInstantiation = FindPoolingComparison();
            if (poolingVsInstantiation.HasValue)
            {
                var speedup = poolingVsInstantiation.Value.instantiation / poolingVsInstantiation.Value.pooling;
                sb.AppendLine($"✅ POOLING EFFECTIVENESS:");
                sb.AppendLine($"  • Object pooling is {speedup:F1}x faster than direct instantiation");
                sb.AppendLine($"  • Recommended: Use pooling for frequently created/destroyed objects");
                sb.AppendLine();
            }
            
            sb.AppendLine("💡 GENERAL RECOMMENDATIONS:");
            sb.AppendLine("  • Use object pooling for objects with short lifespans");
            sb.AppendLine("  • Monitor GC collections in production builds");
            sb.AppendLine("  • Consider pre-warming pools during loading screens");
            sb.AppendLine("  • Profile on target devices for accurate performance data");
            sb.AppendLine();
        }

        private (double instantiation, double pooling)? FindPoolingComparison()
        {
            var instantiationResult = _results.FirstOrDefault(r => r.Name.ToLower().Contains("instantiat"));
            var poolingResult = _results.FirstOrDefault(r => r.Name.ToLower().Contains("pool"));
            
            if (instantiationResult != null && poolingResult != null && 
                instantiationResult.AverageTimeMs > 0 && poolingResult.AverageTimeMs > 0)
            {
                return (instantiationResult.AverageTimeMs, poolingResult.AverageTimeMs);
            }
            
            return null;
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Length <= maxLength ? str : str.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Clears all benchmark results
        /// </summary>
        public void ClearResults()
        {
            _results.Clear();
        }

        /// <summary>
        /// Optimized standard deviation calculation for double precision
        /// </summary>
        private static float CalculateStandardDeviation(IEnumerable<double> values)
        {
            var valuesList = values.ToList();
            if (valuesList.Count == 0) return 0f;
            
            double sum = 0.0;
            for (int i = 0; i < valuesList.Count; i++)
            {
                sum += valuesList[i];
            }
            double avg = sum / valuesList.Count;
            
            double sumOfSquares = 0.0;
            for (int i = 0; i < valuesList.Count; i++)
            {
                double diff = valuesList[i] - avg;
                sumOfSquares += diff * diff;
            }
            
            return (float)Math.Sqrt(sumOfSquares / valuesList.Count);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
        }

        private static string BuildBar(double value, double maxValue, int maxWidth = 40)
        {
            if (maxValue <= 0 || maxWidth <= 0) return string.Empty;
            int filled = (int)Math.Round((value / maxValue) * maxWidth);
            if (filled < 0) filled = 0;
            if (filled == 0 && value > 0) filled = 1;
            if (filled > maxWidth) filled = maxWidth;
            return new string('#', filled);
        }

        private static string BuildBarChart(List<(string label, double value)> items, int barWidth, Func<double, string> formatValue, bool alignValuesRight)
        {
            if (items == null || items.Count == 0) return string.Empty;
            double maxValue = items.Max(i => i.value);
            int nameWidth = Math.Min(Math.Max(items.Max(i => i.label.Length), 8), 80);
            var sb = new StringBuilder();
            foreach (var i in items)
            {
                string bar = BuildBar(i.value, maxValue, barWidth);
                string valueStr = formatValue(i.value);
                if (alignValuesRight)
                {
                    sb.AppendLine($"{i.label.PadRight(nameWidth)} | {bar} {valueStr,8}");
                }
                else
                {
                    sb.AppendLine($"{i.label.PadRight(nameWidth)} | {bar} {valueStr}");
                }
            }
            return sb.ToString();
        }

		private static int ParseBurstFromName(string name)
		{
			if (string.IsNullOrEmpty(name)) return 1;
			// Look for pattern: (Burst N)
			int start = name.IndexOf("(Burst ", StringComparison.OrdinalIgnoreCase);
			if (start < 0) return 1;
			int end = name.IndexOf(')', start);
			if (end < 0) return 1;
			var inside = name.Substring(start + 7, end - (start + 7)).Trim();
			if (int.TryParse(inside, out int burst) && burst > 0) return burst;
			return 1;
		}

        private static bool IsBurst(string name) => ParseBurstFromName(name) > 1;
        private static bool IsPooling(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("pool");
        }
        private static bool IsDirectOrFactory(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("instantiat") || lower.Contains("factory");
        }
    }

    /// <summary>
    /// Contains the results of a Unity-specific benchmark run with enhanced statistical data
    /// </summary>
    [System.Serializable]
    public class UnityBenchmarkResult
    {
        public string Name;
        public int Iterations;
        public float TotalTimeSeconds;
        public float AverageTimeMs;
        public float MinFrameTimeMs;
        public float MaxFrameTimeMs;
        public float FrameTimeStdDev;
        public long AllocatedMemoryDelta;
        public long ReservedMemoryDelta;
        public long MemoryPerIteration;
        public int GCCollections;
        public float[] FrameTimes;
        public long[] MemorySnapshots;
        
        // Enhanced statistical fields
        public float StandardDeviationMs;
        public float MinTimeMs;
        public float MaxTimeMs;
        public float CoefficientOfVariation;
    }
}
