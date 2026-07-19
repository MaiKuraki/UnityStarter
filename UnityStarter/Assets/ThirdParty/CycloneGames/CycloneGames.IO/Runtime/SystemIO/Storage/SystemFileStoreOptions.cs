using System;

namespace CycloneGames.IO
{
    public sealed class SystemFileStoreOptions
    {
        public const int DEFAULT_BUFFER_SIZE = 64 * 1024;
        public const int MIN_BUFFER_SIZE = 4 * 1024;
        public const int MAX_BUFFER_SIZE = 1024 * 1024;

        public static readonly SystemFileStoreOptions Default = new SystemFileStoreOptions();

        public SystemFileStoreOptions(
            int bufferSize = DEFAULT_BUFFER_SIZE,
            PooledBufferClearMode pooledBufferClearMode = PooledBufferClearMode.UsedRegion)
        {
            if (bufferSize < MIN_BUFFER_SIZE || bufferSize > MAX_BUFFER_SIZE)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bufferSize),
                    $"Buffer size must be between {MIN_BUFFER_SIZE} and {MAX_BUFFER_SIZE} bytes.");
            }

            if (!Enum.IsDefined(typeof(PooledBufferClearMode), pooledBufferClearMode))
            {
                throw new ArgumentOutOfRangeException(nameof(pooledBufferClearMode));
            }

            BufferSize = bufferSize;
            PooledBufferClearMode = pooledBufferClearMode;
        }

        public int BufferSize { get; }

        public PooledBufferClearMode PooledBufferClearMode { get; }
    }
}
