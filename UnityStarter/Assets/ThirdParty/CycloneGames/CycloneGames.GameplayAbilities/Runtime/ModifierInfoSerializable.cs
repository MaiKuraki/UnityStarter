using UnityEngine;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class AttributeNameSelectorAttribute : PropertyAttribute { }

    [System.Serializable]
    public class ModifierInfoSerializable
    {
        [Tooltip("The name of the attribute to modify. Must match the name in the AttributeSet exactly.")]
        [AttributeNameSelector]
        public string AttributeName;

        [Tooltip("The operation to perform on the attribute.")]
        public EAttributeModifierOperation Operation;

        [Tooltip("Ordered evaluation channel for this modifier. Channel0 is the default general-purpose path.")]
        public GASModifierEvaluationChannel EvaluationChannel;

        [Tooltip("The magnitude of the modification. Can be a fixed value or scale with level.")]
        public ScalableFloat Magnitude;
    }
}
