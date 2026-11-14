using CycloneGames.BehaviorTree.Core;
using CycloneGames.Logging;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.Compat;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    /// <summary>
    /// Per-manager-type registry that resolves the authoritative manager without scene-wide
    /// scans. Managers register in OnEnable and unregister in OnDisable, so the registry holds
    /// only live, enabled instances. FindExisting{T} picks a deterministic winner (activity
    /// rank, then instance id). All call sites run on the Unity main thread; the registry is
    /// lock-free.
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
            ulong selectedInstanceId = 0UL;
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
                ulong candidateInstanceId = UnityObjectId.Of(candidate);
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

        // RuntimeInitializeOnLoadMethod cannot be declared on a generic type, so every closed
        // Registry<T> registers its own reset delegate and the non-generic entry point below runs them.
        private static readonly List<System.Action> ResetActions = new List<System.Action>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistries()
        {
            for (int i = 0; i < ResetActions.Count; i++)
            {
                ResetActions[i]();
            }
        }

        // One storage instance per manager type. Reset on play-mode start so domain-reload and
        // disabled-domain-reload flows start from an empty registry.
        private static class Registry<T>
            where T : MonoBehaviour
        {
            internal static readonly List<T> Instances = new List<T>(InitialInstanceCapacity);

            static Registry()
            {
                ResetActions.Add(Instances.Clear);
            }
        }
    }
}
