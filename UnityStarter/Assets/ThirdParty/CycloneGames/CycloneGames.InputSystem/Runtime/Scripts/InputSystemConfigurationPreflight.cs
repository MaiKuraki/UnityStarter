using System;
using System.Collections.Generic;
using System.Reflection;

using Cysharp.Threading.Tasks;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using UnityEngine.InputSystem.Processors;
using UnityEngine.InputSystem.Utilities;

namespace CycloneGames.InputSystem.Runtime
{
    public static class InputSystemConfigurationPreflight
    {
        private const int MaxIssues = 64;

        public static InputConfigurationPreflightResult Validate(
            InputConfiguration configuration,
            InputConfigurationLimits limits = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                return new InputConfigurationPreflightResult(
                    InputConfigurationPreflightStatus.NotMainThread,
                    new List<InputConfigurationPreflightIssue>());
            }

            InputConfigurationValidationResult validation =
                InputConfigurationValidator.ValidateAndPrepare(configuration, limits);
            if (validation.IsValid) return Run(validation.RuntimeConfiguration);
            var issues = new List<InputConfigurationPreflightIssue>(1)
            {
                new InputConfigurationPreflightIssue(
                    InputConfigurationPreflightIssueCode.SchemaValidationFailed,
                    validation.Issues.Count == 0 ? string.Empty : validation.Issues[0].Path,
                    validation.Issues.Count == 0
                        ? "Configuration schema validation failed."
                        : validation.Issues[0].Message)
            };
            return new InputConfigurationPreflightResult(
                InputConfigurationPreflightStatus.Failed,
                issues,
                validation.Issues.Count > 1);
        }

        internal static InputConfigurationPreflightResult Run(RuntimeInputConfiguration configuration)
        {
            var issues = new List<InputConfigurationPreflightIssue>();
            if (!PlayerLoopHelper.IsMainThread)
            {
                return new InputConfigurationPreflightResult(
                    InputConfigurationPreflightStatus.NotMainThread,
                    issues);
            }

            bool wasTruncated = false;
            ValidateAction(configuration.JoinAction, "joinAction", issues, ref wasTruncated);
            for (int playerIndex = 0; playerIndex < configuration.PlayerSlots.Count; playerIndex++)
            {
                RuntimePlayerSlotConfig player = configuration.PlayerSlots[playerIndex];
                string playerPath = $"playerSlots[{playerIndex}]";
                ValidateAction(player.JoinAction, playerPath + ".joinAction", issues, ref wasTruncated);
                for (int schemeIndex = 0; schemeIndex < player.ControlSchemes.Count; schemeIndex++)
                {
                    RuntimeControlSchemeConfig scheme = player.ControlSchemes[schemeIndex];
                    for (int requirementIndex = 0;
                         requirementIndex < scheme.DeviceRequirements.Count;
                         requirementIndex++)
                    {
                        ValidateControlPath(
                            scheme.DeviceRequirements[requirementIndex].ControlPath,
                            null,
                            true,
                            $"{playerPath}.controlSchemes[{schemeIndex}].deviceRequirements[{requirementIndex}].controlPath",
                            issues,
                            ref wasTruncated);
                    }
                }

                for (int contextIndex = 0; contextIndex < player.Contexts.Count; contextIndex++)
                {
                    RuntimeContextDefinitionConfig context = player.Contexts[contextIndex];
                    for (int actionIndex = 0; actionIndex < context.Bindings.Count; actionIndex++)
                    {
                        ValidateAction(
                            context.Bindings[actionIndex],
                            $"{playerPath}.contexts[{contextIndex}].bindings[{actionIndex}]",
                            issues,
                            ref wasTruncated);
                    }
                }
            }

            if (issues.Count > 0)
            {
                return new InputConfigurationPreflightResult(
                    InputConfigurationPreflightStatus.Failed,
                    issues,
                    wasTruncated);
            }

            if (configuration.JoinAction != null)
            {
                BuildAndResolveRootJoin(configuration.JoinAction, issues, ref wasTruncated);
            }

            for (int playerIndex = 0;
                 playerIndex < configuration.PlayerSlots.Count && issues.Count == 0;
                 playerIndex++)
            {
                BuildAndResolvePlayer(
                    configuration.PlayerSlots[playerIndex],
                    playerIndex,
                    issues,
                    ref wasTruncated);
            }

            return new InputConfigurationPreflightResult(
                issues.Count == 0
                    ? InputConfigurationPreflightStatus.Success
                    : InputConfigurationPreflightStatus.Failed,
                issues,
                wasTruncated);
        }

