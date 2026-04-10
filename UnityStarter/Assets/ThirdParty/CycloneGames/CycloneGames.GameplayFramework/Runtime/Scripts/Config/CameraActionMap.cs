using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Shareable ScriptableObject that maps action keys to camera preset entries.
    ///
    /// Assign one map to many CameraActionBinding components for a team-wide camera response table
    /// (e.g. all dodge actions share the same preset per character class).
    ///
    /// Per-component inline entries on CameraActionBinding always override map entries of the same key,
    /// so individual characters can still customise specific actions without editing the shared asset.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraActionMap", menuName = "CycloneGames/GameplayFramework/Camera/CameraActionMap")]
    public class CameraActionMap : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Unique identifier used to look up this entry from any animation system.")]
            public string ActionKey;

            [Tooltip("The camera preset to activate when this action is triggered.")]
            public CameraActionPreset Preset;

            [Tooltip("How to handle re-triggering while an action with the same key is already running.")]
            public CameraActionBinding.TriggerPolicy Policy;

            [Tooltip("Automatically remove the camera mode when the preset duration elapses.")]
            public bool AutoRemoveOnFinish;

            [Tooltip("Duration override in seconds. Non-positive = use preset's own duration.")]
            public float DurationOverride;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>(8);

        // Runtime lookup table: key → index into 'entries'.
        // Built lazily on first TryGetEntry call; invalidated on asset reload.
        private Dictionary<string, int> runtimeLookup;

        private void OnEnable()
        {
            // Invalidate so the table is rebuilt after every domain reload / asset re-import.
            runtimeLookup = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Rebuild immediately in the Editor so changes in the Inspector take effect.
            runtimeLookup = null;
        }
#endif

        /// <summary>Looks up an entry by exact key match. O(1) after first call. Returns false when not found.</summary>
        public bool TryGetEntry(string key, out Entry entry)
        {
            if (runtimeLookup == null) BuildLookupTable();

            if (!runtimeLookup.TryGetValue(key, out int index))
            {
                entry = default;
                return false;
            }

            entry = entries[index];
            return true;
        }

        /// <summary>Read-only view of all entries for iteration.</summary>
        public IReadOnlyList<Entry> GetEntries() => entries;

        private void BuildLookupTable()
        {
            runtimeLookup = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                // Last entry wins on duplicate keys.
                if (!string.IsNullOrEmpty(entries[i].ActionKey))
                    runtimeLookup[entries[i].ActionKey] = i;
            }
        }
    }
}
