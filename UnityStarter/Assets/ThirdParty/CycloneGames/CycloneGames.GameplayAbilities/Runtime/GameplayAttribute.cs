using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a numeric attribute that can be modified by GameplayEffects (e.g., Health, Mana, AttackPower).
    /// An attribute's real value is defined by its owning AttributeSet.
    /// </summary>
    public class GameplayAttribute
    {
        public string Name { get; }
        public AttributeSet OwningSet { get; internal set; }
        
        // BaseValue and CurrentValue are now managed by the AttributeSet to centralize logic.
        public float BaseValue => OwningSet.GetBaseValue(this);
        public float CurrentValue => OwningSet.GetCurrentValue(this);

        public event Action<float, float> OnCurrentValueChanged; // old, new

        public GameplayAttribute(string name)
        {
            Name = name;
        }

        internal void InvokeCurrentValueChanged(float oldValue, float newValue)
        {
            OnCurrentValueChanged?.Invoke(oldValue, newValue);
        }
    }
}