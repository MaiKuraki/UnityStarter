using System;

using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

using NUnit.Framework;

using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Tools;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputRecorderTests
    {
        [Test]
        public void RecordAction_RequiresContextQualifiedIdentity()
        {
            using var recorder = new InputRecorder(sampleCapacity: 8, actionCapacity: 4);

            Assert.Throws<ArgumentException>(
                () => recorder.RecordAction(null, "PlayerActions", "Jump"));
            Assert.DoesNotThrow(
                () => recorder.RecordAction("Gameplay", "PlayerActions", "Jump"));
            Assert.DoesNotThrow(
                () => recorder.RecordAction("Menu", "PlayerActions", "Jump"));
        }

        [Test]
        public void SampleBuffer_RejectsNewestSample_WhenCapacityIsFull()
        {
            var buffer = new InputSampleBuffer(2);

            Assert.IsTrue(buffer.TryAdd(CreateButtonSample(0, 10, 0, "Jump")));
            Assert.IsTrue(buffer.TryAdd(CreateButtonSample(0, 10, 1, "Attack")));
            Assert.IsFalse(buffer.TryAdd(CreateButtonSample(0, 10, 2, "Dodge")));

            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(1, buffer.DroppedSampleCount);
            Assert.AreEqual("Jump", buffer.ToArray()[0].ActionName);
            Assert.AreEqual("Attack", buffer.ToArray()[1].ActionName);
        }

        [Test]
        public void ReplayCursor_UsesTickAndOrderWithoutSchedulingTimers()
        {
            var recording = new InputRecording(
                new[]
                {
                    CreateButtonSample(3, 100, 0, "Jump"),
                    CreateButtonSample(3, 100, 1, "Attack"),
                    CreateButtonSample(3, 101, 2, "Dodge")
                },
                capacity: 3,
                droppedSampleCount: 0);
            Assert.AreEqual(3, recording.SampleCount);
            Assert.AreEqual(2, recording.TickCount);

            InputReplayCursor cursor = recording.CreateCursor();

            Assert.IsTrue(cursor.TryReadNext(100, out InputSample first));
            Assert.AreEqual("Jump", first.ActionName);
            Assert.AreEqual(3, first.PlayerId);
            Assert.AreEqual("Gameplay", first.ContextName);
            Assert.AreEqual(
                InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Jump"),
                first.ActionId);
            Assert.AreEqual(InputSamplePhase.Performed, first.Phase);
            Assert.AreEqual(InputUpdateType.Dynamic, first.UpdateType);
            Assert.AreEqual(100, first.Tick);
            Assert.AreEqual(0UL, first.Order);

            Assert.IsTrue(cursor.TryReadNext(100, out InputSample second));
            Assert.AreEqual("Attack", second.ActionName);
            Assert.IsFalse(cursor.TryReadNext(100, out _));

            Assert.IsTrue(cursor.TryReadNext(101, out InputSample third));
            Assert.AreEqual("Dodge", third.ActionName);
            Assert.IsFalse(cursor.HasNext);
        }

        [Test]
        public void Recording_RejectsNonDeterministicOrder()
        {
            InputSample[] samples =
            {
                CreateButtonSample(0, 10, 1, "Jump"),
                CreateButtonSample(0, 10, 1, "Attack")
            };

            Assert.Throws<ArgumentException>(
                () => new InputRecording(samples, capacity: 2, droppedSampleCount: 0));
        }

        private static InputSample CreateButtonSample(
            int playerId,
            long tick,
            ulong order,
            string actionName)
        {
            return new InputSample(
                playerId,
                InputHashUtility.GetActionId("Gameplay", "PlayerActions", actionName),
                "Gameplay",
                "PlayerActions",
                actionName,
                InputSampleValueKind.Button,
                InputSamplePhase.Performed,
                InputUpdateType.Dynamic,
                tick,
                order,
                timeSinceStartSeconds: tick * 0.01d,
                vector2Value: Vector2.zero,
                scalarValue: 0f);
        }
    }
}
