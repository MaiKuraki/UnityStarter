using System;
using CycloneGames.Networking.Lockstep;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class LockstepManagerTests
    {
        [Test]
        public void ReceiveRemoteInput_Rejects_Frame_At_Buffer_Distance()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 2,
                localPeerId: 0,
                inputDelay: 0,
                bufferSize: 4);

            var input = new TestInput { Value = 99 };
            lockstep.ReceiveRemoteInput(1, 4, input);

            Span<TestInput> inputs = stackalloc TestInput[2];
            Assert.IsFalse(lockstep.TryGetFrameInputs(4, inputs));
        }

        [Test]
        public void ValidateStateHash_Ignores_Reused_Slot_For_Different_Frame()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 2,
                localPeerId: 0,
                inputDelay: 0,
                bufferSize: 4);

            bool desyncDetected = false;
            lockstep.OnDesyncDetected += (_, _) => desyncDetected = true;

            lockstep.SubmitStateHash(0, 123UL);

            Assert.IsTrue(lockstep.ValidateStateHash(1, 4, 456UL));
            Assert.IsFalse(desyncDetected);
        }

        [Test]
        public void DefaultInputDelay_Prefills_Startup_Frames()
        {
            var lockstep = new LockstepManager<TestInput>(peerCount: 1, localPeerId: 0);

            Assert.AreEqual(2, lockstep.SubmitLocalInput(new TestInput { Value = 3 }));
            Assert.IsTrue(lockstep.Tick());
            Assert.AreEqual(3, lockstep.CurrentFrame);
        }

        [Test]
        public void Tick_Respects_CatchUp_Budget()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 1,
                localPeerId: 0,
                inputDelay: 8,
                bufferSize: 16,
                maxFramesPerTick: 3);

            Assert.IsTrue(lockstep.Tick());
            Assert.AreEqual(3, lockstep.CurrentFrame);
        }

        [Test]
        public void ReceiveRemoteInput_LateAlias_DoesNotOverwriteRetainedFutureInput()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 2,
                localPeerId: 0,
                inputDelay: 0,
                bufferSize: 4);

            for (int frame = 0; frame < 4; frame++)
                AdvanceFrame(lockstep, frame);

            lockstep.ReceiveRemoteInput(1, 7, new TestInput { Value = 7 });
            lockstep.ReceiveRemoteInput(1, 3, new TestInput { Value = 99 });

            for (int frame = 4; frame < 7; frame++)
                AdvanceFrame(lockstep, frame);

            Assert.AreEqual(7, lockstep.SubmitLocalInput(default));
            Span<TestInput> inputs = stackalloc TestInput[2];
            Assert.IsTrue(lockstep.TryGetFrameInputs(7, inputs));
            Assert.AreEqual(7, inputs[1].Value);
        }

        [Test]
        public void ReceiveRemoteInput_ConflictingDuplicate_DoesNotRewriteConfirmedInput()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 2,
                localPeerId: 0,
                inputDelay: 0,
                bufferSize: 4);

            lockstep.ReceiveRemoteInput(1, 0, new TestInput { Value = 11 });
            lockstep.ReceiveRemoteInput(1, 0, new TestInput { Value = 22 });
            lockstep.SubmitLocalInput(default);

            Span<TestInput> inputs = stackalloc TestInput[2];
            Assert.IsTrue(lockstep.TryGetFrameInputs(0, inputs));
            Assert.AreEqual(11, inputs[1].Value);
        }

        [Test]
        public void Tick_PublishesStallThresholdOnce_AndThrottlesSubsequentReports()
        {
            var lockstep = new LockstepManager<TestInput>(
                peerCount: 2,
                localPeerId: 0,
                inputDelay: 0,
                bufferSize: 4,
                maxStallFrames: 60);

            int stallEvents = 0;
            int missingPeer = -1;
            lockstep.OnPeerStall += (peerId, _) =>
            {
                stallEvents++;
                missingPeer = peerId;
            };
            lockstep.SubmitLocalInput(default);

            for (int tick = 0; tick < 119; tick++)
                Assert.IsFalse(lockstep.Tick());

            Assert.AreEqual(1, stallEvents);
            Assert.AreEqual(1, missingPeer);

            Assert.IsFalse(lockstep.Tick());
            Assert.AreEqual(2, stallEvents);
        }

        private static void AdvanceFrame(LockstepManager<TestInput> lockstep, int frame)
        {
            Assert.AreEqual(frame, lockstep.SubmitLocalInput(default));
            lockstep.ReceiveRemoteInput(1, frame, default);
            Assert.IsTrue(lockstep.Tick());
            Assert.AreEqual(frame + 1, lockstep.CurrentFrame);
        }

        private struct TestInput
        {
            public int Value;
        }
    }
}
