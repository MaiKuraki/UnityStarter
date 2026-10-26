using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Source lifetime coverage for attribute-based magnitudes. A modifier binds its attribute
    /// instance when the spec is created (source side) or applied (target side); a periodic effect
    /// whose source component is disposed must keep evaluating that binding deterministically
    /// instead of faulting or being dropped from the active list.
    /// </summary>
    public sealed class GameplayEffectSourceLifetimeTests
    {
        private const float Period = 0.1f;
        private const float SettleDelta = 0.0001f;

        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.Snapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.Snapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.Snapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.Snapshot, false)]
        public void PeriodicCapture_SourceAndTargetSides_MatchExpectedMagnitude(
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            bool disposeSource)
        {
            AssertPeriodicCapture(captureSide, snapshotPolicy, disposeSource);
        }

        [Test]
        public void DisposedSource_RepeatedTicks_StayLinearAndKeepTheEffectActive()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(7);
                targetSet.Health.SetBaseValue(0);
                Settle(source);
                Settle(target);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPeriodic("Lifetime.Repeat", EGameplayEffectAttributeCaptureSource.Source,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot),
                    source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                source.Dispose();

                const int ticks = 10;
                for (int i = 0; i < ticks; i++)
                {
                    target.Tick(Period, isServer: true);
                }

                Assert.That(targetSet.Health.CurrentValueRaw,
                    Is.EqualTo(GASFixedValue.FromInt(7 * ticks).RawValue),
                    "Every tick after disposal must apply the frozen source magnitude.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1),
                    "The effect must stay active after its source is disposed.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void DisposedSource_DurationEffect_StillExpiresAtItsDeadline()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(4);
                targetSet.Health.SetBaseValue(0);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.Duration",
                    EDurationPolicy.HasDuration,
                    duration: 0.25f,
                    modifiers: new List<ModifierInfo>
                    {
                        AddFromPower(EGameplayEffectAttributeCaptureSource.Source,
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                    },
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                source.Dispose();

                target.Tick(0.1f, isServer: true);
                target.Tick(0.1f, isServer: true);
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1), "The effect must survive until its duration elapses.");

                target.Tick(0.1f, isServer: true);
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(0), "Duration must still expire after the source is disposed.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SourceAttributeSetRemovedAfterCapture_KeepsReadingTheDetachedAttribute()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(9);
                targetSet.Health.SetBaseValue(50);
                Settle(source);
                Settle(target);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPeriodic("Lifetime.SetRemoved", EGameplayEffectAttributeCaptureSource.Source,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot),
                    source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                source.RemoveAttributeSet(sourceSet);

                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(59).RawValue),
                    "Detaching the attribute set must not invalidate a binding that already captured its attribute.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SpecWithoutSource_AttributeBasedSource_EvaluatesZeroWithoutFault()
        {
            var target = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var targetSet = new CaptureSet();
            target.AddAttributeSet(targetSet);
            try
            {
                targetSet.Health.SetBaseValue(10);
                Settle(target);

                GameplayEffect effect = BuildPeriodic("Lifetime.NoSource",
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source: null);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(10).RawValue),
                    "A missing source resolves to zero magnitude, not a lifetime fault.");
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void DisposedSource_ExecutionOutputModifier_UsesDeclaredCapture()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(6);
                targetSet.Health.SetBaseValue(20);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.Execution",
                    EDurationPolicy.Infinite,
                    period: Period,
                    execution: new SourcePowerExecution(declareCapture: true),
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(26).RawValue));

                source.Dispose();
                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(32).RawValue),
                    "A declared capture keeps execution output at the frozen source value.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void UndeclaredExecutionCapture_ContributesZero()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(6);
                targetSet.Health.SetBaseValue(20);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.ExecutionUndeclared",
                    EDurationPolicy.Infinite,
                    period: Period,
                    execution: new SourcePowerExecution(declareCapture: false),
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(20).RawValue),
                    "Attribute-based execution output requires a declared capture.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void CustomMagnitude_UsesDeclaredCaptureAfterSourceDisposed()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(6);
                targetSet.Health.SetBaseValue(0);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.CustomMagnitude",
                    EDurationPolicy.Infinite,
                    period: Period,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo(
                            "Health",
                            EAttributeModifierOperation.Add,
                            new SourcePowerMagnitude(declareCapture: true),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                    },
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(6).RawValue));

                sourceSet.Power.SetBaseValue(9);
                Settle(source);
                source.Dispose();
                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(15).RawValue),
                    "A custom magnitude calculation must read its declared capture frozen at the last settled value.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void CustomMagnitude_UndeclaredCapture_ContributesZero()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(6);
                targetSet.Health.SetBaseValue(0);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.CustomMagnitudeUndeclared",
                    EDurationPolicy.Infinite,
                    period: Period,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo(
                            "Health",
                            EAttributeModifierOperation.Add,
                            new SourcePowerMagnitude(declareCapture: false),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                    },
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);

                Assert.That(targetSet.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(0).RawValue),
                    "Reading an undeclared attribute is not captured and contributes zero.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void DuplicateDeclaredCaptures_AcrossModifiers_AreCollapsed()
        {
            var effect = new GameplayEffect(
                "Lifetime.DuplicateCapture",
                EDurationPolicy.Infinite,
                period: Period,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add,
                        new SourcePowerMagnitude(declareCapture: true),
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot),
                    new ModifierInfo("Health", EAttributeModifierOperation.Add,
                        new SourcePowerMagnitude(declareCapture: true),
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                },
                executePeriodicEffectOnApplication: false);

            Assert.That(effect.DeclaredCaptures.Count, Is.EqualTo(1));
        }

        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.Snapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Source, EGameplayEffectAttributeCaptureSnapshot.Snapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.NotSnapshot, false)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.Snapshot, true)]
        [TestCase(EGameplayEffectAttributeCaptureSource.Target, EGameplayEffectAttributeCaptureSnapshot.Snapshot, false)]
        public void CustomMagnitude_CaptureMatrix_MatchExpectedMagnitude(
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            bool disposeSource)
        {
            AssertCustomMagnitudeCapture(captureSide, snapshotPolicy, disposeSource);
        }

        private static void AssertCustomMagnitudeCapture(
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            bool disposeSource)
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(5);
                targetSet.Power.SetBaseValue(3);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    $"Lifetime.Custom.{captureSide}.{snapshotPolicy}.{disposeSource}",
                    EDurationPolicy.Infinite,
                    period: Period,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo(
                            "Health",
                            EAttributeModifierOperation.Add,
                            new SourcePowerMagnitude(declareCapture: true, captureSide),
                            snapshotPolicy)
                    },
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                int firstDelta = captureSide == EGameplayEffectAttributeCaptureSource.Source ? 5 : 3;
                Assert.That(targetSet.Health.CurrentValueRaw,
                    Is.EqualTo(GASFixedValue.FromInt(100 + firstDelta).RawValue));

                sourceSet.Power.SetBaseValue(7);
                targetSet.Power.SetBaseValue(4);
                Settle(source);
                Settle(target);
                if (disposeSource)
                {
                    source.Dispose();
                }

                target.Tick(Period, isServer: true);
                int secondDelta;
                if (captureSide == EGameplayEffectAttributeCaptureSource.Source)
                {
                    secondDelta = snapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot ? 7 : 5;
                }
                else
                {
                    secondDelta = snapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot ? 4 : 3;
                }

                Assert.That(targetSet.Health.CurrentValueRaw,
                    Is.EqualTo(GASFixedValue.FromInt(100 + firstDelta + secondDelta).RawValue),
                    $"Unexpected custom magnitude for {captureSide}/{snapshotPolicy}/disposed={disposeSource}.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SourceDisposed_DetachesLiveReferenceButKeepsIdentity()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(5);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                GameplayEffect effect = BuildPeriodic("Lifetime.Detach",
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);

                GASEntityId sourceId = source.CoreEntity;
                source.Dispose();

                ActiveGameplayEffect active = target.FindActiveEffectByDef(effect);
                Assert.That(active, Is.Not.Null);
                Assert.That(active.Spec.Source, Is.Null, "A disposed source must not stay referenced by the effect.");
                Assert.That(active.Spec.SourceEntity, Is.EqualTo(sourceId));
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void RemoveActiveEffectsBySource_WorksAfterSourceDisposed()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var sourceA = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceB = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var setA = new CaptureSet();
            var setB = new CaptureSet();
            var targetSet = new CaptureSet();
            sourceA.AddAttributeSet(setA);
            sourceB.AddAttributeSet(setB);
            target.AddAttributeSet(targetSet);
            try
            {
                setA.Power.SetBaseValue(3);
                setB.Power.SetBaseValue(4);
                targetSet.Health.SetBaseValue(100);
                Settle(sourceA);
                Settle(sourceB);
                Settle(target);

                GameplayEffect effectA = BuildPeriodic("Lifetime.SourceA",
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);
                GameplayEffect effectB = BuildPeriodic("Lifetime.SourceB",
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effectA, sourceA)).Succeeded, Is.True);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effectB, sourceB)).Succeeded, Is.True);

                GASEntityId sourceAId = sourceA.CoreEntity;
                sourceA.Dispose();

                Assert.That(target.RemoveActiveEffectsBySource(sourceAId), Is.EqualTo(1));
                Assert.That(target.FindActiveEffectByDef(effectA), Is.Null);
                Assert.That(target.FindActiveEffectByDef(effectB), Is.Not.Null);
            }
            finally
            {
                target.Dispose();
                sourceA.Dispose();
                sourceB.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SourceDisposed_StackedEffectRemovalStillUnindexes()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(2);
                targetSet.Health.SetBaseValue(0);
                Settle(source);
                Settle(target);

                var effect = new GameplayEffect(
                    "Lifetime.StackedRemoval",
                    EDurationPolicy.Infinite,
                    period: Period,
                    modifiers: new List<ModifierInfo> { AddFromPower(
                        EGameplayEffectAttributeCaptureSource.Source,
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot) },
                    stacking: new GameplayEffectStacking(EGameplayEffectStackingType.AggregateBySource, 5,
                        EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                    executePeriodicEffectOnApplication: false);

                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);
                Assert.That(target.GetCurrentStackCount(effect), Is.EqualTo(2));

                source.Dispose();

                ActiveGameplayEffect active = target.FindActiveEffectByDef(effect);
                Assert.That(active, Is.Not.Null);
                Assert.That(target.TryRemoveActiveEffect(active), Is.True);
                Assert.That(target.GetCurrentStackCount(effect), Is.EqualTo(0),
                    "Removal must still find the stack entry after the source reference is detached.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SourceDisposed_IsCollectibleWhileEffectRemains()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var targetSet = new CaptureSet();
            target.AddAttributeSet(targetSet);
            try
            {
                WeakReference<AbilitySystemComponent> weak = CreateSourceWithEffect(context, target, targetSet);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.That(weak.TryGetTarget(out _), Is.False,
                    "A disposed source must be collectible while its effect is still active.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1));
            }
            finally
            {
                target.Dispose();
                context.Dispose();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference<AbilitySystemComponent> CreateSourceWithEffect(
            GASRuntimeContext context,
            AbilitySystemComponent target,
            CaptureSet targetSet)
        {
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            sourceSet.Power.SetBaseValue(5);
            targetSet.Health.SetBaseValue(100);
            Settle(source);
            Settle(target);

            GameplayEffect effect = BuildPeriodic("Lifetime.Collect",
                EGameplayEffectAttributeCaptureSource.Source,
                EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);
            Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);

            var weak = new WeakReference<AbilitySystemComponent>(source);
            source.Dispose();
            return weak;
        }

        [Test]
        public void EntityId_FormatsForDiagnostics()
        {
            Assert.That(new GASEntityId(3).ToString(), Is.EqualTo("Entity#3"));
            Assert.That(default(GASEntityId).ToString(), Is.EqualTo("Entity#None"));
        }

        [Test]
        public void RemoveActiveEffectsBySource_AcceptsSourceComponent()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(3);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                GameplayEffect effect = BuildPeriodic("Lifetime.RemoveByComponent",
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);

                Assert.That(target.RemoveActiveEffectsBySource(source), Is.EqualTo(1));
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(0));
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        private static void AssertPeriodicCapture(
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            bool disposeSource)
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceSet = new CaptureSet();
            var targetSet = new CaptureSet();
            source.AddAttributeSet(sourceSet);
            target.AddAttributeSet(targetSet);
            try
            {
                sourceSet.Power.SetBaseValue(5);
                targetSet.Power.SetBaseValue(3);
                targetSet.Health.SetBaseValue(100);
                Settle(source);
                Settle(target);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(
                    BuildPeriodic($"Lifetime.{captureSide}.{snapshotPolicy}.{disposeSource}", captureSide, snapshotPolicy),
                    source);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

                target.Tick(Period, isServer: true);
                int firstDelta = captureSide == EGameplayEffectAttributeCaptureSource.Source ? 5 : 3;
                Assert.That(targetSet.Health.CurrentValueRaw,
                    Is.EqualTo(GASFixedValue.FromInt(100 + firstDelta).RawValue),
                    "First tick must read the value captured at creation or application.");

                sourceSet.Power.SetBaseValue(7);
                targetSet.Power.SetBaseValue(4);
                Settle(source);
                Settle(target);

                if (disposeSource)
                {
                    source.Dispose();
                }

                target.Tick(Period, isServer: true);

                int secondDelta;
                if (captureSide == EGameplayEffectAttributeCaptureSource.Source)
                {
                    secondDelta = snapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot ? 7 : 5;
                }
                else
                {
                    secondDelta = snapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot ? 4 : 3;
                }

                Assert.That(targetSet.Health.CurrentValueRaw,
                    Is.EqualTo(GASFixedValue.FromInt(100 + firstDelta + secondDelta).RawValue),
                    $"Unexpected magnitude for {captureSide}/{snapshotPolicy}/disposed={disposeSource}.");
                Assert.That(target.ActiveEffects.Count, Is.EqualTo(1),
                    "The periodic effect must remain active for every capture combination.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        private static void Settle(AbilitySystemComponent asc)
        {
            asc.Tick(SettleDelta, isServer: true);
        }

        private static GameplayEffect BuildPeriodic(
            string name,
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: Period,
                modifiers: new List<ModifierInfo> { AddFromPower(captureSide, snapshotPolicy) },
                executePeriodicEffectOnApplication: false);
        }

        private static ModifierInfo AddFromPower(
            EGameplayEffectAttributeCaptureSource captureSide,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy)
        {
            return new ModifierInfo(
                "Health",
                EAttributeModifierOperation.Add,
                new AttributeBasedMagnitude(
                    "Power",
                    captureSide,
                    EAttributeBasedFloatCalculationType.AttributeMagnitude,
                    new ScalableFloat(1f),
                    new ScalableFloat(0f),
                    new ScalableFloat(0f),
                    snapshotPolicy));
        }

        private sealed class CaptureSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Power);
            }
        }

        private sealed class SourcePowerExecution : GameplayEffectExecutionCalculation
        {
            private readonly bool declareCapture;

            public SourcePowerExecution(bool declareCapture)
            {
                this.declareCapture = declareCapture;
            }

            public override void GetAttributeCaptureDefinitions(
                List<GameplayAttributeCaptureDefinition> definitions)
            {
                if (declareCapture)
                {
                    definitions.Add(new GameplayAttributeCaptureDefinition(
                        "Power", EGameplayEffectAttributeCaptureSource.Source));
                }
            }

            public override void Execute(GameplayEffectSpec spec, GameplayEffectExecutionOutput executionOutput)
            {
                executionOutput.Add(AddFromPower(
                    EGameplayEffectAttributeCaptureSource.Source,
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));
            }
        }

        private sealed class SourcePowerMagnitude : GameplayModMagnitudeCalculation
        {
            private readonly bool declareCapture;
            private readonly EGameplayEffectAttributeCaptureSource captureSide;

            public SourcePowerMagnitude(
                bool declareCapture,
                EGameplayEffectAttributeCaptureSource captureSide = EGameplayEffectAttributeCaptureSource.Source)
            {
                this.declareCapture = declareCapture;
                this.captureSide = captureSide;
            }

            public override void GetAttributeCaptureDefinitions(
                List<GameplayAttributeCaptureDefinition> definitions)
            {
                if (declareCapture)
                {
                    definitions.Add(new GameplayAttributeCaptureDefinition("Power", captureSide));
                }
            }

            public override float CalculateMagnitude(GameplayEffectSpec spec)
            {
                return spec.TryGetCapturedAttribute(captureSide, "Power", out GameplayAttribute attribute)
                    ? attribute.CurrentValue
                    : 0f;
            }
        }
    }
}
