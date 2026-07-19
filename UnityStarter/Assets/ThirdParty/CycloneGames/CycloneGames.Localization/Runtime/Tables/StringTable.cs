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
                // Cache validation and canonicalization for this authoring asset.
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

            if (string.IsNullOrEmpty(tableId))
                throw new InvalidOperationException("String table ID is required.");
            if (!LocaleId.IsValid)
                throw new InvalidOperationException("String table locale code is invalid.");

            var lookup = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Key))
                    throw new InvalidOperationException("String table entries must have non-empty keys.");
                if (!keys.Add(e.Key))
                    throw new InvalidOperationException("Duplicate string key '" + e.Key + "'.");
                if (string.IsNullOrWhiteSpace(e.Value))
                    continue;
                lookup.Add(e.Key, e.Value ?? string.Empty);
            }

            _compiled = new CompiledStringTable(tableId, LocaleId, lookup, true);
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
