using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Trajectory.Tests.Editor.Integrations.DeterministicMath
{
    public sealed class DeterministicTrajectorySolverTests
    {
        [Test]
        public void Trace_WithSameInput_ProducesIdenticalSegments()
        {
            var query = DeterministicTrajectoryQuery.CreateRay(
                traceId: 1,
                ownerEntityId: 10UL,
                collisionLayerMask: ~0,
                origin: FPVector3.Zero,
                direction: FPVector3.Right,
                maxDistance: FPInt64.FromInt(10),
                maxReflectionCount: 1);

            var bufferA = new DeterministicTrajectoryTraceBuffer(4, 4, 4);
            var bufferB = new DeterministicTrajectoryTraceBuffer(4, 4, 4);
            var worldA = new ReflectOnceWorld();
            var worldB = new ReflectOnceWorld();

            DeterministicTrajectoryTraceResult resultA = DeterministicTrajectorySolver.Trace(in query, worldA, bufferA);
            DeterministicTrajectoryTraceResult resultB = DeterministicTrajectorySolver.Trace(in query, worldB, bufferB);

            Assert.That(resultA.Flags, Is.EqualTo(resultB.Flags));
            Assert.That(resultA.EndPosition, Is.EqualTo(resultB.EndPosition));
            Assert.That(bufferA.SegmentCount, Is.EqualTo(bufferB.SegmentCount));
            Assert.That(bufferA.GetSegment(1).To, Is.EqualTo(bufferB.GetSegment(1).To));
        }

        private sealed class ReflectOnceWorld : IDeterministicTrajectoryCollisionWorld
        {
            private int _castCount;

            public int Cast(
                in DeterministicTrajectoryCastQuery query,
                DeterministicTrajectoryHit[] results,
                int maxResults)
            {
                if (_castCount > 0)
                {
                    return 0;
                }

                _castCount++;
                results[0] = new DeterministicTrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: 7,
                    hitLayerMask: 1,
                    distance: FPInt64.FromInt(5),
                    position: new FPVector3(FPInt64.FromInt(5), FPInt64.Zero, FPInt64.Zero),
                    normal: FPVector3.Left,
                    response: TrajectoryHitResponse.Reflect);
                return 1;
            }
        }
    }
}
