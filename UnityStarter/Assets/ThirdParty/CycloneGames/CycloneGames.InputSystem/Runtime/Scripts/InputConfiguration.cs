using System.Collections.Generic;
using VYaml.Annotations;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Explicit value type for an input action. This removes brittle heuristics and
    /// enables zero-GC routing with precise action wiring.
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

        [YamlMember("contexts")]
        public List<ContextDefinitionConfig> Contexts { get; set; }
    }

    /// <summary>
    /// Defines a single input context, its associated action map, and its action bindings.
    /// </summary>
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

    /// <summary>
    /// Defines an action and its corresponding raw device input paths.
    /// </summary>
    [YamlObject]
    public partial class ActionBindingConfig
    {
        [YamlMember("type")]
        public ActionValueType Type { get; set; }

        [YamlMember("action")]
        public string ActionName { get; set; }
        
        [YamlMember("deviceBindings")]
        public List<string> DeviceBindings { get; set; }
    }
}