using System;

namespace CycloneGames.IO
{
    /// <summary>
    /// Explicit transfer budget for one file store. Device tiers inject their own bounds; the
    /// absolute limits only guard against a budget that would allow unbounded allocation.
    /// </summary>
    public sealed class SystemFileStoreOptions
    {
        private const int DefaultBufferBytes = 64 * 1024;
        private const int DefaultMinimumBufferBytes = 4 * 1024;
        private const int DefaultMaximumBufferBytes = 1024 * 1024;
        private const int AbsoluteMinimumBufferBytes = 1;
        private const int AbsoluteMaximumBufferBytes = 16 * 1024 * 1024;

        public static readonly SystemFileStoreOptions Default = new SystemFileStoreOptions();

        public SystemFileStoreOptions()
            : this(
                DefaultBufferBytes,
                PooledBufferClearMode.UsedRegion,
                DefaultMinimumBufferBytes,
                DefaultMaximumBufferBytes)
        {
        }

        public SystemFileStoreOptions(int bufferSize)
            : this(
                bufferSize,
                PooledBufferClearMode.UsedRegion,
                DefaultMinimumBufferBytes,
                DefaultMaximumBufferBytes)
        {
        }

        public SystemFileStoreOptions(
            int bufferSize,
            PooledBufferClearMode pooledBufferClearMode)
            : this(
                bufferSize,
                pooledBufferClearMode,
                DefaultMinimumBufferBytes,
                DefaultMaximumBufferBytes)
        {
        }

        public SystemFileStoreOptions(
            int bufferSize,
            PooledBufferClearMode pooledBufferClearMode,
            int minimumBufferSize,
            int maximumBufferSize)
        {
            if (!IsKnownClearMode(pooledBufferClearMode))
            {
                throw new ArgumentOutOfRangeException(nameof(pooledBufferClearMode));
            }

            if (minimumBufferSize < AbsoluteMinimumBufferBytes
                || maximumBufferSize > AbsoluteMaximumBufferBytes
                || minimumBufferSize > maximumBufferSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumBufferSize),
                    "The declared buffer budget must stay within the absolute allocation limits.");
            }

            if (bufferSize < minimumBufferSize || bufferSize > maximumBufferSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bufferSize),
                    $"Buffer size must be between {minimumBufferSize} and {maximumBufferSize} bytes.");
            }

            BufferSize = bufferSize;
            PooledBufferClearMode = pooledBufferClearMode;
            MinimumBufferSize = minimumBufferSize;
            MaximumBufferSize = maximumBufferSize;
        }

        public int BufferSize { get; }

        public PooledBufferClearMode PooledBufferClearMode { get; }

        public int MinimumBufferSize { get; }

        public int MaximumBufferSize { get; }

        // Explicit comparison keeps enum validation allocation-free and reflection-free.
        private static bool IsKnownClearMode(PooledBufferClearMode mode)
        {
            return mode == PooledBufferClearMode.UsedRegion
                || mode == PooledBufferClearMode.EntireBuffer
                || mode == PooledBufferClearMode.None;
        }
    }
}
