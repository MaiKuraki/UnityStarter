using System;
using System.Threading;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Outcome of one ordered cue receive attempt.</summary>
    public enum GASNetworkCueReceiveResult : byte
    {
        Invalid = 0,
        Consumed = 1,
        PredictedConfirmationSuppressed = 2,
        ConsumerRejected = 3,
        InvalidMessage = 4,
        WrongStreamEpoch = 5,
        WrongEntity = 6,
        Duplicate = 7,
        Stale = 8,
        SequenceGap = 9,
        AuthoritativeStateRegression = 10,
        SequenceExhausted = 11
    }

    /// <summary>
    /// Owner-thread, single-entity ordered cue receiver with bounded predicted-cue confirmation
    /// suppression. It creates no threads, takes no locks, and performs no managed allocation after
    /// construction.
    /// </summary>
    public sealed class GASNetworkCueReceiver : IDisposable
    {
        public const int DefaultPredictedCueCapacity = 64;
        public const int MaximumPredictedCueCapacity = 4096;

        private const GASCueFlags SpatialFlags = GASCueFlags.HasLocation | GASCueFlags.HasNormal;

        private readonly IGASNetworkCueConsumer consumer;
        private readonly GASNetworkEntityId entity;
        private readonly PredictedCueEntry[] predictedCues;
        private readonly int ownerThreadId;
        private uint streamEpoch;
        private uint nextExpectedCueSequence;
        private uint lastAcceptedCueSequence;
        private ulong lastAuthoritativeStateVersion;
        private int predictedCueCount;
        private bool disposed;

        private struct PredictedCueEntry
        {
            public uint SourceCommandSequence;
            public GASNetworkTagId Cue;
            public GASNetworkEntityId Instigator;
            public GASCueEvent Event;
            public GASCueFlags SpatialFlags;
            public float Magnitude;
            public GASNetworkVector3 Location;
            public GASNetworkVector3 Normal;
        }

        public GASNetworkCueReceiver(
            GASNetworkEntityId entity,
            uint streamEpoch,
            IGASNetworkCueConsumer consumer,
            int predictedCueCapacity = DefaultPredictedCueCapacity,
            uint firstExpectedCueSequence = 1u)
        {
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            if (!IsValidSequence(firstExpectedCueSequence))
                throw new ArgumentOutOfRangeException(nameof(firstExpectedCueSequence));
            if (predictedCueCapacity < 0 || predictedCueCapacity > MaximumPredictedCueCapacity)
                throw new ArgumentOutOfRangeException(nameof(predictedCueCapacity));

            this.entity = entity;
            this.streamEpoch = streamEpoch;
            this.consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            nextExpectedCueSequence = firstExpectedCueSequence;
            predictedCues = new PredictedCueEntry[predictedCueCapacity];
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int OwnerThreadId => ownerThreadId;
        public GASNetworkEntityId Entity => entity;
        public uint StreamEpoch => streamEpoch;
        public uint NextExpectedCueSequence => nextExpectedCueSequence;
        public uint LastAcceptedCueSequence => lastAcceptedCueSequence;
        public ulong LastAuthoritativeStateVersion => lastAuthoritativeStateVersion;
        public int PredictedCueCapacity => predictedCues.Length;
        public int PredictedCueCount => predictedCueCount;
        public bool IsSequenceExhausted => nextExpectedCueSequence == 0u;
        public bool IsDisposed => disposed;

        /// <summary>
        /// Records one locally presented predicted cue. A later authority cue is suppressed only when
        /// its command, tag, event, instigator, spatial flags, magnitude, location, and normal match.
        /// The authority-only source-effect identity is intentionally not part of this presentation
        /// fingerprint. Capacity exhaustion rejects the record without evicting older entries.
        /// </summary>
        public bool TryTrackPredictedCue(
            uint sourceCommandSequence,
            GASNetworkTagId cue,
            GASNetworkEntityId instigator,
            GASCueEvent cueEvent,
            GASCueFlags spatialFlags,
            float magnitude,
            in GASNetworkVector3 location,
            in GASNetworkVector3 normal)
        {
            AssertUsable();
            if (!IsValidSequence(sourceCommandSequence) ||
                !cue.IsValid ||
                cueEvent < GASCueEvent.Execute ||
                cueEvent > GASCueEvent.Removed ||
                (spatialFlags & ~SpatialFlags) != 0 ||
                !GASNetworkMessageValidator.IsFinite(magnitude) ||
                !location.IsFinite ||
                !normal.IsFinite ||
                ((spatialFlags & GASCueFlags.HasLocation) == 0 && !IsZero(in location)) ||
                ((spatialFlags & GASCueFlags.HasNormal) == 0 && !IsZero(in normal)) ||
                predictedCueCount >= predictedCues.Length)
            {
                return false;
            }

            predictedCues[predictedCueCount++] = new PredictedCueEntry
            {
                SourceCommandSequence = sourceCommandSequence,
                Cue = cue,
                Instigator = instigator,
                Event = cueEvent,
                SpatialFlags = spatialFlags,
                Magnitude = magnitude,
                Location = location,
                Normal = normal
            };
            return true;
        }

        /// <summary>Discards all unmatched local cue records owned by one terminal command.</summary>
        public int DiscardPredictedCues(uint sourceCommandSequence)
        {
            AssertUsable();
            if (!IsValidSequence(sourceCommandSequence))
                return 0;

            int removed = 0;
            for (int i = predictedCueCount - 1; i >= 0; i--)
            {
                if (predictedCues[i].SourceCommandSequence != sourceCommandSequence)
                    continue;

                RemovePredictedCueAt(i);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Validates and atomically advances one cue from this receiver's entity and stream.
        /// A gap never advances the sequence; reliable-route recovery must replace the stream epoch.
        /// </summary>
        public GASNetworkCueReceiveResult Receive(in GASCueExecuted cue)
        {
            AssertUsable();
            if (GASNetworkMessageValidator.Validate(in cue) != GASNetworkMessageValidationResult.Valid)
                return GASNetworkCueReceiveResult.InvalidMessage;
            if (cue.StreamEpoch != streamEpoch)
                return GASNetworkCueReceiveResult.WrongStreamEpoch;
            if (cue.Entity != entity)
                return GASNetworkCueReceiveResult.WrongEntity;
            if (nextExpectedCueSequence == 0u)
                return GASNetworkCueReceiveResult.SequenceExhausted;
            if (cue.CueSequence < nextExpectedCueSequence)
            {
                return cue.CueSequence == lastAcceptedCueSequence
                    ? GASNetworkCueReceiveResult.Duplicate
                    : GASNetworkCueReceiveResult.Stale;
            }
            if (cue.CueSequence > nextExpectedCueSequence)
                return GASNetworkCueReceiveResult.SequenceGap;
            if (lastAuthoritativeStateVersion != 0UL &&
                cue.AuthoritativeStateVersion < lastAuthoritativeStateVersion)
            {
                return GASNetworkCueReceiveResult.AuthoritativeStateRegression;
            }

            if ((cue.Flags & GASCueFlags.Predicted) != 0 &&
                TryConsumePredictedCue(in cue))
            {
                Advance(in cue);
                return GASNetworkCueReceiveResult.PredictedConfirmationSuppressed;
            }

            if (!consumer.TryConsume(in cue))
                return GASNetworkCueReceiveResult.ConsumerRejected;

            Advance(in cue);
            return GASNetworkCueReceiveResult.Consumed;
        }

        /// <summary>Replaces the stream and clears all pending predicted-cue records.</summary>
        public void ResetEpoch(uint newStreamEpoch, uint firstExpectedCueSequence = 1u)
        {
            AssertUsable();
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));
            if (!IsValidSequence(firstExpectedCueSequence))
                throw new ArgumentOutOfRangeException(nameof(firstExpectedCueSequence));

            ClearPredictedCues();
            streamEpoch = newStreamEpoch;
            nextExpectedCueSequence = firstExpectedCueSequence;
            lastAcceptedCueSequence = 0u;
            lastAuthoritativeStateVersion = 0UL;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            AssertOwnerThread();
            ClearPredictedCues();
            disposed = true;
        }

        private bool TryConsumePredictedCue(in GASCueExecuted cue)
        {
            GASCueFlags spatialFlags = cue.Flags & SpatialFlags;
            for (int i = 0; i < predictedCueCount; i++)
            {
                ref PredictedCueEntry predicted = ref predictedCues[i];
                if (predicted.SourceCommandSequence != cue.SourceCommandSequence ||
                    predicted.Cue != cue.Cue ||
                    predicted.Instigator != cue.Instigator ||
                    predicted.Event != cue.Event ||
                    predicted.SpatialFlags != spatialFlags ||
                    !predicted.Magnitude.Equals(cue.Magnitude) ||
                    predicted.Location != cue.Location ||
                    predicted.Normal != cue.Normal)
                {
                    continue;
                }

                RemovePredictedCueAt(i);
                return true;
            }

            return false;
        }

        private void RemovePredictedCueAt(int index)
        {
            int lastIndex = --predictedCueCount;
            if (index != lastIndex)
                predictedCues[index] = predictedCues[lastIndex];
            predictedCues[lastIndex] = default;
        }

        private void ClearPredictedCues()
        {
            Array.Clear(predictedCues, 0, predictedCues.Length);
            predictedCueCount = 0;
        }

        private void Advance(in GASCueExecuted cue)
        {
            lastAcceptedCueSequence = cue.CueSequence;
            lastAuthoritativeStateVersion = cue.AuthoritativeStateVersion;
            nextExpectedCueSequence = cue.CueSequence == GameplayAbilitiesNetworkProtocol.MaxSequence
                ? 0u
                : cue.CueSequence + 1u;
        }

        private void AssertUsable()
        {
            AssertOwnerThread();
            if (disposed)
                throw new ObjectDisposedException(nameof(GASNetworkCueReceiver));
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS cue receiver is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }

        private static bool IsValidSequence(uint sequence) =>
            sequence > 0u && sequence <= GameplayAbilitiesNetworkProtocol.MaxSequence;

        private static bool IsZero(in GASNetworkVector3 value) =>
            value.X == 0f && value.Y == 0f && value.Z == 0f;
    }
}
