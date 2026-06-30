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

        private sealed class TwoHitWorld : ITrajectoryCollisionWorld
        {
            public int Cast(
                in TrajectoryCastQuery query,
                TrajectoryHit[] results,
                int maxResults)
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
                TrajectoryHit[] results,
                int maxResults)
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

            public int SecondCastIgnoredObjectId { get; private set; }

            public int Cast(
                in TrajectoryCastQuery query,
                TrajectoryHit[] results,
                int maxResults)
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
    }
}
