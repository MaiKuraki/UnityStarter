using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Usage-combination coverage for capture timing: execution shape by capture source and
    /// snapshot policy, plus stacking, multiple modifiers, and allocation checks. Fixtures settle
    /// base values before applying so each delta isolates capture timing.
    /// </summary>
    public sealed class GASPeriodicCaptureTimingTests
    {
        private const float Period = 0.1f;
        private const float SettleDelta = 0.0001f;

        [Test]
        public void PeriodicTargetCapture_NotSnapshot_ReadsLiveValueEveryTick()
        {
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);
                Settle(asc);

                ApplyPeriodic(
                    asc,
                    BuildPeriodicAdd("Capture.Periodic.Target.Live", EGameplayEffectAttributeCaptureSource.Target,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));

                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue),
                    "First tick must add the live Power value (5).");

                attributes.Power.SetBaseValue(7);
                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(112).RawValue),
                    "Second tick must re-read Power (7); NotSnapshot must not reuse the application-time value.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void PeriodicSourceCapture_NotSnapshot_ReadsLiveValueEveryTick()
        {
            // One runtime context per world: ASCs that interact must share it.
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureAttributeSet();
            var targetSet = new CaptureAttributeSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(5);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPeriodicAdd("Capture.Periodic.Source.Live", EGameplayEffectAttributeCaptureSource.Source,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot),
                    source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));

                sourceSet.Power.SetBaseValue(7);
                Settle(source); // the source must settle its own current value first
                target.Tick(Period, isServer: true);
                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(112).RawValue),
                    "Source capture with NotSnapshot must re-read the source ASC's current value each tick.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void PeriodicSnapshot_FreezesValueAcrossTicks_ForBothCaptureSources()
        {
            AssertSnapshotFreeze(EGameplayEffectAttributeCaptureSource.Target);
            AssertSnapshotFreeze(EGameplayEffectAttributeCaptureSource.Source);
        }

        private static void AssertSnapshotFreeze(EGameplayEffectAttributeCaptureSource captureSource)
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureAttributeSet();
            var targetSet = new CaptureAttributeSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(5);
                targetSet.Power.SetBaseValue(5);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPeriodicAdd($"Capture.Periodic.{captureSource}.Frozen", captureSource,
                        EGameplayEffectAttributeCaptureSnapshot.Snapshot),
                    source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                sourceSet.Power.SetBaseValue(9);
                targetSet.Power.SetBaseValue(7);
                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue),
                    $"Snapshot ({captureSource}) must keep the application-time value (5) forever.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void PeriodicNotSnapshot_WithStacks_ScalesLiveValueByStacks()
        {
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);
                Settle(asc);

                GameplayEffect effect = BuildPeriodicAdd(
                    "Capture.Periodic.Stacked.Live",
                    EGameplayEffectAttributeCaptureSource.Target,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot,
                    stacking: new GameplayEffectStacking(
                        EGameplayEffectStackingType.AggregateByTarget,
                        5,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));

                ApplyPeriodic(asc, effect);
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);

                // UE periodic convention: each tick applies magnitude x StackCount, and the live
                // capture supplies the current magnitude.
                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue),
                    "Two stacks with a live Power of 5 must add 10.");

                attributes.Power.SetBaseValue(7);
                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(124).RawValue),
                    "Two stacks with a live Power of 7 must add 14.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void PeriodicNotSnapshot_MultipleAttributeModifiers_AllRefreshTogether()
        {
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Mana.SetBaseValue(50);
                attributes.Power.SetBaseValue(5);
                Settle(asc);

                var effect = new GameplayEffect(
                    "Capture.Periodic.Multi.Live",
                    EDurationPolicy.Infinite,
                    period: Period,
                    modifiers: new List<ModifierInfo>
                    {
                        AddFromLivePower("Health"),
                        AddFromLivePower("Mana")
                    },
                    executePeriodicEffectOnApplication: false);
                ApplyPeriodic(asc, effect);

                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));
                Assert.That(attributes.Mana.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(55).RawValue));

                attributes.Power.SetBaseValue(7);
                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(112).RawValue));
                Assert.That(attributes.Mana.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(62).RawValue),
                    "Both live-capture modifiers must refresh on every tick.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void InstantTargetCapture_ReadsTargetValueAtApplication()
        {
            // Instant target capture: the target is unknown at spec creation, so it must resolve on apply.
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);
                Settle(asc);

                var effect = new GameplayEffect(
                    "Capture.Instant.Target",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo> { AddFromLivePower("Health") });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue),
                    "Instant target capture must resolve the target attribute, not cache zero at spec creation.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void DurationNotSnapshot_ControlCase_RefreshesThroughAggregation()
        {
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);
                Settle(asc);

                var effect = new GameplayEffect(
                    "Capture.Duration.Control.Live",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo> { AddFromLivePower("Health") });
                ApplyPeriodic(asc, effect);

                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));

                attributes.Power.SetBaseValue(7);
                asc.Tick(Period, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(107).RawValue),
                    "Control: duration (non-periodic) NotSnapshot already refreshes through aggregation.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void PeriodicNotSnapshot_TicksAllocateZeroBytes()
        {
            var asc = CreateAsc(out CaptureAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);
                Settle(asc);
                ApplyPeriodic(
                    asc,
                    BuildPeriodicAdd("Capture.Periodic.Alloc", EGameplayEffectAttributeCaptureSource.Target,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));

                for (int i = 0; i < 200; i++)
                {
                    asc.Tick(Period, isServer: true);
                }

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++)
                {
                    asc.Tick(Period, isServer: true);
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                UnityEngine.Debug.Log($"[CaptureTiming][Alloc] 2000 periodic live-capture ticks: {allocated} bytes");
                Assert.Zero(allocated, "Live magnitude refresh on periodic ticks must stay allocation-free.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static void Settle(AbilitySystemComponent asc)
        {
            asc.Tick(SettleDelta, isServer: true);
        }

        private static GameplayEffect BuildPeriodicAdd(
            string name,
            EGameplayEffectAttributeCaptureSource captureSource,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            GameplayEffectStacking stacking = default)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: Period,
                modifiers: new List<ModifierInfo> { AddFromLivePower("Health", captureSource, snapshotPolicy) },
                stacking: stacking,
                executePeriodicEffectOnApplication: false);
        }

        private static ModifierInfo AddFromLivePower(
            string targetAttribute,
            EGameplayEffectAttributeCaptureSource captureSource = EGameplayEffectAttributeCaptureSource.Target,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
        {
            return new ModifierInfo(
                targetAttribute,
                EAttributeModifierOperation.Add,
                new AttributeBasedMagnitude(
                    "Power",
                    captureSource,
                    EAttributeBasedFloatCalculationType.AttributeMagnitude,
                    new ScalableFloat(1f),
                    new ScalableFloat(0f),
                    new ScalableFloat(0f),
                    snapshotPolicy));
        }

        private static void ApplyPeriodic(AbilitySystemComponent asc, GameplayEffect effect)
        {
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True,
                $"Effect '{effect.Name}' failed to apply.");
        }

        private static AbilitySystemComponent CreateAsc(out CaptureAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new CaptureAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private sealed class CaptureAttributeSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Mana = new GameplayAttribute("Mana");
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Mana);
                RegisterAttribute(Power);
            }
        }
    }
}
