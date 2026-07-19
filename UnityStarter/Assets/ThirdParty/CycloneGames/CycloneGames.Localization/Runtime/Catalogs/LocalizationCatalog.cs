using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    [CreateAssetMenu(fileName = "LocalizationCatalog", menuName = "CycloneGames/Localization/Catalog")]
    public sealed class LocalizationCatalog : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string catalogVersion = "1.0.0";
        [SerializeField] private string contentHash;
        [SerializeField] private List<CatalogStringTable> stringTables = new List<CatalogStringTable>();
        [SerializeField] private List<CatalogAssetTable> assetTables = new List<CatalogAssetTable>();

        public int SchemaVersion => schemaVersion;
        public string CatalogVersion => catalogVersion;
        public string ContentHash => contentHash;
        public IReadOnlyList<CatalogStringTable> StringTables => stringTables;
        public IReadOnlyList<CatalogAssetTable> AssetTables => assetTables;

        /// <summary>
        /// Computes the canonical SHA-256 content hash without materializing a second full catalog payload.
        /// </summary>
        public static string ComputeContentHash(
            IReadOnlyList<CatalogStringTable> strings,
            IReadOnlyList<CatalogAssetTable> assets)
        {
            if (strings == null) throw new ArgumentNullException(nameof(strings));
            if (assets == null) throw new ArgumentNullException(nameof(assets));

            var sortedStrings = new List<CatalogStringTable>(strings.Count);
            for (int i = 0; i < strings.Count; i++)
            {
                if (strings[i] == null)
                    throw new ArgumentException("Catalog hash input contains a null string table.", nameof(strings));
                sortedStrings.Add(strings[i]);
            }
            sortedStrings.Sort(CompareStringTables);

            var sortedAssets = new List<CatalogAssetTable>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == null)
                    throw new ArgumentException("Catalog hash input contains a null asset table.", nameof(assets));
                sortedAssets.Add(assets[i]);
            }
            sortedAssets.Sort(CompareAssetTables);

            using (var writer = new CatalogHashWriter())
            {
                writer.WriteInteger(CurrentSchemaVersion);
                writer.WriteNewLine();
                writer.WriteValue("string-table-count");
                writer.WriteInteger(sortedStrings.Count);
                writer.WriteNewLine();

                for (int i = 0; i < sortedStrings.Count; i++)
                {
                    CatalogStringTable table = sortedStrings[i];

                    writer.WriteValue("string-table");
                    writer.WriteValue(table.TableId);
                    writer.WriteValue(table.LocaleId.Code);

                    IReadOnlyList<CatalogStringEntry> sourceEntries = table.Entries;
                    var entries = new List<CatalogStringEntry>(sourceEntries.Count);
                    for (int entryIndex = 0; entryIndex < sourceEntries.Count; entryIndex++)
                        entries.Add(sourceEntries[entryIndex]);
                    entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                    writer.WriteInteger(entries.Count);
                    writer.WriteNewLine();
                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        CatalogStringEntry entry = entries[entryIndex];
                        writer.WriteValue("string-entry");
                        writer.WriteValue(entry.Key);
                        writer.WriteValue(entry.Value);
                    }
                }

                writer.WriteValue("asset-table-count");
                writer.WriteInteger(sortedAssets.Count);
                writer.WriteNewLine();

                for (int i = 0; i < sortedAssets.Count; i++)
                {
                    CatalogAssetTable table = sortedAssets[i];

                    writer.WriteValue("asset-table");
                    writer.WriteValue(table.TableId);
                    writer.WriteValue(table.LocaleId.Code);

                    IReadOnlyList<CatalogAssetEntry> sourceEntries = table.Entries;
                    var entries = new List<CatalogAssetEntry>(sourceEntries.Count);
                    for (int entryIndex = 0; entryIndex < sourceEntries.Count; entryIndex++)
                        entries.Add(sourceEntries[entryIndex]);
                    entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                    writer.WriteInteger(entries.Count);
                    writer.WriteNewLine();
                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        CatalogAssetEntry entry = entries[entryIndex];
                        writer.WriteValue("asset-entry");
                        writer.WriteValue(entry.Key);
                        writer.WriteValue(entry.Asset.Location);
                        writer.WriteValue(entry.Asset.Guid);
                    }
                }

                return writer.Complete();
            }
        }

        private static int CompareStringTables(CatalogStringTable left, CatalogStringTable right)
        {
            int table = string.CompareOrdinal(left.TableId, right.TableId);
            return table != 0 ? table : string.CompareOrdinal(left.LocaleId.Code, right.LocaleId.Code);
        }

        private static int CompareAssetTables(CatalogAssetTable left, CatalogAssetTable right)
        {
            int table = string.CompareOrdinal(left.TableId, right.TableId);
            return table != 0 ? table : string.CompareOrdinal(left.LocaleId.Code, right.LocaleId.Code);
        }

        #if UNITY_EDITOR
        public void SetData(
            string version,
            string hash,
            List<CatalogStringTable> strings,
            List<CatalogAssetTable> assets)
        {
            schemaVersion = CurrentSchemaVersion;
            catalogVersion = string.IsNullOrEmpty(version) ? "1.0.0" : version;
            contentHash = hash ?? string.Empty;
            stringTables = strings ?? new List<CatalogStringTable>();
            assetTables = assets ?? new List<CatalogAssetTable>();
        }
        #endif
    }

    [Serializable]
    public sealed class CatalogStringTable
    {
        [SerializeField] private string tableId;
        [SerializeField] private string localeCode;
        [SerializeField] private List<CatalogStringEntry> entries = new List<CatalogStringEntry>();

        public CatalogStringTable(string tableId, string localeCode, List<CatalogStringEntry> entries)
        {
            this.tableId = tableId;
            this.localeCode = localeCode;
            this.entries = entries ?? new List<CatalogStringEntry>();
        }

        public string TableId => tableId;
        public LocaleId LocaleId => string.IsNullOrEmpty(localeCode) ? LocaleId.Invalid : new LocaleId(localeCode);
        public IReadOnlyList<CatalogStringEntry> Entries => entries;
    }

    [Serializable]
    public struct CatalogStringEntry
    {
        [SerializeField] private string key;
        [SerializeField] private string value;

        public CatalogStringEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }

        public string Key => key;
        public string Value => value;
    }

    [Serializable]
    public sealed class CatalogAssetTable
    {
        [SerializeField] private string tableId;
        [SerializeField] private string localeCode;
        [SerializeField] private List<CatalogAssetEntry> entries = new List<CatalogAssetEntry>();

        public CatalogAssetTable(string tableId, string localeCode, List<CatalogAssetEntry> entries)
        {
            this.tableId = tableId;
            this.localeCode = localeCode;
            this.entries = entries ?? new List<CatalogAssetEntry>();
        }

        public string TableId => tableId;
        public LocaleId LocaleId => string.IsNullOrEmpty(localeCode) ? LocaleId.Invalid : new LocaleId(localeCode);
        public IReadOnlyList<CatalogAssetEntry> Entries => entries;
    }

    [Serializable]
    public struct CatalogAssetEntry
    {
        [SerializeField] private string key;
        [SerializeField] private AssetRef asset;

        public CatalogAssetEntry(string key, AssetRef asset)
        {
            this.key = key;
            this.asset = asset;
        }

        public string Key => key;
        public AssetRef Asset => asset;
    }

    internal sealed class CatalogHashWriter : IDisposable
    {
        private const int BufferSize = 4096;
        private const int MaxCharsPerChunk = BufferSize / 3;

        private static readonly Encoding s_StrictUtf8 = new UTF8Encoding(false, true);

        private readonly SHA256 _hash = SHA256.Create();
        private readonly byte[] _buffer = new byte[BufferSize];
        private bool _completed;

        public void WriteValue(string value)
        {
            if (value == null)
            {
                WriteAscii("-1:\n");
                return;
            }

            WriteInteger(value.Length);
            WriteByte((byte)':');
            WriteUtf8(value);
            WriteNewLine();
        }

        public void WriteInteger(int value)
        {
            int position = _buffer.Length;
            uint remaining;
            if (value < 0)
            {
                remaining = (uint)(-(long)value);
            }
            else
            {
                remaining = (uint)value;
            }

            do
            {
                _buffer[--position] = (byte)('0' + remaining % 10);
                remaining /= 10;
            }
            while (remaining != 0);

            if (value < 0)
                _buffer[--position] = (byte)'-';
            Transform(position, _buffer.Length - position);
        }

        public void WriteNewLine()
        {
            WriteByte((byte)'\n');
        }

        public string Complete()
        {
            if (_completed) throw new InvalidOperationException("The catalog hash is already complete.");
            _completed = true;
            _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            byte[] hash = _hash.Hash;
            var characters = new char[hash.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < hash.Length; i++)
            {
                characters[i * 2] = digits[hash[i] >> 4];
                characters[i * 2 + 1] = digits[hash[i] & 0x0F];
            }

            return new string(characters);
        }

        public void Dispose()
        {
            _hash.Dispose();
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        private void WriteUtf8(string value)
        {
            int index = 0;
            while (index < value.Length)
            {
                int count = Math.Min(MaxCharsPerChunk, value.Length - index);
                if (index + count < value.Length && char.IsHighSurrogate(value[index + count - 1]))
                    count--;

                int byteCount = s_StrictUtf8.GetBytes(value, index, count, _buffer, 0);
                Transform(0, byteCount);
                index += count;
            }
        }

        private void WriteAscii(string value)
        {
            for (int i = 0; i < value.Length; i++)
                _buffer[i] = (byte)value[i];
            Transform(0, value.Length);
        }

        private void WriteByte(byte value)
        {
            _buffer[0] = value;
            Transform(0, 1);
        }

        private void Transform(int offset, int count)
        {
            if (_completed) throw new InvalidOperationException("The catalog hash is already complete.");
            if (count > 0)
                _hash.TransformBlock(_buffer, offset, count, _buffer, offset);
        }
    }
}
