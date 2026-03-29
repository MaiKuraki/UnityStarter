using System;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Represents a single action available on an interactable object.
    /// Supports multi-action prompts like "E: Pick Up / F: Examine / Hold: Disassemble".
    /// </summary>
    [Serializable]
    public struct InteractionAction : IEquatable<InteractionAction>
    {
        /// <summary>Unique identifier for this action (e.g., "pickup", "examine", "disassemble").</summary>
        public string ActionId;

        /// <summary>Display text for this action (e.g., "Pick Up", "Examine").</summary>
        public string DisplayText;

        /// <summary>Localization key (optional). If set, UI should resolve this instead of DisplayText.</summary>
        public string LocalizationKey;

        /// <summary>Input hint for UI display (e.g., "E", "F", "Hold E").</summary>
        public string InputHint;

        /// <summary>Priority for display ordering. Higher values appear first.</summary>
        public int DisplayOrder;

        /// <summary>Whether this action is currently available.</summary>
        public bool IsEnabled;

        public InteractionAction(string actionId, string displayText, string inputHint = "", int displayOrder = 0)
        {
            ActionId = actionId;
            DisplayText = displayText;
            LocalizationKey = null;
            InputHint = inputHint;
            DisplayOrder = displayOrder;
            IsEnabled = true;
        }

        public bool IsValid => !string.IsNullOrEmpty(ActionId);

        public bool Equals(InteractionAction other) => ActionId == other.ActionId;
        public override bool Equals(object obj) => obj is InteractionAction other && Equals(other);
        public override int GetHashCode() => ActionId?.GetHashCode() ?? 0;
    }
}
