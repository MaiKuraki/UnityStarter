using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASStateDeltaResyncTickTests
    {
        [Test]
        public void Resync_FreezesTickAndResumesWithoutCatchUp()
        {
            var asc = CreateAsc(out HealthAttributeSet attributes);
            try
            {
                ApplyPeriodic(asc);
                asc.Tick(1f, true);
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(99).RawValue));

                asc.Tick(0.25f, true);
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(99).RawValue));

                ActiveGameplayEffect effect = asc.ActiveEffects[0];
                long periodRemainingBeforeResync = effect.PeriodTimeRemainingRaw;

                asc.RequireStateDeltaResyncForTests(GASStateDeltaRejectionReason.InvalidSequence);
                Assert.That(asc.StateDeltaResyncRequired, Is.True);

                asc.Tick(3f, true);

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(99).RawValue),
                    "Ticks during a resync must not execute periodic effects.");
                Assert.That(effect.PeriodTimeRemainingRaw, Is.EqualTo(periodRemainingBeforeResync),
                    "The period timer must not advance while the tick is frozen.");

                asc.ResetStateDeltaBaseline(1u, 1UL, asc.ComputeReplicatedStateChecksum());
                Assert.That(asc.StateDeltaResyncRequired, Is.False);

                asc.Tick(0.75f, true);

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(98).RawValue),
                    "Resuming must continue from the frozen timer, not replay the skipped window.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void Control_WithoutResync_TheSameWindowExecutesThreeTimes()
        {
            var asc = CreateAsc(out HealthAttributeSet attributes);
            try
            {
                ApplyPeriodic(asc);
                asc.Tick(1f, true);
                asc.Tick(0.25f, true);
                asc.Tick(3f, true);

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(96).RawValue),
                    "Without a resync the same window must deliver three periodic executions.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static AbilitySystemComponent CreateAsc(out HealthAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);
            return asc;
        }

        private static void ApplyPeriodic(AbilitySystemComponent asc)
        {
            var effect = new GameplayEffect(
                "Resync.Probe",
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-1f))
                },
                executePeriodicEffectOnApplication: false);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
        }

        private sealed class HealthAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }
    }
}
