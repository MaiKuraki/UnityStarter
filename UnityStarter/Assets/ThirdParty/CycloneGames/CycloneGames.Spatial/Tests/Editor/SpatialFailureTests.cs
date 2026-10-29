using System;
using System.Collections.Generic;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Failure-path tests. A rejected call must leave the structure exactly as it was: no half-linked slot, no empty
    /// bucket, no sorted array left out of order. These are the cases that turn a recoverable error into silent
    /// data corruption, so they are tested explicitly rather than assumed.
    /// </summary>
    public sealed class SpatialFailureTests
    {
        [Test]
        public void RejectedInsert_LeavesTheGridUnchanged()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64, SpatialCapacityPolicy.Fixed);
            for (int i = 0; i < 64; i++)
            {
                Assert.That(grid.TryInsert(i, i * 0.5f, 0f, 0f, out _), Is.True);
            }

            int capacityBefore = grid.Capacity;
            var before = new int[64];
            int countBefore = grid.QuerySphere(0f, 0f, 0f, 100f, before, before.Length);

            Assert.That(grid.TryInsert(999, 1f, 1f, 1f, out SpatialHandle rejected), Is.False);
            Assert.That(rejected.IsValid, Is.False, "A rejected insert must not hand out a handle.");

            Assert.That(grid.Count, Is.EqualTo(64));
            Assert.That(grid.Capacity, Is.EqualTo(capacityBefore));
            Assert.That(grid.ValidateStructure(), Is.True,
                "A rejected insert left an empty bucket or a half-linked slot behind.");

            var after = new int[64];
            int countAfter = grid.QuerySphere(0f, 0f, 0f, 100f, after, after.Length);
            Assert.That(countAfter, Is.EqualTo(countBefore));

            grid.Dispose();
        }

        [Test]
        public void RejectedInsert_OnNonFinitePosition_LeavesTheGridUnchanged()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                grid.TryInsert(1, 0f, 0f, 0f, out _);

                Assert.That(grid.TryInsert(2, float.NaN, 0f, 0f, out SpatialHandle rejected), Is.False);
                Assert.That(rejected.IsValid, Is.False);
                Assert.That(grid.Count, Is.EqualTo(1));
                Assert.That(grid.ValidateStructure(), Is.True);
            }
        }

        [Test]
        public void FailedBuild_LeavesThePreviousSnapshotQueryable()
        {
            var random = new Random(91);
            var good = new SpatialEntry<int>[120];
            for (int i = 0; i < good.Length; i++)
            {
                good[i] = new SpatialEntry<int>(
                    (float)((random.NextDouble() * 60.0) - 30.0),
                    (float)((random.NextDouble() * 60.0) - 30.0),
                    (float)((random.NextDouble() * 60.0) - 30.0),
                    SpatialLayers.Default,
                    i);
            }

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 128))
            {
                snapshot.Build(good);

                var before = new int[128];
                int countBefore = snapshot.QuerySphere(0f, 0f, 0f, 200f, before, before.Length, SpatialLayers.All);
                var beforeSet = new HashSet<int>();
                for (int i = 0; i < countBefore; i++)
                {
                    beforeSet.Add(before[i]);
                }

                Assert.That(beforeSet.Count, Is.EqualTo(120));

                // 39 valid entries followed by one that maps outside the addressable span. The build fails after it
                // has already accepted most of the batch, which is exactly when a naive implementation corrupts itself.
                var bad = new SpatialEntry<int>[40];
                for (int i = 0; i < bad.Length - 1; i++)
                {
                    bad[i] = new SpatialEntry<int>(0f, 0f, 0f, SpatialLayers.Default, i);
                }

                bad[bad.Length - 1] = new SpatialEntry<int>(1e9f, 0f, 0f, SpatialLayers.Default, 39);

                Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.Build(bad));

                Assert.That(snapshot.Count, Is.EqualTo(120),
                    "A failed build must not change the element count.");

                var after = new int[128];
                int countAfter = snapshot.QuerySphere(0f, 0f, 0f, 200f, after, after.Length, SpatialLayers.All);
                var afterSet = new HashSet<int>();
                for (int i = 0; i < countAfter; i++)
                {
                    afterSet.Add(after[i]);
                }

                Assert.That(afterSet.SetEquals(beforeSet), Is.True,
                    "A failed build left the previous contents corrupted or partly overwritten.");
            }
        }

        [Test]
        public void FailedBuild_WithNonFiniteEntry_LeavesThePreviousSnapshotQueryable()
        {
            var random = new Random(17);
            var good = new SpatialEntry<int>[50];
            for (int i = 0; i < good.Length; i++)
            {
                good[i] = new SpatialEntry<int>(
                    (float)((random.NextDouble() * 20.0) - 10.0),
                    0f,
                    (float)((random.NextDouble() * 20.0) - 10.0),
                    SpatialLayers.Default,
                    i);
            }

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(2f), 64))
            {
                snapshot.Build(good);

                var bad = new SpatialEntry<int>[10];
                for (int i = 0; i < bad.Length; i++)
                {
                    bad[i] = new SpatialEntry<int>(0f, 0f, 0f, SpatialLayers.Default, i);
                }

                bad[5] = new SpatialEntry<int>(0f, float.PositiveInfinity, 0f, SpatialLayers.Default, 5);

                Assert.Throws<ArgumentException>(() => snapshot.Build(bad));
                Assert.That(snapshot.Count, Is.EqualTo(50));

                var buffer = new int[64];
                Assert.That(snapshot.QuerySphere(0f, 0f, 0f, 100f, buffer, buffer.Length, SpatialLayers.All),
                    Is.EqualTo(50), "The previous snapshot stopped answering correctly after a failed build.");
            }
        }

        [Test]
        public void UseAfterDispose_IsRejectedWithoutThrowingOnQueries()
        {
            var snapshot = new SpatialSnapshot<int>(new GridSettings(1f), 16);
            snapshot.Build(new[] { new SpatialEntry<int>(0f, 0f, 0f, SpatialLayers.Default, 1) });
            snapshot.Dispose();

            Assert.That(snapshot.Count, Is.Zero);
            Assert.That(snapshot.QuerySphere(0f, 0f, 0f, 10f, new int[4], 4, SpatialLayers.All), Is.Zero);
            Assert.That(snapshot.QueryBox(-1f, -1f, -1f, 1f, 1f, 1f, new int[4], 4, SpatialLayers.All), Is.Zero);
            Assert.DoesNotThrow(() => snapshot.Clear());
            Assert.Throws<ObjectDisposedException>(() =>
                snapshot.Build(new[] { new SpatialEntry<int>(0f, 0f, 0f, SpatialLayers.Default, 1) }));
        }
    }
}
