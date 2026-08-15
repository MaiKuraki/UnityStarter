using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Playables;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Receives Unity Timeline Signal notifications and routes them to CameraActionBinding.
    /// Works with any INotification emitter — the built-in SignalEmitter marker is the most common choice.
    ///
    /// Setup:
    ///   1. In your Timeline, add a Signal Track and place SignalEmitter markers at the desired times.
    ///   2. For each marker, assign (or create) a SignalAsset in the Inspector.
    ///   3. Add this component to the same GameObject as the PlayableDirector.
    ///   4. In this component's Inspector, map each SignalAsset to the desired CameraActionBinding key.
    ///
    /// No dependency on com.unity.timeline is required — the receiver uses UnityEngine.Playables
    /// which ships with com.unity.modules.director (always present in Unity 2019.1+).
    /// You only need a SignalAsset asset (right-click in Project > Create > Timeline > Signal) to emit.
    /// </summary>
    public sealed class TimelineCameraActionReceiver : MonoBehaviour, INotificationReceiver
    {
        public const int MaximumSignalMappingCount = 256;

        [Serializable]
        public struct SignalMapping
        {
            [Tooltip("Drag the SignalAsset that this Timeline marker emits.")]
            [SerializeField] private ScriptableObject signal;

            [Tooltip("The action key to look up in CameraActionBinding.")]
            [SerializeField] private string actionKey;

            [Tooltip("If true, StopAction is called; if false, PlayAction is called.")]
            [SerializeField] private bool stopOnReceive;

            [Tooltip("Duration override in seconds. Non-positive = use entry default.")]
            [SerializeField] private float durationOverride;

            public ScriptableObject Signal => signal;
            public string ActionKey => actionKey;
            public bool StopOnReceive => stopOnReceive;
            public float DurationOverride => durationOverride;

            public SignalMapping(
                ScriptableObject signal,
                string actionKey,
                bool stopOnReceive,
                float durationOverride)
            {
                ValidateValues(signal, actionKey, durationOverride, nameof(signal));
                this.signal = signal;
                this.actionKey = actionKey;
                this.stopOnReceive = stopOnReceive;
                this.durationOverride = durationOverride;
            }

            internal void Validate(int index)
            {
                ValidateValues(
                    signal,
                    actionKey,
                    durationOverride,
                    $"signalMappings[{index}]");
            }

            private static void ValidateValues(
                ScriptableObject signal,
                string actionKey,
                float durationOverride,
                string parameterName)
            {
                if (signal == null)
                {
                    throw new ArgumentNullException(
                        parameterName,
                        "Timeline signal mappings require a signal asset.");
                }
                if (!(signal is INotification))
                {
                    throw new ArgumentException(
                        "Timeline signal assets must implement INotification.",
                        parameterName);
                }
                if (string.IsNullOrWhiteSpace(actionKey))
                {
                    throw new ArgumentException(
                        "Timeline camera action keys must contain at least one non-whitespace character.",
                        parameterName);
                }
                if (float.IsNaN(durationOverride) || float.IsInfinity(durationOverride))
                {
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        "Timeline camera action duration overrides must be finite.");
                }
            }
        }

        [SerializeField] private CameraActionBinding actionBinding;
        [SerializeField] private List<SignalMapping> signalMappings = new List<SignalMapping>(8);
        private SignalMapping[] runtimeMappings;
        private Dictionary<int, int> signalLookup;
        private int ownerThreadId;
        private bool isInitialized;

        private void Awake()
        {
            BindOwnerThread();
            BuildRuntimeMappings(
                out SignalMapping[] localMappings,
                out Dictionary<int, int> localLookup);
            if (actionBinding == null)
            {
                actionBinding = GetComponent<CameraActionBinding>();
            }

            if (actionBinding == null)
            {
                throw new InvalidOperationException(
                    "TimelineCameraActionReceiver requires a CameraActionBinding.");
            }

            runtimeMappings = localMappings;
            signalLookup = localLookup;
            isInitialized = true;
        }

        // INotificationReceiver — called by PlayableDirector whenever a signal fires on any track
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            AssertReady();

            // SignalAsset is a ScriptableObject that implements INotification, so casting via
            // UnityEngine.Object lets us compare by asset reference without a hard dependency
            // on UnityEngine.Timeline.
            UnityEngine.Object notifObject = notification as UnityEngine.Object;
            if (notifObject == null) return;

            if (!signalLookup.TryGetValue(notifObject.GetInstanceID(), out int mappingIndex))
            {
                return;
            }

            SignalMapping mapping = runtimeMappings[mappingIndex];
            if (mapping.StopOnReceive)
            {
                actionBinding.StopAction(mapping.ActionKey);
            }
            else
            {
                float duration = mapping.DurationOverride > 0f ? mapping.DurationOverride : -1f;
                actionBinding.PlayAction(mapping.ActionKey, duration);
            }
        }

        private void BuildRuntimeMappings(
            out SignalMapping[] localMappings,
            out Dictionary<int, int> localLookup)
        {
            int mappingCount = signalMappings?.Count ?? 0;
            if (mappingCount > MaximumSignalMappingCount)
            {
                throw new InvalidOperationException(
                    $"TimelineCameraActionReceiver supports at most {MaximumSignalMappingCount} signal mappings.");
            }

            localMappings = new SignalMapping[mappingCount];
            localLookup = new Dictionary<int, int>(mappingCount);
            for (int index = 0; index < mappingCount; index++)
            {
                SignalMapping mapping = signalMappings[index];
                try
                {
                    mapping.Validate(index);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    throw new InvalidOperationException(
                        $"Timeline camera signal mapping {index} is invalid.",
                        exception);
                }

                int signalId = mapping.Signal.GetInstanceID();
                if (localLookup.ContainsKey(signalId))
                {
                    throw new InvalidOperationException(
                        $"TimelineCameraActionReceiver contains duplicate signal mapping at index {index}.");
                }

                localMappings[index] = mapping;
                localLookup.Add(signalId, index);
            }
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "TimelineCameraActionReceiver Unity lifecycle moved to a different owner thread.");
            }

            ownerThreadId = currentThreadId;
        }

        private void AssertOwnerThread()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0 ||
                Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "TimelineCameraActionReceiver live state must be accessed on its Awake owner thread.");
            }
        }

        private void AssertReady()
        {
            AssertOwnerThread();
            if (!isInitialized || runtimeMappings == null || signalLookup == null)
            {
                throw new InvalidOperationException(
                    "TimelineCameraActionReceiver live state is not available before Awake completes successfully.");
            }
        }
    }
}
