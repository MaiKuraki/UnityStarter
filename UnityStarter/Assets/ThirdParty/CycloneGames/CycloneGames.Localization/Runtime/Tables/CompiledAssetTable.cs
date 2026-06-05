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
        {
            TableId = tableId;
            LocaleId = localeId;
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
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
    }
}
