using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;
using Unity.Profiling;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Sample.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SpriteSequencePerformanceBenchmark : MonoBehaviour
    {
        private enum BenchmarkPhase
        {
            Baseline = 0,
            MonoUpdate = 1,
            BurstManaged = 2,
        }

        private struct PhaseResult
        {
            public BenchmarkPhase Phase;
            public int ControllerCount;
            public double AvgFrameMs;
            public double MinFrameMs;
            public double MaxFrameMs;
            public double AvgFps;
            public double AvgGcBytesPerFrame;
            public long Gc0Delta;
            public long Gc1Delta;
            public long Gc2Delta;
            public double AvgBatches;
            public double AvgSetPass;
            public int MaxBatches;
            public int MaxSetPass;
        }

        private struct BenchmarkCaseResult
        {
            public int TargetControllerCount;
            public int ActualControllerCount;
            public bool RanBaseline;
            public bool RanMono;
            public bool RanBurst;
            public PhaseResult Baseline;
            public PhaseResult Mono;
            public PhaseResult Burst;
        }

        private struct CapacitySearchResult
        {
            public SpriteSequenceController.UpdateDriver Driver;
            public int TargetFps;
            public int SearchMin;
            public int InitialSearchMax;
            public int SearchMax;
            public int Iterations;
            public int BestCount;
            public double BestAvgFps;
            public double BestAvgFrameMs;
            public bool AutoExpanded;
            public int ExpansionSteps;
            public bool FoundFailingUpperBound;
            public bool HitSearchCeiling;
            public int RecommendedMin;
            public int RecommendedMax;
            public int RecommendedIterations;
            public int RecommendedExpandSteps;
            public string RecommendationReason;
            public bool UsedNoiseResistantSampling;
            public int SamplesPerPoint;
            public bool NonMonotonicDetected;
            public int NonMonotonicEvents;
            public bool LocalRescanApplied;
            public int CorrectedSearchMin;
            public int CorrectedSearchMax;
        }

        private struct CapacityProbe
        {
            public int Count;
            public double Fps;
            public bool Pass;
        }

        [Header("Run")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField, Min(30)] private int warmupFrames = 120;
        [SerializeField, Min(60)] private int sampleFrames = 600;
        [SerializeField] private bool compareMonoAndBurst = true;
        [SerializeField] private bool includeBaselinePhase = true;
        [SerializeField] private bool silentMode = true;

        [Header("Scale Sweep")]
        [SerializeField] private bool enableScaleSweep = false;
        [SerializeField] private bool ignoreInactiveSceneControllers = true;
        [SerializeField] private bool includeSceneControllersInSweep = true;
        [SerializeField, Min(1)] private int sweepStartCount = 1;
        [SerializeField, Min(1)] private int sweepStep = 50;
        [SerializeField, Min(1)] private int sweepSteps = 4;
        [SerializeField] private SpriteSequenceController sweepTemplate;
        [SerializeField] private bool cleanupGeneratedAfterRun = true;
        [SerializeField] private bool prewarmGeneratedToMaxTargetBeforeRun = true;
        [SerializeField] private bool useFactoryMonoFastPool = true;
        [SerializeField, Min(1)] private int poolWarmupBatchSize = 64;
        [SerializeField] private Vector3 generatedGridOrigin = Vector3.zero;
        [SerializeField] private Vector2 generatedGridSpacing = new(1.25f, 1.25f);
        [SerializeField, Min(1)] private int generatedGridColumns = 25;

        [Header("Capacity Search")]
        [SerializeField] private bool enableCapacitySearch = false;
        [SerializeField, Min(1f)] private float targetFps = 60f;
        [SerializeField, Min(1)] private int capacitySearchMinCount = 1;
        [SerializeField, Min(1)] private int capacitySearchMaxCount = 1000;
        [SerializeField, Min(1)] private int capacitySearchMaxIterations = 12;
        [SerializeField] private bool capacitySearchTestMono = true;
        [SerializeField] private bool capacitySearchTestBurst = true;
        [SerializeField] private bool autoExpandCapacitySearchUpperBound = true;
        [SerializeField, Min(1)] private int capacitySearchMaxAutoExpandSteps = 4;
        [SerializeField, Min(2)] private int capacitySearchExpansionFactor = 2;
        [SerializeField, Min(1)] private int capacitySearchSamplesPerPoint = 3;
        [SerializeField] private bool capacitySearchUseMedian = true;
        [SerializeField, Min(0f)] private float nonMonotonicToleranceFps = 1.0f;
        [SerializeField] private bool enableNonMonotonicLocalRescan = true;
        [SerializeField, Min(1)] private int nonMonotonicRescanStep = 250;
        [SerializeField, Min(2)] private int nonMonotonicRescanPoints = 12;

        [Header("Logger")]
        [SerializeField] private string logFileName = "SpriteSequenceBenchmark.log";
        [SerializeField] private string logCategory = "SpriteSequence.Benchmark";

        private FileLogger _fileLogger;
        private ProfilerRecorder _gcAllocRecorder;
        private bool _running;
        private readonly List<SpriteSequenceController> _generatedControllers = new(512);
        private readonly List<SpriteSequenceController> _activeGeneratedFromPool = new(512);
        private MonoFastPool<SpriteSequenceController> _generatedControllerPool;
        private Transform _generatedRoot;

        private void Start()
        {
            if (runOnStart)
            {
                StartBenchmark();
            }
        }

        [ContextMenu("Run Benchmark")]
        public void StartBenchmark()
        {
            if (_running)
            {
                return;
            }

            StartCoroutine(RunBenchmarkCoroutine());
        }

        private IEnumerator RunBenchmarkCoroutine()
        {
            _running = true;
            AttachFileLogger();

            SpriteSequenceController[] sceneControllers = FindControllers(ignoreInactiveSceneControllers);
            if (prewarmGeneratedToMaxTargetBeforeRun)
            {
                yield return PrewarmGeneratedTargetsCoroutine(sceneControllers);
            }

            var caseResults = new List<BenchmarkCaseResult>(8);
            var capacityResults = new List<CapacitySearchResult>(2);

            _gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc");

            try
            {
                if (enableScaleSweep)
                {
                    for (int i = 0; i < sweepSteps; i++)
                    {
                        int targetCount = sweepStartCount + i * sweepStep;
                        SpriteSequenceController[] controllersForCase = BuildControllersForTarget(sceneControllers, targetCount);
                        RefreshBurstManagersInScene();

                        BenchmarkCaseResult caseResult = default;
                        yield return RunSingleCaseCoroutine(targetCount, controllersForCase, r => caseResult = r);
                        caseResults.Add(caseResult);
                    }
                }
                else
                {
                    RefreshBurstManagersInScene();
                    BenchmarkCaseResult caseResult = default;
                    yield return RunSingleCaseCoroutine(sceneControllers.Length, sceneControllers, r => caseResult = r);
                    caseResults.Add(caseResult);
                }

                if (enableCapacitySearch)
                {
                    if (capacitySearchTestMono)
                    {
                        CapacitySearchResult monoResult = default;
                        yield return RunCapacitySearchCoroutine(sceneControllers, SpriteSequenceController.UpdateDriver.MonoUpdate, r => monoResult = r);
                        capacityResults.Add(monoResult);
                    }

                    if (capacitySearchTestBurst)
                    {
                        CapacitySearchResult burstResult = default;
                        yield return RunCapacitySearchCoroutine(sceneControllers, SpriteSequenceController.UpdateDriver.BurstManaged, r => burstResult = r);
                        capacityResults.Add(burstResult);
                    }
                }
            }
            finally
            {
                SetGeneratedActiveCount(0);
                if (cleanupGeneratedAfterRun)
                {
                    CleanupGeneratedControllers();
                }
            }

            string report = BuildSuiteReport(caseResults, capacityResults);
            CLogger.LogInfo(report, logCategory);
            if (!silentMode)
            {
                Debug.Log(report);
            }

            if (_gcAllocRecorder.Valid)
            {
                _gcAllocRecorder.Dispose();
            }

            _running = false;
        }

        private void OnDisable()
        {
            if (_gcAllocRecorder.Valid)
            {
                _gcAllocRecorder.Dispose();
            }

            if (cleanupGeneratedAfterRun)
            {
                CleanupGeneratedControllers();
            }

            DetachFileLogger();
        }

        private void AttachFileLogger()
        {
            if (_fileLogger != null)
            {
                return;
            }

            string fileName = string.IsNullOrWhiteSpace(logFileName) ? "SpriteSequenceBenchmark.log" : logFileName;
            string logPath = Path.Combine(Application.persistentDataPath, "Logs", fileName);
            var options = new FileLoggerOptions
            {
                MaintenanceMode = FileMaintenanceMode.Rotate,
                MaxFileBytes = 20L * 1024L * 1024L,
                MaxArchiveFiles = 8,
                FlushBatchSize = 8,
                FlushIntervalMs = 200,
            };

            _fileLogger = new FileLogger(logPath, options);
            CLogger.Instance.AddLogger(_fileLogger);
            CLogger.LogInfo($"Sprite benchmark logger attached: {logPath}", logCategory);
        }

        private void DetachFileLogger()
        {
            if (_fileLogger == null)
            {
                return;
            }

            CLogger.Instance.RemoveLogger(_fileLogger);
            _fileLogger.Dispose();
            _fileLogger = null;
        }

        private static SpriteSequenceController[] FindControllers(bool ignoreInactive)
        {
#if UNITY_2023_1_OR_NEWER
        SpriteSequenceController[] controllers = FindObjectsByType<SpriteSequenceController>(
            ignoreInactive ? FindObjectsInactive.Exclude : FindObjectsInactive.Include,
            FindObjectsSortMode.None);
#else
            SpriteSequenceController[] controllers = FindObjectsOfType<SpriteSequenceController>(!ignoreInactive);
#endif
            return controllers ?? Array.Empty<SpriteSequenceController>();
        }

        private IEnumerator RunWarmup()
        {
            for (int i = 0; i < warmupFrames; i++)
            {
                yield return null;
            }
        }

        private IEnumerator RunSingleCaseCoroutine(int targetCount, SpriteSequenceController[] controllers, Action<BenchmarkCaseResult> onComplete)
        {
            CaptureOriginalState(controllers, out var originalDrivers, out var originalPlaying, out var originalEnabled);

            PhaseResult baseline = default;
            PhaseResult mono = default;
            PhaseResult burst = default;
            bool ranBaseline = false;
            bool ranMono = false;
            bool ranBurst = false;

            try
            {
                if (includeBaselinePhase)
                {
                    ApplyPhaseSetup(controllers, BenchmarkPhase.Baseline);
                    yield return RunWarmup();
                    yield return CapturePhaseCoroutine(BenchmarkPhase.Baseline, controllers, r => baseline = r);
                    ranBaseline = true;
                }

                ApplyPhaseSetup(controllers, BenchmarkPhase.MonoUpdate);
                yield return RunWarmup();
                yield return CapturePhaseCoroutine(BenchmarkPhase.MonoUpdate, controllers, r => mono = r);
                ranMono = true;

                if (compareMonoAndBurst)
                {
                    RefreshBurstManagersInScene();
                    ApplyPhaseSetup(controllers, BenchmarkPhase.BurstManaged);
                    yield return RunWarmup();
                    yield return CapturePhaseCoroutine(BenchmarkPhase.BurstManaged, controllers, r => burst = r);
                    ranBurst = true;
                }
            }
            finally
            {
                RestoreOriginalControllerState(controllers, originalDrivers, originalPlaying, originalEnabled);
            }

            onComplete?.Invoke(new BenchmarkCaseResult
            {
                TargetControllerCount = targetCount,
                ActualControllerCount = CountNonNullControllers(controllers),
                RanBaseline = ranBaseline,
                RanMono = ranMono,
                RanBurst = ranBurst,
                Baseline = baseline,
                Mono = mono,
                Burst = burst,
            });
        }

        private IEnumerator RunCapacitySearchCoroutine(
            SpriteSequenceController[] sceneControllers,
            SpriteSequenceController.UpdateDriver driver,
            Action<CapacitySearchResult> onComplete)
        {
            int low = Mathf.Max(1, capacitySearchMinCount);
            int high = Mathf.Max(low, capacitySearchMaxCount);
            int initialHigh = high;
            int maxIterations = Mathf.Max(1, capacitySearchMaxIterations);
            int expansionFactor = Mathf.Max(2, capacitySearchExpansionFactor);
            int maxExpandSteps = Mathf.Max(0, capacitySearchMaxAutoExpandSteps);
            int target = Mathf.Max(1, Mathf.RoundToInt(targetFps));

            int bestCount = 0;
            PhaseResult bestPhase = default;
            int iterations = 0;
            int expansionSteps = 0;
            bool autoExpanded = false;
            bool foundFailingUpperBound = false;
            bool hitSearchCeiling = false;
            bool nonMonotonicDetected = false;
            int nonMonotonicEvents = 0;

            int samplesPerPoint = Mathf.Max(1, capacitySearchSamplesPerPoint);
            bool useMedian = capacitySearchUseMedian;
            float tolerance = Mathf.Max(0f, nonMonotonicToleranceFps);
            var probes = new List<CapacityProbe>(64);

            int observedMinCount = int.MaxValue;
            int observedMaxCount = 0;

            bool localRescanApplied = false;
            int correctedMin = 0;
            int correctedMax = 0;

            PhaseResult upperProbe = default;
            yield return MeasureDriverAtCount(sceneControllers, high, driver, targetFps, samplesPerPoint, useMedian, r => upperProbe = r);
            RegisterProbe(probes, high, upperProbe.AvgFps, targetFps, tolerance, ref nonMonotonicDetected, ref nonMonotonicEvents, ref observedMinCount, ref observedMaxCount);
            if (upperProbe.AvgFps >= targetFps)
            {
                bestCount = high;
                bestPhase = upperProbe;

                if (autoExpandCapacitySearchUpperBound)
                {
                    while (expansionSteps < maxExpandSteps)
                    {
                        int expandedHigh = high * expansionFactor;
                        if (expandedHigh <= high)
                        {
                            hitSearchCeiling = true;
                            break;
                        }

                        autoExpanded = true;
                        expansionSteps++;
                        high = expandedHigh;

                        PhaseResult expandedProbe = default;
                        yield return MeasureDriverAtCount(sceneControllers, high, driver, targetFps, samplesPerPoint, useMedian, r => expandedProbe = r);
                        RegisterProbe(probes, high, expandedProbe.AvgFps, targetFps, tolerance, ref nonMonotonicDetected, ref nonMonotonicEvents, ref observedMinCount, ref observedMaxCount);
                        if (expandedProbe.AvgFps >= targetFps)
                        {
                            bestCount = high;
                            bestPhase = expandedProbe;
                            continue;
                        }

                        foundFailingUpperBound = true;
                        break;
                    }

                    if (!foundFailingUpperBound && expansionSteps >= maxExpandSteps)
                    {
                        hitSearchCeiling = true;
                    }
                }
                else
                {
                    hitSearchCeiling = true;
                }
            }
            else
            {
                foundFailingUpperBound = true;
            }

            while (low <= high && iterations < maxIterations)
            {
                int mid = low + ((high - low) >> 1);
                PhaseResult measured = default;
                yield return MeasureDriverAtCount(sceneControllers, mid, driver, targetFps, samplesPerPoint, useMedian, r => measured = r);
                RegisterProbe(probes, mid, measured.AvgFps, targetFps, tolerance, ref nonMonotonicDetected, ref nonMonotonicEvents, ref observedMinCount, ref observedMaxCount);

                iterations++;
                if (measured.AvgFps >= targetFps)
                {
                    bestCount = mid;
                    bestPhase = measured;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (nonMonotonicDetected && enableNonMonotonicLocalRescan)
            {
                int step = Mathf.Max(1, nonMonotonicRescanStep);
                int points = Mathf.Max(2, nonMonotonicRescanPoints);
                int half = points / 2;
                int center = Mathf.Max(1, bestCount > 0 ? bestCount : Mathf.Max(1, low - 1));

                int scanMin = Mathf.Max(Mathf.Max(1, capacitySearchMinCount), center - half * step);
                int scanMax = center + half * step;
                if (observedMaxCount > 0)
                {
                    scanMax = Mathf.Max(scanMax, observedMaxCount);
                }

                scanMax = Mathf.Max(scanMin, scanMax);

                int localBestCount = bestCount;
                PhaseResult localBestPhase = bestPhase;
                bool scanned = false;

                for (int c = scanMin; c <= scanMax; c += step)
                {
                    scanned = true;
                    PhaseResult localMeasured = default;
                    yield return MeasureDriverAtCount(sceneControllers, c, driver, targetFps, samplesPerPoint, useMedian, r => localMeasured = r);
                    RegisterProbe(probes, c, localMeasured.AvgFps, targetFps, tolerance, ref nonMonotonicDetected, ref nonMonotonicEvents, ref observedMinCount, ref observedMaxCount);

                    if (localMeasured.AvgFps >= targetFps && c >= localBestCount)
                    {
                        localBestCount = c;
                        localBestPhase = localMeasured;
                    }
                }

                if (scanned)
                {
                    localRescanApplied = true;
                    correctedMin = scanMin;
                    correctedMax = scanMax;
                    if (localBestCount >= bestCount)
                    {
                        bestCount = localBestCount;
                        bestPhase = localBestPhase;
                    }
                }
            }

            CapacitySearchResult result = new CapacitySearchResult
            {
                Driver = driver,
                TargetFps = target,
                SearchMin = Mathf.Max(1, capacitySearchMinCount),
                InitialSearchMax = initialHigh,
                SearchMax = Mathf.Max(Mathf.Max(1, capacitySearchMinCount), Mathf.Max(high, bestCount)),
                Iterations = iterations,
                BestCount = bestCount,
                BestAvgFps = bestCount > 0 ? bestPhase.AvgFps : 0d,
                BestAvgFrameMs = bestCount > 0 ? bestPhase.AvgFrameMs : 0d,
                AutoExpanded = autoExpanded,
                ExpansionSteps = expansionSteps,
                FoundFailingUpperBound = foundFailingUpperBound,
                HitSearchCeiling = hitSearchCeiling,
                UsedNoiseResistantSampling = samplesPerPoint > 1,
                SamplesPerPoint = samplesPerPoint,
                NonMonotonicDetected = nonMonotonicDetected,
                NonMonotonicEvents = nonMonotonicEvents,
                LocalRescanApplied = localRescanApplied,
                CorrectedSearchMin = correctedMin,
                CorrectedSearchMax = correctedMax,
            };

            CapacitySearchResult recommendation = BuildRecommendation(
                driver,
                target,
                result.SearchMin,
                result.InitialSearchMax,
                result.SearchMax,
                result.BestCount,
                result.FoundFailingUpperBound,
                result.HitSearchCeiling,
                result.ExpansionSteps);
            ApplyRecommendation(ref result, recommendation);

            onComplete?.Invoke(result);
        }

        private IEnumerator RunDriverOnlyCaseCoroutine(
            SpriteSequenceController[] controllers,
            SpriteSequenceController.UpdateDriver driver,
            Action<PhaseResult> onComplete)
        {
            CaptureOriginalState(controllers, out var originalDrivers, out var originalPlaying, out var originalEnabled);

            BenchmarkPhase phase = driver == SpriteSequenceController.UpdateDriver.BurstManaged
                ? BenchmarkPhase.BurstManaged
                : BenchmarkPhase.MonoUpdate;

            PhaseResult measured = default;
            try
            {
                if (driver == SpriteSequenceController.UpdateDriver.BurstManaged)
                {
                    RefreshBurstManagersInScene();
                }

                ApplyPhaseSetup(controllers, phase);
                yield return RunWarmup();
                yield return CapturePhaseCoroutine(phase, controllers, r => measured = r);
            }
            finally
            {
                RestoreOriginalControllerState(controllers, originalDrivers, originalPlaying, originalEnabled);
            }

            onComplete?.Invoke(measured);
        }

        private IEnumerator MeasureDriverAtCount(
            SpriteSequenceController[] sceneControllers,
            int count,
            SpriteSequenceController.UpdateDriver driver,
            float targetFpsValue,
            int samplesPerPoint,
            bool useMedian,
            Action<PhaseResult> onComplete)
        {
            int runs = Mathf.Max(1, samplesPerPoint);
            var samples = new PhaseResult[runs];

            for (int i = 0; i < runs; i++)
            {
                PhaseResult measured = default;
                yield return RunDriverOnlyCaseCoroutine(BuildControllersForTarget(sceneControllers, count), driver, r => measured = r);
                samples[i] = measured;
            }

            if (runs == 1)
            {
                onComplete?.Invoke(samples[0]);
                yield break;
            }

            if (useMedian)
            {
                Array.Sort(samples, (a, b) => a.AvgFps.CompareTo(b.AvgFps));
                onComplete?.Invoke(samples[runs / 2]);
                yield break;
            }

            // Fallback aggregation when median is disabled.
            double sumFrameMs = 0d;
            double minFrameMs = double.MaxValue;
            double maxFrameMs = 0d;
            double sumFps = 0d;
            double sumGc = 0d;
            long gc0 = 0;
            long gc1 = 0;
            long gc2 = 0;
            double sumBatches = 0d;
            double sumSetPass = 0d;
            int maxBatches = 0;
            int maxSetPass = 0;
            int controllerCount = 0;

            for (int i = 0; i < runs; i++)
            {
                PhaseResult s = samples[i];
                sumFrameMs += s.AvgFrameMs;
                sumFps += s.AvgFps;
                sumGc += s.AvgGcBytesPerFrame;
                gc0 += s.Gc0Delta;
                gc1 += s.Gc1Delta;
                gc2 += s.Gc2Delta;
                sumBatches += s.AvgBatches;
                sumSetPass += s.AvgSetPass;
                controllerCount = Mathf.Max(controllerCount, s.ControllerCount);
                if (s.MinFrameMs < minFrameMs)
                {
                    minFrameMs = s.MinFrameMs;
                }

                if (s.MaxFrameMs > maxFrameMs)
                {
                    maxFrameMs = s.MaxFrameMs;
                }

                if (s.MaxBatches > maxBatches)
                {
                    maxBatches = s.MaxBatches;
                }

                if (s.MaxSetPass > maxSetPass)
                {
                    maxSetPass = s.MaxSetPass;
                }
            }

            onComplete?.Invoke(new PhaseResult
            {
                Phase = driver == SpriteSequenceController.UpdateDriver.BurstManaged ? BenchmarkPhase.BurstManaged : BenchmarkPhase.MonoUpdate,
                ControllerCount = controllerCount,
                AvgFrameMs = sumFrameMs / runs,
                MinFrameMs = minFrameMs,
                MaxFrameMs = maxFrameMs,
                AvgFps = sumFps / runs,
                AvgGcBytesPerFrame = sumGc / runs,
                Gc0Delta = gc0 / runs,
                Gc1Delta = gc1 / runs,
                Gc2Delta = gc2 / runs,
                AvgBatches = sumBatches / runs,
                AvgSetPass = sumSetPass / runs,
                MaxBatches = maxBatches,
                MaxSetPass = maxSetPass,
            });
        }

        private static void RegisterProbe(
            List<CapacityProbe> probes,
            int count,
            double fps,
            float targetFpsValue,
            float tolerance,
            ref bool nonMonotonicDetected,
            ref int nonMonotonicEvents,
            ref int observedMinCount,
            ref int observedMaxCount)
        {
            if (count < observedMinCount)
            {
                observedMinCount = count;
            }

            if (count > observedMaxCount)
            {
                observedMaxCount = count;
            }

            for (int i = 0; i < probes.Count; i++)
            {
                CapacityProbe p = probes[i];
                if (p.Count < count && fps > p.Fps + tolerance)
                {
                    nonMonotonicDetected = true;
                    nonMonotonicEvents++;
                }

                if (p.Count > count && p.Fps > fps + tolerance)
                {
                    nonMonotonicDetected = true;
                    nonMonotonicEvents++;
                }
            }

            probes.Add(new CapacityProbe
            {
                Count = count,
                Fps = fps,
                Pass = fps >= targetFpsValue,
            });
        }

        private IEnumerator CapturePhaseCoroutine(BenchmarkPhase phase, SpriteSequenceController[] controllers, Action<PhaseResult> onComplete)
        {
            double sumFrameMs = 0d;
            double minFrameMs = double.MaxValue;
            double maxFrameMs = 0d;
            long sumGcBytes = 0;

            long gc0Before = GC.CollectionCount(0);
            long gc1Before = GC.CollectionCount(1);
            long gc2Before = GC.CollectionCount(2);

#if UNITY_EDITOR
            long sumBatches = 0;
            long sumSetPass = 0;
            int maxBatches = 0;
            int maxSetPass = 0;
#endif

            for (int i = 0; i < sampleFrames; i++)
            {
                yield return new WaitForEndOfFrame();

                double frameMs = Time.unscaledDeltaTime * 1000.0;
                sumFrameMs += frameMs;
                if (frameMs < minFrameMs)
                {
                    minFrameMs = frameMs;
                }

                if (frameMs > maxFrameMs)
                {
                    maxFrameMs = frameMs;
                }

                if (_gcAllocRecorder.Valid)
                {
                    sumGcBytes += _gcAllocRecorder.LastValue;
                }

#if UNITY_EDITOR
                int batches = UnityEditor.UnityStats.batches;
                int setPass = UnityEditor.UnityStats.setPassCalls;
                sumBatches += batches;
                sumSetPass += setPass;
                if (batches > maxBatches)
                {
                    maxBatches = batches;
                }

                if (setPass > maxSetPass)
                {
                    maxSetPass = setPass;
                }
#endif
            }

            var result = new PhaseResult
            {
                Phase = phase,
                ControllerCount = CountNonNullControllers(controllers),
                AvgFrameMs = sumFrameMs / Mathf.Max(1, sampleFrames),
                MinFrameMs = minFrameMs,
                MaxFrameMs = maxFrameMs,
                AvgGcBytesPerFrame = sumGcBytes / (double)Mathf.Max(1, sampleFrames),
                Gc0Delta = GC.CollectionCount(0) - gc0Before,
                Gc1Delta = GC.CollectionCount(1) - gc1Before,
                Gc2Delta = GC.CollectionCount(2) - gc2Before,
            };

            result.AvgFps = result.AvgFrameMs > 0.0001d ? 1000.0 / result.AvgFrameMs : 0d;

#if UNITY_EDITOR
            result.AvgBatches = sumBatches / (double)Mathf.Max(1, sampleFrames);
            result.AvgSetPass = sumSetPass / (double)Mathf.Max(1, sampleFrames);
            result.MaxBatches = maxBatches;
            result.MaxSetPass = maxSetPass;
#endif

            onComplete?.Invoke(result);
        }

        private void ApplyPhaseSetup(SpriteSequenceController[] controllers, BenchmarkPhase phase)
        {
            if (phase == BenchmarkPhase.BurstManaged)
            {
                RefreshBurstManagersInScene();
            }

            for (int i = 0; i < controllers.Length; i++)
            {
                SpriteSequenceController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                if (!controller.enabled)
                {
                    controller.enabled = true;
                }

                switch (phase)
                {
                    case BenchmarkPhase.Baseline:
                        controller.Stop();
                        break;
                    case BenchmarkPhase.MonoUpdate:
                        controller.SetUpdateDriver(SpriteSequenceController.UpdateDriver.MonoUpdate);
                        controller.Play();
                        break;
                    case BenchmarkPhase.BurstManaged:
                        controller.SetUpdateDriver(SpriteSequenceController.UpdateDriver.BurstManaged);
                        controller.Play();
                        break;
                }
            }
        }

        private static void CaptureOriginalState(
            SpriteSequenceController[] controllers,
            out SpriteSequenceController.UpdateDriver[] originalDrivers,
            out bool[] originalPlaying,
            out bool[] originalEnabled)
        {
            originalDrivers = new SpriteSequenceController.UpdateDriver[controllers.Length];
            originalPlaying = new bool[controllers.Length];
            originalEnabled = new bool[controllers.Length];

            for (int i = 0; i < controllers.Length; i++)
            {
                SpriteSequenceController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                originalDrivers[i] = controller.CurrentUpdateDriver;
                originalPlaying[i] = controller.IsPlaying;
                originalEnabled[i] = controller.enabled;
            }
        }

        private static void RestoreOriginalControllerState(
            SpriteSequenceController[] controllers,
            SpriteSequenceController.UpdateDriver[] originalDrivers,
            bool[] originalPlaying,
            bool[] originalEnabled)
        {
            int len = Mathf.Min(controllers.Length, Mathf.Min(originalDrivers.Length, Mathf.Min(originalPlaying.Length, originalEnabled.Length)));
            for (int i = 0; i < len; i++)
            {
                SpriteSequenceController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                if (!originalEnabled[i])
                {
                    if (controller.enabled)
                    {
                        controller.Stop();
                    }

                    controller.SetUpdateDriver(originalDrivers[i]);
                    controller.enabled = false;
                    continue;
                }

                if (!controller.enabled)
                {
                    controller.enabled = true;
                }

                controller.SetUpdateDriver(originalDrivers[i]);
                if (originalPlaying[i])
                {
                    controller.Play();
                }
                else
                {
                    controller.Stop();
                }
            }
        }

        private SpriteSequenceController[] BuildControllersForTarget(SpriteSequenceController[] sceneControllers, int targetCount)
        {
            int safeTarget = Mathf.Max(1, targetCount);
            var result = new List<SpriteSequenceController>(safeTarget);

            if (includeSceneControllersInSweep && sceneControllers != null)
            {
                for (int i = 0; i < sceneControllers.Length && result.Count < safeTarget; i++)
                {
                    if (sceneControllers[i] != null)
                    {
                        result.Add(sceneControllers[i]);
                    }
                }
            }

            int needGenerated = safeTarget - result.Count;
            if (needGenerated > 0)
            {
                if (sweepTemplate == null)
                {
                    sweepTemplate = ResolveTemplate(sceneControllers);
                }

                if (sweepTemplate != null)
                {
                    EnsureGeneratedPoolSize(needGenerated, sweepTemplate);
                    SetGeneratedActiveCount(needGenerated);
                    RepositionGeneratedControllers(needGenerated);

                    IList<SpriteSequenceController> source = useFactoryMonoFastPool && _generatedControllerPool != null
                        ? _activeGeneratedFromPool
                        : _generatedControllers;

                    for (int i = 0; i < needGenerated && i < source.Count; i++)
                    {
                        SpriteSequenceController controller = source[i];
                        if (controller != null)
                        {
                            result.Add(controller);
                        }
                    }
                }
                else
                {
                    CLogger.LogWarning("Scale sweep requires a template controller but none is assigned/found. Running with available scene controllers only.", logCategory);
                }
            }
            else
            {
                SetGeneratedActiveCount(0);
            }

            return result.ToArray();
        }

        private IEnumerator PrewarmGeneratedTargetsCoroutine(SpriteSequenceController[] sceneControllers)
        {
            if (!enableScaleSweep && !enableCapacitySearch)
            {
                yield break;
            }

            int maxTarget = EstimateMaxTargetCount(sceneControllers);
            if (maxTarget <= 0)
            {
                yield break;
            }

            if (sweepTemplate == null)
            {
                sweepTemplate = ResolveTemplate(sceneControllers);
            }

            if (sweepTemplate == null)
            {
                CLogger.LogWarning("Benchmark prewarm skipped: no sweep template was found.", logCategory);
                yield break;
            }

            int sceneContribution = includeSceneControllersInSweep ? CountNonNullControllers(sceneControllers) : 0;
            int requiredGenerated = Mathf.Max(0, maxTarget - sceneContribution);
            if (requiredGenerated <= 0)
            {
                yield break;
            }

            if (useFactoryMonoFastPool)
            {
                EnsureGeneratedRoot();
                _generatedControllerPool ??= new MonoFastPool<SpriteSequenceController>(sweepTemplate, 0, _generatedRoot, true);

                int currentCapacity = _generatedControllerPool.CountInactive + _activeGeneratedFromPool.Count;
                int toWarm = requiredGenerated - currentCapacity;
                if (toWarm > 0)
                {
                    int batchSize = Mathf.Max(1, poolWarmupBatchSize);
                    yield return _generatedControllerPool.WarmupCoroutine(toWarm, batchSize);
                }
            }
            else
            {
                EnsureGeneratedPoolSize(requiredGenerated, sweepTemplate);
            }
        }

        private int EstimateMaxTargetCount(SpriteSequenceController[] sceneControllers)
        {
            int maxTarget = CountNonNullControllers(sceneControllers);

            if (enableScaleSweep)
            {
                int sweepMax = sweepStartCount + Mathf.Max(0, sweepSteps - 1) * sweepStep;
                maxTarget = Mathf.Max(maxTarget, sweepMax);
            }

            if (enableCapacitySearch)
            {
                int capMax = Mathf.Max(1, capacitySearchMaxCount);
                if (autoExpandCapacitySearchUpperBound)
                {
                    int factor = Mathf.Max(2, capacitySearchExpansionFactor);
                    int steps = Mathf.Max(0, capacitySearchMaxAutoExpandSteps);
                    for (int i = 0; i < steps; i++)
                    {
                        int expanded = capMax * factor;
                        if (expanded <= capMax)
                        {
                            break;
                        }

                        capMax = expanded;
                    }
                }

                maxTarget = Mathf.Max(maxTarget, capMax);
            }

            return maxTarget;
        }

        private static void RefreshBurstManagersInScene()
        {
#if UNITY_2023_1_OR_NEWER
        SpriteSequenceBurstManager[] managers = FindObjectsByType<SpriteSequenceBurstManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            SpriteSequenceBurstManager[] managers = FindObjectsOfType<SpriteSequenceBurstManager>(true);
#endif
            if (managers == null)
            {
                return;
            }

            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] != null)
                {
                    managers[i].RefreshControllers();
                }
            }
        }

        private SpriteSequenceController ResolveTemplate(SpriteSequenceController[] sceneControllers)
        {
            if (sceneControllers == null)
            {
                return null;
            }

            for (int i = 0; i < sceneControllers.Length; i++)
            {
                if (sceneControllers[i] != null)
                {
                    return sceneControllers[i];
                }
            }

            return null;
        }

        private void EnsureGeneratedPoolSize(int requiredCount, SpriteSequenceController template)
        {
            if (template == null || requiredCount <= 0)
            {
                return;
            }

            if (useFactoryMonoFastPool)
            {
                EnsureGeneratedRoot();
                _generatedControllerPool ??= new MonoFastPool<SpriteSequenceController>(template, 0, _generatedRoot, true);

                int currentCapacity = _generatedControllerPool.CountInactive + _activeGeneratedFromPool.Count;
                int toExpand = requiredCount - currentCapacity;
                if (toExpand > 0)
                {
                    _generatedControllerPool.Prewarm(toExpand);
                }

                return;
            }

            EnsureGeneratedRoot();

            while (_generatedControllers.Count < requiredCount)
            {
                GameObject clone = Instantiate(template.gameObject, _generatedRoot);
                clone.name = template.gameObject.name + "_Bench_" + _generatedControllers.Count;
                SpriteSequenceController controller = clone.GetComponent<SpriteSequenceController>();
                if (controller == null)
                {
                    Destroy(clone);
                    break;
                }

                controller.enabled = false;
                _generatedControllers.Add(controller);
                clone.SetActive(false);
            }
        }

        private void EnsureGeneratedRoot()
        {
            if (_generatedRoot != null)
            {
                return;
            }

            GameObject root = new("SpriteSequenceBenchmark.GeneratedRoot");
            _generatedRoot = root.transform;
        }

        private void SetGeneratedActiveCount(int activeCount)
        {
            int safeActive = Mathf.Max(0, activeCount);

            if (useFactoryMonoFastPool && _generatedControllerPool != null)
            {
                while (_activeGeneratedFromPool.Count < safeActive)
                {
                    SpriteSequenceController spawned = _generatedControllerPool.Spawn();
                    if (spawned == null)
                    {
                        break;
                    }

                    spawned.enabled = false;
                    _activeGeneratedFromPool.Add(spawned);
                }

                while (_activeGeneratedFromPool.Count > safeActive)
                {
                    int last = _activeGeneratedFromPool.Count - 1;
                    SpriteSequenceController item = _activeGeneratedFromPool[last];
                    _activeGeneratedFromPool.RemoveAt(last);
                    if (item != null)
                    {
                        _generatedControllerPool.Despawn(item);
                    }
                }

                return;
            }

            for (int i = 0; i < _generatedControllers.Count; i++)
            {
                SpriteSequenceController controller = _generatedControllers[i];
                if (controller == null)
                {
                    continue;
                }

                bool shouldBeActive = i < safeActive;
                if (controller.gameObject.activeSelf != shouldBeActive)
                {
                    controller.gameObject.SetActive(shouldBeActive);
                }
            }
        }

        private void RepositionGeneratedControllers(int activeCount)
        {
            int cols = Mathf.Max(1, generatedGridColumns);
            float spacingX = Mathf.Max(0.01f, generatedGridSpacing.x);
            float spacingY = Mathf.Max(0.01f, generatedGridSpacing.y);

            IList<SpriteSequenceController> source = useFactoryMonoFastPool && _generatedControllerPool != null
                ? _activeGeneratedFromPool
                : _generatedControllers;

            for (int i = 0; i < activeCount && i < source.Count; i++)
            {
                SpriteSequenceController controller = source[i];
                if (controller == null)
                {
                    continue;
                }

                int row = i / cols;
                int col = i % cols;
                controller.transform.position = generatedGridOrigin + new Vector3(col * spacingX, row * spacingY, 0f);
            }
        }

        private void CleanupGeneratedControllers()
        {
            if (_generatedControllerPool != null)
            {
                for (int i = _activeGeneratedFromPool.Count - 1; i >= 0; i--)
                {
                    SpriteSequenceController item = _activeGeneratedFromPool[i];
                    if (item != null)
                    {
                        _generatedControllerPool.Despawn(item);
                    }
                }

                _activeGeneratedFromPool.Clear();
                _generatedControllerPool.Dispose();
                _generatedControllerPool = null;
            }

            for (int i = 0; i < _generatedControllers.Count; i++)
            {
                SpriteSequenceController controller = _generatedControllers[i];
                if (controller == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(controller.gameObject);
                }
                else
                {
                    DestroyImmediate(controller.gameObject);
                }
            }

            _generatedControllers.Clear();

            if (_generatedRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_generatedRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(_generatedRoot.gameObject);
                }

                _generatedRoot = null;
            }
        }

        private string BuildSuiteReport(List<BenchmarkCaseResult> cases, List<CapacitySearchResult> capacityResults)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("================ Sprite Sequence Benchmark ================");
            sb.Append("Time: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("Warmup Frames: ").AppendLine(warmupFrames.ToString());
            sb.Append("Sample Frames: ").AppendLine(sampleFrames.ToString());
            sb.Append("Silent Mode: ").AppendLine(silentMode ? "On" : "Off");
            sb.Append("Scale Sweep: ").AppendLine(enableScaleSweep ? "On" : "Off");
            sb.Append("Capacity Search: ").AppendLine(enableCapacitySearch ? "On" : "Off");
            if (enableScaleSweep)
            {
                sb.Append("Sweep (start/step/steps): ").Append(sweepStartCount).Append('/').Append(sweepStep).Append('/').AppendLine(sweepSteps.ToString());
                sb.Append("Ignore Inactive Scene Controllers: ").AppendLine(ignoreInactiveSceneControllers ? "On" : "Off");
                sb.Append("Include Scene Controllers: ").AppendLine(includeSceneControllersInSweep ? "On" : "Off");
                sb.Append("Prewarm Generated To Max Target: ").AppendLine(prewarmGeneratedToMaxTargetBeforeRun ? "On" : "Off");
                sb.Append("Use Factory MonoFastPool: ").AppendLine(useFactoryMonoFastPool ? "On" : "Off");
                sb.Append("Pool Warmup Batch Size: ").AppendLine(Mathf.Max(1, poolWarmupBatchSize).ToString());
            }

            if (enableCapacitySearch)
            {
                sb.Append("Capacity Search target FPS: ").AppendLine(targetFps.ToString("F1"));
                sb.Append("Capacity Search range: ").Append(capacitySearchMinCount).Append("..").AppendLine(capacitySearchMaxCount.ToString());
                sb.Append("Capacity Search max iterations: ").AppendLine(capacitySearchMaxIterations.ToString());
                sb.Append("Capacity Search samples per point: ").AppendLine(Mathf.Max(1, capacitySearchSamplesPerPoint).ToString());
                sb.Append("Capacity Search median aggregation: ").AppendLine(capacitySearchUseMedian ? "On" : "Off");
                sb.Append("Non-monotonic tolerance FPS: ").AppendLine(nonMonotonicToleranceFps.ToString("F2"));
                sb.Append("Non-monotonic local rescan: ").AppendLine(enableNonMonotonicLocalRescan ? "On" : "Off");
                sb.Append("Capacity Search auto expand upper bound: ").AppendLine(autoExpandCapacitySearchUpperBound ? "On" : "Off");
                if (autoExpandCapacitySearchUpperBound)
                {
                    sb.Append("Capacity Search expand factor/max steps: ")
                        .Append(capacitySearchExpansionFactor)
                        .Append('/')
                        .AppendLine(capacitySearchMaxAutoExpandSteps.ToString());
                }
            }

            sb.AppendLine("-----------------------------------------------------------");

            for (int i = 0; i < cases.Count; i++)
            {
                BenchmarkCaseResult c = cases[i];
                sb.Append("Case ").Append(i + 1).Append(": Target Controllers = ").Append(c.TargetControllerCount)
                    .Append(", Actual = ").AppendLine(c.ActualControllerCount.ToString());
                sb.AppendLine("-----------------------------------------------------------");

                if (c.RanBaseline)
                {
                    AppendPhase(sb, c.Baseline);
                }

                if (c.RanMono)
                {
                    AppendPhase(sb, c.Mono);
                }

                if (c.RanBurst)
                {
                    AppendPhase(sb, c.Burst);
                }

                sb.AppendLine("================ Delta Analysis ================");
                if (c.RanBaseline && c.RanMono)
                {
                    AppendDelta(sb, "Mono - Baseline", c.Mono, c.Baseline);
                }

                if (c.RanBaseline && c.RanBurst)
                {
                    AppendDelta(sb, "Burst - Baseline", c.Burst, c.Baseline);
                }

                if (c.RanMono && c.RanBurst)
                {
                    AppendDelta(sb, "Burst - Mono", c.Burst, c.Mono);
                }

                sb.AppendLine("===========================================================");
            }

            if (capacityResults.Count > 0)
            {
                sb.AppendLine("================ Capacity Search ================");
                for (int i = 0; i < capacityResults.Count; i++)
                {
                    AppendCapacitySearch(sb, capacityResults[i]);
                }
                sb.AppendLine("=================================================");
            }

            return sb.ToString();
        }

        private static void AppendCapacitySearch(StringBuilder sb, CapacitySearchResult r)
        {
            sb.Append("Driver: ").AppendLine(r.Driver.ToString());
            sb.Append("Target FPS: ").AppendLine(r.TargetFps.ToString());
            sb.Append("Initial Search Range: ").Append(r.SearchMin).Append(".. ").AppendLine(r.InitialSearchMax.ToString());
            sb.Append("Final Search Range: ").Append(r.SearchMin).Append(".. ").AppendLine(r.SearchMax.ToString());
            sb.Append("Iterations: ").AppendLine(r.Iterations.ToString());
            sb.Append("Best Count: ").AppendLine(r.BestCount.ToString());
            if (r.BestCount > 0)
            {
                sb.Append("Best Avg FPS: ").AppendLine(r.BestAvgFps.ToString("F2"));
                sb.Append("Best Avg Frame ms: ").AppendLine(r.BestAvgFrameMs.ToString("F3"));
            }
            else
            {
                sb.AppendLine("Best Avg FPS: below target in whole search range");
            }

            sb.Append("Auto Expanded: ").AppendLine(r.AutoExpanded ? "Yes" : "No");
            sb.Append("Expansion Steps: ").AppendLine(r.ExpansionSteps.ToString());
            sb.Append("Found Failing Upper Bound: ").AppendLine(r.FoundFailingUpperBound ? "Yes" : "No");
            sb.Append("Hit Search Ceiling: ").AppendLine(r.HitSearchCeiling ? "Yes" : "No");
            sb.Append("Noise Resistant Sampling: ").AppendLine(r.UsedNoiseResistantSampling ? "Yes" : "No");
            sb.Append("Samples Per Point: ").AppendLine(r.SamplesPerPoint.ToString());
            sb.Append("Non-Monotonic Detected: ").AppendLine(r.NonMonotonicDetected ? "Yes" : "No");
            if (r.NonMonotonicDetected)
            {
                sb.Append("Non-Monotonic Events: ").AppendLine(r.NonMonotonicEvents.ToString());
            }

            sb.Append("Local Rescan Applied: ").AppendLine(r.LocalRescanApplied ? "Yes" : "No");
            if (r.LocalRescanApplied)
            {
                sb.Append("Local Rescan Range: ").Append(r.CorrectedSearchMin).Append("..").AppendLine(r.CorrectedSearchMax.ToString());
            }

            if (r.HitSearchCeiling)
            {
                sb.AppendLine("Capacity Note: Result may still be truncated by the configured auto-expand ceiling.");
            }
            else if (r.FoundFailingUpperBound)
            {
                sb.AppendLine("Capacity Note: Search found a true upper bound below target FPS within the explored range.");
            }

            sb.Append("Recommended Next Range: ").Append(r.RecommendedMin).Append("..").AppendLine(r.RecommendedMax.ToString());
            sb.Append("Recommended Iterations: ").AppendLine(r.RecommendedIterations.ToString());
            sb.Append("Recommended Expand Steps: ").AppendLine(r.RecommendedExpandSteps.ToString());
            if (!string.IsNullOrEmpty(r.RecommendationReason))
            {
                sb.Append("Recommendation Reason: ").AppendLine(r.RecommendationReason);
            }

            sb.AppendLine("-------------------------------------------------");
        }

        private static CapacitySearchResult BuildRecommendation(
            SpriteSequenceController.UpdateDriver driver,
            int targetFpsValue,
            int searchMin,
            int initialSearchMax,
            int finalSearchMax,
            int bestCount,
            bool foundFailingUpperBound,
            bool hitSearchCeiling,
            int expansionSteps)
        {
            var result = new CapacitySearchResult
            {
                Driver = driver,
                TargetFps = targetFpsValue,
                SearchMin = searchMin,
                InitialSearchMax = initialSearchMax,
                SearchMax = finalSearchMax,
                BestCount = bestCount,
                FoundFailingUpperBound = foundFailingUpperBound,
                HitSearchCeiling = hitSearchCeiling,
                ExpansionSteps = expansionSteps,
            };

            int safeBest = Mathf.Max(1, bestCount);
            int window = Mathf.Max(64, Mathf.CeilToInt(safeBest * 0.25f));

            if (bestCount <= 0)
            {
                result.RecommendedMin = Mathf.Max(1, searchMin / 2);
                result.RecommendedMax = Mathf.Max(searchMin, initialSearchMax / 2);
                result.RecommendedIterations = 8;
                result.RecommendedExpandSteps = 0;
                result.RecommendationReason = "Current minimum range already struggles to hit target FPS. Next run should probe a smaller range.";
                return result;
            }

            if (hitSearchCeiling && !foundFailingUpperBound)
            {
                result.RecommendedMin = safeBest;
                result.RecommendedMax = Mathf.Max(safeBest + 1, finalSearchMax * 2);
                result.RecommendedIterations = 12;
                result.RecommendedExpandSteps = Mathf.Max(2, expansionSteps + 1);
                result.RecommendationReason = "Search likely hit the configured ceiling before finding the true failure point. Expand the upper bound.";
                return result;
            }

            if (foundFailingUpperBound)
            {
                result.RecommendedMin = Mathf.Max(1, safeBest - window);
                result.RecommendedMax = Mathf.Max(result.RecommendedMin + 1, Mathf.Min(finalSearchMax, safeBest + window));
                result.RecommendedIterations = 10;
                result.RecommendedExpandSteps = 0;
                result.RecommendationReason = "Search found a valid upper bound. Next run can narrow around the threshold for a tighter estimate.";
                return result;
            }

            result.RecommendedMin = Mathf.Max(1, safeBest - window);
            result.RecommendedMax = Mathf.Max(result.RecommendedMin + 1, safeBest + window);
            result.RecommendedIterations = 10;
            result.RecommendedExpandSteps = 1;
            result.RecommendationReason = "Search completed without a strong boundary signal. Re-run around the current best result with a modest expansion.";
            return result;
        }

        private static void ApplyRecommendation(ref CapacitySearchResult target, CapacitySearchResult recommendation)
        {
            target.RecommendedMin = recommendation.RecommendedMin;
            target.RecommendedMax = recommendation.RecommendedMax;
            target.RecommendedIterations = recommendation.RecommendedIterations;
            target.RecommendedExpandSteps = recommendation.RecommendedExpandSteps;
            target.RecommendationReason = recommendation.RecommendationReason;
        }

        private static void AppendPhase(StringBuilder sb, PhaseResult r)
        {
            sb.Append("Phase: ").AppendLine(r.Phase.ToString());
            sb.Append("Controllers: ").AppendLine(r.ControllerCount.ToString());
            sb.Append("Frame Time ms (Avg/Min/Max): ")
                .Append(r.AvgFrameMs.ToString("F3")).Append('/')
                .Append(r.MinFrameMs.ToString("F3")).Append('/')
                .AppendLine(r.MaxFrameMs.ToString("F3"));
            sb.Append("FPS (Avg): ").AppendLine(r.AvgFps.ToString("F2"));
            sb.Append("GC.Alloc bytes/frame (avg): ").AppendLine(r.AvgGcBytesPerFrame.ToString("F2"));
            sb.Append("GC Collections Delta (Gen0/Gen1/Gen2): ")
                .Append(r.Gc0Delta).Append('/')
                .Append(r.Gc1Delta).Append('/')
                .AppendLine(r.Gc2Delta.ToString());

#if UNITY_EDITOR
            sb.Append("Batches (Avg/Max): ").Append(r.AvgBatches.ToString("F2")).Append('/').AppendLine(r.MaxBatches.ToString());
            sb.Append("SetPass (Avg/Max): ").Append(r.AvgSetPass.ToString("F2")).Append('/').AppendLine(r.MaxSetPass.ToString());
#else
        sb.AppendLine("Batches/SetPass: UnityEditor.UnityStats unavailable in player build.");
#endif

            sb.AppendLine("-----------------------------------------------------------");
        }

        private static void AppendDelta(StringBuilder sb, string title, PhaseResult a, PhaseResult b)
        {
            sb.Append("Delta ").Append(title).AppendLine(":");
            sb.Append("  Frame ms(avg): ").Append((a.AvgFrameMs - b.AvgFrameMs).ToString("F3")).AppendLine(" (negative is better)");
            sb.Append("  FPS(avg): ").Append((a.AvgFps - b.AvgFps).ToString("F2")).AppendLine(" (positive is better)");
            sb.Append("  GC bytes/frame(avg): ").Append((a.AvgGcBytesPerFrame - b.AvgGcBytesPerFrame).ToString("F2")).AppendLine(" (negative is better)");
#if UNITY_EDITOR
            sb.Append("  Batches(avg): ").Append((a.AvgBatches - b.AvgBatches).ToString("F2")).AppendLine(" (negative is better)");
            sb.Append("  SetPass(avg): ").Append((a.AvgSetPass - b.AvgSetPass).ToString("F2")).AppendLine(" (negative is better)");
#endif
            sb.AppendLine("-----------------------------------------------------------");
        }

        private static int CountNonNullControllers(SpriteSequenceController[] controllers)
        {
            int count = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
