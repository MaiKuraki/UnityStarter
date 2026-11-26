using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Contract of the buffered press-edge ring: window boundaries, ownership of a consumed edge, per-action
    /// isolation, capacity overflow and retention pruning.
    /// </summary>
    public sealed class InputActionBufferTests
    {
        private const int ActionA = 101;
        private const int ActionB = 202;

        [Test]
        public void RecordAndConsume_DeliversTheEdgeOnce()
        {
            var buffer = new InputActionBuffer(8, 600);

            Assert.That(buffer.Record(ActionA, 10), Is.True);
            Assert.That(buffer.TryConsume(ActionA, 10, 0), Is.True);
            Assert.That(buffer.TryConsume(ActionA, 10, 0), Is.False);
            Assert.That(buffer.TryConsume(ActionA, 11, 6), Is.False);
        }

        [Test]
        public void Window_AcceptsEqualAgeAndRejectsOlder()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 100);

            Assert.That(buffer.TryPeek(ActionA, 100, 0), Is.True, "age 0");
            Assert.That(buffer.TryPeek(ActionA, 106, 6), Is.True, "age 6 with window 6");
            Assert.That(buffer.TryPeek(ActionA, 107, 6), Is.False, "age 7 with window 6");
        }

        [Test]
        public void Peek_DoesNotTakeOwnership()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 1);

            Assert.That(buffer.TryPeek(ActionA, 2, 4), Is.True);
            Assert.That(buffer.TryPeek(ActionA, 2, 4), Is.True);
            Assert.That(buffer.TryConsume(ActionA, 2, 4), Is.True);
            Assert.That(buffer.TryPeek(ActionA, 2, 4), Is.False);
        }

        [Test]
        public void Actions_AreIsolated()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 1);

            Assert.That(buffer.TryConsume(ActionB, 1, 4), Is.False);
            Assert.That(buffer.TryConsume(ActionA, 1, 4), Is.True);
            Assert.That(buffer.TryConsume(ActionB, 1, 4), Is.False);
        }

        [Test]
        public void NewestEdge_IsDeliveredFirst()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 1);
            buffer.Record(ActionA, 2);

            Assert.That(buffer.TryConsume(ActionA, 2, 4), Is.True);
            Assert.That(buffer.Count, Is.EqualTo(2), "consumed entries stay in the ring until overwritten");
            Assert.That(buffer.TryConsume(ActionA, 2, 4), Is.True);
            Assert.That(buffer.TryConsume(ActionA, 2, 4), Is.False);
        }

        [Test]
        public void CapacityOverflow_DropsTheOldestEntry()
        {
            const int ActionC = 303;
            var buffer = new InputActionBuffer(2, 600);
            buffer.Record(ActionA, 1);
            buffer.Record(ActionB, 2);
            buffer.Record(ActionC, 3);

            Assert.That(buffer.DroppedCount, Is.EqualTo(1));
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.TryConsume(ActionA, 3, 9), Is.False, "the oldest entry was evicted");
            Assert.That(buffer.TryConsume(ActionB, 3, 9), Is.True);
            Assert.That(buffer.TryConsume(ActionC, 3, 9), Is.True);
        }

        [Test]
        public void Retention_PrunesExpiredEntriesOnRecord()
        {
            var buffer = new InputActionBuffer(8, 10);
            buffer.Record(ActionA, 100);
            buffer.Record(ActionB, 111);

            Assert.That(buffer.Count, Is.EqualTo(1), "age 11 exceeds the retention of 10");
            Assert.That(buffer.TryConsume(ActionA, 111, 100), Is.False);
            Assert.That(buffer.TryConsume(ActionB, 111, 0), Is.True);
        }

        [Test]
        public void Clear_ActionRemovesOnlyPendingEdgesOfThatAction()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 1);
            buffer.Record(ActionA, 2);
            buffer.Record(ActionB, 2);

            Assert.That(buffer.Clear(ActionA), Is.EqualTo(2));
            Assert.That(buffer.TryConsume(ActionA, 2, 8), Is.False);
            Assert.That(buffer.TryConsume(ActionB, 2, 8), Is.True);
        }

        [Test]
        public void Clear_ResetsTheRingButKeepsCumulativeCounters()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 1);
            buffer.Record(ActionB, 1);
            buffer.Clear();

            Assert.That(buffer.Count, Is.Zero);
            Assert.That(buffer.RecordedCount, Is.EqualTo(2));
            Assert.That(buffer.TryConsume(ActionA, 1, 8), Is.False);
        }

        [Test]
        public void Record_IgnoresTheReservedActionId()
        {
            var buffer = new InputActionBuffer(8, 600);

            Assert.That(buffer.Record(0, 1), Is.False);
            Assert.That(buffer.Count, Is.Zero);
        }

        [Test]
        public void Consume_RejectsANegativeWindow()
        {
            var buffer = new InputActionBuffer(8, 600);
            buffer.Record(ActionA, 5);

            Assert.That(buffer.TryConsume(ActionA, 5, -1), Is.False);
            Assert.That(buffer.TryPeek(ActionA, 5, -1), Is.False);
            Assert.That(buffer.TryConsume(ActionA, 5, 0), Is.True, "the edge itself is untouched");
        }

        [Test]
        public void Constructor_RejectsOutOfRangeArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputActionBuffer(0, 600));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputActionBuffer(InputActionBuffer.MaximumCapacity + 1, 600));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputActionBuffer(8, 0));
        }

        [Test]
        public void DefaultConstructor_UsesTheDocumentedCapacity()
        {
            var buffer = new InputActionBuffer();

            Assert.That(buffer.Capacity, Is.EqualTo(InputActionBuffer.DefaultCapacity));
            for (int i = 0; i < InputActionBuffer.DefaultCapacity; i++)
            {
                Assert.That(buffer.Record(ActionA, i), Is.True);
            }

            Assert.That(buffer.Record(ActionA, InputActionBuffer.DefaultCapacity), Is.True);
            Assert.That(buffer.DroppedCount, Is.EqualTo(1));
        }
    }
}
