using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using CycloneGames.Utility.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    internal static class InputConfigurationDrawerNullUtility
    {
        internal static string RestoreNullableString(bool wasNull, string value)
        {
            return wasNull && string.IsNullOrEmpty(value) ? null : value;
        }
    }

    [Serializable]
    public class CompositePartBindingDrawerData
    {
        public string Name;
        public string Path;
        public string Processors;
        [HideInInspector] public bool ProcessorsWasNull;
        public string Interactions;
        [HideInInspector] public bool InteractionsWasNull;

        public void FromData(CompositePartBindingConfig data)
        {
            Name = data.Name;
            Path = data.Path;
            Processors = data.Processors;
            ProcessorsWasNull = data.Processors == null;
            Interactions = data.Interactions;
            InteractionsWasNull = data.Interactions == null;
        }

        public CompositePartBindingConfig ToData()
        {
            return new CompositePartBindingConfig
            {
                Name = Name,
                Path = Path,
                Processors = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    ProcessorsWasNull,
                    Processors),
                Interactions = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    InteractionsWasNull,
                    Interactions)
            };
        }
    }

    [Serializable]
    public class CompositeBindingDrawerData
    {
        public string Name;
        public string Parameters;
        [HideInInspector] public bool ParametersWasNull;
        public string BindingGroups;
        [HideInInspector] public bool BindingGroupsWasNull;
        public List<CompositePartBindingDrawerData> Parts = new List<CompositePartBindingDrawerData>();
        [HideInInspector] public bool PartsWereNull;

        public void FromData(CompositeBindingConfig data)
        {
            Name = data.Name;
            Parameters = data.Parameters;
            ParametersWasNull = data.Parameters == null;
            BindingGroups = data.BindingGroups;
            BindingGroupsWasNull = data.BindingGroups == null;
            PartsWereNull = data.Parts == null;
            Parts = new List<CompositePartBindingDrawerData>();
            if (data.Parts == null)
            {
                return;
            }

            for (int index = 0; index < data.Parts.Count; index++)
            {
                CompositePartBindingConfig part = data.Parts[index];
                if (part == null)
                {
                    Parts.Add(null);
                    continue;
                }

                var drawer = new CompositePartBindingDrawerData();
                drawer.FromData(part);
                Parts.Add(drawer);
            }
        }

        public CompositeBindingConfig ToData()
        {
            return new CompositeBindingConfig
            {
                Name = Name,
                Parameters = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    ParametersWasNull,
                    Parameters),
                BindingGroups = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    BindingGroupsWasNull,
                    BindingGroups),
                Parts = ToParts()
            };
        }

        private List<CompositePartBindingConfig> ToParts()
        {
            if (Parts == null || (PartsWereNull && Parts.Count == 0))
            {
                return null;
            }

            var result = new List<CompositePartBindingConfig>(Parts.Count);
            for (int index = 0; index < Parts.Count; index++)
            {
                result.Add(Parts[index]?.ToData());
            }
            return result;
        }
    }

    [Serializable]
    public class ActionBindingDrawerData
    {
        [Tooltip("Required value shape exposed by this action: Button, Float, Vector2, Vector3, Quaternion, or Integer.")]
        public ActionValueType Type;
        [Tooltip("Required stable action identity used by runtime lookup and generated constants, for example Confirm or Move.")]
        public string ActionName;
        [Tooltip("Optional Unity Input System control layout expected from the binding, for example Button or Vector2. Leave empty when no extra layout constraint is needed.")]
        public string ExpectedControlType;
        [HideInInspector] public bool ExpectedControlTypeWasNull;
        [Tooltip("Optional Unity Input System interaction expression, for example Tap or Hold(duration=0.5). Leave empty for the control's default actuation behavior.")]
        public string Interactions;
        [HideInInspector] public bool InteractionsWasNull;
        [Tooltip("Optional Unity Input System processor expression, for example NormalizeVector2 or Scale(factor=2). Leave empty when the raw value is desired.")]
        public string Processors;
        [HideInInspector] public bool ProcessorsWasNull;
        [Tooltip("Optional semicolon-separated control-scheme binding groups, for example KeyboardMouse;Gamepad. Leave empty to make the binding available to every compatible scheme.")]
        public string BindingGroups;
        [HideInInspector] public bool BindingGroupsWasNull;
        [Tooltip("EventDriven publishes value changes; Polling samples the action from the configured frame provider.")]
        public InputUpdateMode UpdateMode = InputUpdateMode.EventDriven;

        [Tooltip("Direct Unity Input System control paths. Type any valid path or use the picker for known constants.")]
        [StringAsConstSelector(typeof(InputBindingConstants), UseMenu = true, AllowCustom = true)]
        public List<string> DeviceBindings = new List<string>();
        [HideInInspector] public bool DeviceBindingsWereNull;

        [Tooltip("Optional composite bindings such as a keyboard 2DVector. Direct and composite bindings may coexist.")]
        public List<CompositeBindingDrawerData> CompositeBindings = new List<CompositeBindingDrawerData>();
        [HideInInspector] public bool CompositeBindingsWereNull;

        [Tooltip("Optional long-press duration in milliseconds for Button or Float actions. Use 0 to disable module-level long-press tracking.")]
        [Min(0)]
        public int LongPressMs;

        [Range(0f, 1f)]
        [Tooltip("Actuation threshold used by module-level long-press timing, especially for Float actions.")]
        public float LongPressValueThreshold = 0.5f;

        public void FromData(ActionBindingConfig data)
        {
            Type = data.Type;
            ActionName = data.ActionName;
            ExpectedControlType = data.ExpectedControlType;
            ExpectedControlTypeWasNull = data.ExpectedControlType == null;
            Interactions = data.Interactions;
            InteractionsWasNull = data.Interactions == null;
            Processors = data.Processors;
            ProcessorsWasNull = data.Processors == null;
            BindingGroups = data.BindingGroups;
            BindingGroupsWasNull = data.BindingGroups == null;
            UpdateMode = data.UpdateMode;
            LongPressMs = data.LongPressMs;
            LongPressValueThreshold = data.LongPressValueThreshold;

            DeviceBindingsWereNull = data.DeviceBindings == null;
            DeviceBindings = data.DeviceBindings == null
                ? new List<string>()
                : new List<string>(data.DeviceBindings);

            CompositeBindingsWereNull = data.CompositeBindings == null;
            CompositeBindings = new List<CompositeBindingDrawerData>();
            if (data.CompositeBindings == null)
            {
                return;
            }

            for (int index = 0; index < data.CompositeBindings.Count; index++)
            {
                CompositeBindingConfig composite = data.CompositeBindings[index];
                if (composite == null)
                {
                    CompositeBindings.Add(null);
                    continue;
                }

                var drawer = new CompositeBindingDrawerData();
                drawer.FromData(composite);
                CompositeBindings.Add(drawer);
            }
        }

        public ActionBindingConfig ToData()
        {
            return new ActionBindingConfig
            {
                Type = Type,
                ActionName = ActionName,
                ExpectedControlType = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    ExpectedControlTypeWasNull,
                    ExpectedControlType),
                Interactions = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    InteractionsWasNull,
                    Interactions),
                Processors = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    ProcessorsWasNull,
                    Processors),
                BindingGroups = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    BindingGroupsWasNull,
                    BindingGroups),
                UpdateMode = UpdateMode,
                DeviceBindings = DeviceBindings == null ||
                                 (DeviceBindingsWereNull && DeviceBindings.Count == 0)
                    ? null
                    : new List<string>(DeviceBindings),
                CompositeBindings = ToCompositeBindings(),
                LongPressMs = LongPressMs,
                LongPressValueThreshold = LongPressValueThreshold
            };
        }

        private List<CompositeBindingConfig> ToCompositeBindings()
        {
            if (CompositeBindings == null ||
                (CompositeBindingsWereNull && CompositeBindings.Count == 0))
            {
                return null;
            }

            var result = new List<CompositeBindingConfig>(CompositeBindings.Count);
            for (int index = 0; index < CompositeBindings.Count; index++)
            {
                result.Add(CompositeBindings[index]?.ToData());
            }
            return result;
        }
    }

    [Serializable]
    public class ContextDefinitionDrawerData
    {
        public string Name;
        public string ActionMap;
        public int Priority;
        public bool BlocksLowerPriority = true;
        public List<ActionBindingDrawerData> Bindings = new List<ActionBindingDrawerData>();
        [HideInInspector] public bool BindingsWereNull;

        public void FromData(ContextDefinitionConfig data)
        {
            Name = data.Name;
            ActionMap = data.ActionMap;
            Priority = data.Priority;
            BlocksLowerPriority = data.BlocksLowerPriority;
            BindingsWereNull = data.Bindings == null;
            Bindings = new List<ActionBindingDrawerData>();
            if (data.Bindings == null)
            {
                return;
            }

            for (int index = 0; index < data.Bindings.Count; index++)
            {
                ActionBindingConfig binding = data.Bindings[index];
                if (binding == null)
                {
                    Bindings.Add(null);
                    continue;
                }

                var drawer = new ActionBindingDrawerData();
                drawer.FromData(binding);
                Bindings.Add(drawer);
            }
        }

        public ContextDefinitionConfig ToData()
        {
            return new ContextDefinitionConfig
            {
                Name = Name,
                ActionMap = ActionMap,
                Priority = Priority,
                BlocksLowerPriority = BlocksLowerPriority,
                Bindings = ToBindings()
            };
        }

        private List<ActionBindingConfig> ToBindings()
        {
            if (Bindings == null || (BindingsWereNull && Bindings.Count == 0))
            {
                return null;
            }

            var result = new List<ActionBindingConfig>(Bindings.Count);
            for (int index = 0; index < Bindings.Count; index++)
            {
                result.Add(Bindings[index]?.ToData());
            }
            return result;
        }
    }

    [Serializable]
    public class ControlSchemeDeviceRequirementDrawerData
    {
        public string ControlPath;
        public bool IsOptional;
        public bool IsOr;

        public void FromData(ControlSchemeDeviceRequirementConfig data)
        {
            ControlPath = data.ControlPath;
            IsOptional = data.IsOptional;
            IsOr = data.IsOr;
        }

        public ControlSchemeDeviceRequirementConfig ToData()
        {
            return new ControlSchemeDeviceRequirementConfig
            {
                ControlPath = ControlPath,
                IsOptional = IsOptional,
                IsOr = IsOr
            };
        }
    }

    [Serializable]
    public class ControlSchemeDrawerData
    {
        public string Name;
        public string BindingGroup;
        public List<ControlSchemeDeviceRequirementDrawerData> DeviceRequirements =
            new List<ControlSchemeDeviceRequirementDrawerData>();
        [HideInInspector] public bool DeviceRequirementsWereNull;

        public void FromData(ControlSchemeConfig data)
        {
            Name = data.Name;
            BindingGroup = data.BindingGroup;
            DeviceRequirementsWereNull = data.DeviceRequirements == null;
            DeviceRequirements = new List<ControlSchemeDeviceRequirementDrawerData>();
            if (data.DeviceRequirements == null)
            {
                return;
            }

            for (int index = 0; index < data.DeviceRequirements.Count; index++)
            {
                ControlSchemeDeviceRequirementConfig requirement = data.DeviceRequirements[index];
                if (requirement == null)
                {
                    DeviceRequirements.Add(null);
                    continue;
                }

                var drawer = new ControlSchemeDeviceRequirementDrawerData();
                drawer.FromData(requirement);
                DeviceRequirements.Add(drawer);
            }
        }

        public ControlSchemeConfig ToData()
        {
            return new ControlSchemeConfig
            {
                Name = Name,
                BindingGroup = BindingGroup,
                DeviceRequirements = ToDeviceRequirements()
            };
        }

        private List<ControlSchemeDeviceRequirementConfig> ToDeviceRequirements()
        {
            if (DeviceRequirements == null ||
                (DeviceRequirementsWereNull && DeviceRequirements.Count == 0))
            {
                return null;
            }

            var result = new List<ControlSchemeDeviceRequirementConfig>(DeviceRequirements.Count);
            for (int index = 0; index < DeviceRequirements.Count; index++)
            {
                result.Add(DeviceRequirements[index]?.ToData());
            }
            return result;
        }
    }

    [Serializable]
    public class PlayerSlotDrawerData
    {
        public int PlayerId;
        public bool HasJoinAction = true;
        public ActionBindingDrawerData JoinAction = new ActionBindingDrawerData();
        [Tooltip("Optional preferred control-scheme name. Leave empty to select the best compatible declared scheme at join time.")]
        public string DefaultControlScheme;
        [HideInInspector] public bool DefaultControlSchemeWasNull;
        public List<ControlSchemeDrawerData> ControlSchemes = new List<ControlSchemeDrawerData>();
        [HideInInspector] public bool ControlSchemesWereNull;
        public List<ContextDefinitionDrawerData> Contexts = new List<ContextDefinitionDrawerData>();
        [HideInInspector] public bool ContextsWereNull;

        public void FromData(PlayerSlotConfig data)
        {
            PlayerId = data.PlayerId;
            HasJoinAction = data.JoinAction != null;
            JoinAction = new ActionBindingDrawerData();
            if (data.JoinAction != null)
            {
                JoinAction.FromData(data.JoinAction);
            }

            DefaultControlScheme = data.DefaultControlScheme;
            DefaultControlSchemeWasNull = data.DefaultControlScheme == null;
            ControlSchemesWereNull = data.ControlSchemes == null;
            ControlSchemes = new List<ControlSchemeDrawerData>();
            if (data.ControlSchemes != null)
            {
                for (int index = 0; index < data.ControlSchemes.Count; index++)
                {
                    ControlSchemeConfig scheme = data.ControlSchemes[index];
                    if (scheme == null)
                    {
                        ControlSchemes.Add(null);
                        continue;
                    }

                    var drawer = new ControlSchemeDrawerData();
                    drawer.FromData(scheme);
                    ControlSchemes.Add(drawer);
                }
            }

            ContextsWereNull = data.Contexts == null;
            Contexts = new List<ContextDefinitionDrawerData>();
            if (data.Contexts == null)
            {
                return;
            }

            for (int index = 0; index < data.Contexts.Count; index++)
            {
                ContextDefinitionConfig context = data.Contexts[index];
                if (context == null)
                {
                    Contexts.Add(null);
                    continue;
                }

                var drawer = new ContextDefinitionDrawerData();
                drawer.FromData(context);
                Contexts.Add(drawer);
            }
        }

        public PlayerSlotConfig ToData()
        {
            return new PlayerSlotConfig
            {
                PlayerId = PlayerId,
                JoinAction = HasJoinAction ? JoinAction?.ToData() : null,
                DefaultControlScheme = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    DefaultControlSchemeWasNull,
                    DefaultControlScheme),
                ControlSchemes = ToControlSchemes(),
                Contexts = ToContexts()
            };
        }

        private List<ControlSchemeConfig> ToControlSchemes()
        {
            if (ControlSchemes == null ||
                (ControlSchemesWereNull && ControlSchemes.Count == 0))
            {
                return null;
            }

            var result = new List<ControlSchemeConfig>(ControlSchemes.Count);
            for (int index = 0; index < ControlSchemes.Count; index++)
            {
                result.Add(ControlSchemes[index]?.ToData());
            }
            return result;
        }

        private List<ContextDefinitionConfig> ToContexts()
        {
            if (Contexts == null || (ContextsWereNull && Contexts.Count == 0))
            {
                return null;
            }

            var result = new List<ContextDefinitionConfig>(Contexts.Count);
            for (int index = 0; index < Contexts.Count; index++)
            {
                result.Add(Contexts[index]?.ToData());
            }
            return result;
        }
    }

