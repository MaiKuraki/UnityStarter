using CycloneGames.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    /// <summary>
    /// Explicit per-manager-type instance registry used to resolve the authoritative
    /// manager component without scene-wide scans (FindObjectsOfType and friends).
    ///
    /// Manager components register themselves in <see cref="MonoBehaviour.OnEnable"/> and
    /// unregister in <see cref="MonoBehaviour.OnDisable"/>, so the registry only contains
    /// live, enabled instances. <see cref="FindExisting{T}"/> picks a deterministic winner
    /// (activity rank, then instance id) and warns when duplicates coexist.
    ///
    /// Main-thread only: every call site is a Unity lifecycle callback or a manager's
    /// static Instance getter, both of which run on the Unity main thread. The registry
    /// is intentionally lock-free for that reason.
    /// </summary>
    internal static class BTManagerInstanceRegistry
    {
        private static readonly LogChannel Log = BehaviorTreeRuntimeLog.Channel;
        private const int InitialInstanceCapacity = 4;

        public static void Register<T>(T instance)
            where T : MonoBehaviour
        {
            if (instance == null) return;

            List<T> instances = Registry<T>.Instances;
            if (instances.Contains(instance)) return;

            instances.Add(instance);
        }

        public static void Unregister<T>(T instance)
            where T : MonoBehaviour
        {
            if (instance == null) return;

            Registry<T>.Instances.Remove(instance);
        }

        public static T FindExisting<T>(string managerName)
            where T : MonoBehaviour
        {
            List<T> instances = Registry<T>.Instances;
            T selected = null;
            int selectedRank = int.MaxValue;
            int selectedInstanceId = 0;
            int liveInstanceCount = 0;

            // Backward pass drops destroyed-but-not-yet-unregistered entries. Unity fake
            // null makes the managed wrapper compare equal to null after destruction.
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                T candidate = instances[i];
                if (candidate == null)
                {
                    instances.RemoveAt(i);
                    continue;
                }

                liveInstanceCount++;
                int candidateRank = GetActivityRank(candidate);
                int candidateInstanceId = candidate.GetInstanceID();
                if (selected == null ||
                    candidateRank < selectedRank ||
                    (candidateRank == selectedRank && candidateInstanceId < selectedInstanceId))
                {
                    selected = candidate;
                    selectedRank = candidateRank;
                    selectedInstanceId = candidateInstanceId;
                }
            }

            if (liveInstanceCount > 1 && selected != null)
            {
                Log.Warning(
                    $"[{managerName}] Found {liveInstanceCount} registered live instances. " +
                    $"'{selected.gameObject.name}' was selected deterministically; duplicate components remove themselves in Awake.");
            }

            return selected;
        }

        private static int GetActivityRank(MonoBehaviour candidate)
        {
            if (candidate.isActiveAndEnabled)
            {
                return 0;
            }

            return candidate.enabled ? 1 : 2;
        }

        // One storage instance per manager component type. Reset on play-mode start so
        // both domain-reload and disabled-domain-reload (Enter Play Mode Options) flows
        // start from an empty registry, mirroring the managers' ResetStaticState pattern.
        private static class Registry<T>
            where T : MonoBehaviour
        {
            internal static readonly List<T> Instances = new List<T>(InitialInstanceCapacity);

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void Reset()
            {
                Instances.Clear();
            }
        }
    }
}
