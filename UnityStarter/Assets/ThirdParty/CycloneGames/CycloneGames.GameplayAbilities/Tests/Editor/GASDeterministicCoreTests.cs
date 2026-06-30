using System.Collections.Generic;

using NUnit.Framework;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASDeterministicCoreTests
    {
        [TearDown]
        public void TearDown()
        {
            PoolManager.ClearAllPools();
            GASServices.Reset();
            TrackingAbility.ResetCounters();
            TrackingTask.ResetCounters();
        }

        [Test]
        public void AttributeValues_StoreRawFixedValues()
        {
            var attribute = new GASAttributeValueData(
                new GASAttributeId(1),
                GASFixedValue.FromFloat(10.5f),
                GASFixedValue.FromFloat(7.25f),
                1u);

            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(10.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(7.25f).RawValue));
        }

        [Test]
        public void FixedValue_OperatorsPreserveRawDeterminism()
        {
            var a = GASFixedValue.FromFloat(10.5f);
            var b = GASFixedValue.FromFloat(2f);

            Assert.That((a + b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(12.5f).RawValue));
            Assert.That((a - b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(8.5f).RawValue));
            Assert.That((a * b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(21f).RawValue));
            Assert.That((a / b).RawValue, Is.EqualTo(GASFixedValue.FromFloat(5.25f).RawValue));
            Assert.That(a > b, Is.True);
        }

        [Test]
        public void InstantModifier_UsesDeterministicRawMath()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);

            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromFloat(-12.5f)));

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
        }

        [Test]
        public void RawAttributeApi_AvoidsFloatRoundTrip()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            long baseRaw = GASFixedValue.FromFloat(100f).RawValue;
            long deltaRaw = GASFixedValue.FromFloat(-12.5f).RawValue;

            state.SetAttributeBaseRaw(health, baseRaw);
            state.ApplyInstantModifierRaw(health, GASModifierOp.Add, deltaRaw);

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(attribute.BaseValueRaw));
        }

        [Test]
        public void ModifierData_DefaultsToChannel0()
        {
            var health = new GASAttributeId(100);
            var modifier = new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(5));

            Assert.That(modifier.EvaluationChannel, Is.EqualTo(GASModifierEvaluationChannel.Channel0));
        }

        [Test]
        public void ActiveEffectModifiers_EvaluateChannelsInOrder()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            state.SetAttributeBase(health, GASFixedValue.FromInt(10));

            var modifiers = new[]
            {
                new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(5), GASModifierEvaluationChannel.Channel0),
                new GASModifierData(health, GASModifierOp.Multiply, GASFixedValue.FromInt(2), GASModifierEvaluationChannel.Channel1),
                new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(3), GASModifierEvaluationChannel.Channel2)
            };

            state.ApplyGameplayEffectSpecToSelf(new GASGameplayEffectSpecData(
                new GASDefinitionId(1),
                new GASEntityId(2),
                default,
                GASEffectDurationPolicy.Duration,
                1,
                1,
                0,
                100,
                modifiers,
                0,
                modifiers.Length));

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(33).RawValue));
        }

        [Test]
        public void CoreChecksum_IncludesModifierEvaluationChannel()
        {
            var channel0State = BuildSingleModifierState(GASModifierEvaluationChannel.Channel0);
            var channel1State = BuildSingleModifierState(GASModifierEvaluationChannel.Channel1);

            Assert.That(channel0State.TryGetAttribute(new GASAttributeId(100), out var channel0Attribute), Is.True);
            Assert.That(channel1State.TryGetAttribute(new GASAttributeId(100), out var channel1Attribute), Is.True);
            Assert.That(channel0Attribute.CurrentValueRaw, Is.EqualTo(channel1Attribute.CurrentValueRaw));
            Assert.That(channel0State.ComputeChecksum().Effects, Is.Not.EqualTo(channel1State.ComputeChecksum().Effects));
        }

        [Test]
        public void AttributeBasedMagnitude_UsesUnrealStyleFormula()
        {
            var source = CreateMagnitudeTestAsc(out var sourceAttributes);
            var target = CreateMagnitudeTestAsc(out _);
            sourceAttributes.SetBaseValue(sourceAttributes.Power, GASFixedValue.FromInt(10));
            sourceAttributes.SetCurrentValue(sourceAttributes.Power, GASFixedValue.FromInt(15));

            var modifiers = new List<ModifierInfo>
            {
                new ModifierInfo(
                    "Health",
                    EAttributeModifierOperation.Add,
                    new AttributeBasedMagnitude(
                        "Power",
                        EGameplayEffectAttributeCaptureSource.Source,
                        EAttributeBasedFloatCalculationType.AttributeMagnitude,
                        new ScalableFloat(2f),
                        new ScalableFloat(1f),
                        new ScalableFloat(3f)))
            };
            var effect = new GameplayEffect("FormulaCheck", EDurationPolicy.Instant, modifiers: modifiers);
            var spec = GameplayEffectSpec.Create(effect, source);

            spec.SetTarget(target);

            Assert.That(spec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(GASFixedValue.FromInt(35).RawValue));

            spec.ReturnToPool();
            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void AttributeBasedMagnitude_TargetCapture_RecalculatesWhenTargetAssigned()
        {
            var source = CreateMagnitudeTestAsc(out _);
            var target = CreateMagnitudeTestAsc(out var targetAttributes);
            targetAttributes.SetBaseValue(targetAttributes.Armor, GASFixedValue.FromInt(4));
            targetAttributes.SetCurrentValue(targetAttributes.Armor, GASFixedValue.FromInt(8));

            var modifiers = new List<ModifierInfo>
            {
                new ModifierInfo(
                    "Health",
                    EAttributeModifierOperation.Add,
                    new AttributeBasedMagnitude(
                        "Armor",
                        EGameplayEffectAttributeCaptureSource.Target,
                        EAttributeBasedFloatCalculationType.AttributeMagnitude))
            };
            var effect = new GameplayEffect("TargetCapture", EDurationPolicy.Instant, modifiers: modifiers);
            var spec = GameplayEffectSpec.Create(effect, source);

            Assert.That(spec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(0L));

            spec.SetTarget(target);

            Assert.That(spec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(GASFixedValue.FromInt(8).RawValue));

            spec.ReturnToPool();
            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void SetByCallerMagnitude_UsesSpecTagValue()
        {
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Magnitude.SetByCaller", "SetByCaller magnitude test tag");
            GameplayTagManager.InitializeIfNeeded();
            var dataTag = GameplayTagManager.RequestTag("Test.GAS.Magnitude.SetByCaller");
            var source = CreateMagnitudeTestAsc(out _);
            var target = CreateMagnitudeTestAsc(out _);
            var modifiers = new List<ModifierInfo>
            {
                new ModifierInfo(
                    "Health",
                    EAttributeModifierOperation.Add,
                    new SetByCallerMagnitude(dataTag))
            };
            var effect = new GameplayEffect("SetByCallerCheck", EDurationPolicy.Instant, modifiers: modifiers);
            var spec = GameplayEffectSpec.Create(effect, source);

            spec.SetSetByCallerMagnitude(dataTag, GASFixedValue.FromFloat(12.5f));
            spec.SetTarget(target);

            Assert.That(spec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(GASFixedValue.FromFloat(12.5f).RawValue));

            spec.ReturnToPool();
            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void NotSnapshotAttributeBasedMagnitude_RecalculatesDependentTargetAttribute()
        {
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            attributes.SetBaseValue(attributes.Power, GASFixedValue.FromInt(10));
            asc.Tick(0f, true);

            var modifiers = new List<ModifierInfo>
            {
                new ModifierInfo(
                    "Health",
                    EAttributeModifierOperation.Add,
                    new AttributeBasedMagnitude(
                        "Power",
                        EGameplayEffectAttributeCaptureSource.Target,
                        EAttributeBasedFloatCalculationType.AttributeMagnitude,
                        new ScalableFloat(1f),
                        new ScalableFloat(0f),
                        new ScalableFloat(0f),
                        EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
            };
            var effect = new GameplayEffect("LivePowerBuff", EDurationPolicy.Infinite, modifiers: modifiers);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            attributes.SetBaseValue(attributes.Power, GASFixedValue.FromInt(20));
            asc.Tick(0f, true);

            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(120).RawValue));

            asc.Dispose();
        }

        [Test]
        public void Facade_FixedValueApi_UsesRawCorePath()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var facade = new GASAbilitySystemFacade(state);
            var health = new GASAttributeId(100);

            facade.SetNumericAttributeBase(health, GASFixedValue.FromInt(100));
            facade.ApplyInstantModifier(health, GASModifierOp.Add, GASFixedValue.FromFloat(-12.5f));

            Assert.That(facade.GetGameplayAttributeFixedValues(health, out var baseValue, out var currentValue), Is.True);
            Assert.That(baseValue.RawValue, Is.EqualTo(GASFixedValue.FromFloat(87.5f).RawValue));
            Assert.That(currentValue.RawValue, Is.EqualTo(baseValue.RawValue));
        }

        [Test]
        public void SnapshotTypes_StoreRawFixedValues()
        {
            long baseRaw = GASFixedValue.FromFloat(10f).RawValue;
            long currentRaw = GASFixedValue.FromFloat(8.5f).RawValue;
            long durationRaw = GASFixedValue.FromFloat(3.25f).RawValue;

            var attribute = GASAttributeStateData.FromRaw("Health", baseRaw, currentRaw);
            var effect = GASActiveEffectStateData.FromRaw(
                1,
                null,
                null,
                1,
                2,
                durationRaw,
                durationRaw,
                0L,
                default,
                null,
                0);

            Assert.That(attribute.BaseValueRaw, Is.EqualTo(baseRaw));
            Assert.That(attribute.CurrentValueRaw, Is.EqualTo(currentRaw));
            Assert.That(effect.DurationRaw, Is.EqualTo(durationRaw));
            Assert.That(effect.TimeRemainingRaw, Is.EqualTo(durationRaw));
        }

        [Test]
        public void EffectReplicationData_StoresRawTimeAndSetByCallerValues()
        {
            long durationRaw = GASFixedValue.FromFloat(12.5f).RawValue;
            long remainingRaw = GASFixedValue.FromFloat(4.25f).RawValue;
            long setByCallerRaw = GASFixedValue.FromFloat(1.75f).RawValue;
            var setByCallerValuesRaw = new[] { setByCallerRaw };

            var data = new GASEffectReplicationData
            {
                NetworkId = 1,
                EffectDefId = 2,
                SourceAscNetId = 3,
                TargetAscNetId = 4,
                Level = 5,
                StackCount = 2,
                DurationRaw = durationRaw,
                TimeRemainingRaw = remainingRaw,
                PeriodTimeRemainingRaw = 0L,
                SetByCallerValuesRaw = setByCallerValuesRaw,
                SetByCallerCount = setByCallerValuesRaw.Length
            };

            Assert.That(data.DurationRaw, Is.EqualTo(durationRaw));
            Assert.That(data.TimeRemainingRaw, Is.EqualTo(remainingRaw));
            Assert.That(data.Duration.RawValue, Is.EqualTo(durationRaw));
            Assert.That(data.TimeRemaining.RawValue, Is.EqualTo(remainingRaw));
            Assert.That(data.SetByCallerValuesRaw[0], Is.EqualTo(setByCallerRaw));
        }

        [Test]
        public void GameplayCueEventParams_StoresRawDuration()
        {
            long durationRaw = GASFixedValue.FromFloat(2.5f).RawValue;

            var parameters = new GameplayCueEventParams(
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                durationRaw);

            Assert.That(parameters.EffectDurationRaw, Is.EqualTo(durationRaw));
            Assert.That(parameters.EffectDuration.RawValue, Is.EqualTo(durationRaw));
        }

        [Test]
        public void DeterministicTimeProvider_AdvancesWithRawTicks()
        {
            var time = new DeterministicTimeProvider();
            long deltaRaw = GASFixedValue.FromFloat(0.125f).RawValue;

            time.TickRaw(deltaRaw);
            time.Tick(GASFixedValue.FromFloat(0.25f));

            Assert.That(time.DeltaTimeRaw, Is.EqualTo(GASFixedValue.FromFloat(0.25f).RawValue));
            Assert.That(time.TotalTimeRaw, Is.EqualTo(deltaRaw + GASFixedValue.FromFloat(0.25f).RawValue));
            Assert.That(time.FrameCount, Is.EqualTo(2));
        }

        [Test]
        public void DeterministicRandomProvider_UsesStableFixedSequence()
        {
            var a = new DeterministicRandomProvider(12345);
            var b = new DeterministicRandomProvider(12345);

            Assert.That(a.NextRaw(), Is.EqualTo(b.NextRaw()));
            Assert.That(a.NextFixed().RawValue, Is.EqualTo(b.NextFixed().RawValue));
            Assert.That(a.NextInt(1, 100), Is.EqualTo(b.NextInt(1, 100)));
        }

        [Test]
        public void PredictionReject_RestoresRawBaseValue()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            var prediction = new GASPredictionKey(1, new GASEntityId(99), 7);

            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(-20)), prediction);
            state.RejectPrediction(prediction);

            Assert.That(state.TryGetAttribute(health, out var attribute), Is.True);
            Assert.That(attribute.BaseValueRaw, Is.EqualTo(GASFixedValue.FromFloat(100f).RawValue));
        }

        [Test]
        public void Checksum_IsStableForSameRawState()
        {
            var a = BuildState();
            var b = BuildState();

            Assert.That(a.ComputeChecksum().Combined, Is.EqualTo(b.ComputeChecksum().Combined));
        }

        [Test]
        public void AbilitySystemComponent_DefaultMirrorMode_SynchronizesCoreState()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var ability = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);
            var spec = asc.GrantAbility(ability);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(CreateDurationEffect("MirrorEffect"), asc));

            var diagnostics = asc.GetRuntimeDiagnostics(computeChecksum: false);

            Assert.That(asc.CoreStateMode, Is.EqualTo(GASCoreStateMode.MirrorRuntime));
            Assert.That(asc.IsCoreStateEnabled, Is.True);
            Assert.That(asc.TryGetCoreState(out var coreState), Is.True);
            Assert.That(coreState, Is.Not.Null);
            Assert.That(asc.TryGetCoreSpecHandle(spec, out var coreHandle), Is.True);
            Assert.That(coreHandle.IsValid, Is.True);
            Assert.That(diagnostics.IsCoreStateEnabled, Is.True);
            Assert.That(diagnostics.CoreAbilitySpecCount, Is.EqualTo(1));
            Assert.That(diagnostics.CoreSpecHandleCount, Is.EqualTo(1));
            Assert.That(diagnostics.CoreActiveEffectCount, Is.EqualTo(1));
            Assert.That(diagnostics.CoreActiveEffectHandleCount, Is.EqualTo(1));
            Assert.That(diagnostics.HasCriticalIssues, Is.False);

            asc.Dispose();
        }

        [Test]
        public void AbilitySystemComponent_RuntimeOnlyMode_SkipsCoreMirror()
        {
            var asc = new AbilitySystemComponent(
                new GameplayEffectContextFactory(),
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var ability = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);
            var spec = asc.GrantAbility(ability);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(CreateDurationEffect("RuntimeOnlyEffect"), asc));

            var diagnostics = asc.GetRuntimeDiagnostics(computeChecksum: false);

            Assert.That(asc.CoreStateMode, Is.EqualTo(GASCoreStateMode.RuntimeOnly));
            Assert.That(asc.IsCoreStateEnabled, Is.False);
            Assert.That(asc.TryGetCoreState(out var coreState), Is.False);
            Assert.That(coreState, Is.Null);
            Assert.That(asc.TryGetCoreFacade(out var coreFacade), Is.False);
            Assert.That(coreFacade, Is.Null);
            Assert.That(asc.TryGetCoreSpecHandle(spec, out var coreHandle), Is.False);
            Assert.That(coreHandle.IsValid, Is.False);
            Assert.That(asc.AbilitySpecs.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));
            Assert.That(diagnostics.IsCoreStateEnabled, Is.False);
            Assert.That(diagnostics.CoreAbilitySpecCount, Is.EqualTo(0));
            Assert.That(diagnostics.CoreSpecHandleCount, Is.EqualTo(0));
            Assert.That(diagnostics.CoreActiveEffectCount, Is.EqualTo(0));
            Assert.That(diagnostics.CoreActiveEffectHandleCount, Is.EqualTo(0));
            Assert.That(diagnostics.HasCriticalIssues, Is.False);

            asc.Dispose();
        }

        [Test]
        public void ClearAbility_CallsInstancedAbilityRemoveHook()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var template = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);

            var spec = asc.GrantAbility(template);
            Assert.That(spec.GetPrimaryInstance(), Is.Not.SameAs(template));

            asc.ClearAbility(spec);

            Assert.That(TrackingAbility.InstanceRemoveCount, Is.EqualTo(1));
            Assert.That(TrackingAbility.TemplateRemoveCount, Is.EqualTo(0));

            asc.Dispose();
        }

        [Test]
        public void EndAbility_ReturnsInactiveTasksToPool()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var template = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);
            var spec = asc.GrantAbility(template);
            var instance = (TrackingAbility)spec.GetPrimaryInstance();

            var task = instance.CreateInactiveTaskForTest();
            Assert.That(task.IsActive, Is.False);

            instance.EndAbility();

            Assert.That(TrackingTask.DestroyCount, Is.EqualTo(1));

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void AbilitySpecContainer_RemoveMiddleSpec_PreservesLookupIndexes()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var first = asc.GrantAbility(new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor));
            var middle = asc.GrantAbility(new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor));
            var last = asc.GrantAbility(new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor));
            int firstHandle = first.Handle;
            int middleHandle = middle.Handle;
            int lastHandle = last.Handle;

            asc.ClearAbility(middle);

            Assert.That(asc.AbilitySpecs.Count, Is.EqualTo(2));
            Assert.That(asc.AbilitySpecs.TryGetSpecByHandle(firstHandle, out var firstLookup), Is.True);
            Assert.That(firstLookup, Is.SameAs(first));
            Assert.That(asc.AbilitySpecs.TryGetSpecByHandle(middleHandle, out _), Is.False);
            Assert.That(asc.AbilitySpecs.TryGetSpecByHandle(lastHandle, out var lastLookup), Is.True);
            Assert.That(lastLookup, Is.SameAs(last));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(first);
            asc.ClearAbility(last);
            asc.Dispose();
        }

        [Test]
        public void ActiveEffectContainer_RemoveMiddleEffect_PreservesSwapBackIndexes()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var firstSpec = GameplayEffectSpec.Create(CreateDurationEffect("First"), asc);
            var middleSpec = GameplayEffectSpec.Create(CreateDurationEffect("Middle"), asc);
            var lastSpec = GameplayEffectSpec.Create(CreateDurationEffect("Last"), asc);

            asc.ApplyGameplayEffectSpecToSelf(firstSpec);
            asc.ApplyGameplayEffectSpecToSelf(middleSpec);
            asc.ApplyGameplayEffectSpecToSelf(lastSpec);

            var first = asc.ActiveEffects[0];
            var middle = asc.ActiveEffects[1];
            var last = asc.ActiveEffects[2];

            Assert.That(asc.TryRemoveActiveEffect(middle), Is.True);

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(2));
            Assert.That(asc.ActiveEffectContainer.TryFindIndex(first, out _), Is.True);
            Assert.That(asc.ActiveEffectContainer.TryFindIndex(middle, out _), Is.False);
            Assert.That(asc.ActiveEffectContainer.TryFindIndex(last, out _), Is.True);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.Dispose();
        }

        [Test]
        public void ActiveEffectContainer_NetworkIdIndex_RebuildsAfterIdChanges()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var spec = GameplayEffectSpec.Create(CreateDurationEffect("Networked"), asc);
            var activeEffect = ActiveGameplayEffect.Create(spec);
            var container = new ActiveEffectContainer();

            container.AddEffect(activeEffect);
            container.SetNetworkId(activeEffect, 42);

            Assert.That(container.FindByNetworkId(42), Is.SameAs(activeEffect));

            container.SetNetworkId(activeEffect, 84);

            Assert.That(container.FindByNetworkId(42), Is.Null);
            Assert.That(container.FindByNetworkId(84), Is.SameAs(activeEffect));

            container.RemoveAtSwapBack(0);

            Assert.That(container.FindByNetworkId(84), Is.Null);
            Assert.That(container.ValidateIndexes(), Is.True);

            activeEffect.ReturnToPool();
            asc.Dispose();
        }

        [Test]
        public void ActiveEffectContainer_UntracksAbilityAppliedEffectBeforePoolReuse()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var ability = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);
            var spec = GameplayEffectSpec.Create(CreateDurationEffect("TrackedByAbility"), asc);
            var activeEffect = ActiveGameplayEffect.Create(spec);
            var container = new ActiveEffectContainer();
            int returnedLists = 0;

            container.AddEffect(activeEffect);
            container.TrackAbilityAppliedEffect(
                ability,
                activeEffect,
                () => new System.Collections.Generic.List<ActiveGameplayEffect>(4));

            Assert.That(container.AbilityEffectIndexCount, Is.EqualTo(1));

            container.RemoveAtSwapBack(0);
            container.UntrackAppliedEffectFromAbilities(
                activeEffect,
                effects =>
                {
                    effects.Clear();
                    returnedLists++;
                });

            Assert.That(container.AbilityEffectIndexCount, Is.EqualTo(0));
            Assert.That(returnedLists, Is.EqualTo(1));
            Assert.That(container.ValidateIndexes(), Is.True);

            activeEffect.ReturnToPool();
            asc.Dispose();
        }

        [Test]
        public void ActiveEffectContainer_StackingIndex_AggregatesByTarget()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var effect = CreateDurationEffect(
                "Stacking",
                new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            Assert.That(asc.TryRemoveActiveEffect(asc.ActiveEffects[0]), Is.True);
            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(0));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectStacking_RefreshesDurationAndAggregatesModifierMagnitude()
        {
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);

            var effect = CreateAttributeModifierEffect(
                "StackingHealthBuff",
                EDurationPolicy.HasDuration,
                5f,
                "Health",
                10f,
                new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));
            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));

            var activeEffect = asc.ActiveEffects[0];
            asc.Tick(3f, true);
            Assert.That(activeEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromInt(2).RawValue));

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));
            Assert.That(activeEffect.StackCount, Is.EqualTo(2));
            Assert.That(activeEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromInt(5).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(120).RawValue));

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectStacking_OverflowAppliesOverflowEffectAtLimit()
        {
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            attributes.SetBaseValue(attributes.Armor, GASFixedValue.Zero);
            asc.Tick(0f, true);

            var overflowEffect = CreateAttributeModifierEffect(
                "OverflowArmorReward",
                EDurationPolicy.Instant,
                0f,
                "Armor",
                25f);
            var stackEffect = CreateAttributeModifierEffect(
                "LimitedStackingHealthBuff",
                EDurationPolicy.HasDuration,
                5f,
                "Health",
                10f,
                new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    1,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                overflowEffects: new List<GameplayEffect> { overflowEffect },
                denyOverflowApplication: true);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(stackEffect, asc));
            asc.Tick(0f, true);

            var activeEffect = asc.ActiveEffects[0];
            activeEffect.SetRemainingDuration(2f);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(stackEffect, asc));
            asc.Tick(0f, true);

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));
            Assert.That(activeEffect.StackCount, Is.EqualTo(1));
            Assert.That(activeEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromInt(2).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));
            Assert.That(attributes.Armor.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(25).RawValue));
            Assert.That(attributes.Armor.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(25).RawValue));

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectExpiration_RemovesDurationEffectAndRestoresModifier()
        {
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);

            var effect = CreateAttributeModifierEffect(
                "ExpiringHealthBuff",
                EDurationPolicy.HasDuration,
                1f,
                "Health",
                10f);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            asc.Tick(1.1f, true);
            asc.Tick(0f, true);

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(0));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectExpiration_RemoveSingleStackRefreshesDuration()
        {
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);

            var effect = CreateAttributeModifierEffect(
                "SingleStackExpirationBuff",
                EDurationPolicy.HasDuration,
                1f,
                "Health",
                10f,
                new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
                    EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration));

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, true);

            var activeEffect = asc.ActiveEffects[0];
            Assert.That(activeEffect.StackCount, Is.EqualTo(3));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(130).RawValue));

            asc.Tick(1.1f, true);
            asc.Tick(0f, true);

            Assert.That(asc.ActiveEffectContainer.Count, Is.EqualTo(1));
            Assert.That(activeEffect.StackCount, Is.EqualTo(2));
            Assert.That(activeEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromInt(1).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(120).RawValue));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectOngoingRequirements_InhibitsAndRestoresModifier()
        {
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Effect.Powered", "Ongoing requirement test tag");
            GameplayTagManager.InitializeIfNeeded();
            var requiredTag = GameplayTagManager.RequestTag("Test.GAS.Effect.Powered");
            var requiredTags = new GameplayTagContainer();
            requiredTags.AddTag(requiredTag);
            var ongoingRequirements = new GameplayTagRequirements(new GameplayTagContainer(), requiredTags);
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);

            var effect = CreateAttributeModifierEffect(
                "ConditionalHealthBuff",
                EDurationPolicy.Infinite,
                0f,
                "Health",
                10f,
                ongoingTagRequirements: ongoingRequirements);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            var activeEffect = asc.ActiveEffects[0];
            Assert.That(activeEffect.IsInhibited, Is.True);

            int inhibitionChangeCount = 0;
            bool lastInhibitionState = false;
            activeEffect.OnInhibitionChanged += inhibited =>
            {
                inhibitionChangeCount++;
                lastInhibitionState = inhibited;
            };

            asc.Tick(0f, true);

            Assert.That(activeEffect.IsInhibited, Is.True);
            Assert.That(lastInhibitionState, Is.False);
            Assert.That(inhibitionChangeCount, Is.EqualTo(0));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));

            asc.AddLooseGameplayTag(requiredTag);
            asc.Tick(0f, true);

            Assert.That(activeEffect.IsInhibited, Is.False);
            Assert.That(lastInhibitionState, Is.False);
            Assert.That(inhibitionChangeCount, Is.EqualTo(1));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            asc.RemoveLooseGameplayTag(requiredTag);
            asc.Tick(0f, true);

            Assert.That(activeEffect.IsInhibited, Is.True);
            Assert.That(lastInhibitionState, Is.True);
            Assert.That(inhibitionChangeCount, Is.EqualTo(2));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));

            asc.Dispose();
        }

        [Test]
        public void GameplayEffectOngoingRequirements_InhibitsPeriodicExecution()
        {
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Effect.PeriodicEnabled", "Periodic inhibition test tag");
            GameplayTagManager.InitializeIfNeeded();
            var requiredTag = GameplayTagManager.RequestTag("Test.GAS.Effect.PeriodicEnabled");
            var requiredTags = new GameplayTagContainer();
            requiredTags.AddTag(requiredTag);
            var ongoingRequirements = new GameplayTagRequirements(new GameplayTagContainer(), requiredTags);
            var asc = CreateMagnitudeTestAsc(out var attributes);
            attributes.SetBaseValue(attributes.Health, GASFixedValue.FromInt(100));
            asc.Tick(0f, true);

            var effect = CreateAttributeModifierEffect(
                "ConditionalPeriodicDamage",
                EDurationPolicy.Infinite,
                0f,
                "Health",
                -5f,
                ongoingTagRequirements: ongoingRequirements,
                period: 1f);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            var activeEffect = asc.ActiveEffects[0];

            asc.Tick(0f, true);

            Assert.That(activeEffect.IsInhibited, Is.True);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));

            asc.AddLooseGameplayTag(requiredTag);
            asc.Tick(0f, true);

            Assert.That(activeEffect.IsInhibited, Is.False);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(95).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(95).RawValue));

            asc.RemoveLooseGameplayTag(requiredTag);
            asc.Tick(1f, true);

            Assert.That(activeEffect.IsInhibited, Is.True);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(95).RawValue));
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(95).RawValue));

            asc.Dispose();
        }

        [Test]
        public void PredictionManager_RemoveMiddleWindow_PreservesIndexes()
        {
            var manager = new PredictionManager();
            var first = new GASPredictionKey(101, new GASEntityId(1), 1);
            var middle = new GASPredictionKey(102, new GASEntityId(1), 2);
            var last = new GASPredictionKey(103, new GASEntityId(1), 3);

            manager.RegisterWindow(CreatePredictionWindow(first));
            manager.RegisterWindow(CreatePredictionWindow(middle));
            manager.RegisterWindow(CreatePredictionWindow(last));

            Assert.That(manager.TryRemoveWindow(middle, out var removed), Is.True);

            Assert.That(removed.PredictionKey, Is.EqualTo(middle));
            Assert.That(manager.WindowCount, Is.EqualTo(2));
            Assert.That(manager.HasOpenWindow(first), Is.True);
            Assert.That(manager.HasOpenWindow(middle), Is.False);
            Assert.That(manager.HasOpenWindow(last), Is.True);
            Assert.That(manager.ValidateIndexes(), Is.True);
            Assert.That(manager.GetStats().TotalOpenedCount, Is.EqualTo(3));
        }

        [Test]
        public void PredictionManager_TransactionRecords_PreserveRecentOrderAfterGrow()
        {
            var manager = new PredictionManager(transactionRecordCapacity: 2);
            var first = new GASPredictionKey(201, new GASEntityId(2), 1);
            var second = new GASPredictionKey(202, new GASEntityId(2), 2);
            var third = new GASPredictionKey(203, new GASEntityId(2), 3);

            manager.RecordTransaction(CreatePredictionWindow(first), GASPredictionWindowStatus.Confirmed, GASPredictionRollbackFlags.None, 10);
            manager.RecordTransaction(CreatePredictionWindow(second), GASPredictionWindowStatus.Rejected, GASPredictionRollbackFlags.ActiveEffects, 20);
            manager.RecordTransaction(CreatePredictionWindow(third), GASPredictionWindowStatus.TimedOut, GASPredictionRollbackFlags.DependentWindows, 30);

            Assert.That(manager.TryGetClosedTransactionRecord(0, out var newest), Is.True);
            Assert.That(newest.PredictionKey, Is.EqualTo(third));
            Assert.That(manager.TryGetClosedTransactionRecord(1, out var previous), Is.True);
            Assert.That(previous.PredictionKey, Is.EqualTo(second));

            manager.EnsureTransactionRecordCapacity(4);

            Assert.That(manager.TryGetClosedTransactionRecord(0, out newest), Is.True);
            Assert.That(newest.PredictionKey, Is.EqualTo(third));
            Assert.That(manager.TryGetClosedTransactionRecord(1, out previous), Is.True);
            Assert.That(previous.PredictionKey, Is.EqualTo(second));
            Assert.That(manager.GetStats().ClosedTransactionRecordCapacity, Is.EqualTo(4));
        }

        [Test]
        public void PredictionManager_PendingPredictedEffects_TakeOnlyMatchingKey()
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory());
            var manager = new PredictionManager();
            var firstKey = new GASPredictionKey(301, new GASEntityId(3), 1);
            var secondKey = new GASPredictionKey(302, new GASEntityId(3), 2);
            var firstEffect = CreatePredictedActiveEffect("PredictedFirst", asc, firstKey);
            var secondEffect = CreatePredictedActiveEffect("PredictedSecond", asc, secondKey);

            manager.AddPendingPredictedEffect(firstEffect);
            manager.AddPendingPredictedEffect(secondEffect);

            Assert.That(manager.TryTakePendingPredictedEffect(firstKey, out var taken), Is.True);

            Assert.That(taken, Is.SameAs(firstEffect));
            Assert.That(manager.PendingPredictedEffects.Count, Is.EqualTo(1));
            Assert.That(manager.PendingPredictedEffects[0], Is.SameAs(secondEffect));
            Assert.That(manager.TryTakePendingPredictedEffect(firstKey, out _), Is.False);
            Assert.That(manager.TryTakePendingPredictedEffect(secondKey, out taken), Is.True);
            Assert.That(taken, Is.SameAs(secondEffect));
            Assert.That(manager.PendingPredictedEffects.Count, Is.EqualTo(0));

            firstEffect.ReturnToPool();
            secondEffect.ReturnToPool();
            asc.Dispose();
        }

        [Test]
        public void ReplicationStateBuilder_DeduplicatesAndCompletesPendingState()
        {
            var builder = new ReplicationStateBuilder();
            var health = new GameplayAttribute("Health");
            var ability = new TrackingAbility(EGameplayAbilityInstancingPolicy.InstancedPerActor);
            var buffer = new GASAbilitySystemStateDeltaBuffer();

            builder.MarkGrantedAbilitiesDirty();
            builder.MarkActiveEffectsDirty();
            builder.MarkAttributeValueDirty(health);
            builder.MarkAttributeValueDirty(health);
            builder.MarkAttributeStructureDirty();
            builder.TrackRemovedEffectNetworkId(42);
            builder.TrackRemovedAbilityDefinition(ability);

            builder.BeginCapture(buffer);

            Assert.That(buffer.BaseVersion, Is.EqualTo(0UL));
            Assert.That(builder.StateVersion, Is.EqualTo(5UL));
            Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(1U));
            Assert.That(builder.DirtyAttributeValueSnapshots.Count, Is.EqualTo(1));
            Assert.That(builder.PendingRemovedEffectNetIds, Is.EqualTo(new[] { 42 }));
            Assert.That(builder.PendingRemovedAbilityDefs.Count, Is.EqualTo(1));
            Assert.That(builder.PendingMask.HasFlag(AbilitySystemStateChangeMask.GrantedAbilities), Is.True);
            Assert.That(builder.PendingMask.HasFlag(AbilitySystemStateChangeMask.ActiveEffects), Is.True);
            Assert.That(builder.PendingMask.HasFlag(AbilitySystemStateChangeMask.Attributes), Is.True);

            buffer.ChangeMask = builder.PendingMask;
            builder.CompleteCapture(buffer, 123UL);

            Assert.That(buffer.CurrentVersion, Is.EqualTo(5UL));
            Assert.That(buffer.StateChecksum, Is.EqualTo(123UL));
            Assert.That(builder.LastReplicatedStateVersion, Is.EqualTo(5UL));
            Assert.That(builder.PendingMask, Is.EqualTo(AbilitySystemStateChangeMask.None));
            Assert.That(builder.DirtyAttributeValueSnapshots.Count, Is.EqualTo(0));
            Assert.That(builder.PendingRemovedEffectNetIds.Count, Is.EqualTo(0));
            Assert.That(builder.PendingRemovedAbilityDefs.Count, Is.EqualTo(0));
        }

        [Test]
        public void ReplicationStateBuilder_CollapsesOppositeTagEdgesWithinOneCaptureWindow()
        {
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Replication.TagWindow", "Replication window test tag");
            GameplayTagManager.InitializeIfNeeded();
            var tag = GameplayTagManager.RequestTag("Test.GAS.Replication.TagWindow");
            var builder = new ReplicationStateBuilder();

            Assert.That(builder.TrackTagCountChange(tag, 1), Is.True);
            Assert.That(builder.PendingAddedTagSnapshots.Count, Is.EqualTo(1));
            Assert.That(builder.PendingRemovedTagSnapshots.Count, Is.EqualTo(0));

            Assert.That(builder.TrackTagCountChange(tag, 0), Is.True);

            Assert.That(builder.PendingAddedTagSnapshots.Count, Is.EqualTo(0));
            Assert.That(builder.PendingRemovedTagSnapshots.Count, Is.EqualTo(0));
            Assert.That(builder.PendingMask.HasFlag(AbilitySystemStateChangeMask.Tags), Is.True);
        }

        [Test]
        public void PoolManagerClearAllPools_ClearsSharedGasPools()
        {
            var context = GASPool<GameplayEffectContext>.Shared.Get();
            GASPool<GameplayEffectContext>.Shared.Return(context);

            Assert.That(GASPool<GameplayEffectContext>.Shared.GetStatistics().PoolSize, Is.EqualTo(1));

            PoolManager.ClearAllPools();

            Assert.That(GASPool<GameplayEffectContext>.Shared.GetStatistics().PoolSize, Is.EqualTo(0));
        }

        private static GameplayEffect CreateDurationEffect(string name, GameplayEffectStacking stacking = default)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.HasDuration,
                5f,
                stacking: stacking);
        }

        private static GameplayEffect CreateAttributeModifierEffect(
            string name,
            EDurationPolicy durationPolicy,
            float duration,
            string attributeName,
            float magnitude,
            GameplayEffectStacking stacking = default,
            GameplayTagRequirements ongoingTagRequirements = default,
            List<GameplayEffect> overflowEffects = null,
            bool denyOverflowApplication = false,
            float period = 0f)
        {
            return new GameplayEffect(
                name,
                durationPolicy,
                duration,
                period,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(attributeName, EAttributeModifierOperation.Add, new ScalableFloat(magnitude))
                },
                stacking: stacking,
                ongoingTagRequirements: ongoingTagRequirements,
                overflowEffects: overflowEffects,
                denyOverflowApplication: denyOverflowApplication);
        }

        private static GASPredictionWindowData CreatePredictionWindow(GASPredictionKey key)
        {
            return new GASPredictionWindowData(key, default, default, key.InputSequence, 1, 60);
        }

        private static ActiveGameplayEffect CreatePredictedActiveEffect(string name, AbilitySystemComponent asc, GASPredictionKey predictionKey)
        {
            var spec = GameplayEffectSpec.Create(CreateDurationEffect(name), asc);
            spec.Context.PredictionKey = predictionKey;
            return ActiveGameplayEffect.Create(spec);
        }

        private static AbilitySystemComponent CreateMagnitudeTestAsc(out MagnitudeTestAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(new GameplayEffectContextFactory(), GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new MagnitudeTestAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private static GASAbilitySystemState BuildState()
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            state.SetAttributeBase(health, GASFixedValue.FromInt(100));
            state.ApplyInstantModifier(new GASModifierData(health, GASModifierOp.Division, GASFixedValue.FromInt(4)));
            return state;
        }

        private static GASAbilitySystemState BuildSingleModifierState(GASModifierEvaluationChannel evaluationChannel)
        {
            var state = new GASAbilitySystemState(new GASEntityId(1));
            var health = new GASAttributeId(100);
            state.SetAttributeBase(health, GASFixedValue.FromInt(10));
            var modifiers = new[]
            {
                new GASModifierData(health, GASModifierOp.Add, GASFixedValue.FromInt(5), evaluationChannel)
            };

            state.ApplyGameplayEffectSpecToSelf(new GASGameplayEffectSpecData(
                new GASDefinitionId(1),
                new GASEntityId(2),
                default,
                GASEffectDurationPolicy.Duration,
                1,
                1,
                0,
                100,
                modifiers,
                0,
                modifiers.Length));

            return state;
        }

        private sealed class MagnitudeTestAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
            public GameplayAttribute Power { get; } = new GameplayAttribute("Power");
            public GameplayAttribute Armor { get; } = new GameplayAttribute("Armor");
        }

        private sealed class TrackingAbility : GameplayAbility
        {
            public static int TemplateRemoveCount;
            public static int InstanceRemoveCount;

            private readonly EGameplayAbilityInstancingPolicy policy;
            private readonly bool isTemplate;

            public TrackingAbility(EGameplayAbilityInstancingPolicy policy)
                : this(policy, true)
            {
            }

            private TrackingAbility(EGameplayAbilityInstancingPolicy policy, bool isTemplate)
            {
                this.policy = policy;
                this.isTemplate = isTemplate;
                Initialize(
                    "TrackingAbility",
                    policy,
                    ENetExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreatePoolableInstance()
            {
                return new TrackingAbility(policy, false);
            }

            public TrackingTask CreateInactiveTaskForTest()
            {
                return NewAbilityTask<TrackingTask>();
            }

            public override void OnRemoveAbility()
            {
                if (isTemplate)
                {
                    TemplateRemoveCount++;
                }
                else
                {
                    InstanceRemoveCount++;
                }

                base.OnRemoveAbility();
            }

            public static void ResetCounters()
            {
                TemplateRemoveCount = 0;
                InstanceRemoveCount = 0;
            }
        }

        private sealed class TrackingTask : AbilityTask
        {
            public static int DestroyCount;

            public TrackingTask()
            {
            }

            protected override void OnActivate()
            {
            }

            protected override void OnDestroy()
            {
                DestroyCount++;
                base.OnDestroy();
            }

            public static void ResetCounters()
            {
                DestroyCount = 0;
            }
        }
    }
}
