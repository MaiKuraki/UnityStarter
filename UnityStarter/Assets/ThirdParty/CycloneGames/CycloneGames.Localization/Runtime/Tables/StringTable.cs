using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Localization.Core;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Authoring asset for key-value string pairs belonging to a single locale.
    /// </summary>
    [CreateAssetMenu(fileName = "StringTable", menuName = "CycloneGames/Localization/String Table")]
    public sealed class StringTable : ScriptableObject
    {
        [SerializeField] private string tableId;
        [SerializeField] private string localeCode;
        [SerializeField] private List<StringEntry> entries = new List<StringEntry>();

        private LocaleId _cachedLocale;
        private CompiledStringTable _compiled;

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
            return Compile().TryGetValue(key, out value);
        }

        public void WarmUp()
        {
            Compile();
        }

        public CompiledStringTable Compile()
        {
            if (_compiled != null) return _compiled;

            var lookup = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Key)) continue;
                lookup[e.Key] = e.Value;
            }

            _compiled = new CompiledStringTable(tableId, LocaleId, lookup);
            return _compiled;
        }

        private void OnEnable()
        {
            _cachedLocale = default;
            _compiled = null;
        }

        private void OnValidate()
        {
            _cachedLocale = default;
            _compiled = null;
        }
    }

    [Serializable]
    public struct StringEntry
    {
        public string Key;
        public string Value;
    }
}
