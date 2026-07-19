using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Runtime lookup data compiled from an <see cref="AssetTable"/> authoring asset.
    /// </summary>
    public sealed class CompiledAssetTable
    {
        private readonly Dictionary<string, AssetRef> _lookup;

        public CompiledAssetTable(string tableId, LocaleId localeId, Dictionary<string, AssetRef> lookup)
            : this(tableId, localeId, lookup, false)
        {
        }

        internal CompiledAssetTable(
            string tableId,
            LocaleId localeId,
            Dictionary<string, AssetRef> lookup,
            bool takeOwnership)
        {
            if (string.IsNullOrEmpty(tableId)) throw new ArgumentException("Table ID is required.", nameof(tableId));
            if (!localeId.IsValid) throw new ArgumentException("A valid locale is required.", nameof(localeId));
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));

            TableId = tableId;
            LocaleId = localeId;
            _lookup = takeOwnership
                ? lookup
                : new Dictionary<string, AssetRef>(lookup.Count, StringComparer.Ordinal);
            foreach (var pair in lookup)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    throw new ArgumentException("Compiled asset keys must not be empty.", nameof(lookup));
                if (!takeOwnership)
                    _lookup.Add(pair.Key, pair.Value);
            }
        }

        public string TableId { get; }
        public LocaleId LocaleId { get; }
        public int Count => _lookup.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out AssetRef value)
        {
            if (string.IsNullOrEmpty(key))
            {
                value = default;
                return false;
            }

            return _lookup.TryGetValue(key, out value);
        }

        internal Dictionary<string, AssetRef>.Enumerator GetEnumerator() => _lookup.GetEnumerator();
    }
}
