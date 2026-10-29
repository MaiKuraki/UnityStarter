using System;
using System.Collections.Generic;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.Spatial.Tests.Editor
{
    /// <summary>
    /// Behaviour of the mutable index: lifetime, queries and the guarantees callers are allowed to rely on.
    /// </summary>
    public sealed class SpatialHashGridTests
    {
        [Test]
        public void Insert_AssignsLiveHandles()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(2f), 64))
            {
                for (int i = 0; i < 1000; i++)
                {
                    Assert.That(grid.TryInsert(i, i * 0.37f, i * -0.11f, i * 0.53f, out SpatialHandle handle), Is.True);
                    Assert.That(handle.IsValid, Is.True);
                }

                Assert.That(grid.Count, Is.EqualTo(1000));
                Assert.That(grid.ValidateStructure(), Is.True);
            }
        }

        [Test]
        public void TryGetPosition_ReturnsStoredPosition()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                grid.TryInsert(7, 1.25f, -2.5f, 3.75f, out SpatialHandle handle);

                Assert.That(grid.TryGetPosition(handle, out float x, out float y, out float z), Is.True);
                Assert.That(x, Is.EqualTo(1.25f));
                Assert.That(y, Is.EqualTo(-2.5f));
                Assert.That(z, Is.EqualTo(3.75f));
            }
        }

        [Test]
        public void Remove_InvalidatesHandleAndRecyclesSlot()
        {
            using (var grid = new SpatialHashGrid<string>(new GridSettings(1f), 8))
            {
                grid.TryInsert("first", 0f, 0f, 0f, out SpatialHandle first);

                Assert.That(grid.TryRemove(first), Is.True);
                Assert.That(grid.TryRemove(first), Is.False, "Double removal must fail instead of corrupting the grid.");
                Assert.That(grid.TryUpdate(first, 1f, 1f, 1f), Is.False);
                Assert.That(grid.TryGetPosition(first, out _, out _, out _), Is.False);
                Assert.That(grid.Count, Is.Zero);

                grid.TryInsert("second", 0f, 0f, 0f, out SpatialHandle second);
                Assert.That(second.Slot, Is.EqualTo(first.Slot), "The recycled slot should be reused.");
                Assert.That(second.Generation, Is.Not.EqualTo(first.Generation), "Reuse must advance the generation.");
                Assert.That(grid.TryRemove(first), Is.False, "A stale handle must not remove the new element.");
                Assert.That(grid.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void InvalidHandle_IsRejectedByEveryMutation()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                Assert.That(SpatialHandle.Invalid.IsValid, Is.False);
                Assert.That(grid.TryUpdate(SpatialHandle.Invalid, 0f, 0f, 0f), Is.False);
                Assert.That(grid.TryRemove(SpatialHandle.Invalid), Is.False);
                Assert.That(grid.TryGetPosition(SpatialHandle.Invalid, out _, out _, out _), Is.False);
            }
        }

        [Test]
        public void DuplicatePayloads_AreIndependent()
        {
            using (var grid = new SpatialHashGrid<string>(new GridSettings(1f), 8))
            {
                grid.TryInsert("same", 0f, 0f, 0f, out SpatialHandle first);
                grid.TryInsert("same", 0f, 0f, 0f, out SpatialHandle second);

                Assert.That(first.Slot, Is.Not.EqualTo(second.Slot));
                Assert.That(grid.Count, Is.EqualTo(2));

                Assert.That(grid.TryRemove(first), Is.True);
                Assert.That(grid.Count, Is.EqualTo(1));
                Assert.That(grid.TryGetPosition(second, out _, out _, out _), Is.True);
            }
        }

        [Test]
        public void Clear_DropsElementsKeepsCapacityAndKillsHandles()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64);
            var handles = new SpatialHandle[500];
            for (int i = 0; i < handles.Length; i++)
            {
                grid.TryInsert(i, i * 0.25f, 0f, 0f, out handles[i]);
            }

            int capacityBefore = grid.Capacity;
            grid.Clear();

            Assert.That(grid.Count, Is.Zero);
            Assert.That(grid.Capacity, Is.EqualTo(capacityBefore));
            Assert.That(grid.ValidateStructure(), Is.True);
            Assert.That(grid.TryRemove(handles[10]), Is.False, "Handles issued before Clear must be dead.");

            grid.TryInsert(999, 0f, 0f, 0f, out SpatialHandle fresh);
            Assert.That(fresh.IsValid, Is.True);
            Assert.That(handles[0], Is.Not.EqualTo(fresh));
            Assert.That(grid.ValidateStructure(), Is.True);

            grid.Dispose();
        }

        [Test]
        public void TrimExcess_ShrinksAfterClearAndKeepsLiveHandles()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64);
            for (int i = 0; i < 400; i++)
            {
                grid.TryInsert(i, i * 0.25f, 0f, 0f, out _);
            }

            Assert.That(grid.TrimExcess(), Is.False, "A populated grid cannot shrink without relocating elements.");

            grid.Clear();
            grid.TryInsert(1, 0f, 0f, 0f, out SpatialHandle live);

            Assert.That(grid.TrimExcess(), Is.True);
            Assert.That(grid.TryGetPosition(live, out _, out _, out _), Is.True, "Shrinking must not relocate live elements.");
            Assert.That(grid.ValidateStructure(), Is.True);

            grid.Dispose();
        }

        [Test]
        public void SetOrigin_KeepsHandlesAndMovesCells()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64);
            grid.TryInsert(1, 10f, 0f, 0f, out SpatialHandle first);
            grid.TryInsert(2, 10.5f, 0f, 0f, out SpatialHandle second);

            var buffer = new int[8];
            Assert.That(grid.QuerySphere(10.2f, 0f, 0f, 0.5f, buffer, 8), Is.EqualTo(2));

            grid.SetOrigin(10f, 0f, 0f);

            Assert.That(grid.TryGetPosition(first, out float x, out _, out _), Is.True, "SetOrigin must not invalidate handles.");
            Assert.That(x, Is.EqualTo(10f));
            Assert.That(grid.QuerySphere(10.2f, 0f, 0f, 0.5f, buffer, 8), Is.EqualTo(2));
            Assert.That(grid.ValidateStructure(), Is.True);
            Assert.That(grid.QuerySphere(0.2f, 0f, 0f, 0.1f, buffer, 8), Is.Zero, "Cells must follow the new origin.");

            grid.Dispose();
        }

        [Test]
        public void SphereQuery_MatchesBruteForce()
        {
            const int ElementCount = 2000;
            var random = new Random(777);
            var positions = new float[ElementCount * 3];
            var grid = new SpatialHashGrid<int>(new GridSettings(1.5f, 3f, -2f, 7f), 64);

            for (int i = 0; i < ElementCount; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 200.0) - 100.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 200.0) - 100.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 200.0) - 100.0);
                grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out _);
            }

            var buffer = new int[ElementCount];
            var expected = new HashSet<int>();
            var actual = new HashSet<int>();

            for (int trial = 0; trial < 100; trial++)
            {
                float centerX = (float)((random.NextDouble() * 200.0) - 100.0);
                float centerY = (float)((random.NextDouble() * 200.0) - 100.0);
                float centerZ = (float)((random.NextDouble() * 200.0) - 100.0);
                float radius = (float)(random.NextDouble() * 30.0);

                int count = grid.QuerySphere(centerX, centerY, centerZ, radius, buffer, ElementCount);

                expected.Clear();
                actual.Clear();
                float radiusSquared = radius * radius;
                for (int i = 0; i < ElementCount; i++)
                {
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

                Assert.That(actual, Is.EquivalentTo(expected), "Sphere query diverged at trial " + trial + ".");
            }

            grid.Dispose();
        }

        [Test]
        public void BoxQuery_MatchesBruteForce()
        {
            const int ElementCount = 1500;
            var random = new Random(4242);
            var positions = new float[ElementCount * 3];
            var grid = new SpatialHashGrid<int>(new GridSettings(2.5f), 64);

            for (int i = 0; i < ElementCount; i++)
            {
                positions[i * 3] = (float)((random.NextDouble() * 100.0) - 50.0);
                positions[i * 3 + 1] = (float)((random.NextDouble() * 100.0) - 50.0);
                positions[i * 3 + 2] = (float)((random.NextDouble() * 100.0) - 50.0);
                grid.TryInsert(i, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], out _);
            }

            var buffer = new int[ElementCount];
            var expected = new HashSet<int>();
            var actual = new HashSet<int>();

            for (int trial = 0; trial < 80; trial++)
            {
                float minX = (float)((random.NextDouble() * 100.0) - 50.0);
                float minY = (float)((random.NextDouble() * 100.0) - 50.0);
                float minZ = (float)((random.NextDouble() * 100.0) - 50.0);
                float maxX = minX + (float)(random.NextDouble() * 20.0);
                float maxY = minY + (float)(random.NextDouble() * 20.0);
                float maxZ = minZ + (float)(random.NextDouble() * 20.0);

                int count = grid.QueryBox(minX, minY, minZ, maxX, maxY, maxZ, buffer, ElementCount);

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

                Assert.That(actual.IsSubsetOf(expected), Is.True, "Box query returned an element outside the box.");
                Assert.That(expected.IsSubsetOf(actual), Is.True, "Box query missed an element inside the box.");
            }

            grid.Dispose();
        }

        [Test]
        public void Queries_TruncateAtTheBufferLimit()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                for (int i = 0; i < 100; i++)
                {
                    grid.TryInsert(i, i * 0.01f, 0f, 0f, out _);
                }

                var all = new int[200];
                Assert.That(grid.QuerySphere(0.5f, 0f, 0f, 10f, all, 200), Is.EqualTo(100));
                Assert.That(grid.QuerySphere(0.5f, 0f, 0f, 10f, all, 5), Is.EqualTo(5));

                var small = new int[3];
                Assert.That(grid.QuerySphere(0.5f, 0f, 0f, 10f, small, 100), Is.EqualTo(3));
                Assert.That(grid.QuerySphere(0.5f, 0f, 0f, 10f, all, 0), Is.Zero);
            }
        }

        [Test]
        public void ListSink_NeverGrowsTheList()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                for (int i = 0; i < 100; i++)
                {
                    grid.TryInsert(i, i * 0.01f, 0f, 0f, out _);
                }

                var results = new List<int>(7);
                int count = grid.QuerySphere(0.5f, 0f, 0f, 10f, results);

                Assert.That(count, Is.EqualTo(7));
                Assert.That(results.Capacity, Is.EqualTo(7), "The list must be filled up to capacity, not reallocated.");

                var larger = new List<int>(256);
                Assert.That(grid.QuerySphere(0.5f, 0f, 0f, 10f, larger, 42), Is.EqualTo(42));
                Assert.That(larger.Capacity, Is.EqualTo(256));
            }
        }

        [Test]
        public void FixedPolicy_RejectsOverflowUntilCapacityIsReserved()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64, SpatialCapacityPolicy.Fixed);

            int inserted = 0;
            for (int i = 0; i < 200; i++)
            {
                if (grid.TryInsert(i, i * 0.5f, 0f, 0f, out _))
                {
                    inserted++;
                }
            }

            Assert.That(inserted, Is.EqualTo(grid.Capacity));
            Assert.That(grid.GetStatistics().GrowthCount, Is.Zero);

            grid.EnsureCapacity(200);
            Assert.That(grid.Capacity, Is.GreaterThanOrEqualTo(200));
            Assert.That(grid.TryInsert(1000, 0f, 0f, 0f, out _), Is.True);
            Assert.That(grid.ValidateStructure(), Is.True);

            grid.Dispose();
        }

        [Test]
        public void GrowPolicy_ExpandsAndStaysConsistent()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64);
            for (int i = 0; i < 5000; i++)
            {
                Assert.That(grid.TryInsert(i, i * 0.5f, 0f, 0f, out _), Is.True);
            }

            Assert.That(grid.Count, Is.EqualTo(5000));
            Assert.That(grid.GetStatistics().GrowthCount, Is.GreaterThan(0));
            Assert.That(grid.ValidateStructure(), Is.True);

            grid.Dispose();
        }

        [Test]
        public void HugeRange_FallsBackToLinearScan()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                for (int i = 0; i < 50; i++)
                {
                    grid.TryInsert(i, i * 0.1f, 0f, 0f, out _);
                }

                var results = new int[64];
                Assert.That(grid.QuerySphere(0f, 0f, 0f, 5_000_000f, results, 64), Is.EqualTo(50));

                var box = new int[64];
                Assert.That(grid.QueryBox(-5_000_000f, -1f, -1f, 5_000_000f, 1f, 1f, box, 64), Is.EqualTo(50));
            }
        }

        [Test]
        public void Churn_KeepsEveryInvariant()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(0.5f), 256);
            var random = new Random(99);
            var handles = new List<SpatialHandle>(4096);

            for (int round = 0; round < 100; round++)
            {
                for (int i = 0; i < 200; i++)
                {
                    if (grid.TryInsert(
                            (round * 200) + i,
                            (float)((random.NextDouble() * 50.0) - 25.0),
                            (float)((random.NextDouble() * 50.0) - 25.0),
                            (float)((random.NextDouble() * 50.0) - 25.0),
                            out SpatialHandle handle))
                    {
                        handles.Add(handle);
                    }
                }

                int removals = Math.Min(120, handles.Count);
                for (int i = 0; i < removals; i++)
                {
                    int index = random.Next(handles.Count);
                    SpatialHandle handle = handles[index];
                    handles[index] = handles[handles.Count - 1];
                    handles.RemoveAt(handles.Count - 1);
                    grid.TryRemove(handle);
                }

                Assert.That(grid.ValidateStructure(), Is.True, "Invariant broken in round " + round + ".");
            }

            Assert.That(grid.GetStatistics().OccupiedCellCount, Is.GreaterThan(0));
            grid.Dispose();
        }

        [Test]
        public void Dispose_ReleasesArraysAndRejectsFurtherUse()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64);
            grid.TryInsert(1, 0f, 0f, 0f, out _);
            grid.Dispose();

            Assert.That(grid.IsDisposed, Is.True);
            Assert.That(grid.Capacity, Is.Zero);
            Assert.That(grid.Count, Is.Zero);
            Assert.That(grid.TryInsert(2, 0f, 0f, 0f, out _), Is.False);
            Assert.That(grid.QuerySphere(0f, 0f, 0f, 10f, new int[4], 4), Is.Zero);
            Assert.DoesNotThrow(() => grid.Clear());
            Assert.DoesNotThrow(() => grid.Dispose());
        }

        [Test]
        public void NonFiniteInput_IsRejectedWithoutThrowing()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                Assert.That(grid.TryInsert(1, float.NaN, 0f, 0f, out _), Is.False);
                Assert.That(grid.TryInsert(2, float.PositiveInfinity, 0f, 0f, out _), Is.False);
                Assert.That(grid.QuerySphere(float.NaN, 0f, 0f, 1f, new int[4], 4), Is.Zero);
                Assert.That(grid.QuerySphere(0f, 0f, 0f, -1f, new int[4], 4), Is.Zero);
                Assert.That(grid.Count, Is.Zero);
            }
        }

        [Test]
        public void Statistics_CountEveryOperation()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 64))
            {
                grid.TryInsert(1, 0f, 0f, 0f, out SpatialHandle handle);
                grid.TryRemove(handle);

                SpatialStatistics statistics = grid.GetStatistics();
                Assert.That(statistics.ElementCount, Is.Zero);
                Assert.That(statistics.PeakElementCount, Is.EqualTo(1));
                Assert.That(statistics.TotalInsertCount, Is.EqualTo(1L));
                Assert.That(statistics.TotalRemoveCount, Is.EqualTo(1L));
                Assert.That(statistics.GenerationWrapCount, Is.Zero);
                Assert.That(statistics.BucketCapacity, Is.GreaterThan(0));
            }
        }

