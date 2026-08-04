using System;
using System.Threading;
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

        [Test]
        public void DesyncDetector_MatchingHash_ReturnsHashMatch()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong localHash = RecordFrame(detector, 3, 17);

            DesyncValidationResult result = detector.EvaluateRemoteHash(3, localHash);

            Assert.AreEqual(DesyncValidationVerdict.HashMatch, result.Verdict);
            Assert.IsTrue(result.HasLocalHash);
            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(3, result.Frame);
            Assert.AreEqual(localHash, result.LocalHash);
            Assert.AreEqual(localHash, result.RemoteHash);
            Assert.IsTrue(detector.ValidateRemoteHash(3, localHash));
        }

        [Test]
        public void DesyncDetector_MismatchingHash_ReturnsMismatchAndRaisesEvent()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong localHash = RecordFrame(detector, 2, 9);
            int eventCount = 0;
            detector.OnDesyncDetected += (frame, reportedLocal, reportedRemote) =>
            {
                eventCount++;
                Assert.AreEqual(2, frame);
                Assert.AreEqual(localHash, reportedLocal);
                Assert.AreEqual(localHash + 1UL, reportedRemote);
            };

            Assert.IsFalse(detector.ValidateRemoteHash(2, localHash + 1UL));
            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void DesyncDetector_FutureFrame_IsUnavailableWithoutEvent()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            RecordFrame(detector, 2, 5);
            int eventCount = 0;
            detector.OnDesyncDetected += (_, _, _) => eventCount++;

            DesyncValidationResult result = detector.EvaluateRemoteHash(3, 123UL);

            Assert.AreEqual(DesyncValidationVerdict.FrameUnavailable, result.Verdict);
            Assert.IsFalse(result.HasLocalHash);
            Assert.IsTrue(detector.ValidateRemoteHash(3, 123UL));
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void DesyncDetector_UnrecordedFrame_IsUnavailableWithoutEvent()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            RecordFrame(detector, 2, 5);
            int eventCount = 0;
            detector.OnDesyncDetected += (_, _, _) => eventCount++;

            DesyncValidationResult result = detector.EvaluateRemoteHash(1, 123UL);

            Assert.AreEqual(DesyncValidationVerdict.FrameUnavailable, result.Verdict);
            Assert.IsFalse(detector.TryGetFrameHash(1, out ulong missingHash));
            Assert.AreEqual(0UL, missingHash);
            Assert.IsTrue(detector.ValidateRemoteHash(1, 123UL));
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void DesyncDetector_ExpiredFrame_IsDistinctAndDoesNotRaiseEvent()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong frameZeroHash = RecordFrame(detector, 0, 10);
            for (int frame = 1; frame <= 4; frame++)
                RecordFrame(detector, frame, frame + 10);

            int eventCount = 0;
            detector.OnDesyncDetected += (_, _, _) => eventCount++;

            DesyncValidationResult result = detector.EvaluateRemoteHash(0, frameZeroHash);

            Assert.AreEqual(DesyncValidationVerdict.Expired, result.Verdict);
            Assert.IsTrue(detector.ValidateRemoteHash(0, frameZeroHash));
            Assert.AreEqual(0, eventCount);
        }

        [Test]
        public void DesyncDetector_UnavailableVerdict_RaisesDedicatedEventOnly()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            RecordFrame(detector, 2, 5);
            int desyncEvents = 0;
            int unavailableEvents = 0;
            DesyncValidationVerdict reportedVerdict = DesyncValidationVerdict.Invalid;
            detector.OnDesyncDetected += (_, _, _) => desyncEvents++;
            detector.OnValidationUnavailable += (frame, verdict) =>
            {
                Assert.AreEqual(3, frame);
                unavailableEvents++;
                reportedVerdict = verdict;
            };

            DesyncValidationResult result = detector.EvaluateRemoteHash(3, 123UL);

            Assert.AreEqual(DesyncValidationVerdict.FrameUnavailable, result.Verdict);
            Assert.AreEqual(DesyncValidationVerdict.FrameUnavailable, reportedVerdict);
            Assert.AreEqual(1, unavailableEvents);
            Assert.AreEqual(0, desyncEvents);
        }

        [Test]
        public void DesyncDetector_OverwrittenSlot_DoesNotExposePreviousFrameHash()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong oldHash = RecordFrame(detector, 0, 10);
            ulong replacementHash = RecordFrame(detector, 4, 20);

            Assert.IsFalse(detector.TryGetFrameHash(0, out _));
            Assert.IsTrue(detector.TryGetFrameHash(4, out ulong retainedHash));
            Assert.AreEqual(replacementHash, retainedHash);
            Assert.AreNotEqual(oldHash, retainedHash);
            Assert.AreEqual(
                DesyncValidationVerdict.Expired,
                detector.EvaluateRemoteHash(0, oldHash).Verdict);
        }

        [Test]
        public void DesyncDetector_HistoryBoundary_RetainsNewestCapacityFrames()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong boundaryHash = 0UL;
            for (int frame = 10; frame <= 14; frame++)
            {
                ulong hash = RecordFrame(detector, frame, frame);
                if (frame == 11) boundaryHash = hash;
            }

            Assert.AreEqual(
                DesyncValidationVerdict.Expired,
                detector.EvaluateRemoteHash(10, 0UL).Verdict);
            Assert.AreEqual(
                DesyncValidationVerdict.HashMatch,
                detector.EvaluateRemoteHash(11, boundaryHash).Verdict);
        }

        [Test]
        public void DesyncDetector_IntMinFrame_CanBeStampedAndRetrieved()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong hash = RecordFrame(detector, int.MinValue, 42);

            Assert.IsTrue(detector.TryGetFrameHash(int.MinValue, out ulong retainedHash));
            Assert.AreEqual(hash, retainedHash);
        }

        [Test]
        public void DesyncDetector_FrameRollover_RetainsPreviousModularFrame()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong previousHash = RecordFrame(detector, int.MaxValue, 41);
            RecordFrame(detector, int.MinValue, 42);

            DesyncValidationResult result = detector.EvaluateRemoteHash(
                int.MaxValue,
                previousHash);

            Assert.AreEqual(DesyncValidationVerdict.HashMatch, result.Verdict);
            Assert.IsTrue(detector.TryGetFrameHash(int.MaxValue, out ulong retainedHash));
            Assert.AreEqual(previousHash, retainedHash);
        }

        [Test]
        public void DesyncDetector_ResetClearsFrameStamps()
        {
            var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
            ulong hash = RecordFrame(detector, 0, 42);
            detector.Reset();

            Assert.IsFalse(detector.TryGetFrameHash(0, out _));
            Assert.AreEqual(
                DesyncValidationVerdict.FrameUnavailable,
                detector.EvaluateRemoteHash(0, hash).Verdict);
        }

        [Test]
        public void LockstepManager_WrongThreadAccess_FailsFastInEditor()
        {
            var lockstep = new LockstepManager<TestInput>(1, 0, inputDelay: 0);
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    lockstep.Tick();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }

        [Test]
        public void DesyncDetector_ConstructingWorkerThread_BecomesOwner()
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var detector = new DesyncDetector<Fnv1aHasher>(historySize: 4);
                    RecordFrame(detector, 0, 7);
                    detector.ValidateRemoteHash(0, detector.GetFrameHash(0));
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.IsNull(captured);
        }

        private static void AdvanceFrame(LockstepManager<TestInput> lockstep, int frame)
        {
            Assert.AreEqual(frame, lockstep.SubmitLocalInput(default));
            lockstep.ReceiveRemoteInput(1, frame, default);
            Assert.IsTrue(lockstep.Tick());
            Assert.AreEqual(frame + 1, lockstep.CurrentFrame);
        }

        private static ulong RecordFrame(
            DesyncDetector<Fnv1aHasher> detector,
            int frame,
            int value)
        {
            detector.BeginFrame(frame);
            detector.HashInt(value);
            return detector.EndFrame();
        }

        private struct TestInput
        {
            public int Value;
        }
    }
}
