using System;
using System.Collections.Generic;

namespace CycloneGames.DataTable
{
    public sealed class DataTableManifest
    {
        public const int DEFAULT_SCHEMA_VERSION = 1;

        private readonly Dictionary<string, DataTableManifestEntry> _entriesByTableName;
        private readonly DataTableManifestEntry[] _entries;
        private readonly bool _requireKnownTables;

        public DataTableManifest(params DataTableManifestEntry[] entries)
            : this(DEFAULT_SCHEMA_VERSION, entries, false)
        {
        }

        public DataTableManifest(
            int schemaVersion,
            IReadOnlyList<DataTableManifestEntry> entries,
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

            SchemaVersion = schemaVersion;
            _requireKnownTables = requireKnownTables;
            _entries = new DataTableManifestEntry[entries.Count];
            _entriesByTableName = new Dictionary<string, DataTableManifestEntry>(
                entries.Count,
                StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                DataTableManifestEntry entry = entries[i];
                if (string.IsNullOrEmpty(entry.TableName))
                {
                    throw new ArgumentException(
                        $"Manifest entry at index {i} has an empty table name.",
                        nameof(entries));
                }

                _entries[i] = entry;
                _entriesByTableName.Add(entry.TableName, entry);
            }
        }

        public int SchemaVersion { get; }

        public bool RequireKnownTables => _requireKnownTables;

        public IReadOnlyList<DataTableManifestEntry> Entries => _entries;

        public bool TryGetEntry(string tableName, out DataTableManifestEntry entry)
        {
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            return _entriesByTableName.TryGetValue(normalizedName, out entry);
        }

        public DataTableManifestEntry GetEntry(string tableName)
        {
            if (TryGetEntry(tableName, out DataTableManifestEntry entry))
            {
                return entry;
            }

            throw new KeyNotFoundException(
                $"Data table manifest does not contain table: {DataTableNameUtility.NormalizeTableName(tableName)}");
        }

        public void ValidateBytes(string tableName, byte[] bytes)
        {
            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            if (!TryGetEntry(normalizedName, out DataTableManifestEntry entry))
            {
                if (_requireKnownTables)
                {
                    throw new InvalidOperationException(
                        $"Data table manifest does not contain required table entry: {normalizedName}");
                }

                return;
            }

            ValidateBytes(entry, bytes);
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
            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Data table bytes are null or empty. Table={entry.TableName}");
            }

            if (entry.HasExpectedByteLength && bytes.Length != entry.ExpectedByteLength)
            {
                throw new InvalidOperationException(
                    $"Data table byte length mismatch. Table={entry.TableName}, Expected={entry.ExpectedByteLength}, Actual={bytes.Length}");
            }

            if (entry.HasSha256Hash &&
                !DataTableHashUtility.Sha256Matches(bytes, entry.Sha256Hex))
            {
                string actualHash = DataTableHashUtility.ComputeSha256Hex(bytes);
                throw new InvalidOperationException(
                    $"Data table SHA-256 mismatch. Table={entry.TableName}, Expected={entry.Sha256Hex}, Actual={actualHash}");
            }
        }
    }
}
