using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Shareable authoring asset that publishes an immutable, owner-thread-bound runtime lookup.
    /// Call Warmup during owned startup before reading entries.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CameraActionMap",
        menuName = "CycloneGames/GameplayFramework/Camera/CameraActionMap")]
    public sealed class CameraActionMap : ScriptableObject
    {
        public const int MaximumEntryCount = 256;

        [Serializable]
        public struct Entry
        {
            [Tooltip("Unique identifier used to look up this entry from any animation system.")]
            [SerializeField] private string actionKey;

            [Tooltip("The camera preset to activate when this action is triggered.")]
            [SerializeField] private CameraActionPreset preset;

            [Tooltip("How to handle re-triggering while an action with the same key is already running.")]
            [SerializeField] private CameraActionBinding.TriggerPolicy policy;

            [Tooltip("Automatically remove the camera mode when the preset duration elapses.")]
            [SerializeField] private bool autoRemoveOnFinish;

            [Tooltip("Duration override in seconds. Non-positive uses the preset duration.")]
            [SerializeField] private float durationOverride;

            public string ActionKey => actionKey;
            public CameraActionPreset Preset => preset;
            public CameraActionBinding.TriggerPolicy Policy => policy;
            public bool AutoRemoveOnFinish => autoRemoveOnFinish;
            public float DurationOverride => durationOverride;

            public Entry(
                string actionKey,
                CameraActionPreset preset,
                CameraActionBinding.TriggerPolicy policy,
                bool autoRemoveOnFinish,
                float durationOverride)
            {
                ValidateValues(actionKey, preset, durationOverride, nameof(actionKey));
                this.actionKey = actionKey;
                this.preset = preset;
                this.policy = policy;
                this.autoRemoveOnFinish = autoRemoveOnFinish;
                this.durationOverride = durationOverride;
            }

            internal void Validate(int index)
            {
                ValidateValues(
                    actionKey,
                    preset,
                    durationOverride,
                    $"entries[{index}]");
            }

            private static void ValidateValues(
                string actionKey,
                CameraActionPreset preset,
                float durationOverride,
                string parameterName)
            {
                if (string.IsNullOrWhiteSpace(actionKey))
                {
                    throw new ArgumentException(
                        "Camera action keys must contain at least one non-whitespace character.",
                        parameterName);
                }

                if (preset == null)
                {
                    throw new ArgumentNullException(
                        parameterName,
                        "Camera action entries require a preset.");
                }

                if (float.IsNaN(durationOverride) || float.IsInfinity(durationOverride))
                {
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        "Camera action duration overrides must be finite.");
                }
            }
        }

        [SerializeField] private List<Entry> entries = new List<Entry>(8);

        private Entry[] runtimeEntries;
        private Dictionary<string, int> runtimeLookup;
        private int ownerThreadId;

        public int EntryCount
        {
            get
            {
                AssertRuntimeSnapshotReady();
                return runtimeEntries.Length;
            }
        }

        private void OnEnable()
        {
            InvalidateRuntimeSnapshot();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateRuntimeSnapshot();
        }
#endif

        /// <summary>
        /// Builds and publishes the complete runtime snapshot on the calling owner thread.
        /// </summary>
        public void Warmup()
        {
            BindOwnerThread();
            if (runtimeLookup != null)
            {
                return;
            }

            if (entries == null)
            {
                throw new InvalidOperationException(
                    "CameraActionMap authoring entries are unavailable.");
            }
            if (entries.Count > MaximumEntryCount)
            {
                throw new InvalidOperationException(
                    $"CameraActionMap supports at most {MaximumEntryCount} entries.");
            }

            int count = entries.Count;
            var localEntries = new Entry[count];
            var localLookup = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                Entry entry = entries[index];
                try
                {
                    entry.Validate(index);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    throw new InvalidOperationException(
                        $"CameraActionMap entry {index} is invalid.",
                        exception);
                }

                if (localLookup.ContainsKey(entry.ActionKey))
                {
                    throw new InvalidOperationException(
                        $"CameraActionMap contains duplicate key '{entry.ActionKey}'.");
                }

                localEntries[index] = entry;
                localLookup.Add(entry.ActionKey, index);
            }

            // Publish only after every validation and insertion completed successfully.
            runtimeEntries = localEntries;
            runtimeLookup = localLookup;
        }

        public bool TryGetEntry(string key, out Entry entry)
        {
            AssertRuntimeSnapshotReady();
            if (string.IsNullOrEmpty(key) ||
                !runtimeLookup.TryGetValue(key, out int index))
            {
                entry = default;
                return false;
            }

            entry = runtimeEntries[index];
            return true;
        }

        public Entry GetEntry(int index)
        {
            AssertRuntimeSnapshotReady();
            if ((uint)index >= (uint)runtimeEntries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return runtimeEntries[index];
        }

        private void InvalidateRuntimeSnapshot()
        {
            runtimeEntries = null;
            runtimeLookup = null;
            ownerThreadId = 0;
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "CameraActionMap runtime ownership cannot move to another thread.");
            }

            ownerThreadId = currentThreadId;
        }

        private void AssertRuntimeSnapshotReady()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0 ||
                Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "CameraActionMap runtime state must be accessed on its Warmup owner thread.");
            }
            if (runtimeEntries == null || runtimeLookup == null)
            {
                throw new InvalidOperationException(
                    "CameraActionMap requires Warmup after load or authoring invalidation.");
            }
        }
    }
}
