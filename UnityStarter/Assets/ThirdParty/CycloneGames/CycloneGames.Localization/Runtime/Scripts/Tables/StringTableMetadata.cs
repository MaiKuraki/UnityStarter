using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Distinguishes metadata for string tables vs asset tables.
    /// Allows the same table ID to have separate metadata per type.
    /// </summary>
    public enum TableType
    {
        String = 0,
        Asset  = 1,
    }

    /// <summary>
    /// Editor-only metadata attached to a string table entry.
    /// Provides context for translators: notes, character limits, tags, screenshots.
    /// <para>
    /// <b>Runtime behavior</b>: In release builds, metadata is stripped from lookups entirely.
    /// Only the <see cref="MaxLength"/> field is available at runtime (for input validation),
    /// and only when explicitly queried — it does not affect normal string resolution.
    /// </para>
    /// </summary>
    [Serializable]
    public struct EntryMetadata
    {
        /// <summary>
        /// The entry key this metadata is attached to.
        /// </summary>
        public string Key;

        /// <summary>
        /// Translator notes describing context, tone, or constraints.
        /// Example: "Button label on the main menu — keep short"
        /// </summary>
        [TextArea(1, 3)]
        public string Comment;

        /// <summary>
        /// Maximum character count for this entry (0 = no limit).
        /// Used both as a hint for translators and for runtime validation.
        /// </summary>
        public int MaxLength;

        /// <summary>
        /// Whether this entry is locked (finalized, should not be modified by translators).
        /// </summary>
        public bool Locked;

        /// <summary>
        /// Comma-separated tags for categorization and filtering.
        /// Example: "menu,button,short"
        /// </summary>
        public string Tags;

        /// <summary>
        /// Reference screenshot showing where this text appears in the UI.
        /// Stored as an asset reference to avoid bloating builds — Editor only.
        /// </summary>
        #if UNITY_EDITOR
        public Texture2D Screenshot;
        #endif
    }

    /// <summary>
    /// Serializable metadata collection for a <see cref="StringTable"/>.
    /// Stored as a parallel asset alongside the StringTable, or embedded directly.
    /// <para>
    /// <b>Design rationale</b>: Metadata is stored in a separate ScriptableObject
    /// so that runtime StringTable assets remain lean. The metadata asset is only
    /// loaded in Editor / dev builds.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "StringTableMetadata", menuName = "CycloneGames/Localization/String Table Metadata")]
    public sealed class StringTableMetadata : ScriptableObject
    {
        [SerializeField] private string tableId;
        [SerializeField] private TableType tableType;
        [SerializeField] private List<EntryMetadata> entries = new List<EntryMetadata>();

        private Dictionary<string, int> _indexLookup;

        public string TableId => tableId;
        public TableType TableType => tableType;
        public IReadOnlyList<EntryMetadata> Entries => entries;

        /// <summary>
        /// O(1) lookup for metadata by entry key. Returns false if no metadata exists for this key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetMetadata(string key, out EntryMetadata metadata)
        {
            EnsureLookup();
            if (_indexLookup.TryGetValue(key, out int idx))
            {
                metadata = entries[idx];
                return true;
            }
            metadata = default;
            return false;
        }

        /// <summary>
        /// Returns the max character length for an entry, or 0 if no limit is set.
        /// This is the only metadata field intended for runtime use (input validation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMaxLength(string key)
        {
            EnsureLookup();
            if (_indexLookup.TryGetValue(key, out int idx))
                return entries[idx].MaxLength;
            return 0;
        }

        private void EnsureLookup()
        {
            if (_indexLookup != null) return;

            _indexLookup = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!string.IsNullOrEmpty(e.Key))
                    _indexLookup[e.Key] = i;
            }
        }

        private void OnEnable()
        {
            // Force rebuild on hot-reload
            _indexLookup = null;
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Editor-only: ensures an entry exists for the given key, creating one if needed.
        /// Returns the index of the entry.
        /// </summary>
        public int EnsureEntry(string key)
        {
            EnsureLookup();
            if (_indexLookup.TryGetValue(key, out int idx))
                return idx;

            var meta = new EntryMetadata { Key = key };
            entries.Add(meta);
            idx = entries.Count - 1;
            _indexLookup[key] = idx;
            return idx;
        }

        /// <summary>
        /// Editor-only: removes metadata for the given key.
        /// </summary>
        public bool RemoveEntry(string key)
        {
            EnsureLookup();
            if (!_indexLookup.TryGetValue(key, out int idx))
                return false;

            entries.RemoveAt(idx);
            _indexLookup = null; // Invalidate — indices shifted
            return true;
        }

        /// <summary>
        /// Editor-only: sets the metadata for an entry at the given index.
        /// </summary>
        public void SetEntry(int index, EntryMetadata metadata)
        {
            if (index < 0 || index >= entries.Count) return;
            entries[index] = metadata;
            // Update key mapping if key changed
            if (_indexLookup != null)
                _indexLookup[metadata.Key] = index;
        }
        #endif
    }
}
