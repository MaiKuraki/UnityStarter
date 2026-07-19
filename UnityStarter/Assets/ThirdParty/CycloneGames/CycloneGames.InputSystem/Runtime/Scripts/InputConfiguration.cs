using System.Collections.Generic;
using VYaml.Annotations;

namespace CycloneGames.InputSystem.Runtime
{
    public enum InputDeviceKind
    {
        Unknown,
        KeyboardMouse,
        Gamepad,
        Touchscreen,
        Other
    }

    public enum ActionValueType
    {
        Button,
        Vector2,
        Float
    }

    /// <summary>
    /// Input update mode: Event-driven (only on value change) or Polling (every frame).
    /// Polling is recommended for continuous inputs like mouse delta or analog sticks.
    /// </summary>
    public enum InputUpdateMode
    {
        EventDriven,  // Only trigger when value changes (default, efficient for buttons/discrete inputs)
        Polling       // Poll every frame (required for smooth continuous inputs like mouse delta)
    }

    /// <summary>
    /// Represents the root of the YAML input configuration file.
    /// </summary>
    [YamlObject]
    public partial class InputConfiguration
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// Explicit persisted schema version. A missing value deserializes as schema zero and is migrated during validation.
        /// </summary>
        [YamlMember("schemaVersion")]
        public int SchemaVersion { get; set; }

        /// <summary>
        /// Optional Editor diagnostic stamp. Runtime compatibility is governed exclusively by SchemaVersion
        /// and InputConfigurationValidator.
        /// </summary>
        [YamlMember("schemaFingerprint")]
        public string SchemaFingerprint { get; set; }

        // A template configuration for each possible player slot.
        [YamlMember("playerSlots")]
        public List<PlayerSlotConfig> PlayerSlots { get; set; }

        // A special action definition used to listen for players wanting to join.
        [YamlMember("joinAction")]
        public ActionBindingConfig JoinAction { get; set; }
    }

    /// <summary>
    /// Defines the input setup template for a player joining in a specific slot.
    /// </summary>
    [YamlObject]
    public partial class PlayerSlotConfig
    {
        [YamlMember("playerId")]
        public int PlayerId { get; set; }

        [YamlMember("joinAction")]
        public ActionBindingConfig JoinAction { get; set; }

        [YamlMember("contexts")]
        public List<ContextDefinitionConfig> Contexts { get; set; }

        /// <summary>
        /// Optional Unity Input System control schemes used for deterministic device matching.
        /// </summary>
        [YamlMember("controlSchemes")]
        public List<ControlSchemeConfig> ControlSchemes { get; set; }

        /// <summary>
        /// Optional preferred control scheme name. Null selects the best successful match.
        /// </summary>
        [YamlMember("defaultControlScheme")]
        public string DefaultControlScheme { get; set; }
    }

    [YamlObject]
    public partial class ContextDefinitionConfig
    {
        [YamlMember("name")]
        public string Name { get; set; }

        [YamlMember("actionMap")]
        public string ActionMap { get; set; }

        [YamlMember("bindings")]
        public List<ActionBindingConfig> Bindings { get; set; }

        /// <summary>
        /// Higher-priority contexts are evaluated before lower-priority contexts.
        /// </summary>
        [YamlMember("priority")]
        public int Priority { get; set; }

        /// <summary>
        /// Stops lower-priority contexts from receiving input while this context is active.
        /// The default is an exclusive context.
        /// </summary>
        [YamlMember("blocksLowerPriority")]
        public bool BlocksLowerPriority { get; set; } = true;
    }

    [YamlObject]
    public partial class ActionBindingConfig
    {
        [YamlMember("type")]
        public ActionValueType Type { get; set; }

        [YamlMember("action")]
        public string ActionName { get; set; }

        [YamlMember("deviceBindings")]
        public List<string> DeviceBindings { get; set; }

        /// <summary>
        /// Explicit Unity Input System composite bindings. Direct bindings remain available through deviceBindings.
        /// </summary>
        [YamlMember("compositeBindings")]
        public List<CompositeBindingConfig> CompositeBindings { get; set; }

        /// <summary>
        /// Optional Unity Input System expected control layout, for example "Vector2" or "Button".
        /// </summary>
        [YamlMember("expectedControlType")]
        public string ExpectedControlType { get; set; }

        /// <summary>
        /// Optional Unity Input System interaction expression.
        /// </summary>
        [YamlMember("interactions")]
        public string Interactions { get; set; }

        /// <summary>
        /// Optional Unity Input System processor expression.
        /// </summary>
        [YamlMember("processors")]
        public string Processors { get; set; }

        /// <summary>
        /// Optional semicolon-separated Unity Input System binding groups.
        /// </summary>
        [YamlMember("bindingGroups")]
        public string BindingGroups { get; set; }

        /// <summary>
        /// Requested input update mode. Vector2 and Float actions with a direct or composite-part
        /// "/delta" path are always polled so frame-relative values are emitted consistently.
        /// </summary>
        [YamlMember("updateMode")]
        public InputUpdateMode UpdateMode { get; set; } = InputUpdateMode.EventDriven;

        /// <summary>
        /// Long-press duration in milliseconds. When > 0, emits separate long-press event.
        /// </summary>
        [YamlMember("longPressMs")]
        public int LongPressMs { get; set; }

        /// <summary>
        /// For Float actions: actuation threshold (0-1) for long-press timing. Default 0.5.
        /// </summary>
        [YamlMember("longPressValueThreshold")]
        public float LongPressValueThreshold { get; set; } = 0.5f;
    }

    [YamlObject]
    public partial class ControlSchemeConfig
    {
        [YamlMember("name")]
        public string Name { get; set; }

        [YamlMember("bindingGroup")]
        public string BindingGroup { get; set; }

        [YamlMember("deviceRequirements")]
        public List<ControlSchemeDeviceRequirementConfig> DeviceRequirements { get; set; }
    }

    [YamlObject]
    public partial class ControlSchemeDeviceRequirementConfig
    {
        [YamlMember("controlPath")]
        public string ControlPath { get; set; }

        [YamlMember("isOptional")]
        public bool IsOptional { get; set; }

        [YamlMember("isOr")]
        public bool IsOr { get; set; }
    }

    [YamlObject]
    public partial class CompositeBindingConfig
    {
        [YamlMember("name")]
        public string Name { get; set; }

        /// <summary>
        /// Optional parameter expression without parentheses, for example "mode=2".
        /// </summary>
        [YamlMember("parameters")]
        public string Parameters { get; set; }

        [YamlMember("bindingGroups")]
        public string BindingGroups { get; set; }

        [YamlMember("parts")]
        public List<CompositePartBindingConfig> Parts { get; set; }
    }

    [YamlObject]
    public partial class CompositePartBindingConfig
    {
        [YamlMember("name")]
        public string Name { get; set; }

        [YamlMember("path")]
        public string Path { get; set; }

        [YamlMember("processors")]
        public string Processors { get; set; }

        [YamlMember("interactions")]
        public string Interactions { get; set; }
    }
}
