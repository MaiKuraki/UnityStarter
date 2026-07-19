using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASRuntimeHardeningTests
    {
        [Test]
        public void GASSmokeTest_RunBasicRuntimeValidationPasses()
        {
            bool passed = false;
            GASSmokeTestResult result = default;

            Assert.DoesNotThrow(() => passed = GASSmokeTest.RunBasicRuntimeValidation(out result));
            Assert.That(passed, Is.True, result.FailureFlags.ToString());
            Assert.That(result.Passed, Is.True);
            Assert.That(result.FailureFlags, Is.EqualTo(GASSmokeTestFailureFlags.None));
            Assert.That(result.RuntimeThreadViolationCount, Is.Zero);
        }

        [Test]
        public void GameplayCueScratchListPool_ReusesClearedListWithoutSteadyStateAllocation()
        {
            var pool = new GameplayCueScratchListPool<int>("Test", 4, 2, 16);
            try
            {
                GameplayCueScratchListLease<int> initialLease = pool.Rent();
                List<int> initialList = initialLease.Value;
                initialList.Add(7);
                pool.Return(initialLease);

                GameplayCueScratchListLease<int> reusedLease = pool.Rent();
                Assert.That(reusedLease.Value, Is.SameAs(initialList));
                Assert.That(reusedLease.Value, Is.Empty);
                pool.Return(reusedLease);

                for (int i = 0; i < 16; i++)
                {
                    GameplayCueScratchListLease<int> warmupLease = pool.Rent();
                    warmupLease.Value.Add(i);
                    pool.Return(warmupLease);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++)
                {
                    GameplayCueScratchListLease<int> lease = pool.Rent();
                    lease.Value.Add(i);
                    pool.Return(lease);
                }

                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
                Assert.That(pool.OutstandingCount, Is.Zero);
                Assert.That(pool.PeakOutstandingCount, Is.EqualTo(1));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void GameplayCueScratchListPool_DiscardsOversizedAndExcessInactiveEntries()
        {
            var pool = new GameplayCueScratchListPool<int>("Test", 3, 1, 2);
            try
            {
                GameplayCueScratchListLease<int> oversizedLease = pool.Rent();
                List<int> oversizedList = oversizedLease.Value;
                oversizedList.Capacity = 4;
                pool.Return(oversizedLease);

                Assert.That(pool.RetainedCount, Is.Zero);
                Assert.That(pool.DiscardedCount, Is.EqualTo(1));

                GameplayCueScratchListLease<int> firstLease = pool.Rent();
                GameplayCueScratchListLease<int> secondLease = pool.Rent();
                Assert.That(firstLease.Value, Is.Not.SameAs(oversizedList));
                pool.Return(firstLease);
                pool.Return(secondLease);

                Assert.That(pool.RetainedCount, Is.EqualTo(1));
                Assert.That(pool.DiscardedCount, Is.EqualTo(2));
                Assert.That(pool.OutstandingCount, Is.Zero);
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void GameplayCueScratchListPool_RejectsExhaustionForeignAndDuplicateReturn()
        {
            var owner = new GameplayCueScratchListPool<int>("Owner", 1, 1, 8);
            var foreign = new GameplayCueScratchListPool<int>("Foreign", 1, 1, 8);
            try
            {
                GameplayCueScratchListLease<int> lease = owner.Rent();
                Assert.Throws<InvalidOperationException>(() => owner.Rent());
                Assert.Throws<InvalidOperationException>(() => foreign.Return(lease));
                Assert.That(foreign.InvalidReturnCount, Is.EqualTo(1));

                var foreignEntry = new GameplayCueScratchListPool<int>.Entry(foreign)
                {
                    Generation = lease.Generation,
                    IsOutstanding = true
                };
                var forgedLease = new GameplayCueScratchListLease<int>(owner, foreignEntry, lease.Generation);
                Assert.Throws<InvalidOperationException>(() => owner.Return(forgedLease));
                Assert.That(owner.InvalidReturnCount, Is.EqualTo(1));

                owner.Return(lease);
                Assert.Throws<InvalidOperationException>(() => owner.Return(lease));
                Assert.That(owner.InvalidReturnCount, Is.EqualTo(2));

                GameplayCueScratchListLease<int> reusedLease = owner.Rent();
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = lease.Value;
                });
                Assert.That(owner.InvalidReturnCount, Is.EqualTo(3));
                owner.Return(reusedLease);
                Assert.That(owner.OutstandingCount, Is.Zero);
            }
            finally
            {
                owner.Dispose();
                foreign.Dispose();
            }
        }

        [Test]
        public void GameplayCueScratchListPool_DisposeRejectsRentAndDiscardsOutstandingReturn()
        {
            var pool = new GameplayCueScratchListPool<object>("Test", 2, 1, 8);
            GameplayCueScratchListLease<object> lease = pool.Rent();
            List<object> list = lease.Value;
            list.Add(new object());

            pool.Dispose();

            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(pool.RetainedCount, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => pool.Rent());
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = lease.Value;
            });

            pool.Return(lease);

            Assert.That(list, Is.Empty);
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.RetainedCount, Is.Zero);
            Assert.That(pool.DiscardedCount, Is.EqualTo(1));
        }

        [Test]
        public void GASTrace_SetCapacityRejectsUnboundedConfiguration()
        {
            int originalCapacity = GASTrace.Capacity;
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => GASTrace.SetCapacity(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => GASTrace.SetCapacity(GASTrace.MaxCapacity + 1));

                GASTrace.SetCapacity(1);

                Assert.That(GASTrace.Capacity, Is.EqualTo(1));
                Assert.That(GASTrace.Count, Is.Zero);
            }
            finally
            {
                GASTrace.SetCapacity(originalCapacity);
                GASTrace.Clear();
            }
        }

        [Test]
        public void GASTrace_EffectRemovalPublishesStableDefinitionAfterCommit()
        {
            int originalCapacity = GASTrace.Capacity;
            bool originalEnabled = GASTrace.Enabled;
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = CreateDurationEffect("TraceRemoval");
            try
            {
                GASTrace.SetCapacity(16);
                GASTrace.Enabled = true;

                GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, asc));
                Assert.That(application.Succeeded, Is.True);
                Assert.That(asc.TryRemoveActiveEffect(application.ActiveEffect), Is.True);
                Assert.That(GASTrace.TryGetRecent(0, out GASTraceEvent traceEvent), Is.True);
                Assert.That(traceEvent.Type, Is.EqualTo(GASTraceEventType.EffectRemoved));
                Assert.That(traceEvent.Decision, Is.EqualTo(GASTraceDecision.Success));
                Assert.That(traceEvent.Effect, Is.SameAs(effect));
                Assert.That(traceEvent.Target, Is.SameAs(asc));
            }
            finally
            {
                GASTrace.Enabled = false;
                GASTrace.Clear();
                GASTrace.SetCapacity(originalCapacity);
                GASTrace.Enabled = originalEnabled;
                asc.Dispose();
            }
        }

        [Test]
        public void GASTrace_RuntimeAbilityRecordRetainsDefinitionInsteadOfReleasedInstance()
        {
            int originalCapacity = GASTrace.Capacity;
            bool originalEnabled = GASTrace.Enabled;
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new DefinitionAwareAbility("TraceAbility", 7);
            GameplayAbilitySpec spec = null;
            try
            {
                GASTrace.SetCapacity(8);
                GASTrace.Enabled = true;
                spec = asc.GrantAbility(definition);
                GameplayAbility runtimeInstance = spec.GetPrimaryInstance();

                GASTrace.Record(
                    GASTraceEventType.AbilityActivated,
                    asc,
                    runtimeInstance,
                    decision: GASTraceDecision.Success,
                    abilitySpecHandle: spec.Handle,
                    level: spec.Level);
                asc.ClearAbility(spec);
                spec = null;

                Assert.That(GASTrace.TryGetRecent(0, out GASTraceEvent traceEvent), Is.True);
                Assert.That(traceEvent.AbilityDefinition, Is.SameAs(definition));
                Assert.That(traceEvent.AbilityDefinition, Is.Not.SameAs(runtimeInstance));
            }
            finally
            {
                if (spec != null)
                {
                    asc.ClearAbility(spec);
                }
                GASTrace.Enabled = false;
                GASTrace.Clear();
                GASTrace.SetCapacity(originalCapacity);
                GASTrace.Enabled = originalEnabled;
                asc.Dispose();
            }
        }

        [Test]
        public void RuntimeContext_RejectsCrossContextEffectApplication()
        {
            var source = new AbilitySystemComponent();
            var target = new AbilitySystemComponent();
            GameplayEffectSpec spec = GameplayEffectSpec.Create(CreateDurationEffect("CrossContext"), source);

            GameplayEffectApplicationResult result = target.ApplyGameplayEffectSpecToSelf(spec);

            Assert.That(result.Code, Is.EqualTo(GameplayEffectApplicationResultCode.RuntimeContextMismatch));
            Assert.That(target.ActiveEffects, Is.Empty);
            Assert.That(source.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            source.Dispose();
            target.Dispose();
        }

        [Test]
        public void RuntimeContext_RejectsCrossContextPooledEffectContextWithoutSource()
        {
            var firstContext = new GASRuntimeContext();
            var secondContext = new GASRuntimeContext();
            var first = new AbilitySystemComponent(firstContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var second = new AbilitySystemComponent(secondContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectContext effectContext = first.MakeEffectContext();
            GameplayEffectSpec spec = GameplayEffectSpec.Create(
                CreateDurationEffect("CrossContextContext"),
                null,
                effectContext);

            GameplayEffectApplicationResult result = second.ApplyGameplayEffectSpecToSelf(spec);

            Assert.That(result.Code, Is.EqualTo(GameplayEffectApplicationResultCode.RuntimeContextMismatch));
            Assert.That(second.ActiveEffects, Is.Empty);
            Assert.That(firstContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            first.Dispose();
            second.Dispose();
            firstContext.Dispose();
            secondContext.Dispose();
        }

        [Test]
        public void RuntimeContext_CannotDisposeWhileAbilitySystemsAreRegistered()
        {
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            Assert.Throws<InvalidOperationException>(() => context.Dispose());
            Assert.That(context.IsDisposed, Is.False);

            asc.Dispose();
            context.Dispose();

            Assert.That(context.IsDisposed, Is.True);
        }

        [Test]
        public void RuntimeAbilityInstances_AreIsolatedByDefinitionAndNeverReuseReleasedIdentity()
        {
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var firstDefinition = new DefinitionAwareAbility("FirstDefinition", 11);
            var secondDefinition = new DefinitionAwareAbility("SecondDefinition", 22);

            GameplayAbilitySpec firstSpec = asc.GrantAbility(firstDefinition);
            var firstInstance = (DefinitionAwareAbility)firstSpec.GetPrimaryInstance();
            firstInstance.RuntimeMarker = 73;
            Assert.That(firstInstance.DefinitionValue, Is.EqualTo(11));

            asc.ClearAbility(firstSpec);
            Assert.That(firstInstance.RuntimeMarker, Is.Zero);
            Assert.That(firstInstance.ActorInfo.OwnerActor, Is.Null);
            Assert.That(firstInstance.ActorInfo.AvatarActor, Is.Null);

            GameplayAbilitySpec secondSpec = asc.GrantAbility(secondDefinition);
            var secondInstance = (DefinitionAwareAbility)secondSpec.GetPrimaryInstance();
            Assert.That(secondInstance.DefinitionValue, Is.EqualTo(22));
            Assert.That(secondInstance, Is.Not.SameAs(firstInstance));
            asc.ClearAbility(secondSpec);

            GameplayAbilitySpec replacementFirstSpec = asc.GrantAbility(firstDefinition);
            var replacementFirstInstance = (DefinitionAwareAbility)replacementFirstSpec.GetPrimaryInstance();
            Assert.That(replacementFirstInstance, Is.Not.SameAs(firstInstance));
            Assert.That(replacementFirstInstance.DefinitionValue, Is.EqualTo(11));
            Assert.That(replacementFirstInstance.RuntimeMarker, Is.Zero);

            Assert.That(asc.TryActivateAbility(replacementFirstSpec), Is.True);
            Assert.That(replacementFirstSpec.IsActive, Is.True);
            Assert.DoesNotThrow(firstInstance.EndAbility);
            Assert.That(replacementFirstSpec.IsActive, Is.True, "A released runtime ability reference must not end a replacement instance.");

            asc.ClearAbility(replacementFirstSpec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void AbilityDefinition_RejectsRepeatedInitialization()
        {
            var definition = new DefinitionAwareAbility("InitializedOnce", 1);

            Assert.Throws<InvalidOperationException>(() => definition.InitializeAgain());
        }

        [Test]
        public void AbilityDefinition_SealsConfigurationTagsAndGrantRejectsUninitializedDefinition()
        {
            const string tagName = "Test.GAS.Runtime.ImmutableAbilityDefinition";
            GameplayTagManager.RegisterDynamicTag(tagName, "Immutable ability definition tag");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag tag = GameplayTagManager.RequestTag(tagName);
            var mutableInput = new GameplayTagContainer();
            mutableInput.AddTag(tag);
            ReadOnlyGameplayTagContainer readOnlyInput = mutableInput.CreateSnapshot();
            var definition = new DefinitionAwareAbility("ImmutableDefinition", 1, true, readOnlyInput);

            mutableInput.Clear();
            Assert.That(definition.AbilityTags.HasTagExact(tag), Is.True);
            Assert.That(definition.AbilityTags, Is.InstanceOf<IReadOnlyGameplayTagContainer>());
            Assert.That(definition.AbilityTags, Is.Not.InstanceOf<IGameplayTagContainer>());

            var asc = new AbilitySystemComponent();
            var uninitialized = new DefinitionAwareAbility("Uninitialized", 2, false);
            Assert.Throws<InvalidOperationException>(() => asc.GrantAbility(uninitialized));
            asc.Dispose();
        }

        [Test]
        public void RuntimeLimits_RejectGrowthWithoutPartialMutation()
        {
            var limits = new GASRuntimeLimits(
                maxAttributeSets: 1,
                maxAttributes: 1,
                maxGrantedAbilities: 1,
                maxActiveEffects: 1,
                maxPredictionWindows: 1,
                maxTargetsPerTargetData: 2,
                maxSetByCallerEntries: 2,
                maxModifiersPerEffect: 2,
                maxCoreModifiers: 4,
                maxPredictedAttributeChanges: 2);
            var options = new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits);
            var asc = new AbilitySystemComponent(null, options);
            asc.AddAttributeSet(new ResourceAttributeSet("Mana"));

            Assert.Throws<InvalidOperationException>(() => asc.AddAttributeSet(new ResourceAttributeSet("Health")));
            Assert.That(asc.AttributeSets.Count, Is.EqualTo(1));

            GameplayAbilitySpec granted = asc.GrantAbility(new CommitTestAbility(null, null));
            Assert.Throws<InvalidOperationException>(() => asc.GrantAbility(new CommitTestAbility(null, null)));
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(1));

            GameplayEffectApplicationResult first = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(CreateDurationEffect("First"), asc));
            GameplayEffectApplicationResult second = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(CreateDurationEffect("Second"), asc));

            Assert.That(first.Code, Is.EqualTo(GameplayEffectApplicationResultCode.Applied));
            Assert.That(second.Code, Is.EqualTo(GameplayEffectApplicationResultCode.ActiveEffectLimitReached));
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));

            asc.TryRemoveActiveEffect(first.ActiveEffect);
            asc.ClearAbility(granted);
            asc.Dispose();
        }

        [Test]
        public void TargetData_IsBoundedAndDetectsInvalidRelease()
        {
            var limits = new GASRuntimeLimits(maxTargetsPerTargetData: 2);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            var data = asc.RentTargetData<GameplayAbilityTargetData_MultiTarget>();
            var first = new GameObject("FirstTarget");
            var second = new GameObject("SecondTarget");
            var overflow = new GameObject("OverflowTarget");

            try
            {
                data.AddTarget(first);
                data.AddTarget(second);
                Assert.Throws<InvalidOperationException>(() => data.AddTarget(overflow));

                data.Release();
                Assert.Throws<ObjectDisposedException>(() => data.AddTarget(first));
                Assert.Throws<ObjectDisposedException>(() => _ = data.ActorCount);
                Assert.Throws<ObjectDisposedException>(() => _ = data.GetActor(0));
                Assert.Throws<ObjectDisposedException>(() => _ = data.FirstActor);
                Assert.Throws<ObjectDisposedException>(() => _ = data.PredictionKey);
                Assert.Throws<ObjectDisposedException>(() => _ = data.AbilitySpecHandle);
                Assert.Throws<ObjectDisposedException>(() => _ = data.Source);
                Assert.Throws<ObjectDisposedException>(() => _ = data.CreatedFrame);
                data.Release();

                GASRuntimeMemoryStatistics stats = asc.RuntimeContext.GetMemoryStatistics();
                Assert.That(stats.TargetData.Active, Is.EqualTo(0));
                Assert.That(stats.TargetData.InvalidReleases, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(overflow);
                asc.Dispose();
            }
        }

        [Test]
        public void SetByCallerFloatUpdate_RecalculatesFixedMagnitude()
        {
            const string tagName = "Test.GAS.Runtime.SetByCaller";
            GameplayTagManager.RegisterDynamicTag(tagName, "Runtime hardening SetByCaller tag");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag tag = GameplayTagManager.RequestTag(tagName);
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var effect = new GameplayEffect(
                "SetByCaller",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Mana", EAttributeModifierOperation.Add, new SetByCallerMagnitude(tag))
                });
            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);

            spec.SetSetByCallerMagnitude(tag, 12.5f);

            Assert.That(spec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(GASFixedValue.FromFloat(12.5f).RawValue));
            spec.Discard();
            asc.Dispose();
        }

        [Test]
        public void CommitAbility_AppliesCostAndCooldownExactlyOnce()
        {
            const string cooldownTagName = "Test.GAS.Runtime.Cooldown";
            GameplayTagManager.RegisterDynamicTag(cooldownTagName, "Runtime hardening cooldown tag");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag cooldownTag = GameplayTagManager.RequestTag(cooldownTagName);

            var cooldownTags = new GameplayTagContainer();
            cooldownTags.AddTag(cooldownTag);
            GameplayEffect cost = CreateInstantModifierEffect("ManaCost", "Mana", -25f);
            var cooldown = new GameplayEffect(
                "Cooldown",
                EDurationPolicy.HasDuration,
                duration: 5f,
                grantedTags: cooldownTags);
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ResourceAttributeSet("Mana");
            attributes.Resource.SetBaseValue(100f);
            attributes.Resource.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new CommitTestAbility(cost, cooldown));
            GameplayAbility ability = spec.GetPrimaryInstance();
            int commitEvents = 0;
            asc.OnAbilityCommitted += _ => commitEvents++;

            GameplayAbilityCommitResult first = ability.CommitAbility(default, spec);
            GameplayAbilityCommitResult second = ability.CommitAbility(default, spec);

            Assert.That(first.Code, Is.EqualTo(GameplayAbilityCommitResultCode.Committed));
            Assert.That(second.Code, Is.EqualTo(GameplayAbilityCommitResultCode.CooldownActive));
            Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(75).RawValue));
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.HasMatchingGameplayTag(cooldownTag), Is.True);
            Assert.That(commitEvents, Is.EqualTo(1));

            asc.TryRemoveActiveEffect(asc.ActiveEffects[0]);
            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void CommitAbility_InsufficientCostDoesNotApplyCooldown()
        {
            GameplayEffect cost = CreateInstantModifierEffect("ExpensiveCost", "Mana", -125f);
            var cooldown = new GameplayEffect("Cooldown", EDurationPolicy.HasDuration, duration: 5f);
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ResourceAttributeSet("Mana");
            attributes.Resource.SetBaseValue(100f);
            attributes.Resource.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new CommitTestAbility(cost, cooldown));

            GameplayAbilityCommitResult result = spec.GetPrimaryInstance().CommitAbility(default, spec);

            Assert.That(result.Code, Is.EqualTo(GameplayAbilityCommitResultCode.CostUnavailable));
            Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(asc.ActiveEffects, Is.Empty);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void AbilityTask_FailurePathsReleaseEveryLease()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new CommitTestAbility(null, null));
            GameplayAbility ability = spec.GetPrimaryInstance();

            Assert.Throws<InvalidOperationException>(() => ability.NewAbilityTask<ThrowingInitTask>());
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(0));

            ThrowingActivateTask task = ability.NewAbilityTask<ThrowingActivateTask>();
            Assert.Throws<InvalidOperationException>(() => task.Activate());
            GASRuntimeMemoryStatistics statistics = asc.RuntimeContext.GetMemoryStatistics();
            Assert.That(statistics.Tasks.Active, Is.EqualTo(0));

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void PeriodicEffect_CatchUpUsesPerTickBudgetAndPreservesBacklog()
        {
            var limits = new GASRuntimeLimits(maxPeriodicEffectExecutionsPerTick: 3);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            var attributes = new ResourceAttributeSet("Health");
            attributes.Resource.SetBaseValue(100f);
            attributes.Resource.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            var periodicEffect = new GameplayEffect(
                "BudgetedPeriodicDamage",
                EDurationPolicy.Infinite,
                period: 0.1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-1f))
                },
                executePeriodicEffectOnApplication: false);
            GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(periodicEffect, asc));

            Assert.That(application.Succeeded, Is.True);
            Assert.That(application.ActiveEffect, Is.Not.Null);
            application.ActiveEffect.Tick(1f, asc);
            Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(97).RawValue));
            Assert.That(application.ActiveEffect.PeriodTimeRemainingRaw, Is.LessThanOrEqualTo(0L));

            application.ActiveEffect.Tick(0f, asc);
            Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(94).RawValue));
            Assert.That(application.ActiveEffect.PeriodTimeRemainingRaw, Is.LessThanOrEqualTo(0L));

            asc.TryRemoveActiveEffect(application.ActiveEffect);
            asc.Dispose();
        }

        [Test]
        public void AbilityTaskRepeat_CatchUpUsesPerTickBudgetAndPreservesBacklog()
        {
            var limits = new GASRuntimeLimits(maxAbilityTaskRepeatExecutionsPerTick: 2);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            GameplayAbilitySpec spec = asc.GrantAbility(new CommitTestAbility(null, null));
            GameplayAbility ability = spec.GetPrimaryInstance();
            AbilityTask_Repeat task = AbilityTask_Repeat.Repeat(ability, 0.1f, -1);
            int executionCount = 0;
            task.OnPerformAction = _ =>
            {
                executionCount++;
                return true;
            };
            task.Activate();

            task.Tick(1f);
            Assert.That(executionCount, Is.EqualTo(2));

            task.Tick(0f);
            Assert.That(executionCount, Is.EqualTo(4));

            task.CancelTask();
            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void RuntimeLimits_RejectNonPositiveCatchUpBudgets()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GASRuntimeLimits(maxPeriodicEffectExecutionsPerTick: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GASRuntimeLimits(maxAbilityTaskRepeatExecutionsPerTick: 0));
        }

        [Test]
        public void PredictionWindow_RejectsCapacityOverflowAndInvalidScopes()
        {
            var context = new GASRuntimeContext();
            var options = new GASAbilitySystemRuntimeOptions(
                GASCoreStateMode.RuntimeOnly,
                new GASRuntimeLimits(maxPredictionWindows: 1));
            var first = new AbilitySystemComponent(context, options);
            var second = new AbilitySystemComponent(context, options);
            GameplayAbilitySpec firstSpec = first.GrantAbility(new CommitTestAbility(null, null));
            GameplayAbilitySpec secondSpec = second.GrantAbility(new CommitTestAbility(null, null));

            GASPredictionKey firstKey = first.OpenPredictionWindow(firstSpec);
            GASPredictionKey foreignKey = second.OpenPredictionWindow(secondSpec);

            Assert.That(first.OpenPredictionWindowCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => first.OpenPredictionWindow(firstSpec));
            Assert.Throws<InvalidOperationException>(() => first.BeginPredictionScope(default));
            Assert.Throws<InvalidOperationException>(() => first.BeginPredictionScope(foreignKey));

            using (first.BeginPredictionScope(firstKey))
            {
                Assert.That(first.CurrentPredictionKey, Is.EqualTo(firstKey));
            }

            Assert.That(first.CurrentPredictionKey, Is.EqualTo(default(GASPredictionKey)));
            Assert.That(first.CommitPredictionWindow(firstKey), Is.True);
            Assert.Throws<InvalidOperationException>(() => first.BeginPredictionScope(firstKey));

            second.RollbackPredictionWindow(foreignKey);
            first.ClearAbility(firstSpec);
            second.ClearAbility(secondSpec);
            first.Dispose();
            second.Dispose();
            context.Dispose();
        }

        [Test]
        public void PredictedInstantEffect_RejectsSnapshotOverflowWithoutPartialMutation()
        {
            var limits = new GASRuntimeLimits(
                maxModifiersPerEffect: 2,
                maxPredictedAttributeChanges: 1);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            var attributes = new DualResourceAttributeSet();
            SetAttributeValues(attributes.Primary, 100f);
            SetAttributeValues(attributes.Secondary, 200f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new CommitTestAbility(null, null));
            GASPredictionKey predictionKey = asc.OpenPredictionWindow(abilitySpec);
            var effect = new GameplayEffect(
                "PredictionBudget",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Primary", EAttributeModifierOperation.Add, new ScalableFloat(10f)),
                    new ModifierInfo("Secondary", EAttributeModifierOperation.Add, new ScalableFloat(20f))
                });

            GameplayEffectApplicationResult result;
            using (asc.BeginPredictionScope(predictionKey))
            {
                result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
                Assert.That(asc.CurrentPredictionKey, Is.EqualTo(predictionKey));
            }

            Assert.That(result.Code, Is.EqualTo(GameplayEffectApplicationResultCode.PredictionLimitReached));
            Assert.That(attributes.Primary.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Primary.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Secondary.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(200).RawValue));
            Assert.That(attributes.Secondary.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(200).RawValue));
            Assert.That(asc.CurrentPredictionKey, Is.EqualTo(default(GASPredictionKey)));
            GASRuntimeMemoryStatistics statistics = asc.RuntimeContext.GetMemoryStatistics();
            Assert.That(statistics.EffectSpecs.Active, Is.EqualTo(0));
            Assert.That(statistics.EffectContexts.Active, Is.EqualTo(0));
            Assert.That(statistics.ActiveEffects.Active, Is.EqualTo(0));
            Assert.That(statistics.AbilitySpecs.Active, Is.EqualTo(1));
            Assert.That(statistics.Abilities.Active, Is.EqualTo(1));

            asc.RollbackPredictionWindow(predictionKey);
            asc.ClearAbility(abilitySpec);
            asc.Dispose();
        }

        [Test]
        public void InstantEffect_HookFailureRollsBackEveryAttributeAndLease()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ThrowOnSecondaryAttributeSet();
            SetAttributeValues(attributes.Primary, 100f);
            SetAttributeValues(attributes.Secondary, 200f);
            asc.AddAttributeSet(attributes);
            var effect = new GameplayEffect(
                "TransactionalInstant",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Primary", EAttributeModifierOperation.Add, new ScalableFloat(-10f)),
                    new ModifierInfo("Secondary", EAttributeModifierOperation.Add, new ScalableFloat(-20f))
                });

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, asc));

            Assert.That(result.Code, Is.EqualTo(GameplayEffectApplicationResultCode.ExecutionFailed));
            Assert.That(attributes.Primary.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Primary.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Secondary.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(200).RawValue));
            Assert.That(attributes.Secondary.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(200).RawValue));
            Assert.That(asc.ActiveEffects, Is.Empty);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            asc.Dispose();
        }

        [Test]
        public void InstantEffect_ExecutionOutputRejectsOverflowAndReusesCleanScratch()
        {
            var limits = new GASRuntimeLimits(maxModifiersPerEffect: 1);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            try
            {
                var attributes = new ResourceAttributeSet("Health");
                SetAttributeValues(attributes.Resource, 100f);
                asc.AddAttributeSet(attributes);

                var overflowingEffect = new GameplayEffect(
                    "ExecutionOverflow",
                    EDurationPolicy.Instant,
                    execution: new FixedOutputExecution("Health", outputCount: 2));
                GameplayEffectApplicationResult failed = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(overflowingEffect, asc));

                Assert.That(failed.Code, Is.EqualTo(GameplayEffectApplicationResultCode.ExecutionFailed));
                Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
                Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
                Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

                var validEffect = new GameplayEffect(
                    "ExecutionValid",
                    EDurationPolicy.Instant,
                    execution: new FixedOutputExecution("Health", outputCount: 1));
                GameplayEffectApplicationResult succeeded = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(validEffect, asc));

                Assert.That(succeeded.Succeeded, Is.True);
                Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(99).RawValue));
                Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(99).RawValue));
                Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void AbilityActivationFailure_RestoresPredictionTagsAndPerExecutionLease()
        {
            const string tagName = "Test.GAS.Runtime.ActivationFailure";
            GameplayTag activationTag = RegisterTag(tagName);
            var activationTags = new GameplayTagContainer();
            activationTags.AddTag(activationTag);
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new ThrowingActivationAbility(activationTags));

            Assert.Throws<InvalidOperationException>(() => asc.TryActivateAbility(spec));

            Assert.That(spec.IsActive, Is.False);
            Assert.That(asc.CurrentPredictionKey, Is.EqualTo(default(GASPredictionKey)));
            Assert.That(asc.OpenPredictionWindowCount, Is.EqualTo(0));
            Assert.That(asc.HasMatchingGameplayTag(activationTag), Is.False);
            GASRuntimeMemoryStatistics statistics = asc.RuntimeContext.GetMemoryStatistics();
            Assert.That(statistics.Abilities.Active, Is.EqualTo(0));
            Assert.That(statistics.Tasks.Active, Is.EqualTo(0));
            Assert.That(statistics.AbilitySpecs.Active, Is.EqualTo(1));

            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));
            asc.Dispose();
        }

        [Test]
        public void Dispose_OnRemoveFailureStillReleasesContextAndAttributeOwnership()
        {
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ResourceAttributeSet("Mana");
            asc.AddAttributeSet(attributes);
            asc.GrantAbility(new ThrowingRemoveAbility());

            asc.Dispose();

            Assert.That(asc.IsDisposed, Is.True);
            Assert.That(context.RegisteredAbilitySystemCount, Is.EqualTo(0));
            Assert.That(attributes.OwningAbilitySystemComponent, Is.Null);
            Assert.That(context.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            context.Dispose();
        }

        [Test]
        public void Dispose_ActiveAbilityTasksUnsubscribeAndReleaseEveryRuntimeLease()
        {
            GameplayTag watchedTag = RegisterTag("Test.GAS.Runtime.DisposeTasks.Tag");
            GameplayTag eventTag = RegisterTag("Test.GAS.Runtime.DisposeTasks.Event");
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ResourceAttributeSet("Mana");
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new CommitTestAbility(null, null));
            Assert.That(asc.TryActivateAbility(spec), Is.True);
            GameplayAbility ability = spec.GetPrimaryInstance();
            int callbackCount = 0;

            AbilityTask_Repeat repeat = AbilityTask_Repeat.Repeat(ability, 1f, -1);
            repeat.OnPerformAction = _ =>
            {
                callbackCount++;
                return true;
            };
            repeat.Activate();

            AbilityTask_WaitGameplayTagAdded waitTag =
                AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(ability, watchedTag, triggerOnce: false);
            waitTag.OnTagAdded = () => callbackCount++;
            waitTag.Activate();

            AbilityTask_WaitGameplayEvent waitEvent =
                AbilityTask_WaitGameplayEvent.WaitGameplayEvent(ability, eventTag, triggerOnce: false);
            waitEvent.OnEventReceived = _ => callbackCount++;
            waitEvent.Activate();

            AbilityTask_WaitAttributeChange waitAttribute =
                AbilityTask_WaitAttributeChange.WaitAttributeChange(ability, "Mana", triggerOnce: false);
            waitAttribute.OnAttributeChanged = (_, __) => callbackCount++;
            waitAttribute.Activate();

            GASRuntimeMemoryStatistics before = context.GetMemoryStatistics();
            Assert.That(before.Abilities.Active, Is.EqualTo(1));
            Assert.That(before.Tasks.Active, Is.EqualTo(4));
            Assert.That(before.AbilitySpecs.Active, Is.EqualTo(1));

            asc.Dispose();

            GASRuntimeMemoryStatistics after = context.GetMemoryStatistics();
            Assert.That(asc.IsDisposed, Is.True);
            Assert.That(context.RegisteredAbilitySystemCount, Is.Zero);
            Assert.That(after.Abilities.Active, Is.Zero);
            Assert.That(after.Tasks.Active, Is.Zero);
            Assert.That(after.AbilitySpecs.Active, Is.Zero);
            Assert.That(after.OutstandingLeases, Is.Zero);
            Assert.That(attributes.OwningAbilitySystemComponent, Is.Null);
            Assert.That(repeat.OnPerformAction, Is.Null);
            Assert.That(waitTag.OnTagAdded, Is.Null);
            Assert.That(waitEvent.OnEventReceived, Is.Null);
            Assert.That(waitAttribute.OnAttributeChanged, Is.Null);

            attributes.Resource.SetCurrentValue(5f);
            Assert.That(callbackCount, Is.Zero);
            context.Dispose();
        }

        [Test]
        public void ClearAbility_OnRemoveFailureForceEndsAndReturnsActiveTasks()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new ThrowingRemoveAbility());
            AbilityTask task = spec.GetPrimaryInstance().NewAbilityTask<NeverEndingTask>();
            task.Activate();
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            Assert.Throws<InvalidOperationException>(() => asc.ClearAbility(spec));

            GASRuntimeMemoryStatistics statistics = asc.RuntimeContext.GetMemoryStatistics();
            Assert.That(statistics.Tasks.Active, Is.Zero);
            Assert.That(statistics.Abilities.Active, Is.Zero);
            Assert.That(statistics.AbilitySpecs.Active, Is.Zero);
            Assert.That(statistics.OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void TargetDataValidation_DefaultAndHostileArgumentsFailClosed()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new CommitTestAbility(null, null));
            Assert.That(default(TargetDataValidationResult), Is.EqualTo(TargetDataValidationResult.Invalid));
            Assert.That(
                asc.TryValidateTargetData(null, abilitySpec, default, 0f, 0, out TargetDataValidationResult result),
                Is.False);
            Assert.That(result, Is.EqualTo(TargetDataValidationResult.MissingData));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asc.TryValidateTargetData(null, abilitySpec, default, float.NaN, 0, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asc.TryValidateTargetData(null, abilitySpec, default, 0f, -1, out _));

            asc.ClearAbility(abilitySpec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));
            asc.Dispose();
        }

        [Test]
        public void StateDelta_InvalidCountAndBaselineMismatchDoNotMutateState()
        {
            GameplayTag tag = RegisterTag("Test.GAS.Runtime.Delta.Invalid");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var wrongSchema = new GASAbilitySystemStateDeltaBuffer
            {
                SchemaVersion = checked((ushort)(GASRuntimeDataContract.ReconciliationSchemaVersion + 1)),
                Sequence = 1u,
                BaseVersion = 0UL,
                CurrentVersion = 1UL,
                StateChecksum = asc.ComputeReplicatedStateChecksum(),
                ChangeMask = AbilitySystemStateChangeMask.Tags,
                AddedTags = new[] { tag },
                AddedTagCount = 1
            };

            Assert.That(asc.TryApplyStateDelta(wrongSchema, out GASStateDeltaRejectionReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));
            Assert.That(asc.HasMatchingGameplayTag(tag), Is.False);
            Assert.That(asc.StateDeltaResyncRequired, Is.False);

            var invalidCount = new GASAbilitySystemStateDeltaBuffer
            {
                Sequence = 1u,
                BaseVersion = 0UL,
                CurrentVersion = 1UL,
                StateChecksum = asc.ComputeReplicatedStateChecksum(),
                ChangeMask = AbilitySystemStateChangeMask.Tags,
                AddedTags = Array.Empty<GameplayTag>(),
                AddedTagCount = 1
            };

            Assert.That(asc.TryApplyStateDelta(invalidCount, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidCounts));
            Assert.That(asc.HasMatchingGameplayTag(tag), Is.False);
            Assert.That(asc.StateDeltaResyncRequired, Is.False);

            var wrongBaseline = new GASAbilitySystemStateDeltaBuffer
            {
                Sequence = 1u,
                BaseVersion = 99UL,
                CurrentVersion = 100UL,
                StateChecksum = asc.ComputeReplicatedStateChecksum(),
                ChangeMask = AbilitySystemStateChangeMask.Tags,
                AddedTags = new[] { tag },
                AddedTagCount = 1
            };

            Assert.That(asc.TryApplyStateDelta(wrongBaseline, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.BaselineMismatch));
            Assert.That(asc.HasMatchingGameplayTag(tag), Is.False);
            Assert.That(asc.StateDeltaResyncRequired, Is.True);

            invalidCount.AddedTags = new[] { tag };
            Assert.That(asc.TryApplyStateDelta(invalidCount, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.ResyncRequired));
            Assert.That(asc.HasMatchingGameplayTag(tag), Is.False);

            asc.Dispose();
        }

        [Test]
        public void StateDelta_ChecksumReplayAndBaselineResetContractsAreEnforced()
        {
            GameplayTag tag = RegisterTag("Test.GAS.Runtime.Delta.Sequence");
            var server = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var client = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            server.AddLooseGameplayTag(tag);
            var addDelta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(addDelta);
            addDelta.Sequence = 1u;

            Assert.That(client.TryApplyStateDelta(addDelta, out GASStateDeltaRejectionReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.HasMatchingGameplayTag(tag), Is.True);
            Assert.That(client.ComputeReplicatedStateChecksum(), Is.EqualTo(addDelta.StateChecksum));

            Assert.That(client.TryApplyStateDelta(addDelta, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.StaleOrReplayedSequence));
            Assert.That(client.HasMatchingGameplayTag(tag), Is.True);

            server.RemoveLooseGameplayTag(tag);
            var removeDelta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(removeDelta);
            removeDelta.Sequence = 2u;
            ulong authoritativeChecksum = removeDelta.StateChecksum;
            removeDelta.StateChecksum = authoritativeChecksum ^ 1UL;

            Assert.That(client.TryApplyStateDelta(removeDelta, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.ChecksumMismatch));
            Assert.That(client.HasMatchingGameplayTag(tag), Is.False);
            Assert.That(client.StateDeltaResyncRequired, Is.True);

            ulong currentChecksum = client.ComputeReplicatedStateChecksum();
            Assert.That(currentChecksum, Is.EqualTo(authoritativeChecksum));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.ResetStateDeltaBaseline(0u, removeDelta.CurrentVersion, currentChecksum));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                client.ResetStateDeltaBaseline(2u, removeDelta.CurrentVersion, 0UL));
            Assert.Throws<InvalidOperationException>(() =>
                client.ResetStateDeltaBaseline(2u, removeDelta.CurrentVersion, currentChecksum ^ 1UL));
            Assert.That(client.StateDeltaResyncRequired, Is.True);

            client.ResetStateDeltaBaseline(2u, removeDelta.CurrentVersion, currentChecksum);
            Assert.That(client.StateDeltaResyncRequired, Is.False);

            server.Dispose();
            client.Dispose();
        }

        [Test]
        public void StateDelta_AbilityGrantFailureFailsClosedAndRequestsResync()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var ability = new ThrowingGiveAbility();
            var delta = new GASAbilitySystemStateDeltaBuffer
            {
                Sequence = 1u,
                BaseVersion = 0UL,
                CurrentVersion = 1UL,
                StateChecksum = 1UL,
                ChangeMask = AbilitySystemStateChangeMask.GrantedAbilities,
                GrantedAbilities = new[]
                {
                    new GASGrantedAbilityStateData(1, ability, 1, false)
                },
                GrantedAbilityCount = 1
            };

            Assert.That(asc.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.ApplicationFailed));
            Assert.That(asc.StateDeltaResyncRequired, Is.True);
            Assert.That(asc.GetActivatableAbilities(), Is.Empty);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            asc.Dispose();
        }

        [Test]
        public void TickExceptions_EndTaskAndRemovePeriodicEffectWithoutLeases()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new CommitTestAbility(null, null));
            GameplayAbility ability = abilitySpec.GetPrimaryInstance();
            AbilityTask_Repeat task = AbilityTask_Repeat.Repeat(ability, 0.1f, -1);
            task.OnPerformAction = _ => throw new InvalidOperationException("Expected task tick failure.");
            task.Activate();

            ability.TickTasks(1f);

            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(0));

            var attributes = new ThrowOnPrimaryAttributeSet();
            SetAttributeValues(attributes.Resource, 100f);
            asc.AddAttributeSet(attributes);
            var effect = new GameplayEffect(
                "ThrowingPeriodic",
                EDurationPolicy.Infinite,
                period: 0.1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-10f))
                },
                executePeriodicEffectOnApplication: false);
            GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, asc));

            Assert.That(application.Succeeded, Is.True);
            asc.Tick(1f, true);

            Assert.That(attributes.Resource.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(asc.ActiveEffects, Is.Empty);

            asc.ClearAbility(abilitySpec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));
            asc.Dispose();
        }

        [Test]
        public void RuntimeModifiers_EvaluateChannelsInOrder()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.MirrorRuntime);
            var attributes = new ResourceAttributeSet("Health");
            asc.AddAttributeSet(attributes);
            SetAttributeValues(attributes.Resource, 10f);

            var effect = new GameplayEffect(
                "RuntimeChannels",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f), GASModifierEvaluationChannel.Channel0),
                    new ModifierInfo("Health", EAttributeModifierOperation.Multiply, new ScalableFloat(2f), GASModifierEvaluationChannel.Channel1),
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(3f), GASModifierEvaluationChannel.Channel2)
                });

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(0f, isServer: true);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(33).RawValue));
            asc.Dispose();
        }

        [Test]
        public void RuntimeStackedMultiply_ComposesOncePerStack()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.MirrorRuntime);
            var attributes = new ResourceAttributeSet("Health");
            asc.AddAttributeSet(attributes);
            SetAttributeValues(attributes.Resource, 10f);

            var effect = new GameplayEffect(
                "RuntimeStackedMultiply",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Multiply, new ScalableFloat(2f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));

            for (int i = 0; i < 3; i++)
            {
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);
            }
            asc.Tick(0f, isServer: true);

            Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(80).RawValue));
            asc.Dispose();
        }

        [Test]
        public void RuntimeOverrideWinner_RemainsStableWhenOlderEffectIsRemoved()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ResourceAttributeSet("Health");
            asc.AddAttributeSet(attributes);
            SetAttributeValues(attributes.Resource, 10f);

            ActiveGameplayEffect first = ApplyOverride(asc, "Override20", 20f);
            ApplyOverride(asc, "Override30", 30f);
            ApplyOverride(asc, "Override40", 40f);
            asc.Tick(0f, isServer: true);
            Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(40).RawValue));

            Assert.That(asc.TryRemoveActiveEffect(first), Is.True);
            asc.Tick(0f, isServer: true);
            Assert.That(attributes.Resource.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(40).RawValue));
            asc.Dispose();
        }

        private static ActiveGameplayEffect ApplyOverride(
            AbilitySystemComponent asc,
            string name,
            float value)
        {
            var effect = new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Override, new ScalableFloat(value))
                });
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            Assert.That(result.Succeeded, Is.True);
            return result.ActiveEffect;
        }

        private static GameplayEffect CreateDurationEffect(string name)
        {
            return new GameplayEffect(name, EDurationPolicy.HasDuration, duration: 5f);
        }

        private static GameplayEffect CreateInstantModifierEffect(string name, string attributeName, float magnitude)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(attributeName, EAttributeModifierOperation.Add, new ScalableFloat(magnitude))
                });
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities runtime hardening tag");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private static void SetAttributeValues(GameplayAttribute attribute, float value)
        {
            attribute.SetBaseValue(value);
            attribute.SetCurrentValue(value);
        }

        private sealed class ResourceAttributeSet : AttributeSet
        {
            public ResourceAttributeSet(string name)
            {
                Resource = new GameplayAttribute(name);
            }

            public GameplayAttribute Resource { get; }

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Resource);
            }
        }

        private class DualResourceAttributeSet : AttributeSet
        {
            public GameplayAttribute Primary { get; } = new GameplayAttribute("Primary");
            public GameplayAttribute Secondary { get; } = new GameplayAttribute("Secondary");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Primary);
                RegisterAttribute(Secondary);
            }
        }

        private sealed class ThrowOnSecondaryAttributeSet : DualResourceAttributeSet
        {
            public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
            {
                base.PostGameplayEffectExecute(data);
                if (string.Equals(data.Modifier.AttributeName, "Secondary", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected secondary attribute hook failure.");
                }
            }
        }

        private sealed class ThrowOnPrimaryAttributeSet : AttributeSet
        {
            public GameplayAttribute Resource { get; } = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Resource);
            }

            public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
            {
                base.PostGameplayEffectExecute(data);
                throw new InvalidOperationException("Expected periodic attribute hook failure.");
            }
        }

        private sealed class FixedOutputExecution : GameplayEffectExecutionCalculation
        {
            private readonly string attributeName;
            private readonly int outputCount;

            public FixedOutputExecution(string attributeName, int outputCount)
            {
                this.attributeName = attributeName;
                this.outputCount = outputCount;
            }

            public override void Execute(
                GameplayEffectSpec spec,
                GameplayEffectExecutionOutput executionOutput)
            {
                for (int i = 0; i < outputCount; i++)
                {
                    executionOutput.Add(new ModifierInfo(
                        attributeName,
                        EAttributeModifierOperation.Add,
                        new ScalableFloat(-1f)));
                }
            }
        }

        private sealed class CommitTestAbility : GameplayAbility
        {
            private readonly GameplayEffect cost;
            private readonly GameplayEffect cooldown;

            public CommitTestAbility(GameplayEffect cost, GameplayEffect cooldown)
            {
                this.cost = cost;
                this.cooldown = cooldown;
                Initialize(
                    "CommitTest",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    cost,
                    cooldown,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new CommitTestAbility(cost, cooldown);
            }
        }

        private sealed class DefinitionAwareAbility : GameplayAbility
        {
            public DefinitionAwareAbility(
                string name,
                int definitionValue,
                bool initialize = true,
                IReadOnlyGameplayTagContainer abilityTags = null)
            {
                DefinitionValue = definitionValue;
                if (initialize)
                {
                    Initialize(
                        name,
                        EGameplayAbilityInstancingPolicy.InstancedPerActor,
                        EAbilityExecutionPolicy.LocalOnly,
                        null,
                        null,
                        abilityTags,
                        null,
                        null,
                        null,
                        null);
                }
            }

            public int DefinitionValue { get; }
            public int RuntimeMarker { get; set; }

            public void InitializeAgain()
            {
                Initialize(
                    "Repeated",
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

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new DefinitionAwareAbility(Name, DefinitionValue, false);
            }

            protected override void ResetRuntimeState()
            {
                RuntimeMarker = 0;
            }
        }

        private sealed class ThrowingActivationAbility : GameplayAbility
        {
            private readonly GameplayTagContainer activationTags;

            public ThrowingActivationAbility(GameplayTagContainer activationTags)
            {
                this.activationTags = activationTags;
                Initialize(
                    "ThrowingActivation",
                    EGameplayAbilityInstancingPolicy.InstancedPerExecution,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    activationTags);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                throw new InvalidOperationException("Expected ability activation failure.");
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new ThrowingActivationAbility(activationTags);
            }
        }

        private sealed class ThrowingRemoveAbility : GameplayAbility
        {
            public ThrowingRemoveAbility()
            {
                Initialize(
                    "ThrowingRemove",
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

            public override void OnRemoveAbility()
            {
                throw new InvalidOperationException("Expected ability removal failure.");
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new ThrowingRemoveAbility();
            }
        }

        private sealed class ThrowingGiveAbility : GameplayAbility
        {
            public ThrowingGiveAbility()
            {
                Initialize(
                    "ThrowingGive",
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

            public override void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
            {
                base.OnGiveAbility(actorInfo, spec);
                throw new InvalidOperationException("Expected ability grant failure.");
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new ThrowingGiveAbility();
            }
        }

        private sealed class ThrowingInitTask : AbilityTask
        {
            public override void InitTask(GameplayAbility ability)
            {
                base.InitTask(ability);
                throw new InvalidOperationException("Expected initialization failure.");
            }

            protected override void OnActivate()
            {
            }
        }

        private sealed class NeverEndingTask : AbilityTask
        {
            protected override void OnActivate()
            {
            }
        }

        private sealed class ThrowingActivateTask : AbilityTask
        {
            protected override void OnActivate()
            {
                throw new InvalidOperationException("Expected activation failure.");
            }
        }
    }
}
