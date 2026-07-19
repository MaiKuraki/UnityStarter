using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    internal sealed class BoundedFileReader
    {
        private readonly SystemFileStoreOptions _options;

        internal BoundedFileReader(SystemFileStoreOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        internal byte[] Read(string path, int maxByteCount)
        {
            Validate(path, maxByteCount);
            using (var source = SystemFileStreams.OpenRead(path, false))
            {
                int length = ValidateLength(source.Length, maxByteCount, path);
                var content = new byte[length];
                try
                {
                    ReadExactly(source, content);
                    EnsureEndOfFile(source);
                    return content;
                }
                catch
                {
                    ByteBufferSecurity.Clear(content);
                    throw;
                }
            }
        }

        internal async Task<byte[]> ReadAsync(
            string path,
            int maxByteCount,
            CancellationToken cancellationToken)
        {
            Validate(path, maxByteCount);
            cancellationToken.ThrowIfCancellationRequested();

            using (var source = SystemFileStreams.OpenRead(path, true))
            {
                int length = ValidateLength(source.Length, maxByteCount, path);
                var content = new byte[length];
                try
                {
                    int offset = 0;
                    while (offset < length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(_options.BufferSize, length - offset);
                        int bytesRead = await source.ReadAsync(
                            content,
                            offset,
                            count,
                            CancellationToken.None).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            throw new EndOfStreamException(
                                $"File ended before {length} bytes were read.");
                        }

                        offset += bytesRead;
#if UNITY_WEBGL && !UNITY_EDITOR
                        await Task.Yield();
#endif
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await EnsureEndOfFileAsync(source).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return content;
                }
                catch
                {
                    ByteBufferSecurity.Clear(content);
                    throw;
                }
            }
        }

        private static async Task EnsureEndOfFileAsync(Stream source)
        {
            var trailingByte = new byte[1];
            int trailingCount;
            try
            {
                trailingCount = await source.ReadAsync(
                    trailingByte,
                    0,
                    1,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                ByteBufferSecurity.Clear(trailingByte);
            }

            if (trailingCount != 0)
            {
                throw new IOException("File length changed while it was being read.");
            }
        }

        private static void ReadExactly(Stream source, byte[] destination)
        {
            int offset = 0;
            while (offset < destination.Length)
            {
                int bytesRead = source.Read(destination, offset, destination.Length - offset);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException(
                        $"File ended before {destination.Length} bytes were read.");
                }

                offset += bytesRead;
            }
        }

        private static void EnsureEndOfFile(Stream source)
        {
            if (source.ReadByte() >= 0)
            {
                throw new IOException("File length changed while it was being read.");
            }
        }

        private static int ValidateLength(long length, int maxByteCount, string path)
        {
            if (length < 0L || length > maxByteCount || length > int.MaxValue)
            {
                throw new IOException(
                    $"File '{Path.GetFileName(path)}' is {length} bytes and exceeds the {maxByteCount}-byte read limit.");
            }

            return (int)length;
        }

        private static void Validate(string path, int maxByteCount)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Path cannot be null, empty, or whitespace.",
                    nameof(path));
            }

            if (maxByteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxByteCount));
            }
        }
    }
}
