using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;

namespace CycloneGames.BehaviorTree.Runtime.PerformanceTest
{
    public sealed class BehaviorTreeBenchmarkSession : IDisposable
    {
        private readonly BehaviorTreeBenchmarkConfig _config;
        private readonly List<RuntimeBehaviorTree> _trees;
        private readonly List<BTBlackboardDelta> _deltas;
        private readonly Stopwatch _stopwatch;

        private bool _isSetup;
        private int _frameIndex;
        private int _measuredFrameCount;
        private int _potentialTicks;
        private int _totalTicks;
        private int _totalDeltaFlushes;
        private int _warmupDeltaFlushes;
        private int _measuredDeltaFlushes;
        private int _totalHashChecks;
        private int _soakSampleCount;
        private int _peakActiveAgentsPerFrame;
        private double _activeAgentsAccumulated;
        private double _totalMeasuredMilliseconds;
        private double _maxFrameMilliseconds;
        private long _managedMemoryBeforeBytes;
        private long _managedMemoryAfterBytes;
        private long _peakManagedMemoryBytes;
        private long _soakMemoryAtStartBytes;
        private int _gen0Before;
        private int _gen1Before;
        private int _gen2Before;

        public BehaviorTreeBenchmarkSession(BehaviorTreeBenchmarkConfig config)
        {
            _config = config?.Clone() ?? new BehaviorTreeBenchmarkConfig();
            _config.Sanitize();
            _trees = new List<RuntimeBehaviorTree>(_config.AgentCount);
            _deltas = new List<BTBlackboardDelta>(_config.AgentCount);
            _stopwatch = new Stopwatch();
        }

        public BehaviorTreeBenchmarkConfig Config => _config;

