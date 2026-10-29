using System;
using System.Collections.Generic;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Accuracy tests: results must not depend on cell size, insertion order, sign of the coordinate, or whether a
    /// candidate happens to sit exactly on a partition boundary. Every case is compared against a brute-force scan
    /// over the same positions, and the hard rule is "the grid may return a boundary extra, it may never miss".
    /// </summary>
    public sealed class SpatialAccuracyTests
    {
        private const float QueryTolerance = 1e-5f;

        private static bool InsideTolerant(float distanceSquared, float radiusSquared)
        {
            return distanceSquared <= (radiusSquared * (1f + QueryTolerance)) + QueryTolerance;
        }

        private static void AssertNoMiss(
            int[] buffer,
            int count,
            float[] positions,
            int elementCount,
            float cx,
            float cy,
            float cz,
            float radius,
            uint mask,
            uint[] layers,
            string label)
        {
            var returned = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                Assert.That(returned.Add(buffer[i]), Is.True, "Duplicate result: " + label);
            }

            float radiusSquared = radius * radius;
            float tolerated = (radiusSquared * (1f + QueryTolerance)) + QueryTolerance;

            for (int i = 0; i < elementCount; i++)
            {
                if ((layers[i] & mask) == 0u)
                {
                    continue;
                }

                float dx = positions[i * 3] - cx;
                float dy = positions[i * 3 + 1] - cy;
                float dz = positions[i * 3 + 2] - cz;
                float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

                if (distanceSquared <= radiusSquared)
                {
                    Assert.That(returned.Contains(i), Is.True,
                        "Missed element " + i + " at distanceSquared=" + distanceSquared + " for " + label);
                }
                else if (returned.Contains(i))
                {
                    Assert.That(distanceSquared, Is.LessThanOrEqualTo(tolerated),
                        "Returned element " + i + " beyond the tolerance band for " + label);
                }
            }
        }

        [Test]
        public void BoundaryAlignedPositions_AreNeverMissed()
        {
            // Every position sits on an exact 0.25 lattice, so a large fraction of them land exactly on cell
            // boundaries for a 1-unit cell, and query radii are chosen from exact lattice distances.
            const int Side = 12;
            const float Step = 0.25f;
            int elementCount = Side * Side * Side;

            var positions = new float[elementCount * 3];
            var layers = new uint[elementCount];
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 256);

            int index = 0;
            for (int x = 0; x < Side; x++)
            {
                for (int y = 0; y < Side; y++)
                {
                    for (int z = 0; z < Side; z++)
                    {
                        positions[index * 3] = x * Step;
                        positions[index * 3 + 1] = y * Step;
                        positions[index * 3 + 2] = z * Step;
                        layers[index] = SpatialLayers.Default;
                        grid.TryInsert(index, positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2], out _);
                        index++;
                    }
                }
            }

            var buffer = new int[elementCount];
            float[] radii = { 0f, 0.25f, 0.5f, 0.75f, 1f, 1.25f, 2f };
            float[] centers = { 0f, 0.25f, 0.5f, 0.75f, 1f, 1.5f };

            for (int c = 0; c < centers.Length; c++)
            {
                for (int r = 0; r < radii.Length; r++)
                {
                    int count = grid.QuerySphere(centers[c], centers[c], centers[c], radii[r], buffer, buffer.Length);
                    AssertNoMiss(buffer, count, positions, elementCount,
                        centers[c], centers[c], centers[c], radii[r], SpatialLayers.All, layers,
                        "lattice center=" + centers[c] + " radius=" + radii[r]);
                }
            }

            grid.Dispose();
        }

        [Test]
        public void CellSize_NeverChangesWhichElementsAreFound()
        {
            const int ElementCount = 800;
            const int QueryCount = 12;

            var random = new Random(24680);
            var positions = new float[ElementCount * 3];
            var layers = new uint[ElementCount];

            for (int i = 0; i < ElementCount; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 60.0) - 30.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 60.0) - 30.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 60.0) - 30.0);
                layers[i] = SpatialLayers.Default;
            }

            // The queries are generated once and replayed against every cell size. Regenerating them inside the loop
            // would compare different questions.
            var queries = new float[QueryCount * 4];
            for (int i = 0; i < QueryCount; i++)
            {
                queries[i * 4] = (float)((random.NextDouble() * 60.0) - 30.0);
                queries[i * 4 + 1] = (float)((random.NextDouble() * 60.0) - 30.0);
                queries[i * 4 + 2] = (float)((random.NextDouble() * 60.0) - 30.0);
                queries[i * 4 + 3] = (float)(random.NextDouble() * 12.0);
            }

            float[] cellSizes = { 0.25f, 1f, 4f, 37f };
            var buffer = new int[ElementCount];

            for (int c = 0; c < cellSizes.Length; c++)
            {
                using (var grid = new SpatialHashGrid<int>(new GridSettings(cellSizes[c]), 128))
                {
                    for (int i = 0; i < ElementCount; i++)
                    {
                        grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out _);
                    }

                    for (int trial = 0; trial < QueryCount; trial++)
                    {
                        int count = grid.QuerySphere(
                            queries[trial * 4],
                            queries[trial * 4 + 1],
                            queries[trial * 4 + 2],
                            queries[trial * 4 + 3],
                            buffer,
                            buffer.Length);

                        // Cell size changes which cells are visited, never which elements are inside the radius. The
                        // tolerance band may admit a boundary extra, so the assertion is no-miss plus band-limited
                        // extras rather than exact set equality across cell sizes.
                        AssertNoMiss(buffer, count, positions, ElementCount,
                            queries[trial * 4], queries[trial * 4 + 1], queries[trial * 4 + 2], queries[trial * 4 + 3],
                            SpatialLayers.All, layers, "cellSize=" + cellSizes[c] + " trial=" + trial);
                    }
                }
            }
        }

        [Test]
        public void InsertionOrder_DoesNotChangeTheResultSet()
        {
            const int ElementCount = 600;
            var random = new Random(13579);
            var positions = new float[ElementCount * 3];
            for (int i = 0; i < ElementCount; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 50.0) - 25.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 50.0) - 25.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 50.0) - 25.0);
            }

            var order = new int[ElementCount];
            for (int i = 0; i < ElementCount; i++)
            {
                order[i] = i;
            }

            var actual = new HashSet<int>();
            var buffer = new int[ElementCount];
            int expectedAtTrial;

            for (int pass = 0; pass < 4; pass++)
            {
                if (pass > 0)
                {
                    // Fisher-Yates, deterministic per pass.
                    for (int i = order.Length - 1; i > 0; i--)
                    {
                        int swap = random.Next(i + 1);
                        int temporary = order[i];
                        order[i] = order[swap];
                        order[swap] = temporary;
                    }
                }

                using (var grid = new SpatialHashGrid<int>(new GridSettings(2f), 64))
                {
                    for (int i = 0; i < ElementCount; i++)
                    {
                        int id = order[i];
                        grid.TryInsert(id, positions[id * 3], positions[id * 3 + 1], positions[id * 3 + 2], out _);
                    }

                    for (int trial = 0; trial < 12; trial++)
                    {
                        int count = grid.QuerySphere(0f, 0f, 0f, 15f + trial, buffer, buffer.Length);

                        actual.Clear();
                        for (int i = 0; i < count; i++)
                        {
                            actual.Add(buffer[i]);
                        }

                        expectedAtTrial = ReferenceCount(positions, 15f + trial);
                        Assert.That(actual.Count, Is.EqualTo(expectedAtTrial),
                            "Insertion order changed the result count at pass " + pass + " trial " + trial + ".");
                    }
                }
            }
        }

        private static int ReferenceCount(float[] positions, float radius)
        {
            int count = 0;
            float radiusSquared = radius * radius;
            int elementCount = positions.Length / 3;
            for (int i = 0; i < elementCount; i++)
            {
                float dx = positions[i * 3];
                float dy = positions[i * 3 + 1];
                float dz = positions[i * 3 + 2];
                if ((dx * dx) + (dy * dy) + (dz * dz) <= radiusSquared)
                {
                    count++;
                }
            }

            return count;
        }

        [Test]
        public void NegativeCoordinates_MirrorTheResultSet()
        {
            const int ElementCount = 400;
            var random = new Random(97531);
            var positive = new float[ElementCount * 3];
            var negative = new float[ElementCount * 3];

            for (int i = 0; i < ElementCount; i++)
            {
                positive[i * 3] = (float)(random.NextDouble() * 40.0);
                positive[i * 3 + 1] = (float)(random.NextDouble() * 40.0);
                positive[i * 3 + 2] = (float)(random.NextDouble() * 40.0);
                negative[i * 3] = -positive[i * 3];
                negative[i * 3 + 1] = -positive[i * 3 + 1];
                negative[i * 3 + 2] = -positive[i * 3 + 2];
            }

            using (var positiveGrid = new SpatialHashGrid<int>(new GridSettings(3f), 128))
            using (var negativeGrid = new SpatialHashGrid<int>(new GridSettings(3f), 128))
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    positiveGrid.TryInsert(i, positive[i * 3], positive[i * 3 + 1], positive[i * 3 + 2], out _);
                    negativeGrid.TryInsert(i, negative[i * 3], negative[i * 3 + 1], negative[i * 3 + 2], out _);
                }

                Assert.That(positiveGrid.ValidateStructure(), Is.True);
                Assert.That(negativeGrid.ValidateStructure(), Is.True, "Negative cell indices broke an invariant.");

                var bufferA = new int[ElementCount];
                var bufferB = new int[ElementCount];
                var setA = new HashSet<int>();
                var setB = new HashSet<int>();

                for (int trial = 0; trial < 10; trial++)
                {
                    float radius = 5f + trial;

                    int countA = positiveGrid.QuerySphere(0f, 0f, 0f, radius, bufferA, bufferA.Length);
                    int countB = negativeGrid.QuerySphere(0f, 0f, 0f, radius, bufferB, bufferB.Length);

                    setA.Clear();
                    for (int i = 0; i < countA; i++)
                    {
                        setA.Add(bufferA[i]);
                    }

                    setB.Clear();
                    for (int i = 0; i < countB; i++)
                    {
                        setB.Add(bufferB[i]);
                    }

                    Assert.That(setB.SetEquals(setA), Is.True,
                        "A mirrored world returned a different set at radius " + radius + ".");
                }
            }
        }

        [Test]
        public void LargeCoordinates_StayAccurateWithinTheDocumentedRange()
        {
            const int ElementCount = 500;
            var random = new Random(86420);
            var positions = new float[ElementCount * 3];

            for (int i = 0; i < ElementCount; i++)
            {
                // The README keeps |position| under roughly 8192 for float precision; this stays inside that.
                positions[i * 3] = (float)((random.NextDouble() * 16000.0) - 8000.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 16000.0) - 8000.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 16000.0) - 8000.0);
            }

            using (var grid = new SpatialHashGrid<int>(new GridSettings(8f, 1000f, -1000f, 0f), 256))
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out _);
                }

                var buffer = new int[ElementCount];
                var layers = new uint[ElementCount];
                for (int i = 0; i < ElementCount; i++)
                {
                    layers[i] = SpatialLayers.Default;
                }

                for (int trial = 0; trial < 15; trial++)
                {
                    float radius = 50f + (trial * 37f);
                    int count = grid.QuerySphere(0f, 0f, 0f, radius, buffer, buffer.Length);
                    AssertNoMiss(buffer, count, positions, ElementCount, 0f, 0f, 0f, radius,
                        SpatialLayers.All, layers, "large coordinates radius=" + radius);
                }
            }
        }

        [Test]
        public void CoincidentElements_AreAllReturned()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                for (int i = 0; i < 50; i++)
                {
                    grid.TryInsert(i, 3.25f, -2.5f, 0f, out _);
                }

                var buffer = new int[64];
                Assert.That(grid.QuerySphere(3.25f, -2.5f, 0f, 0f, buffer, buffer.Length), Is.EqualTo(50),
                    "Coincident elements must all be returned, not collapsed.");
            }
        }

        [Test]
        public void CellsSharingOneBucket_StillReturnTheirOwnElements()
        {
            // A hash collision means "one more probe", never "two cells are the same cell". This test forces six
            // different cells into a single bucket and requires each lookup to return its own element, so a future
            // change that compares hashes instead of full keys fails here instead of silently returning neighbours.
            using (var grid = new SpatialHashGrid<int>(new GridSettings(10f), 16))
            {
                grid.TryInsert(-1, 5000f, 5000f, 5000f, out _);

                int capacity = grid.BucketCapacity;
                int mask = capacity - 1;

                var byBucket = new Dictionary<int, List<CellCoordinate>>();
                List<CellCoordinate> chosen = null;
                for (int x = 0; x < 400 && chosen == null; x++)
                {
                    for (int z = 0; z < 400; z++)
                    {
                        var cell = new CellCoordinate(x, 0, z);
                        int bucket = (int)(cell.ComputeHash() & (uint)mask);
                        if (!byBucket.TryGetValue(bucket, out List<CellCoordinate> list))
                        {
                            list = new List<CellCoordinate>(8);
                            byBucket[bucket] = list;
                        }

                        list.Add(cell);
                        if (list.Count == 6)
                        {
                            chosen = list;
                            break;
                        }
                    }
                }

                Assert.That(chosen, Is.Not.Null, "No bucket received six cells; the test setup is wrong.");

                for (int i = 0; i < chosen.Count; i++)
                {
                    CellCoordinate cell = chosen[i];
                    Assert.That(
                        grid.TryInsert(i, (cell.X + 0.5f) * 10f, 5f, (cell.Z + 0.5f) * 10f, out _),
                        Is.True);
                }

                Assert.That(grid.BucketCapacity, Is.EqualTo(capacity),
                    "The bucket table grew, so the chosen cells may no longer share a bucket.");

                var buffer = new int[8];
                for (int i = 0; i < chosen.Count; i++)
                {
                    CellCoordinate cell = chosen[i];
                    float centerX = (cell.X + 0.5f) * 10f;
                    float centerZ = (cell.Z + 0.5f) * 10f;

                    int count = grid.QuerySphere(centerX, 5f, centerZ, 0f, buffer, buffer.Length);
                    Assert.That(count, Is.EqualTo(1), "Cell " + cell + " returned " + count + " elements.");
                    Assert.That(buffer[0], Is.EqualTo(i), "Cell " + cell + " returned its bucket neighbour.");
                }

                Assert.That(grid.ValidateStructure(), Is.True);
            }
        }

        [Test]
        public void ZeroRadius_ReturnsOnlyTheExactCenter()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                grid.TryInsert(1, 0.5f, 0.5f, 0.5f, out _);
                grid.TryInsert(2, 0.5f, 0.5f, 0.6f, out _);

                var buffer = new int[8];
                int count = grid.QuerySphere(0.5f, 0.5f, 0.5f, 0f, buffer, buffer.Length);

                Assert.That(count, Is.EqualTo(1));
                Assert.That(buffer[0], Is.EqualTo(1));
            }
        }

        [Test]
        public void LargeMagnitude_IndexAddsNoErrorOfItsOwn()
        {
            // The grid stores whatever float the caller supplied. Far from the origin a float carries less resolution,
            // but the index must not lose anything on top of that: its result has to match a brute-force scan over
            // the very same stored values. This separates "float is coarse here" from "the grid is wrong here".
            float[] magnitudes = { 100_000f, 1_000_000f };
            const int ElementCount = 600;
            const float Extent = 200f;

            foreach (float magnitude in magnitudes)
            {
                var random = new Random(4711);
                var positions = new float[ElementCount * 3];
                var layers = new uint[ElementCount];

                using (var grid = new SpatialHashGrid<int>(new GridSettings(2f), ElementCount))
                {
                    for (int i = 0; i < ElementCount; i++)
                    {
                        positions[i * 3] = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        positions[i * 3 + 1] = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        positions[i * 3 + 2] = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        layers[i] = SpatialLayers.Default;
                        grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out _);
                    }

                    var buffer = new int[ElementCount];
                    for (int trial = 0; trial < 10; trial++)
                    {
                        float cx = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        float cy = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        float cz = magnitude + (float)((random.NextDouble() * Extent) - (Extent * 0.5));
                        float radius = 20f + (trial * 5f);

                        int count = grid.QuerySphere(cx, cy, cz, radius, buffer, buffer.Length);
                        AssertNoMiss(buffer, count, positions, ElementCount, cx, cy, cz, radius,
                            SpatialLayers.All, layers, "magnitude=" + magnitude + " trial=" + trial);
                    }
                }
            }
        }

        [Test]
        public void Rebase_KeepsHandlesAndResults()
        {
            const int ElementCount = 500;
            var random = new Random(1234);
            var positions = new float[ElementCount * 3];
            var handles = new SpatialHandle[ElementCount];

            using (var grid = new SpatialHashGrid<int>(new GridSettings(2f), ElementCount + 64))
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    positions[i * 3] = (float)((random.NextDouble() * 4000.0) - 2000.0);
                    positions[i * 3 + 1] = (float)((random.NextDouble() * 4000.0) - 2000.0);
                    positions[i * 3 + 2] = (float)((random.NextDouble() * 4000.0) - 2000.0);
                    grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out handles[i]);
                }

                var buffer = new int[ElementCount];
                var before = new HashSet<int>();
                int countBefore = grid.QuerySphere(0f, 0f, 0f, 300f, buffer, buffer.Length);
                for (int i = 0; i < countBefore; i++)
                {
                    before.Add(buffer[i]);
                }

                // Rebasing is the documented answer to float resolution drifting far from the origin.
                grid.SetOrigin(1500f, -800f, 250f);

                for (int i = 0; i < ElementCount; i++)
                {
                    Assert.That(grid.TryGetPosition(handles[i], out _, out _, out _), Is.True,
                        "SetOrigin must not invalidate handles.");
                }

                var after = new HashSet<int>();
                int countAfter = grid.QuerySphere(0f, 0f, 0f, 300f, buffer, buffer.Length);
                for (int i = 0; i < countAfter; i++)
                {
                    after.Add(buffer[i]);
                }

                Assert.That(after.SetEquals(before), Is.True,
                    "Rebasing the origin moved the cell grid but must not change which elements are inside a radius.");
                Assert.That(grid.ValidateStructure(), Is.True);
            }
        }
    }
}
