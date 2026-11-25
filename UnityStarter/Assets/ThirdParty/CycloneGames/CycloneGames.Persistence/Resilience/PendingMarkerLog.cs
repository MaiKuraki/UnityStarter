using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Hash.Core;

namespace CycloneGames.Persistence.Resilience
{
    /// <summary>
    /// One pending marker records the intended target length and digest before the write.
    /// Recovery compares them against the stored record, so a completed write is never rolled
    /// back and an incomplete one is restored from its backup.
    /// </summary>
    internal sealed class PendingMarkerLog
    {
        private const int HeaderByteLength = 4 + 4 + 8 + 4;
        private const string PendingSuffix = ".pending";
        private const string PendingPattern = "*.pending";

        private static readonly byte[] MagicBytes =
        {
            (byte)'C', (byte)'G', (byte)'P', (byte)'W'
        };

        private readonly string _directory;
        private readonly string _session;
        private long _nextId;

        internal PendingMarkerLog(string directory)
        {
            _directory = directory;
            _session = Guid.NewGuid().ToString("N");
        }

        private static ReadOnlySpan<byte> Magic => MagicBytes;

        internal string Begin(byte[] content, string backupName)
        {
            Directory.CreateDirectory(_directory);
            long id = Interlocked.Increment(ref _nextId);
            string fileName = _session + "-" + id.ToString("X16") + PendingSuffix;
            string path = Path.Combine(_directory, fileName);
            byte[] nameBytes = Encoding.UTF8.GetBytes(backupName ?? string.Empty);

            Span<byte> header = stackalloc byte[HeaderByteLength];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4), content.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(
                header.Slice(8),
                XxHash64.Compute(content));
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), nameBytes.Length);

            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(header);
                if (nameBytes.Length > 0)
                {
                    stream.Write(nameBytes, 0, nameBytes.Length);
                }

                stream.Flush();
            }

            return fileName;
        }

        internal void Complete(string commitId)
        {
            File.Delete(Path.Combine(_directory, commitId));
        }

        internal string[] EnumeratePending()
        {
            return Directory.GetFiles(_directory, PendingPattern);
        }

        internal bool TryRead(string path, out WalEntry entry)
        {
            entry = default;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length < HeaderByteLength
                    || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
                {
                    return false;
                }

                int contentLength = BinaryPrimitives.ReadInt32LittleEndian(
                    new ReadOnlySpan<byte>(bytes, 4, 4));
                ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(
                    new ReadOnlySpan<byte>(bytes, 8, 8));
                int nameLength = BinaryPrimitives.ReadInt32LittleEndian(
                    new ReadOnlySpan<byte>(bytes, 16, 4));
                if (nameLength < 0 || nameLength != bytes.Length - HeaderByteLength)
                {
                    return false;
                }

                string backupName = nameLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(bytes, HeaderByteLength, nameLength);
                entry = new WalEntry(contentLength, contentHash, backupName);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    internal readonly struct WalEntry
    {
        internal WalEntry(int contentLength, ulong contentHash, string backupName)
        {
            ContentLength = contentLength;
            ContentHash = contentHash;
            BackupName = backupName;
        }

        internal int ContentLength { get; }

        internal ulong ContentHash { get; }

        internal string BackupName { get; }
    }
}
