namespace CycloneGames.Logger
{
    public readonly struct LogProcessingStatistics
    {
        public readonly int QueuedCount;
        public readonly long DroppedMessageCount;
        public readonly long ProcessedMessageCount;

        public LogProcessingStatistics(int queuedCount, long droppedMessageCount, long processedMessageCount)
        {
            QueuedCount = queuedCount;
            DroppedMessageCount = droppedMessageCount;
            ProcessedMessageCount = processedMessageCount;
        }
    }
}
