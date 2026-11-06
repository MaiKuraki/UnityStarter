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
    /// Fix verification for the 2026-11-06 audit. Each test either guards a regression that was
    /// measured before the fix or proves a newly added contract. Evidence lines are written with the
    /// same channel as <see cref="AIPerceptionEvidenceTests"/> so both suites can be diffed.
    /// </summary>
    public sealed class AIPerceptionFixVerificationTests
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

        // ====================================================================================
        // Perf regressions
        // ====================================================================================

        [Test]
        public void Fix_RebuildSortKey_DirtyRebuildStaysInsideFrameBudget()
        {
            const int count = 16000;
            var registry = new PerceptibleRegistry(
                initialCapacity: count,
                maximumCapacity: count + 16,
                cellSize: 8f);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    registry.Register(new FixPerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(i % 128, 0f, i / 128),
                        LineOfSightPoint = new float3(i % 128, 1f, i / 128),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                var captureWatch = Stopwatch.StartNew();
                for (int i = 0; i < 8; i++)
                {
                    registry.RebuildData();
                }

                captureWatch.Stop();
                double captureMs = captureWatch.Elapsed.TotalMilliseconds / 8d;

                var dirtyWatch = Stopwatch.StartNew();
                for (int i = 0; i < 8; i++)
                {
                    registry.MarkDirty();
                    registry.RebuildData();
                }

                dirtyWatch.Stop();
                double dirtyMs = dirtyWatch.Elapsed.TotalMilliseconds / 8d;

                EvidenceWriter.Write(
                    $"fix.rebuild|count={count}|captureOnlyMs={Format(captureMs)}|dirtyRepublishMs={Format(dirtyMs)}|baselineBeforeFixMs=52.723|budgetMs=16.6|withinBudget={dirtyMs < 16.6d}");

                // Baseline before the fix was 52.7 ms for this exact shape. The gate is deliberately
                // loose so slower CI hardware still passes while a real regression cannot hide.
                Assert.That(dirtyMs, Is.LessThan(30d), "Dirty rebuild regressed toward the pre-fix cost.");
            }
            finally
            {
                registry.Dispose();
            }
        }

        [Test]
        public void Fix_RegistrationDedupe_StaysSubQuadraticUnderBulkLoad()
        {
            int[] counts = { 1000, 2000, 4000, 8000, 16000 };
            var measurements = new double[counts.Length];

            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                var registry = new PerceptibleRegistry(
                    initialCapacity: count,
                    maximumCapacity: count + 16,
                    cellSize: 8f);
                var targets = new FixPerceptible[count];
                for (int t = 0; t < count; t++)
                {
                    targets[t] = new FixPerceptible
                    {
                        PerceptibleId = t,
                        Position = new float3(t % 128, 0f, t / 128),
                        LineOfSightPoint = new float3(t % 128, 1f, t / 128),
                        DetectionRadius = 0.5f,
                        IsDetectable = true
                    };
                }

                var watch = Stopwatch.StartNew();
                for (int t = 0; t < count; t++)
                {
                    registry.Register(targets[t]);
                }

                watch.Stop();
                measurements[i] = watch.Elapsed.TotalMilliseconds;
                EvidenceWriter.Write(
                    $"fix.registration|count={count}|ms={Format(measurements[i])}|perItemNs={Format(measurements[i] * 1_000_000d / count)}");
                registry.Dispose();
            }

            double exponent = math.log(measurements[measurements.Length - 1] / math.max(measurements[0], 1e-6d)) /
                              math.log((double)counts[counts.Length - 1] / counts[0]);
            EvidenceWriter.Write(
                $"fix.registration|empiricalExponent={Format(exponent)}|baselineBeforeFix=1.910|linear=1.00");

            Assert.That(exponent, Is.LessThan(1.35d), "Bulk registration is superlinear again; the dedupe scan probably returned.");
            Assert.That(measurements[measurements.Length - 1], Is.LessThan(40d), "16k bulk registration exceeded the spike budget.");
        }

        [Test]
        public void Feature_SightRuleMatrix_MatchesIndependentReference()
        {
            float[] nearClips = { 0f, 1.5f };
            float[] loseSights = { 0f, 20f };
            float[] memories = { 0f, 5f };
            float[] distances = { 0.5f, 5f, 15f, 25f };
            float[] angles = { 0f, 90f };
            const float halfAngle = 60f;
            const float maxDistance = 10f;
            float cosine = math.cos(math.radians(halfAngle));

            var failures = new List<string>();
            int cases = 0;

            for (int n = 0; n < nearClips.Length; n++)
            {
                for (int l = 0; l < loseSights.Length; l++)
                {
                    for (int m = 0; m < memories.Length; m++)
                    {
                        for (int d = 0; d < distances.Length; d++)
                        {
                            for (int a = 0; a < angles.Length; a++)
                            {
                                cases++;
                                string tag =
                                    $"near={nearClips[n]}|lose={loseSights[l]}|mem={memories[m]}|d={distances[d]}|angle={angles[a]}";

                                var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
                                Transform transform = CreateGameObject($"rule-{cases}").transform;
                                var manager = new SensorManager(registry);
                                SightSensor sensor = null;
                                try
                                {
                                    var target = new FixPerceptible
                                    {
                                        PerceptibleId = 1,
                                        Position = new float3(0f, 0f, 2f),
                                        LineOfSightPoint = new float3(0f, 0f, 2f),
                                        DetectionRadius = 0f,
                                        IsDetectable = true
                                    };
                                    registry.Register(target);
                                    registry.RebuildData();

                                    SightSensorConfig config = SightSensorConfig.Default;
                                    config.HalfAngle = halfAngle;
                                    config.MaxDistance = maxDistance;
                                    config.NearClippingRadius = nearClips[n];
                                    config.LoseSightRadius = loseSights[l];
                                    config.MemoryDuration = memories[m];
                                    config.UpdateInterval = 0f;
                                    config.UseLineOfSight = false;
                                    config.Capacity = new PerceptionSensorCapacity
                                    {
                                        InitialCandidateCapacity = 4,
                                        MaximumCandidates = 16,
                                        InitialResultCapacity = 4,
                                        MaximumResults = 16,
                                        InitialMemoryCapacity = 4,
                                        MaximumMemoryEntries = 16
                                    };

                                    sensor = new SightSensor(transform, config, manager);

                                    // Warm-up establishes the remembered state used by lose-sight hysteresis.
                                    sensor.UpdateSensor(0f);
                                    bool warmDetected = HasLiveResult(sensor);
                                    if (!warmDetected)
                                    {
                                        failures.Add($"{tag}|warmUpNotDetected|status={sensor.LastUpdateStatus}");
                                    }

                                    float radians = math.radians(angles[a]);
                                    float3 position = new float3(
                                        math.sin(radians) * distances[d],
                                        0f,
                                        math.cos(radians) * distances[d]);
                                    target.Position = position;
                                    target.LineOfSightPoint = position;
                                    registry.RebuildData();
                                    sensor.UpdateSensor(0f);

                                    bool remembered = memories[m] > 0f && warmDetected;
                                    float range = remembered && loseSights[l] > maxDistance ? loseSights[l] : maxDistance;
                                    bool expected;
                                    if (nearClips[n] > 0f && distances[d] <= nearClips[n])
                                    {
                                        expected = true;
                                    }
                                    else if (distances[d] > range)
                                    {
                                        expected = false;
                                    }
                                    else
                                    {
                                        expected = math.cos(radians) >= cosine;
                                    }

                                    bool actual = HasLiveResult(sensor);
                                    if (actual != expected)
                                    {
                                        failures.Add($"{tag}|expected={expected}|actual={actual}|status={sensor.LastUpdateStatus}");
                                    }
                                }
                                finally
                                {
                                    sensor?.Dispose();
                                    manager.Dispose();
                                    registry.Dispose();
                                }
                            }
                        }
                    }
                }
            }

            EvidenceWriter.Write($"feature.sightRules|cases={cases}|failures={failures.Count}");
            Assert.That(failures.Count, Is.Zero, string.Join("\n", failures.GetRange(0, math.min(10, failures.Count))));
        }

        private static bool HasLiveResult(ISensor sensor)
        {
            for (int i = 0; i < sensor.DetectedCount; i++)
            {
                if (sensor.TryGetResult(i, out DetectionResult result) && !result.IsFromMemory)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void Fix_InlineExecution_MatchesJobPathExactly()
        {
            var signatures = new List<string>();
            int[] thresholds = { 0, 64, 128, 4096 };

            for (int i = 0; i < thresholds.Length; i++)
            {
                signatures.Add(RunThresholdSignature(thresholds[i]));
                EvidenceWriter.Write($"fix.inline|threshold={thresholds[i]}|signatureLength={signatures[i].Length}");
            }

            for (int i = 1; i < signatures.Count; i++)
            {
                Assert.That(
                    signatures[i],
                    Is.EqualTo(signatures[0]),
                    $"Inline threshold {thresholds[i]} changed the result set relative to the job path.");
            }
        }

        private string RunThresholdSignature(int threshold)
        {
            var registry = new PerceptibleRegistry(initialCapacity: 128, maximumCapacity: 256, cellSize: 6f);
            Transform transform = CreateGameObject($"inline-sensor-{threshold}").transform;
            var manager = new SensorManager(registry);
            var sensors = new List<ISensor>(3);
            try
            {
                manager.InlineExecutionThreshold = threshold;

                for (int i = 0; i < 96; i++)
                {
                    registry.Register(new FixPerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3((i % 10) * 1.5f, 0f, (i / 10) * 1.5f),
                        LineOfSightPoint = new float3((i % 10) * 1.5f, 1f, (i / 10) * 1.5f),
                        DetectionRadius = (i % 3) * 0.5f,
                        Loudness = (i % 4) * 0.5f,
                        IsSoundSource = (i % 2) == 0,
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                SightSensorConfig sightConfig = SightSensorConfig.Default;
                sightConfig.HalfAngle = 80f;
                sightConfig.MaxDistance = 20f;
                sightConfig.UpdateInterval = 0f;
                sightConfig.UseLineOfSight = false;
                sightConfig.MemoryDuration = 0f;
                sensors.Add(new SightSensor(transform, sightConfig, manager));

                ProximitySensorConfig proximityConfig = ProximitySensorConfig.Default;
                proximityConfig.Radius = 18f;
                proximityConfig.UpdateInterval = 0f;
                proximityConfig.MemoryDuration = 0f;
                sensors.Add(new ProximitySensor(transform, proximityConfig, manager));

                HearingSensorConfig hearingConfig = HearingSensorConfig.Default;
                hearingConfig.Radius = 18f;
                hearingConfig.UpdateInterval = 0f;
                hearingConfig.UseOcclusion = false;
                hearingConfig.MemoryDuration = 0f;
                sensors.Add(new HearingSensor(transform, hearingConfig, manager));

                var builder = new System.Text.StringBuilder();
                for (int s = 0; s < sensors.Count; s++)
                {
                    sensors[s].UpdateSensor(0f);
                    builder.Append(sensors[s].DetectedCount).Append('|');
                    for (int r = 0; r < sensors[s].DetectedCount; r++)
                    {
                        if (sensors[s].TryGetResult(r, out DetectionResult result))
                        {
                            builder.Append(result.Target.Id).Append(':').Append(Format(result.Distance)).Append(';');
                        }
                    }

                    builder.Append('#');
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

        [Test]
        public void Measure_ResultCommitCost_SeparatesCandidateWorkFromResultWork()
        {
            int[] counts = { 16, 64, 256 };
            for (int c = 0; c < counts.Length; c++)
            {
                int count = counts[c];
                var registry = new PerceptibleRegistry(
                    initialCapacity: count,
                    maximumCapacity: count + 8,
                    cellSize: 4f);
                Transform transform = CreateGameObject($"commit-sweep-{count}").transform;
                var manager = new SensorManager(registry);
                ProximitySensor cappedSensor = null;
                ProximitySensor fullSensor = null;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        registry.Register(new FixPerceptible
                        {
                            PerceptibleId = i,
                            Position = new float3(0f, 0f, 1f + i * 0.2f),
                            LineOfSightPoint = new float3(0f, 0f, 1f + i * 0.2f),
                            DetectionRadius = 0f,
                            IsDetectable = true
                        });
                    }

                    registry.RebuildData();

                    ProximitySensorConfig cappedConfig = ProximitySensorConfig.Default;
                    cappedConfig.Radius = 200f;
                    cappedConfig.UpdateInterval = 0f;
                    cappedConfig.MemoryDuration = 0f;
                    cappedConfig.Capacity = new PerceptionSensorCapacity
                    {
                        InitialCandidateCapacity = count,
                        MaximumCandidates = count,
                        InitialResultCapacity = 1,
                        MaximumResults = 1,
                        InitialMemoryCapacity = 1,
                        MaximumMemoryEntries = 1
                    };
                    cappedSensor = new ProximitySensor(transform, cappedConfig, manager);

                    ProximitySensorConfig fullConfig = cappedConfig;
                    fullConfig.Capacity = new PerceptionSensorCapacity
                    {
                        InitialCandidateCapacity = count,
                        MaximumCandidates = count,
                        InitialResultCapacity = count,
                        MaximumResults = count,
                        InitialMemoryCapacity = 1,
                        MaximumMemoryEntries = 1
                    };
                    fullSensor = new ProximitySensor(transform, fullConfig, manager);

                    cappedSensor.UpdateSensor(0f);
                    fullSensor.UpdateSensor(0f);

                    int iterations = math.max(20, 4096 / count);
                    double cappedMs = TimeSensor(cappedSensor, iterations);
                    double fullMs = TimeSensor(fullSensor, iterations);
                    double delta = fullMs - cappedMs;

                    // Attribute the delta against a managed sort of the same size (a lower bound for
                    // the real comparer-based native sort) and a native append of the same count.
                    double managedSortMs = TimeSortPrimitive(count);
                    double appendMs = TimeNativeAppend(count);

                    EvidenceWriter.Write(
                        $"measure.commit|count={count}|cappedMs={Format(cappedMs)}|fullMs={Format(fullMs)}|deltaMs={Format(delta)}|usPerResult={Format(delta * 1000d / math.max(1, count - 1))}|managedSortLowerBoundMs={Format(managedSortMs)}|nativeAppendMs={Format(appendMs)}|cappedDetected={cappedSensor.DetectedCount}|fullDetected={fullSensor.DetectedCount}");

                    Assert.That(cappedSensor.DetectedCount, Is.EqualTo(1));
                    Assert.That(fullSensor.DetectedCount, Is.EqualTo(count));
                }
                finally
                {
                    cappedSensor?.Dispose();
                    fullSensor?.Dispose();
                    manager.Dispose();
                    registry.Dispose();
                }
            }
        }

        private static double TimeSensor(ISensor sensor, int iterations)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                sensor.UpdateSensor(0f);
            }

            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / iterations;
        }

        private static double TimeSortPrimitive(int count)
        {
            var keys = new ulong[count];
            var order = new int[count];

            const int iterations = 200;
            var watch = Stopwatch.StartNew();
            for (int it = 0; it < iterations; it++)
            {
                // Refill each pass so every sort starts from the same unsorted input; sorting an
                // already ordered array would measure the best case.
                for (int i = 0; i < count; i++)
                {
                    keys[i] = ((ulong)(uint)(count - i) << 32) | (uint)i;
                    order[i] = i;
                }

                Array.Sort(keys, order, 0, count);
            }

            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / iterations;
        }

        private static double TimeNativeAppend(int count)
        {
            var list = new Unity.Collections.NativeList<DetectionResult>(count, Unity.Collections.Allocator.Temp);
            try
            {
                var value = new DetectionResult { Distance = 1f, Visibility = 1f };
                const int iterations = 200;
                var watch = Stopwatch.StartNew();
                for (int it = 0; it < iterations; it++)
                {
                    list.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        list.Add(value);
                    }
                }

                watch.Stop();
                return watch.Elapsed.TotalMilliseconds / iterations;
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Fix_InlineExecution_RemovesPerSensorSchedulingOverhead()
        {
            int[] thresholds = { 0, 64 };
            var measured = new double[thresholds.Length];
            var registry = new PerceptibleRegistry(initialCapacity: 64, maximumCapacity: 256, cellSize: 20f);
            try
            {
                registry.Register(new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(1f, 0f, 1f),
                    LineOfSightPoint = new float3(1f, 0f, 1f),
                    DetectionRadius = 0.5f,
                    IsDetectable = true
                });
                registry.RebuildData();

                for (int t = 0; t < thresholds.Length; t++)
                {
                    const int sensorCount = 200;
                    var manager = new SensorManager(registry, sensorCount + 8);
                    manager.InlineExecutionThreshold = thresholds[t];
                    var sensors = new ProximitySensor[sensorCount];
                    try
                    {
                        for (int i = 0; i < sensorCount; i++)
                        {
                            ProximitySensorConfig config = ProximitySensorConfig.Default;
                            config.Radius = 12f;
                            config.UpdateInterval = 0f;
                            config.MemoryDuration = 0f;
                            sensors[i] = new ProximitySensor(
                                CreateGameObject($"inline-perf-{t}-{i}").transform,
                                config,
                                manager);
                            manager.TryRegister(sensors[i]);
                        }

                        manager.Update(0f);
                        manager.LateUpdate();
                        manager.Update(0f);
                        manager.LateUpdate();

                        const int iterations = 20;
                        var watch = Stopwatch.StartNew();
                        for (int it = 0; it < iterations; it++)
                        {
                            manager.Update(0f);
                            manager.LateUpdate();
                        }

                        watch.Stop();
                        measured[t] = watch.Elapsed.TotalMilliseconds / iterations;
                        EvidenceWriter.Write(
                            $"fix.inlinePerf|threshold={thresholds[t]}|sensors={sensorCount}|msPerFrame={Format(measured[t])}|usPerSensor={Format(measured[t] * 1000d / sensorCount)}");
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

                double jobPathMs = measured[0];
                double inlineMs = measured[1];
                EvidenceWriter.Write(
                    $"fix.inlinePerf|jobPathMs={Format(jobPathMs)}|inlineMs={Format(inlineMs)}|speedup={Format(jobPathMs / math.max(inlineMs, 1e-6d))}");

                // Relative gate: machine independent, still impossible to satisfy without removing
                // the per-sensor Schedule/Complete pair.
                Assert.That(inlineMs * 2d, Is.LessThan(jobPathMs), "Inline execution did not at least halve the per-sensor cost.");
            }
            finally
            {
                registry.Dispose();
            }
        }

        // ====================================================================================
        // New contracts
        // ====================================================================================

        [Test]
        public void Feature_NearClippingRadius_AddsPointBlankAwareness()
        {
            float[] nearClip = { 0f, 1f };
            var detected = new bool[nearClip.Length];

            for (int i = 0; i < nearClip.Length; i++)
            {
                var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
                Transform transform = CreateGameObject($"nearclip-sensor-{i}").transform;
                var manager = new SensorManager(registry);
                SightSensor sensor = null;
                try
                {
                    registry.Register(new FixPerceptible
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
                    config.NearClippingRadius = nearClip[i];
                    sensor = new SightSensor(transform, config, manager);
                    sensor.UpdateSensor(0f);
                    detected[i] = sensor.HasDetection;
                    EvidenceWriter.Write(
                        $"feature.nearClip|radius={nearClip[i]}|lateralDistance=0.3|detected={detected[i]}");
                }
                finally
                {
                    sensor?.Dispose();
                    manager.Dispose();
                    registry.Dispose();
                }
            }

            Assert.That(detected[0], Is.False, "Zero near clipping must preserve the pre-fix blind spot.");
            Assert.That(detected[1], Is.True, "A non-zero near clipping radius must sense a point-blank lateral target.");
        }

        [Test]
        public void Feature_LoseSightRadius_KeepsRememberedTargetsVisibleBeyondSightRange()
        {
            const float sightRange = 10f;
            const float loseSightRange = 40f;
            var failures = new List<string>();

            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            Transform transform = CreateGameObject("losesight-sensor").transform;
            var manager = new SensorManager(registry);
            SightSensor sensor = null;
            try
            {
                var target = new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 5f),
                    LineOfSightPoint = new float3(0f, 0f, 5f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                };
                registry.Register(target);
                registry.RebuildData();

                SightSensorConfig config = SightSensorConfig.Default;
                config.HalfAngle = 60f;
                config.MaxDistance = sightRange;
                config.LoseSightRadius = loseSightRange;
                config.UpdateInterval = 0f;
                config.UseLineOfSight = false;
                config.MemoryDuration = 10f;
                sensor = new SightSensor(transform, config, manager);

                sensor.UpdateSensor(0f);
                bool firstLive = sensor.DetectedCount == 1 && sensor.TryGetResult(0, out DetectionResult initial) &&
                                 !initial.IsFromMemory;
                if (!firstLive)
                {
                    failures.Add($"step1|count={sensor.DetectedCount}|status={sensor.LastUpdateStatus}");
                }

                target.Position = new float3(0f, 0f, 25f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                bool remembered = sensor.DetectedCount == 1 &&
                                  sensor.TryGetResult(0, out DetectionResult extended) &&
                                  !extended.IsFromMemory &&
                                  math.abs(extended.Distance - 25f) < 0.01f;
                if (!remembered)
                {
                    failures.Add(
                        $"step2|count={sensor.DetectedCount}|status={sensor.LastUpdateStatus}|fromMemory={(sensor.DetectedCount > 0 && sensor.TryGetResult(0, out DetectionResult d) ? d.IsFromMemory : false)}");
                }

                EvidenceWriter.Write(
                    $"feature.loseSight|sightRange={sightRange}|loseSightRange={loseSightRange}|distance=25|liveAfterMove={remembered}|failures={failures.Count}");

                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_LoseSightRadius_DoesNotWidenRangeWithoutMemory()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            Transform transform = CreateGameObject("losesight-cold-sensor").transform;
            var manager = new SensorManager(registry);
            SightSensor sensor = null;
            try
            {
                registry.Register(new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 25f),
                    LineOfSightPoint = new float3(0f, 0f, 25f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                });
                registry.RebuildData();

                SightSensorConfig config = SightSensorConfig.Default;
                config.HalfAngle = 60f;
                config.MaxDistance = 10f;
                config.LoseSightRadius = 40f;
                config.UpdateInterval = 0f;
                config.UseLineOfSight = false;
                config.MemoryDuration = 10f;
                sensor = new SightSensor(transform, config, manager);
                sensor.UpdateSensor(0f);

                EvidenceWriter.Write(
                    $"feature.loseSightCold|distance=25|detected={sensor.HasDetection}|count={sensor.DetectedCount}");
                Assert.That(sensor.HasDetection, Is.False, "Lose-sight range must not apply to targets that were never seen.");
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_PerceptionClock_DrivesDeterministicStimulusAging()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            Transform transform = CreateGameObject("clock-sensor").transform;
            var clock = new ManualClock { NowSeconds = 100d };
            var manager = new SensorManager(registry, SensorManager.DefaultMaximumSensorCount, clock);
            ProximitySensor sensor = null;
            try
            {
                var target = new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 2f),
                    LineOfSightPoint = new float3(0f, 0f, 2f),
                    DetectionRadius = 0f,
                    IsDetectable = true
                };
                registry.Register(target);
                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 10f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 2f;
                sensor = new ProximitySensor(transform, config, manager);

                sensor.UpdateSensor(0f);
                float live = sensor.TryGetResult(0, out DetectionResult liveResult) ? liveResult.Visibility : -1f;

                target.Position = new float3(0f, 0f, 500f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();

                clock.NowSeconds = 101d;
                sensor.UpdateSensor(0f);
                float halfAge = sensor.TryGetResult(0, out DetectionResult halfResult) ? halfResult.Visibility : -1f;
                bool halfIsMemory = sensor.TryGetResult(0, out DetectionResult halfFlag) && halfFlag.IsFromMemory;

                clock.NowSeconds = 103d;
                sensor.UpdateSensor(0f);
                int expiredCount = sensor.DetectedCount;
                int memoryCount = sensor.MemoryCount;

                EvidenceWriter.Write(
                    $"feature.clock|live={Format(live)}|halfAge={Format(halfAge)}|halfIsMemory={halfIsMemory}|expiredCount={expiredCount}|memoryCount={memoryCount}");

                Assert.That(live, Is.GreaterThan(0f));
                Assert.That(halfIsMemory, Is.True);
                Assert.That(halfAge, Is.EqualTo(live * 0.5f).Within(0.02f));
                Assert.That(expiredCount, Is.Zero, "Stimulus must expire exactly at MemoryDuration under an injected clock.");
                Assert.That(memoryCount, Is.Zero);
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Fix_RegistryDedupe_UsesIdentityAndInvalidatesOnReuse()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 16, cellSize: 20f);
            try
            {
                var target = new FixPerceptible { PerceptibleId = 1, IsDetectable = true };
                PerceptibleHandle first = registry.Register(target);
                PerceptibleHandle second = registry.Register(target);

                Assert.That(second, Is.EqualTo(first), "Registering the same instance twice must return the same handle.");
                Assert.That(registry.Count, Is.EqualTo(1));

                Assert.That(registry.Unregister(first), Is.True);
                Assert.That(registry.IsValid(first), Is.False);
                Assert.That(registry.Count, Is.Zero);
                Assert.That(registry.Unregister(first), Is.False, "Double unregister must be rejected.");

                PerceptibleHandle reRegistered = registry.Register(target);
                Assert.That(reRegistered.Id, Is.EqualTo(first.Id), "The freed slot should be reused.");
                Assert.That(reRegistered.Generation, Is.Not.EqualTo(first.Generation));
                Assert.That(registry.IsValid(reRegistered), Is.True);

                var other = new FixPerceptible { PerceptibleId = 2, IsDetectable = true };
                Assert.That(registry.Register(other).Id, Is.Not.EqualTo(reRegistered.Id));
                Assert.That(registry.Count, Is.EqualTo(2));

                EvidenceWriter.Write(
                    $"fix.dedupe|firstGeneration={first.Generation}|reusedGeneration={reRegistered.Generation}|count={registry.Count}");
            }
            finally
            {
                registry.Dispose();
            }
        }

        [Test]
        public void Fix_DriverResolution_DoesNotSpawnHiddenGameObjects()
        {
            int before = UnityEngine.Object.FindObjectsOfType<PerceptionManagerComponent>(true).Length;
            PerceptionManagerComponent instance = PerceptionManagerComponent.Instance;
            int after = UnityEngine.Object.FindObjectsOfType<PerceptionManagerComponent>(true).Length;

            EvidenceWriter.Write(
                $"fix.noImplicitSpawn|before={before}|after={after}|created={after - before}|instanceNull={instance == null}");

            Assert.That(after, Is.EqualTo(before), "Reading Instance must never create a hidden driver object.");
        }

        // ====================================================================================
        // Helpers
        // ====================================================================================

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static string Format(double value)
        {
            return value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class ManualClock : IPerceptionClock
        {
            public double NowSeconds { get; set; }
        }

        private sealed class FixPerceptible : IPerceptible
        {
            public int PerceptibleId { get; set; }
            public int PerceptibleTypeId { get; set; }
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
            private static string _path;

            public static void Write(string line)
            {
                Debug.Log("EVIDENCE|" + line);
                try
                {
                    if (_path == null)
                    {
                        string root = System.IO.Path.GetFullPath(
                            System.IO.Path.Combine(Application.dataPath, "..", "..", "Temp~", "aiperception-evidence"));
                        System.IO.Directory.CreateDirectory(root);
                        _path = System.IO.Path.Combine(root, "evidence-fixes.txt");
                        if (System.IO.File.Exists(_path))
                        {
                            System.IO.File.Delete(_path);
                        }
                    }

                    System.IO.File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch
                {
                    _path = null;
                }
            }
        }
    }
}
