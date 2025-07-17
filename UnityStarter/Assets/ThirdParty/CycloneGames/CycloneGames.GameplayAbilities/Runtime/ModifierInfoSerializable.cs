using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    [System.Serializable]
    public class ModifierInfoSerializable
    {
        [Tooltip("The name of the attribute to modify. Must match the name in the AttributeSet exactly.")]
        public string AttributeName;

        [Tooltip("The operation to perform on the attribute.")]
        public EAttributeModifierOperation Operation;

        [Tooltip("The magnitude of the modification. Can be a fixed value or scale with level.")]
        public ScalableFloat Magnitude;
    }
}
