using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    public partial class InputEditorWindow
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private void GenerateConstantsFile(InputConfiguration configuration)
        {
            if (!InputConstantsCodeGenerator.TryGenerate(
                    configuration,
                    _codegenNamespace,
                    out string source,
                    out string generationError))
            {
                SetStatus($"Cannot generate constants: {generationError}", MessageType.Error);
                return;
            }

            if (!InputEditorFileUtility.TryResolveAssetFile(
                    _codegenFolder,
                    "InputActions.cs",
                    out string assetPath,
                    out string absolutePath,
                    out string pathError))
            {
                SetStatus(pathError, MessageType.Error);
                return;
            }

            if (!InputEditorFileUtility.TryWriteBytesTransactional(
                    absolutePath,
                    Utf8WithoutBom.GetBytes(source),
                    out string backupPath,
                    out string writeError))
            {
                SetStatus(writeError, MessageType.Error);
                return;
            }

            InputEditorFileUtility.ImportAssetAtPath(assetPath);
            string backupLabel = string.IsNullOrEmpty(backupPath)
                ? string.Empty
                : $" Backup: {Path.GetFileName(backupPath)}.";
            bool recoveryBackup = !string.IsNullOrEmpty(backupPath) &&
                                  backupPath.IndexOf(".bak.tmp.", StringComparison.Ordinal) >= 0;
            string recoveryLabel = recoveryBackup
                ? " Fixed-backup promotion did not complete; retain the reported recovery backup."
                : string.Empty;
            SetStatus(
                $"Generated constants at {assetPath}.{backupLabel}{recoveryLabel}",
                recoveryBackup ? MessageType.Warning : MessageType.Info);
        }

        private InputConfiguration CreateDefaultConfigTemplate()
        {
            return new InputConfiguration
            {
                SchemaVersion = InputConfiguration.CurrentSchemaVersion,
                SchemaFingerprint = InputSchemaFingerprint.EditorDiagnosticCurrent,
                JoinAction = null,
                PlayerSlots = new List<PlayerSlotConfig>
                {
                    CreateDefaultPlayerSlot(0),
                    CreateDefaultPlayerSlot(1)
                }
            };
        }

        private static PlayerSlotConfig CreateDefaultPlayerSlot(int playerId)
        {
            return new PlayerSlotConfig
            {
                PlayerId = playerId,
                JoinAction = CreateJoinAction(),
                DefaultControlScheme = null,
                ControlSchemes = CreateDefaultControlSchemes(),
                Contexts = new List<ContextDefinitionConfig>
                {
                    new ContextDefinitionConfig
                    {
                        Name = "Gameplay",
                        ActionMap = "PlayerActions",
                        Priority = 0,
                        BlocksLowerPriority = true,
                        Bindings = new List<ActionBindingConfig>
                        {
                            CreateMoveAction(),
                            CreateConfirmAction()
                        }
                    }
                }
            };
        }

        private static ActionBindingConfig CreateJoinAction()
        {
            return new ActionBindingConfig
            {
                Type = ActionValueType.Button,
                ActionName = "JoinGame",
                ExpectedControlType = "Button",
                UpdateMode = InputUpdateMode.EventDriven,
                DeviceBindings = new List<string>
                {
                    "<Keyboard>/enter",
                    "<Gamepad>/start"
                },
                CompositeBindings = null,
                LongPressMs = 0,
                LongPressValueThreshold = 0.5f
            };
        }

        private static ActionBindingConfig CreateMoveAction()
        {
            return new ActionBindingConfig
            {
                Type = ActionValueType.Vector2,
                ActionName = "Move",
                ExpectedControlType = "Vector2",
                UpdateMode = InputUpdateMode.Polling,
                DeviceBindings = new List<string>
                {
                    InputBindingConstants.Vector2Sources.Gamepad_LeftStick,
                    InputBindingConstants.Vector2Sources.Mouse_Delta
                },
                CompositeBindings = new List<CompositeBindingConfig>
                {
                    new CompositeBindingConfig
                    {
                        Name = "2DVector",
                        Parameters = "mode=2",
                        BindingGroups = "KeyboardMouse",
                        Parts = new List<CompositePartBindingConfig>
                        {
                            CreateCompositePart("Up", "<Keyboard>/w"),
                            CreateCompositePart("Down", "<Keyboard>/s"),
                            CreateCompositePart("Left", "<Keyboard>/a"),
                            CreateCompositePart("Right", "<Keyboard>/d")
                        }
                    }
                },
                LongPressMs = 0,
                LongPressValueThreshold = 0.5f
            };
        }

        private static ActionBindingConfig CreateConfirmAction()
        {
            return new ActionBindingConfig
            {
                Type = ActionValueType.Button,
                ActionName = "Confirm",
                ExpectedControlType = "Button",
                UpdateMode = InputUpdateMode.EventDriven,
                DeviceBindings = new List<string>
                {
                    "<Gamepad>/buttonSouth",
                    "<Keyboard>/space"
                },
                CompositeBindings = null,
                LongPressMs = 500,
                LongPressValueThreshold = 0.5f
            };
        }

        private static CompositePartBindingConfig CreateCompositePart(string name, string path)
        {
            return new CompositePartBindingConfig
            {
                Name = name,
                Path = path
            };
        }

        private static List<ControlSchemeConfig> CreateDefaultControlSchemes()
        {
            return new List<ControlSchemeConfig>
            {
                new ControlSchemeConfig
                {
                    Name = "KeyboardMouse",
                    BindingGroup = "KeyboardMouse",
                    DeviceRequirements = new List<ControlSchemeDeviceRequirementConfig>
                    {
                        new ControlSchemeDeviceRequirementConfig
                        {
                            ControlPath = "<Keyboard>"
                        },
                        new ControlSchemeDeviceRequirementConfig
                        {
                            ControlPath = "<Mouse>",
                            IsOptional = true
                        }
                    }
                },
                new ControlSchemeConfig
                {
                    Name = "Gamepad",
                    BindingGroup = "Gamepad",
                    DeviceRequirements = new List<ControlSchemeDeviceRequirementConfig>
                    {
                        new ControlSchemeDeviceRequirementConfig
                        {
                            ControlPath = "<Gamepad>"
                        }
                    }
                }
            };
        }

        private void AddNewPlayer(SerializedProperty slots)
        {
            int playerId = FindAvailablePlayerId(slots);
            int newIndex = slots.arraySize;
            slots.arraySize++;
            SerializedProperty slot = slots.GetArrayElementAtIndex(newIndex);

            SetInt(slot, "PlayerId", playerId);
            SetBool(slot, "HasJoinAction", true);
            SetBool(slot, "DefaultControlSchemeWasNull", true);
            SetString(slot, "DefaultControlScheme", string.Empty);
            InitializeActionBinding(slot.FindPropertyRelative("JoinAction"), CreateJoinAction());

            SetBool(slot, "ControlSchemesWereNull", false);
            SerializedProperty schemes = slot.FindPropertyRelative("ControlSchemes");
            schemes.arraySize = 2;
            InitializeControlScheme(schemes.GetArrayElementAtIndex(0), "KeyboardMouse", "KeyboardMouse", true);
            InitializeControlScheme(schemes.GetArrayElementAtIndex(1), "Gamepad", "Gamepad", false);

            SetBool(slot, "ContextsWereNull", false);
            SerializedProperty contexts = slot.FindPropertyRelative("Contexts");
            contexts.arraySize = 1;
            SerializedProperty context = contexts.GetArrayElementAtIndex(0);
            SetString(context, "Name", string.Empty);
            SetString(context, "Name", GenerateUniqueContextName(newIndex, "Gameplay"));
            SetString(context, "ActionMap", GenerateActionMapName("PlayerActions"));
            SetInt(context, "Priority", 0);
            SetBool(context, "BlocksLowerPriority", true);
            SetBool(context, "BindingsWereNull", false);

            SerializedProperty bindings = context.FindPropertyRelative("Bindings");
            bindings.arraySize = 2;
            InitializeActionBinding(bindings.GetArrayElementAtIndex(0), CreateMoveAction());
            InitializeActionBinding(bindings.GetArrayElementAtIndex(1), CreateConfirmAction());

            if (_serializedConfig.ApplyModifiedProperties())
            {
                MarkValidationDirty();
            }
        }

        private void AddNewBinding(SerializedProperty bindings)
        {
            int newIndex = bindings.arraySize;
            bindings.arraySize++;
            InitializeActionBinding(
                bindings.GetArrayElementAtIndex(newIndex),
                new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "NewAction",
                    ExpectedControlType = "Button",
                    UpdateMode = InputUpdateMode.EventDriven,
                    DeviceBindings = new List<string> { "<Keyboard>/space" },
                    CompositeBindings = null,
                    LongPressValueThreshold = 0.5f
                });
            if (_serializedConfig.ApplyModifiedProperties())
            {
                MarkValidationDirty();
            }
        }

        private static int FindAvailablePlayerId(SerializedProperty slots)
        {
            var used = new HashSet<int>();
            for (int index = 0; index < slots.arraySize; index++)
            {
                SerializedProperty id = slots.GetArrayElementAtIndex(index).FindPropertyRelative("PlayerId");
                if (id != null)
                {
                    used.Add(id.intValue);
                }
            }

            int candidate = 0;
            while (used.Contains(candidate))
            {
                candidate++;
            }
            return candidate;
        }

        private static void InitializeControlScheme(
            SerializedProperty property,
            string name,
            string bindingGroup,
            bool keyboardMouse)
        {
            SetString(property, "Name", name);
            SetString(property, "BindingGroup", bindingGroup);
            SetBool(property, "DeviceRequirementsWereNull", false);
            SerializedProperty requirements = property.FindPropertyRelative("DeviceRequirements");
            requirements.arraySize = keyboardMouse ? 2 : 1;

            InitializeDeviceRequirement(
                requirements.GetArrayElementAtIndex(0),
                keyboardMouse ? "<Keyboard>" : "<Gamepad>",
                false);
            if (keyboardMouse)
            {
                InitializeDeviceRequirement(requirements.GetArrayElementAtIndex(1), "<Mouse>", true);
            }
        }

        private static void InitializeDeviceRequirement(
            SerializedProperty property,
            string controlPath,
            bool optional)
        {
            SetString(property, "ControlPath", controlPath);
            SetBool(property, "IsOptional", optional);
            SetBool(property, "IsOr", false);
        }

        private static void InitializeActionBinding(
            SerializedProperty property,
            ActionBindingConfig model)
        {
            property.FindPropertyRelative("Type").enumValueIndex = (int)model.Type;
            SetString(property, "ActionName", model.ActionName);
            SetNullableString(property, "ExpectedControlType", model.ExpectedControlType);
            SetNullableString(property, "Interactions", model.Interactions);
            SetNullableString(property, "Processors", model.Processors);
            SetNullableString(property, "BindingGroups", model.BindingGroups);
            property.FindPropertyRelative("UpdateMode").enumValueIndex = (int)model.UpdateMode;
            SetInt(property, "LongPressMs", model.LongPressMs);
            property.FindPropertyRelative("LongPressValueThreshold").floatValue = model.LongPressValueThreshold;

            SetBool(property, "DeviceBindingsWereNull", model.DeviceBindings == null);
            SerializedProperty deviceBindings = property.FindPropertyRelative("DeviceBindings");
            deviceBindings.arraySize = model.DeviceBindings?.Count ?? 0;
            for (int index = 0; index < deviceBindings.arraySize; index++)
            {
                deviceBindings.GetArrayElementAtIndex(index).stringValue = model.DeviceBindings[index];
            }

            SetBool(property, "CompositeBindingsWereNull", model.CompositeBindings == null);
            SerializedProperty composites = property.FindPropertyRelative("CompositeBindings");
            composites.arraySize = model.CompositeBindings?.Count ?? 0;
            for (int index = 0; index < composites.arraySize; index++)
            {
                InitializeCompositeBinding(
                    composites.GetArrayElementAtIndex(index),
                    model.CompositeBindings[index]);
            }
        }

        private static void InitializeCompositeBinding(
            SerializedProperty property,
            CompositeBindingConfig model)
        {
            SetString(property, "Name", model.Name);
            SetNullableString(property, "Parameters", model.Parameters);
            SetNullableString(property, "BindingGroups", model.BindingGroups);
            SetBool(property, "PartsWereNull", model.Parts == null);

            SerializedProperty parts = property.FindPropertyRelative("Parts");
            parts.arraySize = model.Parts?.Count ?? 0;
            for (int index = 0; index < parts.arraySize; index++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(index);
                CompositePartBindingConfig partModel = model.Parts[index];
                SetString(part, "Name", partModel.Name);
                SetString(part, "Path", partModel.Path);
                SetNullableString(part, "Processors", partModel.Processors);
                SetNullableString(part, "Interactions", partModel.Interactions);
            }
        }

        private static void SetNullableString(
            SerializedProperty parent,
            string propertyName,
            string value)
        {
            SetString(parent, propertyName, value ?? string.Empty);
            SetBool(parent, propertyName + "WasNull", value == null);
        }

        private static void SetString(SerializedProperty parent, string propertyName, string value)
        {
            SerializedProperty property = parent?.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.stringValue = value ?? string.Empty;
            }
        }

        private static void SetBool(SerializedProperty parent, string propertyName, bool value)
        {
            SerializedProperty property = parent?.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetInt(SerializedProperty parent, string propertyName, int value)
        {
            SerializedProperty property = parent?.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }
    }
}
