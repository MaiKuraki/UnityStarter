using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    internal readonly struct InputEditorValidationResult
    {
        internal InputEditorValidationResult(
            string error,
            string warning,
            InputConfiguration preparedConfiguration = null)
        {
            Error = error;
            Warning = warning;
            PreparedConfiguration = preparedConfiguration;
        }

        internal string Error { get; }
        internal string Warning { get; }
        internal InputConfiguration PreparedConfiguration { get; }
        internal bool IsValid => string.IsNullOrEmpty(Error);
    }

    internal static class InputEditorConfigurationValidator
    {
        private const string ReservedActionMap = "GlobalActions";

        internal static InputEditorValidationResult Validate(InputConfiguration configuration)
        {
            if (configuration == null)
            {
                return Error("The input configuration is null.");
            }

            if (configuration.PlayerSlots == null)
            {
                return Error("PlayerSlots is null.");
            }

            InputConfigurationValidationResult runtimeValidation =
                InputConfigurationValidator.ValidateAndPrepare(configuration);
            if (!runtimeValidation.IsValid)
            {
                InputConfigurationIssue issue = runtimeValidation.Issues[0];
                return Error($"Runtime validation failed: {issue}");
            }

            configuration = runtimeValidation.Configuration;

            InputEditorValidationResult rootJoinResult = ValidateBinding(
                configuration.JoinAction,
                "Root JoinAction",
                true);
            if (!rootJoinResult.IsValid)
            {
                return rootJoinResult;
            }

            var playerIds = new HashSet<int>();
            string warning = runtimeValidation.WasMigrated
                ? "The schema-zero configuration was migrated in memory. Save explicitly to persist schema one."
                : null;

            for (int playerIndex = 0; playerIndex < configuration.PlayerSlots.Count; playerIndex++)
            {
                PlayerSlotConfig slot = configuration.PlayerSlots[playerIndex];
                if (slot == null)
                {
                    return Error($"Player slot {playerIndex} is null.");
                }

                if (!playerIds.Add(slot.PlayerId))
                {
                    return Error($"PlayerId {slot.PlayerId} is duplicated.");
                }

                InputEditorValidationResult joinResult = ValidateBinding(
                    slot.JoinAction,
                    $"Player {slot.PlayerId} JoinAction",
                    true);
                if (!joinResult.IsValid)
                {
                    return joinResult;
                }

                if (slot.Contexts == null)
                {
                    return Error($"Player {slot.PlayerId} Contexts is null.");
                }

                var contextNames = new HashSet<string>(StringComparer.Ordinal);
                for (int contextIndex = 0; contextIndex < slot.Contexts.Count; contextIndex++)
                {
                    ContextDefinitionConfig context = slot.Contexts[contextIndex];
                    if (context == null)
                    {
                        return Error($"Player {slot.PlayerId}, context {contextIndex} is null.");
                    }

                    if (string.IsNullOrWhiteSpace(context.Name))
                    {
                        return Error($"Player {slot.PlayerId}, context {contextIndex} has no name.");
                    }

                    if (!contextNames.Add(context.Name))
                    {
                        return Error($"Context \"{context.Name}\" is duplicated for Player {slot.PlayerId}.");
                    }

                    if (string.IsNullOrWhiteSpace(context.ActionMap))
                    {
                        return Error($"Player {slot.PlayerId}, context \"{context.Name}\" has no ActionMap.");
                    }

                    if (context.ActionMap.Equals(ReservedActionMap, StringComparison.Ordinal))
                    {
                        return Error($"ActionMap \"{ReservedActionMap}\" is reserved for join actions.");
                    }

                    if (context.Bindings == null)
                    {
                        return Error($"Player {slot.PlayerId}, context \"{context.Name}\" has a null Bindings list.");
                    }

                    for (int bindingIndex = 0; bindingIndex < context.Bindings.Count; bindingIndex++)
                    {
                        InputEditorValidationResult bindingResult = ValidateBinding(
                            context.Bindings[bindingIndex],
                            $"Player {slot.PlayerId}, context \"{context.Name}\", binding {bindingIndex}",
                            false);
                        if (!bindingResult.IsValid)
                        {
                            return bindingResult;
                        }
                    }
                }

                List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(slot);
                for (int conflictIndex = 0; conflictIndex < conflicts.Count; conflictIndex++)
                {
                    BindingConflict conflict = conflicts[conflictIndex];
                    if (conflict.Severity == BindingConflictSeverity.Critical)
                    {
                        return Error($"Player {slot.PlayerId}: {conflict}");
                    }

                    warning ??= $"Player {slot.PlayerId}: {conflict}";
                }
            }

            if (configuration.SchemaVersion < 0)
            {
                return Error("SchemaVersion cannot be negative.");
            }
            if (configuration.SchemaVersion > InputConfiguration.CurrentSchemaVersion)
            {
                return Error(
                    $"Schema version {configuration.SchemaVersion} is newer than supported version {InputConfiguration.CurrentSchemaVersion}.");
            }

            return new InputEditorValidationResult(null, warning, configuration);
        }

        private static InputEditorValidationResult ValidateBinding(
            ActionBindingConfig binding,
            string location,
            bool optional)
        {
            if (binding == null)
            {
                return optional
                    ? new InputEditorValidationResult(null, null)
                    : Error($"{location} is null.");
            }

            if (string.IsNullOrWhiteSpace(binding.ActionName))
            {
                return Error($"{location} has no action name.");
            }

            int directBindingCount = binding.DeviceBindings?.Count ?? 0;
            int compositeBindingCount = binding.CompositeBindings?.Count ?? 0;
            if (directBindingCount == 0 && compositeBindingCount == 0)
            {
                return Error($"{location} has no direct or composite bindings.");
            }

            if (binding.LongPressMs < 0)
            {
                return Error($"{location} has a negative long-press duration.");
            }

            if (binding.LongPressValueThreshold < 0f || binding.LongPressValueThreshold > 1f)
            {
                return Error($"{location} has a long-press threshold outside 0..1.");
            }

            return new InputEditorValidationResult(null, null);
        }

        private static InputEditorValidationResult Error(string message)
        {
            return new InputEditorValidationResult(message, null);
        }
    }
}
