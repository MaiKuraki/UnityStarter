using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CycloneGames.AIPerception.Runtime;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace CycloneGames.AIPerception.Tests.Editor
{
    /// <summary>
    /// Parameterized stress driver for editor-side measurement through the Unity MCP bridge.
    /// Every method builds an isolated registry/manager pair, runs a warm-up, then measures a fixed
    /// number of synchronous frames and returns one line of key=value pairs.
    /// </summary>
    public static class PerceptionStressHarness
    {
        private sealed class Target : IPerceptible, IPerceptionTeamProvider
        {
            public int PerceptibleId { get; set; }
            public int PerceptibleTypeId { get; set; }
            public int TeamId { get; set; }
            public bool IsDetectable { get; set; }
            public float3 Position { get; set; }
            public float DetectionRadius { get; set; }
            public float Loudness { get; set; }
            public bool IsSoundSource { get; set; }
            public string Tag => string.Empty;
            public float3 GetLOSPoint() => Position;
        }

        /// <summary>
        /// Registers <paramref name="perceptibleCount"/> targets and <paramref name="sensorCount"/>
        /// proximity sensors, then measures complete manager frames.
        /// </summary>
        public static string Scale(
            int perceptibleCount,
            int sensorCount,
            int frames,
            float cellSize,
            float radius,
            bool moveTargets,
            int warmupFrames,
            int inlineThreshold = -1)
        {
            if (perceptibleCount <= 0 || sensorCount <= 0 || frames <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frames));
            }

            var registry = new PerceptibleRegistry(
                initialCapacity: math.min(perceptibleCount, 4096),
                maximumCapacity: PerceptibleRegistry.HardMaximumPerceptibleCount,
                cellSize: cellSize);
            var manager = new SensorManager(registry, math.min(65536, sensorCount + 8));
            if (inlineThreshold >= 0)
            {
                manager.InlineExecutionThreshold = inlineThreshold;
            }

            var targets = new Target[perceptibleCount];
            var sensorObjects = new GameObject[sensorCount];
            var sensors = new ProximitySensor[sensorCount];

            try
            {
                int side = (int)math.ceil(math.sqrt(perceptibleCount));
                for (int i = 0; i < perceptibleCount; i++)
                {
                    var target = new Target
                    {
                        PerceptibleId = i,
                        Position = new float3((i % side) * 2f, 0f, (i / side) * 2f),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    targets[i] = target;
                    registry.Register(target);
                }

                var sensorTransform = new GameObject("stress-sensor").transform;
                sensorObjects[0] = sensorTransform.gameObject;
                for (int i = 0; i < sensorCount; i++)
                {
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = radius;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 0f;
                    config.Capacity = new PerceptionSensorCapacity
                    {
                        InitialCandidateCapacity = 32,
                        MaximumCandidates = 16384,
                        InitialResultCapacity = 32,
                        MaximumResults = 4096,
                        InitialMemoryCapacity = 8,
                        MaximumMemoryEntries = 256
                    };
                    var sensor = new ProximitySensor(sensorTransform, config, manager);
                    manager.TryRegister(sensor);
                    sensors[i] = sensor;
                }

                for (int i = 0; i < warmupFrames; i++)
                {
                    Step(manager, targets, moveTargets, i);
                }

                long heapBefore = GC.GetTotalMemory(true);
                long gcBytes = 0;
                long gcPeak = 0;
                int detections = 0;

                var watch = Stopwatch.StartNew();
                using (var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    for (int i = 0; i < frames; i++)
                    {
                        Step(manager, targets, moveTargets, i);
                        long allocated = recorder.Valid ? recorder.LastValue : 0L;
                        gcBytes += allocated;
                        gcPeak = math.max(gcPeak, allocated);
                        detections = sensors[0].DetectedCount;
                    }
                }

                watch.Stop();
                long heapAfter = GC.GetTotalMemory(true);

                var builder = new StringBuilder(256);
                builder.Append("scale")
                    .Append("|n=").Append(perceptibleCount)
                    .Append("|sensors=").Append(sensorCount)
                    .Append("|frames=").Append(frames)
                    .Append("|cell=").Append(F(cellSize))
                    .Append("|radius=").Append(F(radius))
                    .Append("|move=").Append(moveTargets)
                    .Append("|inline=").Append(manager.InlineExecutionThreshold)
                    .Append("|msPerFrame=").Append(F(watch.Elapsed.TotalMilliseconds / frames))
                    .Append("|usPerSensor=").Append(F(watch.Elapsed.TotalMilliseconds * 1000d / frames / sensorCount))
                    .Append("|gcBytes=").Append(gcBytes)
                    .Append("|gcPeakPerFrame=").Append(gcPeak)
                    .Append("|heapDelta=").Append(heapAfter - heapBefore)
                    .Append("|detections=").Append(detections)
                    .Append("|peakCandidate=").Append(manager.GetMemoryStats().PeakCandidateCount);
                return builder.ToString();
            }
            finally
            {
                for (int i = 0; i < sensorCount; i++)
                {
                    sensors[i]?.Dispose();
                }

                manager.Dispose();
                registry.Dispose();
                if (sensorObjects[0] != null)
                {
                    UnityEngine.Object.DestroyImmediate(sensorObjects[0]);
                }
            }
        }

        /// <summary>
        /// Splits one frame into rebuild, broadphase and remainder. The broadphase figure comes from
        /// calling <see cref="SpatialGrid.CollectIndices"/> directly with the same origin, radius and
        /// candidate capacity the sensors use, so it can be diffed against the whole-frame number.
        /// </summary>
        public static string Decompose(int perceptibleCount, int sensorCount, int frames, float cellSize, float radius, float originZ)
        {
            var registry = new PerceptibleRegistry(
                initialCapacity: math.min(perceptibleCount, 4096),
                maximumCapacity: PerceptibleRegistry.HardMaximumPerceptibleCount,
                cellSize: cellSize);
            var manager = new SensorManager(registry, math.min(65536, sensorCount + 8));
            var targets = new Target[perceptibleCount];
            var sensorObject = new GameObject("decompose-sensor");
            var sensors = new ProximitySensor[sensorCount];
            var candidates = new Unity.Collections.NativeList<int>(256, Unity.Collections.Allocator.Persistent);

            try
            {
                int side = (int)math.ceil(math.sqrt((float)perceptibleCount));
                for (int i = 0; i < perceptibleCount; i++)
                {
                    var target = new Target
                    {
                        PerceptibleId = i,
                        Position = new float3((i % side) * 2f, 0f, (i / side) * 2f),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    targets[i] = target;
                    registry.Register(target);
                }

                for (int i = 0; i < sensorCount; i++)
                {
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = radius;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 0f;
                    config.Capacity = new PerceptionSensorCapacity
                    {
                        InitialCandidateCapacity = 32,
                        MaximumCandidates = 16384,
                        InitialResultCapacity = 32,
                        MaximumResults = 4096,
                        InitialMemoryCapacity = 8,
                        MaximumMemoryEntries = 256
                    };
                    sensors[i] = new ProximitySensor(sensorObject.transform, config, manager);
                    manager.TryRegister(sensors[i]);
                }

                for (int i = 0; i < 4; i++)
                {
                    manager.Update(0.016f);
                    manager.LateUpdate();
                }

                // Whole frame.
                var watch = Stopwatch.StartNew();
                for (int f = 0; f < frames; f++)
                {
                    manager.Update(0.016f);
                    manager.LateUpdate();
                }

                watch.Stop();
                double frameMs = watch.Elapsed.TotalMilliseconds / frames;

                // Rebuild only (no sensors).
                for (int i = 0; i < sensorCount; i++)
                {
                    manager.Unregister(sensors[i]);
                }

                watch.Restart();
                for (int f = 0; f < frames; f++)
                {
                    registry.RebuildData();
                }

                watch.Stop();
                double rebuildMs = watch.Elapsed.TotalMilliseconds / frames;

                // Broadphase only.
                using (var native = registry.CreateNativeDataCopy(Unity.Collections.Allocator.Temp))
                {
                    var data = new PerceptibleData[native.Length];
                    native.CopyTo(data);
                    var grid = new SpatialGrid(cellSize);
                    grid.Rebuild(data, data.Length);
                    var origin = new float3(0f, 0f, originZ);

                    grid.CollectIndices(data, data.Length, origin, radius, ref candidates, 4096);
                    watch.Restart();
                    for (int f = 0; f < frames; f++)
                    {
                        for (int s = 0; s < sensorCount; s++)
                        {
                            grid.CollectIndices(data, data.Length, origin, radius, ref candidates, 4096);
                        }
                    }

                    watch.Stop();
                    double broadphaseMs = watch.Elapsed.TotalMilliseconds / frames;
                    double remainderMs = frameMs - rebuildMs - broadphaseMs;

                    var builder = new StringBuilder(256);
                    builder.Append("decompose")
                        .Append("|n=").Append(perceptibleCount)
                        .Append("|sensors=").Append(sensorCount)
                        .Append("|cell=").Append(F(cellSize))
                        .Append("|radius=").Append(F(radius))
                        .Append("|frameMs=").Append(F(frameMs))
                        .Append("|rebuildMs=").Append(F(rebuildMs))
                        .Append("|broadphaseMs=").Append(F(broadphaseMs))
                        .Append("|broadphaseShare=").Append(F(broadphaseMs / math.max(frameMs, 1e-6d)))
                        .Append("|jobAndCommitMs=").Append(F(remainderMs))
                        .Append("|usPerSensorBroadphase=").Append(F(broadphaseMs * 1000d / sensorCount))
                        .Append("|candidates=").Append(candidates.Length);
                    return builder.ToString();
                }
            }
            finally
            {
                for (int i = 0; i < sensorCount; i++)
                {
                    sensors[i]?.Dispose();
                }

                candidates.Dispose();
                manager.Dispose();
                registry.Dispose();
                UnityEngine.Object.DestroyImmediate(sensorObject);
            }
        }

        /// <summary>Bulk registration spike followed by a full drain, repeated.</summary>
        public static string Impulse(int burst, int rounds)
        {
            var registry = new PerceptibleRegistry(initialCapacity: 256, maximumCapacity: burst + 16, cellSize: 8f);
            var targets = new Target[burst];
            var handles = new PerceptibleHandle[burst];
            try
            {
                var builder = new StringBuilder(256);
                builder.Append("impulse|burst=").Append(burst);
                double totalRegister = 0d;
                double totalDrain = 0d;
                for (int round = 0; round < rounds; round++)
                {
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < burst; i++)
                    {
                        var target = new Target
                        {
                            PerceptibleId = i,
                            Position = new float3(i % 256, 0f, i / 256),
                            DetectionRadius = 0.5f,
                            IsDetectable = true
                        };
                        targets[i] = target;
                        handles[i] = registry.Register(target);
                    }

                    watch.Stop();
                    totalRegister += watch.Elapsed.TotalMilliseconds;
                    registry.RebuildData();

                    watch.Restart();
                    for (int i = 0; i < burst; i++)
                    {
                        registry.Unregister(handles[i]);
                    }

                    watch.Stop();
                    totalDrain += watch.Elapsed.TotalMilliseconds;
                }

                builder.Append("|rounds=").Append(rounds)
                    .Append("|registerMs=").Append(F(totalRegister / rounds))
                    .Append("|drainMs=").Append(F(totalDrain / rounds))
                    .Append("|count=").Append(registry.Count)
                    .Append("|rejected=").Append(registry.GetMemoryStats().RejectedRegistrationCount);
                return builder.ToString();
            }
            finally
            {
                registry.Dispose();
            }
        }

        /// <summary>Repeated frames with a fixed target population to expose creep.</summary>
        public static string Soak(int perceptibleCount, int sensorCount, int frames)
        {
            var registry = new PerceptibleRegistry(
                initialCapacity: math.min(perceptibleCount, 4096),
                maximumCapacity: PerceptibleRegistry.HardMaximumPerceptibleCount,
                cellSize: 8f);
            var manager = new SensorManager(registry, math.min(65536, sensorCount + 8));
            var targets = new Target[perceptibleCount];
            var sensorObject = new GameObject("soak-sensor");
            var sensors = new ProximitySensor[sensorCount];

            try
            {
                int side = (int)math.ceil(math.sqrt(perceptibleCount));
                for (int i = 0; i < perceptibleCount; i++)
                {
                    var target = new Target
                    {
                        PerceptibleId = i,
                        Position = new float3((i % side) * 2f, 0f, (i / side) * 2f),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    targets[i] = target;
                    registry.Register(target);
                }

                for (int i = 0; i < sensorCount; i++)
                {
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 12f;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 5f;
                    sensors[i] = new ProximitySensor(sensorObject.transform, config, manager);
                    manager.TryRegister(sensors[i]);
                }

                for (int i = 0; i < 50; i++)
                {
                    Step(manager, targets, true, i);
                }

                const int blockSize = 100;
                int blocks = frames / blockSize;
                var blockTimes = new double[blocks];
                long heapBefore = GC.GetTotalMemory(true);
                int capacityBefore = manager.GetMemoryStats().NativeBufferCapacity;
                long gcBytes = 0;

                int block = 0;
                var blockWatch = Stopwatch.StartNew();
                using (var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    for (int i = 0; i < frames; i++)
                    {
                        Step(manager, targets, true, i);
                        gcBytes += recorder.Valid ? recorder.LastValue : 0L;
                        if ((i + 1) % blockSize == 0 && block < blocks)
                        {
                            blockWatch.Stop();
                            blockTimes[block++] = blockWatch.Elapsed.TotalMilliseconds;
                            blockWatch.Restart();
                        }
                    }
                }

                long heapAfter = GC.GetTotalMemory(true);
                int capacityAfter = manager.GetMemoryStats().NativeBufferCapacity;
                double first = blockTimes[0];
                double last = blockTimes[blocks - 1];

                var builder = new StringBuilder(256);
                builder.Append("soak")
                    .Append("|n=").Append(perceptibleCount)
                    .Append("|sensors=").Append(sensorCount)
                    .Append("|frames=").Append(frames)
                    .Append("|firstBlockMs=").Append(F(first))
                    .Append("|lastBlockMs=").Append(F(last))
                    .Append("|creep=").Append(F(last / math.max(first, 1e-6d)))
                    .Append("|gcBytes=").Append(gcBytes)
                    .Append("|heapDelta=").Append(heapAfter - heapBefore)
                    .Append("|capacityBefore=").Append(capacityBefore)
                    .Append("|capacityAfter=").Append(capacityAfter);
                return builder.ToString();
            }
            finally
            {
                for (int i = 0; i < sensorCount; i++)
                {
                    sensors[i]?.Dispose();
                }

                manager.Dispose();
                registry.Dispose();
                UnityEngine.Object.DestroyImmediate(sensorObject);
            }
        }

        /// <summary>
        /// Rebuild-only cost: registration is excluded, only the snapshot publish is measured, with
        /// and without target movement.
        /// </summary>
        public static string Rebuild(int perceptibleCount, int frames, float cellSize, bool moveTargets)
        {
            var registry = new PerceptibleRegistry(
                initialCapacity: math.min(perceptibleCount, 4096),
                maximumCapacity: PerceptibleRegistry.HardMaximumPerceptibleCount,
                cellSize: cellSize);
            var targets = new Target[perceptibleCount];
            try
            {
                int side = (int)math.ceil(math.sqrt((float)perceptibleCount));
                for (int i = 0; i < perceptibleCount; i++)
                {
                    var target = new Target
                    {
                        PerceptibleId = i,
                        Position = new float3((i % side) * 2f, 0f, (i / side) * 2f),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    targets[i] = target;
                    registry.Register(target);
                }

                registry.RebuildData();
                for (int i = 0; i < 4; i++)
                {
                    registry.RebuildData();
                }

                var watch = Stopwatch.StartNew();
                for (int i = 0; i < frames; i++)
                {
                    if (moveTargets)
                    {
                        targets[i % perceptibleCount].Position += new float3(0.01f, 0f, 0f);
                    }

                    registry.RebuildData();
                }

                watch.Stop();

                var builder = new StringBuilder(160);
                builder.Append("rebuild")
                    .Append("|n=").Append(perceptibleCount)
                    .Append("|frames=").Append(frames)
                    .Append("|cell=").Append(F(cellSize))
                    .Append("|move=").Append(moveTargets)
                    .Append("|msPerRebuild=").Append(F(watch.Elapsed.TotalMilliseconds / frames))
                    .Append("|nsPerEntity=").Append(F(watch.Elapsed.TotalMilliseconds * 1_000_000d / frames / perceptibleCount));
                return builder.ToString();
            }
            finally
            {
                registry.Dispose();
            }
        }

        private static void Step(SensorManager manager, Target[] targets, bool moveTargets, int frame)
        {
            if (moveTargets)
            {
                int stride = 1 + (frame % 7);
                for (int i = frame % stride; i < targets.Length; i += stride)
                {
                    float3 position = targets[i].Position;
                    targets[i].Position = new float3(position.x, 0f, position.z + 0.05f);
                }
            }

            manager.Update(0.016f);
            manager.LateUpdate();
        }

        private static string F(double value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
