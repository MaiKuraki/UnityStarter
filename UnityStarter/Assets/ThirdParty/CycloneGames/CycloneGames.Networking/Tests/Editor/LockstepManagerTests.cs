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

        private struct TestInput
        {
            public int Value;
        }
    }
}