        public void Setup()
        {
            if (_isSetup)
            {
                return;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _managedMemoryBeforeBytes = GC.GetTotalMemory(true);
            _peakManagedMemoryBytes = _managedMemoryBeforeBytes;
            _soakMemoryAtStartBytes = _managedMemoryBeforeBytes;
            _gen0Before = GC.CollectionCount(0);
            _gen1Before = GC.CollectionCount(1);
            _gen2Before = GC.CollectionCount(2);

            for (int agentIndex = 0; agentIndex < _config.AgentCount; agentIndex++)
            {
                var tree = CreateAgentTree(agentIndex);
                _trees.Add(tree);

                var delta = new BTBlackboardDelta(Math.Max(1, _config.TrackedKeysPerAgent));
                int trackedKeys = Math.Min(_config.TrackedKeysPerAgent,
                    _config.LeafNodesPerTree * Math.Max(1, _config.WritesPerLeafPerTick));
                int baseKey = agentIndex * 100000;
                for (int i = 0; i < trackedKeys; i++)
                {
                    delta.TrackKey(baseKey + i);
                }

                _deltas.Add(delta);
            }

            _isSetup = true;
        }

        public void RunWarmupFrame()
        {
            EnsureSetup();
            ExecuteFrame(measured: false, sampleSoakMemory: false);
        }

        public void RunMeasuredFrame()
        {
            EnsureSetup();
            ExecuteFrame(measured: true, sampleSoakMemory: false);
        }

        public void RunSoakFrame()
        {
            EnsureSetup();
            ExecuteFrame(measured: false, sampleSoakMemory: true);
        }

        public BehaviorTreeBenchmarkResult Complete()
        {
            EnsureSetup();
            _managedMemoryAfterBytes = GC.GetTotalMemory(false);
            _peakManagedMemoryBytes = Math.Max(_peakManagedMemoryBytes, _managedMemoryAfterBytes);

            return new BehaviorTreeBenchmarkResult
            {
                IsValid = true,
                BenchmarkName = _config.BenchmarkName,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                Complexity = _config.Complexity.ToString(),
                SchedulingProfile = _config.SchedulingProfile.ToString(),
                AgentCount = _config.AgentCount,
                LeafNodesPerTree = _config.LeafNodesPerTree,
                BlackboardReadsPerLeafPerTick = _config.BlackboardReadsPerLeafPerTick,
                WritesPerLeafPerTick = _config.WritesPerLeafPerTick,
                DecoratorLayersPerLeaf = _config.DecoratorLayersPerLeaf,
                SimulatedWorkIterationsPerLeaf = _config.SimulatedWorkIterationsPerLeaf,
                TrackedKeysPerAgent = _config.TrackedKeysPerAgent,
                WarmupFrames = _config.WarmupFrames,
                MeasurementFrames = _config.MeasurementFrames,
                TicksPerFrame = _config.TicksPerFrame,
                EnableDeltaFlush = _config.EnableDeltaFlush,
                EnableDeterministicHashCheck = _config.EnableDeterministicHashCheck,
                HashCheckIntervalFrames = _config.HashCheckIntervalFrames,
                SoakFrames = _config.SoakFrames,
                SoakSampleIntervalFrames = _config.SoakSampleIntervalFrames,
                MeasuredFrameCount = _measuredFrameCount,
                PotentialTicks = _potentialTicks,
                TotalTicks = _totalTicks,
                ExecutedTicks = _totalTicks,
                EffectiveTickRatio = _potentialTicks > 0 ? (double)_totalTicks / _potentialTicks : 0d,
                AverageActiveAgentsPerFrame = _measuredFrameCount > 0 ? _activeAgentsAccumulated / _measuredFrameCount : 0d,
                PeakActiveAgentsPerFrame = _peakActiveAgentsPerFrame,
                TotalDeltaFlushes = _totalDeltaFlushes,
                WarmupDeltaFlushes = _warmupDeltaFlushes,
                MeasuredDeltaFlushes = _measuredDeltaFlushes,
                TotalHashChecks = _totalHashChecks,
                SoakSampleCount = _soakSampleCount,
                TotalElapsedMilliseconds = _totalMeasuredMilliseconds,
                AverageFrameMilliseconds = _measuredFrameCount > 0 ? _totalMeasuredMilliseconds / _measuredFrameCount : 0d,
                MaxFrameMilliseconds = _maxFrameMilliseconds,
                TicksPerSecond = _totalMeasuredMilliseconds > 0d ? _totalTicks / (_totalMeasuredMilliseconds / 1000d) : 0d,
                ManagedMemoryBeforeBytes = _managedMemoryBeforeBytes,
                ManagedMemoryAfterBytes = _managedMemoryAfterBytes,
                ManagedMemoryDeltaBytes = _managedMemoryAfterBytes - _managedMemoryBeforeBytes,
                PeakManagedMemoryBytes = _peakManagedMemoryBytes,
                SoakManagedMemoryDeltaBytes = _peakManagedMemoryBytes - _soakMemoryAtStartBytes,
                Gen0Collections = GC.CollectionCount(0) - _gen0Before,
                Gen1Collections = GC.CollectionCount(1) - _gen1Before,
                Gen2Collections = GC.CollectionCount(2) - _gen2Before
            };
        }

        public static BehaviorTreeBenchmarkResult RunImmediate(BehaviorTreeBenchmarkConfig config)
        {
            using var session = new BehaviorTreeBenchmarkSession(config);
            session.Setup();

            for (int i = 0; i < session.Config.WarmupFrames; i++)
            {
                session.RunWarmupFrame();
            }

            for (int i = 0; i < session.Config.MeasurementFrames; i++)
            {
                session.RunMeasuredFrame();
            }

            for (int i = 0; i < session.Config.SoakFrames; i++)
            {
                session.RunSoakFrame();
            }

            return session.Complete();
        }

        public void Dispose()
        {
            for (int i = 0; i < _trees.Count; i++)
            {
                _trees[i]?.Dispose();
            }

            _trees.Clear();
            _deltas.Clear();
            _isSetup = false;
        }

        private void EnsureSetup()
        {
            if (!_isSetup)
            {
                Setup();
            }
        }

        private void ExecuteFrame(bool measured, bool sampleSoakMemory)
        {
            _stopwatch.Restart();
            int activeAgentsThisFrame = 0;

            for (int tickIndex = 0; tickIndex < _config.TicksPerFrame; tickIndex++)
            {
                for (int treeIndex = 0; treeIndex < _trees.Count; treeIndex++)
                {
                    if (!ShouldTickAgent(treeIndex, _frameIndex, tickIndex))
                    {
                        continue;
                    }

                    _trees[treeIndex].Tick();
                    activeAgentsThisFrame++;
                }

                if (_config.EnableDeltaFlush)
                {
                    for (int deltaIndex = 0; deltaIndex < _deltas.Count; deltaIndex++)
                    {
                        if (_deltas[deltaIndex].TryFlush(_trees[deltaIndex].Blackboard, out _))
                        {
                            _totalDeltaFlushes++;
                            if (measured)
                            {
                                _measuredDeltaFlushes++;
                            }
                            else
                            {
                                _warmupDeltaFlushes++;
                            }
                        }
                    }
                }
            }

            if (_config.EnableDeterministicHashCheck && ShouldRunHashCheck(_frameIndex))
            {
                RunHashChecks();
            }

            _stopwatch.Stop();

            if (measured)
            {
                _measuredFrameCount++;
                _potentialTicks += _trees.Count * _config.TicksPerFrame;
                _totalTicks += activeAgentsThisFrame;
                _activeAgentsAccumulated += activeAgentsThisFrame;
                if (activeAgentsThisFrame > _peakActiveAgentsPerFrame)
                {
                    _peakActiveAgentsPerFrame = activeAgentsThisFrame;
                }

                double frameMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;
                _totalMeasuredMilliseconds += frameMilliseconds;
                if (frameMilliseconds > _maxFrameMilliseconds)
                {
                    _maxFrameMilliseconds = frameMilliseconds;
                }
            }

            if (sampleSoakMemory && ShouldSampleSoakMemory(_frameIndex))
            {
                _soakSampleCount++;
                long managedMemory = GC.GetTotalMemory(false);
                if (managedMemory > _peakManagedMemoryBytes)
                {
                    _peakManagedMemoryBytes = managedMemory;
                }
            }

            _frameIndex++;
        }

        private bool ShouldTickAgent(int agentIndex, int frameIndex, int tickIndex)
        {
            return _config.SchedulingProfile switch
            {
                BehaviorTreeBenchmarkSchedulingProfile.LodCrowd => ShouldTickLodCrowd(agentIndex, frameIndex, tickIndex),
                BehaviorTreeBenchmarkSchedulingProfile.FarCrowd => ShouldTickFarCrowd(agentIndex, frameIndex, tickIndex),
                BehaviorTreeBenchmarkSchedulingProfile.PriorityLod => ShouldTickPriorityLod(agentIndex, frameIndex, tickIndex),
                BehaviorTreeBenchmarkSchedulingProfile.UltraLod => ShouldTickUltraLod(agentIndex, frameIndex, tickIndex),
                BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged => ShouldTickPriorityManaged(agentIndex, frameIndex, tickIndex),
                BehaviorTreeBenchmarkSchedulingProfile.NetworkMixed => ShouldTickNetworkMixed(agentIndex, frameIndex, tickIndex),
                _ => true
            };
        }

        private bool ShouldTickLodCrowd(int agentIndex, int frameIndex, int tickIndex)
        {
            double normalized = (double)agentIndex / Math.Max(1, _config.AgentCount);
            if (normalized < 0.2d)
            {
                return true;
            }

            if (normalized < 0.5d)
            {
                return (frameIndex + tickIndex + agentIndex) % 2 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 5 == 0;
        }

        private bool ShouldTickFarCrowd(int agentIndex, int frameIndex, int tickIndex)
        {
            double normalized = (double)agentIndex / Math.Max(1, _config.AgentCount);
            if (normalized < 0.1d)
            {
                return true;
            }

            if (normalized < 0.3d)
            {
                return (frameIndex + tickIndex + agentIndex) % 4 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 12 == 0;
        }

        private bool ShouldTickPriorityLod(int agentIndex, int frameIndex, int tickIndex)
        {
            double normalized = (double)agentIndex / Math.Max(1, _config.AgentCount);
            if (normalized < 0.1d)
            {
                return true;
            }

            if (normalized < 0.3d)
            {
                return true;
            }

            if (normalized < 0.6d)
            {
                return (frameIndex + tickIndex + agentIndex) % 2 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 6 == 0;
        }

        private bool ShouldTickUltraLod(int agentIndex, int frameIndex, int tickIndex)
        {
            double normalized = (double)agentIndex / Math.Max(1, _config.AgentCount);
            if (normalized < 0.05d)
            {
                return true;
            }

            if (normalized < 0.15d)
            {
                return (frameIndex + tickIndex + agentIndex) % 4 == 0;
            }

            if (normalized < 0.4d)
            {
                return (frameIndex + tickIndex + agentIndex) % 8 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 16 == 0;
        }

        private bool ShouldTickPriorityManaged(int agentIndex, int frameIndex, int tickIndex)
        {
            double normalized = (double)agentIndex / Math.Max(1, _config.AgentCount);
            if (normalized < 0.1d)
            {
                return true;
            }

            if (normalized < 0.3d)
            {
                return (frameIndex + tickIndex + agentIndex) % 2 == 0;
            }

            if (normalized < 0.6d)
            {
                return (frameIndex + tickIndex + agentIndex) % 4 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 8 == 0;
        }

        private bool ShouldTickNetworkMixed(int agentIndex, int frameIndex, int tickIndex)
        {
            int playerCount = Math.Min(100, Math.Max(1, _config.AgentCount / 6));
            if (agentIndex < playerCount)
            {
                return true;
            }

            int aiIndex = agentIndex - playerCount;
            int aiCount = Math.Max(1, _config.AgentCount - playerCount);
            double normalized = (double)aiIndex / aiCount;
            if (normalized < 0.3d)
            {
                return true;
            }

            if (normalized < 0.65d)
            {
                return (frameIndex + tickIndex + agentIndex) % 2 == 0;
            }

            return (frameIndex + tickIndex + agentIndex) % 4 == 0;
        }

        private bool ShouldRunHashCheck(int frameIndex)
        {
            return frameIndex >= _config.WarmupFrames &&
                   ((frameIndex - _config.WarmupFrames) % _config.HashCheckIntervalFrames == 0);
        }

        private bool ShouldSampleSoakMemory(int frameIndex)
        {
            int soakStartFrame = _config.WarmupFrames + _config.MeasurementFrames;
            return frameIndex >= soakStartFrame &&
                   ((frameIndex - soakStartFrame) % _config.SoakSampleIntervalFrames == 0);
        }

        private void RunHashChecks()
        {
            for (int i = 0; i < _trees.Count; i++)
            {
                _trees[i].Blackboard.ComputeHash();
                _totalHashChecks++;
            }
        }

        private RuntimeBehaviorTree CreateAgentTree(int agentIndex)
        {
            var leaves = new List<RuntimeNode>(_config.LeafNodesPerTree);
            int baseKey = agentIndex * 100000;

            for (int leafIndex = 0; leafIndex < _config.LeafNodesPerTree; leafIndex++)
            {
                RuntimeNode node = new RuntimeBenchmarkLeafNode
                {
                    BaseKey = baseKey + (leafIndex * Math.Max(1, _config.WritesPerLeafPerTick)),
                    ReadCount = _config.BlackboardReadsPerLeafPerTick,
                    WriteCount = _config.WritesPerLeafPerTick,
                    SimulatedWorkIterations = _config.SimulatedWorkIterationsPerLeaf
                };

                for (int decoratorIndex = 0; decoratorIndex < _config.DecoratorLayersPerLeaf; decoratorIndex++)
                {
                    node = new RuntimeBenchmarkDecoratorNode
                    {
                        Child = node,
                        DecoratorKey = baseKey + 50000 + (leafIndex * 8) + decoratorIndex
                    };
                }

                leaves.Add(node);
            }

            RuntimeNode rootChild = BuildBalancedParallelTree(leaves);
            var root = new RuntimeRootNode { Child = rootChild };
            root.OnAwake();

            return new RuntimeBehaviorTree(root, new RuntimeBlackboard(), new RuntimeBTContext());
        }

        private RuntimeNode BuildBalancedParallelTree(List<RuntimeNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return new RuntimeParallelNode { Mode = RuntimeParallelMode.Default };
            }

            if (nodes.Count == 1)
            {
                return nodes[0];
            }

            int groupSize = _config.Complexity switch
            {
                BehaviorTreeBenchmarkComplexity.Light => 6,
                BehaviorTreeBenchmarkComplexity.Heavy => 2,
                _ => 4
            };

            var currentLevel = new List<RuntimeNode>(nodes);
            while (currentLevel.Count > 1)
            {
                var nextLevel = new List<RuntimeNode>((currentLevel.Count + groupSize - 1) / groupSize);
                for (int i = 0; i < currentLevel.Count; i += groupSize)
                {
                    var parallel = new RuntimeParallelNode
                    {
                        Mode = RuntimeParallelMode.Default
                    };

                    int end = Math.Min(i + groupSize, currentLevel.Count);
                    for (int j = i; j < end; j++)
                    {
                        parallel.AddChild(currentLevel[j]);
                    }

                    parallel.Seal();
                    nextLevel.Add(parallel);
                }

                currentLevel = nextLevel;
            }

            return currentLevel[0];
        }

        private sealed class RuntimeBenchmarkLeafNode : RuntimeNode
        {
            public int BaseKey { get; set; }
            public int ReadCount { get; set; }
            public int WriteCount { get; set; }
            public int SimulatedWorkIterations { get; set; }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                int accumulator = 0;

                for (int i = 0; i < ReadCount; i++)
                {
                    accumulator += blackboard.GetInt(BaseKey + i);
                }

                for (int i = 0; i < SimulatedWorkIterations; i++)
                {
                    accumulator = (accumulator * 1664525 + 1013904223) & 0x7fffffff;
                }

                for (int i = 0; i < WriteCount; i++)
                {
                    int key = BaseKey + i;
                    int value = blackboard.GetInt(key);
                    blackboard.SetInt(key, value + 1 + (accumulator & 1));
                }

                return RuntimeState.Running;
            }
        }

        private sealed class RuntimeBenchmarkDecoratorNode : RuntimeNode
        {
            public RuntimeNode Child { get; set; }
            public int DecoratorKey { get; set; }

            public override void OnAwake()
            {
                Child?.OnAwake();
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                blackboard.SetInt(DecoratorKey, blackboard.GetInt(DecoratorKey) + 1);
                return Child != null ? Child.Run(blackboard) : RuntimeState.Success;
            }

            protected override void OnStop(RuntimeBlackboard blackboard)
            {
                if (Child != null && Child.IsStarted)
                {
                    Child.Abort(blackboard);
                }
            }
        }
    }
}
