using System;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private void TryRenameInvocation(
            int ownerIndex,
            SerializedProperty invocationId,
            string editedValue)
        {
            string candidate = editedValue?.Trim() ?? string.Empty;
            try
            {
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    candidate,
                    "Build Invocation ID");
            }
            catch (ArgumentException)
            {
                return;
            }

            if (IsInvocationIdConfiguredAtAnotherIndex(candidate, ownerIndex))
            {
                return;
            }

            RenameInvocation(ownerIndex, invocationId, candidate);
        }

        private void RenameInvocation(
            int ownerIndex,
            SerializedProperty invocationId,
            string nextValue)
        {
            string previousValue = invocationId.stringValue?.Trim() ?? string.Empty;
            string normalizedNextValue = nextValue?.Trim() ?? string.Empty;
            if (string.Equals(previousValue, normalizedNextValue, StringComparison.Ordinal))
            {
                return;
            }

            invocationId.stringValue = normalizedNextValue;
            InvalidateRecipeGraphSnapshot();
            if (string.IsNullOrWhiteSpace(previousValue)
                || string.IsNullOrWhiteSpace(normalizedNextValue))
            {
                return;
            }

            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                if (index == ownerIndex)
                {
                    continue;
                }

                SerializedProperty dependencies = recipeInvocations
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("dependencies");
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.arraySize;
                     dependencyIndex++)
                {
                    SerializedProperty target = dependencies
                        .GetArrayElementAtIndex(dependencyIndex)
                        .FindPropertyRelative("invocationId");
                    if (string.Equals(
                            target.stringValue?.Trim(),
                            previousValue,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        target.stringValue = normalizedNextValue;
                    }
                }
            }
        }

        private int CountDependencyReferences(string invocationId, int ignoredOwnerIndex)
        {
            return GetRecipeGraphSnapshot().CountDependencyReferences(
                invocationId,
                ignoredOwnerIndex);
        }

        private void RemoveDependencyReferences(string invocationId, int ignoredOwnerIndex)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                return;
            }

            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                if (index == ignoredOwnerIndex)
                {
                    continue;
                }

                SerializedProperty dependencies = recipeInvocations
                    .GetArrayElementAtIndex(index)
                    .FindPropertyRelative("dependencies");
                for (int dependencyIndex = dependencies.arraySize - 1;
                     dependencyIndex >= 0;
                     dependencyIndex--)
                {
                    string target = dependencies.GetArrayElementAtIndex(dependencyIndex)
                        .FindPropertyRelative("invocationId")
                        .stringValue?.Trim();
                    if (string.Equals(
                            target,
                            invocationId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        dependencies.DeleteArrayElementAtIndex(dependencyIndex);
                    }
                }
            }

            InvalidateRecipeGraphSnapshot();
        }

        private bool IsStepTypeConfiguredAtAnotherIndex(
            string stepTypeId,
            int currentIndex)
        {
            return GetRecipeGraphSnapshot().IsStepTypeConfiguredAtAnotherIndex(
                stepTypeId,
                currentIndex);
        }

        private string CreateUniqueInvocationId(string stepTypeId, int ignoredIndex)
        {
            string baseId = string.IsNullOrWhiteSpace(stepTypeId)
                ? "invocation"
                : stepTypeId.Trim();
            string candidate = baseId;
            int suffix = 2;
            while (IsInvocationIdConfiguredAtAnotherIndex(candidate, ignoredIndex))
            {
                candidate = baseId + "-" + suffix;
                suffix++;
            }

            return candidate;
        }

        private bool IsInvocationIdConfiguredAtAnotherIndex(
            string invocationId,
            int currentIndex)
        {
            return GetRecipeGraphSnapshot().IsInvocationIdConfiguredAtAnotherIndex(
                invocationId,
                currentIndex);
        }

    }
}
