using System.Collections.Generic;
using VYaml.Annotations;

namespace CycloneGames.InputSystem.Runtime
{
    public enum InputDeviceKind
    {
        Unknown,
        KeyboardMouse,
        Gamepad,
        Other
    }
    /// <summary>
    /// Explicit action value type for zero-GC routing and precise action wiring.
    /// </summary>
    public enum ActionValueType
    {
        Button,
        Vector2,
        Float
    }

    /// <summary>
    /// Represents the root of the YAML input configuration file.
    /// </summary>
    [YamlObject]
    public partial class InputConfiguration
    {
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
        /// Long-press duration in milliseconds. When > 0, emits separate long-press event.
        /// </summary>
        [YamlMember("longPressMs")]
        public int LongPressMs { get; set; }

        /// <summary>
        /// For Float actions: actuation threshold (0-1) for long-press timing. Default 0.5.
        /// </summary>
        [YamlMember("longPressValueThreshold")]
        public float LongPressValueThreshold { get; set; }
    }
}