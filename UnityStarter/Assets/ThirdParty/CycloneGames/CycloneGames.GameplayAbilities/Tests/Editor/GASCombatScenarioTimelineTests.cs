using System.Collections.Generic;
using System.Text;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Combat timelines for capture timing, driven through the public ASC API and comparable to a
    /// designer's numbers: %-current-health poison, live and snapshot spell-power ignite, stacked
    /// bleed, and a heal-over-time that follows the healer's current power.
    /// </summary>
    public sealed class GASCombatScenarioTimelineTests
    {
        private const float TickSeconds = 1f;
        private const float PeriodSeconds = 1f;

        [Test]
        public void Poison_PercentOfCurrentHealth_RecomputesEveryTick()
        {
            var asc = CreateAsc(out CombatSet set);
            try
            {
                set.Health.SetBaseValue(1000);
                set.Power.SetBaseValue(0);
                Settle(asc);

                // 50% of current health per tick: 1000 -> 500 -> 250 -> 125.
                ApplyPeriodic(asc, BuildPercentOfHealth("Poison.HalfCurrent", 0.5f, snapshot: false));

                var timeline = new List<long>();
                for (int tick = 0; tick < 3; tick++)
                {
                    asc.Tick(TickSeconds, isServer: true);
                    timeline.Add(set.Health.CurrentValueRaw);
                }

                Assert.That(timeline, Is.EqualTo(new[]
                {
                    GASFixedValue.FromInt(500).RawValue,
                    GASFixedValue.FromInt(250).RawValue,
                    GASFixedValue.FromInt(125).RawValue
                }), "Poison must halve the current health each tick, not the application-time health.");
                LogTimeline("Poison 50% current HP", timeline);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void Ignite_LiveSpellPower_TracksBuffAppliedMidDuration()
        {
            // One runtime context per world: caster and victim interact.
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var caster = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var victim = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var casterSet = new CombatSet();
            var victimSet = new CombatSet();
            caster.AddAttributeSet(casterSet);
            victim.AddAttributeSet(victimSet);
            try
            {
                casterSet.Power.SetBaseValue(40);
                victimSet.Health.SetBaseValue(1000);
                Settle(caster);
                Settle(victim);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPowerDamage("Ignite.Live", captureSource: EGameplayEffectAttributeCaptureSource.Source,
                        snapshot: false, signedCoefficient: -1f), caster);
                Assert.That(victim.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                var timeline = new List<long>();
                victim.Tick(TickSeconds, isServer: true);
                timeline.Add(victimSet.Health.CurrentValueRaw);   // 40 damage

                casterSet.Power.SetBaseValue(70);                 // mid-duration buff
                Settle(caster);
                victim.Tick(TickSeconds, isServer: true);
                timeline.Add(victimSet.Health.CurrentValueRaw);   // 70 damage

                Assert.That(timeline, Is.EqualTo(new[]
                {
                    GASFixedValue.FromInt(960).RawValue,
                    GASFixedValue.FromInt(890).RawValue
                }), "Ignite must follow the caster's live spell power, including a buff applied mid-duration.");
                LogTimeline("Ignite live spell power (40 then 70)", timeline);
            }
            finally
            {
                victim.Dispose();
                caster.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void Ignite_SnapshotAtCast_KeepsCastValueAfterBuff()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var caster = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var victim = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var casterSet = new CombatSet();
            var victimSet = new CombatSet();
            caster.AddAttributeSet(casterSet);
            victim.AddAttributeSet(victimSet);
            try
            {
                casterSet.Power.SetBaseValue(40);
                victimSet.Health.SetBaseValue(1000);
                Settle(caster);
                Settle(victim);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPowerDamage("Ignite.Snapshot", captureSource: EGameplayEffectAttributeCaptureSource.Source,
                        snapshot: true, signedCoefficient: -1f), caster);
                Assert.That(victim.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                var timeline = new List<long>();
                victim.Tick(TickSeconds, isServer: true);
                timeline.Add(victimSet.Health.CurrentValueRaw);

                casterSet.Power.SetBaseValue(70);
                Settle(caster);
                victim.Tick(TickSeconds, isServer: true);
                timeline.Add(victimSet.Health.CurrentValueRaw);

                Assert.That(timeline, Is.EqualTo(new[]
                {
                    GASFixedValue.FromInt(960).RawValue,
                    GASFixedValue.FromInt(920).RawValue
                }), "Snapshot ignite keeps the cast-time value (40) for every tick.");
                LogTimeline("Ignite snapshot at cast (40, buff to 70 ignored)", timeline);
            }
            finally
            {
                victim.Dispose();
                caster.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void Bleed_TwoStacks_ScalesEachTick()
        {
            var asc = CreateAsc(out CombatSet set);
            try
            {
                set.Health.SetBaseValue(1000);
                Settle(asc);

                GameplayEffect bleed = BuildFlatDamage("Bleed.Stacked", 15f, stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    5,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
                ApplyPeriodic(asc, bleed);
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(bleed, asc)).Succeeded, Is.True);

                var timeline = new List<long>();
                for (int tick = 0; tick < 2; tick++)
                {
                    asc.Tick(TickSeconds, isServer: true);
                    timeline.Add(set.Health.CurrentValueRaw);
                }

                // Two stacks x 15 per tick: 1000 -> 970 -> 940.
                Assert.That(timeline, Is.EqualTo(new[]
                {
                    GASFixedValue.FromInt(970).RawValue,
                    GASFixedValue.FromInt(940).RawValue
                }), "A 2-stack bleed must deal 30 per tick (UE periodic stack convention).");
                LogTimeline("Bleed 2 stacks x 15", timeline);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void HealOverTime_LiveHealerPower_TracksMidDurationGearChange()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var healer = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var patient = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var healerSet = new CombatSet();
            var patientSet = new CombatSet();
            healer.AddAttributeSet(healerSet);
            patient.AddAttributeSet(patientSet);
            try
            {
                healerSet.Power.SetBaseValue(20);
                patientSet.Health.SetBaseValue(100);
                Settle(healer);
                Settle(patient);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPowerDamage("HoT.Live", captureSource: EGameplayEffectAttributeCaptureSource.Source,
                        snapshot: false, signedCoefficient: 1f), healer);
                Assert.That(patient.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                var timeline = new List<long>();
                patient.Tick(TickSeconds, isServer: true);
                timeline.Add(patientSet.Health.CurrentValueRaw);   // +20

                healerSet.Power.SetBaseValue(30);                 // gear swap mid-HoT
                Settle(healer);
                patient.Tick(TickSeconds, isServer: true);
                timeline.Add(patientSet.Health.CurrentValueRaw);   // +30

                Assert.That(timeline, Is.EqualTo(new[]
                {
                    GASFixedValue.FromInt(120).RawValue,
                    GASFixedValue.FromInt(150).RawValue
                }), "A HoT must heal for the healer's current spell power on each tick.");
                LogTimeline("HoT live healer power (20 then 30)", timeline);
            }
            finally
            {
                patient.Dispose();
                healer.Dispose();
                context.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static void LogTimeline(string scenario, IReadOnlyList<long> timeline)
        {
            var builder = new StringBuilder();
            builder.Append("[CombatTimeline] ").Append(scenario).Append(": ");
            for (int i = 0; i < timeline.Count; i++)
            {
                if (i > 0) builder.Append(" -> ");
                builder.Append(GASFixedValue.FromRaw(timeline[i]).ToFloat().ToString("0.###"));
            }
            UnityEngine.Debug.Log(builder.ToString());
        }

        private static void Settle(AbilitySystemComponent asc) => asc.Tick(0f, isServer: true);

        private static GameplayEffect BuildPercentOfHealth(string name, float coefficient, bool snapshot)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: PeriodSeconds,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Health",
                            EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(-coefficient),
                            new ScalableFloat(0f),
                            new ScalableFloat(0f),
                            snapshot
                                ? EGameplayEffectAttributeCaptureSnapshot.Snapshot
                                : EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                },
                executePeriodicEffectOnApplication: false);
        }

        private static GameplayEffect BuildPowerDamage(
            string name,
            EGameplayEffectAttributeCaptureSource captureSource,
            bool snapshot,
            float signedCoefficient)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: PeriodSeconds,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Power",
                            captureSource,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(signedCoefficient),
                            new ScalableFloat(0f),
                            new ScalableFloat(0f),
                            snapshot
                                ? EGameplayEffectAttributeCaptureSnapshot.Snapshot
                                : EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                },
                executePeriodicEffectOnApplication: false);
        }

        private static GameplayEffect BuildFlatDamage(string name, float damagePerStack, GameplayEffectStacking stacking)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: PeriodSeconds,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-damagePerStack))
                },
                stacking: stacking,
                executePeriodicEffectOnApplication: false);
        }

        private static void ApplyPeriodic(AbilitySystemComponent asc, GameplayEffect effect)
        {
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True,
                $"Effect '{effect.Name}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc(out CombatSet set)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            set = new CombatSet();
            asc.AddAttributeSet(set);
            return asc;
        }

        private sealed class CombatSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Power);
            }
        }
    }
}
