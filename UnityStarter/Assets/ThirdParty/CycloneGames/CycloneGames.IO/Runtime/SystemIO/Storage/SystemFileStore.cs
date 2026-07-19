using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    /// <summary>
    /// Production file-system storage with bounded whole-file reads, streaming, and strict atomic commits.
    /// </summary>
    public sealed class SystemFileStore : IFileStore, IAtomicFileStore, IStreamFileStore
    {
        public static readonly SystemFileStore Default = new SystemFileStore();

        private readonly AtomicFileTransaction _atomicTransaction;
        private readonly BoundedFileReader _boundedReader;

        public SystemFileStore(SystemFileStoreOptions options = null)
            : this(options ?? SystemFileStoreOptions.Default, SystemAtomicFileOperations.Instance)
        {
        }

        internal SystemFileStore(
            SystemFileStoreOptions options,
            IAtomicFileOperations fileOperations)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _atomicTransaction = new AtomicFileTransaction(Options, fileOperations);
            _boundedReader = new BoundedFileReader(Options);
        }

        public SystemFileStoreOptions Options { get; }

        public bool Exists(string path)
        {
            ValidatePath(path, nameof(path));
            return File.Exists(path);
        }

        public long GetLength(string path)
        {
            ValidatePath(path, nameof(path));
            return new FileInfo(path).Length;
        }

        public void Delete(string path)
        {
            ValidatePath(path, nameof(path));
            File.Delete(path);
        }

        public byte[] ReadBytes(string path, int maxByteCount)
        {
            return _boundedReader.Read(path, maxByteCount);
        }

        public Task<byte[]> ReadBytesAsync(
            string path,
            int maxByteCount,
            CancellationToken cancellationToken = default)
        {
            return _boundedReader.ReadAsync(path, maxByteCount, cancellationToken);
        }

        public string ReadText(
            string path,
            int maxByteCount,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true)
        {
            byte[] content = ReadBytes(path, maxByteCount);
            try
            {
                return TextCodec.Decode(content, fallbackEncoding, detectByteOrderMark);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public async Task<string> ReadTextAsync(
            string path,
            int maxByteCount,
            Encoding fallbackEncoding = null,
            bool detectByteOrderMark = true,
            CancellationToken cancellationToken = default)
        {
            byte[] content = await ReadBytesAsync(path, maxByteCount, cancellationToken).ConfigureAwait(false);
            try
            {
                return TextCodec.Decode(content, fallbackEncoding, detectByteOrderMark);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public void WriteBytes(string path, byte[] content)
        {
            ValidatePath(path, nameof(path));
            ValidateContent(content);
            using (var destination = SystemFileStreams.CreateWrite(path, false))
            {
                destination.Write(content, 0, content.Length);
            }
        }

        public async Task WriteBytesAsync(
            string path,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(path, nameof(path));
            ValidateContent(content);
            cancellationToken.ThrowIfCancellationRequested();

            using (var destination = SystemFileStreams.CreateWrite(path, true))
            {
                int offset = 0;
                while (offset < content.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(Options.BufferSize, content.Length - offset);
                    await destination.WriteAsync(
                        content,
                        offset,
                        count,
                        CancellationToken.None).ConfigureAwait(false);
                    offset += count;
#if UNITY_WEBGL && !UNITY_EDITOR
                    await Task.Yield();
#endif
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public void WriteText(
            string path,
            string text,
            Encoding encoding = null)
        {
            byte[] content = TextCodec.Encode(text, encoding);
            try
            {
                WriteBytes(path, content);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public async Task WriteTextAsync(
            string path,
            string text,
            Encoding encoding = null,
            CancellationToken cancellationToken = default)
        {
            byte[] content = TextCodec.Encode(text, encoding);
            try
            {
                await WriteBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public void WriteBytesAtomically(string path, byte[] content)
        {
            _atomicTransaction.WriteBytes(path, content);
        }

        public Task WriteBytesAtomicallyAsync(
            string path,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            return _atomicTransaction.WriteBytesAsync(path, content, cancellationToken);
        }

        public void WriteTextAtomically(
            string path,
            string text,
            Encoding encoding = null)
        {
            byte[] content = TextCodec.Encode(text, encoding);
            try
            {
                WriteBytesAtomically(path, content);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public async Task WriteTextAtomicallyAsync(
            string path,
            string text,
            Encoding encoding = null,
            CancellationToken cancellationToken = default)
        {
            byte[] content = TextCodec.Encode(text, encoding);
            try
            {
                await WriteBytesAtomicallyAsync(path, content, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ByteBufferSecurity.Clear(content);
            }
        }

        public Task<long> WriteStreamAtomicallyAsync(
            string path,
            Stream source,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return _atomicTransaction.WriteStreamAsync(path, source, progress, cancellationToken);
        }

        public Stream OpenRead(string path)
        {
            ValidatePath(path, nameof(path));
            return SystemFileStreams.OpenRead(path, false);
        }

        public Stream CreateWrite(string path)
        {
            ValidatePath(path, nameof(path));
            return SystemFileStreams.CreateWrite(path, false);
        }

        public Stream OpenAppend(string path)
        {
            ValidatePath(path, nameof(path));
            return SystemFileStreams.OpenAppend(path);
        }

        public async Task<long> WriteStreamAsync(
            string path,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(path, nameof(path));
            ValidateReadableStream(source, nameof(source));
            cancellationToken.ThrowIfCancellationRequested();

            using (var destination = SystemFileStreams.CreateWrite(path, true))
            {
                return await StreamTransfer.CopyAsync(
                    source,
                    destination,
                    Options,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<long> ReadStreamAsync(
            string path,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(path, nameof(path));
            ValidateWritableStream(destination, nameof(destination));
            cancellationToken.ThrowIfCancellationRequested();

            using (var source = SystemFileStreams.OpenRead(path, true))
            {
                return await StreamTransfer.CopyAsync(
                    source,
                    destination,
                    Options,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<FileCopyResult> CopyAtomicallyAsync(
            string sourcePath,
            string destinationPath,
            FileCopyBehavior behavior = FileCopyBehavior.SkipIfIdentical,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(sourcePath, nameof(sourcePath));
            ValidatePath(destinationPath, nameof(destinationPath));
            ValidateCopyBehavior(behavior);
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedSourcePath = Path.GetFullPath(sourcePath);
            string normalizedDestinationPath = Path.GetFullPath(destinationPath);
            using (var source = SystemFileStreams.OpenRead(normalizedSourcePath, true))
            {
                long sourceLength = source.Length;
                if (string.Equals(normalizedSourcePath, normalizedDestinationPath, PathComparison))
                {
                    progress?.Report(new FileTransferProgress(sourceLength, sourceLength));
                    return FileCopyResult.SkippedIdentical;
                }

                if (behavior == FileCopyBehavior.SkipIfIdentical
                    && File.Exists(normalizedDestinationPath))
                {
                    try
                    {
                        using (var destination = SystemFileStreams.OpenRead(
                            normalizedDestinationPath,
                            true))
                        {
                            if (sourceLength == destination.Length
                                && await BinaryContentComparer.AreEqualAsync(
                                    source,
                                    destination,
                                    sourceLength,
                                    null,
                                    cancellationToken,
                                    Options).ConfigureAwait(false))
                            {
                                progress?.Report(new FileTransferProgress(sourceLength, sourceLength));
                                return FileCopyResult.SkippedIdentical;
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // The destination disappeared after the existence check; copy it normally.
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // The destination directory disappeared; the atomic writer will recreate it.
                    }

                    source.Seek(0L, SeekOrigin.Begin);
                }

                await WriteStreamAtomicallyAsync(
                    normalizedDestinationPath,
                    source,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            return FileCopyResult.Copied;
        }

        private static StringComparison PathComparison =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static void ValidatePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null, empty, or whitespace.", parameterName);
            }
        }

        private static void ValidateContent(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
        }

        private static void ValidateReadableStream(Stream source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!source.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", parameterName);
            }
        }

        private static void ValidateWritableStream(Stream destination, string parameterName)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!destination.CanWrite)
            {
                throw new ArgumentException("Stream must be writable.", parameterName);
            }
        }

        private static void ValidateCopyBehavior(FileCopyBehavior behavior)
        {
            if (!Enum.IsDefined(typeof(FileCopyBehavior), behavior))
            {
                throw new ArgumentOutOfRangeException(nameof(behavior));
            }
        }
    }
}