#if DEBUG
        [Test]
        public void WriteFromAnotherThread_ThrowsInDebug()
        {
            using (var grid = new SpatialHashGrid<int>(new GridSettings(1f), 8))
            {
                grid.TryInsert(1, 0f, 0f, 0f, out _);

                Exception captured = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        grid.TryInsert(2, 0f, 0f, 0f, out _);
                    }
                    catch (Exception exception)
                    {
                        captured = exception;
                    }
                });

                thread.Start();
                thread.Join();

                Assert.That(captured, Is.TypeOf<InvalidOperationException>(),
                    "A second writing thread must be detected in a Debug build.");
            }
        }
#endif

        [Test]
        public void SteadyState_DoesNotAllocate()
        {
            var grid = new SpatialHashGrid<int>(new GridSettings(1f), 4096);
            var handles = new SpatialHandle[1024];
            for (int i = 0; i < handles.Length; i++)
            {
                grid.TryInsert(i, i * 0.25f, 0f, 0f, out handles[i]);
            }

            var buffer = new int[4096];
            var list = new List<int>(4096);

            for (int i = 0; i < 64; i++)
            {
                grid.QuerySphere(10f, 0f, 0f, 1.5f, buffer, buffer.Length);
                grid.QuerySphere(10f, 0f, 0f, 1.5f, list);
                grid.QueryBox(0f, -1f, -1f, 20f, 1f, 1f, buffer, buffer.Length);
                grid.TryUpdate(handles[i], i * 0.25f, 0.5f, 0f);
                grid.TryGetPosition(handles[i], out _, out _, out _);
            }

            // Unity's Mono runtime reports zero from GC.GetAllocatedBytesForCurrentThread, so this gate uses total
            // managed memory, matching the convention already used by CycloneGames.Scheduling's performance gates.
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 20_000; i++)
            {
                grid.QuerySphere(10f, 0f, 0f, 1.5f, buffer, buffer.Length);
                grid.QuerySphere(10f, 0f, 0f, 1.5f, list);
                grid.QueryBox(0f, -1f, -1f, 20f, 1f, 1f, buffer, buffer.Length);
                grid.TryUpdate(handles[i & 1023], (i & 1023) * 0.25f, 0f, 0f);
                grid.GetStatistics();
            }

            long delta = GC.GetTotalMemory(false) - before;
            Assert.That(delta, Is.LessThan(65_536L), "Steady-state spatial operations allocated " + delta + " B.");

            grid.Dispose();
        }
    }
}
