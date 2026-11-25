using System;

namespace CycloneGames.Persistence
{
    public sealed class PersistenceLimits
    {
        internal const int MaximumRecordHeaderBytes = 256;

        public PersistenceLimits()
            : this(HardMaximumPayloadBytes, DefaultInitialBufferBytes)
        {
        }

        public PersistenceLimits(int maximumPayloadBytes)
            : this(maximumPayloadBytes, DefaultInitialBufferBytes)
        {
        }

        public PersistenceLimits(int maximumPayloadBytes, int initialBufferBytes)
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

        public static int HardMaximumPayloadBytes => 1024 * 1024;

        public static int DefaultInitialBufferBytes => 256;

        public int MaximumPayloadBytes { get; }

        public int InitialBufferBytes { get; }

        public int MaximumRecordBytes => MaximumPayloadBytes + MaximumRecordHeaderBytes;
    }
}
