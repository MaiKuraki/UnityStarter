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
            limits.ValidateTableCount(entries.Count);

            SchemaVersion = schemaVersion;
            RequireKnownTables = requireKnownTables;
            Limits = limits;
            _entries = new DataTableManifestEntry[entries.Count];
            _entriesView = Array.AsReadOnly(_entries);
            _entriesByTableName = new Dictionary<string, DataTableManifestEntry>(
                entries.Count,
                StringComparer.OrdinalIgnoreCase);

            long knownRequiredBytes = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                DataTableManifestEntry entry = entries[i];
                if (string.IsNullOrEmpty(entry.TableName))
                {
                    throw new ArgumentException(
                        $"Manifest entry at index {i} has an empty table name.",
                        nameof(entries));
                }

                limits.ValidateTableName(entry.TableName);

                if (entry.HasExpectedByteLength)
                {
                    limits.ValidatePayloadLength(entry.TableName, entry.ExpectedByteLength);
                    if (entry.Required)
                    {
                        knownRequiredBytes = checked(knownRequiredBytes + entry.ExpectedByteLength);
                        limits.ValidateTotalBytes(knownRequiredBytes);
                    }
                }

                _entries[i] = entry;
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
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            Limits.ValidateTableName(normalizedName);
            return _entriesByTableName.TryGetValue(normalizedName, out entry);
        }

        public DataTableManifestEntry GetEntry(string tableName)
        {
            if (TryGetEntry(tableName, out DataTableManifestEntry entry))
            {
                return entry;
            }

            throw new KeyNotFoundException(
                $"Data-table manifest does not contain table: {DataTableNameUtility.NormalizeTableName(tableName)}");
        }

        public void ValidateBytes(string tableName, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            ValidateBytes(tableName, new ReadOnlyMemory<byte>(bytes));
        }

        public void ValidateBytes(string tableName, ReadOnlyMemory<byte> bytes)
        {
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            Limits.ValidateTableName(normalizedName);
            Limits.ValidatePayloadLength(normalizedName, bytes.Length);
            if (!TryGetEntry(normalizedName, out DataTableManifestEntry entry))
            {
                if (RequireKnownTables)
                {
                    throw new InvalidOperationException(
                        $"Data-table manifest does not contain required table entry: {normalizedName}");
                }

                return;
            }

            ValidateBytes(entry, bytes, Limits);
        }

        public void ValidateRequiredTables(IDataTableBytesProvider bytesProvider)
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
        }

        public static void ValidateBytes(DataTableManifestEntry entry, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            ValidateBytes(entry, new ReadOnlyMemory<byte>(bytes), DataTableLoadLimits.Default);
        }

        public static void ValidateBytes(DataTableManifestEntry entry, ReadOnlyMemory<byte> bytes)
        {
            ValidateBytes(entry, bytes, DataTableLoadLimits.Default);
        }

        public static void ValidateBytes(
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
