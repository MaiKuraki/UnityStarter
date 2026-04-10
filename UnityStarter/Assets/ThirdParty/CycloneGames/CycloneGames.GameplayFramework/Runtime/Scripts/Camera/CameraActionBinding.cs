using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Animation-system-agnostic bridge for triggering camera action presets.
    /// Can be called from Animator events, Animancer events, Timeline signals, or custom gameplay code.
    /// </summary>
    public class CameraActionBinding : MonoBehaviour
    {
        public enum TriggerPolicy
        {
            ReplaceSameKey,
            IgnoreIfRunning,
            Stack
        }

        [Serializable]
        public struct CameraActionEntry
        {
            public string ActionKey;
            public CameraActionPreset Preset;
            public TriggerPolicy Policy;
            public bool AutoRemoveOnFinish;
            public float DurationOverride;
        }

        // Struct (not class) — stored inline in the List<> array, zero per-entry heap allocation.
        private struct ActiveAction
        {
            public string Key;
            public PresetCameraMode Mode;
            public bool AutoRemove;
        }

        [SerializeField] private PlayerController playerController;
        [SerializeField] private bool autoResolvePlayerController = true;

        [Tooltip("Shared action map asset. Per-component inline entries override map entries of the same key.")]
        [SerializeField] private CameraActionMap actionMap;

        [Tooltip("Instance-level entries that override the shared action map.")]
        [SerializeField] private List<CameraActionEntry> actionEntries = new List<CameraActionEntry>(8);

        private readonly List<ActiveAction> activeActions = new List<ActiveAction>(8);

        // Pool avoids one heap allocation per PlayPreset call.
        // Capacity 8 covers even the most action-dense frames without resizing.
        private readonly Stack<PresetCameraMode> modePool = new Stack<PresetCameraMode>(8);

        private void Awake()
        {
            TryResolvePlayerController();
        }

        private void OnDisable()
        {
            // Fired on SetActive(false) and object-pool return — stop all actions so
            // no orphaned CameraModes linger on the PlayerController's camera stack.
            StopAllActions();
        }

        private void OnDestroy()
        {
            // OnDisable fires before OnDestroy, so StopAllActions() has already run.
            // This call is a safety net for the hypothetical case where OnDisable is
            // suppressed (e.g., destroyed without going through the disable path in edit-mode).
            StopAllActions();
        }

        private void LateUpdate()
        {
            if (activeActions.Count == 0) return;
            if (!TryResolvePlayerController()) return;

            for (int i = activeActions.Count - 1; i >= 0; i--)
            {
                ActiveAction action = activeActions[i];
                if (!action.AutoRemove || action.Mode == null || !action.Mode.IsFinished) continue;

                playerController.RemoveCameraMode(action.Mode);
                ReturnMode(action.Mode);
                activeActions.RemoveAt(i);
            }
        }

        public bool PlayAction(string actionKey)
        {
            return PlayAction(actionKey, -1f);
        }

        public bool PlayAction(string actionKey, float durationOverride)
        {
            // 1. Inline entries take priority (per-component override)
            int index = FindActionEntryIndex(actionKey);
            if (index >= 0)
            {
                CameraActionEntry entry = actionEntries[index];
                float resolvedDuration = durationOverride > 0f
                    ? durationOverride
                    : (entry.DurationOverride > 0f ? entry.DurationOverride : -1f);
                return PlayPreset(entry.ActionKey, entry.Preset, resolvedDuration, entry.Policy, entry.AutoRemoveOnFinish);
            }

            // 2. Fall back to shared action map
            if (actionMap != null && actionMap.TryGetEntry(actionKey, out CameraActionMap.Entry mapEntry))
            {
                float resolvedDuration = durationOverride > 0f
                    ? durationOverride
                    : (mapEntry.DurationOverride > 0f ? mapEntry.DurationOverride : -1f);
                return PlayPreset(mapEntry.ActionKey, mapEntry.Preset, resolvedDuration, mapEntry.Policy, mapEntry.AutoRemoveOnFinish);
            }

            return false;
        }

        public bool PlayPreset(string actionKey, CameraActionPreset preset, float overrideDuration = -1f,
            TriggerPolicy policy = TriggerPolicy.ReplaceSameKey, bool autoRemoveOnFinish = true)
        {
            if (preset == null) return false;
            if (!TryResolvePlayerController()) return false;

            if (policy == TriggerPolicy.IgnoreIfRunning && IsActionRunning(actionKey))
            {
                return false;
            }

            if (policy == TriggerPolicy.ReplaceSameKey)
            {
                StopAction(actionKey);
            }

            PresetCameraMode mode = RentMode();
            mode.Setup(preset, overrideDuration);
            playerController.PushCameraMode(mode);

            activeActions.Add(new ActiveAction
            {
                Key = actionKey,
                Mode = mode,
                AutoRemove = autoRemoveOnFinish
            });

            return true;
        }

        public bool StopAction(string actionKey)
        {
            if (!TryResolvePlayerController()) return false;

            bool removedAny = false;
            for (int i = activeActions.Count - 1; i >= 0; i--)
            {
                ActiveAction action = activeActions[i];
                if (!string.Equals(action.Key, actionKey, StringComparison.Ordinal)) continue;

                if (action.Mode != null)
                {
                    playerController.RemoveCameraMode(action.Mode);
                    ReturnMode(action.Mode);
                }
                activeActions.RemoveAt(i);
                removedAny = true;
            }

            return removedAny;
        }

        public void StopAllActions()
        {
            if (!TryResolvePlayerController())
            {
                activeActions.Clear();
                return;
            }

            for (int i = activeActions.Count - 1; i >= 0; i--)
            {
                ActiveAction action = activeActions[i];
                if (action.Mode != null)
                {
                    playerController.RemoveCameraMode(action.Mode);
                    ReturnMode(action.Mode);
                }
            }

            activeActions.Clear();
        }

        public bool IsActionRunning(string actionKey)
        {
            for (int i = 0; i < activeActions.Count; i++)
            {
                if (string.Equals(activeActions[i].Key, actionKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ── Object pool ──────────────────────────────────────────────────────
        // IMPORTANT: RemoveCameraMode is assumed to be synchronous. Do not call
        // ReturnMode until after RemoveCameraMode to avoid use-after-return.
        // Note: Re-entrant PlayAction from within RemoveCameraMode callbacks is
        // safe because backward iteration is not invalidated by tail Adds.

        private PresetCameraMode RentMode()
        {
            return modePool.Count > 0 ? modePool.Pop() : new PresetCameraMode();
        }

        private void ReturnMode(PresetCameraMode mode)
        {
            if (mode == null) return;
            // Clear preset reference so the pooled object does not keep a GC root.
            mode.Setup(null, -1f);
            modePool.Push(mode);
        }

        private int FindActionEntryIndex(string actionKey)
        {
            for (int i = 0; i < actionEntries.Count; i++)
            {
                if (string.Equals(actionEntries[i].ActionKey, actionKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryResolvePlayerController()
        {
            if (playerController != null) return true;
            if (!autoResolvePlayerController) return false;

            playerController = GetComponent<PlayerController>();
            if (playerController != null) return true;

            Actor ownerActor = GetComponent<Actor>();
            if (ownerActor != null)
            {
                playerController = ownerActor.GetOwner<PlayerController>();
            }

            return playerController != null;
        }
    }
}
