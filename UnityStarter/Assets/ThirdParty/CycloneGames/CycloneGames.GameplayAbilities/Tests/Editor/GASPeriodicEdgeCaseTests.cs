using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Boundary coverage for periodic capture timing: integer-multiple durations, durations shorter
    /// than one period, predicted application with rollback, and replica ticks that must not execute.
    /// Catch-up budgets are covered by PeriodicEffect_CatchUpUsesPerTickBudgetAndPreservesBacklog.
    /// </summary>
    public sealed class GASPeriodicEdgeCaseTests
    {
        [Test]
        public void DurationExactlyFourPeriods_ExecutesExactlyFourTimes()
        {
            var asc = CreateAsc(out EdgeSet set);
            try
            {
                set.Health.SetBaseValue(1000);
                asc.Tick(0f, isServer: true);

                Apply(asc, Periodic("Edge.ExactMultiple", duration: 1f, period: 0.25f, damage: -5f));

                int[] expected = { 995, 990, 985, 980 };
                for (int tick = 0; tick < expected.Length; tick++)
                {
                    asc.Tick(0.25f, isServer: true);
                    Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(expected[tick]).RawValue),
                        $"Tick {tick + 1} must apply one period worth of damage.");
                }

                Assert.That(asc.ActiveEffects, Is.Empty, "Duration 1.0 with period 0.25 expires after four ticks.");

                asc.Tick(0.25f, isServer: true);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(980).RawValue),
                    "An expired effect must not produce a ghost tick.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void DurationShorterThanOnePeriod_NeverExecutes()
        {
            var asc = CreateAsc(out EdgeSet set);
            try
            {
                set.Health.SetBaseValue(1000);
                asc.Tick(0f, isServer: true);

                Apply(asc, Periodic("Edge.ShortDuration", duration: 0.5f, period: 1f, damage: -5f));

                asc.Tick(0.5f, isServer: true);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(1000).RawValue),
                    "A duration shorter than one period expires before the first execution.");
                Assert.That(asc.ActiveEffects, Is.Empty);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void DurationEqualsOnePeriod_ExecutesExactlyOnceOnExpiryTick()
        {
            var asc = CreateAsc(out EdgeSet set);
            try
            {
                set.Health.SetBaseValue(1000);
                asc.Tick(0f, isServer: true);

                Apply(asc, Periodic("Edge.DurationEqualsPeriod", duration: 1f, period: 1f, damage: -5f));

                asc.Tick(1f, isServer: true);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(995).RawValue),
                    "The single due execution lands on the expiry tick.");
                Assert.That(asc.ActiveEffects, Is.Empty);

                asc.Tick(1f, isServer: true);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(995).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void PredictedPeriodicApplication_RollbackRestoresState()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var replica = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new EdgeSet();
            replica.AddAttributeSet(set);
            GameplayAbilitySpec abilitySpec = null;
            try
            {
                set.Health.SetBaseValue(1000);
                replica.Tick(0f, isServer: true);

                var ability = new EdgePredictionAbility();
                abilitySpec = replica.GrantAbility(ability);
                GASPredictionKey predictionKey = replica.OpenPredictionWindow(abilitySpec);

                GameplayEffectApplicationResultCode code;
                using (replica.BeginPredictionScope(predictionKey))
                {
                    GameplayEffectSpec spec = GameplayEffectSpec.Create(
                        Periodic("Edge.Predicted", duration: 5f, period: 1f, damage: -5f), replica);
                    code = replica.ApplyGameplayEffectSpecToSelf(spec).Code;
                }

                Assert.That(code, Is.EqualTo(GameplayEffectApplicationResultCode.Applied));
                Assert.That(replica.ActiveEffects, Has.Count.EqualTo(1));

                Assert.That(replica.RollbackPredictionWindow(predictionKey), Is.True);
                Assert.That(replica.ActiveEffects, Is.Empty, "Rollback must remove the predicted effect.");
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(1000).RawValue));

                // Periodic execution is authority-side; a rolled-back predicted effect must leave no
                // residue that a later tick could execute.
                replica.Tick(1f, isServer: false);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(1000).RawValue),
                    "A rolled-back effect must not resume ticking.");
            }
            finally
            {
                if (abilitySpec != null)
                {
                    replica.ClearAbility(abilitySpec);
                }
                replica.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void ReplicaTick_DoesNotExecutePeriodic()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var replica = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var set = new EdgeSet();
            replica.AddAttributeSet(set);
            try
            {
                set.Health.SetBaseValue(1000);
                replica.Tick(0f, isServer: false);

                Apply(replica, Periodic("Edge.Replica", duration: 5f, period: 1f, damage: -5f));

                replica.Tick(1f, isServer: false);
                Assert.That(set.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(1000).RawValue),
                    "Replica ticks must not execute periodic effects; authority owns execution.");
            }
            finally
            {
                replica.Dispose();
                context.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static GameplayEffect Periodic(string name, float duration, float period, float damage)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.HasDuration,
                duration: duration,
                period: period,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(damage))
                },
                executePeriodicEffectOnApplication: false);
        }

        private static void Apply(AbilitySystemComponent asc, GameplayEffect effect)
        {
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True,
                $"Effect '{effect.Name}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc(out EdgeSet set)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            set = new EdgeSet();
            asc.AddAttributeSet(set);
            return asc;
        }

        private sealed class EdgeSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }

        private sealed class EdgePredictionAbility : GameplayAbility
        {
            public EdgePredictionAbility()
            {
                Initialize(
                    "EdgePredictionAbility",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                EndAbility();
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new EdgePredictionAbility();
            }
        }
    }
}
