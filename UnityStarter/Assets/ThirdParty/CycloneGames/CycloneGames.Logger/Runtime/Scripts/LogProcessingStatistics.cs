namespace CycloneGames.Logger
{
    public readonly struct LogProcessingStatistics
    {
        public readonly int QueuedCount;
        public readonly int ReservedCount;
        public readonly int InFlightCount;
        public readonly int PeakQueuedCount;
        public readonly int QueuedCharacters;
        public readonly int InFlightCharacters;
        public readonly int PeakQueuedCharacters;
        public readonly long EnqueuedMessageCount;
        public readonly long DroppedMessageCount;
        public readonly long DroppedNewestCount;
        public readonly long DroppedOldestCount;
        public readonly long DroppedCriticalCount;
        public readonly long RejectedAfterStopCount;
        public readonly long ProcessedMessageCount;
        public readonly long SinkFailureCount;
        public readonly long SinkDisposalFailureCount;
        public readonly int PendingSinkDisposalCount;
        public readonly int QuarantinedSinkCount;
        public readonly int FilterCategoryCount;
        public readonly int FilterCharacters;
        public readonly long RejectedFilterMutationCount;
        public readonly long TimestampProviderFailureCount;
        public readonly long MessageBuilderFailureCount;

        public LogProcessingStatistics(int queuedCount, long droppedMessageCount, long processedMessageCount)
            : this(
                queuedCount,
                0,
                0,
                queuedCount,
                0,
                0,
                0,
                0,
                droppedMessageCount,
                droppedMessageCount,
                0,
                0,
                0,
                processedMessageCount,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0)
        {
        }

        internal LogProcessingStatistics(
            int queuedCount,
            int reservedCount,
            int inFlightCount,
            int peakQueuedCount,
            int queuedCharacters,
            int inFlightCharacters,
            int peakQueuedCharacters,
            long enqueuedMessageCount,
            long droppedMessageCount,
            long droppedNewestCount,
            long droppedOldestCount,
            long droppedCriticalCount,
            long rejectedAfterStopCount,
            long processedMessageCount,
            long sinkFailureCount,
            long sinkDisposalFailureCount,
            int pendingSinkDisposalCount,
            int quarantinedSinkCount,
            int filterCategoryCount,
            int filterCharacters,
            long rejectedFilterMutationCount,
            long timestampProviderFailureCount,
            long messageBuilderFailureCount)
        {
            QueuedCount = queuedCount;
            ReservedCount = reservedCount;
            InFlightCount = inFlightCount;
            PeakQueuedCount = peakQueuedCount;
            QueuedCharacters = queuedCharacters;
            InFlightCharacters = inFlightCharacters;
            PeakQueuedCharacters = peakQueuedCharacters;
            EnqueuedMessageCount = enqueuedMessageCount;
            DroppedMessageCount = droppedMessageCount;
            DroppedNewestCount = droppedNewestCount;
            DroppedOldestCount = droppedOldestCount;
            DroppedCriticalCount = droppedCriticalCount;
            RejectedAfterStopCount = rejectedAfterStopCount;
            ProcessedMessageCount = processedMessageCount;
            SinkFailureCount = sinkFailureCount;
            SinkDisposalFailureCount = sinkDisposalFailureCount;
            PendingSinkDisposalCount = pendingSinkDisposalCount;
            QuarantinedSinkCount = quarantinedSinkCount;
            FilterCategoryCount = filterCategoryCount;
            FilterCharacters = filterCharacters;
            RejectedFilterMutationCount = rejectedFilterMutationCount;
            TimestampProviderFailureCount = timestampProviderFailureCount;
            MessageBuilderFailureCount = messageBuilderFailureCount;
        }

        internal LogProcessingStatistics WithSinkStatistics(
            long sinkFailureCount,
            long sinkDisposalFailureCount,
            int pendingSinkDisposalCount,
            int quarantinedSinkCount,
            int filterCategoryCount,
            int filterCharacters,
            long rejectedFilterMutationCount,
            long timestampProviderFailureCount,
            long messageBuilderFailureCount)
        {
            return new LogProcessingStatistics(
                QueuedCount,
                ReservedCount,
                InFlightCount,
                PeakQueuedCount,
                QueuedCharacters,
                InFlightCharacters,
                PeakQueuedCharacters,
                EnqueuedMessageCount,
                DroppedMessageCount,
                DroppedNewestCount,
                DroppedOldestCount,
                DroppedCriticalCount,
                RejectedAfterStopCount,
                ProcessedMessageCount,
                sinkFailureCount,
                sinkDisposalFailureCount,
                pendingSinkDisposalCount,
                quarantinedSinkCount,
                filterCategoryCount,
                filterCharacters,
                rejectedFilterMutationCount,
                timestampProviderFailureCount,
                messageBuilderFailureCount);
        }
    }
}
