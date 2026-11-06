using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.AIPerception.Runtime;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CycloneGames.AIPerception.Tests.Editor
{
    /// <summary>
    /// Deterministic evidence suite. Cross-checks built-in sensor output against an independent
    /// reference implementation over a parameter matrix and records scaling, burst, soak, and fuzz
    /// measurements. Every evidence line is written to the Editor log and to
    /// &lt;repo-root&gt;/Temp~/aiperception-evidence/evidence.txt.
    /// </summary>
    public sealed class AIPerceptionEvidenceTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EvidenceWriter.Reset();
        }

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

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            EvidenceWriter.Flush();
        }

        // ====================================================================================
        // 0. Measurement channel probe
        // ====================================================================================

        [Test]
        public void Probe_AllocationCounters_ReportWhichChannelIsUsableInThisHost()
        {
            const int payload = 1 << 20;
            _ = GC.GetTotalMemory(false);
            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            long heapBefore = GC.GetTotalMemory(false);

            var probe = new byte[payload];
            probe[0] = 1;
            probe[payload - 1] = 2;

            long threadDelta = GC.GetAllocatedBytesForCurrentThread() - threadBefore;
            long heapDelta = GC.GetTotalMemory(false) - heapBefore;
            GC.KeepAlive(probe);

            EvidenceWriter.Write(
                $"probe|payloadBytes={payload}|threadCounterDelta={threadDelta}|heapDelta={heapDelta}|threadCounterUsable={threadDelta > 0}");
            Assert.That(probe.Length, Is.EqualTo(payload));
        }

        // ====================================================================================
        // 1. Query correctness matrix
        // ====================================================================================

        [Test]
        public void Evidence_QueryMatrix_MatchesIndependentReference()
        {
            int[] counts = { 0, 1, 7, 64, 257 };
            float[] cellSizes = { 0.25f, 4f, 20f, 1000f };
            int[] layouts = { LayoutGrid, LayoutCluster, LayoutLine, LayoutDuplicate, LayoutWide };

            var failures = new List<string>();
            int caseCount = 0;

            for (int c = 0; c < counts.Length; c++)
            {
                for (int s = 0; s < cellSizes.Length; s++)
                {
                    for (int l = 0; l < layouts.Length; l++)
                    {
                        caseCount += RunSensorCase("sight", SensorKind.Sight, counts[c], cellSizes[s], layouts[l], failures);
                        caseCount += RunSensorCase("proximity", SensorKind.Proximity, counts[c], cellSizes[s], layouts[l], failures);
                        caseCount += RunSensorCase("hearing", SensorKind.Hearing, counts[c], cellSizes[s], layouts[l], failures);
                    }
                }
            }

            EvidenceWriter.Write($"queryMatrix|cases={caseCount}|failures={failures.Count}");
            if (failures.Count > 0)
            {
                var report = new System.Text.StringBuilder();
                report.AppendLine($"Reference mismatches: {failures.Count}/{caseCount}");
                for (int i = 0; i < failures.Count && i < 20; i++)
                {
                    report.AppendLine(failures[i]);
                }

                Assert.Fail(report.ToString());
            }
        }

        private int RunSensorCase(
            string label,
            SensorKind kind,
            int count,
            float cellSize,
            int layout,
            List<string> failures)
        {
            string caseLabel = $"{label}|n={count}|cell={cellSize}|layout={layout}";
            var registry = new PerceptibleRegistry(
                initialCapacity: math.max(1, count),
                maximumCapacity: math.max(1024, count + 1),
                cellSize: cellSize);
            Transform sensorTransform = CreateGameObject("evidence-sensor").transform;
            sensorTransform.position = Vector3.zero;
            sensorTransform.rotation = Quaternion.identity;
            SensorManager manager = new SensorManager(registry);

            ISensor sensor = null;
            try
            {
                var targets = new List<EvidencePerceptible>(count);
                var slots = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    EvidencePerceptible target = CreateLayoutTarget(layout, i, count);
                    targets.Add(target);
                    PerceptibleHandle handle = registry.Register(target);
                    if (!handle.IsValid)
                    {
                        failures.Add($"{caseLabel}|registration failed at index {i}");
                        return 1;
                    }

                    slots.Add(handle.Id);
                }

                registry.RebuildData();
                int expectedDataCount = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].IsDetectable)
                    {
                        expectedDataCount++;
                    }
                }

                if (registry.GetDataCount() != expectedDataCount)
                {
                    failures.Add(
                        $"{caseLabel}|snapshotCount expected={expectedDataCount} actual={registry.GetDataCount()}");
                }

                sensor = CreateSensor(kind, sensorTransform, manager);
                sensor.UpdateSensor(0f);

                var actual = new List<ReferenceHit>();
                for (int i = 0; i < sensor.DetectedCount; i++)
                {
                    if (!sensor.TryGetResult(i, out DetectionResult result))
                    {
                        failures.Add($"{caseLabel}|TryGetResult({i}) false for count={sensor.DetectedCount}");
                        continue;
                    }

                    actual.Add(new ReferenceHit(result.Target.Id, result.Distance, result.Visibility, result.IsFromMemory));
                }

                var expected = new List<ReferenceHit>();
                BuildReference(kind, targets, slots, expected);
                expected.Sort(ReferenceHitComparer.Instance);

                CompareHits(caseLabel, expected, actual, failures);
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }

            return 1;
        }

        private static ISensor CreateSensor(SensorKind kind, Transform sensorTransform, SensorManager manager)
        {
            switch (kind)
            {
                case SensorKind.Sight:
                {
                    SightSensorConfig config = SightSensorConfig.Default;
                    config.HalfAngle = 70f;
                    config.MaxDistance = 24f;
                    config.UpdateInterval = 0f;
                    config.UseLineOfSight = false;
                    config.FilterByType = false;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    return new SightSensor(sensorTransform, config, manager);
                }

                case SensorKind.Proximity:
                {
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 12f;
                    config.UpdateInterval = 0f;
                    config.FilterByType = false;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    return new ProximitySensor(sensorTransform, config, manager);
                }

                default:
                {
                    HearingSensorConfig config = HearingSensorConfig.Default;
                    config.Radius = 12f;
                    config.UpdateInterval = 0f;
                    config.UseOcclusion = false;
                    config.FilterByType = false;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    return new HearingSensor(sensorTransform, config, manager);
                }
            }
        }

        private static PerceptionSensorCapacity SmallCapacity()
        {
            return new PerceptionSensorCapacity
            {
                InitialCandidateCapacity = 8,
                MaximumCandidates = 8192,
                InitialResultCapacity = 8,
                MaximumResults = 4096,
                InitialMemoryCapacity = 8,
                MaximumMemoryEntries = 4096
            };
        }

        private static void BuildReference(
            SensorKind kind,
            List<EvidencePerceptible> targets,
            List<int> slots,
            List<ReferenceHit> output)
        {
            float3 origin = float3.zero;
            float3 forward = new float3(0f, 0f, 1f);
            const float halfAngle = 70f;
            const float maxDistance = 24f;
            const float radius = 12f;
            float cosine = math.cos(math.radians(halfAngle));
            float denominator = 1f - cosine;

            for (int i = 0; i < targets.Count; i++)
            {
                EvidencePerceptible target = targets[i];
                if (!target.IsDetectable)
                {
                    continue;
                }

                float3 position = target.Position;
                if (!math.all(math.isfinite(position)))
                {
                    continue;
                }

                float3 offset = position - origin;
                float distanceSquared = math.lengthsq(offset);
                if (!math.isfinite(distanceSquared))
                {
                    continue;
                }

                switch (kind)
                {
                    case SensorKind.Sight:
                    {
                        float effective = maxDistance + target.DetectionRadius;
                        if (effective <= 0f || distanceSquared > effective * effective)
                        {
                            continue;
                        }

                        float visibility;
                        if (distanceSquared < 0.000001f)
                        {
                            visibility = 1f;
                        }
                        else
                        {
                            float inverse = math.rsqrt(distanceSquared);
                            float3 direction = offset * inverse;
                            float dot = math.dot(forward, direction);
                            if (dot < cosine)
                            {
                                continue;
                            }

                            float distance = math.sqrt(distanceSquared);
                            visibility = distance <= 0.0001f || denominator <= 0.000001f
                                ? 1f
                                : math.saturate((dot - cosine) / denominator);
                        }

                        output.Add(new ReferenceHit(slots[i], math.sqrt(distanceSquared), visibility, false));
                        continue;
                    }

                    case SensorKind.Proximity:
                    {
                        float effective = radius + target.DetectionRadius;
                        if (effective <= 0f || distanceSquared > effective * effective)
                        {
                            continue;
                        }

                        float distance = math.sqrt(distanceSquared);
                        float proximity = math.clamp(1f - (distance / effective), 0f, 1f);
                        if (proximity < 0.01f)
                        {
                            continue;
                        }

                        output.Add(new ReferenceHit(slots[i], distance, proximity, false));
                        continue;
                    }

                    default:
                    {
                        if (!target.IsSoundSource)
                        {
                            continue;
                        }

                        float effective = (radius * target.Loudness) + target.DetectionRadius;
                        if (effective <= 0f || distanceSquared > effective * effective)
                        {
                            continue;
                        }

                        float distance = math.sqrt(distanceSquared);
                        float audibility = math.clamp(1f - (distance / effective), 0f, 1f);
                        if (audibility < 0.01f)
                        {
                            continue;
                        }

                        output.Add(new ReferenceHit(slots[i], distance, audibility, false));
                        continue;
                    }
                }
            }
        }

        private static void CompareHits(
            string label,
            List<ReferenceHit> expected,
            List<ReferenceHit> actual,
            List<string> failures)
        {
            if (expected.Count != actual.Count)
            {
                failures.Add($"{label}|count expected={expected.Count} actual={actual.Count}");
                return;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                ReferenceHit e = expected[i];
                ReferenceHit a = actual[i];
                if (e.Slot != a.Slot)
                {
                    failures.Add($"{label}|order mismatch at {i} expectedSlot={e.Slot} actualSlot={a.Slot}");
                    return;
                }

                if (!Approximately(e.Distance, a.Distance, 1e-4f))
                {
                    failures.Add($"{label}|distance mismatch at {i} slot={e.Slot} expected={e.Distance} actual={a.Distance}");
                    return;
                }

                if (!Approximately(e.Visibility, a.Visibility, 1e-4f))
                {
                    failures.Add(
                        $"{label}|visibility mismatch at {i} slot={e.Slot} expected={e.Visibility} actual={a.Visibility}");
                    return;
                }

                if (e.IsFromMemory != a.IsFromMemory)
                {
                    failures.Add($"{label}|memory flag mismatch at {i} slot={e.Slot}");
                    return;
                }
            }
        }

        private static bool Approximately(float left, float right, float tolerance)
        {
            float difference = math.abs(left - right);
            float scale = math.max(1f, math.max(math.abs(left), math.abs(right)));
            return difference <= tolerance * scale;
        }

        // ====================================================================================
        // 2. Degenerate inputs and near-clipping contract
        // ====================================================================================

        [Test]
        public void Evidence_DegenerateInputs_AreBoundedDeterministicAndNeverThrow()
        {
            var failures = new List<string>();
            int cases = 0;

            float[] radii = { 0f, -1f, 0.5f, float.NaN, float.PositiveInfinity, 1e30f };
            float[] loudness = { 0f, -3f, 1f, float.NaN, float.PositiveInfinity, 1e30f };
            float[] positions = { 0f, 1e-6f, 1f, 1e7f, 1e18f, float.MaxValue, float.NaN, float.PositiveInfinity };

            for (int r = 0; r < radii.Length; r++)
            {
                for (int l = 0; l < loudness.Length; l++)
                {
                    for (int p = 0; p < positions.Length; p++)
                    {
                        cases++;
                        var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
                        Transform sensorTransform = CreateGameObject("degenerate-sensor").transform;
                        SensorManager manager = new SensorManager(registry);
                        SightSensor sight = null;
                        HearingSensor hearing = null;
                        try
                        {
                            registry.Register(new EvidencePerceptible
                            {
                                PerceptibleId = 1,
                                Position = new float3(positions[p], 0f, 1f),
                                LineOfSightPoint = new float3(positions[p], 0f, 1f),
                                DetectionRadius = radii[r],
                                Loudness = loudness[l],
                                IsSoundSource = true,
                                IsDetectable = true
                            });
                            registry.RebuildData();

                            SightSensorConfig sightConfig = SightSensorConfig.Default;
                            sightConfig.MaxDistance = 10f;
                            sightConfig.UpdateInterval = 0f;
                            sightConfig.UseLineOfSight = false;
                            sightConfig.MemoryDuration = 0f;
                            sightConfig.Capacity = SmallCapacity();
                            sight = new SightSensor(sensorTransform, sightConfig, manager);

                            HearingSensorConfig hearingConfig = HearingSensorConfig.Default;
                            hearingConfig.Radius = 10f;
                            hearingConfig.UpdateInterval = 0f;
                            hearingConfig.UseOcclusion = false;
                            hearingConfig.MemoryDuration = 0f;
                            hearingConfig.Capacity = SmallCapacity();
                            hearing = new HearingSensor(sensorTransform, hearingConfig, manager);

                            Assert.DoesNotThrow(() => sight.UpdateSensor(0f));
                            Assert.DoesNotThrow(() => hearing.UpdateSensor(0f));

                            if (sight.DetectedCount < 0 || sight.DetectedCount > 4)
                            {
                                failures.Add($"sight|r={radii[r]}|l={loudness[l]}|p={positions[p]}|count={sight.DetectedCount}");
                            }

                            if (hearing.DetectedCount < 0 || hearing.DetectedCount > 4)
                            {
                                failures.Add($"hearing|r={radii[r]}|l={loudness[l]}|p={positions[p]}|count={hearing.DetectedCount}");
                            }

                            for (int i = 0; i < sight.DetectedCount; i++)
                            {
                                if (!sight.TryGetResult(i, out DetectionResult result))
                                {
                                    failures.Add($"sight|result read failed at {i}");
                                    continue;
                                }

                                if (!float.IsFinite(result.Distance) || !float.IsFinite(result.Visibility))
                                {
                                    failures.Add(
                                        $"sight|nonFiniteResult|r={radii[r]}|l={loudness[l]}|p={positions[p]}|d={result.Distance}|v={result.Visibility}");
                                }
                            }
                        }
                        finally
                        {
                            sight?.Dispose();
                            hearing?.Dispose();
                            manager.Dispose();
                            registry.Dispose();
                        }
                    }
                }
            }

            EvidenceWriter.Write($"degenerate|cases={cases}|failures={failures.Count}");
            if (failures.Count > 0)
            {
                Assert.Fail(string.Join("\n", failures.GetRange(0, math.min(15, failures.Count))));
            }
        }

        [Test]
        public void Evidence_Sight_PointBlankLateralTargetIsNotDetected()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            Transform sensorTransform = CreateGameObject("point-blank-sensor").transform;
            sensorTransform.position = Vector3.zero;
            sensorTransform.rotation = Quaternion.identity;
            SensorManager manager = new SensorManager(registry);
            SightSensor sight = null;
            try
            {
                registry.Register(new EvidencePerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0.3f, 0f, 0f),
                    LineOfSightPoint = new float3(0.3f, 0f, 0f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                });
                registry.RebuildData();

                SightSensorConfig config = SightSensorConfig.Default;
                config.HalfAngle = 60f;
                config.MaxDistance = 30f;
                config.UpdateInterval = 0f;
                config.UseLineOfSight = false;
                config.MemoryDuration = 0f;
                config.Capacity = SmallCapacity();
                sight = new SightSensor(sensorTransform, config, manager);
                sight.UpdateSensor(0f);

                EvidenceWriter.Write(
                    $"pointBlank|distance=0.3|lateralOffset=true|detected={sight.HasDetection}|status={sight.LastUpdateStatus}|count={sight.DetectedCount}");

                Assert.That(
                    sight.HasDetection,
                    Is.False,
                    "Current contract: a 0.3 m lateral target outside the 60 degree cone is invisible; there is no near-clipping radius.");
            }
            finally
            {
                sight?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        // ====================================================================================
        // 3. Scaling
        // ====================================================================================

        [Test]
        public void Evidence_RegistrationScaling_StaysLinearAcrossBulkLoad()
        {
            // Pre-fix baseline for this exact shape was an empirical exponent of 1.91 with 300 ms
            // for 16k registrations, because duplicate detection scanned every active slot. The
            // registry now keys admission on object identity, so the exponent must stay near 1.
            int[] counts = { 500, 1000, 2000, 4000, 8000 };
            var measurements = new double[counts.Length];

            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                var registry = new PerceptibleRegistry(
                    initialCapacity: count,
                    maximumCapacity: count + 16,
                    cellSize: 20f);
                var targets = new EvidencePerceptible[count];
                for (int t = 0; t < count; t++)
                {
                    targets[t] = new EvidencePerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 256, 0f, t / 256),
                        LineOfSightPoint = new float3(t % 256, 0f, t / 256),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                }

                var stopwatch = Stopwatch.StartNew();
                for (int t = 0; t < count; t++)
                {
                    registry.Register(targets[t]);
                }

                stopwatch.Stop();
                double milliseconds = stopwatch.Elapsed.TotalMilliseconds;
                measurements[i] = milliseconds;
                EvidenceWriter.Write(
                    $"registration|count={count}|ms={Format(milliseconds)}|perItemNs={Format(milliseconds * 1_000_000d / count)}");
                registry.Dispose();
            }

            double exponent = math.log(measurements[measurements.Length - 1] / math.max(measurements[0], 1e-6d)) /
                              math.log((double)counts[counts.Length - 1] / counts[0]);
            EvidenceWriter.Write(
                $"registration|empiricalExponent={Format(exponent)}|linear=1.00|quadratic=2.00|baselineBeforeFix=1.91");
            Assert.That(exponent, Is.LessThan(1.35d), "Bulk registration became superlinear again.");
        }

        [Test]
        public void Evidence_SnapshotRebuildScaling()
        {
            int[] counts = { 500, 1000, 2000, 4000, 8000, 16000 };
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                var registry = new PerceptibleRegistry(
                    initialCapacity: count,
                    maximumCapacity: count + 16,
                    cellSize: 8f);
                for (int t = 0; t < count; t++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 128, 0f, t / 128),
                        LineOfSightPoint = new float3(t % 128, 0f, t / 128),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();
                const int iterations = 8;
                var stopwatch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    registry.MarkDirty();
                    registry.RebuildData();
                }

                stopwatch.Stop();
                double perRebuild = stopwatch.Elapsed.TotalMilliseconds / iterations;
                EvidenceWriter.Write(
                    $"rebuild|count={count}|msPerRebuild={Format(perRebuild)}|cells={registry.GetMemoryStats().SpatialCellCount}");
                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_AllocationTrend_WarmedUpdateLoopHeapGrowth()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 16, cellSize: 5f);
            Transform sensorTransform = CreateGameObject("alloc-trend-sensor").transform;
            SensorManager manager = new SensorManager(registry);
            ProximitySensor sensor = null;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i, 0f, 1f),
                        LineOfSightPoint = new float3(i, 0f, 1f),
                        DetectionRadius = 0.25f,
                        IsDetectable = true
                    });
                }

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 4f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 2f;
                config.Capacity = SmallCapacity();
                sensor = new ProximitySensor(sensorTransform, config, manager);
                manager.TryRegister(sensor);

                for (int warmup = 0; warmup < 500; warmup++)
                {
                    Iterate(manager, sensor);
                }

                long baseline = GC.GetTotalMemory(true);
                long peak = baseline;
                const int blockSize = 25_000;
                const int blocks = 8;
                for (int block = 0; block < blocks; block++)
                {
                    for (int iteration = 0; iteration < blockSize; iteration++)
                    {
                        Iterate(manager, sensor);
                    }

                    long sample = GC.GetTotalMemory(false);
                    peak = math.max(peak, sample);
                }

                long growth = peak - baseline;
                int totalIterations = blockSize * blocks;
                EvidenceWriter.Write(
                    $"allocationTrend|iterations={totalIterations}|baselineHeap={baseline}|peakHeap={peak}|growthBytes={growth}|bytesPerIteration={Format((double)growth / totalIterations)}|threadCounterUsable=False");

                Assert.That(
                    growth,
                    Is.LessThan(512L * 1024),
                    "A warmed update loop must not accumulate managed heap; measured growth exceeds the noise budget.");
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        private static void Iterate(SensorManager manager, ProximitySensor sensor)
        {
            manager.Update(0f);
            manager.LateUpdate();
            for (int i = 0; i < sensor.DetectedCount; i++)
            {
                sensor.TryGetResult(i, out DetectionResult result);
                if (result.Distance < 0f)
                {
                    break;
                }
            }

            _ = manager.GetMemoryStats();
        }

        [Test]
        public void Evidence_PhysicsOcclusion_ProbesHostCapabilityThenVerifiesBlocking()
        {
            var probeObject = new GameObject("occlusion-probe");
            _objects.Add(probeObject);
            probeObject.transform.position = new Vector3(0f, 0f, 1f);
            var probeCollider = probeObject.AddComponent<BoxCollider>();
            probeCollider.size = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();

            bool physicsAvailable = Physics.Raycast(
                new Vector3(0f, 0f, -1f),
                Vector3.forward,
                5f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            EvidenceWriter.Write($"physicsProbe|raycastUsable={physicsAvailable}");

            if (!physicsAvailable)
            {
                Assert.Ignore("Physics queries are unavailable in this host; occlusion contracts require an Editor or Player run.");
            }

            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            Transform sensorTransform = CreateGameObject("occlusion-sensor").transform;
            sensorTransform.position = Vector3.zero;
            sensorTransform.rotation = Quaternion.identity;
            SensorManager manager = new SensorManager(registry);
            SightSensor sight = null;
            HearingSensor hearing = null;
            try
            {
                registry.Register(new EvidencePerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 4f),
                    LineOfSightPoint = new float3(0f, 0f, 4f),
                    DetectionRadius = 0f,
                    Loudness = 1f,
                    IsSoundSource = true,
                    IsDetectable = true
                });
                registry.RebuildData();

                SightSensorConfig sightConfig = SightSensorConfig.Default;
                sightConfig.HalfAngle = 60f;
                sightConfig.MaxDistance = 30f;
                sightConfig.UpdateInterval = 0f;
                sightConfig.UseLineOfSight = true;
                sightConfig.ObstacleLayer = Physics.DefaultRaycastLayers;
                sightConfig.MemoryDuration = 0f;
                sightConfig.Capacity = SmallCapacity();
                sight = new SightSensor(sensorTransform, sightConfig, manager);
                sight.UpdateSensor(0f);

                HearingSensorConfig hearingConfig = HearingSensorConfig.Default;
                hearingConfig.Radius = 30f;
                hearingConfig.UpdateInterval = 0f;
                hearingConfig.UseOcclusion = true;
                hearingConfig.OcclusionLayer = Physics.DefaultRaycastLayers;
                hearingConfig.OcclusionAttenuation = 0.5f;
                hearingConfig.MemoryDuration = 0f;
                hearingConfig.Capacity = SmallCapacity();
                hearing = new HearingSensor(sensorTransform, hearingConfig, manager);
                hearing.UpdateSensor(0f);

                float audibility = hearing.DetectedCount > 0 && hearing.TryGetResult(0, out DetectionResult heard)
                    ? heard.Visibility
                    : 0f;

                EvidenceWriter.Write(
                    $"occlusion|sightBlocked={!sight.HasDetection}|sightStatus={sight.LastUpdateStatus}|hearingDetected={hearing.HasDetection}|hearingVisibility={Format(audibility)}");

                Assert.That(sight.HasDetection, Is.False, "A wall on the obstacle layer must block sight.");
                Assert.That(hearing.HasDetection, Is.True, "Occluded sound is attenuated, not removed.");
                Assert.That(audibility, Is.GreaterThan(0f).And.LessThan(0.75f));
            }
            finally
            {
                sight?.Dispose();
                hearing?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_RebuildDecomposition_CaptureVersusDirtyRepublish()
        {
            int[] counts = { 1000, 4000, 16000 };
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                var registry = new PerceptibleRegistry(
                    initialCapacity: count,
                    maximumCapacity: count + 16,
                    cellSize: 8f);
                for (int t = 0; t < count; t++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 128, 0f, t / 128),
                        LineOfSightPoint = new float3(t % 128, 1f, t / 128),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                const int iterations = 8;

                var captureWatch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    registry.RebuildData();
                }

                captureWatch.Stop();
                double captureMs = captureWatch.Elapsed.TotalMilliseconds / iterations;

                var dirtyWatch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    registry.MarkDirty();
                    registry.RebuildData();
                }

                dirtyWatch.Stop();
                double dirtyMs = dirtyWatch.Elapsed.TotalMilliseconds / iterations;

                double sortMs = math.max(0d, dirtyMs - captureMs);
                EvidenceWriter.Write(
                    $"rebuildSplit|count={count}|captureOnlyMs={Format(captureMs)}|dirtyRepublishMs={Format(dirtyMs)}|sortAndPublishMs={Format(sortMs)}|captureShare={Format(captureMs / math.max(dirtyMs, 1e-6d))}|sortShare={Format(sortMs / math.max(dirtyMs, 1e-6d))}|cells={registry.GetMemoryStats().SpatialCellCount}|fits60fpsDirty={dirtyMs < 16.6d}");
                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_PerSensorScheduleOverhead()
        {
            int[] sensorCounts = { 1, 10, 50, 200 };
            var registry = new PerceptibleRegistry(initialCapacity: 64, maximumCapacity: 256, cellSize: 20f);
            try
            {
                registry.Register(new EvidencePerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(1f, 0f, 1f),
                    LineOfSightPoint = new float3(1f, 0f, 1f),
                    DetectionRadius = 0.5f,
                    IsDetectable = true
                });
                registry.RebuildData();

                for (int s = 0; s < sensorCounts.Length; s++)
                {
                    int sensorCount = sensorCounts[s];
                    var manager = new SensorManager(registry, maximumSensorCount: sensorCount + 4);
                    var sensors = new ProximitySensor[sensorCount];
                    for (int i = 0; i < sensorCount; i++)
                    {
                        ProximitySensorConfig config = ProximitySensorConfig.Default;
                        config.Radius = 12f;
                        config.UpdateInterval = 0f;
                        config.MemoryDuration = 0f;
                        config.Capacity = SmallCapacity();
                        var sensor = new ProximitySensor(
                            CreateGameObject($"sched-sensor-{s}-{i}").transform,
                            config,
                            manager);
                        manager.TryRegister(sensor);
                        sensors[i] = sensor;
                    }

                    manager.Update(0f);
                    manager.LateUpdate();
                    manager.Update(0f);
                    manager.LateUpdate();

                    const int iterations = 20;
                    var stopwatch = Stopwatch.StartNew();
                    for (int it = 0; it < iterations; it++)
                    {
                        manager.Update(0f);
                        manager.LateUpdate();
                    }

                    stopwatch.Stop();
                    double perFrameMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
                    EvidenceWriter.Write(
                        $"schedule|sensors={sensorCount}|msPerFrame={Format(perFrameMs)}|usPerSensor={Format(perFrameMs * 1000d / sensorCount)}");

                    for (int i = 0; i < sensors.Length; i++)
                    {
                        sensors[i].Dispose();
                    }

                    manager.Dispose();
                }
            }
            finally
            {
                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_MultiManagerSharedRegistryCost()
        {
            int[] managerCounts = { 1, 2, 4, 8 };
            const int perceptibleCount = 1000;

            for (int m = 0; m < managerCounts.Length; m++)
            {
                int managerCount = managerCounts[m];
                var registry = new PerceptibleRegistry(
                    initialCapacity: perceptibleCount,
                    maximumCapacity: perceptibleCount + 16,
                    cellSize: 8f);
                for (int t = 0; t < perceptibleCount; t++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 100, 0f, t / 100),
                        LineOfSightPoint = new float3(t % 100, 0f, t / 100),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    });
                }

                var managers = new SensorManager[managerCount];
                for (int i = 0; i < managerCount; i++)
                {
                    managers[i] = new SensorManager(registry);
                    managers[i].UseDeferredJobCompletion = true;
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 12f;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    var sensor = new ProximitySensor(
                        CreateGameObject($"multi-manager-sensor-{m}-{i}").transform,
                        config,
                        managers[i]);
                    managers[i].TryRegister(sensor);
                }

                for (int i = 0; i < managerCount; i++)
                {
                    managers[i].Update(0f);
                }

                const int iterations = 20;
                var stopwatch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    for (int i = 0; i < managerCount; i++)
                    {
                        managers[i].Update(0f);
                    }

                    for (int i = 0; i < managerCount; i++)
                    {
                        managers[i].LateUpdate();
                    }
                }

                stopwatch.Stop();
                double perFrameMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
                EvidenceWriter.Write(
                    $"multiManager|managers={managerCount}|perceptibles={perceptibleCount}|msPerFrame={Format(perFrameMs)}|usPerManager={Format(perFrameMs * 1000d / managerCount)}");

                for (int i = 0; i < managerCount; i++)
                {
                    managers[i].Dispose();
                }

                registry.Dispose();
            }
        }

        // ====================================================================================
        // 4. Memory contracts
        // ====================================================================================

        [Test]
        public void Evidence_StimulusMemory_RefreshDecayAndDisableContracts()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 32, cellSize: 20f);
            Transform sensorTransform = CreateGameObject("memory-sensor").transform;
            SensorManager manager = new SensorManager(registry);
            ProximitySensor sensor = null;
            var failures = new List<string>();
            try
            {
                var target = new EvidencePerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(1f, 0f, 0f),
                    LineOfSightPoint = new float3(1f, 0f, 0f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                };
                PerceptibleHandle handle = registry.Register(target);
                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 10f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 30f;
                config.Capacity = SmallCapacity();
                sensor = new ProximitySensor(sensorTransform, config, manager);

                sensor.UpdateSensor(0f);
                float liveVisibility = sensor.TryGetResult(0, out DetectionResult live) ? live.Visibility : -1f;
                if (sensor.DetectedCount != 1 || live.IsFromMemory)
                {
                    failures.Add($"live|count={sensor.DetectedCount}|fromMemory={live.IsFromMemory}");
                }

                target.Position = new float3(500f, 0f, 0f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                float memoryVisibility = -1f;
                if (sensor.DetectedCount != 1 ||
                    !sensor.TryGetResult(0, out DetectionResult remembered) ||
                    !remembered.IsFromMemory)
                {
                    failures.Add($"memory|count={sensor.DetectedCount}|status={sensor.LastUpdateStatus}");
                }
                else
                {
                    memoryVisibility = remembered.Visibility;
                    if (remembered.Visibility > liveVisibility + 1e-4f)
                    {
                        failures.Add($"memory|visibility grew {liveVisibility}->{remembered.Visibility}");
                    }

                    if (remembered.Target != handle)
                    {
                        failures.Add("memory|handle mismatch");
                    }
                }

                EvidenceWriter.Write(
                    $"memory|liveVisibility={Format(liveVisibility)}|memoryVisibility={Format(memoryVisibility)}|count={sensor.DetectedCount}");

                sensor.IsEnabled = false;
                if (sensor.DetectedCount != 0 || sensor.HasDetection)
                {
                    failures.Add("disable|results not cleared");
                }
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void Evidence_ZeroMemoryDurationNeverRetainsStimulus()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 32, cellSize: 20f);
            Transform sensorTransform = CreateGameObject("zero-memory-sensor").transform;
            SensorManager manager = new SensorManager(registry);
            ProximitySensor sensor = null;
            try
            {
                var target = new EvidencePerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(1f, 0f, 0f),
                    LineOfSightPoint = new float3(1f, 0f, 0f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                };
                registry.Register(target);
                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 10f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                config.Capacity = SmallCapacity();
                sensor = new ProximitySensor(sensorTransform, config, manager);

                sensor.UpdateSensor(0f);
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));

                target.Position = new float3(500f, 0f, 0f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                Assert.That(sensor.DetectedCount, Is.Zero);
                Assert.That(sensor.MemoryCount, Is.Zero);
                EvidenceWriter.Write("zeroMemory|retained=0");
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        // ====================================================================================
        // 5. Determinism
        // ====================================================================================

        [Test]
        public void Evidence_RepeatedRunsProduceIdenticalOrdering()
        {
            var signatures = new List<string>();
            for (int run = 0; run < 5; run++)
            {
                signatures.Add(RunSignatureProbe());
            }

            EvidenceWriter.Write($"determinism|runs=5|firstSignatureLength={signatures[0].Length}");
            for (int i = 1; i < signatures.Count; i++)
            {
                Assert.That(signatures[i], Is.EqualTo(signatures[0]), $"Run {i} diverged from run 0");
            }
        }

        private string RunSignatureProbe()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 64, maximumCapacity: 128, cellSize: 5f);
            Transform sensorTransform = CreateGameObject("determinism-sensor").transform;
            SensorManager manager = new SensorManager(registry);
            SightSensor sight = null;
            try
            {
                for (int i = 0; i < 40; i++)
                {
                    float3 position = new float3((i % 4) * 2f, 0f, (i % 5) * 2f);
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = position,
                        LineOfSightPoint = position,
                        DetectionRadius = 0.25f,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();
                SightSensorConfig config = SightSensorConfig.Default;
                config.HalfAngle = 180f;
                config.MaxDistance = 40f;
                config.UpdateInterval = 0f;
                config.UseLineOfSight = false;
                config.MemoryDuration = 0f;
                config.Capacity = SmallCapacity();
                sight = new SightSensor(sensorTransform, config, manager);
                sight.UpdateSensor(0f);

                var builder = new System.Text.StringBuilder();
                for (int i = 0; i < sight.DetectedCount; i++)
                {
                    if (sight.TryGetResult(i, out DetectionResult result))
                    {
                        builder.Append(result.Target.Id).Append(':').Append(Format(result.Distance)).Append(';');
                    }
                }

                return builder.ToString();
            }
            finally
            {
                sight?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        // ====================================================================================
        // 6. FUZZ — hostile randomized worlds, invariants only
        // ====================================================================================

        [Test]
        public void Evidence_Fuzz_HostileWorldsPreserveInvariantsAndDeterminism()
        {
            const int seedCount = 120;
            var failures = new List<string>();
            int sensorRuns = 0;

            for (int seed = 1; seed <= seedCount; seed++)
            {
                string first = RunFuzzWorld(seed, failures, ref sensorRuns);
                string second = RunFuzzWorld(seed, failures, ref sensorRuns);
                if (!string.Equals(first, second, StringComparison.Ordinal))
                {
                    failures.Add($"seed={seed}|nonDeterministic|a={first.Length}|b={second.Length}");
                }
            }

            EvidenceWriter.Write($"fuzz|seeds={seedCount}|sensorRuns={sensorRuns}|failures={failures.Count}");
            if (failures.Count > 0)
            {
                Assert.Fail(string.Join("\n", failures.GetRange(0, math.min(12, failures.Count))));
            }
        }

        private string RunFuzzWorld(int seed, List<string> failures, ref int sensorRuns)
        {
            var random = new XorShift(seed);
            int count = 1 + (int)(random.NextUInt() % 80u);
            float[] cellSizes = { 0.05f, 0.5f, 3f, 17f, 400f };
            float cellSize = cellSizes[random.NextUInt() % (uint)cellSizes.Length];

            var registry = new PerceptibleRegistry(
                initialCapacity: math.max(1, count),
                maximumCapacity: math.max(256, count + 8),
                cellSize: cellSize);
            Transform sensorTransform = CreateGameObject($"fuzz-sensor-{seed}").transform;
            sensorTransform.position = new Vector3(
                (random.NextInt() % 41) - 20f,
                0f,
                (random.NextInt() % 41) - 20f);
            SensorManager manager = new SensorManager(registry);
            var sensors = new List<ISensor>(3);
            var builder = new System.Text.StringBuilder();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    uint bits = random.NextUInt();
                    float3 position = (bits & 0x3F) == 0
                        ? NonFiniteFrom(random.NextUInt())
                        : new float3(
                            (random.NextInt() % 200) * 0.5f,
                            ((random.NextInt() % 20) - 10) * 0.25f,
                            (random.NextInt() % 200) * 0.5f);

                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        PerceptibleTypeId = (int)(random.NextUInt() % 4u),
                        Position = position,
                        LineOfSightPoint = position,
                        DetectionRadius = (bits & 0x100) != 0 ? NonFiniteScalar(bits) : (bits % 7u) * 0.5f,
                        Loudness = (bits & 0x200) != 0 ? NonFiniteScalar(bits >> 3) : (bits % 5u) * 0.5f,
                        IsSoundSource = (bits & 0x400) != 0,
                        IsDetectable = (bits & 0x800) == 0
                    });
                }

                registry.RebuildData();

                for (int kind = 0; kind < 3; kind++)
                {
                    ISensor sensor;
                    switch (kind)
                    {
                        case 0:
                        {
                            SightSensorConfig config = SightSensorConfig.Default;
                            config.HalfAngle = (random.NextUInt() % 181u);
                            config.MaxDistance = (random.NextUInt() % 60u) * 0.5f;
                            config.NearClippingRadius = (random.NextUInt() % 5u) * 0.5f;
                            config.LoseSightRadius = (random.NextUInt() % 120u) * 0.5f;
                            config.UpdateInterval = 0f;
                            config.UseLineOfSight = false;
                            config.FilterByType = (random.NextUInt() & 1u) == 0;
                            config.TargetTypeId = (int)(random.NextUInt() % 4u);
                            config.MemoryDuration = (random.NextUInt() % 4u) * 0.5f;
                            config.Capacity = RandomCapacity(random);
                            sensor = new SightSensor(sensorTransform, config, manager);
                            break;
                        }

                        case 1:
                        {
                            ProximitySensorConfig config = ProximitySensorConfig.Default;
                            config.Radius = (random.NextUInt() % 40u) * 0.5f;
                            config.UpdateInterval = 0f;
                            config.FilterByType = (random.NextUInt() & 1u) == 0;
                            config.TargetTypeId = (int)(random.NextUInt() % 4u);
                            config.MemoryDuration = (random.NextUInt() % 4u) * 0.5f;
                            config.Capacity = RandomCapacity(random);
                            sensor = new ProximitySensor(sensorTransform, config, manager);
                            break;
                        }

                        default:
                        {
                            HearingSensorConfig config = HearingSensorConfig.Default;
                            config.Radius = (random.NextUInt() % 40u) * 0.5f;
                            config.UpdateInterval = 0f;
                            config.UseOcclusion = false;
                            config.FilterByType = (random.NextUInt() & 1u) == 0;
                            config.TargetTypeId = (int)(random.NextUInt() % 4u);
                            config.MemoryDuration = (random.NextUInt() % 4u) * 0.5f;
                            config.Capacity = RandomCapacity(random);
                            sensor = new HearingSensor(sensorTransform, config, manager);
                            break;
                        }
                    }

                    sensors.Add(sensor);
                    sensorRuns++;
                    sensor.UpdateSensor(0f);

                    string tag = $"seed={seed}|kind={kind}|n={count}|cell={Format(cellSize)}";
                    if (sensor.DetectedCount < 0 || sensor.DetectedCount > count)
                    {
                        failures.Add($"{tag}|countOutOfRange={sensor.DetectedCount}");
                    }

                    if (!Enum.IsDefined(typeof(SensorUpdateStatus), sensor.LastUpdateStatus))
                    {
                        failures.Add($"{tag}|undefinedStatus={(byte)sensor.LastUpdateStatus}");
                    }

                    float previousDistance = -1f;
                    for (int i = 0; i < sensor.DetectedCount; i++)
                    {
                        if (!sensor.TryGetResult(i, out DetectionResult result))
                        {
                            failures.Add($"{tag}|readFailed at {i}");
                            break;
                        }

                        if (!float.IsFinite(result.Distance) || result.Distance < 0f)
                        {
                            failures.Add($"{tag}|badDistance={result.Distance}");
                            break;
                        }

                        if (!float.IsFinite(result.Visibility) || result.Visibility < 0f || result.Visibility > 1.0001f)
                        {
                            failures.Add($"{tag}|badVisibility={result.Visibility}");
                            break;
                        }

                        if (result.IsFromMemory && result.DetectionTime <= 0d && result.Distance > 0f)
                        {
                            // Memory entries always carry a real timestamp; zero means a live result.
                            failures.Add($"{tag}|memoryWithoutTimestamp");
                            break;
                        }

                        if (result.Distance < previousDistance - 1e-5f)
                        {
                            failures.Add($"{tag}|unsortedAt{i} prev={previousDistance} current={result.Distance}");
                            break;
                        }

                        previousDistance = result.Distance;
                        if (builder.Length < 4096)
                        {
                            builder.Append(result.Target.Id).Append(':').Append(Format(result.Distance)).Append(';');
                        }
                    }
                }

                return builder.ToString();
            }
            finally
            {
                for (int i = 0; i < sensors.Count; i++)
                {
                    sensors[i].Dispose();
                }

                manager.Dispose();
                registry.Dispose();
            }
        }

        private static PerceptionSensorCapacity RandomCapacity(XorShift random)
        {
            uint bits = random.NextUInt();
            int initial = 1 + (int)(bits % 16u);
            int maximum = initial + (int)((bits >> 8) % 64u);
            return new PerceptionSensorCapacity
            {
                InitialCandidateCapacity = initial,
                MaximumCandidates = maximum,
                InitialResultCapacity = initial,
                MaximumResults = maximum,
                InitialMemoryCapacity = initial,
                MaximumMemoryEntries = maximum
            };
        }

        private static float3 NonFiniteFrom(uint bits)
        {
            switch (bits % 4u)
            {
                case 0: return new float3(float.NaN, 0f, 0f);
                case 1: return new float3(float.PositiveInfinity, 0f, 0f);
                case 2: return new float3(float.MaxValue, float.MaxValue, 0f);
                default: return new float3(-float.MaxValue, 0f, float.NegativeInfinity);
            }
        }

        private static float NonFiniteScalar(uint bits)
        {
            switch (bits % 4u)
            {
                case 0: return float.NaN;
                case 1: return float.PositiveInfinity;
                case 2: return -1f;
                default: return 1e30f;
            }
        }

        // ====================================================================================
        // 7. IMPULSE — registration storms and churn
        // ====================================================================================

        [Test]
        public void Evidence_Impulse_RegistrationBurstsAndFullDrain()
        {
            int[] bursts = { 200, 1000, 3000 };
            for (int b = 0; b < bursts.Length; b++)
            {
                int burst = bursts[b];
                var registry = new PerceptibleRegistry(
                    initialCapacity: 128,
                    maximumCapacity: burst + 64,
                    cellSize: 6f);
                Transform sensorTransform = CreateGameObject($"impulse-sensor-{burst}").transform;
                SensorManager manager = new SensorManager(registry);
                ProximitySensor sensor = null;
                var targets = new EvidencePerceptible[burst];
                var handles = new PerceptibleHandle[burst];
                try
                {
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 15f;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    sensor = new ProximitySensor(sensorTransform, config, manager);

                    for (int round = 0; round < 3; round++)
                    {
                        var registerWatch = Stopwatch.StartNew();
                        for (int i = 0; i < burst; i++)
                        {
                            int index = i;
                            targets[index] = new EvidencePerceptible
                            {
                                PerceptibleId = index,
                                Position = new float3(index % 64, 0f, index / 64),
                                LineOfSightPoint = new float3(index % 64, 0f, index / 64),
                                DetectionRadius = 0.25f,
                                IsDetectable = true
                            };
                            handles[index] = registry.Register(targets[index]);
                        }

                        registerWatch.Stop();
                        registry.RebuildData();
                        sensor.UpdateSensor(0f);

                        int detected = sensor.DetectedCount;
                        var drainWatch = Stopwatch.StartNew();
                        for (int i = 0; i < burst; i++)
                        {
                            registry.Unregister(handles[i]);
                        }

                        drainWatch.Stop();
                        registry.RebuildData();
                        sensor.UpdateSensor(0f);

                        EvidenceWriter.Write(
                            $"impulse|burst={burst}|round={round}|registerMs={Format(registerWatch.Elapsed.TotalMilliseconds)}|drainMs={Format(drainWatch.Elapsed.TotalMilliseconds)}|detectedBeforeDrain={detected}|afterDrainCount={registry.Count}|afterDrainDetected={sensor.DetectedCount}");

                        Assert.That(registry.Count, Is.Zero, "Full drain must return the registry to zero active slots.");
                        Assert.That(sensor.DetectedCount, Is.Zero, "No live or remembered target may survive a full drain.");
                    }

                    for (int i = 0; i < burst; i++)
                    {
                        Assert.That(registry.IsValid(handles[i]), Is.False, "Drained handles must not resolve.");
                    }

                    PerceptibleRegistryMemoryStats stats = registry.GetMemoryStats();
                    EvidenceWriter.Write(
                        $"impulse|burst={burst}|slotCapacity={stats.SlotCapacity}|nativeSnapshotCapacity={stats.NativeSnapshotCapacity}|peak={stats.PeakPerceptibleCount}|rejected={stats.RejectedRegistrationCount}");
                }
                finally
                {
                    sensor?.Dispose();
                    manager.Dispose();
                    registry.Dispose();
                }
            }
        }

        [Test]
        public void Evidence_Impulse_CapacityAdmissionStormSaturatesWithoutGrowth()
        {
            const int capacity = 512;
            const int attempts = 4096;
            var registry = new PerceptibleRegistry(
                initialCapacity: 16,
                maximumCapacity: capacity,
                cellSize: 10f);
            var accepted = new List<PerceptibleHandle>(capacity);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < attempts; i++)
                {
                    PerceptibleHandle handle = registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % 32, 0f, i / 32),
                        LineOfSightPoint = new float3(i % 32, 0f, i / 32),
                        DetectionRadius = 0.25f,
                        IsDetectable = true
                    });

                    if (handle.IsValid)
                    {
                        accepted.Add(handle);
                    }
                }

                stopwatch.Stop();
                PerceptibleRegistryMemoryStats saturated = registry.GetMemoryStats();
                EvidenceWriter.Write(
                    $"capacityStorm|attempts={attempts}|capacity={capacity}|accepted={accepted.Count}|rejected={saturated.RejectedRegistrationCount}|slotCapacity={saturated.SlotCapacity}|ms={Format(stopwatch.Elapsed.TotalMilliseconds)}");

                Assert.That(accepted.Count, Is.EqualTo(capacity));
                Assert.That(saturated.RejectedRegistrationCount, Is.EqualTo((long)(attempts - capacity)));

                for (int i = 0; i < accepted.Count; i++)
                {
                    registry.Unregister(accepted[i]);
                }

                Assert.That(registry.Count, Is.Zero);
                Assert.That(registry.Register(new EvidencePerceptible { PerceptibleId = 0 }).IsValid, Is.True);
                EvidenceWriter.Write("capacityStorm|recovery=ok");
            }
            finally
            {
                registry.Dispose();
            }
        }

        // ====================================================================================
        // 8. SOAK — steady-state creep detection
        // ====================================================================================

        [Test]
        public void Evidence_Soak_SteadyStateShowsNoTimeOrMemoryCreep()
        {
            const int perceptibleCount = 1200;
            const int sensorCount = 6;
            const int iterations = 1200;
            const int blockSize = 200;

            var registry = new PerceptibleRegistry(
                initialCapacity: perceptibleCount,
                maximumCapacity: perceptibleCount + 32,
                cellSize: 8f);
            var targets = new EvidencePerceptible[perceptibleCount];
            var managers = new SensorManager[sensorCount];
            var sensors = new ProximitySensor[sensorCount];
            var blockTimes = new double[iterations / blockSize];

            try
            {
                for (int i = 0; i < perceptibleCount; i++)
                {
                    targets[i] = new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % 40, 0f, i / 40),
                        LineOfSightPoint = new float3(i % 40, 0f, i / 40),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    registry.Register(targets[i]);
                }

                for (int s = 0; s < sensorCount; s++)
                {
                    managers[s] = new SensorManager(registry);
                    managers[s].UseDeferredJobCompletion = true;
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 10f;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 5f;
                    config.Capacity = SmallCapacity();
                    sensors[s] = new ProximitySensor(
                        CreateGameObject($"soak-sensor-{s}").transform,
                        config,
                        managers[s]);
                    managers[s].TryRegister(sensors[s]);
                }

                for (int warmup = 0; warmup < 50; warmup++)
                {
                    StepSoak(managers, targets, warmup);
                }

                long heapBefore = GC.GetTotalMemory(true);
                int bufferCapacityBefore = TotalNativeCapacity(managers);

                int blockIndex = 0;
                var blockWatch = Stopwatch.StartNew();
                int steadyDetections = 0;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    StepSoak(managers, targets, iteration);
                    steadyDetections = sensors[0].DetectedCount;

                    if ((iteration + 1) % blockSize == 0)
                    {
                        blockWatch.Stop();
                        blockTimes[blockIndex++] = blockWatch.Elapsed.TotalMilliseconds;
                        blockWatch.Restart();
                    }
                }

                long heapAfter = GC.GetTotalMemory(true);
                int bufferCapacityAfter = TotalNativeCapacity(managers);
                PerceptibleRegistryMemoryStats stats = registry.GetMemoryStats();

                double firstBlock = blockTimes[0];
                double lastBlock = blockTimes[blockTimes.Length - 1];
                double creep = lastBlock / math.max(firstBlock, 1e-6d);

                EvidenceWriter.Write(
                    $"soak|perceptibles={perceptibleCount}|sensors={sensorCount}|iterations={iterations}|firstBlockMs={Format(firstBlock)}|lastBlockMs={Format(lastBlock)}|creepRatio={Format(creep)}|heapDeltaBytes={heapAfter - heapBefore}|nativeCapacityBefore={bufferCapacityBefore}|nativeCapacityAfter={bufferCapacityAfter}|detections={steadyDetections}|peaks={stats.PeakPerceptibleCount}");

                Assert.That(creep, Is.LessThan(3.0d), "Steady-state per-block cost must not grow.");
                Assert.That(bufferCapacityAfter, Is.EqualTo(bufferCapacityBefore), "Native buffer capacity must stabilize.");
                Assert.That(heapAfter - heapBefore, Is.LessThan(4L * 1024 * 1024));
                Assert.That(steadyDetections, Is.GreaterThan(0));
            }
            finally
            {
                for (int s = 0; s < sensorCount; s++)
                {
                    sensors[s]?.Dispose();
                    managers[s]?.Dispose();
                }

                registry.Dispose();
            }
        }

        private static void StepSoak(SensorManager[] managers, EvidencePerceptible[] targets, int iteration)
        {
            int stride = 1 + (iteration % 7);
            for (int i = iteration % stride; i < targets.Length; i += stride)
            {
                float3 position = targets[i].Position;
                targets[i].Position = new float3(position.x, 0f, position.z + 0.05f);
                targets[i].LineOfSightPoint = targets[i].Position;
            }

            for (int s = 0; s < managers.Length; s++)
            {
                managers[s].Update(0.016f);
            }

            for (int s = 0; s < managers.Length; s++)
            {
                managers[s].LateUpdate();
            }
        }

        private static int TotalNativeCapacity(SensorManager[] managers)
        {
            int total = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                total += managers[i].GetMemoryStats().NativeBufferCapacity;
            }

            return total;
        }

        // ====================================================================================
        // 9. SCALE — large world, many sensors
        // ====================================================================================

        [Test]
        public void Evidence_Scale_LargeWorldWithManySensors()
        {
            const int perceptibleCount = 16000;
            int[] sensorCounts = { 64, 256 };

            var registry = new PerceptibleRegistry(
                initialCapacity: 1024,
                maximumCapacity: perceptibleCount + 64,
                cellSize: 8f);
            var targets = new EvidencePerceptible[perceptibleCount];
            try
            {
                var buildWatch = Stopwatch.StartNew();
                for (int i = 0; i < perceptibleCount; i++)
                {
                    targets[i] = new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % 128, 0f, i / 128),
                        LineOfSightPoint = new float3(i % 128, 0f, i / 128),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                    registry.Register(targets[i]);
                }

                buildWatch.Stop();
                registry.RebuildData();
                EvidenceWriter.Write(
                    $"scale|perceptibles={perceptibleCount}|registerMs={Format(buildWatch.Elapsed.TotalMilliseconds)}|snapshotCount={registry.GetDataCount()}|cells={registry.GetMemoryStats().SpatialCellCount}");

                for (int s = 0; s < sensorCounts.Length; s++)
                {
                    int sensorCount = sensorCounts[s];
                    var manager = new SensorManager(registry, maximumSensorCount: sensorCount + 8);
                    var sensors = new ProximitySensor[sensorCount];
                    try
                    {
                        for (int i = 0; i < sensorCount; i++)
                        {
                            ProximitySensorConfig config = ProximitySensorConfig.Default;
                            config.Radius = 12f;
                            config.UpdateInterval = 0f;
                            config.MemoryDuration = 0f;
                            config.Capacity = SmallCapacity();
                            sensors[i] = new ProximitySensor(
                                CreateGameObject($"scale-sensor-{s}-{i}").transform,
                                config,
                                manager);
                            manager.TryRegister(sensors[i]);
                        }

                        manager.Update(0f);
                        manager.LateUpdate();

                        const int iterations = 10;
                        var stopwatch = Stopwatch.StartNew();
                        int detected = 0;
                        for (int it = 0; it < iterations; it++)
                        {
                            manager.Update(0.016f);
                            manager.LateUpdate();
                            detected = sensors[0].DetectedCount;
                        }

                        stopwatch.Stop();
                        double perFrameMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
                        AIPerceptionMemoryStats stats = manager.GetMemoryStats();
                        EvidenceWriter.Write(
                            $"scale|perceptibles={perceptibleCount}|sensors={sensorCount}|msPerFrame={Format(perFrameMs)}|usPerSensor={Format(perFrameMs * 1000d / sensorCount)}|sampleDetections={detected}|nativeBufferCapacity={stats.NativeBufferCapacity}|peakCandidate={stats.PeakCandidateCount}");
                        Assert.That(detected, Is.GreaterThan(0));
                    }
                    finally
                    {
                        for (int i = 0; i < sensorCount; i++)
                        {
                            sensors[i]?.Dispose();
                        }

                        manager.Dispose();
                    }
                }
            }
            finally
            {
                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_MultiManagerDirtyFrameRepeatsSnapshotPublication()
        {
            int[] managerCounts = { 1, 2, 4 };
            const int perceptibleCount = 4000;

            for (int m = 0; m < managerCounts.Length; m++)
            {
                int managerCount = managerCounts[m];
                var registry = new PerceptibleRegistry(
                    initialCapacity: perceptibleCount,
                    maximumCapacity: perceptibleCount + 16,
                    cellSize: 8f);
                for (int t = 0; t < perceptibleCount; t++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 64, 0f, t / 64),
                        LineOfSightPoint = new float3(t % 64, 1f, t / 64),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    });
                }

                var managers = new SensorManager[managerCount];
                for (int i = 0; i < managerCount; i++)
                {
                    managers[i] = new SensorManager(registry);
                    managers[i].UseDeferredJobCompletion = true;
                    ProximitySensorConfig config = ProximitySensorConfig.Default;
                    config.Radius = 12f;
                    config.UpdateInterval = 0f;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    var sensor = new ProximitySensor(
                        CreateGameObject($"dirty-multi-sensor-{m}-{i}").transform,
                        config,
                        managers[i]);
                    managers[i].TryRegister(sensor);
                }

                managers[0].Update(0f);
                managers[0].LateUpdate();

                const int iterations = 6;
                var stopwatch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    registry.MarkDirty();
                    for (int i = 0; i < managerCount; i++)
                    {
                        managers[i].Update(0f);
                    }

                    for (int i = 0; i < managerCount; i++)
                    {
                        managers[i].LateUpdate();
                    }
                }

                stopwatch.Stop();
                double perFrameMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
                EvidenceWriter.Write(
                    $"dirtyMultiManager|managers={managerCount}|perceptibles={perceptibleCount}|msPerFrame={Format(perFrameMs)}|msPerManager={Format(perFrameMs / managerCount)}|fits60fps={perFrameMs < 16.6d}");

                for (int i = 0; i < managerCount; i++)
                {
                    managers[i].Dispose();
                }

                registry.Dispose();
            }
        }

        [Test]
        public void Evidence_Isolation_IndependentWorldsDoNotInterfere()
        {
            const int worldCount = 4;
            const int perWorld = 200;
            var registries = new PerceptibleRegistry[worldCount];
            var managers = new SensorManager[worldCount];
            var sensors = new SightSensor[worldCount];
            try
            {
                for (int w = 0; w < worldCount; w++)
                {
                    registries[w] = new PerceptibleRegistry(
                        initialCapacity: perWorld,
                        maximumCapacity: perWorld + 8,
                        cellSize: 5f + w);
                    managers[w] = new SensorManager(registries[w]);

                    for (int i = 0; i < perWorld; i++)
                    {
                        registries[w].Register(new EvidencePerceptible
                        {
                            PerceptibleId = i,
                            Position = new float3(1f + (i % 20), 0f, 1f + (i / 20)),
                            LineOfSightPoint = new float3(1f + (i % 20), 0f, 1f + (i / 20)),
                            DetectionRadius = 0.25f,
                            IsDetectable = true
                        });
                    }

                    registries[w].RebuildData();
                    SightSensorConfig config = SightSensorConfig.Default;
                    config.HalfAngle = 180f;
                    config.MaxDistance = 50f;
                    config.UpdateInterval = 0f;
                    config.UseLineOfSight = false;
                    config.MemoryDuration = 0f;
                    config.Capacity = SmallCapacity();
                    sensors[w] = new SightSensor(
                        CreateGameObject($"isolation-sensor-{w}").transform,
                        config,
                        managers[w]);
                    sensors[w].UpdateSensor(0f);
                }

                for (int w = 0; w < worldCount; w++)
                {
                    Assert.That(sensors[w].DetectedCount, Is.EqualTo(perWorld));
                    for (int i = 0; i < sensors[w].DetectedCount; i++)
                    {
                        Assert.That(sensors[w].TryGetResult(i, out DetectionResult result), Is.True);
                        Assert.That(result.Target.RegistryId, Is.EqualTo(registries[w].RegistryId));
                        Assert.That(registries[w].Get(result.Target), Is.Not.Null);
                        for (int other = 0; other < worldCount; other++)
                        {
                            if (other == w)
                            {
                                continue;
                            }

                            Assert.That(registries[other].IsValid(result.Target), Is.False);
                        }
                    }
                }

                EvidenceWriter.Write(
                    $"isolation|worlds={worldCount}|perWorld={perWorld}|detectionsPerWorld={sensors[0].DetectedCount}");
            }
            finally
            {
                for (int w = 0; w < worldCount; w++)
                {
                    sensors[w]?.Dispose();
                    managers[w]?.Dispose();
                    registries[w]?.Dispose();
                }
            }
        }

        // ====================================================================================
        // 10. Lifecycle repetition — leak detection
        // ====================================================================================

        [Test]
        public void Evidence_LifecycleRepeatedCreateDisposeDoesNotAccumulate()
        {
            const int cycles = 120;
            long heapBefore = GC.GetTotalMemory(true);
            int lastCapacity = 0;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                var registry = new PerceptibleRegistry(initialCapacity: 32, maximumCapacity: 256, cellSize: 7f);
                var manager = new SensorManager(registry);
                Transform transform = CreateGameObject($"cycle-sensor-{cycle}").transform;
                for (int i = 0; i < 20; i++)
                {
                    registry.Register(new EvidencePerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i, 0f, 1f),
                        LineOfSightPoint = new float3(i, 0f, 1f),
                        DetectionRadius = 0.25f,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();
                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 10f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 1f;
                config.Capacity = SmallCapacity();
                var sensor = new ProximitySensor(transform, config, manager);
                manager.TryRegister(sensor);
                manager.Update(0f);
                manager.LateUpdate();

                lastCapacity = manager.GetMemoryStats().NativeBufferCapacity;
                sensor.Dispose();
                manager.Dispose();
                registry.Dispose();
            }

            long heapAfter = GC.GetTotalMemory(true);
            EvidenceWriter.Write(
                $"lifecycle|cycles={cycles}|heapDeltaBytes={heapAfter - heapBefore}|lastNativeCapacity={lastCapacity}");
            Assert.That(heapAfter - heapBefore, Is.LessThan(2L * 1024 * 1024));
        }

        // ====================================================================================
        // Helpers
        // ====================================================================================

        private const int LayoutGrid = 0;
        private const int LayoutCluster = 1;
        private const int LayoutLine = 2;
        private const int LayoutDuplicate = 3;
        private const int LayoutWide = 4;

        private enum SensorKind
        {
            Sight = 0,
            Proximity = 1,
            Hearing = 2
        }

        private static string Format(double value)
        {
            return value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static EvidencePerceptible CreateLayoutTarget(int layout, int index, int count)
        {
            float3 position;
            switch (layout)
            {
                case LayoutGrid:
                    position = new float3((index % 8) * 2.5f, (index % 3) * 0.5f, (index / 8) * 2.5f);
                    break;
                case LayoutCluster:
                    position = new float3(3f + (index % 4) * 0.1f, 0f, 3f + (index % 3) * 0.1f);
                    break;
                case LayoutLine:
                    position = new float3(index * 1.5f, 0f, 0f);
                    break;
                case LayoutDuplicate:
                    position = new float3((index % 2) * 3f, 0f, 2f);
                    break;
                default:
                    position = new float3(
                        ((index % 9) - 4) * 350f,
                        0f,
                        ((index % 7) - 3) * 350f + 5f);
                    break;
            }

            uint hash = (uint)(index * 2654435761u) ^ (uint)(count * 40503u) ^ 0x9E3779B9u;
            float radius = (hash & 0x7) * 0.5f;
            float loudness = ((hash >> 3) & 0x7) * 0.5f;
            bool soundSource = ((hash >> 6) & 0x3) != 0;
            bool detectable = ((hash >> 8) & 0x7) != 0;
            int typeId = (int)((hash >> 11) & 0x3);

            return new EvidencePerceptible
            {
                PerceptibleId = index,
                PerceptibleTypeId = typeId,
                Position = position,
                LineOfSightPoint = position + new float3(0f, 1f, 0f),
                DetectionRadius = radius,
                Loudness = loudness,
                IsSoundSource = soundSource,
                IsDetectable = detectable
            };
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private readonly struct ReferenceHit
        {
            public ReferenceHit(int slot, float distance, float visibility, bool isFromMemory)
            {
                Slot = slot;
                Distance = distance;
                Visibility = visibility;
                IsFromMemory = isFromMemory;
            }

            public int Slot { get; }
            public float Distance { get; }
            public float Visibility { get; }
            public bool IsFromMemory { get; }
        }

        private sealed class ReferenceHitComparer : IComparer<ReferenceHit>
        {
            public static readonly ReferenceHitComparer Instance = new ReferenceHitComparer();

            public int Compare(ReferenceHit left, ReferenceHit right)
            {
                int distance = left.Distance.CompareTo(right.Distance);
                if (distance != 0)
                {
                    return distance;
                }

                int slot = left.Slot.CompareTo(right.Slot);
                return slot != 0 ? slot : left.IsFromMemory.CompareTo(right.IsFromMemory);
            }
        }

        /// <summary>Deterministic xorshift so fuzz seeds reproduce byte-identical worlds.</summary>
        private struct XorShift
        {
            private uint _state;

            public XorShift(int seed)
            {
                _state = seed == 0 ? 0x9E3779B9u : (uint)seed;
            }

            public uint NextUInt()
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x == 0 ? 0x9E3779B9u : x;
                return _state;
            }

            public int NextInt()
            {
                return (int)(NextUInt() & 0x7FFFFFFFu);
            }
        }

        private sealed class EvidencePerceptible : IPerceptible, IPerceptionTeamProvider
        {
            public int PerceptibleId { get; set; }
            public int PerceptibleTypeId { get; set; }
            public int TeamId { get; set; }
            public bool IsDetectable { get; set; }
            public float3 Position { get; set; }
            public float DetectionRadius { get; set; }
            public float Loudness { get; set; }
            public bool IsSoundSource { get; set; }
            public string Tag { get; set; } = string.Empty;
            public float3 LineOfSightPoint { get; set; }

            public float3 GetLOSPoint()
            {
                return LineOfSightPoint;
            }
        }

        private static class EvidenceWriter
        {
            private static readonly List<string> Lines = new List<string>(256);
            private static string _path;

            public static void Reset()
            {
                Lines.Clear();
                try
                {
                    string root = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "..", "..", "Temp~", "aiperception-evidence"));
                    System.IO.Directory.CreateDirectory(root);
                    _path = System.IO.Path.Combine(root, "evidence.txt");
                    if (System.IO.File.Exists(_path))
                    {
                        System.IO.File.Delete(_path);
                    }
                }
                catch (Exception exception)
                {
                    _path = null;
                    Debug.LogWarning($"AIPerception evidence writer disabled: {exception.Message}");
                }
            }

            public static void Write(string line)
            {
                Lines.Add(line);
                Debug.Log("EVIDENCE|" + line);
                if (_path == null)
                {
                    return;
                }

                try
                {
                    System.IO.File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch
                {
                    _path = null;
                }
            }

            public static void Flush()
            {
                if (_path == null)
                {
                    return;
                }

                try
                {
                    System.IO.File.AppendAllText(
                        _path,
                        $"summary|lines={Lines.Count}{Environment.NewLine}");
                }
                catch
                {
                    // Evidence is best-effort; the Editor log still contains every line.
                }
            }
        }
    }
}
