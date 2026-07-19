using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Runtime-only compiled lookup data for a single string table and locale.
    /// </summary>
    public sealed class CompiledStringTable
    {
        private readonly Dictionary<string, string> _lookup;

        public CompiledStringTable(string tableId, LocaleId localeId, Dictionary<string, string> lookup)
            : this(tableId, localeId, lookup, false)
        {
        }

        internal CompiledStringTable(
            string tableId,
            LocaleId localeId,
            Dictionary<string, string> lookup,
            bool takeOwnership)
        {
            if (string.IsNullOrEmpty(tableId)) throw new ArgumentException("Table ID is required.", nameof(tableId));
            if (!localeId.IsValid) throw new ArgumentException("A valid locale is required.", nameof(localeId));
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));

            TableId = tableId;
            LocaleId = localeId;
            _lookup = takeOwnership
                ? lookup
                : new Dictionary<string, string>(lookup.Count, StringComparer.Ordinal);
            foreach (var pair in lookup)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    throw new ArgumentException("Compiled string keys must not be empty.", nameof(lookup));
                if (!takeOwnership)
                    _lookup.Add(pair.Key, pair.Value ?? string.Empty);
                else if (pair.Value == null)
                    throw new ArgumentException("Owned compiled string values must not be null.", nameof(lookup));
            }
        }

        public string TableId { get; }
        public LocaleId LocaleId { get; }
        public int Count => _lookup.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = null;
                return false;
            }

            return _lookup.TryGetValue(key, out value);
        }

        internal Dictionary<string, string>.Enumerator GetEnumerator() => _lookup.GetEnumerator();
    }
}
