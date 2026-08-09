using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CycloneGames.DataTable
{
    public sealed class DataTableManifest
    {
        public const int DEFAULT_SCHEMA_VERSION = 1;

        private readonly Dictionary<string, DataTableManifestEntry> _entriesByTableName;
        private readonly DataTableManifestEntry[] _entries;
        private readonly ReadOnlyCollection<DataTableManifestEntry> _entriesView;

        public DataTableManifest(params DataTableManifestEntry[] entries)
            : this(DEFAULT_SCHEMA_VERSION, entries, DataTableLoadLimits.Default, requireKnownTables: false)
        {
        }

        public DataTableManifest(
            int schemaVersion,
            IReadOnlyList<DataTableManifestEntry> entries,
            bool requireKnownTables = false)
            : this(schemaVersion, entries, DataTableLoadLimits.Default, requireKnownTables)
        {
        }

        public DataTableManifest(
            int schemaVersion,
            IReadOnlyList<DataTableManifestEntry> entries,
            DataTableLoadLimits limits,
            bool requireKnownTables = false)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    schemaVersion,
                    "Schema version must be greater than zero.");
            }

            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            limits.EnsureValid(nameof(limits));
            int entryCount = entries.Count;
            limits.ValidateTableCount(entryCount);

            // Freeze the caller-owned list before validation. Indexers are allowed to execute
            // arbitrary code; rereading Count during the loop could otherwise accept a partially
            // initialized manifest when a re-entrant list shrinks itself.
            var entrySnapshot = new DataTableManifestEntry[entryCount];
            for (int i = 0; i < entrySnapshot.Length; i++)
            {
                entrySnapshot[i] = entries[i];
            }

            SchemaVersion = schemaVersion;
            RequireKnownTables = requireKnownTables;
            Limits = limits;
            _entries = entrySnapshot;
            _entriesView = Array.AsReadOnly(_entries);
            _entriesByTableName = new Dictionary<string, DataTableManifestEntry>(
                entryCount,
                StringComparer.OrdinalIgnoreCase);

            long knownRequiredBytes = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                DataTableManifestEntry entry = _entries[i];
                if (string.IsNullOrEmpty(entry.TableName))
                {
                    throw new ArgumentException(
                        $"Manifest entry at index {i} has an empty table name.",
                        nameof(entries));
                }

                limits.ValidateTableName(entry.TableName);
                limits.ValidateLocation(entry.Location);

                if (entry.HasExpectedByteLength)
                {
                    limits.ValidatePayloadLength(entry.TableName, entry.ExpectedByteLength);
                    if (entry.Required)
                    {
                        knownRequiredBytes = checked(knownRequiredBytes + entry.ExpectedByteLength);
                        limits.ValidateTotalBytes(knownRequiredBytes);
                    }
                }
                try
                {
                    _entriesByTableName.Add(entry.TableName, entry);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        $"Manifest contains duplicate table name '{entry.TableName}' at index {i}.",
                        nameof(entries),
                        exception);
                }
            }
        }

        public int SchemaVersion { get; }

        public bool RequireKnownTables { get; }

        public DataTableLoadLimits Limits { get; }

        public IReadOnlyList<DataTableManifestEntry> Entries => _entriesView;

        public void EnsureSchemaVersionSupported(int minimumVersion, int maximumVersion)
        {
            if (minimumVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumVersion));
            }

            if (maximumVersion < minimumVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumVersion));
            }

            if (SchemaVersion < minimumVersion || SchemaVersion > maximumVersion)
            {
                throw new NotSupportedException(
                    $"Data-table manifest schema {SchemaVersion} is not supported. Supported range={minimumVersion}..{maximumVersion}.");
            }
        }

        public bool TryGetEntry(string tableName, out DataTableManifestEntry entry)
        {
            string normalizedName = Limits.NormalizeTableName(tableName);
            return _entriesByTableName.TryGetValue(normalizedName, out entry);
        }

        public DataTableManifestEntry GetEntry(string tableName)
        {
            string normalizedName = Limits.NormalizeTableName(tableName);
            if (_entriesByTableName.TryGetValue(normalizedName, out DataTableManifestEntry entry))
            {
                return entry;
            }

            throw new KeyNotFoundException(
                $"Data-table manifest does not contain table: {normalizedName}");
        }

        /// <summary>
        /// Validates one payload before it becomes part of a candidate payload set.
        /// </summary>
        public void ValidatePayload(string tableName, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            ValidatePayload(tableName, new ReadOnlyMemory<byte>(bytes));
        }

        /// <summary>
        /// Validates one payload before it becomes part of a candidate payload set.
        /// </summary>
        public void ValidatePayload(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = Limits.NormalizeTableName(tableName);
            Limits.ValidatePayloadLength(normalizedName, bytes.Length);
            if (!_entriesByTableName.TryGetValue(normalizedName, out DataTableManifestEntry entry))
            {
                if (RequireKnownTables)
                {
                    throw new InvalidOperationException(
                        $"Data-table manifest does not contain required table entry: {normalizedName}");
                }

                return;
            }

            ValidatePayload(entry, bytes, Limits);
        }

        /// <summary>
        /// Validates the topology and aggregate budgets of an already payload-validated set.
        /// This method does not recompute content hashes, so each payload must first pass
        /// <see cref="ValidatePayload(string, ReadOnlyMemory{byte})"/> exactly once while being
        /// acquired. Inventory traversal is allocation-free when the provider exposes
        /// <see cref="IDataTableBytesInventory"/>.
        /// </summary>
        public void ValidateInventory(IDataTableBytesProvider bytesProvider)
        {
            if (bytesProvider == null)
            {
                throw new ArgumentNullException(nameof(bytesProvider));
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                DataTableManifestEntry entry = _entries[i];
                if (!entry.Required)
                {
                    continue;
                }

                if (!bytesProvider.TryGetBytes(entry.TableName, out _))
                {
                    throw new InvalidOperationException(
                        $"Required data table is not loaded: {entry.TableName}");
                }
            }

            if (!(bytesProvider is IDataTableBytesInventory inventory))
            {
                if (RequireKnownTables)
                {
                    throw new InvalidOperationException(
                        $"A manifest with {nameof(RequireKnownTables)} enabled requires a payload " +
                        $"provider that implements {nameof(IDataTableBytesInventory)}.");
                }

                return;
            }

            int payloadCount = inventory.Count;
            Limits.ValidateTableCount(payloadCount);
            long totalBytes = 0;
            for (int i = 0; i < payloadCount; i++)
            {
                string tableName = Limits.NormalizeTableName(inventory.GetTableName(i));
                if (RequireKnownTables && !_entriesByTableName.ContainsKey(tableName))
                {
                    throw new InvalidOperationException(
                        $"Data-table manifest does not contain payload inventory entry: {tableName}");
                }

                if (!bytesProvider.TryGetBytes(tableName, out ReadOnlyMemory<byte> bytes))
                {
                    throw new InvalidOperationException(
                        $"Data-table payload inventory is inconsistent with its provider: {tableName}");
                }

                Limits.ValidatePayloadLength(tableName, bytes.Length);
                totalBytes = checked(totalBytes + bytes.Length);
                Limits.ValidateTotalBytes(totalBytes);
            }
        }

        public static void ValidatePayload(DataTableManifestEntry entry, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            ValidatePayload(entry, new ReadOnlyMemory<byte>(bytes), DataTableLoadLimits.Default);
        }

        public static void ValidatePayload(DataTableManifestEntry entry, ReadOnlyMemory<byte> bytes)
        {
            ValidatePayload(entry, bytes, DataTableLoadLimits.Default);
        }

        public static void ValidatePayload(
            DataTableManifestEntry entry,
            ReadOnlyMemory<byte> bytes,
            DataTableLoadLimits limits)
        {
            limits.EnsureValid(nameof(limits));
            limits.ValidateTableName(entry.TableName);
            limits.ValidatePayloadLength(entry.TableName, bytes.Length);

            if (entry.HasExpectedByteLength && bytes.Length != entry.ExpectedByteLength)
            {
                throw new InvalidOperationException(
                    $"Data-table byte length mismatch. Table={entry.TableName}, Expected={entry.ExpectedByteLength}, Actual={bytes.Length}");
            }

            if (!entry.HasSha256Hash)
            {
                return;
            }

            string actualHash = DataTableHashUtility.ComputeSha256Hex(bytes);
            if (!string.Equals(actualHash, entry.Sha256Hex, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Data-table SHA-256 mismatch. Table={entry.TableName}, Expected={entry.Sha256Hex}, Actual={actualHash}");
            }
        }
    }
}
