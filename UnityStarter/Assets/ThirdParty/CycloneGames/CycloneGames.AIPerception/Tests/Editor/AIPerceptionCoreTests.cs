using System;
using System.Collections.Generic;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.AIPerception.Runtime.Jobs;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.AIPerception.Tests.Editor
{
    public sealed class AIPerceptionCoreTests
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

        [Test]
        public void PerceptibleHandle_EqualityIncludesRegistryIdentity()
        {
            var first = new PerceptibleHandle(1, 7, 3);
            var same = new PerceptibleHandle(1, 7, 3);
            var differentRegistry = new PerceptibleHandle(2, 7, 3);
            var differentGeneration = new PerceptibleHandle(1, 7, 4);
            var differentId = new PerceptibleHandle(1, 8, 3);

            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(differentRegistry));
            Assert.That(first, Is.Not.EqualTo(differentGeneration));
            Assert.That(first, Is.Not.EqualTo(differentId));
            Assert.That(PerceptibleHandle.Invalid.IsValid, Is.False);
        }

        [Test]
        public void PerceptibleData_FlagPropertiesPreserveUnrelatedFlags()
        {
            var data = new PerceptibleData
            {
                Flags = PerceptibleData.SoundSourceFlag
            };

            data.IsDetectable = true;

            Assert.That(data.IsDetectable, Is.True);
            Assert.That(data.IsSoundSource, Is.True);

            data.IsDetectable = false;

            Assert.That(data.IsDetectable, Is.False);
            Assert.That(data.IsSoundSource, Is.True);
        }

        [Test]
        public void Registry_ZeroMaximum_AllowsGrowthPastInitialCapacity()
        {
            using var registry = new PerceptibleRegistry(initialCapacity: 1, maximumCapacity: 0);
            var handles = new PerceptibleHandle[129];

            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = registry.Register(new TestPerceptible(i));
            }

            Assert.That(registry.MaximumCapacity, Is.Zero);
            Assert.That(registry.Count, Is.EqualTo(handles.Length));
            for (int i = 0; i < handles.Length; i++)
            {
                Assert.That(registry.IsValid(handles[i]), Is.True);
            }
        }

        [Test]
        public void Registry_FiniteMaximum_RejectsRegistrationAfterCapacity()
        {
            using var registry = new PerceptibleRegistry(initialCapacity: 1, maximumCapacity: 1);
            LogAssert.Expect(
                LogType.Warning,
                "[AIPerception] Registry at 1/1. Review the world capacity budget.");
            PerceptibleHandle accepted = registry.Register(new TestPerceptible(1));

            LogAssert.Expect(
                LogType.Error,
                "[AIPerception] Registry capacity exhausted (1).");
            PerceptibleHandle rejected = registry.Register(new TestPerceptible(2));

            Assert.That(accepted.IsValid, Is.True);
            Assert.That(rejected, Is.EqualTo(PerceptibleHandle.Invalid));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void Registry_TrySetMaxCapacity_BelowActiveCountPreservesPreviousLimit()
        {
            using var registry = new PerceptibleRegistry(initialCapacity: 2, maximumCapacity: 4);
            registry.Register(new TestPerceptible(1));
            registry.Register(new TestPerceptible(2));

            Assert.That(registry.TrySetMaxCapacity(1), Is.False);
            Assert.That(registry.MaximumCapacity, Is.EqualTo(4));
            Assert.That(registry.TrySetMaxCapacity(-1), Is.False);
            Assert.That(registry.MaximumCapacity, Is.EqualTo(4));
            Assert.That(registry.TrySetMaxCapacity(2), Is.True);
            Assert.That(registry.MaximumCapacity, Is.EqualTo(2));
        }

        [Test]
        public void Registry_ReusedSlot_InvalidatesOldHandle()
        {
            using var registry = new PerceptibleRegistry();
            var first = new TestPerceptible(10, PerceptibleTypes.Player);
            var second = new TestPerceptible(11, PerceptibleTypes.Enemy);

            PerceptibleHandle firstHandle = registry.Register(first);
            Assert.That(registry.Unregister(firstHandle), Is.True);
            PerceptibleHandle secondHandle = registry.Register(second);

            Assert.That(secondHandle.RegistryId, Is.EqualTo(firstHandle.RegistryId));
            Assert.That(secondHandle.Id, Is.EqualTo(firstHandle.Id));
            Assert.That(secondHandle.Generation, Is.Not.EqualTo(firstHandle.Generation));
            Assert.That(registry.IsValid(firstHandle), Is.False);
            Assert.That(registry.IsValid(secondHandle), Is.True);
            Assert.That(registry.Get(secondHandle), Is.SameAs(second));
        }

        [Test]
        public void Registry_HandleCannotResolveInAnotherRegistry()
        {
            using var firstRegistry = new PerceptibleRegistry();
            using var secondRegistry = new PerceptibleRegistry();
            var firstTarget = new TestPerceptible(1);
            var secondTarget = new TestPerceptible(2);

            PerceptibleHandle firstHandle = firstRegistry.Register(firstTarget);
            PerceptibleHandle secondHandle = secondRegistry.Register(secondTarget);

            Assert.That(firstHandle.Id, Is.EqualTo(secondHandle.Id));
            Assert.That(firstHandle.Generation, Is.EqualTo(secondHandle.Generation));
            Assert.That(firstHandle.RegistryId, Is.Not.EqualTo(secondHandle.RegistryId));
            Assert.That(secondRegistry.IsValid(firstHandle), Is.False);
            Assert.That(secondRegistry.Get(firstHandle), Is.Null);
            Assert.That(secondRegistry.Unregister(firstHandle), Is.False);
        }

        [Test]
        public void Registry_RebuildData_RefreshesDynamicSnapshotWithoutManualDirtySignal()
        {
            using var registry = new PerceptibleRegistry();
            var target = new TestPerceptible(10, PerceptibleTypes.Player)
            {
                Position = new float3(1f, 2f, 3f),
                LineOfSightPoint = new float3(1f, 3f, 3f),
                DetectionRadius = 1f,
                Loudness = 0.25f,
                IsSoundSource = false
            };
            registry.Register(target);
            registry.RebuildData();
            int initialVersion = registry.SnapshotVersion;

            target.Position = new float3(9f, 8f, 7f);
            target.LineOfSightPoint = new float3(9f, 9f, 7f);
            target.DetectionRadius = 2.5f;
            target.Loudness = 0.75f;
            target.IsSoundSource = true;
            registry.RebuildData();

            Assert.That(registry.SnapshotVersion, Is.GreaterThan(initialVersion));
            using NativeArray<PerceptibleData> data = registry.CreateNativeDataCopy(Allocator.Temp);
            Assert.That(data.Length, Is.EqualTo(1));
            Assert.That(data[0].RegistryId, Is.EqualTo(registry.RegistryId));
            Assert.That(data[0].Position, Is.EqualTo(target.Position));
            Assert.That(data[0].LOSPoint, Is.EqualTo(target.LineOfSightPoint));
            Assert.That(data[0].DetectionRadius, Is.EqualTo(2.5f));
            Assert.That(data[0].Loudness, Is.EqualTo(0.75f));
            Assert.That(data[0].IsSoundSource, Is.True);
        }

        [Test]
        public void Registry_RebuildData_ExportsOnlyDetectableFiniteTargets()
        {
            using var registry = new PerceptibleRegistry();
            registry.Register(new TestPerceptible(10, PerceptibleTypes.Player)
            {
                IsDetectable = true
            });
            registry.Register(new TestPerceptible(11, PerceptibleTypes.Enemy)
            {
                IsDetectable = false
            });
            registry.Register(new TestPerceptible(12, PerceptibleTypes.Enemy)
            {
                Position = new float3(float.NaN, 0f, 0f)
            });

            registry.RebuildData();

            Assert.That(registry.GetDataCount(), Is.EqualTo(1));
            using NativeArray<PerceptibleData> data = registry.CreateNativeDataCopy(Allocator.Temp);
            Assert.That(data.Length, Is.EqualTo(1));
            Assert.That(data[0].TypeId, Is.EqualTo(PerceptibleTypes.Player));
            Assert.That(data[0].IsDetectable, Is.True);
        }

        [Test]
        public void SpatialGrid_RangeBoundaryIsInclusive_AndCapacityFailureClearsResults()
        {
            var source = new[]
            {
                CreateData(1, new float3(-10f, 0f, 0f)),
                CreateData(2, new float3(10f, 0f, 0f)),
                CreateData(3, new float3(10.01f, 0f, 0f))
            };
            var grid = new SpatialGrid(4f);
            grid.Rebuild(source, source.Length);
            var indices = new NativeList<int>(3, Allocator.Temp);
            try
            {
                bool succeeded = grid.CollectIndices(
                    source,
                    source.Length,
                    float3.zero,
                    10f,
                    ref indices,
                    maximumResults: 3);

                Assert.That(succeeded, Is.True);
                Assert.That(indices.Length, Is.EqualTo(2));
                Assert.That(ContainsId(source, indices, 1), Is.True);
                Assert.That(ContainsId(source, indices, 2), Is.True);
                Assert.That(ContainsId(source, indices, 3), Is.False);

                bool capacitySucceeded = grid.CollectIndices(
                    source,
                    source.Length,
                    float3.zero,
                    11f,
                    ref indices,
                    maximumResults: 1);

                Assert.That(capacitySucceeded, Is.False);
                Assert.That(indices.Length, Is.Zero);
            }
            finally
            {
                indices.Dispose();
            }
        }

        [Test]
        public void SpatialGrid_SubUnitCellSizePreservesExactRangeQueries()
        {
            var source = new[]
            {
                CreateData(1, new float3(-0.25f, 0f, 0f)),
                CreateData(2, new float3(0.25f, 0f, 0f)),
                CreateData(3, new float3(0.251f, 0f, 0f))
            };
            var grid = new SpatialGrid(0.25f);
            grid.Rebuild(source, source.Length);
            var indices = new NativeList<int>(3, Allocator.Temp);
            try
            {
                bool succeeded = grid.CollectIndices(
                    source,
                    source.Length,
                    float3.zero,
                    0.25f,
                    ref indices,
                    maximumResults: 3);

                Assert.That(grid.CellSize, Is.EqualTo(0.25f));
                Assert.That(succeeded, Is.True);
                Assert.That(indices.Length, Is.EqualTo(2));
                Assert.That(ContainsId(source, indices, 1), Is.True);
                Assert.That(ContainsId(source, indices, 2), Is.True);
                Assert.That(ContainsId(source, indices, 3), Is.False);
            }
            finally
            {
                indices.Dispose();
            }
        }

        [Test]
        public void SpatialGridAndProximity_WarmedTenThousandTargetPathAllocatesZeroManagedBytes()
        {
            const int targetCount = 10_000;
            const int gridWidth = 100;
            const int iterations = 16;
            var source = new PerceptibleData[targetCount];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = CreateData(
                    i + 1,
                    new float3(i % gridWidth, 0f, i / gridWidth),
                    detectionRadius: 0f);
            }

            var grid = new SpatialGrid(2f);
            grid.Rebuild(source, source.Length);
            using var targets = new NativeArray<PerceptibleData>(source, Allocator.Persistent);
            var candidates = new NativeList<int>(targetCount, Allocator.Persistent);
            using var proximity = new NativeArray<float>(targetCount, Allocator.Persistent);
            var origin = new float3(49.5f, 0f, 49.5f);
            const float radius = 12.5f;
            try
            {
                Assert.That(RunProximityBatch(
                    grid,
                    source,
                    targets,
                    ref candidates,
                    proximity,
                    origin,
                    radius,
                    out int warmCandidateCount,
                    out int warmPositiveCount), Is.True);
                Assert.That(warmCandidateCount, Is.GreaterThan(0));
                Assert.That(warmPositiveCount, Is.GreaterThan(0));

                _ = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = new System.Diagnostics.Stopwatch();
                int candidateTotal = 0;
                int positiveTotal = 0;
                bool allSucceeded = true;
                stopwatch.Start();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < iterations; i++)
                {
                    allSucceeded &= RunProximityBatch(
                        grid,
                        source,
                        targets,
                        ref candidates,
                        proximity,
                        origin,
                        radius,
                        out int candidateCount,
                        out int positiveCount);
                    candidateTotal += candidateCount;
                    positiveTotal += positiveCount;
                }

                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                stopwatch.Stop();

                string evidence =
                    $"AIPerception warmed Editor path: targets={targetCount}, iterations={iterations}, candidates/iteration={warmCandidateCount}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}, managedAllocatedBytes={allocatedBytes}.";
                TestContext.Progress.WriteLine(evidence);
                Debug.Log(evidence);
                Assert.That(allSucceeded, Is.True);
                Assert.That(candidateTotal, Is.EqualTo(warmCandidateCount * iterations));
                Assert.That(positiveTotal, Is.EqualTo(warmPositiveCount * iterations));
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                candidates.Dispose();
            }
        }

        [Test]
        public void SightJob_IgnoresSelf_AndIncludesTargetDetectionRadius()
        {
            PerceptibleData self = CreateData(
                1,
                new float3(1f, 0f, 0f),
                detectionRadius: 1f);
            using var targets = new NativeArray<PerceptibleData>(new[]
            {
                self,
                CreateData(2, new float3(5.5f, 0f, 0f), detectionRadius: 1f)
            }, Allocator.TempJob);
            using var candidateIndices = CreateSequentialIndices(2);
            using var passed = new NativeArray<int>(2, Allocator.TempJob);

            var job = new SightConeQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Forward = new float3(1f, 0f, 0f),
                MaxDistance = 5f,
                CosHalfAngle = math.cos(math.radians(60f)),
                IgnoredTarget = self.ToHandle(),
                PassedFilter = passed
            };

            job.Execute(0);
            job.Execute(1);

            Assert.That(passed[0], Is.Zero);
            Assert.That(passed[1], Is.EqualTo(1));
        }

        [Test]
        public void PerceptionJobs_SquaredDistanceOverflowUsesFiniteDoubleFallback()
        {
            using var targets = new NativeArray<PerceptibleData>(new[]
            {
                CreateData(
                    1,
                    new float3(1e20f, 0f, 0f),
                    detectionRadius: 0f,
                    loudness: 1f,
                    isSoundSource: true)
            }, Allocator.TempJob);
            using var candidateIndices = CreateSequentialIndices(1);
            using var sightPassed = new NativeArray<int>(1, Allocator.TempJob);
            using var audibility = new NativeArray<float>(1, Allocator.TempJob);
            using var proximity = new NativeArray<float>(1, Allocator.TempJob);

            var sight = new SightConeQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Forward = new float3(1f, 0f, 0f),
                MaxDistance = 2e20f,
                CosHalfAngle = 0.5f,
                PassedFilter = sightPassed
            };
            var hearing = new SphereQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Radius = 2e20f,
                Audibility = audibility
            };
            var proximityJob = new ProximityQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Radius = 2e20f,
                Proximity = proximity
            };

            sight.Execute(0);
            hearing.Execute(0);
            proximityJob.Execute(0);

            Assert.That(sightPassed[0], Is.EqualTo(1));
            Assert.That(audibility[0], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(proximity[0], Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void PerceptionJobs_UnrepresentableDistanceReturnsCoordinateRangeSentinel()
        {
            using var targets = new NativeArray<PerceptibleData>(new[]
            {
                CreateData(
                    1,
                    new float3(float.MaxValue, 0f, 0f),
                    detectionRadius: 0f,
                    loudness: 1f,
                    isSoundSource: true)
            }, Allocator.TempJob);
            using var candidateIndices = CreateSequentialIndices(1);
            using var sightPassed = new NativeArray<int>(1, Allocator.TempJob);
            using var audibility = new NativeArray<float>(1, Allocator.TempJob);
            using var proximity = new NativeArray<float>(1, Allocator.TempJob);
            var origin = new float3(-float.MaxValue, 0f, 0f);

            var sight = new SightConeQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = origin,
                Forward = new float3(1f, 0f, 0f),
                MaxDistance = float.MaxValue,
                CosHalfAngle = 0.5f,
                PassedFilter = sightPassed
            };
            var hearing = new SphereQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = origin,
                Radius = float.MaxValue,
                Audibility = audibility
            };
            var proximityJob = new ProximityQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = origin,
                Radius = float.MaxValue,
                Proximity = proximity
            };

            sight.Execute(0);
            hearing.Execute(0);
            proximityJob.Execute(0);

            Assert.That(sightPassed[0], Is.EqualTo(-1));
            Assert.That(audibility[0], Is.EqualTo(-1f));
            Assert.That(proximity[0], Is.EqualTo(-1f));
            Assert.That((byte)SensorUpdateStatus.CoordinateRangeExceeded, Is.EqualTo(9));
        }

        [Test]
        public void SightSensor_UnrepresentableLosDistanceReportsCoordinateRangeExceeded()
        {
            var sensorOrigin = new float3(-float.MaxValue, 0f, 0f);
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            registry.Register(new TestPerceptible(1)
            {
                Position = sensorOrigin,
                LineOfSightPoint = new float3(float.MaxValue, 0f, 0f),
                DetectionRadius = 0f
            });
            registry.RebuildData();

            Transform sensorTransform = CreateGameObject("Coordinate Range Sight Sensor").transform;
            sensorTransform.position = sensorOrigin;
            SightSensorConfig config = SightSensorConfig.Default;
            config.MaxDistance = 1f;
            config.MemoryDuration = 0f;
            config.UseLineOfSight = true;
            var sensor = new SightSensor(sensorTransform, config);
            try
            {
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.CoordinateRangeExceeded));
                Assert.That(sensor.DetectedCount, Is.Zero);
            }
            finally
            {
                sensor.Dispose();
            }
        }

        [Test]
        public void HearingJob_RequiresSoundSource_IgnoresSelf_AndRejectsZeroEffectiveRadius()
        {
            PerceptibleData self = CreateData(
                1,
                new float3(1f, 0f, 0f),
                loudness: 1f,
                isSoundSource: true);
            using var targets = new NativeArray<PerceptibleData>(new[]
            {
                self,
                CreateData(
                    2,
                    new float3(1f, 0f, 0f),
                    loudness: 1f,
                    isSoundSource: false),
                CreateData(
                    3,
                    new float3(1f, 0f, 0f),
                    loudness: 1f,
                    isSoundSource: true),
                CreateData(
                    4,
                    float3.zero,
                    detectionRadius: 0f,
                    loudness: 0f,
                    isSoundSource: true)
            }, Allocator.TempJob);
            using var candidateIndices = CreateSequentialIndices(4);
            using var audibility = new NativeArray<float>(4, Allocator.TempJob);

            var job = new SphereQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Radius = 5f,
                IgnoredTarget = self.ToHandle(),
                Audibility = audibility
            };

            for (int i = 0; i < targets.Length; i++)
            {
                job.Execute(i);
            }

            Assert.That(audibility[0], Is.Zero);
            Assert.That(audibility[1], Is.Zero);
            Assert.That(audibility[2], Is.GreaterThan(0f));
            Assert.That(audibility[3], Is.Zero);
        }

        [Test]
        public void HearingSensor_OcclusionBudgetReportsPartialRefinement()
        {
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            registry.Register(new TestPerceptible(1)
            {
                Position = new float3(1f, 0f, 0f),
                LineOfSightPoint = new float3(1f, 0f, 0f),
                IsSoundSource = true,
                Loudness = 1f
            });
            registry.Register(new TestPerceptible(2)
            {
                Position = new float3(2f, 0f, 0f),
                LineOfSightPoint = new float3(2f, 0f, 0f),
                IsSoundSource = true,
                Loudness = 1f
            });
            registry.RebuildData();

            Transform sensorTransform = CreateGameObject("Hearing Sensor").transform;
            HearingSensorConfig config = HearingSensorConfig.Default;
            Assert.That(config.MaximumOcclusionChecksPerUpdate, Is.EqualTo(64));
            config.Radius = 5f;
            config.MemoryDuration = 0f;
            config.UseOcclusion = true;
            config.MaximumOcclusionChecksPerUpdate = 1;
            var sensor = new HearingSensor(sensorTransform, config);
            try
            {
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.OcclusionBudgetExceeded));
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));
            }
            finally
            {
                sensor.Dispose();
            }
        }

        [Test]
        public void ProximityJob_ZeroEffectiveRadiusAtOrigin_ReturnsFiniteZero()
        {
            using var targets = new NativeArray<PerceptibleData>(new[]
            {
                CreateData(1, float3.zero, detectionRadius: 0f)
            }, Allocator.TempJob);
            using var candidateIndices = CreateSequentialIndices(1);
            using var proximity = new NativeArray<float>(1, Allocator.TempJob);
            var job = new ProximityQueryJob
            {
                Targets = targets,
                CandidateIndices = candidateIndices,
                Origin = float3.zero,
                Radius = 0f,
                Proximity = proximity
            };

            job.Execute(0);

            Assert.That(float.IsFinite(proximity[0]), Is.True);
            Assert.That(proximity[0], Is.Zero);
        }

        [Test]
        public void SensorManager_LODUsesFrequencyMultiplier_AndRejectsInvalidLevels()
        {
            using var manager = new SensorManager();
            Transform reference = CreateGameObject("LOD Reference").transform;
            reference.position = Vector3.zero;
            var sensor = new TestSensor
            {
                PositionValue = new float3(50f, 0f, 0f),
                UpdateIntervalValue = 10f
            };
            manager.Register(sensor);

            Assert.That(manager.ConfigureLOD(reference, new[]
            {
                new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1f },
                new SensorLODLevel { Distance = 20f, FrequencyMultiplier = 0.5f }
            }), Is.False);
            Assert.That(manager.ConfigureLOD(reference, new[]
            {
                new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1f },
                new SensorLODLevel { Distance = 80f, FrequencyMultiplier = float.NaN }
            }), Is.False);
            Assert.That(manager.ConfigureLOD(reference, new[]
            {
                new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1f },
                new SensorLODLevel { Distance = 80f, FrequencyMultiplier = 0.5f }
            }), Is.True);

            sensor.SetLastUpdateTime(Time.timeAsDouble - 15d);
            manager.Update(0f);
            Assert.That(sensor.UpdateCount, Is.Zero, "Half frequency must double the base interval.");

            sensor.SetLastUpdateTime(Time.timeAsDouble - 25d);
            manager.Update(0f);
            Assert.That(sensor.UpdateCount, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitOwner_PublicRegistryRebuildDrainsDeferredLocalWorldWithoutSingletons()
        {
            Assert.That(SensorManager.HasInstance, Is.False);
            Assert.That(PerceptibleRegistry.HasInstance, Is.False);

            using var registry = new PerceptibleRegistry();
            using var manager = new SensorManager(registry);
            manager.UseDeferredJobCompletion = true;
            PerceptibleHandle targetHandle = registry.Register(new TestPerceptible(1)
            {
                Position = new float3(1f, 0f, 0f),
                LineOfSightPoint = new float3(1f, 0f, 0f),
                DetectionRadius = 0f
            });
            Transform sensorTransform = CreateGameObject("Local Proximity Sensor").transform;
            ProximitySensorConfig config = ProximitySensorConfig.Default;
            config.Radius = 5f;
            config.UpdateInterval = 0f;
            config.MemoryDuration = 0f;
            var sensor = new ProximitySensor(sensorTransform, config, manager);
            try
            {
                manager.Register(sensor);
                manager.Update(0f);

                Assert.That(sensor.DetectedCount, Is.Zero);
                Assert.That(SensorManager.HasInstance, Is.False);
                Assert.That(PerceptibleRegistry.HasInstance, Is.False);

                Assert.DoesNotThrow(registry.RebuildData);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Ready));
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));
                Assert.That(sensor.TryGetResult(0, out DetectionResult result), Is.True);
                Assert.That(result.Target, Is.EqualTo(targetHandle));
                Assert.That(SensorManager.HasInstance, Is.False);
                Assert.That(PerceptibleRegistry.HasInstance, Is.False);
            }
            finally
            {
                manager.Unregister(sensor);
                sensor.Dispose();
            }
        }

        [Test]
        public void BuiltInSensor_RegistrationWithDifferentOwnerFailsFast()
        {
            using var firstRegistry = new PerceptibleRegistry();
            using var firstManager = new SensorManager(firstRegistry);
            using var secondRegistry = new PerceptibleRegistry();
            using var secondManager = new SensorManager(secondRegistry);
            Transform sensorTransform = CreateGameObject("Owned Proximity Sensor").transform;
            var sensor = new ProximitySensor(
                sensorTransform,
                ProximitySensorConfig.Default,
                firstManager);
            try
            {
                Assert.Throws<InvalidOperationException>(() => secondManager.Register(sensor));
                Assert.That(secondManager.SensorCount, Is.Zero);
            }
            finally
            {
                sensor.Dispose();
            }
        }

        [Test]
        public void SensorManager_ReentrantUnregisterFromSensorCallbackFailsFast()
        {
            using var manager = new SensorManager();
            var sensor = new TestSensor
            {
                UpdateIntervalValue = 0f
            };
            sensor.UpdateAction = () => manager.Unregister(sensor);
            manager.Register(sensor);

            Assert.Throws<InvalidOperationException>(() => manager.Update(0f));
            Assert.That(manager.GetSensor(sensor.SensorId), Is.SameAs(sensor));
        }

        [Test]
        public void ProximitySensor_BroadphaseMemoryAndDisable_HonorResultContracts()
        {
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            var target = new TestPerceptible(1)
            {
                Position = new float3(1f, 0f, 0f),
                LineOfSightPoint = new float3(1f, 0f, 0f),
                DetectionRadius = 1f
            };
            PerceptibleHandle targetHandle = registry.Register(target);
            registry.RebuildData();

            Transform sensorTransform = CreateGameObject("Proximity Sensor").transform;
            var config = ProximitySensorConfig.Default;
            config.Radius = 5f;
            config.MemoryDuration = 10f;
            var sensor = new ProximitySensor(sensorTransform, config);
            try
            {
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Ready));
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));
                Assert.That(sensor.MemoryCount, Is.EqualTo(1));
                Assert.That(sensor.TryGetResult(0, out DetectionResult live), Is.True);
                Assert.That(live.Target, Is.EqualTo(targetHandle));
                Assert.That(live.IsFromMemory, Is.False);

                target.Position = new float3(5.5f, 0f, 0f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Ready));
                Assert.That(sensor.TryGetResult(0, out DetectionResult weakerLive), Is.True);
                Assert.That(weakerLive.Visibility, Is.LessThan(live.Visibility));

                target.Position = new float3(100f, 0f, 0f);
                target.LineOfSightPoint = target.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.NoTargets));
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));
                Assert.That(sensor.MemoryCount, Is.EqualTo(1));
                Assert.That(sensor.TryGetResult(0, out DetectionResult remembered), Is.True);
                Assert.That(remembered.Target, Is.EqualTo(targetHandle));
                Assert.That(remembered.IsFromMemory, Is.True);
                Assert.That(remembered.Visibility, Is.LessThanOrEqualTo(weakerLive.Visibility + 0.001f));

                var results = new NativeList<DetectionResult>(2, Allocator.Temp);
                try
                {
                    sensor.GetDetectionResults(ref results);
                    Assert.That(results.Length, Is.EqualTo(sensor.DetectedCount));
                }
                finally
                {
                    results.Dispose();
                }

                sensor.IsEnabled = false;

                Assert.That(sensor.HasDetection, Is.False);
                Assert.That(sensor.DetectedCount, Is.Zero);
                Assert.That(sensor.MemoryCount, Is.Zero);
                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Ready));
            }
            finally
            {
                sensor.Dispose();
            }
        }

        [Test]
        public void ProximitySensor_NoLiveTargetsWithTruncatedMemoryReportsResultCapacityExceeded()
        {
            PerceptibleRegistry registry = PerceptibleRegistry.Instance;
            var firstTarget = new TestPerceptible(1)
            {
                Position = new float3(0.5f, 0f, 0f),
                LineOfSightPoint = new float3(0.5f, 0f, 0f),
                DetectionRadius = 0f
            };
            var secondTarget = new TestPerceptible(2)
            {
                Position = new float3(100f, 0f, 0f),
                LineOfSightPoint = new float3(100f, 0f, 0f),
                DetectionRadius = 0f
            };
            registry.Register(firstTarget);
            registry.Register(secondTarget);
            registry.RebuildData();

            Transform sensorTransform = CreateGameObject("Memory Capacity Proximity Sensor").transform;
            ProximitySensorConfig config = ProximitySensorConfig.Default;
            config.Radius = 1f;
            config.UpdateInterval = 0f;
            config.MemoryDuration = 60f;
            PerceptionSensorCapacity capacity = PerceptionSensorCapacity.Default;
            capacity.InitialResultCapacity = 1;
            capacity.MaximumResults = 1;
            capacity.InitialMemoryCapacity = 2;
            capacity.MaximumMemoryEntries = 2;
            config.Capacity = capacity;
            var sensor = new ProximitySensor(sensorTransform, config);
            try
            {
                sensor.UpdateSensor(0f);
                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Ready));
                Assert.That(sensor.MemoryCount, Is.EqualTo(1));

                firstTarget.Position = new float3(100f, 0f, 0f);
                firstTarget.LineOfSightPoint = firstTarget.Position;
                secondTarget.Position = new float3(0.5f, 0f, 0f);
                secondTarget.LineOfSightPoint = secondTarget.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);
                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.ResultCapacityExceeded));
                Assert.That(sensor.MemoryCount, Is.EqualTo(2));

                secondTarget.Position = new float3(200f, 0f, 0f);
                secondTarget.LineOfSightPoint = secondTarget.Position;
                registry.RebuildData();
                sensor.UpdateSensor(0f);

                Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.ResultCapacityExceeded));
                Assert.That(sensor.DetectedCount, Is.EqualTo(1));
                Assert.That(sensor.MemoryCount, Is.EqualTo(2));
                Assert.That(sensor.TryGetResult(0, out DetectionResult remembered), Is.True);
                Assert.That(remembered.IsFromMemory, Is.True);
            }
            finally
            {
                sensor.Dispose();
            }
        }

        [Test]
        public void SensorLifecycle_InitializeAndDisposeAreIdempotent()
        {
            Transform sensorTransform = CreateGameObject("Sight Sensor").transform;
            var sensor = new SightSensor(sensorTransform, SightSensorConfig.Default);

            Assert.DoesNotThrow(sensor.Initialize);
            Assert.DoesNotThrow(sensor.Initialize);
            Assert.DoesNotThrow(sensor.Dispose);
            Assert.That(sensor.LastUpdateStatus, Is.EqualTo(SensorUpdateStatus.Disposed));
            Assert.DoesNotThrow(sensor.Dispose);
            Assert.Throws<ObjectDisposedException>(sensor.Initialize);

            var registry = new PerceptibleRegistry();
            Assert.DoesNotThrow(registry.Dispose);
            Assert.DoesNotThrow(registry.Dispose);
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static NativeArray<int> CreateSequentialIndices(int count)
        {
            var indices = new NativeArray<int>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }

            return indices;
        }

        private static PerceptibleData CreateData(
            int id,
            float3 position,
            float detectionRadius = 0f,
            float loudness = 1f,
            bool isSoundSource = false)
        {
            byte flags = PerceptibleData.DetectableFlag;
            if (isSoundSource)
            {
                flags |= PerceptibleData.SoundSourceFlag;
            }

            return new PerceptibleData
            {
                RegistryId = 19,
                Id = id,
                Generation = 1,
                TypeId = PerceptibleTypes.Default,
                Flags = flags,
                DetectionRadius = detectionRadius,
                Loudness = loudness,
                Position = position,
                LOSPoint = position
            };
        }

        private static bool ContainsId(
            PerceptibleData[] source,
            NativeList<int> indices,
            int id)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                if (source[indices[i]].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RunProximityBatch(
            SpatialGrid grid,
            PerceptibleData[] source,
            NativeArray<PerceptibleData> targets,
            ref NativeList<int> candidates,
            NativeArray<float> proximity,
            float3 origin,
            float radius,
            out int candidateCount,
            out int positiveCount)
        {
            bool succeeded = grid.CollectIndices(
                source,
                source.Length,
                origin,
                radius,
                ref candidates,
                source.Length);
            candidateCount = candidates.Length;
            positiveCount = 0;
            if (!succeeded)
            {
                return false;
            }

            var job = new ProximityQueryJob
            {
                Targets = targets,
                CandidateIndices = candidates.AsArray(),
                Origin = origin,
                Radius = radius,
                Proximity = proximity
            };
            for (int i = 0; i < candidateCount; i++)
            {
                job.Execute(i);
                if (proximity[i] > 0f)
                {
                    positiveCount++;
                }
            }

            return true;
        }

        private sealed class TestPerceptible : IPerceptible
        {
            public TestPerceptible(
                int perceptibleId,
                int typeId = PerceptibleTypes.Default)
            {
                PerceptibleId = perceptibleId;
                PerceptibleTypeId = typeId;
                Position = new float3(perceptibleId, 1f, 2f);
                LineOfSightPoint = Position + new float3(0f, 1f, 0f);
                DetectionRadius = 1f;
                Loudness = 0.5f;
                IsDetectable = true;
                Tag = "Test";
            }

            public int PerceptibleId { get; }
            public int PerceptibleTypeId { get; set; }
            public bool IsDetectable { get; set; }
            public float3 Position { get; set; }
            public float DetectionRadius { get; set; }
            public float Loudness { get; set; }
            public bool IsSoundSource { get; set; }
            public string Tag { get; set; }
            public float3 LineOfSightPoint { get; set; }

            public float3 GetLOSPoint() => LineOfSightPoint;
        }

        private sealed class TestSensor : ISensor
        {
            public int SensorId => 1001;
            public SensorType Type => SensorType.Custom;
            public bool IsEnabled { get; set; } = true;
            public float UpdateInterval => UpdateIntervalValue;
            public double LastUpdateTime { get; private set; }
            public float3 Position => PositionValue;
            public SensorUpdateStatus LastUpdateStatus { get; private set; } = SensorUpdateStatus.Ready;
            public bool HasDetection => false;
            public int DetectedCount => 0;
            public float UpdateIntervalValue { get; set; }
            public float3 PositionValue { get; set; }
            public int UpdateCount { get; private set; }
            public Action UpdateAction { get; set; }

            public void SetLastUpdateTime(double value)
            {
                LastUpdateTime = value;
            }

            public void Initialize()
            {
            }

            public void UpdateSensor(float deltaTime)
            {
                UpdateAction?.Invoke();
                UpdateCount++;
                LastUpdateTime = Time.timeAsDouble;
            }

            public void ProcessJobResults()
            {
            }

            public void Dispose()
            {
                LastUpdateStatus = SensorUpdateStatus.Disposed;
            }

            public bool TryGetResult(int index, out DetectionResult result)
            {
                result = default;
                return false;
            }

            public void GetDetectionResults(ref NativeList<DetectionResult> results)
            {
            }

            public void GetDetectedHandles(ref NativeList<PerceptibleHandle> results)
            {
            }
        }

    }
}
