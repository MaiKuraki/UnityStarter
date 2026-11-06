using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.AIPerception.Runtime.Jobs;
using UnityEditor;
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

        private sealed class TestClock : IPerceptionClock
        {
            public double NowSeconds { get; set; }
        }

        [Test]
        public void Feature_TeamAttitudeAsset_NormalizeRepairsDuplicateAndInvalidRelations()
        {
            var asset = ScriptableObject.CreateInstance<PerceptionTeamAttitudeAsset>();
            try
            {
                // Reproduces a hand-edited asset: three entries for the same pair plus one out-of-range
                // team. Without normalisation the cache is last-wins and the list keeps duplicate rows.
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("_sameTeamFriendly").boolValue = true;
                SerializedProperty relations = serialized.FindProperty("_relations");
                relations.arraySize = 4;
                WriteRelation(relations.GetArrayElementAtIndex(0), 0, 0, PerceptionTeamAttitude.Friendly);
                WriteRelation(relations.GetArrayElementAtIndex(1), 0, 0, PerceptionTeamAttitude.Neutral);
                WriteRelation(relations.GetArrayElementAtIndex(2), 0, 0, PerceptionTeamAttitude.Hostile);
                WriteRelation(relations.GetArrayElementAtIndex(3), 999, 0, PerceptionTeamAttitude.Hostile);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // OnValidate already repaired the list when the serialized object was applied, so the
                // four hand-written rows are observed only through their collapsed result.
                int beforeNormalize = asset.RelationCount;
                asset.Normalize();
                int after = asset.RelationCount;
                PerceptionTeamAttitude repaired = asset.GetAttitude(0, 0);

                EvidenceWriter.Write(
                    $"feature.teamAssetNormalize|afterApply={beforeNormalize}|afterNormalize={after}|cell00={repaired}");

                Assert.That(beforeNormalize, Is.EqualTo(1), "OnValidate must collapse duplicates on load");
                Assert.That(after, Is.EqualTo(1), "duplicates and out-of-range teams must collapse to one override");
                Assert.That(asset.Relations[0].ObserverTeam, Is.Zero);
                Assert.That(asset.Relations[0].TargetTeam, Is.Zero);
                Assert.That(asset.Relations[0].Attitude, Is.EqualTo(PerceptionTeamAttitude.Hostile), "last write wins");
                Assert.That(repaired, Is.EqualTo(PerceptionTeamAttitude.Hostile));

                asset.Normalize();
                Assert.That(asset.RelationCount, Is.EqualTo(1), "normalising twice must be a no-op");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static void WriteRelation(
            SerializedProperty element,
            int observer,
            int target,
            PerceptionTeamAttitude attitude)
        {
            element.FindPropertyRelative("ObserverTeam").intValue = observer;
            element.FindPropertyRelative("TargetTeam").intValue = target;
            element.FindPropertyRelative("Attitude").intValue = (int)attitude;
        }

        [Test]
        public void Feature_TeamAttitudeAsset_CapturesMinimalRelationList()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 8, cellSize: 20f);
            var manager = new SensorManager(registry);
            var asset = ScriptableObject.CreateInstance<PerceptionTeamAttitudeAsset>();
            try
            {
                manager.SetSameTeamFriendly();
                manager.SetTeamAttitude(0, 1, PerceptionTeamAttitude.Hostile);

                asset.CaptureFrom(manager);

                // Policy inference should absorb the 256 diagonal cells into SameTeamFriendly and keep
                // only the real override, otherwise a two-faction table serializes 257 rows.
                EvidenceWriter.Write(
                    $"feature.teamAssetMinimal|relations={asset.RelationCount}|sameFriendly={asset.SameTeamFriendly}" +
                    $"|default={asset.DefaultAttitude}|cell01={asset.GetAttitude(0, 1)}|cell00={asset.GetAttitude(0, 0)}");

                Assert.That(asset.RelationCount, Is.EqualTo(1));
                Assert.That(asset.SameTeamFriendly, Is.True);
                Assert.That(asset.DefaultAttitude, Is.EqualTo(PerceptionTeamAttitude.Neutral));
                Assert.That(asset.GetAttitude(0, 1), Is.EqualTo(PerceptionTeamAttitude.Hostile));
                Assert.That(asset.GetAttitude(0, 0), Is.EqualTo(PerceptionTeamAttitude.Friendly));

                manager.FillTeamAttitude(PerceptionTeamAttitude.Hostile);
                var hostile = ScriptableObject.CreateInstance<PerceptionTeamAttitudeAsset>();
                try
                {
                    hostile.CaptureFrom(manager);
                    EvidenceWriter.Write(
                        $"feature.teamAssetAllHostile|relations={hostile.RelationCount}|default={hostile.DefaultAttitude}");
                    Assert.That(hostile.RelationCount, Is.Zero, "a uniform table must serialize as policy only");
                    Assert.That(hostile.DefaultAttitude, Is.EqualTo(PerceptionTeamAttitude.Hostile));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hostile);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_TeamAttitudeAsset_DefaultsToNeutralAndRoundTrips()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 8, cellSize: 20f);
            var manager = new SensorManager(registry);
            var asset = ScriptableObject.CreateInstance<PerceptionTeamAttitudeAsset>();
            try
            {
                // A freshly created asset must be neutral, not friendly: byte defaults to zero and
                // zero is Friendly in the attitude enum.
                int freshNeutralPairs = 0;
                for (int team = 0; team < PerceptionTeam.TeamCount; team++)
                {
                    if (asset.GetAttitude(team, team) == PerceptionTeamAttitude.Neutral)
                    {
                        freshNeutralPairs++;
                    }
                }

                asset.SetSameTeamFriendly();
                asset.SetAttitude(0, 2, PerceptionTeamAttitude.Hostile);
                asset.ApplyTo(manager);

                PerceptionTeamAttitude self = manager.GetTeamAttitude(0, 0);
                PerceptionTeamAttitude untouched = manager.GetTeamAttitude(0, 1);
                PerceptionTeamAttitude hostile = manager.GetTeamAttitude(0, 2);

                var captured = ScriptableObject.CreateInstance<PerceptionTeamAttitudeAsset>();
                try
                {
                    captured.CaptureFrom(manager);
                    PerceptionTeamAttitude capturedHostile = captured.GetAttitude(0, 2);
                    PerceptionTeamAttitude capturedNeutral = captured.GetAttitude(0, 1);
                    PerceptionTeamAttitude capturedSelf = captured.GetAttitude(1, 1);

                    EvidenceWriter.Write(
                        $"feature.teamAsset|freshNeutral={freshNeutralPairs}|self={self}|neutral={untouched}|hostile={hostile}" +
                        $"|roundTrip=({capturedHostile},{capturedNeutral},{capturedSelf})");

                    Assert.That(freshNeutralPairs, Is.EqualTo(PerceptionTeam.TeamCount), "a new asset must be all-neutral");
                    Assert.That(self, Is.EqualTo(PerceptionTeamAttitude.Friendly));
                    Assert.That(untouched, Is.EqualTo(PerceptionTeamAttitude.Neutral));
                    Assert.That(hostile, Is.EqualTo(PerceptionTeamAttitude.Hostile));
                    Assert.That(capturedHostile, Is.EqualTo(PerceptionTeamAttitude.Hostile));
                    Assert.That(capturedNeutral, Is.EqualTo(PerceptionTeamAttitude.Neutral));
                    Assert.That(capturedSelf, Is.EqualTo(PerceptionTeamAttitude.Friendly));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(captured);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_DamageSensor_ConsumesReportedStimulusOnce()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            var clock = new TestClock { NowSeconds = 100d };
            var manager = new SensorManager(registry, 64, clock);
            Transform transform = CreateGameObject("damage-sensor").transform;
            DamageSensor sensor = null;
            try
            {
                var instigator = new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 3f),
                    LineOfSightPoint = new float3(0f, 0f, 3f),
                    IsDetectable = true
                };
                PerceptibleHandle handle = registry.Register(instigator);
                registry.RebuildData();

                DamageSensorConfig config = DamageSensorConfig.Default;
                config.MaxDistance = 25f;
                config.MemoryDuration = 0f;
                sensor = new DamageSensor(transform, config, manager);
                manager.TryRegister(sensor);

                sensor.UpdateSensor(0f);
                int beforeReport = sensor.DetectedCount;

                manager.ReportStimulus(handle, new float3(0f, 0f, 5f), 40f);
                sensor.UpdateSensor(0f);
                int afterReport = sensor.DetectedCount;
                bool hasResult = sensor.TryGetResult(0, out DetectionResult result);

                sensor.UpdateSensor(0f);
                int afterSecondUpdate = sensor.DetectedCount;

                EvidenceWriter.Write(
                    $"feature.damage|before={beforeReport}|after={afterReport}|second={afterSecondUpdate}" +
                    $"|distance={(hasResult ? result.Distance : -1f):F2}|type={(hasResult ? result.SensorType.ToString() : "none")}");

                Assert.That(beforeReport, Is.Zero);
                Assert.That(afterReport, Is.EqualTo(1));
                Assert.That(hasResult, Is.True);
                Assert.That(result.Target, Is.EqualTo(handle));
                Assert.That(result.SensorType, Is.EqualTo(SensorType.Damage));
                Assert.That(result.Distance, Is.EqualTo(5f).Within(0.01f));
                Assert.That(result.Visibility, Is.EqualTo(0.8f).Within(0.01f));
                Assert.That(afterSecondUpdate, Is.Zero);
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_DamageSensor_RejectsOutOfRangeAndWeakStimuli()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            var clock = new TestClock { NowSeconds = 200d };
            var manager = new SensorManager(registry, 64, clock);
            Transform transform = CreateGameObject("damage-filter").transform;
            DamageSensor sensor = null;
            try
            {
                var instigator = new FixPerceptible
                {
                    PerceptibleId = 1,
                    Position = new float3(0f, 0f, 3f),
                    IsDetectable = true
                };
                PerceptibleHandle handle = registry.Register(instigator);
                registry.RebuildData();

                DamageSensorConfig config = DamageSensorConfig.Default;
                config.MaxDistance = 10f;
                config.MinimumStrength = 5f;
                config.MemoryDuration = 0f;
                sensor = new DamageSensor(transform, config, manager);
                manager.TryRegister(sensor);

                manager.ReportStimulus(handle, new float3(0f, 0f, 40f), 100f);
                sensor.UpdateSensor(0f);
                int farCount = sensor.DetectedCount;

                manager.ReportStimulus(handle, new float3(0f, 0f, 2f), 1f);
                sensor.UpdateSensor(0f);
                int weakCount = sensor.DetectedCount;

                manager.ReportStimulus(handle, new float3(0f, 0f, 2f), 50f);
                sensor.UpdateSensor(0f);
                int validCount = sensor.DetectedCount;

                EvidenceWriter.Write($"feature.damageFilter|far={farCount}|weak={weakCount}|valid={validCount}");

                Assert.That(farCount, Is.Zero);
                Assert.That(weakCount, Is.Zero);
                Assert.That(validCount, Is.EqualTo(1));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_Affiliation_ResolvesAttitudeThroughManagerTable()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 32, cellSize: 20f);
            var manager = new SensorManager(registry);
            Transform transform = CreateGameObject("affiliation-sensor").transform;
            ProximitySensor sensor = null;
            try
            {
                // Team 1 is the observer. Team 2 is an enemy, team 3 is neutral, team 4 is friendly.
                for (int i = 1; i <= 4; i++)
                {
                    registry.Register(new FixPerceptible
                    {
                        PerceptibleId = i,
                        TeamId = i,
                        Position = new float3(0f, 0f, i),
                        IsDetectable = true
                    });
                }

                registry.RebuildData();
                manager.SetSameTeamFriendly();
                manager.SetTeamAttitude(1, 2, PerceptionTeamAttitude.Hostile);
                manager.SetTeamAttitude(1, 4, PerceptionTeamAttitude.Friendly);

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 50f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                config.ObserverTeamId = 1;
                config.Affiliation = PerceptionAffiliation.Enemies;

                sensor = new ProximitySensor(transform, config, manager);
                manager.TryRegister(sensor);
                sensor.UpdateSensor(0f);
                int enemiesOnly = sensor.DetectedCount;
                int enemyTeam = sensor.TryGetResult(0, out DetectionResult first)
                    ? registry.Get(first.Target) is IPerceptionTeamProvider teamProvider ? teamProvider.TeamId : -1
                    : -1;

                config.Affiliation = PerceptionAffiliation.Friendlies;
                sensor.ApplyConfig(config);
                sensor.UpdateSensor(0f);
                int friendliesOnly = sensor.DetectedCount;

                config.Affiliation = PerceptionAffiliation.All;
                sensor.ApplyConfig(config);
                sensor.UpdateSensor(0f);
                int all = sensor.DetectedCount;

                EvidenceWriter.Write(
                    $"feature.affiliation|enemies={enemiesOnly}|enemyTeam={enemyTeam}|friendlies={friendliesOnly}|all={all}");

                Assert.That(enemiesOnly, Is.EqualTo(1));
                Assert.That(enemyTeam, Is.EqualTo(2));
                // Team 1's own target counts as friendly too, so friendlies are the self target and team 4.
                Assert.That(friendliesOnly, Is.EqualTo(2));
                Assert.That(all, Is.EqualTo(4));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_Affiliation_SupportsAsymmetricRelations()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 32, cellSize: 20f);
            var manager = new SensorManager(registry);
            Transform teamA = CreateGameObject("observer-a").transform;
            Transform teamB = CreateGameObject("observer-b").transform;
            ProximitySensor sensorA = null;
            ProximitySensor sensorB = null;
            try
            {
                registry.Register(new FixPerceptible
                {
                    PerceptibleId = 1,
                    TeamId = 2,
                    Position = new float3(0f, 0f, 2f),
                    IsDetectable = true
                });
                registry.RebuildData();

                // A regards B as hostile; B regards A as friendly.
                manager.SetTeamAttitude(1, 2, PerceptionTeamAttitude.Hostile);
                manager.SetTeamAttitude(2, 1, PerceptionTeamAttitude.Friendly);

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 50f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                config.Affiliation = PerceptionAffiliation.Enemies;
                config.ObserverTeamId = 1;
                sensorA = new ProximitySensor(teamA, config, manager);
                manager.TryRegister(sensorA);

                config.ObserverTeamId = 2;
                sensorB = new ProximitySensor(teamB, config, manager);
                manager.TryRegister(sensorB);

                sensorA.UpdateSensor(0f);
                sensorB.UpdateSensor(0f);

                EvidenceWriter.Write($"feature.affiliationAsymmetry|a={sensorA.DetectedCount}|b={sensorB.DetectedCount}");
                Assert.That(sensorA.DetectedCount, Is.EqualTo(1), "A must see B as hostile");
                Assert.That(sensorB.DetectedCount, Is.Zero, "B must see A as friendly");
            }
            finally
            {
                sensorA?.Dispose();
                sensorB?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_Affiliation_TreatsNoTeamAndMissingTableAsNeutral()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 32, cellSize: 20f);
            var manager = new SensorManager(registry);
            Transform transform = CreateGameObject("no-team-sensor").transform;
            ProximitySensor sensor = null;
            try
            {
                registry.Register(new FixPerceptible
                {
                    PerceptibleId = 1,
                    TeamId = PerceptionTeam.NoTeam,
                    Position = new float3(0f, 0f, 2f),
                    IsDetectable = true
                });
                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 50f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                config.ObserverTeamId = 1;

                // No table written yet: every pair is neutral, so an enemy-only sense sees nothing and
                // a neutral-only sense sees the target. Only filtering sensors materialise the table.
                bool tableBeforeUpdate = manager.HasTeamAttitudeTable;

                config.Affiliation = PerceptionAffiliation.Enemies;
                sensor = new ProximitySensor(transform, config, manager);
                manager.TryRegister(sensor);
                sensor.UpdateSensor(0f);
                int enemiesWithoutTable = sensor.DetectedCount;
                bool tableAllocatedOnFilteredUpdate = manager.HasTeamAttitudeTable;

                config.Affiliation = PerceptionAffiliation.Neutrals;
                sensor.ApplyConfig(config);
                sensor.UpdateSensor(0f);
                int neutralsWithoutTable = sensor.DetectedCount;

                // Out-of-range team ids collapse to NoTeam instead of aliasing into a real team.
                var clamped = new FixPerceptible { PerceptibleId = 2, TeamId = 9000, IsDetectable = true };
                PerceptibleHandle clampedHandle = registry.Register(clamped);
                registry.RebuildData();
                int clampedTeam = -1;
                using (var snapshot = registry.CreateNativeDataCopy(Unity.Collections.Allocator.Temp))
                {
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        if (snapshot[i].Id == clampedHandle.Id)
                        {
                            clampedTeam = snapshot[i].TeamId;
                        }
                    }
                }

                EvidenceWriter.Write(
                    $"feature.affiliationNoTeam|enemies={enemiesWithoutTable}|neutrals={neutralsWithoutTable}" +
                    $"|tableBefore={tableBeforeUpdate}|tableAfterFiltered={tableAllocatedOnFilteredUpdate}" +
                    $"|clampedTeam={clampedTeam}");

                Assert.That(enemiesWithoutTable, Is.Zero);
                Assert.That(neutralsWithoutTable, Is.EqualTo(1));
                Assert.That(tableBeforeUpdate, Is.False, "a manager must not allocate the table on construction");
                Assert.That(tableAllocatedOnFilteredUpdate, Is.True, "a filtering sensor must materialise the table");
                Assert.That(clampedTeam, Is.EqualTo(PerceptionTeam.NoTeam));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_AffiliationFuzz_MatchesAttitudeTable()
        {
            var random = new System.Random(20261107);
            var registry = new PerceptibleRegistry(initialCapacity: 128, maximumCapacity: 256, cellSize: 8f);
            var manager = new SensorManager(registry);
            Transform transform = CreateGameObject("affiliation-fuzz").transform;
            const int targetCount = 96;
            var teams = new int[targetCount];
            var positions = new float3[targetCount];
            ProximitySensor sensor = null;
            int cases = 0;
            int mismatches = 0;
            try
            {
                for (int i = 0; i < targetCount; i++)
                {
                    teams[i] = random.Next(0, 6);
                    positions[i] = new float3(
                        (float)(random.NextDouble() * 40d - 20d),
                        0f,
                        (float)(random.NextDouble() * 40d - 20d));
                    registry.Register(new FixPerceptible
                    {
                        PerceptibleId = i,
                        TeamId = teams[i],
                        Position = positions[i],
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                // Random but fixed attitude matrix over six teams, so the reference can read it back.
                var table = new PerceptionTeamAttitude[6, 6];
                for (int a = 0; a < 6; a++)
                {
                    for (int b = 0; b < 6; b++)
                    {
                        var attitude = (PerceptionTeamAttitude)random.Next(0, 3);
                        table[a, b] = attitude;
                        manager.SetTeamAttitude(a, b, attitude);
                    }
                }

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 0f;
                sensor = new ProximitySensor(transform, config, manager);
                manager.TryRegister(sensor);

                for (int iteration = 0; iteration < 200; iteration++)
                {
                    int observerTeam = random.Next(0, 6);
                    var affiliation = (PerceptionAffiliation)random.Next(0, 8);
                    float radius = 5f + (float)random.NextDouble() * 20f;
                    config.ObserverTeamId = (byte)observerTeam;
                    config.Affiliation = affiliation;
                    config.Radius = radius;
                    sensor.ApplyConfig(config);
                    sensor.UpdateSensor(0f);

                    int expected = 0;
                    float radiusSq = radius * radius;
                    for (int i = 0; i < targetCount; i++)
                    {
                        int attitude = (int)table[observerTeam, teams[i]];
                        if (((int)affiliation & (1 << attitude)) == 0)
                        {
                            continue;
                        }

                        float distSq = math.distancesq(positions[i], float3.zero);
                        if (distSq > radiusSq)
                        {
                            continue;
                        }

                        // The job drops anything above 99% of the radius, so the reference must too.
                        if (1f - math.sqrt(distSq) / radius < 0.01f)
                        {
                            continue;
                        }

                        expected++;
                    }

                    cases++;
                    if (sensor.DetectedCount != expected)
                    {
                        mismatches++;
                    }
                }

                EvidenceWriter.Write($"feature.affiliationFuzz|cases={cases}|targets={targetCount}|mismatches={mismatches}");
                Assert.That(mismatches, Is.Zero, "affiliation result diverged from the attitude table");
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_AffiliationSoak_RotatingFiltersStayStable()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 2048, maximumCapacity: 8192, cellSize: 8f);
            var manager = new SensorManager(registry);
            Transform transform = CreateGameObject("affiliation-soak").transform;
            const int targetCount = 4000;
            const int updates = 600;
            ProximitySensor sensor = null;
            var expectedByAffiliation = new System.Collections.Generic.Dictionary<PerceptionAffiliation, int>();
            PerceptionAffiliation[] filters =
            {
                PerceptionAffiliation.Enemies,
                PerceptionAffiliation.Friendlies,
                PerceptionAffiliation.Enemies | PerceptionAffiliation.Neutrals,
                PerceptionAffiliation.All
            };
            try
            {
                int side = (int)math.ceil(math.sqrt((float)targetCount));
                for (int i = 0; i < targetCount; i++)
                {
                    registry.Register(new FixPerceptible
                    {
                        PerceptibleId = i,
                        TeamId = i % 4,
                        Position = new float3((i % side) * 2f, 0f, (i / side) * 2f),
                        IsDetectable = true
                    });
                }

                registry.RebuildData();

                // Observer is team 0: team 1 hostile, team 2 friendly, team 3 neutral, team 0 friendly.
                manager.SetSameTeamFriendly();
                manager.SetTeamAttitude(0, 1, PerceptionTeamAttitude.Hostile);
                manager.SetTeamAttitude(0, 2, PerceptionTeamAttitude.Friendly);
                manager.SetTeamAttitude(0, 3, PerceptionTeamAttitude.Neutral);

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 30f;
                config.UpdateInterval = 0f;
                // Memory off so a filter change takes effect immediately; memory interplay has its own tests.
                config.MemoryDuration = 0f;
                config.ObserverTeamId = 0;
                config.Capacity = new PerceptionSensorCapacity
                {
                    InitialCandidateCapacity = 256,
                    MaximumCandidates = 8192,
                    InitialResultCapacity = 256,
                    MaximumResults = 4096,
                    InitialMemoryCapacity = 256,
                    MaximumMemoryEntries = 4096
                };
                sensor = new ProximitySensor(transform, config, manager);
                manager.TryRegister(sensor);

                // Reference counts from one unfiltered pass.
                config.Affiliation = PerceptionAffiliation.All;
                sensor.ApplyConfig(config);
                sensor.UpdateSensor(0f);
                var handles = new System.Collections.Generic.List<(PerceptibleHandle handle, int team)>(sensor.DetectedCount);
                for (int i = 0; i < sensor.DetectedCount; i++)
                {
                    if (sensor.TryGetResult(i, out DetectionResult result))
                    {
                        handles.Add((result.Target, registry.Get(result.Target) is IPerceptionTeamProvider rowProvider ? rowProvider.TeamId : PerceptionTeam.NoTeam));
                    }
                }

                foreach (PerceptionAffiliation filter in filters)
                {
                    int count = 0;
                    foreach ((PerceptibleHandle handle, int team) in handles)
                    {
                        var attitude = (int)manager.GetTeamAttitude(0, team);
                        if (((int)filter & (1 << attitude)) != 0)
                        {
                            count++;
                        }
                    }

                    expectedByAffiliation[filter] = count;
                }

                long heapBefore = GC.GetTotalMemory(true);
                int mismatches = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < updates; i++)
                {
                    PerceptionAffiliation filter = filters[(i / 25) % filters.Length];
                    if (config.Affiliation != filter)
                    {
                        config.Affiliation = filter;
                        sensor.ApplyConfig(config);
                    }

                    sensor.UpdateSensor(0.016f);
                    if (sensor.DetectedCount != expectedByAffiliation[filter])
                    {
                        mismatches++;
                    }
                }

                watch.Stop();
                long heapAfter = GC.GetTotalMemory(true);
                EvidenceWriter.Write(
                    $"feature.affiliationSoak|updates={updates}|targets={targetCount}|mismatches={mismatches}" +
                    $"|msPerUpdate={watch.Elapsed.TotalMilliseconds / updates:F3}|heapDelta={heapAfter - heapBefore}");

                Assert.That(mismatches, Is.Zero, "rotating affiliation filters diverged from the attitude table");
                Assert.That(heapAfter - heapBefore, Is.LessThan(256 * 1024));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void Feature_DamageSensor_RespectsAffiliation()
        {
            var registry = new PerceptibleRegistry(initialCapacity: 4, maximumCapacity: 16, cellSize: 20f);
            var clock = new TestClock { NowSeconds = 400d };
            var manager = new SensorManager(registry, 64, clock);
            Transform transform = CreateGameObject("damage-affiliation").transform;
            DamageSensor sensor = null;
            try
            {
                var instigator = new FixPerceptible
                {
                    PerceptibleId = 1,
                    TeamId = 5,
                    Position = new float3(0f, 0f, 3f),
                    IsDetectable = true
                };
                PerceptibleHandle handle = registry.Register(instigator);
                registry.RebuildData();
                manager.SetTeamAttitude(1, 5, PerceptionTeamAttitude.Hostile);

                DamageSensorConfig config = DamageSensorConfig.Default;
                config.MaxDistance = 25f;
                config.MemoryDuration = 0f;
                config.ObserverTeamId = 1;
                config.Affiliation = PerceptionAffiliation.Friendlies;
                sensor = new DamageSensor(transform, config, manager);
                manager.TryRegister(sensor);

                manager.ReportStimulus(handle, new float3(0f, 0f, 2f), 30f);
                sensor.UpdateSensor(0f);
                int filteredOut = sensor.DetectedCount;

                config.Affiliation = PerceptionAffiliation.Enemies;
                sensor.ApplyConfig(config);
                manager.ReportStimulus(handle, new float3(0f, 0f, 2f), 30f);
                sensor.UpdateSensor(0f);
                int allowed = sensor.DetectedCount;

                EvidenceWriter.Write($"feature.damageAffiliation|filteredOut={filteredOut}|allowed={allowed}");
                Assert.That(filteredOut, Is.Zero);
                Assert.That(allowed, Is.EqualTo(1));
            }
            finally
            {
                sensor?.Dispose();
                manager.Dispose();
                registry.Dispose();
            }
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

        private sealed class FixPerceptible : IPerceptible, IPerceptionTeamProvider
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
