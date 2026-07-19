using System;

namespace CycloneGames.GameplayAbilities.Networking
{
    public enum GASCommandReplayDecision : byte
    {
        Invalid = 0,
        Execute = 1,
        Duplicate = 2,
        WrongEpoch = 3,
        SequenceGap = 4,
        TooOld = 5,
        ConflictingReplay = 6,
        ExecutionPending = 7,
        SequenceExhausted = 8
    }

    /// <summary>
    /// Bounded exactly-once gate for one authenticated peer and one authority-owned GAS entity.
    /// </summary>
    /// <remarks>
    /// The owner calls <see cref="Evaluate"/> before executing a command and must call
    /// <see cref="Complete"/> exactly once for every <see cref="GASCommandReplayDecision.Execute"/>
    /// decision, including failure results. The class is owner-thread-affine and performs no locking.
    /// </remarks>
    public sealed class GASAuthorityCommandReplayWindow
    {
        public const int DefaultCapacity = 64;
        public const int MaximumCapacity = 4096;

        private readonly Entry[] entries;
        private uint streamEpoch;
        private uint highestCompletedSequence;
        private GASAbilityCommand pendingCommand;
        private ulong pendingPayloadFingerprint;
        private bool executionPending;

        private struct Entry
        {
            public uint Sequence;
            public ulong PayloadFingerprint;
            public GASCommandResult Result;
        }

        public GASAuthorityCommandReplayWindow(uint streamEpoch, int capacity = DefaultCapacity)
        {
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            if (capacity <= 0 || capacity > MaximumCapacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            this.streamEpoch = streamEpoch;
            entries = new Entry[capacity];
        }

        public uint StreamEpoch => streamEpoch;
        public uint HighestCompletedSequence => highestCompletedSequence;
        public int Capacity => entries.Length;
        public bool IsExecutionPending => executionPending;
        public bool IsSequenceExhausted => highestCompletedSequence == GameplayAbilitiesNetworkProtocol.MaxSequence;

        public GASCommandReplayDecision Evaluate(
            in GASAbilityCommand command,
            ulong payloadFingerprint,
            out GASCommandResult cachedResult)
        {
            cachedResult = default;
            if (!command.IsHeaderValid || payloadFingerprint == 0UL)
                return GASCommandReplayDecision.Invalid;
            if (command.StreamEpoch != streamEpoch)
                return GASCommandReplayDecision.WrongEpoch;
            if (executionPending)
                return GASCommandReplayDecision.ExecutionPending;

            uint sequence = command.CommandSequence;
            if (sequence <= highestCompletedSequence)
            {
                ref Entry entry = ref entries[GetSlot(sequence)];
                if (entry.Sequence != sequence)
                    return GASCommandReplayDecision.TooOld;
                if (entry.PayloadFingerprint != payloadFingerprint)
                    return GASCommandReplayDecision.ConflictingReplay;

                cachedResult = entry.Result;
                return GASCommandReplayDecision.Duplicate;
            }

            if (highestCompletedSequence == GameplayAbilitiesNetworkProtocol.MaxSequence)
                return GASCommandReplayDecision.SequenceExhausted;

            uint expected = highestCompletedSequence + 1u;
            if (sequence != expected)
                return GASCommandReplayDecision.SequenceGap;

            pendingCommand = command;
            pendingPayloadFingerprint = payloadFingerprint;
            executionPending = true;
            return GASCommandReplayDecision.Execute;
        }

        public void Complete(in GASCommandResult result)
        {
            if (!executionPending)
                throw new InvalidOperationException("No GAS authority command is awaiting completion.");
            if (!result.IsValid ||
                result.StreamEpoch != pendingCommand.StreamEpoch ||
                result.CommandSequence != pendingCommand.CommandSequence ||
                result.Entity != pendingCommand.Entity ||
                result.Grant != pendingCommand.Grant ||
                result.CommandKind != pendingCommand.Kind)
            {
                throw new ArgumentException("The terminal result does not match the pending GAS command.", nameof(result));
            }

            uint sequence = pendingCommand.CommandSequence;
            entries[GetSlot(sequence)] = new Entry
            {
                Sequence = sequence,
                PayloadFingerprint = pendingPayloadFingerprint,
                Result = result
            };
            highestCompletedSequence = sequence;
            pendingCommand = default;
            pendingPayloadFingerprint = 0UL;
            executionPending = false;
        }

        /// <summary>Starts a new authenticated stream and invalidates every cached terminal result.</summary>
        public void Reset(uint newStreamEpoch)
        {
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));
            if (executionPending)
                throw new InvalidOperationException("A pending GAS authority command must be completed before resetting the stream.");

            Array.Clear(entries, 0, entries.Length);
            streamEpoch = newStreamEpoch;
            highestCompletedSequence = 0u;
        }

        private int GetSlot(uint sequence)
        {
            return (int)((sequence - 1u) % (uint)entries.Length);
        }
    }
}
