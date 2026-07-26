namespace CycloneGames.BehaviorTree.Networking
{
    public readonly struct BehaviorTreeNetworkMemoryStats
    {
        public BehaviorTreeNetworkMemoryStats(
            int effectiveMaxSnapshotPayloadBytes,
            int effectiveMaxDeltaPayloadBytes,
            int maxTrackedBlackboardKeys,
            int retainedPayloadBytes,
            int snapshotNodeStateCapacity,
            int snapshotNodeAuxiliaryCapacity,
            int traversalNodeCapacity,
            int traversalStackCapacity,
            int blackboardScratchCapacityBytes,
            int snapshotScratchCapacityBytes,
            long capturedSnapshotCount,
            long capturedDeltaCount,
            long capturedHashOnlyCount,
            long suppressedDeltaCount,
            long outgoingPayloadBytes,
            int peakOutgoingPayloadBytes,
            long receivedPayloadCount,
            long acceptedPayloadCount,
            long rejectedPayloadCount,
            long incomingPayloadBytes,
            int peakIncomingPayloadBytes,
            long acceptedSnapshotCount,
            long acceptedDeltaCount,
            long acceptedHashOnlyCount,
            int retainedHistoryEntryCount,
            int maximumRetainedHistoryEntryCount)
        {
            EffectiveMaxSnapshotPayloadBytes = effectiveMaxSnapshotPayloadBytes;
            EffectiveMaxDeltaPayloadBytes = effectiveMaxDeltaPayloadBytes;
            MaxTrackedBlackboardKeys = maxTrackedBlackboardKeys;
            RetainedPayloadBytes = retainedPayloadBytes;
            SnapshotNodeStateCapacity = snapshotNodeStateCapacity;
            SnapshotNodeAuxiliaryCapacity = snapshotNodeAuxiliaryCapacity;
            TraversalNodeCapacity = traversalNodeCapacity;
            TraversalStackCapacity = traversalStackCapacity;
            BlackboardScratchCapacityBytes = blackboardScratchCapacityBytes;
            SnapshotScratchCapacityBytes = snapshotScratchCapacityBytes;
            CapturedSnapshotCount = capturedSnapshotCount;
            CapturedDeltaCount = capturedDeltaCount;
            CapturedHashOnlyCount = capturedHashOnlyCount;
            SuppressedDeltaCount = suppressedDeltaCount;
            OutgoingPayloadBytes = outgoingPayloadBytes;
            PeakOutgoingPayloadBytes = peakOutgoingPayloadBytes;
            ReceivedPayloadCount = receivedPayloadCount;
            AcceptedPayloadCount = acceptedPayloadCount;
            RejectedPayloadCount = rejectedPayloadCount;
            IncomingPayloadBytes = incomingPayloadBytes;
            PeakIncomingPayloadBytes = peakIncomingPayloadBytes;
            AcceptedSnapshotCount = acceptedSnapshotCount;
            AcceptedDeltaCount = acceptedDeltaCount;
            AcceptedHashOnlyCount = acceptedHashOnlyCount;
            RetainedHistoryEntryCount = retainedHistoryEntryCount;
            MaximumRetainedHistoryEntryCount = maximumRetainedHistoryEntryCount;
        }

        public int EffectiveMaxSnapshotPayloadBytes { get; }
        public int EffectiveMaxDeltaPayloadBytes { get; }
        public int MaxTrackedBlackboardKeys { get; }
        public int RetainedPayloadBytes { get; }
        public int SnapshotNodeStateCapacity { get; }
        public int SnapshotNodeAuxiliaryCapacity { get; }
        public int TraversalNodeCapacity { get; }
        public int TraversalStackCapacity { get; }
        public int BlackboardScratchCapacityBytes { get; }
        public int SnapshotScratchCapacityBytes { get; }
        public long CapturedSnapshotCount { get; }
        public long CapturedDeltaCount { get; }
        public long CapturedHashOnlyCount { get; }
        public long SuppressedDeltaCount { get; }
        public long OutgoingPayloadBytes { get; }
        public int PeakOutgoingPayloadBytes { get; }
        public long ReceivedPayloadCount { get; }
        public long AcceptedPayloadCount { get; }
        public long RejectedPayloadCount { get; }
        public long IncomingPayloadBytes { get; }
        public int PeakIncomingPayloadBytes { get; }
        public long AcceptedSnapshotCount { get; }
        public long AcceptedDeltaCount { get; }
        public long AcceptedHashOnlyCount { get; }
        public int RetainedHistoryEntryCount { get; }
        public int MaximumRetainedHistoryEntryCount { get; }
    }
}
