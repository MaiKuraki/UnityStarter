// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections;
using CycloneGames.Audio.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Audio.Tests
{
    public sealed class AudioConcurrencyGateTests
    {
        [Test]
        public void Constructor_RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioConcurrencyGate(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioConcurrencyGate(-4));
        }

        [Test]
        public void AcquireWithinCapacity_CompletesSynchronously()
        {
            var gate = new AudioConcurrencyGate(2);

            UniTask first = gate.WaitAsync();
            UniTask second = gate.WaitAsync();

            Assert.AreEqual(UniTaskStatus.Succeeded, first.Status);
            Assert.AreEqual(UniTaskStatus.Succeeded, second.Status);
            Assert.AreEqual(2, gate.ActiveCount);
            Assert.AreEqual(0, gate.WaitingCount);
            Assert.IsFalse(gate.IsIdle);

            gate.Release();
            gate.Release();

            Assert.AreEqual(0, gate.ActiveCount);
            Assert.IsTrue(gate.IsIdle);
        }

        [Test]
        public void ReleaseWithoutAcquire_ThrowsAndKeepsCount()
        {
            var gate = new AudioConcurrencyGate(1);

            Assert.Throws<InvalidOperationException>(() => gate.Release());
            Assert.AreEqual(0, gate.ActiveCount);
            Assert.IsTrue(gate.IsIdle);
        }

        [UnityTest]
        public IEnumerator WaitAsync_NeverExceedsCapacityUnderLoad()
        {
            const int capacity = 4;
            const int workers = 48;

            var gate = new AudioConcurrencyGate(capacity);
            int active = 0;
            int peak = 0;
            int completed = 0;

            async UniTask RunWorker()
            {
                await gate.WaitAsync();
                int current = ++active;
                if (current > peak) peak = current;

                await UniTask.Yield();

                active--;
                gate.Release();
                completed++;
            }

            for (int i = 0; i < workers; i++)
            {
                RunWorker().Forget();
            }

            float deadline = Time.realtimeSinceStartup + 15f;
            while (completed < workers && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(workers, completed, "Workers did not drain; the gate may have lost a slot.");
            Assert.LessOrEqual(peak, capacity, "Concurrency exceeded the gate capacity.");
            Assert.AreEqual(0, gate.ActiveCount);
            Assert.IsTrue(gate.IsIdle);
        }

        [UnityTest]
        public IEnumerator WaitAsync_ServesWaitersInArrivalOrder()
        {
            var gate = new AudioConcurrencyGate(1);
            UniTask occupied = gate.WaitAsync();
            Assert.AreEqual(UniTaskStatus.Succeeded, occupied.Status);

            var order = new System.Collections.Generic.List<int>(4);

            async UniTask WaitThenRelease(int index)
            {
                await gate.WaitAsync();
                order.Add(index);
                gate.Release();
            }

            UniTask a = WaitThenRelease(1);
            UniTask b = WaitThenRelease(2);
            UniTask c = WaitThenRelease(3);

            yield return null;

            gate.Release();

            float deadline = Time.realtimeSinceStartup + 15f;
            while (order.Count < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(3, order.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
            Assert.AreEqual(UniTaskStatus.Succeeded, a.Status);
            Assert.AreEqual(UniTaskStatus.Succeeded, b.Status);
            Assert.AreEqual(UniTaskStatus.Succeeded, c.Status);
            Assert.IsTrue(gate.IsIdle);
        }
    }
}
