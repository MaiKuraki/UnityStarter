using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    public sealed class GASNetworkCueReceiverTests
    {
        private static readonly GASNetworkEntityId Entity = new GASNetworkEntityId(41UL);
        private static readonly GASNetworkEntityId Instigator = new GASNetworkEntityId(42UL);
        private static readonly GASNetworkTagId CueTag = new GASNetworkTagId(101UL);
        private const uint Epoch = 7u;

        [Test]
        public void Receive_EnforcesEntityEpochSequenceAndStateVersionWithoutAdvancingOnFailure()
        {
            var consumer = new TestConsumer();
            using (var receiver = new GASNetworkCueReceiver(
                       Entity,
                       Epoch,
                       consumer,
                       predictedCueCapacity: 0,
                       firstExpectedCueSequence: 2u))
            {
                GASCueExecuted wrongEpoch = CreateCue(2u, stateVersion: 10UL, streamEpoch: Epoch + 1u);
                GASCueExecuted wrongEntity = CreateCue(
                    2u,
                    stateVersion: 10UL,
                    entity: new GASNetworkEntityId(99UL));
                GASCueExecuted gap = CreateCue(3u, stateVersion: 10UL);

                Assert.That(receiver.Receive(in wrongEpoch), Is.EqualTo(GASNetworkCueReceiveResult.WrongStreamEpoch));
                Assert.That(receiver.Receive(in wrongEntity), Is.EqualTo(GASNetworkCueReceiveResult.WrongEntity));
                Assert.That(receiver.Receive(in gap), Is.EqualTo(GASNetworkCueReceiveResult.SequenceGap));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(2u));
                Assert.That(consumer.Count, Is.Zero);

                GASCueExecuted second = CreateCue(2u, stateVersion: 10UL);
                Assert.That(receiver.Receive(in second), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(receiver.Receive(in second), Is.EqualTo(GASNetworkCueReceiveResult.Duplicate));

                GASCueExecuted stale = CreateCue(1u, stateVersion: 9UL);
                Assert.That(receiver.Receive(in stale), Is.EqualTo(GASNetworkCueReceiveResult.Stale));

                GASCueExecuted regressed = CreateCue(3u, stateVersion: 9UL);
                Assert.That(
                    receiver.Receive(in regressed),
                    Is.EqualTo(GASNetworkCueReceiveResult.AuthoritativeStateRegression));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(3u));

                GASCueExecuted third = CreateCue(3u, stateVersion: 10UL);
                Assert.That(receiver.Receive(in third), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(receiver.LastAcceptedCueSequence, Is.EqualTo(3u));
                Assert.That(receiver.LastAuthoritativeStateVersion, Is.EqualTo(10UL));
                Assert.That(consumer.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void Receive_ConsumerRejectionAndExceptionKeepSequenceRetryable()
        {
            var consumer = new TestConsumer { Accept = false };
            using (var receiver = new GASNetworkCueReceiver(Entity, Epoch, consumer, 0))
            {
                GASCueExecuted cue = CreateCue(1u, stateVersion: 1UL);
                Assert.That(receiver.Receive(in cue), Is.EqualTo(GASNetworkCueReceiveResult.ConsumerRejected));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(1u));

                consumer.Accept = true;
                consumer.Throw = true;
                Assert.Throws<InvalidOperationException>(() => receiver.Receive(in cue));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(1u));

                consumer.Throw = false;
                Assert.That(receiver.Receive(in cue), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(2u));
            }
        }

        [Test]
        public void PredictedCueConfirmation_IsSuppressedOnlyForAnExactBoundedRecord()
        {
            var consumer = new TestConsumer();
            using (var receiver = new GASNetworkCueReceiver(Entity, Epoch, consumer, 2))
            {
                var location = new GASNetworkVector3(1f, 2f, 3f);
                var normal = new GASNetworkVector3(0f, 1f, 0f);
                const GASCueFlags spatialFlags = GASCueFlags.HasLocation | GASCueFlags.HasNormal;

                Assert.That(
                    receiver.TryTrackPredictedCue(
                        11u,
                        CueTag,
                        Instigator,
                        GASCueEvent.Execute,
                        spatialFlags,
                        2.5f,
                        in location,
                        in normal),
                    Is.True);
                Assert.That(
                    receiver.TryTrackPredictedCue(
                        12u,
                        CueTag,
                        Instigator,
                        GASCueEvent.Execute,
                        spatialFlags,
                        4f,
                        in location,
                        in normal),
                    Is.True);
                Assert.That(
                    receiver.TryTrackPredictedCue(
                        13u,
                        CueTag,
                        Instigator,
                        GASCueEvent.Execute,
                        spatialFlags,
                        8f,
                        in location,
                        in normal),
                    Is.False);

                GASCueExecuted confirmation = CreateCue(
                    1u,
                    stateVersion: 5UL,
                    sourceCommandSequence: 11u,
                    flags: spatialFlags | GASCueFlags.Predicted,
                    magnitude: 2.5f,
                    location: location,
                    normal: normal);
                Assert.That(
                    receiver.Receive(in confirmation),
                    Is.EqualTo(GASNetworkCueReceiveResult.PredictedConfirmationSuppressed));
                Assert.That(consumer.Count, Is.Zero);
                Assert.That(receiver.PredictedCueCount, Is.EqualTo(1));

                GASCueExecuted correctedAuthorityCue = CreateCue(
                    2u,
                    stateVersion: 6UL,
                    sourceCommandSequence: 12u,
                    flags: spatialFlags | GASCueFlags.Predicted,
                    magnitude: 4.5f,
                    location: location,
                    normal: normal);
                Assert.That(
                    receiver.Receive(in correctedAuthorityCue),
                    Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(consumer.Count, Is.EqualTo(1));
                Assert.That(receiver.PredictedCueCount, Is.EqualTo(1));
                Assert.That(receiver.DiscardPredictedCues(12u), Is.EqualTo(1));
                Assert.That(receiver.PredictedCueCount, Is.Zero);
            }
        }

        [Test]
        public void PredictedFlag_RequiresCommandCorrelation()
        {
            GASCueExecuted cue = CreateCue(
                1u,
                stateVersion: 1UL,
                flags: GASCueFlags.Predicted,
                sourceCommandSequence: 0u);

            Assert.That(
                GASNetworkMessageValidator.Validate(in cue),
                Is.EqualTo(GASNetworkMessageValidationResult.NonCanonicalPayload));
        }

        [Test]
        public void ResetEpoch_ClearsOrderingAndPredictedRecords()
        {
            var consumer = new TestConsumer();
            using (var receiver = new GASNetworkCueReceiver(Entity, Epoch, consumer, 1))
            {
                var zero = default(GASNetworkVector3);
                Assert.That(
                    receiver.TryTrackPredictedCue(
                        1u,
                        CueTag,
                        default,
                        GASCueEvent.Execute,
                        GASCueFlags.None,
                        1f,
                        in zero,
                        in zero),
                    Is.True);

                GASCueExecuted oldCue = CreateCue(1u, stateVersion: 1UL);
                Assert.That(receiver.Receive(in oldCue), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));

                receiver.ResetEpoch(Epoch + 1u, firstExpectedCueSequence: 9u);
                Assert.That(receiver.StreamEpoch, Is.EqualTo(Epoch + 1u));
                Assert.That(receiver.NextExpectedCueSequence, Is.EqualTo(9u));
                Assert.That(receiver.LastAcceptedCueSequence, Is.Zero);
                Assert.That(receiver.LastAuthoritativeStateVersion, Is.Zero);
                Assert.That(receiver.PredictedCueCount, Is.Zero);
                Assert.That(receiver.Receive(in oldCue), Is.EqualTo(GASNetworkCueReceiveResult.WrongStreamEpoch));
            }
        }

        [Test]
        public void Receive_MaxSequenceRequiresEpochReplacement()
        {
            var consumer = new TestConsumer();
            using (var receiver = new GASNetworkCueReceiver(
                       Entity,
                       Epoch,
                       consumer,
                       0,
                       GameplayAbilitiesNetworkProtocol.MaxSequence))
            {
                GASCueExecuted cue = CreateCue(
                    GameplayAbilitiesNetworkProtocol.MaxSequence,
                    stateVersion: 1UL);
                Assert.That(receiver.Receive(in cue), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(receiver.IsSequenceExhausted, Is.True);
                Assert.That(receiver.NextExpectedCueSequence, Is.Zero);
                Assert.That(
                    receiver.Receive(in cue),
                    Is.EqualTo(GASNetworkCueReceiveResult.SequenceExhausted));
            }
        }

        [Test]
        public void WarmReceiveAndPredictionTracking_DoNotAllocateAndRejectWrongThread()
        {
            var consumer = new TestConsumer();
            using (var receiver = new GASNetworkCueReceiver(Entity, Epoch, consumer, 1))
            {
                var zero = default(GASNetworkVector3);
                GASCueExecuted warmCue = CreateCue(1u, stateVersion: 1UL);
                Assert.That(receiver.Receive(in warmCue), Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(
                    receiver.TryTrackPredictedCue(
                        1u,
                        CueTag,
                        default,
                        GASCueEvent.Execute,
                        GASCueFlags.None,
                        1f,
                        in zero,
                        in zero),
                    Is.True);
                Assert.That(receiver.DiscardPredictedCues(1u), Is.EqualTo(1));

                GASCueExecuted measuredCue = CreateCue(2u, stateVersion: 2UL);
                long before = GC.GetAllocatedBytesForCurrentThread();
                GASNetworkCueReceiveResult result = receiver.Receive(in measuredCue);
                bool tracked = receiver.TryTrackPredictedCue(
                    2u,
                    CueTag,
                    default,
                    GASCueEvent.Execute,
                    GASCueFlags.None,
                    1f,
                    in zero,
                    in zero);
                int removed = receiver.DiscardPredictedCues(2u);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(result, Is.EqualTo(GASNetworkCueReceiveResult.Consumed));
                Assert.That(tracked, Is.True);
                Assert.That(removed, Is.EqualTo(1));
                Assert.That(allocated, Is.Zero);

                Exception offThreadFailure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        receiver.Receive(in measuredCue);
                    }
                    catch (Exception exception)
                    {
                        offThreadFailure = exception;
                    }
                });
                worker.Start();
                worker.Join();

                Assert.That(offThreadFailure, Is.TypeOf<InvalidOperationException>());
            }
        }

        private static GASCueExecuted CreateCue(
            uint cueSequence,
            ulong stateVersion,
            uint streamEpoch = Epoch,
            GASNetworkEntityId entity = default,
            uint sourceCommandSequence = 0u,
            GASCueFlags flags = GASCueFlags.None,
            float magnitude = 1f,
            GASNetworkVector3 location = default,
            GASNetworkVector3 normal = default)
        {
            return new GASCueExecuted(
                streamEpoch,
                cueSequence,
                entity.IsValid ? entity : Entity,
                CueTag,
                Instigator,
                default,
                sourceCommandSequence,
                stateVersion,
                GASCueEvent.Execute,
                flags,
                magnitude,
                location,
                normal);
        }

        private sealed class TestConsumer : IGASNetworkCueConsumer
        {
            public bool Accept { get; set; } = true;
            public bool Throw { get; set; }
            public int Count { get; private set; }
            public GASCueExecuted LastCue { get; private set; }

            public bool TryConsume(in GASCueExecuted cue)
            {
                if (Throw)
                    throw new InvalidOperationException("Test consumer failure.");
                if (!Accept)
                    return false;

                LastCue = cue;
                Count++;
                return true;
            }
        }
    }
}
