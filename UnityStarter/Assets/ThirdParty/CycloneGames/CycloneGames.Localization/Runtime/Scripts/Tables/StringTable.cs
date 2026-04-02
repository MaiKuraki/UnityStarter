using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// A collection of key→value string pairs for a single locale.
    /// Loaded at runtime as ScriptableObject; entries are baked into a dictionary on first access.
    /// </summary>
    [CreateAssetMenu(fileName = "StringTable", menuName = "CycloneGames/Localization/String Table")]
    public sealed class StringTable : ScriptableObject
    {
        [SerializeField] private string tableId;
        [SerializeField] private string localeCode;
        [SerializeField] private List<StringEntry> entries = new List<StringEntry>();

        private Dictionary<string, string> _lookup;
        private LocaleId _cachedLocale;

        public string TableId => tableId;

        public LocaleId LocaleId
        {
            get
            {
                // Cache to avoid repeated interning
                if (!_cachedLocale.IsValid && !string.IsNullOrEmpty(localeCode))
                    _cachedLocale = new LocaleId(localeCode);
                return _cachedLocale;
            }
        }

        public int Count => entries.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out string value)
        {
            EnsureLookup();
            return _lookup.TryGetValue(key, out value);
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                _lookup[e.Key] = e.Value;
            }
        }

        private void OnEnable()
        {
            // Force rebuild on hot-reload
            _lookup = null;
            _cachedLocale = default;
        }
    }

    [Serializable]
    public struct StringEntry
    {
        public string Key;
        public string Value;
    }
}