#pragma warning disable 0618
    [Serializable]
    [Obsolete("Use ActionBindingDrawerData. This compatibility type will be removed in a future major version.")]
    public class ActionBindingSO : ActionBindingDrawerData
    {
    }

    [Obsolete("Use ContextDefinitionDrawerData for serialized Editor working data.")]
    public class ContextDefinitionSO : ScriptableObject
    {
        public string Name;
        public string ActionMap;
        public int Priority;
        public bool BlocksLowerPriority = true;
        public List<ActionBindingSO> Bindings = new List<ActionBindingSO>();
        [HideInInspector] public bool BindingsWereNull;

        public void FromData(ContextDefinitionConfig data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Name = data.Name;
            ActionMap = data.ActionMap;
            Priority = data.Priority;
            BlocksLowerPriority = data.BlocksLowerPriority;
            BindingsWereNull = data.Bindings == null;
            Bindings = new List<ActionBindingSO>();
            if (data.Bindings == null) return;
            for (int index = 0; index < data.Bindings.Count; index++)
            {
                ActionBindingConfig binding = data.Bindings[index];
                if (binding == null)
                {
                    Bindings.Add(null);
                    continue;
                }

                var legacy = new ActionBindingSO();
                legacy.FromData(binding);
                Bindings.Add(legacy);
            }
        }

        public ContextDefinitionConfig ToData()
        {
            List<ActionBindingConfig> bindings = null;
            if (Bindings != null && (!BindingsWereNull || Bindings.Count > 0))
            {
                bindings = new List<ActionBindingConfig>(Bindings.Count);
                for (int index = 0; index < Bindings.Count; index++)
                    bindings.Add(Bindings[index]?.ToData());
            }

            return new ContextDefinitionConfig
            {
                Name = Name,
                ActionMap = ActionMap,
                Priority = Priority,
                BlocksLowerPriority = BlocksLowerPriority,
                Bindings = bindings
            };
        }
    }

    [Serializable]
    [Obsolete("Use PlayerSlotDrawerData for serialized Editor working data.")]
    public class PlayerSlotSO : ScriptableObject
    {
        public int PlayerId;
        public bool HasJoinAction;
        public ActionBindingSO JoinAction = new ActionBindingSO();
        public string DefaultControlScheme;
        [HideInInspector] public bool DefaultControlSchemeWasNull = true;
        public List<ControlSchemeDrawerData> ControlSchemes = new List<ControlSchemeDrawerData>();
        [HideInInspector] public bool ControlSchemesWereNull = true;
        public List<ContextDefinitionSO> Contexts = new List<ContextDefinitionSO>();
        [HideInInspector] public bool ContextsWereNull;

        public void FromData(PlayerSlotConfig data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            PlayerId = data.PlayerId;
            HasJoinAction = data.JoinAction != null;
            JoinAction = new ActionBindingSO();
            if (data.JoinAction != null) JoinAction.FromData(data.JoinAction);
            DefaultControlSchemeWasNull = data.DefaultControlScheme == null;
            DefaultControlScheme = data.DefaultControlScheme;
            ControlSchemesWereNull = data.ControlSchemes == null;
            ControlSchemes = new List<ControlSchemeDrawerData>();
            if (data.ControlSchemes != null)
            {
                for (int index = 0; index < data.ControlSchemes.Count; index++)
                {
                    ControlSchemeConfig scheme = data.ControlSchemes[index];
                    if (scheme == null)
                    {
                        ControlSchemes.Add(null);
                        continue;
                    }

                    var legacyScheme = new ControlSchemeDrawerData();
                    legacyScheme.FromData(scheme);
                    ControlSchemes.Add(legacyScheme);
                }
            }

            ContextsWereNull = data.Contexts == null;
            Contexts = new List<ContextDefinitionSO>();
            if (data.Contexts == null) return;
            for (int index = 0; index < data.Contexts.Count; index++)
            {
                ContextDefinitionConfig context = data.Contexts[index];
                if (context == null)
                {
                    Contexts.Add(null);
                    continue;
                }

                ContextDefinitionSO legacy = CreateInstance<ContextDefinitionSO>();
                legacy.FromData(context);
                Contexts.Add(legacy);
            }
        }

        public PlayerSlotConfig ToData()
        {
            List<ContextDefinitionConfig> contexts = null;
            if (Contexts != null && (!ContextsWereNull || Contexts.Count > 0))
            {
                contexts = new List<ContextDefinitionConfig>(Contexts.Count);
                for (int index = 0; index < Contexts.Count; index++)
                    contexts.Add(Contexts[index]?.ToData());
            }

            List<ControlSchemeConfig> controlSchemes = null;
            if (ControlSchemes != null && (!ControlSchemesWereNull || ControlSchemes.Count > 0))
            {
                controlSchemes = new List<ControlSchemeConfig>(ControlSchemes.Count);
                for (int index = 0; index < ControlSchemes.Count; index++)
                    controlSchemes.Add(ControlSchemes[index]?.ToData());
            }

            return new PlayerSlotConfig
            {
                PlayerId = PlayerId,
                JoinAction = HasJoinAction ? JoinAction?.ToData() : null,
                DefaultControlScheme = DefaultControlSchemeWasNull ? null : DefaultControlScheme,
                ControlSchemes = controlSchemes,
                Contexts = contexts
            };
        }
    }
