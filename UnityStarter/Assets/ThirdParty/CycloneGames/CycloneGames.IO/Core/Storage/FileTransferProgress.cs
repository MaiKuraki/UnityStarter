namespace CycloneGames.IO
{
    public readonly struct FileTransferProgress
    {
        public const long UNKNOWN_TOTAL_BYTES = -1L;

        public FileTransferProgress(long processedBytes, long totalBytes)
        {
            if (processedBytes < 0L)
            {
                throw new System.ArgumentOutOfRangeException(nameof(processedBytes));
            }

            if (totalBytes < UNKNOWN_TOTAL_BYTES)
            {
                throw new System.ArgumentOutOfRangeException(nameof(totalBytes));
            }

            ProcessedBytes = processedBytes;
            TotalBytes = totalBytes;
        }

        public long ProcessedBytes { get; }

        public long TotalBytes { get; }

        public bool HasKnownTotal => TotalBytes >= 0L;

        public float Ratio => !HasKnownTotal
            ? 0f
            : TotalBytes == 0L
                ? 1f
                : System.Math.Min(1f, (float)ProcessedBytes / TotalBytes);
    }
}
