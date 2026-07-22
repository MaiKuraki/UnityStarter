using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Immutable allocation limits for the buffered Persistence Record V1 pipeline.
    /// </summary>
    public sealed class PersistenceLimits
    {
        public const int HardMaximumPayloadBytes = 1024 * 1024;
        public const int DefaultMaximumPayloadBytes = HardMaximumPayloadBytes;
        public const int DefaultInitialBufferBytes = 256;
        public const int MaximumRecordHeaderBytes = 256;

        public static readonly PersistenceLimits Default = new PersistenceLimits();

        public PersistenceLimits(
            int maximumPayloadBytes = DefaultMaximumPayloadBytes,
            int initialBufferBytes = DefaultInitialBufferBytes)
        {
            if (maximumPayloadBytes <= 0 || maximumPayloadBytes > HardMaximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPayloadBytes),
                    $"The payload limit must be between 1 and {HardMaximumPayloadBytes} bytes.");
            }

            if (initialBufferBytes <= 0 || initialBufferBytes > maximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialBufferBytes),
                    "The initial buffer size must be positive and no larger than the payload limit.");
            }

            MaximumPayloadBytes = maximumPayloadBytes;
            InitialBufferBytes = initialBufferBytes;
        }

        public int MaximumPayloadBytes { get; }

        public int InitialBufferBytes { get; }

        public int MaximumRecordBytes => MaximumPayloadBytes + MaximumRecordHeaderBytes;
    }
}
