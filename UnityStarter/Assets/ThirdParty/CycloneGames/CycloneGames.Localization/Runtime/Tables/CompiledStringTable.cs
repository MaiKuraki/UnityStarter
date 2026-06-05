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
        {
            TableId = tableId;
            LocaleId = localeId;
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
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
    }
}
