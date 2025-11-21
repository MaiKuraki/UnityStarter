using System;
using System.Collections.Generic;

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
        private float _baseValue;
        private float _currentValue;
        internal bool IsDirty;
        internal readonly List<ActiveGameplayEffect> AffectingEffects = new List<ActiveGameplayEffect>(8);

        public float BaseValue => _baseValue;
        public float CurrentValue => _currentValue;

        public event Action<float, float> OnCurrentValueChanged; // old, new

        public GameplayAttribute(string name)
        {
            Name = name;
        }

        public void SetBaseValue(float value)
        {
            _baseValue = value;
        }

        public void SetCurrentValue(float value)
        {
            float oldValue = _currentValue;
            _currentValue = value;
            if (Math.Abs(oldValue - value) > float.Epsilon)
            {
                OnCurrentValueChanged?.Invoke(oldValue, value);
            }
        }
    }
}