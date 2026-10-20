using System.Collections.Generic;
using System.Text;

using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Prints per-tick timelines for capture timing: live and snapshot spell-power ignite,
    /// %-current-health poison, stacked bleed, and a heal-over-time following live power.
    /// </summary>
    public sealed class CombatCaptureTimingSample : MonoBehaviour
    {
        private static readonly LogChannel Log = GameplayAbilitiesSampleLog.Channel;

        [SerializeField] private bool runOnStart = true;
        [SerializeField] private float periodSeconds = 1f;
        [SerializeField] private float startHealth = 1000f;
        [SerializeField] private float startPower = 40f;

        private GASRuntimeContext runtimeContext;
        private AbilitySystemComponent caster;
        private AbilitySystemComponent victim;
        private SampleAttributeSet casterSet;
        private SampleAttributeSet victimSet;

        private void Start()
        {
            if (runOnStart)
            {
                RunDemo();
            }
        }

        [ContextMenu("Run Capture Timing Demo")]
        public void RunDemo()
        {
            // Periodic effects outlive a scenario, so each one gets its own world.
            RunIsolated(RunLiveIgnite);
            RunIsolated(RunSnapshotIgnite);
            RunIsolated(RunPercentHealthPoison);
            RunIsolated(RunStackedBleed);
            RunIsolated(RunLiveHealOverTime);
        }

        private void RunIsolated(System.Action scenario)
        {
            BuildWorld();
            try
            {
                scenario();
            }
            finally
            {
                TearDownWorld();
            }
        }

        private void RunLiveIgnite()
        {
            ResetActors();
            GameplayEffect ignite = BuildPowerEffect("Sample.Ignite.Live", -1f, snapshot: false);
            ApplyFromCaster(ignite);

            var timeline = new List<float>();
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            casterSet.Power.SetBaseValue(startPower + 30f); // buff lands mid-duration
            caster.Tick(0f, isServer: true);
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            LogTimeline("Ignite (live spell power 40 -> 70)", timeline);
        }

        private void RunSnapshotIgnite()
        {
            ResetActors();
            GameplayEffect ignite = BuildPowerEffect("Sample.Ignite.Snapshot", -1f, snapshot: true);
            ApplyFromCaster(ignite);

            var timeline = new List<float>();
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            casterSet.Power.SetBaseValue(startPower + 30f);
            caster.Tick(0f, isServer: true);
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            LogTimeline("Ignite (snapshot at cast, buff ignored)", timeline);
        }

        private void RunPercentHealthPoison()
        {
            ResetActors();
            GameplayEffect poison = BuildPercentHealthEffect("Sample.Poison.HalfCurrent", 0.5f);
            ApplyFromCaster(poison);

            var timeline = new List<float>();
            for (int i = 0; i < 3; i++)
            {
                TickOnce();
                timeline.Add(victimSet.Health.CurrentValue);
            }

            LogTimeline("Poison (50% of current health per tick)", timeline);
        }

        private void RunStackedBleed()
        {
            ResetActors();
            GameplayEffect bleed = BuildFlatDamageEffect("Sample.Bleed", 15f);
            ApplyFromCaster(bleed);
            ApplyFromCaster(bleed); // second application stacks to 2

            var timeline = new List<float>();
            for (int i = 0; i < 2; i++)
            {
                TickOnce();
                timeline.Add(victimSet.Health.CurrentValue);
            }

            LogTimeline("Bleed (2 stacks x 15 per tick)", timeline);
        }

        private void RunLiveHealOverTime()
        {
            ResetActors();
            victimSet.Health.SetBaseValue(100f);
            victim.Tick(0f, isServer: true);
            casterSet.Power.SetBaseValue(20f);
            caster.Tick(0f, isServer: true);

            GameplayEffect heal = BuildPowerEffect("Sample.HoT.Live", 1f, snapshot: false);
            ApplyFromCaster(heal);

            var timeline = new List<float>();
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            casterSet.Power.SetBaseValue(30f);
            caster.Tick(0f, isServer: true);
            TickOnce();
            timeline.Add(victimSet.Health.CurrentValue);

            LogTimeline("Heal over time (healer power 20 -> 30)", timeline);
        }

        private void ResetActors()
        {
            casterSet.Health.SetBaseValue(startHealth);
            casterSet.Power.SetBaseValue(startPower);
            victimSet.Health.SetBaseValue(startHealth);
            victimSet.Power.SetBaseValue(startPower);
            caster.Tick(0f, isServer: true);
            victim.Tick(0f, isServer: true);
        }

        private void ApplyFromCaster(GameplayEffect effect)
        {
            GameplayEffectApplicationResult result =
                victim.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, caster));
            if (!result.Succeeded)
            {
                Log.Warning($"Sample effect '{effect.Name}' failed to apply: {result.Code}.");
            }
        }

        private void TickOnce() => victim.Tick(periodSeconds, isServer: true);

        private GameplayEffect BuildPowerEffect(string name, float signedCoefficient, bool snapshot)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: periodSeconds,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Power",
                            EGameplayEffectAttributeCaptureSource.Source,
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

        private GameplayEffect BuildPercentHealthEffect(string name, float coefficient)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: periodSeconds,
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
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                },
                executePeriodicEffectOnApplication: false);
        }

        private GameplayEffect BuildFlatDamageEffect(string name, float damagePerStack)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: periodSeconds,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-damagePerStack))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    5,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                executePeriodicEffectOnApplication: false);
        }

        private static void LogTimeline(string scenario, IReadOnlyList<float> timeline)
        {
            var builder = new StringBuilder();
            builder.Append("[CaptureTimingSample] ").Append(scenario).Append(": ");
            for (int i = 0; i < timeline.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" -> ");
                }
                builder.Append(timeline[i].ToString("0.###"));
            }
            Log.Info(builder.ToString());
        }

        private void BuildWorld()
        {
            TearDownWorld();
            runtimeContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            caster = new AbilitySystemComponent(runtimeContext);
            victim = new AbilitySystemComponent(runtimeContext);
            casterSet = new SampleAttributeSet();
            victimSet = new SampleAttributeSet();
            caster.AddAttributeSet(casterSet);
            victim.AddAttributeSet(victimSet);
        }

        private void TearDownWorld()
        {
            if (caster != null)
            {
                caster.Dispose();
                caster = null;
            }
            if (victim != null)
            {
                victim.Dispose();
                victim = null;
            }
            if (runtimeContext != null)
            {
                runtimeContext.Dispose();
                runtimeContext = null;
            }
            casterSet = null;
            victimSet = null;
        }

        private void OnDestroy() => TearDownWorld();

        private sealed class SampleAttributeSet : AttributeSet
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
