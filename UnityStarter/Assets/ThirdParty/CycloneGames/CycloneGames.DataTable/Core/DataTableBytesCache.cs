using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Bounded, single-owner storage for materialized table payloads.
    /// </summary>
    /// <remarks>
    /// Mutations, <see cref="Seal"/>, <see cref="Close"/>, <see cref="ReleaseStep"/>, and
    /// <see cref="Dispose"/> are not thread-safe and must be serialized by one owner. After
    /// <see cref="Seal"/>, concurrent readers are permitted until the owner closes the cache.
    /// Closing must never race a reader. Normal lookups and inventory index access are O(1).
    /// Incremental release is O(N) without byte clearing, or O(N + B) when B bytes are cleared.
    /// </remarks>
    public sealed class DataTableBytesCache :
        IDataTableBytesProvider,
        IDataTableBytesInventory,
        IDisposable
    {
        private Dictionary<string, int> _payloadIndexByTableName;
        private List<PayloadEntry> _payloads;
        private readonly string _dataExtension;
        private readonly DataTableLoadLimits _limits;
        private readonly bool _clearBytesOnRelease;
        private long _retainedBytes;
        private long _releasedPayloadCount;
        private long _releasedBytes;
        private long _clearedBytes;
        private int _releasePayloadIndex;
        private int _releaseByteOffset;
        private int _remainingPayloadCount;
        private bool _sealed;
        private bool _closed;

        public DataTableBytesCache(int capacity = 8, string dataExtension = ".bytes")
            : this(DataTableLoadLimits.Default, capacity, dataExtension)
        {
        }

        public DataTableBytesCache(
            DataTableLoadLimits limits,
            int capacity = 8,
            string dataExtension = ".bytes",
            bool clearBytesOnRelease = false)
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
            _payloadIndexByTableName = new Dictionary<string, int>(
                capacity,
                StringComparer.OrdinalIgnoreCase);
            _payloads = new List<PayloadEntry>(capacity);
            _dataExtension = DataTableNameUtility.NormalizeDataExtension(dataExtension);
            _clearBytesOnRelease = clearBytesOnRelease;
        }

        public int Count
        {
            get
            {
                ThrowIfClosed();
                return _payloads.Count;
            }
        }

        public long TotalBytes
        {
            get
            {
                ThrowIfClosed();
                return _retainedBytes;
            }
        }

        public bool IsSealed => _sealed;

        public bool IsClosed => _closed;

        public bool IsReleaseComplete => _closed && _payloads == null;

        public DataTableLoadLimits Limits => _limits;

        /// <summary>
        /// Returns a table name in O(1) without allocating. The index is valid only while no
        /// mutation or close operation is performed by the owner.
        /// </summary>
        public string GetTableName(int index)
        {
            ThrowIfClosed();
            if ((uint)index >= (uint)_payloads.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _payloads[index].TableName;
        }

        /// <summary>
        /// Captures allocation-free aggregate diagnostics. The snapshot remains available after
        /// closure while incremental release is in progress.
        /// </summary>
        public DataTableBytesCacheMemorySnapshot GetMemorySnapshot()
        {
            int payloadCount = _closed
                ? _remainingPayloadCount
                : _payloads.Count;
            return new DataTableBytesCacheMemorySnapshot(
                payloadCount,
                _retainedBytes,
                _sealed,
                _closed,
                IsReleaseComplete,
                _limits,
                _releasedPayloadCount,
                _releasedBytes,
                _clearedBytes);
        }

        /// <summary>Copies the supplied memory so later caller mutation cannot affect the cache.</summary>
        public void Add(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (_payloadIndexByTableName.ContainsKey(normalizedName))
            {
                throw new ArgumentException(
                    $"Data-table payload is already cached: {normalizedName}",
                    nameof(tableName));
            }

            byte[] ownedBytes = CopyAndValidate(normalizedName, bytes, _retainedBytes, isNewEntry: true);
            AddValidatedOwned(normalizedName, ownedBytes);
        }

        /// <summary>
        /// Takes ownership of an array without copying. The caller must relinquish all writable
        /// aliases after this method succeeds.
        /// </summary>
        public void AddOwned(string tableName, byte[] bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (_payloadIndexByTableName.ContainsKey(normalizedName))
            {
                throw new ArgumentException(
                    $"Data-table payload is already cached: {normalizedName}",
                    nameof(tableName));
            }

            ValidateOwnedBytes(normalizedName, bytes, _retainedBytes, isNewEntry: true);
            AddValidatedOwned(normalizedName, bytes);
        }

        /// <summary>Copies and adds or replaces one payload.</summary>
        public void Set(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            bool exists = _payloadIndexByTableName.TryGetValue(normalizedName, out int index);
            int replacedLength = exists ? _payloads[index].Bytes.Length : 0;
            long baseTotal = checked(_retainedBytes - replacedLength);
            byte[] ownedBytes = CopyAndValidate(normalizedName, bytes, baseTotal, !exists);
            SetValidatedOwned(normalizedName, ownedBytes, exists, index, replacedLength);
        }

        /// <summary>Takes ownership and adds or replaces one payload without copying.</summary>
        public void SetOwned(string tableName, byte[] bytes)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            bool exists = _payloadIndexByTableName.TryGetValue(normalizedName, out int index);
            byte[] previousBytes = exists ? _payloads[index].Bytes : null;
            if (exists && ReferenceEquals(previousBytes, bytes))
            {
                return;
            }

            int replacedLength = exists ? previousBytes.Length : 0;
            long baseTotal = checked(_retainedBytes - replacedLength);
            ValidateOwnedBytes(normalizedName, bytes, baseTotal, !exists);
            SetValidatedOwned(normalizedName, bytes, exists, index, replacedLength);
        }

        /// <summary>Prevents further mutation and permits coordinated concurrent reads.</summary>
        public void Seal()
        {
            ThrowIfClosed();
            _limits.ValidateTableCount(_payloads.Count);
            _limits.ValidateTotalBytes(_retainedBytes);
            _sealed = true;
        }

        public ReadOnlyMemory<byte> GetBytes(string tableName)
        {
            ThrowIfClosed();
            string normalizedName = NormalizeRequiredName(tableName);
            if (_payloadIndexByTableName.TryGetValue(normalizedName, out int index))
            {
                return _payloads[index].Bytes;
            }

            throw new KeyNotFoundException($"Data-table payload is not loaded: {normalizedName}");
        }

        public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
        {
            ThrowIfClosed();
            string normalizedName = NormalizeRequiredName(tableName);
            if (_payloadIndexByTableName.TryGetValue(normalizedName, out int index))
            {
                bytes = _payloads[index].Bytes;
                return true;
            }

            bytes = default;
            return false;
        }

        /// <summary>
        /// Removes one payload in O(1) using unordered swap-back compaction. Only the mutation
        /// owner may call this before <see cref="Seal"/>.
        /// </summary>
        public bool Remove(string tableName)
        {
            string normalizedName = ValidateMutationAndName(tableName);
            if (!_payloadIndexByTableName.TryGetValue(normalizedName, out int index))
            {
                return false;
            }

            PayloadEntry removed = _payloads[index];
            ClearOwnedBytesSynchronously(removed.Bytes);

            int lastIndex = _payloads.Count - 1;
            if (index != lastIndex)
            {
                PayloadEntry moved = _payloads[lastIndex];
                _payloads[index] = moved;
                _payloadIndexByTableName[moved.TableName] = index;
            }

            _payloads.RemoveAt(lastIndex);
            _payloadIndexByTableName.Remove(normalizedName);
            _retainedBytes = checked(_retainedBytes - removed.Bytes.Length);
            return true;
        }

        public void Clear()
        {
            ThrowIfClosed();
            ThrowIfSealed();
            ClearOwnedPayloadsSynchronously();
        }

        /// <summary>
        /// Closes the owner-facing API and initializes the forward-only release cursor.
        /// This operation is O(1), allocation-free, and idempotent. The owner must then call
        /// <see cref="ReleaseStep"/> until complete, or call <see cref="Dispose"/>.
        /// </summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _remainingPayloadCount = _payloads.Count;
            _releasePayloadIndex = 0;
            _releaseByteOffset = 0;
            _payloadIndexByTableName = null;
            _closed = true;

            if (_remainingPayloadCount == 0)
            {
                CompleteRelease();
            }
        }

        /// <summary>
        /// Advances a closed cache's release cursor without allocation. Payload work is bounded by
        /// <see cref="DataTableBytesCacheReleaseBudget.MaxPayloads"/>. When byte clearing is
        /// enabled, bytes touched by <see cref="Array.Clear(Array,int,int)"/> are additionally
        /// bounded by <see cref="DataTableBytesCacheReleaseBudget.MaxBytesToClear"/>; a single
        /// large array is therefore cleared across multiple calls.
        /// </summary>
        public DataTableBytesCacheReleaseResult ReleaseStep(
            in DataTableBytesCacheReleaseBudget budget)
        {
            if (!_closed)
            {
                throw new InvalidOperationException("Payload release requires a closed cache.");
            }

            if (IsReleaseComplete || budget.MaxPayloads == 0)
            {
                return CreateReleaseResult(0, 0, 0, 0);
            }

            if (_clearBytesOnRelease && budget.MaxBytesToClear == 0)
            {
                return CreateReleaseResult(0, 0, 0, 0);
            }

            int processedPayloads = 0;
            long clearedBytes = 0;
            int releasedPayloads = 0;
            long releasedBytes = 0;

            while (processedPayloads < budget.MaxPayloads && _remainingPayloadCount > 0)
            {
                PayloadEntry entry = _payloads[_releasePayloadIndex];
                byte[] bytes = entry.Bytes;
                processedPayloads++;

                if (_clearBytesOnRelease)
                {
                    long availableClearBytes = budget.MaxBytesToClear - clearedBytes;
                    if (availableClearBytes <= 0)
                    {
                        processedPayloads--;
                        break;
                    }

                    int remainingInPayload = bytes.Length - _releaseByteOffset;
                    int clearLength = availableClearBytes >= remainingInPayload
                        ? remainingInPayload
                        : (int)availableClearBytes;
                    Array.Clear(bytes, _releaseByteOffset, clearLength);
                    _releaseByteOffset += clearLength;
                    clearedBytes = checked(clearedBytes + clearLength);
                    _clearedBytes = checked(_clearedBytes + clearLength);

                    if (_releaseByteOffset < bytes.Length)
                    {
                        break;
                    }
                }

                _payloads[_releasePayloadIndex] = default;
                _releasePayloadIndex++;
                _releaseByteOffset = 0;
                _remainingPayloadCount = checked(_remainingPayloadCount - 1);
                _retainedBytes = checked(_retainedBytes - bytes.Length);
                _releasedPayloadCount = checked(_releasedPayloadCount + 1);
                _releasedBytes = checked(_releasedBytes + bytes.Length);
                releasedPayloads++;
                releasedBytes = checked(releasedBytes + bytes.Length);
            }

            if (_remainingPayloadCount == 0)
            {
                CompleteRelease();
            }

            return CreateReleaseResult(
                processedPayloads,
                clearedBytes,
                releasedPayloads,
                releasedBytes);
        }

        /// <summary>Closes and synchronously releases every remaining owned payload.</summary>
        public void Dispose()
        {
            Close();
            if (!IsReleaseComplete)
            {
                ReleaseStep(DataTableBytesCacheReleaseBudget.Unlimited);
            }
        }

        private void AddValidatedOwned(string normalizedName, byte[] bytes)
        {
            int index = _payloads.Count;
            _payloads.Add(new PayloadEntry(normalizedName, bytes));
            try
            {
                _payloadIndexByTableName.Add(normalizedName, index);
            }
            catch
            {
                _payloads.RemoveAt(index);
                throw;
            }

            _retainedBytes = checked(_retainedBytes + bytes.Length);
        }

        private void SetValidatedOwned(
            string normalizedName,
            byte[] bytes,
            bool exists,
            int index,
            int replacedLength)
        {
            if (!exists)
            {
                AddValidatedOwned(normalizedName, bytes);
                return;
            }

            byte[] previousBytes = _payloads[index].Bytes;
            _payloads[index] = new PayloadEntry(_payloads[index].TableName, bytes);
            _retainedBytes = checked(_retainedBytes - replacedLength + bytes.Length);
            ClearOwnedBytesSynchronously(previousBytes);
        }

        private string ValidateMutationAndName(string tableName)
        {
            ThrowIfClosed();
            ThrowIfSealed();
            return NormalizeRequiredName(tableName);
        }

        private string NormalizeRequiredName(string tableName)
        {
            string normalizedName = _limits.NormalizeTableName(tableName, _dataExtension);
            if (string.IsNullOrEmpty(normalizedName))
            {
                throw new ArgumentException("Table name is null or empty.", nameof(tableName));
            }

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
                _limits.ValidateTableCount(checked(_payloads.Count + 1));
            }
        }

        private void ClearOwnedPayloadsSynchronously()
        {
            if (_clearBytesOnRelease)
            {
                for (int index = 0; index < _payloads.Count; index++)
                {
                    Array.Clear(_payloads[index].Bytes, 0, _payloads[index].Bytes.Length);
                }
            }

            _payloads.Clear();
            _payloadIndexByTableName.Clear();
            _retainedBytes = 0;
        }

        private void ClearOwnedBytesSynchronously(byte[] bytes)
        {
            if (_clearBytesOnRelease)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private DataTableBytesCacheReleaseResult CreateReleaseResult(
            int processedPayloads,
            long clearedBytes,
            int releasedPayloads,
            long releasedBytes)
        {
            return new DataTableBytesCacheReleaseResult(
                processedPayloads,
                clearedBytes,
                releasedPayloads,
                releasedBytes,
                _remainingPayloadCount,
                _retainedBytes,
                IsReleaseComplete);
        }

        private void CompleteRelease()
        {
            if (_remainingPayloadCount != 0 || _retainedBytes != 0)
            {
                throw new InvalidOperationException(
                    "Payload release accounting is inconsistent with the release cursor.");
            }

            _payloads = null;
            _remainingPayloadCount = 0;
            _retainedBytes = 0;
            _releasePayloadIndex = 0;
            _releaseByteOffset = 0;
        }

        private void ThrowIfSealed()
        {
            if (_sealed)
            {
                throw new InvalidOperationException("The data-table payload cache is sealed.");
            }
        }

        private void ThrowIfClosed()
        {
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(DataTableBytesCache));
            }
        }

        private readonly struct PayloadEntry
        {
            public PayloadEntry(string tableName, byte[] bytes)
            {
                TableName = tableName;
                Bytes = bytes;
            }

            public string TableName { get; }

            public byte[] Bytes { get; }
        }
    }
}
