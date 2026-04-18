using System.Collections;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.PerformanceTest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.BehaviorTree.Tests.PlayMode
{
    public class BehaviorTreePlayModeBenchmarkTests
    {
        [UnityTest]
        public IEnumerator BenchmarkRunner_CompletesAndProducesMetrics()
        {
            var gameObject = new GameObject("BT Benchmark Runner Test");
            var runner = gameObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = false;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.Single;
            runner.SetConfig(new BehaviorTreeBenchmarkConfig
            {
                BenchmarkName = "PlayMode Smoke Benchmark",
                Complexity = BehaviorTreeBenchmarkComplexity.Medium,
                SchedulingProfile = BehaviorTreeBenchmarkSchedulingProfile.LodCrowd,
                AgentCount = 64,
                LeafNodesPerTree = 4,
                BlackboardReadsPerLeafPerTick = 2,
                WritesPerLeafPerTick = 2,
                DecoratorLayersPerLeaf = 1,
                SimulatedWorkIterationsPerLeaf = 4,
                TrackedKeysPerAgent = 8,
                WarmupFrames = 2,
                MeasurementFrames = 6,
                TicksPerFrame = 1,
                EnableDeltaFlush = true,
                EnableDeterministicHashCheck = true,
                HashCheckIntervalFrames = 2,
                SoakFrames = 3,
                SoakSampleIntervalFrames = 1
            });

            runner.BeginBenchmark();

            int timeoutFrames = 120;
            while (runner.IsRunning && timeoutFrames-- > 0)
            {
                yield return null;
            }

            Assert.IsTrue(runner.HasCompleted);
            Assert.IsNotNull(runner.LastResult);
            Assert.IsTrue(runner.LastResult.IsValid);
            Assert.Greater(runner.LastResult.TotalTicks, 0);
            Assert.Greater(runner.LastResult.PotentialTicks, 0);
            Assert.Greater(runner.LastResult.AverageFrameMilliseconds, 0d);
            Assert.AreEqual("Medium", runner.LastResult.Complexity);
            Assert.AreEqual("LodCrowd", runner.LastResult.SchedulingProfile);
            Assert.Greater(runner.LastResult.SoakSampleCount, 0);
            Assert.GreaterOrEqual(runner.LastResult.TotalHashChecks, 0);

            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator BenchmarkRunner_RecommendedMatrix_Completes()
        {
            var gameObject = new GameObject("BT Benchmark Matrix Runner Test");
            var runner = gameObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = false;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.FullMatrix;
            runner.SetConfig(BehaviorTreeBenchmarkPresetCatalog.CreateConfig(
                BehaviorTreeBenchmarkPreset.AiCrowd1000,
                BehaviorTreeBenchmarkComplexity.Medium));

            runner.BeginBenchmark();

            int timeoutFrames = 2000;
            while (runner.IsRunning && timeoutFrames-- > 0)
            {
                yield return null;
            }

            Assert.IsTrue(runner.HasCompleted);
            Assert.IsNotNull(runner.LastBatchResult);
            Assert.IsNotNull(runner.LastBatchResult.Results);
            Assert.Greater(runner.LastBatchResult.Results.Length, 0);
            Assert.AreEqual(BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets().Length *
                            BehaviorTreeBenchmarkPresetCatalog.GetComplexityTiers().Length,
                runner.LastBatchResult.Results.Length);

            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator BenchmarkRunner_PriorityComparison_Completes()
        {
            var gameObject = new GameObject("BT Benchmark Priority Comparison Runner Test");
            var runner = gameObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = false;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.PriorityComparison;
            runner.SetConfig(BehaviorTreeBenchmarkPresetCatalog.CreateConfig(
                BehaviorTreeBenchmarkPreset.AiCrowd1000,
                BehaviorTreeBenchmarkComplexity.Medium));

            runner.BeginBenchmark();

            int timeoutFrames = 1200;
            while (runner.IsRunning && timeoutFrames-- > 0)
            {
                yield return null;
            }

            Assert.IsTrue(runner.HasCompleted);
            Assert.IsNotNull(runner.LastBatchResult);
            Assert.IsNotNull(runner.LastBatchResult.Results);
            Assert.AreEqual(4, runner.LastBatchResult.Results.Length);

            Object.Destroy(gameObject);
        }

        [Test]
        public void BenchmarkExportUtility_ProducesCsvAndJsonPayloads()
        {
            var result = new BehaviorTreeBenchmarkResult
            {
                IsValid = true,
                BenchmarkName = "Export Test",
                GeneratedAtUtc = "2026-04-18T00:00:00.0000000Z",
                Complexity = "Heavy",
                SchedulingProfile = "PriorityLod",
                AgentCount = 10,
                LeafNodesPerTree = 2,
                BlackboardReadsPerLeafPerTick = 3,
                WritesPerLeafPerTick = 1,
                DecoratorLayersPerLeaf = 2,
                SimulatedWorkIterationsPerLeaf = 8,
                TrackedKeysPerAgent = 4,
                WarmupFrames = 1,
                MeasurementFrames = 5,
                TicksPerFrame = 1,
                EnableDeltaFlush = true,
                EnableDeterministicHashCheck = true,
                HashCheckIntervalFrames = 3,
                SoakFrames = 10,
                SoakSampleIntervalFrames = 2,
                MeasuredFrameCount = 5,
                PotentialTicks = 50,
                TotalTicks = 50,
                ExecutedTicks = 40,
                EffectiveTickRatio = 0.8d,
                AverageActiveAgentsPerFrame = 8d,
                PeakActiveAgentsPerFrame = 10,
                TotalDeltaFlushes = 50,
                TotalHashChecks = 20,
                SoakSampleCount = 5,
                TotalElapsedMilliseconds = 10d,
                AverageFrameMilliseconds = 2d,
                MaxFrameMilliseconds = 3d,
                TicksPerSecond = 5000d,
                ManagedMemoryBeforeBytes = 100,
                ManagedMemoryAfterBytes = 200,
                ManagedMemoryDeltaBytes = 100,
                PeakManagedMemoryBytes = 256,
                SoakManagedMemoryDeltaBytes = 156,
                Gen0Collections = 0,
                Gen1Collections = 0,
                Gen2Collections = 0
            };

            string csv = BehaviorTreeBenchmarkExportUtility.ToCsv(result);
            string json = BehaviorTreeBenchmarkExportUtility.ToJson(result);

            StringAssert.Contains("BenchmarkName", csv);
            StringAssert.Contains("Complexity", csv);
            StringAssert.Contains("SchedulingProfile", csv);
            StringAssert.Contains("Export Test", csv);
            StringAssert.Contains("\"BenchmarkName\": \"Export Test\"", json);
            StringAssert.Contains("\"Complexity\": \"Heavy\"", json);
            StringAssert.Contains("\"SchedulingProfile\": \"PriorityLod\"", json);
        }

        [Test]
        public void BenchmarkExportUtility_CanWriteResultFiles()
        {
            var result = new BehaviorTreeBenchmarkResult
            {
                IsValid = true,
                BenchmarkName = "Write Test",
                GeneratedAtUtc = "2026-04-18T00:00:00.0000000Z"
            };

            string directory = Path.Combine(Application.temporaryCachePath, "BTBenchmarkWriteTest");
            string path = BehaviorTreeBenchmarkExportUtility.WriteResultFiles(directory, result, writeCsv: true, writeJson: true);

            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsTrue(Directory.Exists(directory));
        }
    }
}
