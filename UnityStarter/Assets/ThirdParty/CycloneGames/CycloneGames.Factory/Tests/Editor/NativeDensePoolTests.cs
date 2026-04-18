#if PRESENT_COLLECTIONS
using System;
using CycloneGames.Factory.DOD.Runtime;
using NUnit.Framework;
using Unity.Collections;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class NativeDensePoolTests
    {
        [Test]
        public void TrySpawn_Read_Write_AndContains_AreConsistent()
        {
            using var pool = new NativeDensePool<DenseValue>(4, Allocator.Temp);

            Assert.That(pool.TrySpawn(new DenseValue { Value = 10 }, out var handle, out var denseIndex), Is.True);
            Assert.That(handle.IsValid, Is.True);
            Assert.That(denseIndex, Is.EqualTo(0));
            Assert.That(pool.Contains(handle), Is.True);
            Assert.That(pool.TryRead(handle, out var initial), Is.True);
            Assert.That(initial.Value, Is.EqualTo(10));

            Assert.That(pool.TryWrite(handle, new DenseValue { Value = 42 }), Is.True);
            Assert.That(pool.TryRead(handle, out var updated), Is.True);
            Assert.That(updated.Value, Is.EqualTo(42));
        }

        [Test]
        public void Despawn_InvalidatesOldHandle_AndAdvancesGeneration()
        {
            using var pool = new NativeDensePool<DenseValue>(2, Allocator.Temp);

            Assert.That(pool.TrySpawn(new DenseValue { Value = 1 }, out var firstHandle, out _), Is.True);
            Assert.That(pool.Despawn(firstHandle), Is.True);
            Assert.That(pool.Contains(firstHandle), Is.False);
            Assert.That(pool.Despawn(firstHandle), Is.False);

            Assert.That(pool.TrySpawn(new DenseValue { Value = 2 }, out var secondHandle, out _), Is.True);
            Assert.That(secondHandle.Slot, Is.EqualTo(firstHandle.Slot));
            Assert.That(secondHandle.Generation, Is.GreaterThan(firstHandle.Generation));
        }

        [Test]
        public void Despawn_SwapPop_KeepsRemainingHandleValid()
        {
            using var pool = new NativeDensePool<DenseValue>(4, Allocator.Temp);

            pool.TrySpawn(new DenseValue { Value = 1 }, out var firstHandle, out _);
            pool.TrySpawn(new DenseValue { Value = 2 }, out var secondHandle, out _);
            pool.TrySpawn(new DenseValue { Value = 3 }, out var thirdHandle, out _);

            Assert.That(pool.Despawn(secondHandle), Is.True);
            Assert.That(pool.CountActive, Is.EqualTo(2));
            Assert.That(pool.Contains(firstHandle), Is.True);
            Assert.That(pool.Contains(thirdHandle), Is.True);
            Assert.That(pool.TryRead(thirdHandle, out var remaining), Is.True);
            Assert.That(remaining.Value, Is.EqualTo(3));
        }

        [Test]
        public void Clear_InvalidatesExistingHandles_AndRestoresFreeCapacity()
        {
            using var pool = new NativeDensePool<DenseValue>(3, Allocator.Temp);

            pool.TrySpawn(new DenseValue { Value = 5 }, out var handleA, out _);
            pool.TrySpawn(new DenseValue { Value = 6 }, out var handleB, out _);

            pool.Clear();

            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.Contains(handleA), Is.False);
            Assert.That(pool.Contains(handleB), Is.False);
        }

        [Test]
        public void TrySpawn_ReturnsFalseWhenPoolIsFull()
        {
            using var pool = new NativeDensePool<DenseValue>(1, Allocator.Temp);

            Assert.That(pool.TrySpawn(new DenseValue { Value = 7 }, out _, out _), Is.True);
            Assert.That(pool.TrySpawn(new DenseValue { Value = 8 }, out var handle, out var denseIndex), Is.False);
            Assert.That(handle.IsValid, Is.False);
            Assert.That(denseIndex, Is.EqualTo(-1));
        }

        [Test]
        public void SpawnBatch_AndDespawnBatch_WorkUnderHighChurn()
        {
            using var pool = new NativeDensePool<DenseValue>(16, Allocator.Temp);
            var values = new NativeArray<DenseValue>(8, Allocator.Temp);
            var handles = new NativeArray<NativePoolHandle>(8, Allocator.Temp);

            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = new DenseValue { Value = i + 1 };
                }

                int spawned = pool.SpawnBatch(values, values.Length, handles);
                Assert.That(spawned, Is.EqualTo(8));
                Assert.That(pool.CountActive, Is.EqualTo(8));

                int despawned = pool.DespawnBatch(handles, handles.Length);
                Assert.That(despawned, Is.EqualTo(8));
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountInactive, Is.EqualTo(16));
            }
            finally
            {
                values.Dispose();
                handles.Dispose();
            }
        }

        [Test]
        public void FreeList_RemainsStableAcrossRepeatedFullCycles()
        {
            using var pool = new NativeDensePool<DenseValue>(32, Allocator.Temp);
            var handles = new NativeArray<NativePoolHandle>(32, Allocator.Temp);

            try
            {
                for (int cycle = 0; cycle < 10; cycle++)
                {
                    for (int i = 0; i < handles.Length; i++)
                    {
                        Assert.That(pool.TrySpawn(new DenseValue { Value = cycle * 100 + i }, out var h, out _), Is.True);
                        handles[i] = h;
                    }

                    for (int i = handles.Length - 1; i >= 0; i--)
                    {
                        Assert.That(pool.Despawn(handles[i]), Is.True);
                    }

                    Assert.That(pool.CountActive, Is.Zero);
                    Assert.That(pool.CountInactive, Is.EqualTo(32));
                }
            }
            finally
            {
                handles.Dispose();
            }
        }

        [Test]
        public void Diagnostics_AndProfile_UseUnifiedCountVocabulary()
        {
            using var pool = new NativeDensePool<DenseValue>(4, Allocator.Temp);

            Assert.That(pool.TrySpawn(new DenseValue { Value = 1 }, out var handle, out _), Is.True);
            Assert.That(pool.Despawn(handle), Is.True);
            Assert.That(pool.Despawn(handle), Is.False);
            Assert.That(pool.TrySpawn(new DenseValue { Value = 2 }, out _, out _), Is.True);
            Assert.That(pool.TrySpawn(new DenseValue { Value = 3 }, out _, out _), Is.True);
            Assert.That(pool.TrySpawn(new DenseValue { Value = 4 }, out _, out _), Is.True);
            Assert.That(pool.TrySpawn(new DenseValue { Value = 5 }, out _, out _), Is.True);  // reuses despawned slot
            Assert.That(pool.TrySpawn(new DenseValue { Value = 6 }, out _, out _), Is.False); // now truly full

            Assert.That(pool.Diagnostics.PeakCountActive, Is.EqualTo(4));
            Assert.That(pool.Diagnostics.TotalSpawned, Is.EqualTo(5));
            Assert.That(pool.Diagnostics.TotalDespawned, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.InvalidDespawns, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
            Assert.That(pool.Profile.CountAll, Is.EqualTo(4));
            Assert.That(pool.Profile.CountActive, Is.EqualTo(4));
            Assert.That(pool.Profile.CountInactive, Is.EqualTo(0));
        }

        [Test]
        public void SoA2_PreservesParallelStreams_AndStableHandles()
        {
            using var pool = new NativeDenseColumnPool2<int, float>(4, Allocator.Temp);

            Assert.That(pool.TrySpawn(10, 1.5f, out var a, out _), Is.True);
            Assert.That(pool.TrySpawn(20, 2.5f, out var b, out _), Is.True);
            Assert.That(pool.TryRead(a, out var a0, out var a1), Is.True);
            Assert.That(a0, Is.EqualTo(10));
            Assert.That(a1, Is.EqualTo(1.5f));

            Assert.That(pool.Despawn(a), Is.True);
            Assert.That(pool.Contains(a), Is.False);
            Assert.That(pool.Contains(b), Is.True);
            Assert.That(pool.TryWrite(b, 99, 9.5f), Is.True);
            Assert.That(pool.TryRead(b, out var b0, out var b1), Is.True);
            Assert.That(b0, Is.EqualTo(99));
            Assert.That(b1, Is.EqualTo(9.5f));
        }

        [Test]
        public void ColumnPool2_SpawnBatch_AndDespawnBatch_WorkAsExpected()
        {
            using var pool = new NativeDenseColumnPool2<int, float>(8, Allocator.Temp);
            var stream0 = new NativeArray<int>(4, Allocator.Temp);
            var stream1 = new NativeArray<float>(4, Allocator.Temp);
            var handles = new NativeArray<NativePoolHandle>(4, Allocator.Temp);

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    stream0[i] = i + 1;
                    stream1[i] = i + 0.5f;
                }

                int spawned = pool.SpawnBatch(stream0, stream1, 4, handles);
                Assert.That(spawned, Is.EqualTo(4));
                Assert.That(pool.CountActive, Is.EqualTo(4));

                int despawned = pool.DespawnBatch(handles, 4);
                Assert.That(despawned, Is.EqualTo(4));
                Assert.That(pool.CountActive, Is.Zero);
            }
            finally
            {
                stream0.Dispose();
                stream1.Dispose();
                handles.Dispose();
            }
        }

        [Test]
        public void SoA3_PreservesThreeStreamsAcrossSwapPop()
        {
            using var pool = new NativeDenseColumnPool3<int, float, short>(4, Allocator.Temp);

            Assert.That(pool.TrySpawn(1, 1.5f, 10, out var a, out _), Is.True);
            Assert.That(pool.TrySpawn(2, 2.5f, 20, out var b, out _), Is.True);
            Assert.That(pool.Despawn(a), Is.True);
            Assert.That(pool.TryRead(b, out var b0, out var b1, out var b2), Is.True);
            Assert.That(b0, Is.EqualTo(2));
            Assert.That(b1, Is.EqualTo(2.5f));
            Assert.That(b2, Is.EqualTo(20));
        }

        [Test]
        public void SoA4_SupportsStableHandleReadWrite()
        {
            using var pool = new NativeDenseColumnPool4<int, float, short, byte>(4, Allocator.Temp);

            Assert.That(pool.TrySpawn(1, 2f, 3, 4, out var handle, out _), Is.True);
            Assert.That(pool.TryWrite(handle, 10, 20f, 30, 40), Is.True);
            Assert.That(pool.TryRead(handle, out var v0, out var v1, out var v2, out var v3), Is.True);
            Assert.That(v0, Is.EqualTo(10));
            Assert.That(v1, Is.EqualTo(20f));
            Assert.That(v2, Is.EqualTo(30));
            Assert.That(v3, Is.EqualTo(40));
        }

        private struct DenseValue
        {
            public int Value;
        }
    }
}
#endif
