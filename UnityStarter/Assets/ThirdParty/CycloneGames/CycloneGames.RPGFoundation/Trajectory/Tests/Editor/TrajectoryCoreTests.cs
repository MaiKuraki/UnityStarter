using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Trajectory.Tests.Editor
{
    public sealed class TrajectoryCoreTests
    {
        [Test]
        public void Trace_WithoutCollisionWorld_WritesSingleSegment()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            TrajectoryQuery query = TrajectoryQuery.CreateRay(
                traceId: 1,
                ownerEntityId: 10UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f);

            TrajectoryTraceResult result = TrajectorySolver.Trace(in query, collisionWorld: null, buffer);

            Assert.That((result.Flags & TrajectoryTraceFlags.MissingCollisionWorld) != 0, Is.True);
            Assert.That(buffer.SegmentCount, Is.EqualTo(1));
            Assert.That(buffer.HitCount, Is.EqualTo(0));
            Assert.That(buffer.GetSegment(0).To.X, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void Trace_SelectsNearestHitAndStops()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            TrajectoryQuery query = TrajectoryQuery.CreateRay(
                traceId: 2,
                ownerEntityId: 10UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f);

            TrajectoryTraceResult result = TrajectorySolver.Trace(in query, new TwoHitWorld(), buffer);

            Assert.That(result.Flags, Is.EqualTo(TrajectoryTraceFlags.None));
            Assert.That(buffer.HitCount, Is.EqualTo(1));
            Assert.That(buffer.GetHit(0).TargetObjectId, Is.EqualTo(2));
            Assert.That(buffer.GetSegment(0).Distance, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void Trace_ReflectsAndContinuesRemainingDistance()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            TrajectoryQuery query = TrajectoryQuery.CreateRay(
                traceId: 3,
                ownerEntityId: 10UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f,
                maxReflectionCount: 1);

            TrajectoryTraceResult result = TrajectorySolver.Trace(in query, new ReflectOnceWorld(), buffer);

            Assert.That(result.Flags, Is.EqualTo(TrajectoryTraceFlags.None));
            Assert.That(buffer.HitCount, Is.EqualTo(1));
            Assert.That(buffer.SegmentCount, Is.EqualTo(2));
            Assert.That(buffer.GetSegment(0).To.X, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(buffer.GetSegment(1).Direction.X, Is.LessThan(0f));
            Assert.That(result.EndPosition.X, Is.EqualTo(-0.001f).Within(0.0002f));
        }

        [Test]
        public void Trace_PiercePassesIgnoredTargetIntoNextCast()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            var world = new PierceOnceWorld();
            var query = new TrajectoryQuery(
                traceId: 4,
                ownerEntityId: 10UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f,
                radius: 0f,
                maxReflectionCount: 0,
                maxPierceCount: 1);

            TrajectorySolver.Trace(in query, world, buffer);

            Assert.That(world.SecondCastIgnoredObjectId, Is.EqualTo(7));
            Assert.That(buffer.HitCount, Is.EqualTo(1));
            Assert.That(buffer.SegmentCount, Is.EqualTo(2));
        }

        [Test]
        public void QueryValidator_ReportsInvalidAuthoringData()
        {
            var query = new TrajectoryQuery(
                traceId: 10,
                ownerEntityId: 10UL,
                collisionLayerMask: 0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Zero,
                maxDistance: -1f,
                radius: -0.1f,
                maxReflectionCount: -1,
                maxPierceCount: -1,
                maxHitCount: 0,
                maxIterationCount: 0,
                surfaceOffset: -0.1f);

            var issues = new TrajectoryQueryValidationIssue[TrajectoryQueryValidator.RECOMMENDED_ISSUE_CAPACITY];
            int issueCount = TrajectoryQueryValidator.Validate(in query, issues);

            Assert.That(issueCount, Is.GreaterThan(0));
            Assert.That(TrajectoryQueryValidator.GetWorstSeverity(issues, issueCount), Is.EqualTo(TrajectoryValidationSeverity.Error));
        }

        [Test]
        public void Buffer_SegmentCapacityBelowHitCapacity_KeepsHitIndexesResolvable()
        {
            // Hit and its segment are committed together, so HitCount never exceeds SegmentCount even
            // when the two capacities differ.
            var buffer = new TrajectoryTraceBuffer(segmentCapacity: 2, hitCapacity: 8, castHitCapacity: 8);
            var query = new TrajectoryQuery(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 100f,
                radius: 0f,
                maxReflectionCount: 100,
                maxPierceCount: 0,
                maxHitCount: int.MaxValue,
                maxIterationCount: 100,
                surfaceOffset: 0.001f);

            TrajectorySolver.Trace(in query, new RepeatReflectWorld(), buffer);

            Assert.That(buffer.SegmentCount, Is.EqualTo(2));
            Assert.That(buffer.HitCount, Is.LessThanOrEqualTo(buffer.SegmentCount));
            for (int i = 0; i < buffer.HitCount; i++)
            {
                int segmentIndex = buffer.GetHit(i).SegmentIndex;
                Assert.That(segmentIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(segmentIndex, Is.LessThan(buffer.SegmentCount));
                Assert.DoesNotThrow(() => buffer.GetSegment(segmentIndex));
            }
        }

        [Test]
        public void Trace_PierceChain_DoesNotRepeatPassedThroughTarget()
        {
            var buffer = new TrajectoryTraceBuffer(16, 16, 8);
            var query = new TrajectoryQuery(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 100f,
                radius: 0f,
                maxReflectionCount: 0,
                maxPierceCount: 4,
                maxHitCount: 16,
                maxIterationCount: 16,
                surfaceOffset: 0.001f);

            TrajectorySolver.Trace(in query, new PierceRepeatWorld(), buffer);

            Assert.That(buffer.HitCount, Is.EqualTo(2), "A then B; the second A must be suppressed");
            Assert.That(buffer.GetHit(0).TargetObjectId, Is.EqualTo(11UL));
            Assert.That(buffer.GetHit(1).TargetObjectId, Is.EqualTo(22UL));
        }

        [Test]
        public void Trace_ReflectChain_MayRevisitSameTarget()
        {
            // Guards against over-filtering: bouncing between two surfaces must stay legal.
            var buffer = new TrajectoryTraceBuffer(16, 16, 8);
            var query = new TrajectoryQuery(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 100f,
                radius: 0f,
                maxReflectionCount: 4,
                maxPierceCount: 0,
                maxHitCount: 16,
                maxIterationCount: 16,
                surfaceOffset: 0.001f);

            TrajectorySolver.Trace(in query, new ReflectRepeatWorld(), buffer);

            Assert.That(buffer.HitCount, Is.EqualTo(4));
        }

        [Test]
        public void Trace_UntrustedPositionOutsideSegment_FallsBackToDistance()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            var query = TrajectoryQuery.CreateRay(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 100f);

            TrajectorySolver.Trace(in query, new FarAwayPositionWorld(), buffer);

            TrajectorySegment segment = buffer.GetSegment(0);
            TrajectoryVector3 delta = segment.To - segment.From;
            float geometricLength = (float)Math.Sqrt(delta.LengthSquared);
            Assert.That(geometricLength, Is.EqualTo(segment.Distance).Within(0.001f));
        }

        [Test]
        public void Trace_OverclaimedHitCount_DoesNotResurfaceStaleHit()
        {
            var buffer = new TrajectoryTraceBuffer(4, 4, 4);
            var world = new StaleSlotWorld();
            var query = TrajectoryQuery.CreateRay(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f);

            TrajectorySolver.Trace(in query, world, buffer);
            TrajectorySolver.Trace(in query, world, buffer);

            Assert.That(buffer.HitCount, Is.EqualTo(1));
            Assert.That(buffer.GetHit(0).TargetObjectId, Is.EqualTo(901UL),
                "the only hit is the one the second cast wrote, not a stale slot");
        }

        [Test]
        public void QueryValidator_WarnsOnExcessiveIterationBudget()
        {
            var query = new TrajectoryQuery(
                traceId: 1,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 10f,
                radius: 0f,
                maxReflectionCount: 0,
                maxPierceCount: 0,
                maxHitCount: 16,
                maxIterationCount: int.MaxValue,
                surfaceOffset: 0.001f);

            var issues = new TrajectoryQueryValidationIssue[TrajectoryQueryValidator.RECOMMENDED_ISSUE_CAPACITY];
            int issueCount = TrajectoryQueryValidator.Validate(in query, issues);

            bool warned = false;
            for (int i = 0; i < Math.Min(issueCount, issues.Length); i++)
            {
                if (issues[i].Code == TrajectoryQueryValidationCode.IterationBudgetExcessive)
                {
                    warned = true;
                }
            }

            Assert.That(warned, Is.True);
        }

        [Test]
        public void CreateSweep_BuildsRadiusQuery()
        {
            TrajectoryQuery query = TrajectoryQuery.CreateSweep(
                traceId: 3,
                ownerEntityId: 5UL,
                collisionLayerMask: ~0,
                origin: TrajectoryVector3.Zero,
                direction: TrajectoryVector3.Right,
                maxDistance: 50f,
                radius: 0.5f,
                maxReflectionCount: 2,
                maxPierceCount: 3);

            Assert.That(query.Radius, Is.EqualTo(0.5f));
            Assert.That(query.MaxReflectionCount, Is.EqualTo(2));
            Assert.That(query.MaxPierceCount, Is.EqualTo(3));
            Assert.That(query.IsValid, Is.True);
        }

        [Test]
        public void Conformance_RejectsNullWorldAndAcceptsWellFormedHit()
        {
            var castQuery = new TrajectoryCastQuery(
                traceId: 1,
                segmentIndex: 0,
                ownerEntityId: 0UL,
                collisionLayerMask: ~0,
                radius: 0f,
                distance: 10f,
                from: TrajectoryVector3.Zero,
                to: new TrajectoryVector3(10f, 0f, 0f),
                direction: TrajectoryVector3.Right,
                ignoredTargetEntityId: 0UL,
                ignoredTargetObjectId: 0UL);

            Assert.That(
                TrajectoryCollisionWorldConformance.VerifySweep(null, in castQuery, out _),
                Is.False);
            Assert.That(
                TrajectoryCollisionWorldConformance.VerifySweep(new WellFormedWorld(), in castQuery, out string failure),
                Is.True,
                failure);
        }

        private sealed class TwoHitWorld : ITrajectoryCollisionWorld
        {
            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                results[0] = new TrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: 1,
                    hitLayerMask: 1,
                    distance: 5f,
                    position: new TrajectoryVector3(5f, 0f, 0f),
                    normal: new TrajectoryVector3(-1f, 0f, 0f));
                results[1] = new TrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: 2,
                    hitLayerMask: 1,
                    distance: 2f,
                    position: new TrajectoryVector3(2f, 0f, 0f),
                    normal: new TrajectoryVector3(-1f, 0f, 0f));
                return 2;
            }
        }

        private sealed class ReflectOnceWorld : ITrajectoryCollisionWorld
        {
            private int _castCount;

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (_castCount > 0)
                {
                    return 0;
                }

                _castCount++;
                results[0] = new TrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: 7,
                    hitLayerMask: 1,
                    distance: 5f,
                    position: new TrajectoryVector3(5f, 0f, 0f),
                    normal: new TrajectoryVector3(-1f, 0f, 0f),
                    response: TrajectoryHitResponse.Reflect);
                return 1;
            }
        }

        private sealed class PierceOnceWorld : ITrajectoryCollisionWorld
        {
            private int _castCount;

            public ulong SecondCastIgnoredObjectId { get; private set; }

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (_castCount > 0)
                {
                    SecondCastIgnoredObjectId = query.IgnoredTargetObjectId;
                    return 0;
                }

                _castCount++;
                results[0] = new TrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: 7,
                    hitLayerMask: 1,
                    distance: 2f,
                    position: new TrajectoryVector3(2f, 0f, 0f),
                    normal: new TrajectoryVector3(-1f, 0f, 0f),
                    response: TrajectoryHitResponse.Pierce);
                return 1;
            }
        }

        private sealed class RepeatReflectWorld : ITrajectoryCollisionWorld
        {
            private int _casts;

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0 || _casts++ > 256)
                {
                    return 0;
                }

                float distance = query.Distance * 0.2f;
                results[0] = new TrajectoryHit(
                    1000UL, 2000UL, 1,
                    distance,
                    query.From + query.Direction * distance,
                    -query.Direction,
                    TrajectoryHitResponse.Reflect);
                return 1;
            }
        }

        private sealed class PierceRepeatWorld : ITrajectoryCollisionWorld
        {
            private static readonly float[] Distances = { 2f, 4f, 6f };
            private static readonly ulong[] Ids = { 11UL, 22UL, 11UL };
            private int _index;

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0 || _index >= Distances.Length)
                {
                    return 0;
                }

                int i = _index++;
                float distance = Distances[i];
                results[0] = new TrajectoryHit(
                    0UL, Ids[i], 1,
                    distance,
                    query.From + query.Direction * distance,
                    -query.Direction,
                    TrajectoryHitResponse.Pierce);
                return 1;
            }
        }

        private sealed class ReflectRepeatWorld : ITrajectoryCollisionWorld
        {
            private int _casts;

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0 || _casts++ >= 4)
                {
                    return 0;
                }

                results[0] = new TrajectoryHit(
                    0UL, 33UL, 1,
                    2f,
                    query.From + query.Direction * 2f,
                    -query.Direction,
                    TrajectoryHitResponse.Reflect);
                return 1;
            }
        }

        private sealed class FarAwayPositionWorld : ITrajectoryCollisionWorld
        {
            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0)
                {
                    return 0;
                }

                results[0] = new TrajectoryHit(
                    0UL, 777UL, 1,
                    2f,
                    query.From + query.Direction * 2000f,
                    -query.Direction,
                    TrajectoryHitResponse.Stop);
                return 1;
            }
        }

        private sealed class StaleSlotWorld : ITrajectoryCollisionWorld
        {
            private int _calls;

            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0)
                {
                    return 0;
                }

                if (_calls++ == 0)
                {
                    int n = Math.Min(3, results.Length);
                    for (int i = 0; i < n; i++)
                    {
                        float d = 0.1f * (i + 1);
                        results[i] = new TrajectoryHit(
                            0UL, 900UL + (ulong)i, 1, d,
                            query.From + query.Direction * d,
                            -query.Direction,
                            TrajectoryHitResponse.Stop);
                    }

                    return n;
                }

                results[0] = new TrajectoryHit(
                    0UL, 901UL, 1, query.Distance * 0.9f,
                    query.From + query.Direction * (query.Distance * 0.9f),
                    -query.Direction,
                    TrajectoryHitResponse.Stop);
                return results.Length + 5;
            }
        }

        private sealed class WellFormedWorld : ITrajectoryCollisionWorld
        {
            public int Cast(
                in TrajectoryCastQuery query,
                Span<TrajectoryHit> results)
            {
                if (results.Length == 0)
                {
                    return 0;
                }

                results[0] = new TrajectoryHit(
                    0UL, 42UL, 1,
                    5f,
                    query.From + query.Direction * 5f,
                    -query.Direction,
                    TrajectoryHitResponse.Stop);
                return 1;
            }
        }
    }
}
