using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    [CreateAssetMenu(fileName = "LocalizationCatalog", menuName = "CycloneGames/Localization/Catalog")]
    public sealed class LocalizationCatalog : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

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
}
