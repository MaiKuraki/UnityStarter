using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    internal static class BTManagerSceneResolver
    {
        public static T FindExisting<T>(string managerName)
            where T : MonoBehaviour
        {
            T[] candidates = Object.FindObjectsOfType<T>(true);
            T selected = null;
            int selectedRank = int.MaxValue;
            int sceneCandidateCount = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                T candidate = candidates[i];
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                {
                    continue;
                }

                sceneCandidateCount++;
                int candidateRank = GetActivityRank(candidate);
                if (selected == null ||
                    candidateRank < selectedRank ||
                    (candidateRank == selectedRank && candidate.GetInstanceID() < selected.GetInstanceID()))
                {
                    selected = candidate;
                    selectedRank = candidateRank;
                }
            }

            if (sceneCandidateCount > 1 && selected != null)
            {
                Debug.LogWarning(
                    $"[{managerName}] Found {sceneCandidateCount} loaded scene instances. " +
                    $"'{selected.gameObject.name}' was selected deterministically; duplicate components will be removed when they awaken.",
                    selected);
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
    }
}