        internal static bool ValidateBindingOverride(
            string overridePath,
            string overrideInteractions,
            string overrideProcessors,
            string expectedControlLayout,
            bool isComposite,
            bool isPartOfComposite)
        {
            if (!PlayerLoopHelper.IsMainThread) return false;
            var issues = new List<InputConfigurationPreflightIssue>();
            bool wasTruncated = false;
            if (!string.IsNullOrEmpty(overridePath))
            {
                if (isComposite)
                {
                    ValidateCompositeRegistration(
                        overridePath,
                        "bindingOverride.overridePath",
                        issues,
                        ref wasTruncated);
                }
                else
                {
                    ValidateControlPath(
                        overridePath,
                        isPartOfComposite ? null : expectedControlLayout,
                        false,
                        "bindingOverride.overridePath",
                        issues,
                        ref wasTruncated);
                }
            }

            if (isPartOfComposite && !string.IsNullOrEmpty(overrideInteractions))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    InputConfigurationPreflightIssueCode.UnsupportedPlacement,
                    "bindingOverride.overrideInteractions",
                    "Composite-part interactions are not supported; configure the interaction on the action.");
            }
            else
            {
                ValidateRegistrationList(
                    overrideInteractions,
                    UnityEngine.InputSystem.InputSystem.TryGetInteraction,
                    typeof(IInputInteraction),
                    InputConfigurationPreflightIssueCode.UnknownInteraction,
                    InputConfigurationPreflightIssueCode.InvalidInteractionParameters,
                    "bindingOverride.overrideInteractions",
                    issues,
                    ref wasTruncated);
            }
            ValidateRegistrationList(
                overrideProcessors,
                UnityEngine.InputSystem.InputSystem.TryGetProcessor,
                typeof(InputProcessor),
                InputConfigurationPreflightIssueCode.UnknownProcessor,
                InputConfigurationPreflightIssueCode.InvalidProcessorParameters,
                "bindingOverride.overrideProcessors",
                issues,
                ref wasTruncated);
            return issues.Count == 0;
        }

        private static void ValidateAction(
            RuntimeActionBindingConfig action,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            if (action == null || wasTruncated) return;
            string expectedLayout = string.IsNullOrEmpty(action.ExpectedControlType)
                ? action.Type == ActionValueType.Vector2
                    ? "Vector2"
                    : action.Type == ActionValueType.Float
                        ? "Axis"
                        : "Button"
                : action.ExpectedControlType;
            ValidateLayout(expectedLayout, path + ".expectedControlType", issues, ref wasTruncated);
            ValidateRegistrationList(
                action.Interactions,
                UnityEngine.InputSystem.InputSystem.TryGetInteraction,
                typeof(IInputInteraction),
                InputConfigurationPreflightIssueCode.UnknownInteraction,
                InputConfigurationPreflightIssueCode.InvalidInteractionParameters,
                path + ".interactions",
                issues,
                ref wasTruncated);
            ValidateRegistrationList(
                action.Processors,
                UnityEngine.InputSystem.InputSystem.TryGetProcessor,
                typeof(InputProcessor),
                InputConfigurationPreflightIssueCode.UnknownProcessor,
                InputConfigurationPreflightIssueCode.InvalidProcessorParameters,
                path + ".processors",
                issues,
                ref wasTruncated);

            for (int bindingIndex = 0; bindingIndex < action.DeviceBindings.Count; bindingIndex++)
            {
                ValidateControlPath(
                    action.DeviceBindings[bindingIndex],
                    expectedLayout,
                    false,
                    $"{path}.deviceBindings[{bindingIndex}]",
                    issues,
                    ref wasTruncated);
            }

            for (int compositeIndex = 0;
                 compositeIndex < action.CompositeBindings.Count && !wasTruncated;
                 compositeIndex++)
            {
                RuntimeCompositeBindingConfig composite = action.CompositeBindings[compositeIndex];
                string compositePath = $"{path}.compositeBindings[{compositeIndex}]";
                string expression = string.IsNullOrEmpty(composite.Parameters)
                    ? composite.Name
                    : $"{composite.Name}({composite.Parameters})";
                ValidateCompositeRegistration(
                    expression,
                    compositePath,
                    issues,
                    ref wasTruncated);
                for (int partIndex = 0; partIndex < composite.Parts.Count; partIndex++)
                {
                    RuntimeCompositePartBindingConfig part = composite.Parts[partIndex];
                    string partPath = $"{compositePath}.parts[{partIndex}]";
                    ValidateControlPath(
                        part.Path,
                        null,
                        false,
                        partPath + ".path",
                        issues,
                        ref wasTruncated);
                    ValidateRegistrationList(
                        part.Processors,
                        UnityEngine.InputSystem.InputSystem.TryGetProcessor,
                        typeof(InputProcessor),
                        InputConfigurationPreflightIssueCode.UnknownProcessor,
                        InputConfigurationPreflightIssueCode.InvalidProcessorParameters,
                        partPath + ".processors",
                        issues,
                        ref wasTruncated);
                    if (!string.IsNullOrEmpty(part.Interactions))
                    {
                        AddIssue(
                            issues,
                            ref wasTruncated,
                            InputConfigurationPreflightIssueCode.UnsupportedPlacement,
                            partPath + ".interactions",
                            "Composite-part interactions are not supported; configure the interaction on the action.");
                    }
                }
            }
        }

        private static void ValidateRegistrationList(
            string expression,
            Func<string, Type> lookup,
            Type requiredType,
            InputConfigurationPreflightIssueCode unknownCode,
            InputConfigurationPreflightIssueCode invalidCode,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            if (string.IsNullOrEmpty(expression) || wasTruncated) return;
            try
            {
                foreach (NameAndParameters item in NameAndParameters.ParseMultiple(expression))
                {
                    ValidateRegistration(
                        item,
                        lookup,
                        requiredType,
                        unknownCode,
                        invalidCode,
                        path,
                        issues,
                        ref wasTruncated);
                }
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    invalidCode,
                    path,
                    $"The registered expression is invalid ({exception.GetType().Name}).");
            }
        }

        private static void ValidateCompositeRegistration(
            string expression,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            try
            {
                ValidateRegistration(
                    NameAndParameters.Parse(expression),
                    UnityEngine.InputSystem.InputSystem.TryGetBindingComposite,
                    typeof(InputBindingComposite),
                    InputConfigurationPreflightIssueCode.UnknownComposite,
                    InputConfigurationPreflightIssueCode.InvalidCompositeParameters,
                    path,
                    issues,
                    ref wasTruncated);
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    InputConfigurationPreflightIssueCode.InvalidCompositeParameters,
                    path,
                    $"The composite expression is invalid ({exception.GetType().Name}).");
            }
        }

        private static void ValidateRegistration(
            NameAndParameters item,
            Func<string, Type> lookup,
            Type requiredType,
            InputConfigurationPreflightIssueCode unknownCode,
            InputConfigurationPreflightIssueCode invalidCode,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            if (wasTruncated) return;
            Type type = lookup(item.name);
            if (type == null || !requiredType.IsAssignableFrom(type))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    unknownCode,
                    path,
                    "The registered Input System type is unavailable.");
                return;
            }

            try
            {
                object instance = Activator.CreateInstance(type);
                foreach (NamedValue parameter in item.parameters) parameter.ApplyToObject(instance);
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    invalidCode,
                    path,
                    $"The registered expression parameters are invalid ({exception.GetType().Name}).");
            }
        }

        private static void ValidateControlPath(
            string controlPath,
            string expectedLayout,
            bool allowDeviceOnly,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            if (wasTruncated) return;
            try
            {
                int componentCount = 0;
                foreach (InputControlPath.ParsedPathComponent _ in InputControlPath.Parse(controlPath))
                    componentCount++;
                string deviceLayout = InputControlPath.TryGetDeviceLayout(controlPath);
                if (componentCount == 0 || string.IsNullOrEmpty(deviceLayout) || deviceLayout == InputControlPath.Wildcard)
                {
                    AddIssue(
                        issues,
                        ref wasTruncated,
                        InputConfigurationPreflightIssueCode.InvalidControlPath,
                        path,
                        "The control path must declare a concrete device layout.");
                    return;
                }

                if (UnityEngine.InputSystem.InputSystem.LoadLayout(deviceLayout) == null)
                {
                    AddIssue(
                        issues,
                        ref wasTruncated,
                        InputConfigurationPreflightIssueCode.UnknownControlLayout,
                        path,
                        "The control path references an unknown device layout.");
                    return;
                }

                string controlLayout = InputControlPath.TryGetControlLayout(controlPath);
                if (string.IsNullOrEmpty(controlLayout))
                {
                    if (!allowDeviceOnly)
                    {
                        AddIssue(
                            issues,
                            ref wasTruncated,
                            InputConfigurationPreflightIssueCode.UnknownControlLayout,
                            path,
                            "The control path does not resolve to a known control layout.");
                    }

                    return;
                }

                if (UnityEngine.InputSystem.InputSystem.LoadLayout(controlLayout) == null)
                {
                    AddIssue(
                        issues,
                        ref wasTruncated,
                        InputConfigurationPreflightIssueCode.UnknownControlLayout,
                        path,
                        "The control path resolves to an unknown control layout.");
                    return;
                }

                if (!string.IsNullOrEmpty(expectedLayout) &&
                    !UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(controlLayout, expectedLayout))
                {
                    AddIssue(
                        issues,
                        ref wasTruncated,
                        InputConfigurationPreflightIssueCode.IncompatibleControlLayout,
                        path,
                        "The resolved control layout is incompatible with the action's expected layout.");
                }
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    InputConfigurationPreflightIssueCode.InvalidControlPath,
                    path,
                    $"The control path is invalid ({exception.GetType().Name}).");
            }
        }

        private static void ValidateLayout(
            string layout,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            try
            {
                if (UnityEngine.InputSystem.InputSystem.LoadLayout(layout) != null) return;
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
            }

            AddIssue(
                issues,
                ref wasTruncated,
                InputConfigurationPreflightIssueCode.UnknownControlLayout,
                path,
                "The expected control layout is unavailable.");
        }

        private static void BuildAndResolveRootJoin(
            RuntimeActionBindingConfig joinAction,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            BuildAndResolve(
                "joinAction",
                asset =>
                {
                    InputActionMap map = asset.AddActionMap("GlobalActions::PlayerJoin");
                    InputAction action = InputActionGraphBuilder.CreateAction(map, joinAction);
                    InputActionGraphBuilder.AddBindings(action, joinAction);
                },
                issues,
                ref wasTruncated);
        }

        private static void BuildAndResolvePlayer(
            RuntimePlayerSlotConfig player,
            int playerIndex,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            BuildAndResolve(
                $"playerSlots[{playerIndex}]",
                asset =>
                {
                    InputActionGraphBuilder.BuildControlSchemes(asset, player);
                    if (player.JoinAction != null)
                    {
                        InputActionMap joinMap = asset.AddActionMap("GlobalActions::PlayerJoin");
                        InputAction join = InputActionGraphBuilder.CreateAction(joinMap, player.JoinAction);
                        InputActionGraphBuilder.AddBindings(join, player.JoinAction);
                    }

                    for (int contextIndex = 0; contextIndex < player.Contexts.Count; contextIndex++)
                    {
                        RuntimeContextDefinitionConfig context = player.Contexts[contextIndex];
                        InputActionMap map = asset.AddActionMap(
                            $"{context.ActionMap}::{context.Name}#{contextIndex}");
                        for (int actionIndex = 0; actionIndex < context.Bindings.Count; actionIndex++)
                        {
                            RuntimeActionBindingConfig actionConfig = context.Bindings[actionIndex];
                            InputAction action = InputActionGraphBuilder.CreateAction(map, actionConfig);
                            InputActionGraphBuilder.AddBindings(action, actionConfig);
                        }
                    }
                },
                issues,
                ref wasTruncated);
        }

        private static void BuildAndResolve(
            string path,
            Action<InputActionAsset> build,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            InputActionAsset asset = null;
            InputConfigurationPreflightIssueCode phase =
                InputConfigurationPreflightIssueCode.ConstructionFailed;
            try
            {
                asset = ScriptableObject.CreateInstance<InputActionAsset>();
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.devices = new ReadOnlyArray<InputDevice>(Array.Empty<InputDevice>());
                build(asset);
                phase = InputConfigurationPreflightIssueCode.ResolutionFailed;
                foreach (InputActionMap map in asset.actionMaps)
                {
                    foreach (InputAction action in map.actions)
                    {
                        _ = action.controls.Count;
                    }
                }
            }
            catch (Exception exception) when (!IsCritical(exception))
            {
                AddIssue(
                    issues,
                    ref wasTruncated,
                    phase,
                    path,
                    $"The Input System graph could not be prepared ({exception.GetType().Name}).");
            }
            finally
            {
                if (asset != null) CleanupAsset(asset, path, issues, ref wasTruncated);
            }
        }

        private static void CleanupAsset(
            InputActionAsset asset,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated)
        {
            bool cleanupIssueRecorded = false;
            try
            {
                try
                {
                    asset.Disable();
                }
                catch (Exception exception) when (!IsCritical(exception))
                {
                    RecordCleanupFailure(
                        exception,
                        path,
                        issues,
                        ref wasTruncated,
                        ref cleanupIssueRecorded);
                }

                var actionMaps = asset.actionMaps;
                var snapshot = new InputActionMap[actionMaps.Count];
                for (int i = 0; i < actionMaps.Count; i++) snapshot[i] = actionMaps[i];
                for (int i = 0; i < snapshot.Length; i++)
                {
                    InputActionMap map = snapshot[i];
                    if (map == null) continue;
                    try
                    {
                        map.Dispose();
                    }
                    catch (Exception exception) when (!IsCritical(exception))
                    {
                        RecordCleanupFailure(
                            exception,
                            path,
                            issues,
                            ref wasTruncated,
                            ref cleanupIssueRecorded);
                    }
                }
            }
            finally
            {
                try
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
                    else UnityEngine.Object.DestroyImmediate(asset);
                }
                catch (Exception exception) when (!IsCritical(exception))
                {
                    RecordCleanupFailure(
                        exception,
                        path,
                        issues,
                        ref wasTruncated,
                        ref cleanupIssueRecorded);
                }
            }
        }

        private static void RecordCleanupFailure(
            Exception exception,
            string path,
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated,
            ref bool wasRecorded)
        {
            if (wasRecorded) return;
            wasRecorded = true;
            AddIssue(
                issues,
                ref wasTruncated,
                InputConfigurationPreflightIssueCode.CleanupFailed,
                path,
                $"The temporary Input System graph could not be fully released ({exception.GetType().Name}).");
        }

        private static void AddIssue(
            List<InputConfigurationPreflightIssue> issues,
            ref bool wasTruncated,
            InputConfigurationPreflightIssueCode code,
            string path,
            string message)
        {
            if (issues.Count >= MaxIssues)
            {
                wasTruncated = true;
                return;
            }

            issues.Add(new InputConfigurationPreflightIssue(code, path, message));
        }

        private static bool IsCritical(Exception exception)
        {
            Exception current = exception;
            while ((current is TargetInvocationException || current is TypeInitializationException) &&
                   current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current is OutOfMemoryException ||
                   current is AccessViolationException ||
                   current is StackOverflowException;
        }
    }
}
