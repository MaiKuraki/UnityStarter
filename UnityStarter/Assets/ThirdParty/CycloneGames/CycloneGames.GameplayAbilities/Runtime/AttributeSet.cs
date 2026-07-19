using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public abstract class AttributeSet
    {
        private readonly Dictionary<string, GameplayAttribute> registeredAttributes =
            new Dictionary<string, GameplayAttribute>(StringComparer.Ordinal);
        private bool attributesRegistered;
        private bool registrationInProgress;

        public AbilitySystemComponent OwningAbilitySystemComponent { get; internal set; }

        protected AttributeSet()
        {
        }

        /// <summary>
        /// Registers every attribute owned by this set. Implementations must use stable, unique names.
        /// This method is called once, lazily, after derived field initialization has completed.
        /// </summary>
        protected abstract void RegisterAttributes();

        protected void RegisterAttribute(GameplayAttribute attribute)
        {
            if (attribute == null)
            {
                throw new ArgumentNullException(nameof(attribute));
            }

            if (!registrationInProgress)
            {
                throw new InvalidOperationException("Attributes can only be registered from RegisterAttributes().");
            }

            if (string.IsNullOrWhiteSpace(attribute.Name))
            {
                throw new ArgumentException("GameplayAttribute names must be non-empty.", nameof(attribute));
            }

            if (registeredAttributes.ContainsKey(attribute.Name))
            {
                throw new InvalidOperationException($"Attribute '{attribute.Name}' is already registered by {GetType().FullName}.");
            }

            attribute.OwningSet = this;
            registeredAttributes.Add(attribute.Name, attribute);
        }

        private void EnsureAttributesRegistered()
        {
            if (attributesRegistered)
            {
                return;
            }

            if (registrationInProgress)
            {
                throw new InvalidOperationException($"Recursive attribute registration detected for {GetType().FullName}.");
            }

            registrationInProgress = true;
            try
            {
                RegisterAttributes();
                attributesRegistered = true;
            }
            catch
            {
                foreach (GameplayAttribute attribute in registeredAttributes.Values)
                {
                    attribute.OwningSet = null;
                }

                registeredAttributes.Clear();
                throw;
            }
            finally
            {
                registrationInProgress = false;
            }
        }

        public IReadOnlyCollection<GameplayAttribute> GetAttributes()
        {
            EnsureAttributesRegistered();
            return registeredAttributes.Values;
        }

        public float GetBaseValue(GameplayAttribute attribute) => attribute.BaseValue;
        public float GetCurrentValue(GameplayAttribute attribute) => attribute.CurrentValue;
        public long GetBaseValueRaw(GameplayAttribute attribute) => attribute.BaseValueRaw;
        public long GetCurrentValueRaw(GameplayAttribute attribute) => attribute.CurrentValueRaw;
        public GASFixedValue GetBaseFixedValue(GameplayAttribute attribute) => attribute.BaseFixedValue;
        public GASFixedValue GetCurrentFixedValue(GameplayAttribute attribute) => attribute.CurrentFixedValue;

        public void SetBaseValue(GameplayAttribute attribute, float value)
        {
            SetBaseValueRaw(attribute, GASFixedValue.FromFloat(value).RawValue);
        }

        public void SetBaseValue(GameplayAttribute attribute, GASFixedValue value)
        {
            SetBaseValueRaw(attribute, value.RawValue);
        }

        public void SetBaseValueRaw(GameplayAttribute attribute, long valueRaw)
        {
            ValidateOwnedAttribute(attribute);
            if (attribute.BaseValueRaw != valueRaw)
            {
                using (OwningAbilitySystemComponent?.BeginReplicationMutationScope() ?? default)
                {
                    attribute.SetBaseValueRawUnchecked(valueRaw);
                    OwningAbilitySystemComponent?.MarkAttributeDirty(attribute);
                }
            }
        }

        public void SetCurrentValue(GameplayAttribute attribute, float value)
        {
            SetCurrentValueRaw(attribute, GASFixedValue.FromFloat(value).RawValue);
        }

        public void SetCurrentValue(GameplayAttribute attribute, GASFixedValue value)
        {
            SetCurrentValueRaw(attribute, value.RawValue);
        }

        public void SetCurrentValueRaw(GameplayAttribute attribute, long valueRaw)
        {
            ValidateOwnedAttribute(attribute);
            if (attribute.CurrentValueRaw == valueRaw)
            {
                return;
            }

            using (OwningAbilitySystemComponent?.BeginReplicationMutationScope() ?? default)
            {
                attribute.SetCurrentValueRawUnchecked(valueRaw);
                OwningAbilitySystemComponent?.NotifyDirectCurrentValueChanged(attribute);
            }
        }

        private void ValidateOwnedAttribute(GameplayAttribute attribute)
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            EnsureAttributesRegistered();
            if (!ReferenceEquals(attribute.OwningSet, this))
            {
                throw new InvalidOperationException($"Attribute '{attribute.Name}' is not owned by {GetType().FullName}.");
            }
        }

        /// <summary>
        /// Retrieves an attribute by its name.
        /// </summary>
        public GameplayAttribute GetAttribute(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            EnsureAttributesRegistered();
            registeredAttributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        /// <summary>
        /// HOOK for derived classes. Called before the default modification.
        /// Can be overridden to implement special logic (like damage mitigation).
        /// </summary>
        /// <returns>Return true to indicate the effect has been fully handled and to skip the default logic.</returns>
        protected virtual bool PreProcessInstantEffect(GameplayEffectModCallbackData data)
        {
            return false;
        }

        /// <summary>
        /// HOOK for derived classes. Called after the default modification.
        /// Can be overridden to implement reactive logic (like checking for level up).
        /// </summary>
        protected virtual void PostProcessInstantEffect(GameplayEffectModCallbackData data)
        {
        }

        /// <summary>
        /// Hook invoked before an attribute's CurrentValue is recalculated. Default is a no-op.
        /// Override to clamp or adjust the value. Stays in fixed-point: use GASFixedValue.Clamp / Min / Max
        /// so results are bit-identical across platforms and backends (required for lockstep /
        /// server-authoritative play).
        /// </summary>
        public virtual void PreAttributeChange(GameplayAttribute attribute, ref GASFixedValue newValue) { }

        /// <summary>
        /// Hook invoked before an attribute's BaseValue changes (instant effects). Default is a no-op.
        /// Override to clamp or adjust the base value. Stays in fixed-point for deterministic results.
        /// </summary>
        public virtual void PreAttributeBaseChange(GameplayAttribute attribute, ref GASFixedValue newBaseValue) { }

        /// <summary>
        /// Called after a GameplayEffect is executed on this AttributeSet. This is the main entry point for attribute modifications.
        /// It follows a Pre-Process, Default-Process, Post-Process flow.
        /// </summary>
        public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
        {
            // --- Pre-Process ---
            if (PreProcessInstantEffect(data))
            {
                return;
            }

            // --- Default-Process ---
            ApplyDefaultInstantEffectModification(data);

            // --- Post-Process ---
            PostProcessInstantEffect(data);
        }

        /// <summary>
        /// This contains the standard calculation logic.
        /// </summary>
        protected virtual void ApplyDefaultInstantEffectModification(GameplayEffectModCallbackData data)
        {
            var attribute = GetAttribute(data.Modifier.AttributeName);
            if (attribute == null)
            {
                return;
            }

            var newBase = GetBaseFixedValue(attribute);
            var magnitude = data.EvaluatedMagnitudeFixed;
            switch (data.Modifier.Operation)
            {
                case EAttributeModifierOperation.Add:
                    newBase += magnitude;
                    break;
                case EAttributeModifierOperation.Multiply:
                    newBase *= magnitude;
                    break;
                case EAttributeModifierOperation.Division:
                    if (magnitude.RawValue != 0)
                    {
                        newBase /= magnitude;
                    }

                    break;
                case EAttributeModifierOperation.Override:
                    newBase = magnitude;
                    break;
            }

            PreAttributeBaseChange(attribute, ref newBase);

            SetBaseValue(attribute, newBase);
            SetCurrentValue(attribute, newBase);
        }
    }

    // Data struct passed to PostGameplayEffectExecute
    public struct GameplayEffectModCallbackData
    {
        public GameplayEffectSpec EffectSpec { get; }
        public ModifierInfo Modifier { get; }
        public float EvaluatedMagnitude { get; }
        public long EvaluatedMagnitudeRaw { get; }
        public GASFixedValue EvaluatedMagnitudeFixed => GASFixedValue.FromRaw(EvaluatedMagnitudeRaw);
        public AbilitySystemComponent Target { get; }
        public AbilitySystemComponent Source => EffectSpec.Source;

        public GameplayEffectModCallbackData(GameplayEffectSpec spec, ModifierInfo modifier, float magnitude, AbilitySystemComponent target)
            : this(spec, modifier, GASFixedValue.FromFloat(magnitude).RawValue, target)
        {
        }

        public GameplayEffectModCallbackData(GameplayEffectSpec spec, ModifierInfo modifier, long magnitudeRaw, AbilitySystemComponent target)
        {
            EffectSpec = spec;
            Modifier = modifier;
            EvaluatedMagnitudeRaw = magnitudeRaw;
            EvaluatedMagnitude = GASFixedValue.FromRaw(magnitudeRaw).ToFloat();
            Target = target;
        }
    }
}
