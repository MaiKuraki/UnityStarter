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
        private readonly GASCallbackList<Action<float, float>> currentValueObservers = new GASCallbackList<Action<float, float>>(2);
        private readonly GASCallbackList<Action<float, float>> baseValueObservers = new GASCallbackList<Action<float, float>>(2);
        internal bool IsDirty;
        internal readonly List<ActiveGameplayEffect> AffectingEffects = new List<ActiveGameplayEffect>(8);
        public int ActiveModifierSourceCount => AffectingEffects.Count;

        public long BaseValueRaw => baseValueRaw;
        public long CurrentValueRaw => currentValueRaw;
        public GASFixedValue BaseFixedValue => GASFixedValue.FromRaw(baseValueRaw);
        public GASFixedValue CurrentFixedValue => GASFixedValue.FromRaw(currentValueRaw);
        public float BaseValue => BaseFixedValue.ToFloat();
        public float CurrentValue => CurrentFixedValue.ToFloat();

        public event Action<float, float> OnCurrentValueChanged
        {
            add
            {
                AssertObserverSubscriptionAccess(false);
                currentValueObservers.Add(value);
            }
            remove
            {
                AssertObserverSubscriptionAccess(true);
                currentValueObservers.RemoveLast(value);
            }
        }

        public event Action<float, float> OnBaseValueChanged
        {
            add
            {
                AssertObserverSubscriptionAccess(false);
                baseValueObservers.Add(value);
            }
            remove
            {
                AssertObserverSubscriptionAccess(true);
                baseValueObservers.RemoveLast(value);
            }
        }

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
            if (OwningSet?.OwningAbilitySystemComponent != null)
            {
                OwningSet.SetBaseValueRaw(this, valueRaw);
                return;
            }

            SetBaseValueRawUnchecked(valueRaw);
        }

        internal void SetBaseValueRawUnchecked(long valueRaw)
        {
            long oldRaw = baseValueRaw;
            baseValueRaw = valueRaw;
            if (oldRaw != valueRaw)
            {
                InvokeValueObserversSafely(baseValueObservers, oldRaw, valueRaw, "OnBaseValueChanged");
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
            if (OwningSet?.OwningAbilitySystemComponent != null)
            {
                OwningSet.SetCurrentValueRaw(this, valueRaw);
                return;
            }

            SetCurrentValueRawUnchecked(valueRaw);
        }

        internal void SetCurrentValueRawUnchecked(long valueRaw)
        {
            long oldRaw = currentValueRaw;
            currentValueRaw = valueRaw;
            if (oldRaw != valueRaw)
            {
                InvokeValueObserversSafely(currentValueObservers, oldRaw, valueRaw, "OnCurrentValueChanged");
            }
        }

        private void InvokeValueObserversSafely(
            GASCallbackList<Action<float, float>> observers,
            long oldRaw,
            long newRaw,
            string observerName)
        {
            float oldValue = GASFixedValue.FromRaw(oldRaw).ToFloat();
            float newValue = GASFixedValue.FromRaw(newRaw).ToFloat();
            AbilitySystemComponent owner = OwningSet?.OwningAbilitySystemComponent;
            owner?.EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = observers.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    Action<float, float> observer = observers.GetCallback(i);
                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer.Invoke(oldValue, newValue);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"{observerName} observer failed after the attribute value was committed: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        observers.EndDispatch();
                    }
                }
                finally
                {
                    owner?.ExitRuntimeCallbackDispatch();
                }
            }
        }

        private void AssertObserverSubscriptionAccess(bool removal)
        {
            AbilitySystemComponent owner = OwningSet?.OwningAbilitySystemComponent;
            if (owner == null)
            {
                return;
            }

            if (removal)
            {
                owner.AssertRuntimeSubscriptionRemovalAccess();
            }
            else
            {
                owner.AssertRuntimeSubscriptionAccess();
            }
        }
    }
}
