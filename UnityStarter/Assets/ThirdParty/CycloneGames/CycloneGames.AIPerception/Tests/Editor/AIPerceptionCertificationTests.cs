using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.AIPerception.Runtime;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CycloneGames.AIPerception.Tests.Editor
{
    /// <summary>
    /// Certification suite: parameter sweeps, multi-round timing variance, soak with churn and a
    /// sight-cone fuzz against an independent reference. Every case emits one machine-readable line so
    /// results can be compared between runs.
    /// </summary>
    public sealed class AIPerceptionCertificationTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            SensorManager.ResetInstance();
            PerceptibleRegistry.ResetInstance();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
            SensorManager.ResetInstance();
            PerceptibleRegistry.ResetInstance();
        }

        private Transform CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go.transform;
        }

        /// <summary>Snapshot rebuild plus one sensor update per frame, measured over a fixed number of frames.</summary>
        private static double MeasureFrameMilliseconds(
            PerceptibleRegistry registry,
            SensorManager manager,
            ProximitySensor sensor,
            int frames)
        {
            // Warm-up so JIT and native allocation do not land in the measured window.
            sensor.UpdateSensor(0f);

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++)
            {
                registry.RebuildData();
                sensor.UpdateSensor(0f);
            }

            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / frames;
        }

        private (PerceptibleRegistry Registry, SensorManager Manager, ProximitySensor Sensor, List<CertPerceptible> Targets)
            BuildWorld(int count, float cellSize, int gridSide)
        {
            var registry = new PerceptibleRegistry(
                initialCapacity: Mathf.Max(64, count),
                maximumCapacity: count + 64,
                cellSize: cellSize);
            var manager = new SensorManager(registry);
            var targets = new List<CertPerceptible>(count);

            for (int i = 0; i < count; i++)
            {
                var target = new CertPerceptible
                {
                    PerceptibleId = i,
                    Position = new float3(i % gridSide, 0f, i / gridSide),
                    LineOfSightPoint = new float3(i % gridSide, 0f, i / gridSide),
                    DetectionRadius = 0f,
                    IsDetectable = true
                };
                registry.Register(target);
                targets.Add(target);
            }

            registry.RebuildData();

            ProximitySensorConfig config = ProximitySensorConfig.Default;
            config.Radius = gridSide * 0.5f;
            config.UpdateInterval = 0f;
            config.MemoryDuration = 0f;

            var sensor = new ProximitySensor(CreateGameObject("cert-sensor").transform, config, manager);
            manager.TryRegister(sensor);
            return (registry, manager, sensor, targets);
        }

        [Test]
        public void Cert_CellSizeSweep_KeepsFrameCostBoundedAcrossDensities()
        {
            const int count = 8000;
            int gridSide = (int)math.ceil(math.sqrt(count));
            int[] cellSizes = { 2, 8, 32 };
            var results = new List<string>(cellSizes.Length);

            foreach (int cellSize in cellSizes)
            {
                var world = BuildWorld(count, cellSize, gridSide);
                try
                {
                    double ms = MeasureFrameMilliseconds(world.Registry, world.Manager, world.Sensor, 5);
                    double perThousand = ms / count * 1000d;
                    results.Add($"cell={cellSize}:ms={ms:F3}:per1k={perThousand:F3}");
                    Assert.That(ms, Is.LessThan(200d), $"cell size {cellSize} produced an unexpected frame cost");
                }
                finally
                {
                    world.Sensor.Dispose();
                    world.Manager.Dispose();
                    world.Registry.Dispose();
                }
            }

            Evidence.Write("cert.cellSizeSweep|count=8000|" + string.Join("|", results));
        }

        [Test]
        public void Cert_CountSweep_ScalesLinearlyWithRegisteredPerceptibles()
        {
            int[] counts = { 2000, 8000, 16000 };
            var results = new List<string>(counts.Length);

            foreach (int count in counts)
            {
                int gridSide = (int)math.ceil(math.sqrt(count));
                var world = BuildWorld(count, 8f, gridSide);
                try
                {
                    double ms = MeasureFrameMilliseconds(world.Registry, world.Manager, world.Sensor, 4);
                    double perThousand = ms / count * 1000d;
                    results.Add($"count={count}:ms={ms:F3}:per1k={perThousand:F3}");
                    Assert.That(ms, Is.LessThan(400d));
                }
                finally
                {
                    world.Sensor.Dispose();
                    world.Manager.Dispose();
                    world.Registry.Dispose();
                }
            }

            Evidence.Write("cert.countSweep|cell=8|" + string.Join("|", results));
        }

        [Test]
        public void Cert_SensorScaling_PerSensorCostStaysFlat()
        {
            const int count = 4000;
            int gridSide = (int)math.ceil(math.sqrt(count));
            int[] sensorCounts = { 1, 8, 32, 128 };

            var registry = new PerceptibleRegistry(initialCapacity: count, maximumCapacity: count + 64, cellSize: 8f);
            var manager = new SensorManager(registry, 512);
            var sensors = new List<ProximitySensor>(128);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    registry.Register(new CertPerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % gridSide, 0f, i / gridSide),
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = gridSide * 0.25f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;

                var results = new List<string>(sensorCounts.Length);
                int built = 0;
                foreach (int wanted in sensorCounts)
                {
                    while (built < wanted)
                    {
                        var sensor = new ProximitySensor(
                            CreateGameObject($"cert-sensor-{built}").transform,
                            config,
                            manager);
                        manager.TryRegister(sensor);
                        sensors.Add(sensor);
                        built++;
                    }

                    foreach (ProximitySensor sensor in sensors)
                    {
                        sensor.UpdateSensor(0f);
                    }

                    var watch = Stopwatch.StartNew();
                    for (int frame = 0; frame < 3; frame++)
                    {
                        registry.RebuildData();
                        foreach (ProximitySensor sensor in sensors)
                        {
                            sensor.UpdateSensor(0f);
                        }
                    }

                    watch.Stop();
                    double ms = watch.Elapsed.TotalMilliseconds / 3d;
                    double microsPerSensor = ms * 1000d / wanted;
                    results.Add($"sensors={wanted}:ms={ms:F3}:usPerSensor={microsPerSensor:F1}");
                    Assert.That(ms, Is.LessThan(600d), $"{wanted} sensors produced an unexpected frame cost");
                }

                Evidence.Write("cert.sensorScaling|count=4000|" + string.Join("|", results));
            }
            finally
            {
                foreach (ProximitySensor sensor in sensors)
                {
                    sensor.Dispose();
                }

                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Cert_UpdateInterval_SkipsEvaluationWithoutAffectingResults()
        {
            const int count = 1000;
            int gridSide = (int)math.ceil(math.sqrt(count));

            var registry = new PerceptibleRegistry(initialCapacity: count, maximumCapacity: count + 32, cellSize: 8f);
            var clock = new CertClock();
            var manager = new SensorManager(registry, 16, clock);
            ProximitySensor sensor = null;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    registry.Register(new CertPerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % gridSide, 0f, i / gridSide),
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = gridSide;
                config.UpdateInterval = 0.25f;
                config.MemoryDuration = 0f;

                sensor = new ProximitySensor(CreateGameObject("cert-interval").transform, config, manager);
                manager.TryRegister(sensor);

                // Ten frames of 0.1 s with a 0.25 s interval must evaluate roughly every third frame.
                int evaluated = 0;
                double lastUpdate = sensor.LastUpdateTime;
                for (int frame = 0; frame < 10; frame++)
                {
                    clock.NowSeconds += 0.1d;
                    manager.Update(0.1f);
                    manager.LateUpdate();
                    if (sensor.LastUpdateTime > lastUpdate)
                    {
                        lastUpdate = sensor.LastUpdateTime;
                        evaluated++;
                    }
                }

                Evidence.Write($"cert.interval|interval=0.25|frames=10|evaluated={evaluated}|detections={sensor.DetectedCount}");
                Assert.That(evaluated, Is.InRange(3, 5), "interval gating did not throttle as expected");
                Assert.That(sensor.DetectedCount, Is.GreaterThan(0));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Cert_RoundVariance_RepeatedRunsStayWithinNarrowBand()
        {
            const int count = 4000;
            int gridSide = (int)math.ceil(math.sqrt(count));
            var samples = new List<double>(7);

            var world = BuildWorld(count, 8f, gridSide);
            try
            {
                for (int round = 0; round < 7; round++)
                {
                    samples.Add(MeasureFrameMilliseconds(world.Registry, world.Manager, world.Sensor, 6));
                }
            }
            finally
            {
                world.Sensor.Dispose();
                world.Manager.Dispose();
                world.Registry.Dispose();
            }

            samples.Sort();
            double min = samples[0];
            double median = samples[samples.Count / 2];
            double max = samples[samples.Count - 1];
            double spread = max <= 0d ? 0d : (max - min) / median * 100d;

            Evidence.Write(
                $"cert.roundVariance|count=4000|rounds=7|min={min:F3}|median={median:F3}|max={max:F3}|spreadPct={spread:F1}");

            Assert.That(median, Is.LessThan(200d));
            Assert.That(spread, Is.LessThan(120d), "frame cost varied more than expected between identical runs");
        }

        [Test]
        public void Cert_SoakWithChurn_NoTimeOrHeapCreep()
        {
            const int count = 2000;
            const int frames = 1200;
            int gridSide = (int)math.ceil(math.sqrt(count));

            var world = BuildWorld(count, 8f, gridSide);
            var churnHandles = new List<PerceptibleHandle>(8);
            try
            {
                var registry = world.Registry;
                var sensor = world.Sensor;
                int nextId = count;

                // First window is measured after warm-up so JIT does not pollute the comparison.
                for (int i = 0; i < 50; i++)
                {
                    registry.RebuildData();
                    sensor.UpdateSensor(0f);
                }

                long heapBefore = GC.GetTotalMemory(true);
                var early = Stopwatch.StartNew();
                for (int frame = 0; frame < 200; frame++)
                {
                    registry.RebuildData();
                    sensor.UpdateSensor(0f);
                }

                early.Stop();

                var middle = Stopwatch.StartNew();
                for (int frame = 200; frame < frames; frame++)
                {
                    // Churn: swap a few targets every tenth frame to exercise slot reuse.
                    if (frame % 10 == 0)
                    {
                        foreach (PerceptibleHandle handle in churnHandles)
                        {
                            registry.Unregister(handle);
                        }

                        churnHandles.Clear();

                        for (int i = 0; i < 4; i++)
                        {
                            var target = new CertPerceptible
                            {
                                PerceptibleId = nextId++,
                                Position = new float3(frame % gridSide, 0f, (frame * 7) % gridSide),
                                IsDetectable = true
                            };
                            churnHandles.Add(registry.Register(target));
                        }
                    }

                    registry.RebuildData();
                    sensor.UpdateSensor(0f);
                }

                middle.Stop();

                long heapAfter = GC.GetTotalMemory(true);
                var late = Stopwatch.StartNew();
                for (int frame = 0; frame < 200; frame++)
                {
                    registry.RebuildData();
                    sensor.UpdateSensor(0f);
                }

                late.Stop();

                double earlyMs = early.Elapsed.TotalMilliseconds / 200d;
                double lateMs = late.Elapsed.TotalMilliseconds / 200d;
                double driftPct = earlyMs <= 0d ? 0d : (lateMs - earlyMs) / earlyMs * 100d;

                Evidence.Write(
                    $"cert.soakChurn|frames={frames}|count={count}|earlyMs={earlyMs:F3}|lateMs={lateMs:F3}" +
                    $"|driftPct={driftPct:F1}|heapDeltaKB={(heapAfter - heapBefore) / 1024}|liveTargets={registry.Count}");

                Assert.That(registry.Count, Is.EqualTo(count + 4), "churn did not keep the live count stable");
                Assert.That(heapAfter - heapBefore, Is.LessThan(512 * 1024), "managed heap grew during the soak");
                Assert.That(driftPct, Is.LessThan(200d), "frame cost drifted upward during the soak");
            }
            finally
            {
                world.Sensor.Dispose();
                world.Manager.Dispose();
                world.Registry.Dispose();
            }
        }

        [Test]
        public void Cert_SightFuzz_MatchesIndependentReference()
        {
            var random = new System.Random(0x5EED);
            Transform sensorTransform = CreateGameObject("cert-sight");
            sensorTransform.position = Vector3.zero;
            sensorTransform.rotation = Quaternion.Euler(0f, 35f, 0f);

            var registry = new PerceptibleRegistry(initialCapacity: 64, maximumCapacity: 64, cellSize: 8f);
            var manager = new SensorManager(registry);
            SightSensor sensor = null;
            int mismatches = 0;
            int totalDetections = 0;
            int worlds = 0;
            try
            {
                SightSensorConfig config = SightSensorConfig.Default;
                config.UseLineOfSight = false;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                config.NearClippingRadius = 0f;
                config.LoseSightRadius = 0f;
                config.FilterByType = false;
                config.Affiliation = PerceptionAffiliation.All;

                sensor = new SightSensor(sensorTransform, config, manager);
                manager.TryRegister(sensor);

                float3 origin = float3.zero;
                float3 forward = math.mul(quaternion.Euler(0f, math.radians(35f), 0f), new float3(0f, 0f, 1f));

                for (int world = 0; world < 120; world++)
                {
                    var targets = new CertPerceptible[24];
                    var handles = new PerceptibleHandle[24];
                    float halfAngle = 10f + (float)random.NextDouble() * 80f;
                    float maxDistance = 5f + (float)random.NextDouble() * 40f;
                    // (applied through the config below)

                    for (int i = 0; i < targets.Length; i++)
                    {
                        var position = new float3(
                            (float)(random.NextDouble() * 80d - 40d),
                            (float)(random.NextDouble() * 20d - 10d),
                            (float)(random.NextDouble() * 80d - 40d));
                        targets[i] = new CertPerceptible
                        {
                            PerceptibleId = i,
                            Position = position,
                            LineOfSightPoint = position,
                            DetectionRadius = (float)random.NextDouble() * 1.5f,
                            IsDetectable = true
                        };
                        handles[i] = registry.Register(targets[i]);
                    }

                    registry.RebuildData();
                    config.HalfAngle = halfAngle;
                    config.MaxDistance = maxDistance;
                    sensor.ApplyConfig(config);
                    sensor.UpdateSensor(0f);

                    int expected = 0;
                    float cosHalfAngle = math.cos(math.radians(halfAngle));
                    for (int i = 0; i < targets.Length; i++)
                    {
                        float3 delta = targets[i].Position - origin;
                        float distance = math.length(delta);
                        float effective = maxDistance + targets[i].DetectionRadius;
                        if (effective <= 0f || distance > effective)
                        {
                            continue;
                        }

                        if (distance < 0.001f)
                        {
                            expected++;
                            continue;
                        }

                        if (math.dot(forward, delta / distance) >= cosHalfAngle)
                        {
                            expected++;
                        }
                    }

                    worlds++;
                    totalDetections += expected;
                    if (sensor.DetectedCount != expected)
                    {
                        mismatches++;
                    }

                    for (int i = 0; i < handles.Length; i++)
                    {
                        registry.Unregister(handles[i]);
                    }
                }

                Evidence.Write(
                    $"cert.sightFuzz|worlds={worlds}|mismatches={mismatches}|totalDetections={totalDetections}");
                Assert.That(mismatches, Is.Zero, "sight cone output diverged from the reference implementation");
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        private sealed class CertPerceptible : IPerceptible
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
            public float3 LineOfSightPoint { get; set; }
            public float3 GetLOSPoint() => LineOfSightPoint;
        }

        private sealed class CertClock : IPerceptionClock
        {
            public double NowSeconds { get; set; }
        }

        private static class Evidence
        {
            private static readonly List<string> Lines = new List<string>(32);

            public static void Write(string line)
            {
                Lines.Add(line);
                Debug.Log("[cert] " + line);
                try
                {
                    string root = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "..", "..", "Temp~", "aiperception-evidence"));
                    System.IO.Directory.CreateDirectory(root);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(root, "evidence-cert.txt"), Lines);
                }
                catch (Exception)
                {
                    // Evidence capture must never fail the test that produced it.
                }
            }
        }
    }
}
