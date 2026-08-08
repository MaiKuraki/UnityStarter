using System;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private BuildRecipeAuthoringGraphSnapshot recipeGraphSnapshot;

        private void InvalidateRecipeGraphSnapshot()
        {
            recipeGraphSnapshot = null;
        }

        private void RefreshRecipeGraphSnapshotValidity()
        {
            if (TryGetRecipeBudgetViolation(out _))
            {
                InvalidateRecipeGraphSnapshot();
                return;
            }

            BuildRecipeAuthoringGraphSnapshot snapshot = recipeGraphSnapshot;
            if (snapshot == null)
            {
                return;
            }

            int invocationCount = recipeInvocations?.arraySize ?? 0;
            if (snapshot.InvocationCount != invocationCount)
            {
                InvalidateRecipeGraphSnapshot();
                return;
            }

            for (int invocationIndex = 0;
                 invocationIndex < invocationCount;
                 invocationIndex++)
            {
                SerializedProperty invocation =
                    recipeInvocations.GetArrayElementAtIndex(invocationIndex);
                SerializedProperty dependencies =
                    invocation.FindPropertyRelative("dependencies");
                int dependencyCount = dependencies?.arraySize ?? 0;
                if (!snapshot.MatchesInvocation(
                        invocationIndex,
                        invocation.FindPropertyRelative("invocationId").stringValue,
                        invocation.FindPropertyRelative("stepTypeId").stringValue,
                        dependencyCount))
                {
                    InvalidateRecipeGraphSnapshot();
                    return;
                }

                for (int dependencyIndex = 0;
                     dependencyIndex < dependencyCount;
                     dependencyIndex++)
                {
                    string dependencyId = dependencies
                        .GetArrayElementAtIndex(dependencyIndex)
                        .FindPropertyRelative("invocationId")
                        .stringValue;
                    if (!snapshot.MatchesDependency(
                            invocationIndex,
                            dependencyIndex,
                            dependencyId))
                    {
                        InvalidateRecipeGraphSnapshot();
                        return;
                    }
                }
            }
        }

        private BuildRecipeAuthoringGraphSnapshot GetRecipeGraphSnapshot()
        {
            if (TryGetRecipeBudgetViolation(out string violation))
            {
                throw new InvalidOperationException(violation);
            }

            if (recipeGraphSnapshot != null)
            {
                return recipeGraphSnapshot;
            }

            int invocationCount = recipeInvocations?.arraySize ?? 0;
            var nodes = new BuildRecipeAuthoringGraphNode[invocationCount];
            for (int invocationIndex = 0;
                 invocationIndex < invocationCount;
                 invocationIndex++)
            {
                SerializedProperty invocation =
                    recipeInvocations.GetArrayElementAtIndex(invocationIndex);
                SerializedProperty dependencies =
                    invocation.FindPropertyRelative("dependencies");
                int dependencyCount = dependencies?.arraySize ?? 0;
                var dependencyIds = new string[dependencyCount];
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencyCount;
                     dependencyIndex++)
                {
                    dependencyIds[dependencyIndex] = dependencies
                        .GetArrayElementAtIndex(dependencyIndex)
                        .FindPropertyRelative("invocationId")
                        .stringValue;
                }

                nodes[invocationIndex] = new BuildRecipeAuthoringGraphNode(
                    invocation.FindPropertyRelative("invocationId").stringValue,
                    invocation.FindPropertyRelative("stepTypeId").stringValue,
                    dependencyIds);
            }

            recipeGraphSnapshot = BuildRecipeAuthoringGraphSnapshot.Create(nodes);
            return recipeGraphSnapshot;
        }
    }
}
