using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Animation-system-agnostic bridge for triggering camera action presets.
    /// Can be called from Animator events, Animancer events, Timeline signals, or custom gameplay code.
    /// </summary>
    public sealed class CameraActionBinding : MonoBehaviour
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        public const int MaximumActiveActionCount = 64;
        public const int MaximumPooledModeCount = 64;
        public const int MaximumInlineActionEntryCount = 256;

        public enum TriggerPolicy
        {
            ReplaceSameKey,
            IgnoreIfRunning,
            Stack
        }

        [Serializable]
        public struct CameraActionEntry
        {
            [SerializeField] private string actionKey;
            [SerializeField] private CameraActionPreset preset;
            [SerializeField] private TriggerPolicy policy;
            [SerializeField] private bool autoRemoveOnFinish;
            [SerializeField] private float durationOverride;

            public string ActionKey => actionKey;
            public CameraActionPreset Preset => preset;
            public TriggerPolicy Policy => policy;
            public bool AutoRemoveOnFinish => autoRemoveOnFinish;
            public float DurationOverride => durationOverride;

            public CameraActionEntry(
                string actionKey,
                CameraActionPreset preset,
                TriggerPolicy policy,
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
                    $"actionEntries[{index}]");
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

        private readonly struct ActiveAction
        {
            public string Key { get; }
            public PresetCameraMode Mode { get; }
            public CameraContext Context { get; }
            public bool AutoRemove { get; }

            public ActiveAction(
                string key,
                PresetCameraMode mode,
                CameraContext context,
                bool autoRemove)
            {
                Key = key;
                Mode = mode;
                Context = context;
                AutoRemove = autoRemove;
            }
        }

        [SerializeField] private PlayerController playerController;
        [SerializeField] private bool autoResolvePlayerController = true;

        [Tooltip("Shared action map asset. Per-component inline entries override map entries of the same key.")]
        [SerializeField] private CameraActionMap actionMap;

        [Tooltip("Instance-level entries that override the shared action map.")]
        [SerializeField] private List<CameraActionEntry> actionEntries = new List<CameraActionEntry>(8);

        [Tooltip("Maximum number of camera actions this binding can track at the same time.")]
        [SerializeField, Range(0, MaximumActiveActionCount)]
        private int maxActiveActions = 8;

        [Tooltip("Maximum number of inactive preset modes retained by this binding.")]
        [SerializeField, Range(0, MaximumPooledModeCount)]
        private int maxPooledModes = 8;

        private List<ActiveAction> activeActions;
        private Stack<PresetCameraMode> modePool;
        private Dictionary<string, int> actionEntryLookup;
        private int runtimeMaxActiveActions;
        private int runtimeMaxPooledModes;
        private int ownerThreadId;
        private bool ownerThreadBound;
        private bool isInitialized;
        private bool isStoppingAllActions;

        public int ActiveActionCount
        {
            get
            {
                EnsureReady();
                return activeActions.Count;
            }
        }

        public int PooledModeCount
        {
            get
            {
                EnsureReady();
                return modePool.Count;
            }
        }

        public int MaxActiveActions
        {
            get
            {
                EnsureReady();
                return runtimeMaxActiveActions;
            }
        }

        public int MaxPooledModes
        {
            get
            {
                EnsureReady();
                return runtimeMaxPooledModes;
            }
        }

        private void Awake()
        {
            BindOwnerThread();
            int activeCapacity = ValidateBudget(
                maxActiveActions,
                MaximumActiveActionCount,
                nameof(maxActiveActions));
            int poolCapacity = ValidateBudget(
                maxPooledModes,
                MaximumPooledModeCount,
                nameof(maxPooledModes));
            Dictionary<string, int> localActionEntryLookup =
                BuildActionEntryLookup();
            actionMap?.Warmup();
            TryResolvePlayerController();

            var localActiveActions = new List<ActiveAction>(activeCapacity);
            var localModePool = new Stack<PresetCameraMode>(poolCapacity);

            activeActions = localActiveActions;
            modePool = localModePool;
            actionEntryLookup = localActionEntryLookup;
            runtimeMaxActiveActions = activeCapacity;
            runtimeMaxPooledModes = poolCapacity;
            isInitialized = true;
        }

        private void OnEnable()
        {
            if (isInitialized)
            {
                EnsureOwnerThread();
            }
        }

        private void OnDisable()
        {
            // Fired on SetActive(false) and object-pool return. Stop all actions so
            // no orphaned CameraModes linger on the PlayerController's camera stack.
            if (isInitialized)
            {
                StopAllActions();
            }
        }

        private void OnDestroy()
        {
            if (!isInitialized)
            {
                return;
            }

            EnsureOwnerThread();
            TransferPendingCleanupOwnership();
            activeActions.Clear();
            modePool.Clear();
            isInitialized = false;
        }

        private void LateUpdate()
        {
            EnsureReady();
            if (activeActions.Count == 0) return;

            for (int i = activeActions.Count - 1; i >= 0; i--)
            {
                ActiveAction action = activeActions[i];
                if (action.Mode == null ||
                    action.Context == null ||
                    !action.Context.ContainsCameraMode(action.Mode))
                {
                    activeActions.RemoveAt(i);
                    ReturnMode(action.Mode);
                    continue;
                }

                if (!action.AutoRemove || !action.Mode.IsFinished) continue;

                if (TryReleaseAction(action, out bool releaseCompleted) && releaseCompleted)
                {
                    RemoveActiveAction(action.Mode);
                }
            }
        }

        public bool PlayAction(string actionKey)
        {
            EnsureReady();
            return PlayAction(actionKey, -1f);
        }

        public bool PlayAction(string actionKey, float durationOverride)
        {
            EnsureReady();
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
            EnsureReady();
            if (string.IsNullOrEmpty(actionKey) || preset == null || isStoppingAllActions) return false;
            if (float.IsNaN(overrideDuration) || float.IsInfinity(overrideDuration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(overrideDuration),
                    "Camera action duration overrides must be finite.");
            }
            if (!TryResolvePlayerController()) return false;

            if (policy == TriggerPolicy.IgnoreIfRunning && IsActionRunning(actionKey))
            {
                return false;
            }

            if (policy == TriggerPolicy.ReplaceSameKey)
            {
                StopAction(actionKey);
            }

            if (activeActions.Count >= runtimeMaxActiveActions)
            {
                return false;
            }

            PresetCameraMode mode = RentMode();
            mode.Setup(preset, overrideDuration);
            PlayerController actionOwner = playerController;
            CameraContext actionContext = actionOwner.GetCameraContext();
            activeActions.Add(new ActiveAction(
                actionKey,
                mode,
                actionContext,
                autoRemoveOnFinish));

            bool pushed;
            try
            {
                pushed = actionOwner.TryPushCameraMode(mode);
            }
            catch
            {
                if (!actionContext.ContainsCameraMode(mode))
                {
                    RemoveActiveAction(mode);
                    ReturnMode(mode);
                }

                throw;
            }

            if (!pushed)
            {
                if (!actionContext.ContainsCameraMode(mode))
                {
                    RemoveActiveAction(mode);
                    ReturnMode(mode);
                }

                return false;
            }

            return true;
        }

        public bool StopAction(string actionKey)
        {
            EnsureReady();
            if (string.IsNullOrEmpty(actionKey)) return false;

            bool acceptedAny = false;
            for (int i = activeActions.Count - 1; i >= 0; i--)
            {
                ActiveAction action = activeActions[i];
                if (!string.Equals(action.Key, actionKey, StringComparison.Ordinal)) continue;

                if (!TryReleaseAction(action, out bool releaseCompleted))
                {
                    continue;
                }

                acceptedAny = true;
                if (releaseCompleted)
                {
                    RemoveActiveAction(action.Mode);
                }
            }

            return acceptedAny;
        }

        public void StopAllActions()
        {
            EnsureReady();
            if (isStoppingAllActions) return;

            isStoppingAllActions = true;
            try
            {
                for (int i = activeActions.Count - 1; i >= 0; i--)
                {
                    ActiveAction action = activeActions[i];
                    if (TryReleaseAction(action, out bool releaseCompleted) && releaseCompleted)
                    {
                        RemoveActiveAction(action.Mode);
                    }
                }
            }
            finally
            {
                isStoppingAllActions = false;
            }
        }

        public bool IsActionRunning(string actionKey)
        {
            EnsureReady();
            if (string.IsNullOrEmpty(actionKey)) return false;

            for (int i = 0; i < activeActions.Count; i++)
            {
                if (string.Equals(activeActions[i].Key, actionKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Object pool
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
            if (isInitialized && modePool.Count < runtimeMaxPooledModes)
            {
                modePool.Push(mode);
            }
        }

        private bool TryReleaseAction(in ActiveAction action, out bool releaseCompleted)
        {
            releaseCompleted = false;
            if (action.Mode == null)
            {
                releaseCompleted = true;
                return true;
            }

            CameraContext context = action.Context;
            if (context != null)
            {
                bool removalAccepted = context.RemoveCameraMode(action.Mode);
                if (context.ContainsCameraMode(action.Mode))
                {
                    return removalAccepted;
                }
            }

            ReturnMode(action.Mode);
            releaseCompleted = true;
            return true;
        }

        private void TransferPendingCleanupOwnership()
        {
            int actionCount = activeActions.Count;
            for (int index = 0; index < actionCount; index++)
            {
                ActiveAction action = activeActions[index];
                if (action.Mode == null)
                {
                    continue;
                }

                CameraContext context = action.Context;
                if (context == null || !context.TryAdoptModeCleanup(action.Mode))
                {
                    Log.Error(
                        $"CameraActionBinding could not transfer pending cleanup for camera mode '{action.Mode.GetType().Name}' to its CameraContext during destruction.");
                }
            }
        }

        private bool RemoveActiveAction(PresetCameraMode mode)
        {
            for (int index = activeActions.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(activeActions[index].Mode, mode))
                {
                    continue;
                }

                activeActions.RemoveAt(index);
                return true;
            }

            return false;
        }

        private int FindActionEntryIndex(string actionKey)
        {
            return actionKey != null && actionEntryLookup.TryGetValue(actionKey, out int index)
                ? index
                : -1;
        }

        private Dictionary<string, int> BuildActionEntryLookup()
        {
            int entryCount = actionEntries?.Count ?? 0;
            if (entryCount > MaximumInlineActionEntryCount)
            {
                throw new InvalidOperationException(
                    $"CameraActionBinding supports at most {MaximumInlineActionEntryCount} inline entries.");
            }

            var localLookup = new Dictionary<string, int>(
                entryCount,
                StringComparer.Ordinal);
            for (int index = 0; index < entryCount; index++)
            {
                CameraActionEntry entry = actionEntries[index];
                try
                {
                    entry.Validate(index);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    throw new InvalidOperationException(
                        $"CameraActionBinding inline entry {index} is invalid.",
                        exception);
                }

                if (localLookup.ContainsKey(entry.ActionKey))
                {
                    throw new InvalidOperationException(
                        $"CameraActionBinding contains duplicate inline key '{entry.ActionKey}'.");
                }

                localLookup.Add(entry.ActionKey, index);
            }

            return localLookup;
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

        private void EnsureOwnerThread()
        {
            if (!ownerThreadBound || Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "CameraActionBinding live state must be accessed on its Unity lifecycle owner thread.");
            }
        }

        private void EnsureReady()
        {
            EnsureOwnerThread();
            if (!isInitialized ||
                activeActions == null ||
                modePool == null ||
                actionEntryLookup == null)
            {
                throw new InvalidOperationException(
                    "CameraActionBinding live state is not available before Awake completes successfully.");
            }
        }

        private static int ValidateBudget(int value, int hardCeiling, string fieldName)
        {
            if (value < 0 || value > hardCeiling)
            {
                throw new InvalidOperationException(
                    $"CameraActionBinding {fieldName} must be between 0 and {hardCeiling}.");
            }

            return value;
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadBound && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "CameraActionBinding Unity lifecycle moved to a different owner thread.");
            }

            ownerThreadId = currentThreadId;
            ownerThreadBound = true;
        }
    }
}
