using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Bounded owner of materialized table payloads. Mutations are intended for one loading owner.
    /// After <see cref="Seal"/>, any number of readers may use the cache concurrently as long as
    /// disposal is coordinated by the owner and does not race those reads.
    /// </summary>
    public sealed class DataTableBytesCache : IDataTableBytesProvider, IDisposable
    {
        private Dictionary<string, byte[]> _bytesByTableName;
        private readonly string _dataExtension;
        private readonly DataTableLoadLimits _limits;
        private readonly bool _clearBytesOnDispose;
        private long _totalBytes;
        private long _releasedPayloadCount;
        private long _releasedBytes;
        private bool _sealed;
        private bool _disposed;

        public DataTableBytesCache(int capacity = 8, string dataExtension = ".bytes")
            : this(DataTableLoadLimits.Default, capacity, dataExtension)
        {
        }

        public DataTableBytesCache(
            DataTableLoadLimits limits,
            int capacity = 8,
            string dataExtension = ".bytes",
            bool clearBytesOnDispose = false)
        {
            limits.EnsureValid(nameof(limits));
            if (capacity < 0 || capacity > limits.MaxTableCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    $"Initial capacity must be between zero and {limits.MaxTableCount}.");
            }

            _limits = limits;
            // Table identities are case-insensitive so a cache cannot represent two entries that
            // collapse to the same native path on Windows or common console file systems.
            _bytesByTableName = new Dictionary<string, byte[]>(capacity, StringComparer.OrdinalIgnoreCase);
            _dataExtension = DataTableNameUtility.NormalizeDataExtension(dataExtension);
            _clearBytesOnDispose = clearBytesOnDispose;
        }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return _bytesByTableName.Count;
            }
        }

        public long TotalBytes
        {
            get
            {
                ThrowIfDisposed();
                return _totalBytes;
            }
        }

        public bool IsSealed => _sealed;

        public bool IsDisposed => _disposed;

        public DataTableLoadLimits Limits => _limits;

        /// <summary>
        /// Captures bounded owner diagnostics without enumerating payloads. This remains available
        /// after closure so a release responder can report its remaining work.
        /// </summary>
        public DataTableBytesCacheMemorySnapshot GetMemorySnapshot()
        {
            return new DataTableBytesCacheMemorySnapshot(
                _bytesByTableName?.Count ?? 0,
                _totalBytes,
                _sealed,
                _disposed,
                _limits,
                _releasedPayloadCount,
                _releasedBytes);
        }

        /// <summary>Copies the supplied memory so later caller mutation cannot affect the cache.</summary>
        public void Add(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (_bytesByTableName.ContainsKey(normalizedName))
            {
                throw new ArgumentException($"Data-table payload is already cached: {normalizedName}", nameof(tableName));
            }

            byte[] ownedBytes = CopyAndValidate(normalizedName, bytes, _totalBytes, isNewEntry: true);
            _bytesByTableName.Add(normalizedName, ownedBytes);
            _totalBytes += ownedBytes.Length;
        }

        /// <summary>
        /// Takes ownership of an array without copying. The caller must relinquish all writable
        /// aliases after this method succeeds.
        /// </summary>
        public void AddOwned(string tableName, byte[] bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (_bytesByTableName.ContainsKey(normalizedName))
            {
                throw new ArgumentException($"Data-table payload is already cached: {normalizedName}", nameof(tableName));
            }

            ValidateOwnedBytes(normalizedName, bytes, _totalBytes, isNewEntry: true);
            _bytesByTableName.Add(normalizedName, bytes);
            _totalBytes += bytes.Length;
        }

        /// <summary>Copies and adds or replaces one payload.</summary>
        public void Set(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            int replacedLength = GetExistingLength(normalizedName);
            long baseTotal = _totalBytes - replacedLength;
            byte[] ownedBytes = CopyAndValidate(
                normalizedName,
                bytes,
                baseTotal,
                isNewEntry: replacedLength == 0 && !_bytesByTableName.ContainsKey(normalizedName));
            ReplaceOwned(normalizedName, ownedBytes, replacedLength);
        }

        /// <summary>Takes ownership and adds or replaces one payload without copying.</summary>
        public void SetOwned(string tableName, byte[] bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            bool exists = _bytesByTableName.TryGetValue(normalizedName, out byte[] previousBytes);
            if (exists && ReferenceEquals(previousBytes, bytes))
            {
                return;
            }

            int replacedLength = exists ? previousBytes.Length : 0;
            long baseTotal = _totalBytes - replacedLength;
            ValidateOwnedBytes(normalizedName, bytes, baseTotal, isNewEntry: !exists);
            ReplaceOwned(normalizedName, bytes, replacedLength);
        }

        /// <summary>Prevents further Add, Set, or Clear operations.</summary>
        public void Seal()
        {
            ThrowIfDisposed();
            _limits.ValidateTableCount(_bytesByTableName.Count);
            _limits.ValidateTotalBytes(_totalBytes);
            _sealed = true;
        }

        public ReadOnlyMemory<byte> GetBytes(string tableName)
        {
            ThrowIfDisposed();
            string normalizedName = NormalizeRequiredName(tableName);
            if (_bytesByTableName.TryGetValue(normalizedName, out byte[] bytes))
            {
                return bytes;
            }

            throw new KeyNotFoundException($"Data-table payload is not loaded: {normalizedName}");
        }

        public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
        {
            ThrowIfDisposed();
            string normalizedName = NormalizeRequiredName(tableName);
            if (_bytesByTableName.TryGetValue(normalizedName, out byte[] ownedBytes))
            {
                bytes = ownedBytes;
                return true;
            }

            bytes = default;
            return false;
        }

        /// <summary>Removes one owned payload. Only the single mutation owner may call this before Seal.</summary>
        public bool Remove(string tableName)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (!_bytesByTableName.TryGetValue(normalizedName, out byte[] bytes))
            {
                return false;
            }

            if (_clearBytesOnDispose)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }

            _bytesByTableName.Remove(normalizedName);
            _totalBytes = checked(_totalBytes - bytes.Length);
            return true;
        }

        public void Clear()
        {
            ThrowIfDisposed();
            ThrowIfSealed();
            ClearOwnedBytes();
        }

        public void Dispose()
        {
            BeginBoundedDispose();
            ReleaseClosedPayloadsStep(int.MaxValue);
        }

        /// <summary>
        /// Closes the cache to all reads and mutations without releasing every payload in one call.
        /// The owner must subsequently call <see cref="ReleaseClosedPayloadsStep"/> until complete,
        /// or call <see cref="Dispose"/> for the synchronous fallback.
        /// </summary>
        public void BeginBoundedDispose()
        {
            _disposed = true;
        }

        /// <summary>
        /// Releases at most <paramref name="maxWork"/> payload arrays from an already closed cache.
        /// Calling this method before <see cref="BeginBoundedDispose"/> is rejected so live data
        /// cannot be removed by a pressure responder.
        /// </summary>
        public DataTableBytesCacheReleaseResult ReleaseClosedPayloadsStep(int maxWork)
        {
            if (maxWork < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWork));
            }

            if (!_disposed)
            {
                throw new InvalidOperationException(
                    "Bounded payload release is available only after the cache has been closed.");
            }

            int releasedCount = 0;
            long releasedBytes = 0;
            while (releasedCount < maxWork && _bytesByTableName != null && _bytesByTableName.Count > 0)
            {
                KeyValuePair<string, byte[]> entry;
                using (Dictionary<string, byte[]>.Enumerator enumerator = _bytesByTableName.GetEnumerator())
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    entry = enumerator.Current;
                }

                byte[] bytes = entry.Value;
                if (_clearBytesOnDispose)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }

                _bytesByTableName.Remove(entry.Key);
                _totalBytes = checked(_totalBytes - bytes.Length);
                releasedBytes = checked(releasedBytes + bytes.Length);
                releasedCount++;
            }

            _releasedPayloadCount = checked(_releasedPayloadCount + releasedCount);
            _releasedBytes = checked(_releasedBytes + releasedBytes);
            int remainingCount = _bytesByTableName?.Count ?? 0;
            if (remainingCount == 0)
            {
                _bytesByTableName = null;
                _totalBytes = 0;
            }

            return new DataTableBytesCacheReleaseResult(
                releasedCount,
                releasedBytes,
                remainingCount,
                _totalBytes);
        }

        private string ValidateMutationAndName(string tableName)
        {
            ThrowIfDisposed();
            ThrowIfSealed();
            return NormalizeRequiredName(tableName);
        }

        private string NormalizeRequiredName(string tableName)
        {
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName, _dataExtension);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

            _limits.ValidateTableName(normalizedName);
            return normalizedName;
        }

        private byte[] CopyAndValidate(
            string normalizedName,
            ReadOnlyMemory<byte> bytes,
            long baseTotal,
            bool isNewEntry)
        {
            ValidateLengths(normalizedName, bytes.Length, baseTotal, isNewEntry);
            return bytes.ToArray();
        }

        private void ValidateOwnedBytes(
            string normalizedName,
            byte[] bytes,
            long baseTotal,
            bool isNewEntry)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            ValidateLengths(normalizedName, bytes.Length, baseTotal, isNewEntry);
        }

        private void ValidateLengths(
            string normalizedName,
            int byteLength,
            long baseTotal,
            bool isNewEntry)
        {
            _limits.ValidatePayloadLength(normalizedName, byteLength);
            _limits.ValidateTotalBytes(checked(baseTotal + byteLength));
            if (isNewEntry)
            {
                _limits.ValidateTableCount(checked(_bytesByTableName.Count + 1));
            }
        }

        private int GetExistingLength(string normalizedName)
        {
            return _bytesByTableName.TryGetValue(normalizedName, out byte[] bytes)
                ? bytes.Length
                : 0;
        }

        private void ReplaceOwned(string normalizedName, byte[] bytes, int replacedLength)
        {
            if (_bytesByTableName.TryGetValue(normalizedName, out byte[] previousBytes) &&
                _clearBytesOnDispose &&
                !ReferenceEquals(previousBytes, bytes))
            {
                Array.Clear(previousBytes, 0, previousBytes.Length);
            }

            _bytesByTableName[normalizedName] = bytes;
            _totalBytes = checked(_totalBytes - replacedLength + bytes.Length);
        }

        private void ClearOwnedBytes()
        {
            if (_bytesByTableName == null)
            {
                _totalBytes = 0;
                return;
            }

            if (_clearBytesOnDispose)
            {
                foreach (byte[] bytes in _bytesByTableName.Values)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }

            _bytesByTableName.Clear();
            _totalBytes = 0;
        }

        private void ThrowIfSealed()
        {
            if (_sealed)
            {
                throw new InvalidOperationException("The data-table payload cache is sealed.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DataTableBytesCache));
            }
        }
    }
}
