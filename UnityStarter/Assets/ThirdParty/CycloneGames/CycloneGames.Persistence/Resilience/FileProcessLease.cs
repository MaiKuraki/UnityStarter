using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Persistence;

namespace CycloneGames.Persistence.Resilience
{
    /// <summary>
    /// Cross-process advisory lease backed by a lock file with an owner id and a monotonic
    /// expiry. An expired record is reclaimed by bounded retries. No OS-mandatory lock is used.

    /// </summary>
    public sealed class FileProcessLease : IDisposable
    {
        private const int OwnerByteLength = 32;
        private const int RecordByteLength = 4 + OwnerByteLength + 8;

        private readonly string _lockPath;
        private readonly byte[] _owner;
        private readonly TimeSpan _expiry;
        private readonly int _maxAttempts;
        private readonly IPersistenceEventSink _sink;
        private FileStream _handle;
        private long _conflicts;

        public FileProcessLease(
            string lockPath,
            TimeSpan expiry,
            int maxAttempts,
            IPersistenceEventSink eventSink)
        {
            if (string.IsNullOrWhiteSpace(lockPath))
            {
                throw new ArgumentException("A lock file path is required.", nameof(lockPath));
            }

            if (expiry <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(expiry));
            }

            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            _lockPath = lockPath;
            _owner = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
            _expiry = expiry;
            _maxAttempts = maxAttempts;
            _sink = eventSink;
        }

        public long ConflictCount => Interlocked.Read(ref _conflicts);

        public bool IsHeld => Volatile.Read(ref _handle) != null;

        public bool TryAcquire()
        {
            if (IsHeld)
            {
                return false;
            }

            for (int attempt = 0; attempt < _maxAttempts; attempt++)
            {
                FileStream handle = null;
                try
                {
                    handle = new FileStream(
                        _lockPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    Span<byte> record = stackalloc byte[RecordByteLength];
                    BinaryPrimitives.WriteInt32LittleEndian(record, OwnerByteLength);
                    _owner.AsSpan().CopyTo(record.Slice(4));
                    BinaryPrimitives.WriteInt64LittleEndian(
                        record.Slice(4 + OwnerByteLength),
                        DateTime.UtcNow.Add(_expiry).Ticks);
                    handle.Write(record);
                    handle.Flush();
                    Volatile.Write(ref _handle, handle);
                    return true;
                }
                catch (IOException)
                {
                    if (handle != null)
                    {
                        handle.Dispose();
                    }

                    Interlocked.Increment(ref _conflicts);
                    if (!TryReclaimStale())
                    {
                        break;
                    }
                }
            }

            EmitConflict();
            return false;
        }

        public void Release()
        {
            FileStream handle = Interlocked.Exchange(ref _handle, null);
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Dispose();
            }
            catch (IOException)
            {
            }

            try
            {
                if (OwnsCurrentLock())
                {
                    File.Delete(_lockPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            Release();
        }

        // A stale record is one whose expiry has passed. The file can only be removed when the
        // owning process released its exclusive handle, which FileShare.None enforces.
        private bool TryReclaimStale()
        {
            try
            {
                if (!File.Exists(_lockPath))
                {
                    return true;
                }

                byte[] record = File.ReadAllBytes(_lockPath);
                if (record.Length != RecordByteLength
                    || BinaryPrimitives.ReadInt32LittleEndian(
                        new ReadOnlySpan<byte>(record, 0, 4)) != OwnerByteLength)
                {
                    return false;
                }

                long expiryTicks = BinaryPrimitives.ReadInt64LittleEndian(
                    new ReadOnlySpan<byte>(record, 4 + OwnerByteLength, 8));
                if (DateTime.UtcNow.Ticks <= expiryTicks)
                {
                    return false;
                }

                File.Delete(_lockPath);
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

        private bool OwnsCurrentLock()
        {
            try
            {
                if (!File.Exists(_lockPath))
                {
                    return false;
                }

                byte[] record = File.ReadAllBytes(_lockPath);
                if (record.Length != RecordByteLength)
                {
                    return false;
                }

                for (int index = 0; index < OwnerByteLength; index++)
                {
                    if (record[4 + index] != _owner[index])
                    {
                        return false;
                    }
                }

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

        private void EmitConflict()
        {
            if (_sink == null)
            {
                return;
            }

            var value = new PersistenceEvent(
                PersistenceEventKind.LeaseConflict,
                DateTime.UtcNow.Ticks,
                PersistenceErrorCode.WriteFailed,
                0,
                0);
            _sink.Record(in value);
        }
    }
}
