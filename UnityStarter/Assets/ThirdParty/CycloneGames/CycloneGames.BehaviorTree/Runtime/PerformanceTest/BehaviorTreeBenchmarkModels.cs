using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.PerformanceTest
{
    [Serializable]
    public enum BehaviorTreeBenchmarkRunnerMode
    {
        Single = 0,
        RecommendedMatrix = 1,
        FullMatrix = 2,
        PriorityComparison = 3,
        ConfiguredBudgetMatrix = 4
    }

    [Serializable]
    public enum BehaviorTreeBenchmarkComplexity
    {
        Light = 0,
        Medium = 1,
        Heavy = 2
    }

    [Serializable]
    public enum BehaviorTreeBenchmarkPreset
    {
        Custom = 0,
        AiBattle500 = 1,
        AiCrowd1000 = 2,
        AiStress5000 = 3,
        AiExtreme10000 = 4,
        Network100Players500Ai = 5,
        LongSoak1000 = 6
    }

    [Serializable]
    public enum BehaviorTreeBenchmarkSchedulingProfile
    {
        FullRate = 0,
        LodCrowd = 1,
        PriorityLod = 2,
        NetworkMixed = 3,
        FarCrowd = 4,
        UltraLod = 5,
        PriorityManaged = 6
    }

    [Serializable]
    public class BehaviorTreeBenchmarkConfig
    {
        public BehaviorTreeBenchmarkPreset Preset = BehaviorTreeBenchmarkPreset.Custom;
        public BehaviorTreeBenchmarkComplexity Complexity = BehaviorTreeBenchmarkComplexity.Medium;
        public BehaviorTreeBenchmarkSchedulingProfile SchedulingProfile = BehaviorTreeBenchmarkSchedulingProfile.FullRate;
        public string BenchmarkName = "Managed Runtime Benchmark";
        public int AgentCount = 1000;
        public int LeafNodesPerTree = 8;
        public int BlackboardReadsPerLeafPerTick = 3;
        public int WritesPerLeafPerTick = 2;
        public int DecoratorLayersPerLeaf = 1;
        public int SimulatedWorkIterationsPerLeaf = 12;
        public int TrackedKeysPerAgent = 16;
        public int WarmupFrames = 10;
        public int MeasurementFrames = 120;
        public int TicksPerFrame = 1;
        public bool EnableDeltaFlush = true;
        public bool EnableDeterministicHashCheck = false;
        public int HashCheckIntervalFrames = 30;
        public int SoakFrames = 0;
        public int SoakSampleIntervalFrames = 60;
        public double TargetAverageFrameMilliseconds = 16.6667d;
        public double TargetMaxFrameMilliseconds = 33.3334d;
        public long MaxManagedMemoryDeltaBytes = 8L * 1024L * 1024L;
        public int MaxGcCollections = 0;
        public double MinimumEffectiveTickRatio = 1d;

        public BehaviorTreeBenchmarkConfig Clone()
        {
            return new BehaviorTreeBenchmarkConfig
            {
                Preset = Preset,
                Complexity = Complexity,
                SchedulingProfile = SchedulingProfile,
                BenchmarkName = BenchmarkName,
                AgentCount = AgentCount,
                LeafNodesPerTree = LeafNodesPerTree,
                BlackboardReadsPerLeafPerTick = BlackboardReadsPerLeafPerTick,
                WritesPerLeafPerTick = WritesPerLeafPerTick,
                DecoratorLayersPerLeaf = DecoratorLayersPerLeaf,
                SimulatedWorkIterationsPerLeaf = SimulatedWorkIterationsPerLeaf,
                TrackedKeysPerAgent = TrackedKeysPerAgent,
                WarmupFrames = WarmupFrames,
                MeasurementFrames = MeasurementFrames,
                TicksPerFrame = TicksPerFrame,
                EnableDeltaFlush = EnableDeltaFlush,
                EnableDeterministicHashCheck = EnableDeterministicHashCheck,
                HashCheckIntervalFrames = HashCheckIntervalFrames,
                SoakFrames = SoakFrames,
                SoakSampleIntervalFrames = SoakSampleIntervalFrames,
                TargetAverageFrameMilliseconds = TargetAverageFrameMilliseconds,
                TargetMaxFrameMilliseconds = TargetMaxFrameMilliseconds,
                MaxManagedMemoryDeltaBytes = MaxManagedMemoryDeltaBytes,
                MaxGcCollections = MaxGcCollections,
                MinimumEffectiveTickRatio = MinimumEffectiveTickRatio
            };
        }

        public void Sanitize()
        {
            BenchmarkName ??= "Managed Runtime Benchmark";
            AgentCount = Mathf.Max(1, AgentCount);
            LeafNodesPerTree = Mathf.Max(1, LeafNodesPerTree);
            BlackboardReadsPerLeafPerTick = Mathf.Max(0, BlackboardReadsPerLeafPerTick);
            WritesPerLeafPerTick = Mathf.Max(1, WritesPerLeafPerTick);
            DecoratorLayersPerLeaf = Mathf.Max(0, DecoratorLayersPerLeaf);
            SimulatedWorkIterationsPerLeaf = Mathf.Max(0, SimulatedWorkIterationsPerLeaf);
            TrackedKeysPerAgent = Mathf.Max(0, TrackedKeysPerAgent);
            WarmupFrames = Mathf.Max(0, WarmupFrames);
            MeasurementFrames = Mathf.Max(1, MeasurementFrames);
            TicksPerFrame = Mathf.Max(1, TicksPerFrame);
            HashCheckIntervalFrames = Mathf.Max(1, HashCheckIntervalFrames);
            SoakFrames = Mathf.Max(0, SoakFrames);
            SoakSampleIntervalFrames = Mathf.Max(1, SoakSampleIntervalFrames);
            TargetAverageFrameMilliseconds = Math.Max(0.0001d, TargetAverageFrameMilliseconds);
            TargetMaxFrameMilliseconds = Math.Max(TargetAverageFrameMilliseconds, TargetMaxFrameMilliseconds);
            MaxManagedMemoryDeltaBytes = Math.Max(0L, MaxManagedMemoryDeltaBytes);
            MaxGcCollections = Mathf.Max(0, MaxGcCollections);
            MinimumEffectiveTickRatio = Math.Max(0d, Math.Min(1d, MinimumEffectiveTickRatio));
        }
    }

    [Serializable]
    public class BehaviorTreeBenchmarkResult
    {
        public bool IsValid;
        public string BenchmarkName;
        public string GeneratedAtUtc;
        public string Complexity;
        public string SchedulingProfile;
        public int AgentCount;
        public int LeafNodesPerTree;
        public int BlackboardReadsPerLeafPerTick;
        public int WritesPerLeafPerTick;
        public int DecoratorLayersPerLeaf;
        public int SimulatedWorkIterationsPerLeaf;
        public int TrackedKeysPerAgent;
        public int WarmupFrames;
        public int MeasurementFrames;
        public int TicksPerFrame;
        public bool EnableDeltaFlush;
        public bool EnableDeterministicHashCheck;
        public int HashCheckIntervalFrames;
        public int SoakFrames;
        public int SoakSampleIntervalFrames;
        public int MeasuredFrameCount;
        public int PotentialTicks;
        public int TotalTicks;
        public int ExecutedTicks;
        public double EffectiveTickRatio;
        public double AverageActiveAgentsPerFrame;
        public int PeakActiveAgentsPerFrame;
        public int TotalDeltaFlushes;
        public int WarmupDeltaFlushes;
        public int MeasuredDeltaFlushes;
        public int TotalHashChecks;
        public int SoakSampleCount;
        public double TotalElapsedMilliseconds;
        public double AverageFrameMilliseconds;
        public double MaxFrameMilliseconds;
        public double TicksPerSecond;
        public long ManagedMemoryBeforeBytes;
        public long ManagedMemoryAfterBytes;
        public long ManagedMemoryDeltaBytes;
        public long PeakManagedMemoryBytes;
        public long SoakManagedMemoryDeltaBytes;
        public int Gen0Collections;
        public int Gen1Collections;
        public int Gen2Collections;
        public double TargetAverageFrameMilliseconds;
        public double TargetMaxFrameMilliseconds;
        public long MaxManagedMemoryDeltaBytes;
        public int MaxGcCollections;
        public double MinimumEffectiveTickRatio;
        public bool AverageFrameBudgetPassed;
        public bool MaxFrameBudgetPassed;
        public bool ManagedMemoryBudgetPassed;
        public bool GcBudgetPassed;
        public bool EffectiveTickRatioBudgetPassed;
        public bool ProductionBudgetPassed;
        public string BudgetSummary;
    }

    [Serializable]
    public class BehaviorTreeBenchmarkBatchResult
    {
        public string GeneratedAtUtc;
        public string BatchName;
        public BehaviorTreeBenchmarkResult[] Results;
        public int CaseCount;
        public int PassedCount;
        public int FailedCount;
        public double SlowestAverageFrameMilliseconds;
        public double SlowestMaxFrameMilliseconds;
        public long PeakManagedMemoryBytes;
        public string Summary;
    }

    public static class BehaviorTreeBenchmarkPresetCatalog
    {
        private const long BYTES_PER_MIB = 1024L * 1024L;
        private const long BASE_MANAGED_MEMORY_BUDGET_BYTES = 8L * BYTES_PER_MIB;
        private const long PER_AGENT_STATE_BUDGET_BYTES = 2048L;
        private const long PER_LEAF_NODE_BUDGET_BYTES = 256L;
        private const long PER_DECORATOR_LAYER_BUDGET_BYTES = 128L;
        private const long PER_BLACKBOARD_KEY_BUDGET_BYTES = 128L;
        private const long PER_DELTA_TRACKER_BUDGET_BYTES = 1024L;
        private const long PER_DELTA_TRACKED_KEY_BUDGET_BYTES = 64L;

        private static readonly BehaviorTreeBenchmarkPreset[] RecommendedPresets =
        {
            BehaviorTreeBenchmarkPreset.AiBattle500,
            BehaviorTreeBenchmarkPreset.AiCrowd1000,
            BehaviorTreeBenchmarkPreset.AiStress5000,
            BehaviorTreeBenchmarkPreset.AiExtreme10000,
            BehaviorTreeBenchmarkPreset.Network100Players500Ai
        };

        private static readonly BehaviorTreeBenchmarkPreset[] ConfiguredBudgetPresets =
        {
            BehaviorTreeBenchmarkPreset.AiBattle500,
            BehaviorTreeBenchmarkPreset.AiCrowd1000,
            BehaviorTreeBenchmarkPreset.AiStress5000,
            BehaviorTreeBenchmarkPreset.AiExtreme10000,
            BehaviorTreeBenchmarkPreset.Network100Players500Ai,
            BehaviorTreeBenchmarkPreset.LongSoak1000
        };

        private static readonly BehaviorTreeBenchmarkComplexity[] ComplexityTiers =
        {
            BehaviorTreeBenchmarkComplexity.Light,
            BehaviorTreeBenchmarkComplexity.Medium,
            BehaviorTreeBenchmarkComplexity.Heavy
        };

        private static readonly BehaviorTreeBenchmarkSchedulingProfile[] SchedulingProfiles =
        {
            BehaviorTreeBenchmarkSchedulingProfile.FullRate,
            BehaviorTreeBenchmarkSchedulingProfile.LodCrowd,
            BehaviorTreeBenchmarkSchedulingProfile.PriorityLod,
            BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed,
            BehaviorTreeBenchmarkSchedulingProfile.FarCrowd,
            BehaviorTreeBenchmarkSchedulingProfile.UltraLod,
            BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged
        };

        public static BehaviorTreeBenchmarkPreset[] GetRecommendedPresets()
        {
            var copy = new BehaviorTreeBenchmarkPreset[RecommendedPresets.Length];
            Array.Copy(RecommendedPresets, copy, RecommendedPresets.Length);
            return copy;
        }

        public static BehaviorTreeBenchmarkPreset[] GetConfiguredBudgetPresets()
        {
            var copy = new BehaviorTreeBenchmarkPreset[ConfiguredBudgetPresets.Length];
            Array.Copy(ConfiguredBudgetPresets, copy, ConfiguredBudgetPresets.Length);
            return copy;
        }

        public static BehaviorTreeBenchmarkComplexity[] GetComplexityTiers()
        {
            var copy = new BehaviorTreeBenchmarkComplexity[ComplexityTiers.Length];
            Array.Copy(ComplexityTiers, copy, ComplexityTiers.Length);
            return copy;
        }

        public static BehaviorTreeBenchmarkSchedulingProfile[] GetSchedulingProfiles()
        {
            var copy = new BehaviorTreeBenchmarkSchedulingProfile[SchedulingProfiles.Length];
            Array.Copy(SchedulingProfiles, copy, SchedulingProfiles.Length);
            return copy;
        }

        public static BehaviorTreeBenchmarkConfig CreateConfig(BehaviorTreeBenchmarkPreset preset,
            BehaviorTreeBenchmarkComplexity complexity = BehaviorTreeBenchmarkComplexity.Medium)
        {
            var config = preset switch
            {
                BehaviorTreeBenchmarkPreset.AiBattle500 => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 500,
                    LeafNodesPerTree = 10,
                    WarmupFrames = 10,
                    MeasurementFrames = 180,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true
                },
                BehaviorTreeBenchmarkPreset.AiCrowd1000 => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 1000,
                    LeafNodesPerTree = 8,
                    WarmupFrames = 10,
                    MeasurementFrames = 120,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true
                },
                BehaviorTreeBenchmarkPreset.AiStress5000 => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 5000,
                    LeafNodesPerTree = 6,
                    WarmupFrames = 8,
                    MeasurementFrames = 90,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true
                },
                BehaviorTreeBenchmarkPreset.AiExtreme10000 => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 10000,
                    LeafNodesPerTree = 4,
                    WarmupFrames = 5,
                    MeasurementFrames = 60,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true
                },
                BehaviorTreeBenchmarkPreset.Network100Players500Ai => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 600,
                    LeafNodesPerTree = 12,
                    WarmupFrames = 10,
                    MeasurementFrames = 150,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true
                },
                BehaviorTreeBenchmarkPreset.LongSoak1000 => new BehaviorTreeBenchmarkConfig
                {
                    Preset = preset,
                    AgentCount = 1000,
                    LeafNodesPerTree = 8,
                    WarmupFrames = 30,
                    MeasurementFrames = 300,
                    TicksPerFrame = 1,
                    EnableDeltaFlush = true,
                    SoakFrames = 3600,
                    SoakSampleIntervalFrames = 30
                },
                _ => new BehaviorTreeBenchmarkConfig()
            };

            ApplyComplexity(config, complexity);
            ApplySchedulingProfile(config, GetRecommendedSchedulingProfile(preset));
            ApplyProductionBudgets(config);
            config.Sanitize();
            return config;
        }

        public static BehaviorTreeBenchmarkConfig[] CreateConfiguredBudgetMatrixConfigs()
        {
            var configs = new BehaviorTreeBenchmarkConfig[ConfiguredBudgetPresets.Length];
            for (int i = 0; i < ConfiguredBudgetPresets.Length; i++)
            {
                BehaviorTreeBenchmarkPreset preset = ConfiguredBudgetPresets[i];
                BehaviorTreeBenchmarkComplexity complexity = preset switch
                {
                    BehaviorTreeBenchmarkPreset.AiBattle500 => BehaviorTreeBenchmarkComplexity.Heavy,
                    BehaviorTreeBenchmarkPreset.AiCrowd1000 => BehaviorTreeBenchmarkComplexity.Heavy,
                    BehaviorTreeBenchmarkPreset.AiStress5000 => BehaviorTreeBenchmarkComplexity.Heavy,
                    BehaviorTreeBenchmarkPreset.AiExtreme10000 => BehaviorTreeBenchmarkComplexity.Heavy,
                    BehaviorTreeBenchmarkPreset.Network100Players500Ai => BehaviorTreeBenchmarkComplexity.Heavy,
                    BehaviorTreeBenchmarkPreset.LongSoak1000 => BehaviorTreeBenchmarkComplexity.Medium,
                    _ => BehaviorTreeBenchmarkComplexity.Medium
                };

                configs[i] = CreateConfig(preset, complexity);
            }

            return configs;
        }

        public static void ApplyComplexity(BehaviorTreeBenchmarkConfig config, BehaviorTreeBenchmarkComplexity complexity)
        {
            if (config == null)
            {
                return;
            }

            config.Complexity = complexity;
            string baseLabel = GetPresetLabel(config.Preset);

            switch (complexity)
            {
                case BehaviorTreeBenchmarkComplexity.Light:
                    config.BlackboardReadsPerLeafPerTick = 1;
                    config.WritesPerLeafPerTick = config.LeafNodesPerTree >= 10 ? 2 : 1;
                    config.DecoratorLayersPerLeaf = 0;
                    config.SimulatedWorkIterationsPerLeaf = 2;
                    config.TrackedKeysPerAgent = Math.Max(4, config.LeafNodesPerTree * config.WritesPerLeafPerTick / 2);
                    config.BenchmarkName = $"{baseLabel} [Light]";
                    break;
                case BehaviorTreeBenchmarkComplexity.Heavy:
                    config.BlackboardReadsPerLeafPerTick = 6;
                    config.WritesPerLeafPerTick = Math.Max(3, Mathf.CeilToInt(config.LeafNodesPerTree / 3f));
                    config.DecoratorLayersPerLeaf = 2;
                    config.SimulatedWorkIterationsPerLeaf = 32;
                    config.TrackedKeysPerAgent = Math.Max(16, config.LeafNodesPerTree * config.WritesPerLeafPerTick);
                    config.BenchmarkName = $"{baseLabel} [Heavy]";
                    break;
                default:
                    config.BlackboardReadsPerLeafPerTick = 3;
                    config.WritesPerLeafPerTick = Math.Max(2, Mathf.CeilToInt(config.LeafNodesPerTree / 4f));
                    config.DecoratorLayersPerLeaf = 1;
                    config.SimulatedWorkIterationsPerLeaf = 12;
                    config.TrackedKeysPerAgent = Math.Max(8, Mathf.CeilToInt(config.LeafNodesPerTree * config.WritesPerLeafPerTick * 0.8f));
                    config.BenchmarkName = $"{baseLabel} [Medium]";
                    break;
            }
        }

        public static void ApplySchedulingProfile(BehaviorTreeBenchmarkConfig config, BehaviorTreeBenchmarkSchedulingProfile schedulingProfile)
        {
            if (config == null)
            {
                return;
            }

            config.SchedulingProfile = schedulingProfile;
            config.EnableDeterministicHashCheck = schedulingProfile == BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed;

            switch (schedulingProfile)
            {
                case BehaviorTreeBenchmarkSchedulingProfile.LodCrowd:
                    config.HashCheckIntervalFrames = 45;
                    break;
                case BehaviorTreeBenchmarkSchedulingProfile.FarCrowd:
                    config.HashCheckIntervalFrames = 60;
                    break;
                case BehaviorTreeBenchmarkSchedulingProfile.PriorityLod:
                    config.HashCheckIntervalFrames = 30;
                    break;
                case BehaviorTreeBenchmarkSchedulingProfile.UltraLod:
                    config.HashCheckIntervalFrames = 90;
                    break;
                case BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged:
                    config.HashCheckIntervalFrames = 30;
                    break;
                case BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed:
                    config.HashCheckIntervalFrames = 15;
                    break;
                default:
                    config.HashCheckIntervalFrames = 60;
                    break;
            }

            config.MinimumEffectiveTickRatio = GetExpectedEffectiveTickRatio(schedulingProfile) * 0.9d;
        }

        public static void ApplyProductionBudgets(BehaviorTreeBenchmarkConfig config)
        {
            if (config == null)
            {
                return;
            }

            double averageBudget = config.Preset switch
            {
                BehaviorTreeBenchmarkPreset.AiBattle500 => 8.3334d,
                BehaviorTreeBenchmarkPreset.AiCrowd1000 => 12.5d,
                BehaviorTreeBenchmarkPreset.AiStress5000 => 16.6667d,
                BehaviorTreeBenchmarkPreset.AiExtreme10000 => 33.3334d,
                BehaviorTreeBenchmarkPreset.Network100Players500Ai => 16.6667d,
                BehaviorTreeBenchmarkPreset.LongSoak1000 => 16.6667d,
                _ => 16.6667d
            };

            if (config.Complexity == BehaviorTreeBenchmarkComplexity.Heavy)
            {
                averageBudget *= config.Preset == BehaviorTreeBenchmarkPreset.AiExtreme10000 ? 1d : 1.25d;
            }
            else if (config.Complexity == BehaviorTreeBenchmarkComplexity.Light)
            {
                averageBudget *= 0.75d;
            }

            config.TargetAverageFrameMilliseconds = averageBudget;
            config.TargetMaxFrameMilliseconds = averageBudget * 2d;
            config.MaxManagedMemoryDeltaBytes = EstimateManagedMemoryDeltaBudgetBytes(config);
            config.MaxGcCollections = 0;
            config.MinimumEffectiveTickRatio = GetExpectedEffectiveTickRatio(config.SchedulingProfile) * 0.9d;
            config.Sanitize();
        }

        public static long EstimateManagedMemoryDeltaBudgetBytes(BehaviorTreeBenchmarkConfig config)
        {
            if (config == null)
            {
                return BASE_MANAGED_MEMORY_BUDGET_BYTES;
            }

            int agentCount = Math.Max(1, config.AgentCount);
            int leafCount = Math.Max(1, config.LeafNodesPerTree);
            int decoratorLayers = Math.Max(0, config.DecoratorLayersPerLeaf);
            int writeCount = Math.Max(1, config.WritesPerLeafPerTick);
            int trackedKeys = Math.Max(config.TrackedKeysPerAgent, leafCount * writeCount);

            long agentStateBudget = agentCount * PER_AGENT_STATE_BUDGET_BYTES;
            long nodeBudget = agentCount
                              * leafCount
                              * (PER_LEAF_NODE_BUDGET_BYTES + (decoratorLayers * PER_DECORATOR_LAYER_BUDGET_BYTES));
            long blackboardBudget = agentCount * trackedKeys * PER_BLACKBOARD_KEY_BUDGET_BYTES;
            long deltaBudget = agentCount * (PER_DELTA_TRACKER_BUDGET_BYTES + (trackedKeys * PER_DELTA_TRACKED_KEY_BUDGET_BYTES));

            long networkBudget = config.SchedulingProfile == BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed
                                 || config.EnableDeterministicHashCheck
                ? BYTES_PER_MIB + (agentCount * 512L)
                : 0L;

            long soakBudget = config.SoakFrames > 0
                ? (2L * BYTES_PER_MIB) + (agentCount * 512L)
                : 0L;

            return BASE_MANAGED_MEMORY_BUDGET_BYTES
                   + agentStateBudget
                   + nodeBudget
                   + blackboardBudget
                   + deltaBudget
                   + networkBudget
                   + soakBudget;
        }

        public static double GetExpectedEffectiveTickRatio(BehaviorTreeBenchmarkSchedulingProfile schedulingProfile)
        {
            return schedulingProfile switch
            {
                BehaviorTreeBenchmarkSchedulingProfile.LodCrowd => 0.45d,
                BehaviorTreeBenchmarkSchedulingProfile.FarCrowd => 0.2083d,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityLod => 0.5167d,
                BehaviorTreeBenchmarkSchedulingProfile.UltraLod => 0.1438d,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged => 0.325d,
                BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed => 0.6354d,
                _ => 1d
            };
        }

        public static BehaviorTreeBenchmarkSchedulingProfile GetRecommendedSchedulingProfile(BehaviorTreeBenchmarkPreset preset)
        {
            return preset switch
            {
                BehaviorTreeBenchmarkPreset.AiBattle500 => BehaviorTreeBenchmarkSchedulingProfile.FullRate,
                BehaviorTreeBenchmarkPreset.AiCrowd1000 => BehaviorTreeBenchmarkSchedulingProfile.LodCrowd,
                BehaviorTreeBenchmarkPreset.AiStress5000 => BehaviorTreeBenchmarkSchedulingProfile.FarCrowd,
                BehaviorTreeBenchmarkPreset.AiExtreme10000 => BehaviorTreeBenchmarkSchedulingProfile.UltraLod,
                BehaviorTreeBenchmarkPreset.Network100Players500Ai => BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed,
                BehaviorTreeBenchmarkPreset.LongSoak1000 => BehaviorTreeBenchmarkSchedulingProfile.FarCrowd,
                _ => BehaviorTreeBenchmarkSchedulingProfile.FullRate
            };
        }

        public static string GetPresetLabel(BehaviorTreeBenchmarkPreset preset)
        {
            return preset switch
            {
                BehaviorTreeBenchmarkPreset.AiBattle500 => "500 AI Battle",
                BehaviorTreeBenchmarkPreset.AiCrowd1000 => "1000 AI Crowd",
                BehaviorTreeBenchmarkPreset.AiStress5000 => "5000 AI Stress",
                BehaviorTreeBenchmarkPreset.AiExtreme10000 => "10000 AI Extreme",
                BehaviorTreeBenchmarkPreset.Network100Players500Ai => "100 Players + 500 AI",
                BehaviorTreeBenchmarkPreset.LongSoak1000 => "Long Soak 1000 AI",
                _ => "Custom Benchmark"
            };
        }
    }

    public static class BehaviorTreeBenchmarkAssessmentUtility
    {
        public static void Evaluate(BehaviorTreeBenchmarkResult result)
        {
            if (result == null)
            {
                return;
            }

            result.AverageFrameBudgetPassed = result.AverageFrameMilliseconds <= result.TargetAverageFrameMilliseconds;
            result.MaxFrameBudgetPassed = result.MaxFrameMilliseconds <= result.TargetMaxFrameMilliseconds;
            long maxMemoryDeltaBytes = Math.Max(0L, result.MaxManagedMemoryDeltaBytes);
            result.ManagedMemoryBudgetPassed =
                result.ManagedMemoryDeltaBytes >= -maxMemoryDeltaBytes
                && result.ManagedMemoryDeltaBytes <= maxMemoryDeltaBytes;
            result.GcBudgetPassed = result.Gen0Collections + result.Gen1Collections + result.Gen2Collections <= result.MaxGcCollections;
            result.EffectiveTickRatioBudgetPassed = result.EffectiveTickRatio + 0.000001d >= result.MinimumEffectiveTickRatio;
            result.ProductionBudgetPassed =
                result.IsValid
                && result.AverageFrameBudgetPassed
                && result.MaxFrameBudgetPassed
                && result.ManagedMemoryBudgetPassed
                && result.GcBudgetPassed
                && result.EffectiveTickRatioBudgetPassed;

            result.BudgetSummary = CreateSummary(result);
        }

        public static void PopulateBatchSummary(BehaviorTreeBenchmarkBatchResult batch)
        {
            if (batch == null || batch.Results == null)
            {
                return;
            }

            int passed = 0;
            int failed = 0;
            double slowestAverage = 0d;
            double slowestMax = 0d;
            long peakMemory = 0L;

            for (int i = 0; i < batch.Results.Length; i++)
            {
                BehaviorTreeBenchmarkResult result = batch.Results[i];
                if (result == null)
                {
                    failed++;
                    continue;
                }

                if (string.IsNullOrEmpty(result.BudgetSummary))
                {
                    Evaluate(result);
                }

                if (result.ProductionBudgetPassed)
                {
                    passed++;
                }
                else
                {
                    failed++;
                }

                slowestAverage = Math.Max(slowestAverage, result.AverageFrameMilliseconds);
                slowestMax = Math.Max(slowestMax, result.MaxFrameMilliseconds);
                peakMemory = Math.Max(peakMemory, result.PeakManagedMemoryBytes);
            }

            batch.CaseCount = batch.Results.Length;
            batch.PassedCount = passed;
            batch.FailedCount = failed;
            batch.SlowestAverageFrameMilliseconds = slowestAverage;
            batch.SlowestMaxFrameMilliseconds = slowestMax;
            batch.PeakManagedMemoryBytes = peakMemory;
            batch.Summary = failed == 0
                ? $"PASS: {passed}/{batch.CaseCount} cases within configured budgets."
                : $"FAIL: {failed}/{batch.CaseCount} cases exceeded configured budgets.";
        }

        private static string CreateSummary(BehaviorTreeBenchmarkResult result)
        {
            if (result.ProductionBudgetPassed)
            {
                return "PASS: all configured budgets satisfied.";
            }

            var builder = new StringBuilder(160);
            builder.Append("FAIL:");
            AppendFailure(builder, result.IsValid, " invalid result");
            AppendFailure(builder, result.AverageFrameBudgetPassed, " average frame");
            AppendFailure(builder, result.MaxFrameBudgetPassed, " max frame");
            AppendFailure(builder, result.ManagedMemoryBudgetPassed, " managed memory delta");
            AppendFailure(builder, result.GcBudgetPassed, " GC collections");
            AppendFailure(builder, result.EffectiveTickRatioBudgetPassed, " effective tick ratio");
            return builder.ToString();
        }

        private static void AppendFailure(StringBuilder builder, bool passed, string label)
        {
            if (passed)
            {
                return;
            }

            if (builder.Length > "FAIL:".Length)
            {
                builder.Append(',');
            }

            builder.Append(label);
        }
    }

    public static class BehaviorTreeBenchmarkExportUtility
    {
        public static string ToJson(BehaviorTreeBenchmarkResult result)
        {
            if (result != null && string.IsNullOrEmpty(result.BudgetSummary))
            {
                BehaviorTreeBenchmarkAssessmentUtility.Evaluate(result);
            }

            var builder = new StringBuilder(2048);
            builder.AppendLine("{");
            AppendJson(builder, "IsValid", result.IsValid ? "true" : "false", false);
            AppendJson(builder, "BenchmarkName", result.BenchmarkName);
            AppendJson(builder, "GeneratedAtUtc", result.GeneratedAtUtc);
            AppendJson(builder, "Complexity", result.Complexity);
            AppendJson(builder, "SchedulingProfile", result.SchedulingProfile);
            AppendJson(builder, "AgentCount", result.AgentCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "LeafNodesPerTree", result.LeafNodesPerTree.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "BlackboardReadsPerLeafPerTick", result.BlackboardReadsPerLeafPerTick.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "WritesPerLeafPerTick", result.WritesPerLeafPerTick.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "DecoratorLayersPerLeaf", result.DecoratorLayersPerLeaf.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SimulatedWorkIterationsPerLeaf", result.SimulatedWorkIterationsPerLeaf.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TrackedKeysPerAgent", result.TrackedKeysPerAgent.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "WarmupFrames", result.WarmupFrames.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MeasurementFrames", result.MeasurementFrames.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TicksPerFrame", result.TicksPerFrame.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "EnableDeltaFlush", result.EnableDeltaFlush ? "true" : "false", false);
            AppendJson(builder, "EnableDeterministicHashCheck", result.EnableDeterministicHashCheck ? "true" : "false", false);
            AppendJson(builder, "HashCheckIntervalFrames", result.HashCheckIntervalFrames.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SoakFrames", result.SoakFrames.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SoakSampleIntervalFrames", result.SoakSampleIntervalFrames.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MeasuredFrameCount", result.MeasuredFrameCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "PotentialTicks", result.PotentialTicks.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TotalTicks", result.TotalTicks.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "ExecutedTicks", result.ExecutedTicks.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "EffectiveTickRatio", result.EffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "AverageActiveAgentsPerFrame", result.AverageActiveAgentsPerFrame.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "PeakActiveAgentsPerFrame", result.PeakActiveAgentsPerFrame.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TotalDeltaFlushes", result.TotalDeltaFlushes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "WarmupDeltaFlushes", result.WarmupDeltaFlushes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MeasuredDeltaFlushes", result.MeasuredDeltaFlushes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TotalHashChecks", result.TotalHashChecks.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SoakSampleCount", result.SoakSampleCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TotalElapsedMilliseconds", result.TotalElapsedMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "AverageFrameMilliseconds", result.AverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MaxFrameMilliseconds", result.MaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TicksPerSecond", result.TicksPerSecond.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "ManagedMemoryBeforeBytes", result.ManagedMemoryBeforeBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "ManagedMemoryAfterBytes", result.ManagedMemoryAfterBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "ManagedMemoryDeltaBytes", result.ManagedMemoryDeltaBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "PeakManagedMemoryBytes", result.PeakManagedMemoryBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SoakManagedMemoryDeltaBytes", result.SoakManagedMemoryDeltaBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "Gen0Collections", result.Gen0Collections.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "Gen1Collections", result.Gen1Collections.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "Gen2Collections", result.Gen2Collections.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TargetAverageFrameMilliseconds", result.TargetAverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "TargetMaxFrameMilliseconds", result.TargetMaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MaxManagedMemoryDeltaBytes", result.MaxManagedMemoryDeltaBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MaxGcCollections", result.MaxGcCollections.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "MinimumEffectiveTickRatio", result.MinimumEffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "AverageFrameBudgetPassed", result.AverageFrameBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "MaxFrameBudgetPassed", result.MaxFrameBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "ManagedMemoryBudgetPassed", result.ManagedMemoryBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "GcBudgetPassed", result.GcBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "EffectiveTickRatioBudgetPassed", result.EffectiveTickRatioBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "ProductionBudgetPassed", result.ProductionBudgetPassed ? "true" : "false", false);
            AppendJson(builder, "BudgetSummary", result.BudgetSummary, true, true);
            builder.Append('}');
            return builder.ToString();
        }

        public static string ToJson(BehaviorTreeBenchmarkBatchResult batch)
        {
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(batch);
            var builder = new StringBuilder(8192);
            builder.AppendLine("{");
            AppendJson(builder, "GeneratedAtUtc", batch.GeneratedAtUtc);
            AppendJson(builder, "BatchName", batch.BatchName);
            AppendJson(builder, "CaseCount", batch.CaseCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "PassedCount", batch.PassedCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "FailedCount", batch.FailedCount.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SlowestAverageFrameMilliseconds", batch.SlowestAverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "SlowestMaxFrameMilliseconds", batch.SlowestMaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture), false);
            AppendJson(builder, "PeakManagedMemoryBytes", batch.PeakManagedMemoryBytes.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(builder, "Summary", batch.Summary);
            builder.AppendLine("  \"Results\": [");
            if (batch.Results != null)
            {
                for (int i = 0; i < batch.Results.Length; i++)
                {
                    string item = IndentJson(ToJson(batch.Results[i]), 4);
                    builder.Append(item);
                    if (i < batch.Results.Length - 1)
                    {
                        builder.Append(',');
                    }
                    builder.AppendLine();
                }
            }
            builder.AppendLine("  ]");
            builder.Append('}');
            return builder.ToString();
        }

        public static string ToCsv(BehaviorTreeBenchmarkResult result)
        {
            if (result != null && string.IsNullOrEmpty(result.BudgetSummary))
            {
                BehaviorTreeBenchmarkAssessmentUtility.Evaluate(result);
            }

            var builder = new StringBuilder(1536);
            builder.AppendLine("BenchmarkName,GeneratedAtUtc,Complexity,SchedulingProfile,AgentCount,LeafNodesPerTree,BlackboardReadsPerLeafPerTick,WritesPerLeafPerTick,DecoratorLayersPerLeaf,SimulatedWorkIterationsPerLeaf,TrackedKeysPerAgent,WarmupFrames,MeasurementFrames,TicksPerFrame,EnableDeltaFlush,EnableDeterministicHashCheck,HashCheckIntervalFrames,SoakFrames,SoakSampleIntervalFrames,MeasuredFrameCount,PotentialTicks,TotalTicks,ExecutedTicks,EffectiveTickRatio,AverageActiveAgentsPerFrame,PeakActiveAgentsPerFrame,TotalDeltaFlushes,WarmupDeltaFlushes,MeasuredDeltaFlushes,TotalHashChecks,SoakSampleCount,TotalElapsedMilliseconds,AverageFrameMilliseconds,MaxFrameMilliseconds,TicksPerSecond,ManagedMemoryBeforeBytes,ManagedMemoryAfterBytes,ManagedMemoryDeltaBytes,PeakManagedMemoryBytes,SoakManagedMemoryDeltaBytes,Gen0Collections,Gen1Collections,Gen2Collections,TargetAverageFrameMilliseconds,TargetMaxFrameMilliseconds,MaxManagedMemoryDeltaBytes,MaxGcCollections,MinimumEffectiveTickRatio,AverageFrameBudgetPassed,MaxFrameBudgetPassed,ManagedMemoryBudgetPassed,GcBudgetPassed,EffectiveTickRatioBudgetPassed,ProductionBudgetPassed,BudgetSummary");
            builder.Append(Escape(result.BenchmarkName)).Append(',')
                .Append(Escape(result.GeneratedAtUtc)).Append(',')
                .Append(result.Complexity).Append(',')
                .Append(result.SchedulingProfile).Append(',')
                .Append(result.AgentCount).Append(',')
                .Append(result.LeafNodesPerTree).Append(',')
                .Append(result.BlackboardReadsPerLeafPerTick).Append(',')
                .Append(result.WritesPerLeafPerTick).Append(',')
                .Append(result.DecoratorLayersPerLeaf).Append(',')
                .Append(result.SimulatedWorkIterationsPerLeaf).Append(',')
                .Append(result.TrackedKeysPerAgent).Append(',')
                .Append(result.WarmupFrames).Append(',')
                .Append(result.MeasurementFrames).Append(',')
                .Append(result.TicksPerFrame).Append(',')
                .Append(result.EnableDeltaFlush ? "true" : "false").Append(',')
                .Append(result.EnableDeterministicHashCheck ? "true" : "false").Append(',')
                .Append(result.HashCheckIntervalFrames).Append(',')
                .Append(result.SoakFrames).Append(',')
                .Append(result.SoakSampleIntervalFrames).Append(',')
                .Append(result.MeasuredFrameCount).Append(',')
                .Append(result.PotentialTicks).Append(',')
                .Append(result.TotalTicks).Append(',')
                .Append(result.ExecutedTicks).Append(',')
                .Append(result.EffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.AverageActiveAgentsPerFrame.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.PeakActiveAgentsPerFrame).Append(',')
                .Append(result.TotalDeltaFlushes).Append(',')
                .Append(result.WarmupDeltaFlushes).Append(',')
                .Append(result.MeasuredDeltaFlushes).Append(',')
                .Append(result.TotalHashChecks).Append(',')
                .Append(result.SoakSampleCount).Append(',')
                .Append(result.TotalElapsedMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.AverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.MaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.TicksPerSecond.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.ManagedMemoryBeforeBytes).Append(',')
                .Append(result.ManagedMemoryAfterBytes).Append(',')
                .Append(result.ManagedMemoryDeltaBytes).Append(',')
                .Append(result.PeakManagedMemoryBytes).Append(',')
                .Append(result.SoakManagedMemoryDeltaBytes).Append(',')
                .Append(result.Gen0Collections).Append(',')
                .Append(result.Gen1Collections).Append(',')
                .Append(result.Gen2Collections).Append(',')
                .Append(result.TargetAverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.TargetMaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.MaxManagedMemoryDeltaBytes).Append(',')
                .Append(result.MaxGcCollections).Append(',')
                .Append(result.MinimumEffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.AverageFrameBudgetPassed ? "true" : "false").Append(',')
                .Append(result.MaxFrameBudgetPassed ? "true" : "false").Append(',')
                .Append(result.ManagedMemoryBudgetPassed ? "true" : "false").Append(',')
                .Append(result.GcBudgetPassed ? "true" : "false").Append(',')
                .Append(result.EffectiveTickRatioBudgetPassed ? "true" : "false").Append(',')
                .Append(result.ProductionBudgetPassed ? "true" : "false").Append(',')
                .Append(Escape(result.BudgetSummary))
                .AppendLine();
            return builder.ToString();
        }

        public static string ToCsv(BehaviorTreeBenchmarkBatchResult batch)
        {
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(batch);
            var builder = new StringBuilder(8192);
            builder.AppendLine("BatchName,BatchSummary,CaseCount,PassedCount,FailedCount,BenchmarkName,GeneratedAtUtc,Complexity,SchedulingProfile,AgentCount,LeafNodesPerTree,BlackboardReadsPerLeafPerTick,WritesPerLeafPerTick,DecoratorLayersPerLeaf,SimulatedWorkIterationsPerLeaf,TrackedKeysPerAgent,WarmupFrames,MeasurementFrames,TicksPerFrame,EnableDeltaFlush,EnableDeterministicHashCheck,HashCheckIntervalFrames,SoakFrames,SoakSampleIntervalFrames,MeasuredFrameCount,PotentialTicks,TotalTicks,ExecutedTicks,EffectiveTickRatio,AverageActiveAgentsPerFrame,PeakActiveAgentsPerFrame,TotalDeltaFlushes,WarmupDeltaFlushes,MeasuredDeltaFlushes,TotalHashChecks,SoakSampleCount,TotalElapsedMilliseconds,AverageFrameMilliseconds,MaxFrameMilliseconds,TicksPerSecond,ManagedMemoryBeforeBytes,ManagedMemoryAfterBytes,ManagedMemoryDeltaBytes,PeakManagedMemoryBytes,SoakManagedMemoryDeltaBytes,Gen0Collections,Gen1Collections,Gen2Collections,TargetAverageFrameMilliseconds,TargetMaxFrameMilliseconds,MaxManagedMemoryDeltaBytes,MaxGcCollections,MinimumEffectiveTickRatio,AverageFrameBudgetPassed,MaxFrameBudgetPassed,ManagedMemoryBudgetPassed,GcBudgetPassed,EffectiveTickRatioBudgetPassed,ProductionBudgetPassed,BudgetSummary");
            if (batch.Results == null)
            {
                return builder.ToString();
            }

            for (int i = 0; i < batch.Results.Length; i++)
            {
                var result = batch.Results[i];
                builder.Append(Escape(batch.BatchName)).Append(',')
                    .Append(Escape(batch.Summary)).Append(',')
                    .Append(batch.CaseCount).Append(',')
                    .Append(batch.PassedCount).Append(',')
                    .Append(batch.FailedCount).Append(',')
                    .Append(Escape(result.BenchmarkName)).Append(',')
                    .Append(Escape(result.GeneratedAtUtc)).Append(',')
                    .Append(result.Complexity).Append(',')
                    .Append(result.SchedulingProfile).Append(',')
                    .Append(result.AgentCount).Append(',')
                    .Append(result.LeafNodesPerTree).Append(',')
                    .Append(result.BlackboardReadsPerLeafPerTick).Append(',')
                    .Append(result.WritesPerLeafPerTick).Append(',')
                    .Append(result.DecoratorLayersPerLeaf).Append(',')
                    .Append(result.SimulatedWorkIterationsPerLeaf).Append(',')
                    .Append(result.TrackedKeysPerAgent).Append(',')
                    .Append(result.WarmupFrames).Append(',')
                    .Append(result.MeasurementFrames).Append(',')
                    .Append(result.TicksPerFrame).Append(',')
                    .Append(result.EnableDeltaFlush ? "true" : "false").Append(',')
                    .Append(result.EnableDeterministicHashCheck ? "true" : "false").Append(',')
                    .Append(result.HashCheckIntervalFrames).Append(',')
                    .Append(result.SoakFrames).Append(',')
                    .Append(result.SoakSampleIntervalFrames).Append(',')
                    .Append(result.MeasuredFrameCount).Append(',')
                    .Append(result.PotentialTicks).Append(',')
                    .Append(result.TotalTicks).Append(',')
                    .Append(result.ExecutedTicks).Append(',')
                    .Append(result.EffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.AverageActiveAgentsPerFrame.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.PeakActiveAgentsPerFrame).Append(',')
                    .Append(result.TotalDeltaFlushes).Append(',')
                    .Append(result.WarmupDeltaFlushes).Append(',')
                    .Append(result.MeasuredDeltaFlushes).Append(',')
                    .Append(result.TotalHashChecks).Append(',')
                    .Append(result.SoakSampleCount).Append(',')
                    .Append(result.TotalElapsedMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.AverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.MaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.TicksPerSecond.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.ManagedMemoryBeforeBytes).Append(',')
                    .Append(result.ManagedMemoryAfterBytes).Append(',')
                    .Append(result.ManagedMemoryDeltaBytes).Append(',')
                    .Append(result.PeakManagedMemoryBytes).Append(',')
                    .Append(result.SoakManagedMemoryDeltaBytes).Append(',')
                    .Append(result.Gen0Collections).Append(',')
                    .Append(result.Gen1Collections).Append(',')
                    .Append(result.Gen2Collections).Append(',')
                    .Append(result.TargetAverageFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.TargetMaxFrameMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.MaxManagedMemoryDeltaBytes).Append(',')
                    .Append(result.MaxGcCollections).Append(',')
                    .Append(result.MinimumEffectiveTickRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(result.AverageFrameBudgetPassed ? "true" : "false").Append(',')
                    .Append(result.MaxFrameBudgetPassed ? "true" : "false").Append(',')
                    .Append(result.ManagedMemoryBudgetPassed ? "true" : "false").Append(',')
                    .Append(result.GcBudgetPassed ? "true" : "false").Append(',')
                    .Append(result.EffectiveTickRatioBudgetPassed ? "true" : "false").Append(',')
                    .Append(result.ProductionBudgetPassed ? "true" : "false").Append(',')
                    .Append(Escape(result.BudgetSummary))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void AppendJson(StringBuilder builder, string key, string value, bool quoteValue = true, bool isLast = false)
        {
            builder.Append("  \"").Append(key).Append("\": ");
            if (quoteValue)
            {
                builder.Append('"').Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                builder.Append(value);
            }

            if (!isLast)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static string IndentJson(string json, int spaces)
        {
            string indent = new string(' ', spaces);
            return indent + json.Replace("\r\n", "\n").Replace("\n", "\n" + indent).TrimEnd(indent.ToCharArray());
        }

        public static string WriteResultFiles(string directoryPath, BehaviorTreeBenchmarkResult result, bool writeCsv, bool writeJson)
        {
            if (result == null || !result.IsValid)
            {
                return null;
            }

            Directory.CreateDirectory(directoryPath);
            string safeName = SanitizeFileName(result.BenchmarkName);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            string lastPath = null;
            if (writeCsv)
            {
                lastPath = Path.Combine(directoryPath, $"{safeName}_{stamp}.csv");
                File.WriteAllText(lastPath, ToCsv(result));
            }

            if (writeJson)
            {
                lastPath = Path.Combine(directoryPath, $"{safeName}_{stamp}.json");
                File.WriteAllText(lastPath, ToJson(result));
            }

            return lastPath;
        }

        public static string WriteBatchFiles(string directoryPath, BehaviorTreeBenchmarkBatchResult batch, bool writeCsv, bool writeJson)
        {
            if (batch == null || batch.Results == null || batch.Results.Length == 0)
            {
                return null;
            }

            Directory.CreateDirectory(directoryPath);
            string safeName = SanitizeFileName(batch.BatchName);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            string lastPath = null;
            if (writeCsv)
            {
                lastPath = Path.Combine(directoryPath, $"{safeName}_{stamp}.csv");
                File.WriteAllText(lastPath, ToCsv(batch));
            }

            if (writeJson)
            {
                lastPath = Path.Combine(directoryPath, $"{safeName}_{stamp}.json");
                File.WriteAllText(lastPath, ToJson(batch));
            }

            return lastPath;
        }

        private static string SanitizeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "behavior-tree-benchmark";
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(invalidChar, '_');
            }

            return input;
        }
    }
}
