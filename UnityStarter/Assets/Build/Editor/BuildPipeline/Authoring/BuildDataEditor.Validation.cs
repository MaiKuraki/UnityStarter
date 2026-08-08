using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Data;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private IReadOnlyList<string> ValidateSerializedProfile(
            BuildRecipeAnalysis recipe,
            IReadOnlyCollection<string> selectedInvocationIds = null)
        {
            var errors = new List<string>();
            if (recipe.IncludesPlayer && launchScene.objectReferenceValue == null)
            {
                errors.Add("Launch Scene is required when the recipe builds a Player.");
            }

            try
            {
                BuildIdentityPolicy.ValidateApplicationVersion(applicationVersion.stringValue);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }

            ValidateOutputRoot(outputBasePath.stringValue, errors);
            if (recipe.IncludesPlayer || recipe.IncludesHotUpdate)
            {
                try
                {
                    BuildIdentityPolicy.ValidatePlainText(companyName.stringValue, "Company Name", 256);
                }
                catch (ArgumentException exception)
                {
                    errors.Add(exception.Message);
                }

                ValidateRequired(productName.stringValue, "Product Name", errors);
                if (!string.IsNullOrWhiteSpace(productName.stringValue))
                {
                    TryValidatePortableFileName(productName.stringValue, "Product Name", errors);
                }

                try
                {
                    BuildIdentityPolicy.ValidateApplicationIdentifier(applicationIdentifier.stringValue);
                }
                catch (ArgumentException exception)
                {
                    errors.Add(exception.Message);
                }
            }

            if (recipe.IncludesPlayer)
            {
                ValidateVersionInfoPath(versionInfoAssetPath.stringValue, errors);
                if (!string.IsNullOrEmpty(versionInfoTargetOccupationError))
                {
                    errors.Add(versionInfoTargetOccupationError);
                }
            }
            ValidateRecipeInvocations(errors, selectedInvocationIds);
            foreach (string issue in recipe.BlockingIssues)
            {
                errors.Add(issue);
            }

            return errors;
        }

        private void ValidateRecipeInvocations(
            ICollection<string> errors,
            IReadOnlyCollection<string> selectedInvocationIds)
        {
            if (TryGetRecipeBudgetViolation(out string budgetViolation))
            {
                errors.Add(budgetViolation);
                return;
            }

            if (recipeInvocations.arraySize == 0)
            {
                errors.Add("At least one Build Recipe entry is required.");
                return;
            }

            var invocationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> selected = selectedInvocationIds == null
                ? null
                : new HashSet<string>(selectedInvocationIds, StringComparer.OrdinalIgnoreCase);
            var selectedTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                SerializedProperty entry = recipeInvocations.GetArrayElementAtIndex(index);
                bool enabled = entry.FindPropertyRelative("enabled").boolValue;
                string invocationId = entry.FindPropertyRelative("invocationId")
                    .stringValue?.Trim();
                string stepTypeId = entry.FindPropertyRelative("stepTypeId")
                    .stringValue?.Trim();
                UnityEngine.Object configuration = entry
                    .FindPropertyRelative("configuration")
                    .objectReferenceValue;

                if (string.IsNullOrEmpty(invocationId))
                {
                    errors.Add($"Build Recipe entry at index {index} has an empty Invocation ID.");
                }
                else
                {
                    try
                    {
                        BuildIdentityPolicy.ValidateBuildIdentifier(
                            invocationId,
                            "Build Invocation ID");
                    }
                    catch (ArgumentException exception)
                    {
                        errors.Add(exception.Message);
                    }

                    if (!invocationIds.Add(invocationId))
                    {
                        errors.Add(
                            $"Build Invocation ID '{invocationId}' is configured more than once.");
                    }
                }

                bool validateConfiguration = selected == null
                    ? enabled
                    : !string.IsNullOrEmpty(invocationId)
                      && selected.Contains(invocationId);
                if (!validateConfiguration)
                {
                    continue;
                }

                BuildStepDescriptor descriptor = FindStepDescriptor(stepTypeId);
                if (descriptor == null)
                {
                    errors.Add(
                        $"Enabled build invocation '{invocationId}' references unavailable step type '{stepTypeId}'.");
                    continue;
                }

                selectedTypeCounts.TryGetValue(stepTypeId, out int selectedTypeCount);
                selectedTypeCounts[stepTypeId] = selectedTypeCount + 1;

                if (descriptor.ConfigurationRequired && configuration == null)
                {
                    errors.Add(
                        $"Build invocation '{invocationId}' requires a {descriptor.ConfigurationType.Name} configuration asset.");
                    continue;
                }

                if (configuration != null
                    && (descriptor.ConfigurationType == null
                        || !descriptor.ConfigurationType.IsInstanceOfType(configuration)))
                {
                    string expected = descriptor.ConfigurationType?.Name ?? "no configuration";
                    errors.Add(
                        $"Build invocation '{invocationId}' expects {expected}, but references {configuration.GetType().Name}.");
                    continue;
                }

                if (configuration != null
                    && !ValidateConfigurationAssetReference(
                        invocationId,
                        configuration,
                        errors))
                {
                    continue;
                }

                if (configuration is AssetContentBuildConfiguration contentConfiguration)
                {
                    AssetContentProviderDescriptor provider =
                        FindProviderDescriptor(contentConfiguration.ProviderId);
                    if (provider == null
                        || !provider.ConfigurationType.IsInstanceOfType(configuration))
                    {
                        errors.Add(
                            $"Asset Content configuration '{configuration.name}' does not map to one declared provider.");
                    }
                    else if (!provider.IsAvailable)
                    {
                        errors.Add(
                            $"{provider.DisplayName} is selected, but its package-compatible build adapter is unavailable.");
                    }
                }

                else if (configuration is HotUpdateBuildConfiguration hotUpdateConfiguration)
                {
                    HotUpdateProviderDescriptor provider =
                        FindHotUpdateProviderDescriptor(
                            hotUpdateConfiguration.ProviderId);
                    if (provider == null
                        || !provider.ConfigurationType.IsInstanceOfType(configuration))
                    {
                        errors.Add(
                            $"Hot Update configuration '{configuration.name}' does not map to one declared provider.");
                    }
                    else if (!provider.IsAvailable)
                    {
                        errors.Add(
                            $"{provider.DisplayName} is selected, but its package-compatible build adapter is unavailable.");
                    }
                }
            }

            foreach (KeyValuePair<string, int> entry in selectedTypeCounts)
            {
                BuildStepDescriptor descriptor = FindStepDescriptor(entry.Key);
                if (entry.Value > 1
                    && descriptor?.Multiplicity != BuildStepMultiplicity.Multiple)
                {
                    errors.Add(
                        $"Step type '{entry.Key}' allows one invocation per build, but {entry.Value} are selected.");
                }
            }

            ValidateInvocationDependencies(errors, selected, invocationIds);

        }

        private void ValidateInvocationDependencies(
            ICollection<string> errors,
            IReadOnlyCollection<string> selected,
            IReadOnlyCollection<string> knownInvocationIds)
        {
            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                SerializedProperty owner = recipeInvocations.GetArrayElementAtIndex(index);
                string ownerId = owner.FindPropertyRelative("invocationId")
                    .stringValue?.Trim();
                bool ownerSelected = selected == null
                    ? owner.FindPropertyRelative("enabled").boolValue
                    : selected.Contains(ownerId);
                if (!ownerSelected)
                {
                    continue;
                }

                SerializedProperty dependencies = owner.FindPropertyRelative("dependencies");
                var dependencyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.arraySize;
                     dependencyIndex++)
                {
                    SerializedProperty dependency =
                        dependencies.GetArrayElementAtIndex(dependencyIndex);
                    string dependencyId = dependency.FindPropertyRelative("invocationId")
                        .stringValue?.Trim();
                    BuildDependencyMode mode = (BuildDependencyMode)dependency
                        .FindPropertyRelative("mode")
                        .enumValueIndex;
                    if (string.IsNullOrWhiteSpace(dependencyId))
                    {
                        errors.Add(
                            $"Build invocation '{ownerId}' has an empty dependency target.");
                        continue;
                    }

                    if (!dependencyIds.Add(dependencyId))
                    {
                        errors.Add(
                            $"Build invocation '{ownerId}' declares dependency '{dependencyId}' more than once.");
                        continue;
                    }

                    if (string.Equals(
                            ownerId,
                            dependencyId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"Build invocation '{ownerId}' cannot depend on itself.");
                        continue;
                    }

                    if (!knownInvocationIds.Contains(dependencyId))
                    {
                        errors.Add(
                            $"Build invocation '{ownerId}' references unknown dependency '{dependencyId}'.");
                        continue;
                    }

                    bool dependencySelected = selected == null
                        ? IsInvocationEnabled(dependencyId)
                        : selected.Contains(dependencyId);
                    if (mode == BuildDependencyMode.Required && !dependencySelected)
                    {
                        errors.Add(
                            $"Build invocation '{ownerId}' requires disabled or unselected invocation '{dependencyId}'.");
                    }
                }
            }
        }

        private bool IsInvocationEnabled(string invocationId)
        {
            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                SerializedProperty entry = recipeInvocations.GetArrayElementAtIndex(index);
                if (string.Equals(
                        entry.FindPropertyRelative("invocationId").stringValue?.Trim(),
                        invocationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.FindPropertyRelative("enabled").boolValue;
                }
            }

            return false;
        }

        private static bool ValidateConfigurationAssetReference(
            string invocationId,
            UnityEngine.Object configuration,
            ICollection<string> errors)
        {
            string path = AssetDatabase.GetAssetPath(configuration)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Build invocation '{invocationId}' configuration must be a persistent .asset below Assets.");
                return false;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    path,
                    $"Build invocation '{invocationId}' configuration");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
                return false;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != configuration)
            {
                errors.Add(
                    $"Build invocation '{invocationId}' configuration must be the main asset at '{path}', not a sub-asset.");
                return false;
            }

            return true;
        }

        private AssetContentProviderDescriptor FindProviderDescriptor(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return providerDescriptors.FirstOrDefault(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    providerId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private HotUpdateProviderDescriptor FindHotUpdateProviderDescriptor(
            string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return hotUpdateProviderDescriptors.FirstOrDefault(
                descriptor => string.Equals(
                    descriptor.ProviderId,
                    providerId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateOutputRoot(string value, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("Output Base Directory is required.");
                return;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(value, "Output Base Directory");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void ValidateVersionInfoPath(string value, ICollection<string> errors)
        {
            try
            {
                RuntimeVersionInfoPathPolicy.Validate(value);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void TryValidatePortableFileName(
            string value,
            string label,
            ICollection<string> errors)
        {
            try
            {
                BuildPathPolicy.ValidatePortableFileName(value, label);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void ValidateRequired(string value, string label, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(label + " is required.");
            }
        }

    }
}
