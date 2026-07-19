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
        private readonly Dictionary<string, byte[]> _bytesByTableName;
        private readonly string _dataExtension;
        private readonly DataTableLoadLimits _limits;
        private readonly bool _clearBytesOnDispose;
        private long _totalBytes;
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearOwnedBytes();
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
