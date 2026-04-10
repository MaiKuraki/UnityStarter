#if ANIMANCER_PRESENT
using System;
using System.Collections.Generic;
using Animancer;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Animancer
{
    /// <summary>
    /// Optional bridge that maps Animancer named events to CameraActionBinding keys.
    /// This keeps camera triggering animation-system agnostic while allowing direct Animancer wiring.
    /// </summary>
    public class AnimancerCameraActionBridge : MonoBehaviour
    {
        [Serializable]
        public struct CameraActionCommand
        {
            public string ActionKey;
            public bool StopAction;
            public float DurationOverride;

            [Tooltip("Optional. If set, this command runs only when that action is running (or not running if InvertRequirement is true).")]
            public string RequireActionRunningKey;
            public bool InvertRequirement;
        }

        [Serializable]
        public struct EventToAction
        {
            public string EventName;

            [Tooltip("Optional. Filter by current state's key string containing this substring.")]
            public string RequiredCurrentStateKeyContains;
            public bool InvertCurrentStateKeyFilter;

            [Tooltip("Animancer layer used by RequiredCurrentStateKeyContains. Default 0.")]
            public int LayerIndex;

            [Tooltip("Minimum interval between two triggers of the same mapping. <=0 means no throttle.")]
            public float MinTriggerInterval;

            [Tooltip("Primary action command (for backward compatibility with existing setup).")]
            public string ActionKey;
            public bool StopAction;
            public float DurationOverride;

            [Tooltip("Optional additional commands to execute when this event fires.")]
            public List<CameraActionCommand> AdditionalCommands;
        }

        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private CameraActionBinding actionBinding;
        [SerializeField] private List<EventToAction> mappings = new List<EventToAction>(8);

        private readonly List<Action> registeredCallbacks = new List<Action>(8);
        private readonly List<float> lastTriggerTimes = new List<float>(8);

        private void Awake()
        {
            if (animancer == null)
                animancer = GetComponent<AnimancerComponent>();

            if (actionBinding == null)
                actionBinding = GetComponent<CameraActionBinding>();

            // Pre-create delegates once here instead of on every OnEnable.
            // Each lambda captures a different 'index' so they must be separate objects,
            // but creating them once eliminates the GC pressure on every enable cycle.
            for (int i = 0; i < mappings.Count; i++)
            {
                EventToAction mapping = mappings[i];
                bool hasPrimary = !string.IsNullOrEmpty(mapping.ActionKey);
                bool hasAdditional = mapping.AdditionalCommands != null && mapping.AdditionalCommands.Count > 0;
                bool hasAnyCommand = hasPrimary || hasAdditional;

                if (string.IsNullOrEmpty(mapping.EventName) || !hasAnyCommand)
                {
                    registeredCallbacks.Add(null);
                    lastTriggerTimes.Add(float.NegativeInfinity);
                    continue;
                }

                int index = i;
                registeredCallbacks.Add(() => OnAnimancerEvent(index));
                lastTriggerTimes.Add(float.NegativeInfinity);
            }
        }

        private void OnEnable()
        {
            RegisterCallbacks();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void RegisterCallbacks()
        {
            // Callbacks are pre-created in Awake; just re-subscribe them here.
            if (animancer == null || actionBinding == null) return;
            int count = Mathf.Min(registeredCallbacks.Count, mappings.Count);
            for (int i = 0; i < count; i++)
            {
                Action callback = registeredCallbacks[i];
                if (callback == null) continue;
                string eventName = mappings[i].EventName;
                if (!string.IsNullOrEmpty(eventName))
                    animancer.Events.AddTo(eventName, callback);
            }
        }

        private void UnregisterCallbacks()
        {
            // Do NOT Clear() — the pre-created callbacks must survive for re-use on next OnEnable.
            if (animancer == null || registeredCallbacks.Count == 0) return;
            int count = Mathf.Min(registeredCallbacks.Count, mappings.Count);
            for (int i = 0; i < count; i++)
            {
                Action callback = registeredCallbacks[i];
                if (callback == null) continue;
                string eventName = mappings[i].EventName;
                if (!string.IsNullOrEmpty(eventName))
                    animancer.Events.Remove(eventName, callback);
            }
        }

        private void OnAnimancerEvent(int index)
        {
            if (actionBinding == null) return;
            if (index < 0 || index >= mappings.Count) return;

            EventToAction mapping = mappings[index];

            if (!PassTriggerInterval(index, mapping)) return;
            if (!PassCurrentStateFilter(mapping)) return;

            ExecuteCommand(mapping.ActionKey, mapping.StopAction, mapping.DurationOverride, null, false);

            if (mapping.AdditionalCommands == null || mapping.AdditionalCommands.Count == 0) return;
            for (int i = 0; i < mapping.AdditionalCommands.Count; i++)
            {
                CameraActionCommand cmd = mapping.AdditionalCommands[i];
                ExecuteCommand(cmd.ActionKey, cmd.StopAction, cmd.DurationOverride, cmd.RequireActionRunningKey, cmd.InvertRequirement);
            }
        }

        private void ExecuteCommand(string actionKey, bool stopAction, float durationOverride, string requireActionRunningKey, bool invertRequirement)
        {
            if (string.IsNullOrEmpty(actionKey)) return;

            if (!string.IsNullOrEmpty(requireActionRunningKey))
            {
                bool running = actionBinding.IsActionRunning(requireActionRunningKey);
                if (invertRequirement ? running : !running)
                {
                    return;
                }
            }

            if (stopAction)
            {
                actionBinding.StopAction(actionKey);
            }
            else
            {
                actionBinding.PlayAction(actionKey, durationOverride);
            }
        }

        private bool PassTriggerInterval(int index, EventToAction mapping)
        {
            if (mapping.MinTriggerInterval <= 0f) return true;
            if (index < 0 || index >= lastTriggerTimes.Count) return true;

            float now = Time.unscaledTime;
            float last = lastTriggerTimes[index];
            if (now - last < mapping.MinTriggerInterval)
            {
                return false;
            }

            lastTriggerTimes[index] = now;
            return true;
        }

        private bool PassCurrentStateFilter(EventToAction mapping)
        {
            if (string.IsNullOrEmpty(mapping.RequiredCurrentStateKeyContains)) return true;
            if (animancer == null) return false;

            int layerIndex = mapping.LayerIndex < 0 ? 0 : mapping.LayerIndex;
            if (layerIndex >= animancer.Layers.Count) return false;

            AnimancerState current = animancer.Layers[layerIndex].CurrentState;
            object currentKeyObj = current != null ? current.Key : null;
            string currentKey = currentKeyObj as string;
            if (currentKey == null && currentKeyObj != null)
            {
                currentKey = currentKeyObj.ToString();
            }

            bool matched = !string.IsNullOrEmpty(currentKey)
                           && currentKey.IndexOf(mapping.RequiredCurrentStateKeyContains, StringComparison.Ordinal) >= 0;

            return mapping.InvertCurrentStateKeyFilter ? !matched : matched;
        }
    }
}
#endif
