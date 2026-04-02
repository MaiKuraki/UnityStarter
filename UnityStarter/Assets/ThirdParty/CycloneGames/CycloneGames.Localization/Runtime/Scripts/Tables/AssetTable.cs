using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Maps logical keys to asset references for a single locale.
    /// Use for sprites, audio clips, fonts, or any UnityEngine.Object that varies by locale.
    /// <para>
    /// Architecture mirrors <see cref="StringTable"/>: one SO = one tableId + one localeCode.
    /// Create multiple AssetTable assets with the same <see cref="TableId"/> and different
    /// <see cref="LocaleId"/> to support multiple languages.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AssetTable", menuName = "CycloneGames/Localization/Asset Table")]
    public sealed class AssetTable : ScriptableObject
    {
        [SerializeField] private string tableId;
        [SerializeField] private string localeCode;
        [SerializeField] private List<AssetEntry> entries = new List<AssetEntry>();

        private Dictionary<string, AssetRef> _lookup;
        private LocaleId _cachedLocale;

        public string TableId => tableId;

        public LocaleId LocaleId
        {
            get
            {
                if (!_cachedLocale.IsValid && !string.IsNullOrEmpty(localeCode))
                    _cachedLocale = new LocaleId(localeCode);
                return _cachedLocale;
            }
        }

        public int Count => entries.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out AssetRef value)
        {
            EnsureLookup();
            return _lookup.TryGetValue(key, out value);
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, AssetRef>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                _lookup[e.Key] = e.Asset;
            }
        }

        private void OnEnable()
        {
            _lookup = null;
            _cachedLocale = default;
        }
    }

    [Serializable]
    public struct AssetEntry
    {
        public string Key;
        public AssetRef Asset;
    }
}
