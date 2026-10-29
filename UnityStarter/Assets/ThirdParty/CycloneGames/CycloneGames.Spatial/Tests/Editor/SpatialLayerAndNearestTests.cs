using System;
using System.Collections.Generic;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Layer filtering and the nearest-neighbour query: the two features that decide whether this index can serve
    /// an AI target picker without the caller re-filtering or re-sorting truncated results.
    /// </summary>
    public sealed class SpatialLayerAndNearestTests
    {
        private const uint Enemies = 1u << 0;
        private const uint Allies = 1u << 1;
        private const uint Props = 1u << 2;

        [Test]
        public void LayerMask_FiltersEveryQueryShape()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                grid.TryInsert(1, 0f, 0f, 0f, Enemies, out _);
                grid.TryInsert(2, 0.1f, 0f, 0f, Allies, out _);
                grid.TryInsert(3, 0.2f, 0f, 0f, Props, out _);
                grid.TryInsert(4, 0.3f, 0f, 0f, Enemies | Props, out _);

                var buffer = new int[16];

                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, Enemies), Is.EqualTo(2));
                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, Allies), Is.EqualTo(1));
                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, Props), Is.EqualTo(2));
                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, Enemies | Allies), Is.EqualTo(3));
                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, SpatialLayers.All), Is.EqualTo(4));
                Assert.That(grid.QuerySphere(0.1f, 0f, 0f, 5f, buffer, 16, 0u), Is.Zero,
                    "A zero mask must match nothing rather than fall back to unfiltered.");

                var box = new int[16];
                Assert.That(grid.QueryBox(-1f, -1f, -1f, 1f, 1f, 1f, box, 16, Enemies), Is.EqualTo(2));
            }
        }

        [Test]
        public void LayerMask_AppliesToTheListAndNearestPaths()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                for (int i = 0; i < 10; i++)
                {
                    grid.TryInsert(i, i * 0.5f, 0f, 0f, i % 2 == 0 ? Enemies : Allies, out _);
                }

                var list = new List<int>(16);
                Assert.That(grid.QuerySphere(0f, 0f, 0f, 100f, list, int.MaxValue, Enemies), Is.EqualTo(5));

                var results = new int[8];
                var distances = new float[8];
                int count = grid.QueryNearest(0f, 0f, 0f, 100f, 3, Allies, results, distances);
                Assert.That(count, Is.EqualTo(3));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(results[i] % 2, Is.EqualTo(1), "Nearest must respect the layer mask.");
                }
            }
        }

        [Test]
        public void Layer_ZeroIsNormalisedAndRejectedOnMutation()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                grid.TryInsert(1, 0f, 0f, 0f, 0u, out SpatialHandle handle);

                Assert.That(grid.TryGetLayer(handle, out uint stored), Is.True);
                Assert.That(stored, Is.EqualTo(SpatialLayers.Default), "A zero layer would be invisible to every query.");

                Assert.That(grid.TrySetLayer(handle, 0u), Is.False);
                Assert.That(grid.TrySetLayer(SpatialHandle.Invalid, Enemies), Is.False);
                Assert.That(grid.TrySetLayer(handle, Props), Is.True);
                Assert.That(grid.TryGetLayer(handle, out uint updated), Is.True);
                Assert.That(updated, Is.EqualTo(Props));
            }
        }

        [Test]
        public void Nearest_ReturnsAscendingSquaredDistances()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                grid.TryInsert(10, 3f, 0f, 0f, SpatialLayers.Default, out _);
                grid.TryInsert(11, 1f, 0f, 0f, SpatialLayers.Default, out _);
                grid.TryInsert(12, 2f, 0f, 0f, SpatialLayers.Default, out _);
                grid.TryInsert(13, 9f, 0f, 0f, SpatialLayers.Default, out _);

                var results = new int[3];
                var distances = new float[3];
                int count = grid.QueryNearest(0f, 0f, 0f, 10f, 3, SpatialLayers.All, results, distances);

                Assert.That(count, Is.EqualTo(3));
                Assert.That(results[0], Is.EqualTo(11));
                Assert.That(results[1], Is.EqualTo(12));
                Assert.That(results[2], Is.EqualTo(10));
                Assert.That(distances[0], Is.LessThanOrEqualTo(distances[1]));
                Assert.That(distances[1], Is.LessThanOrEqualTo(distances[2]));
                Assert.That(distances[0], Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test]
        public void Nearest_RespectsRadiusAndBufferLimits()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                for (int i = 1; i <= 20; i++)
                {
                    grid.TryInsert(i, i, 0f, 0f, SpatialLayers.Default, out _);
                }

                var results = new int[5];
                var distances = new float[5];

                Assert.That(grid.QueryNearest(0f, 0f, 0f, 3.5f, 5, SpatialLayers.All, results, distances), Is.EqualTo(3));
                Assert.That(grid.QueryNearest(0f, 0f, 0f, 100f, 5, SpatialLayers.All, results, distances), Is.EqualTo(5));

                var small = new int[2];
                var smallDistances = new float[2];
                Assert.That(grid.QueryNearest(0f, 0f, 0f, 100f, 99, SpatialLayers.All, small, smallDistances), Is.EqualTo(2),
                    "The shorter buffer must bound the result.");
                Assert.That(grid.QueryNearest(0f, 0f, 0f, -1f, 5, SpatialLayers.All, results, distances), Is.Zero);
            }
        }

        [Test]
        public void Nearest_MatchesABruteForceSort()
        {
            const int ElementCount = 800;
            var random = new Random(31337);
            var positions = new float[ElementCount * 3];

            using (var grid = new SpatialHashGrid<int>(new GridSettings(2f), 64))
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    positions[i * 3] = (float)((random.NextDouble() * 100.0) - 50.0);
                    positions[i * 3 + 1] = (float)((random.NextDouble() * 100.0) - 50.0);
                    positions[i * 3 + 2] = (float)((random.NextDouble() * 100.0) - 50.0);
                    grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], SpatialLayers.Default, out _);
                }

                var results = new int[16];
                var distances = new float[16];

                for (int trial = 0; trial < 40; trial++)
                {
                    float centerX = (float)((random.NextDouble() * 100.0) - 50.0);
                    float centerY = (float)((random.NextDouble() * 100.0) - 50.0);
                    float centerZ = (float)((random.NextDouble() * 100.0) - 50.0);
                    float radius = (float)(random.NextDouble() * 20.0) + 1f;
                    int k = 1 + (trial % 10);

                    int count = grid.QueryNearest(centerX, centerY, centerZ, radius, k, SpatialLayers.All, results, distances);

                    var expected = new List<float>();
                    float radiusSquared = radius * radius;
                    for (int i = 0; i < ElementCount; i++)
                    {
                        float dx = positions[i * 3] - centerX;
                        float dy = positions[i * 3 + 1] - centerY;
                        float dz = positions[i * 3 + 2] - centerZ;
                        float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                        if (distanceSquared <= radiusSquared)
                        {
                            expected.Add(distanceSquared);
                        }
                    }

                    expected.Sort();

                    int expectedCount = Math.Min(k, expected.Count);
                    Assert.That(count, Is.EqualTo(expectedCount), "Nearest count diverged at trial " + trial + ".");

                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(distances[i], Is.EqualTo(expected[i]).Within(1e-2f),
                            "Nearest distance diverged at trial " + trial + " index " + i + ".");
                    }
                }
            }
        }
    }
}
