using System;
using System.Collections.Generic;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// The read-only snapshot backend, and the parity requirement that makes the backend contract meaningful: two
    /// different structures must answer the same query the same way.
    /// </summary>
    public sealed class SpatialSnapshotTests
    {
        private static SpatialEntry<int>[] BuildRandomEntries(int count, int seed, out float[] positions, out uint[] layers)
        {
            var random = new Random(seed);
            var entries = new SpatialEntry<int>[count];
            positions = new float[count * 3];
            layers = new uint[count];

            for (int i = 0; i < count; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 120.0) - 60.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 120.0) - 60.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 120.0) - 60.0);
                layers[i] = (i % 3) == 0 ? 1u : 2u;
                entries[i] = new SpatialEntry<int>(
                    positions[i * 3],
                    positions[i * 3 + 1],
                    positions[i * 3 + 2],
                    layers[i],
                    i);
            }

            return entries;
        }

        [Test]
        public void Build_PreservesEveryEntry()
        {
            SpatialEntry<int>[] entries = BuildRandomEntries(500, 11, out _, out _);

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 16))
            {
                snapshot.Build(entries);

                Assert.That(snapshot.Count, Is.EqualTo(500));
                Assert.That(snapshot.BuildCount, Is.EqualTo(1L));
                Assert.That(snapshot.Capacity, Is.GreaterThanOrEqualTo(500));
            }
        }

        [Test]
        public void SphereQuery_MatchesBruteForce()
        {
            const int ElementCount = 1200;
            SpatialEntry<int>[] entries = BuildRandomEntries(ElementCount, 22, out float[] positions, out uint[] layers);

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 64))
            {
                snapshot.Build(entries);

                var random = new Random(99);
                var buffer = new int[ElementCount];
                var expected = new HashSet<int>();
                var actual = new HashSet<int>();

                for (int trial = 0; trial < 60; trial++)
                {
                    float centerX = (float)((random.NextDouble() * 120.0) - 60.0);
                    float centerY = (float)((random.NextDouble() * 120.0) - 60.0);
                    float centerZ = (float)((random.NextDouble() * 120.0) - 60.0);
                    float radius = (float)(random.NextDouble() * 20.0);
                    uint mask = trial % 2 == 0 ? 1u : SpatialLayers.All;

                    int count = snapshot.QuerySphere(centerX, centerY, centerZ, radius, buffer, buffer.Length, mask);

                    expected.Clear();
                    actual.Clear();

                    float radiusSquared = radius * radius;
                    for (int i = 0; i < ElementCount; i++)
                    {
                        if ((layers[i] & mask) == 0u)
                        {
                            continue;
                        }

                        float dx = positions[i * 3] - centerX;
                        float dy = positions[i * 3 + 1] - centerY;
                        float dz = positions[i * 3 + 2] - centerZ;
                        if ((dx * dx) + (dy * dy) + (dz * dz) <= radiusSquared)
                        {
                            expected.Add(i);
                        }
                    }

                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(actual.Add(buffer[i]), Is.True, "A slot must not be returned twice.");
                    }

                    Assert.That(expected.IsSubsetOf(actual), Is.True, "The snapshot missed an element inside the radius.");
                }
            }
        }

        [Test]
        public void BoxQuery_MatchesBruteForce()
        {
            const int ElementCount = 900;
            SpatialEntry<int>[] entries = BuildRandomEntries(ElementCount, 33, out float[] positions, out _);

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(2f), 64))
            {
                snapshot.Build(entries);

                var random = new Random(7);
                var buffer = new int[ElementCount];
                var expected = new HashSet<int>();
                var actual = new HashSet<int>();

                for (int trial = 0; trial < 50; trial++)
                {
                    float minX = (float)((random.NextDouble() * 120.0) - 60.0);
                    float minY = (float)((random.NextDouble() * 120.0) - 60.0);
                    float minZ = (float)((random.NextDouble() * 120.0) - 60.0);
                    float maxX = minX + (float)(random.NextDouble() * 15.0);
                    float maxY = minY + (float)(random.NextDouble() * 15.0);
                    float maxZ = minZ + (float)(random.NextDouble() * 15.0);

                    int count = snapshot.QueryBox(minX, minY, minZ, maxX, maxY, maxZ, buffer, buffer.Length, SpatialLayers.All);

                    expected.Clear();
                    actual.Clear();
                    for (int i = 0; i < ElementCount; i++)
                    {
                        float x = positions[i * 3];
                        float y = positions[i * 3 + 1];
                        float z = positions[i * 3 + 2];
                        if (x >= minX && x <= maxX && y >= minY && y <= maxY && z >= minZ && z <= maxZ)
                        {
                            expected.Add(i);
                        }
                    }

                    for (int i = 0; i < count; i++)
                    {
                        actual.Add(buffer[i]);
                    }

                    Assert.That(expected.IsSubsetOf(actual), Is.True, "Box query missed an element inside the box.");
                    Assert.That(actual.IsSubsetOf(expected), Is.True, "Box query returned an element outside the box.");
                }
            }
        }

        [Test]
        public void NearestQuery_MatchesABruteForceSort()
        {
            const int ElementCount = 700;
            SpatialEntry<int>[] entries = BuildRandomEntries(ElementCount, 44, out float[] positions, out _);

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 64))
            {
                snapshot.Build(entries);

                var random = new Random(1234);
                var buffer = new int[8];
                var distances = new float[8];

                for (int trial = 0; trial < 40; trial++)
                {
                    float centerX = (float)((random.NextDouble() * 120.0) - 60.0);
                    float centerY = (float)((random.NextDouble() * 120.0) - 60.0);
                    float centerZ = (float)((random.NextDouble() * 120.0) - 60.0);
                    int k = 1 + (trial % 7);

                    int count = snapshot.QueryNearest(centerX, centerY, centerZ, 1000f, k, SpatialLayers.All, buffer, distances);

                    var expected = new List<float>(ElementCount);
                    for (int i = 0; i < ElementCount; i++)
                    {
                        float dx = positions[i * 3] - centerX;
                        float dy = positions[i * 3 + 1] - centerY;
                        float dz = positions[i * 3 + 2] - centerZ;
                        expected.Add((dx * dx) + (dy * dy) + (dz * dz));
                    }

                    expected.Sort();

                    Assert.That(count, Is.EqualTo(Math.Min(k, ElementCount)));
                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(distances[i], Is.EqualTo(expected[i]).Within(1e-2f),
                            "Nearest distance diverged at trial " + trial + " index " + i + ".");
                    }
                }
            }
        }

        [Test]
        public void Build_RejectsCellsOutsideTheAddressableSpan()
        {
            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(1f), 4))
            {
                var far = new[]
                {
                    new SpatialEntry<int>(1e9f, 0f, 0f, SpatialLayers.Default, 0)
                };

                Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.Build(far));
                Assert.That(SpatialSnapshot<int>.AxisLimitCells, Is.GreaterThan(0));
            }
        }

        [Test]
        public void Build_RejectsNonFinitePositions()
        {
            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(1f), 4))
            {
                var broken = new[]
                {
                    new SpatialEntry<int>(float.NaN, 0f, 0f, SpatialLayers.Default, 0)
                };

                Assert.Throws<ArgumentException>(() => snapshot.Build(broken));
            }
        }

        [Test]
        public void Rebuild_ReusesArraysAndStaysCorrect()
        {
            const int ElementCount = 512;
            SpatialEntry<int>[] entries = BuildRandomEntries(ElementCount, 55, out _, out _);

            using (var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), ElementCount))
            {
                snapshot.Build(entries);
                long before = GC.GetTotalMemory(true);
                for (int i = 0; i < 20; i++)
                {
                    snapshot.Build(entries);
                }

                long delta = GC.GetTotalMemory(false) - before;

                Assert.That(delta, Is.LessThan(65_536L), "A same-size rebuild allocated " + delta + " B; it should reuse arrays.");
                Assert.That(snapshot.Count, Is.EqualTo(ElementCount));

                var buffer = new int[16];
                Assert.That(snapshot.QuerySphere(0f, 0f, 0f, 200f, buffer, 16, SpatialLayers.All), Is.GreaterThan(0));
            }
        }

        [Test]
        public void ClearAndDispose_BehaveLikeTheGrid()
        {
            SpatialEntry<int>[] entries = BuildRandomEntries(100, 66, out _, out _);

            var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 128);
            snapshot.Build(entries);
            snapshot.Clear();

            Assert.That(snapshot.Count, Is.Zero);
            Assert.That(snapshot.QuerySphere(0f, 0f, 0f, 1000f, new int[8], 8, SpatialLayers.All), Is.Zero);

            snapshot.Build(entries);
            snapshot.Dispose();

            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(snapshot.Count, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => snapshot.Build(entries));
        }

        [Test]
        public void BothBackends_AgreeOnSphereAndNearest()
        {
            const int ElementCount = 600;
            SpatialEntry<int>[] entries = BuildRandomEntries(ElementCount, 77, out float[] positions, out uint[] layers);

            var grid = new SpatialHashGrid<int>(new GridSettings(3f), 64);
            for (int i = 0; i < ElementCount; i++)
            {
                grid.TryInsert(
                    entries[i].Item,
                    entries[i].X,
                    entries[i].Y,
                    entries[i].Z,
                    entries[i].LayerMask,
                    out _);
            }

            var snapshot = new SpatialSnapshot<int>(new GridSettings(3f), 64);
            snapshot.Build(entries);

            var random = new Random(4321);
            var gridBuffer = new int[ElementCount];
            var snapshotBuffer = new int[ElementCount];
            var gridDistances = new float[6];
            var snapshotDistances = new float[6];

            for (int trial = 0; trial < 40; trial++)
            {
                float centerX = (float)((random.NextDouble() * 120.0) - 60.0);
                float centerY = (float)((random.NextDouble() * 120.0) - 60.0);
                float centerZ = (float)((random.NextDouble() * 120.0) - 60.0);
                float radius = (float)(random.NextDouble() * 18.0);
                uint mask = trial % 2 == 0 ? 1u : SpatialLayers.All;

                int gridCount = grid.QuerySphere(centerX, centerY, centerZ, radius, gridBuffer, gridBuffer.Length, mask);
                int snapshotCount = snapshot.QuerySphere(centerX, centerY, centerZ, radius, snapshotBuffer, snapshotBuffer.Length, mask);

                var gridSet = new HashSet<int>();
                for (int i = 0; i < gridCount; i++)
                {
                    gridSet.Add(gridBuffer[i]);
                }

                var snapshotSet = new HashSet<int>();
                for (int i = 0; i < snapshotCount; i++)
                {
                    snapshotSet.Add(snapshotBuffer[i]);
                }

                Assert.That(gridSet.SetEquals(snapshotSet), Is.True,
                    "The two backends disagreed on a sphere query at trial " + trial + ".");

                int gridNearest = grid.QueryNearest(centerX, centerY, centerZ, radius, 4, mask, gridBuffer, gridDistances);
                int snapshotNearest = snapshot.QueryNearest(centerX, centerY, centerZ, radius, 4, mask, snapshotBuffer, snapshotDistances);

                Assert.That(gridNearest, Is.EqualTo(snapshotNearest));
                for (int i = 0; i < gridNearest; i++)
                {
                    Assert.That(gridDistances[i], Is.EqualTo(snapshotDistances[i]).Within(1e-3f));
                }
            }

            grid.Dispose();
            snapshot.Dispose();
        }
    }
}