#pragma warning restore 0618

    public class InputConfigurationSO : ScriptableObject, ISerializationCallbackReceiver
    {
        private const int CurrentEditorDataVersion = 1;

        [SerializeField, HideInInspector] private int _editorDataVersion;
        [SerializeField, HideInInspector] private int _schemaVersion;
        [SerializeField, HideInInspector] private string _schemaFingerprint;
        [SerializeField, HideInInspector] private bool _schemaFingerprintWasNull;
        [SerializeField] private bool _hasJoinAction = true;
        [SerializeField] private ActionBindingDrawerData _joinAction = new ActionBindingDrawerData();
        [SerializeField, HideInInspector] private bool _playerSlotsWereNull;
        [SerializeField] private List<PlayerSlotDrawerData> _playerSlots = new List<PlayerSlotDrawerData>();

        public void FromData(InputConfiguration data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            _editorDataVersion = CurrentEditorDataVersion;
            _schemaVersion = data.SchemaVersion;
            _schemaFingerprint = data.SchemaFingerprint;
            _schemaFingerprintWasNull = data.SchemaFingerprint == null;
            _hasJoinAction = data.JoinAction != null;
            _joinAction = new ActionBindingDrawerData();
            if (data.JoinAction != null)
            {
                _joinAction.FromData(data.JoinAction);
            }

            _playerSlotsWereNull = data.PlayerSlots == null;
            _playerSlots = new List<PlayerSlotDrawerData>();
            if (data.PlayerSlots == null)
            {
                return;
            }

            for (int index = 0; index < data.PlayerSlots.Count; index++)
            {
                PlayerSlotConfig player = data.PlayerSlots[index];
                if (player == null)
                {
                    _playerSlots.Add(null);
                    continue;
                }

                var drawer = new PlayerSlotDrawerData();
                drawer.FromData(player);
                _playerSlots.Add(drawer);
            }
        }

        public InputConfiguration ToData()
        {
            EnsureSerializedDataMigrated();
            return new InputConfiguration
            {
                SchemaVersion = _schemaVersion,
                SchemaFingerprint = InputConfigurationDrawerNullUtility.RestoreNullableString(
                    _schemaFingerprintWasNull,
                    _schemaFingerprint),
                JoinAction = _hasJoinAction ? _joinAction?.ToData() : null,
                PlayerSlots = ToPlayerSlots()
            };
        }

        private List<PlayerSlotConfig> ToPlayerSlots()
        {
            if (_playerSlots == null || (_playerSlotsWereNull && _playerSlots.Count == 0))
            {
                return null;
            }

            var result = new List<PlayerSlotConfig>(_playerSlots.Count);
            for (int index = 0; index < _playerSlots.Count; index++)
            {
                result.Add(_playerSlots[index]?.ToData());
            }
            return result;
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            EnsureSerializedDataMigrated();
        }

        private void EnsureSerializedDataMigrated()
        {
            if (_editorDataVersion >= CurrentEditorDataVersion) return;
            _hasJoinAction = _joinAction != null;
            if (_playerSlots != null)
            {
                for (int index = 0; index < _playerSlots.Count; index++)
                {
                    PlayerSlotDrawerData player = _playerSlots[index];
                    if (player != null) player.HasJoinAction = player.JoinAction != null;
                }
            }

            _editorDataVersion = CurrentEditorDataVersion;
        }
    }

    [CustomPropertyDrawer(typeof(ActionBindingDrawerData))]
    internal sealed class ActionBindingDrawerDataPropertyDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        private static readonly GUIContent TypeLabel = new GUIContent(
            "Type",
            "Required value shape exposed by this action.");
        private static readonly GUIContent ActionNameLabel = new GUIContent(
            "Action Name",
            "Required stable runtime identity, for example Confirm or Move.");
        private static readonly GUIContent UpdateModeLabel = new GUIContent(
            "Update Mode",
            "EventDriven publishes changes; Polling samples from the configured frame provider.");
        private static readonly GUIContent DeviceBindingsLabel = new GUIContent(
            "Device Bindings",
            "Direct Unity Input System control paths. Values can be typed or selected from the picker.");
        private static readonly GUIContent CompositeBindingsLabel = new GUIContent(
            "Composite Bindings",
            "Optional composite bindings such as a keyboard 2DVector.");
        private static readonly GUIContent LongPressMsLabel = new GUIContent(
            "Long Press Ms",
            "Optional module-level long-press duration in milliseconds. Zero disables it.");
        private static readonly GUIContent LongPressThresholdLabel = new GUIContent(
            "Press Threshold",
            "Actuation threshold used by module-level long-press timing.");
        private static readonly GUIContent AdvancedLabel = new GUIContent(
            "Advanced Options",
            "Optional Input System metadata. Most bindings can leave these values empty.");
        private static readonly GUIContent ExpectedControlLabel = new GUIContent(
            "Expected Type",
            "Optional expected control layout, for example Button or Vector2.");
        private static readonly GUIContent InteractionsLabel = new GUIContent(
            "Interactions",
            "Optional Input System expression, for example Tap or Hold(duration=0.5).");
        private static readonly GUIContent ProcessorsLabel = new GUIContent(
            "Processors",
            "Optional Input System expression, for example NormalizeVector2 or Scale(factor=2).");
        private static readonly GUIContent BindingGroupsLabel = new GUIContent(
            "Scheme Groups",
            "Optional semicolon-separated scheme groups, for example KeyboardMouse;Gamepad.");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float y = position.y;
            Rect foldoutRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            y += EditorGUIUtility.singleLineHeight + Spacing;

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawProperty(position, ref y, property.FindPropertyRelative("Type"), TypeLabel);
                DrawProperty(position, ref y, property.FindPropertyRelative("ActionName"), ActionNameLabel);
                DrawProperty(position, ref y, property.FindPropertyRelative("UpdateMode"), UpdateModeLabel);
                DrawProperty(position, ref y, property.FindPropertyRelative("DeviceBindings"), DeviceBindingsLabel, true);
                DrawProperty(position, ref y, property.FindPropertyRelative("CompositeBindings"), CompositeBindingsLabel, true);
                DrawProperty(position, ref y, property.FindPropertyRelative("LongPressMs"), LongPressMsLabel);
                DrawProperty(
                    position,
                    ref y,
                    property.FindPropertyRelative("LongPressValueThreshold"),
                    LongPressThresholdLabel);

                SerializedProperty advancedState = property.FindPropertyRelative("ExpectedControlType");
                Rect advancedRect = new Rect(
                    position.x,
                    y,
                    position.width,
                    EditorGUIUtility.singleLineHeight);
                advancedState.isExpanded = EditorGUI.Foldout(
                    advancedRect,
                    advancedState.isExpanded,
                    AdvancedLabel,
                    true);
                y += EditorGUIUtility.singleLineHeight + Spacing;
                if (advancedState.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    DrawProperty(position, ref y, advancedState, ExpectedControlLabel);
                    DrawProperty(position, ref y, property.FindPropertyRelative("Interactions"), InteractionsLabel);
                    DrawProperty(position, ref y, property.FindPropertyRelative("Processors"), ProcessorsLabel);
                    DrawProperty(position, ref y, property.FindPropertyRelative("BindingGroups"), BindingGroupsLabel);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;

            height += Spacing;
            height += GetPropertyHeight(property.FindPropertyRelative("Type"));
            height += GetPropertyHeight(property.FindPropertyRelative("ActionName"));
            height += GetPropertyHeight(property.FindPropertyRelative("UpdateMode"));
            height += GetPropertyHeight(property.FindPropertyRelative("DeviceBindings"), true);
            height += GetPropertyHeight(property.FindPropertyRelative("CompositeBindings"), true);
            height += GetPropertyHeight(property.FindPropertyRelative("LongPressMs"));
            height += GetPropertyHeight(property.FindPropertyRelative("LongPressValueThreshold"));
            height += EditorGUIUtility.singleLineHeight + Spacing;

            SerializedProperty advancedState = property.FindPropertyRelative("ExpectedControlType");
            if (advancedState.isExpanded)
            {
                height += GetPropertyHeight(advancedState);
                height += GetPropertyHeight(property.FindPropertyRelative("Interactions"));
                height += GetPropertyHeight(property.FindPropertyRelative("Processors"));
                height += GetPropertyHeight(property.FindPropertyRelative("BindingGroups"));
            }
            return height;
        }

        private static void DrawProperty(
            Rect outer,
            ref float y,
            SerializedProperty property,
            GUIContent label,
            bool includeChildren = false)
        {
            if (property == null) return;
            float height = EditorGUI.GetPropertyHeight(property, label, includeChildren);
            EditorGUI.PropertyField(
                new Rect(outer.x, y, outer.width, height),
                property,
                label,
                includeChildren);
            y += height + Spacing;
        }

        private static float GetPropertyHeight(SerializedProperty property, bool includeChildren = false)
        {
            return property == null
                ? 0f
                : EditorGUI.GetPropertyHeight(property, includeChildren) + Spacing;
        }
    }
}
