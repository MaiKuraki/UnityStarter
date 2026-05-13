using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;

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
        private long baseValueRaw;
        private long currentValueRaw;
        internal bool IsDirty;
        internal readonly List<ActiveGameplayEffect> AffectingEffects = new List<ActiveGameplayEffect>(8);

        public long BaseValueRaw => baseValueRaw;
        public long CurrentValueRaw => currentValueRaw;
        public GASFixedValue BaseFixedValue => GASFixedValue.FromRaw(baseValueRaw);
        public GASFixedValue CurrentFixedValue => GASFixedValue.FromRaw(currentValueRaw);
        public float BaseValue => BaseFixedValue.ToFloat();
        public float CurrentValue => CurrentFixedValue.ToFloat();

        public event Action<float, float> OnCurrentValueChanged; // old, new
        public event Action<float, float> OnBaseValueChanged; // old, new

        public GameplayAttribute(string name)
        {
            Name = name;
        }

        public void SetBaseValue(float value)
        {
            SetBaseValueRaw(GASFixedValue.FromFloat(value).RawValue);
        }

        public void SetBaseValue(GASFixedValue value)
        {
            SetBaseValueRaw(value.RawValue);
        }

        public void SetBaseValueRaw(long valueRaw)
        {
            long oldRaw = baseValueRaw;
            baseValueRaw = valueRaw;
            if (oldRaw != valueRaw)
            {
                OnBaseValueChanged?.Invoke(GASFixedValue.FromRaw(oldRaw).ToFloat(), GASFixedValue.FromRaw(valueRaw).ToFloat());
            }
        }

        public void SetCurrentValue(float value)
        {
            SetCurrentValueRaw(GASFixedValue.FromFloat(value).RawValue);
        }

        public void SetCurrentValue(GASFixedValue value)
        {
            SetCurrentValueRaw(value.RawValue);
        }

        public void SetCurrentValueRaw(long valueRaw)
        {
            long oldRaw = currentValueRaw;
            currentValueRaw = valueRaw;
            if (oldRaw != valueRaw)
            {
                OnCurrentValueChanged?.Invoke(GASFixedValue.FromRaw(oldRaw).ToFloat(), GASFixedValue.FromRaw(valueRaw).ToFloat());
            }
        }
    }
}
