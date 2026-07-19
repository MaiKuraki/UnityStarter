using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class InputActionGraphBuilder
    {
        internal static InputAction CreateAction(
            InputActionMap map,
            RuntimeActionBindingConfig config)
        {
            InputActionType actionType = config.Type == ActionValueType.Button
                ? InputActionType.Button
                : InputActionType.Value;
            string expectedControlType = config.ExpectedControlType;
            if (string.IsNullOrEmpty(expectedControlType))
            {
                expectedControlType = config.Type == ActionValueType.Vector2
                    ? "Vector2"
                    : config.Type == ActionValueType.Float
                        ? "Axis"
                        : "Button";
            }

            return map.AddAction(
                config.ActionName,
                actionType,
                interactions: config.Interactions,
                processors: config.Processors,
                expectedControlLayout: expectedControlType);
        }

        internal static void AddBindings(
            InputAction action,
            RuntimeActionBindingConfig config)
        {
            for (int i = 0; i < config.DeviceBindings.Count; i++)
            {
                action.AddBinding(config.DeviceBindings[i], groups: config.BindingGroups);
            }

            for (int compositeIndex = 0; compositeIndex < config.CompositeBindings.Count; compositeIndex++)
            {
                RuntimeCompositeBindingConfig composite = config.CompositeBindings[compositeIndex];
                string compositeName = string.IsNullOrEmpty(composite.Parameters)
                    ? composite.Name
                    : $"{composite.Name}({composite.Parameters})";
                InputActionSetupExtensions.CompositeSyntax syntax = action.AddCompositeBinding(compositeName);
                string groups = string.IsNullOrEmpty(composite.BindingGroups)
                    ? config.BindingGroups
                    : composite.BindingGroups;
                for (int partIndex = 0; partIndex < composite.Parts.Count; partIndex++)
                {
                    RuntimeCompositePartBindingConfig part = composite.Parts[partIndex];
                    syntax = syntax.With(part.Name, part.Path, groups, part.Processors);
                }
            }
        }

        internal static void BuildControlSchemes(
            InputActionAsset asset,
            RuntimePlayerSlotConfig config)
        {
            for (int schemeIndex = 0; schemeIndex < config.ControlSchemes.Count; schemeIndex++)
            {
                RuntimeControlSchemeConfig scheme = config.ControlSchemes[schemeIndex];
                InputActionSetupExtensions.ControlSchemeSyntax syntax = asset
                    .AddControlScheme(scheme.Name)
                    .WithBindingGroup(scheme.BindingGroup);
                for (int requirementIndex = 0; requirementIndex < scheme.DeviceRequirements.Count; requirementIndex++)
                {
                    RuntimeControlSchemeDeviceRequirementConfig requirement =
                        scheme.DeviceRequirements[requirementIndex];
                    if (requirement.IsOr)
                    {
                        syntax = requirement.IsOptional
                            ? syntax.OrWithOptionalDevice(requirement.ControlPath)
                            : syntax.OrWithRequiredDevice(requirement.ControlPath);
                    }
                    else
                    {
                        syntax = requirement.IsOptional
                            ? syntax.WithOptionalDevice(requirement.ControlPath)
                            : syntax.WithRequiredDevice(requirement.ControlPath);
                    }
                }
            }
        }
    }
}
