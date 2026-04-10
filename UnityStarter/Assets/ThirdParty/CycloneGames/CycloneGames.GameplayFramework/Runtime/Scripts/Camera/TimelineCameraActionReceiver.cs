using System;
using System.Collections.Generic;
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
    public class TimelineCameraActionReceiver : MonoBehaviour, INotificationReceiver
    {
        [Serializable]
        public struct SignalMapping
        {
            [Tooltip("Drag the SignalAsset that this Timeline marker emits.")]
            public ScriptableObject Signal;

            [Tooltip("The action key to look up in CameraActionBinding.")]
            public string ActionKey;

            [Tooltip("If true, StopAction is called; if false, PlayAction is called.")]
            public bool StopOnReceive;

            [Tooltip("Duration override in seconds. Non-positive = use entry default.")]
            public float DurationOverride;
        }

        [SerializeField] private CameraActionBinding actionBinding;
        [SerializeField] private List<SignalMapping> signalMappings = new List<SignalMapping>(8);

        private void Awake()
        {
            if (actionBinding == null)
                actionBinding = GetComponent<CameraActionBinding>();
        }

        // INotificationReceiver — called by PlayableDirector whenever a signal fires on any track
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (actionBinding == null) return;

            // SignalAsset is a ScriptableObject that implements INotification, so casting via
            // UnityEngine.Object lets us compare by asset reference without a hard dependency
            // on UnityEngine.Timeline.
            UnityEngine.Object notifObject = notification as UnityEngine.Object;
            if (notifObject == null) return;

            for (int i = 0; i < signalMappings.Count; i++)
            {
                SignalMapping mapping = signalMappings[i];
                if (mapping.Signal != notifObject) continue;
                if (string.IsNullOrEmpty(mapping.ActionKey)) continue;

                if (mapping.StopOnReceive)
                {
                    actionBinding.StopAction(mapping.ActionKey);
                }
                else
                {
                    float duration = mapping.DurationOverride > 0f ? mapping.DurationOverride : -1f;
                    actionBinding.PlayAction(mapping.ActionKey, duration);
                }
                break;
            }
        }
    }
}
