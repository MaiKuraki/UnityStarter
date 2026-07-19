using System;
using System.Collections.Generic;
using System.Threading;

using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASAbilitySystemComponentBoundaryTests
    {
        private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(5);

        [Test]
        public void PublicCollectionViews_AreStableNonMutableAndConcreteEnumerationDoesNotAllocate()
        {
            GameplayTag tag = RegisterTag("Test.GAS.ASCBoundary.ReadOnlyViews");
            var asc = new AbilitySystemComponent();
            var attributeSet = new BoundaryAttributeSet();
            asc.AddAttributeSet(attributeSet);
            asc.MarkAttributeDirty(attributeSet.Resource);
            asc.AddLooseGameplayTag(tag);
            asc.AddImmunityTag(tag);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new TickMutationAbility("ReadOnlyViewAbility", null));
            GameplayEffectApplicationResult effectResult = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect("ReadOnlyViewEffect", EDurationPolicy.HasDuration, duration: 1f),
                    asc));

            GASReadOnlyListView<AttributeSet> attributeSets = asc.AttributeSets;
            GASReadOnlyListView<ActiveGameplayEffect> activeEffects = asc.ActiveEffects;
            GASReadOnlyListView<GameplayAbilitySpec> abilities = asc.GetActivatableAbilities();
            GASReadOnlySetView<string> dirtyNames = asc.DirtyAttributeNames;
            GASReadOnlyListView<GameplayAttribute> dirtyValues = asc.DirtyAttributeValueSnapshots;
            GASReadOnlySetView<GameplayTag> addedTags = asc.PendingAddedTags;
            GASReadOnlySetView<GameplayTag> removedTags = asc.PendingRemovedTags;
            GASReadOnlyTagView combinedTags = asc.CombinedTags;
            GASReadOnlyTagView immunityTags = asc.ImmunityTags;

            Assert.That(asc.AttributeSets, Is.SameAs(attributeSets));
            Assert.That(asc.ActiveEffects, Is.SameAs(activeEffects));
            Assert.That(asc.GetActivatableAbilities(), Is.SameAs(abilities));
            Assert.That(asc.DirtyAttributeNames, Is.SameAs(dirtyNames));
            Assert.That(asc.DirtyAttributeValueSnapshots, Is.SameAs(dirtyValues));
            Assert.That(asc.PendingAddedTags, Is.SameAs(addedTags));
            Assert.That(asc.PendingRemovedTags, Is.SameAs(removedTags));
            Assert.That(asc.CombinedTags, Is.SameAs(combinedTags));
            Assert.That(asc.ImmunityTags, Is.SameAs(immunityTags));

            Assert.That((object)attributeSets, Is.Not.InstanceOf<List<AttributeSet>>());
            Assert.That((object)activeEffects, Is.Not.InstanceOf<List<ActiveGameplayEffect>>());
            Assert.That((object)abilities, Is.Not.InstanceOf<List<GameplayAbilitySpec>>());
            Assert.That((object)dirtyNames, Is.Not.InstanceOf<HashSet<string>>());
            Assert.That((object)dirtyValues, Is.Not.InstanceOf<List<GameplayAttribute>>());
            Assert.That((object)addedTags, Is.Not.InstanceOf<HashSet<GameplayTag>>());
            Assert.That((object)removedTags, Is.Not.InstanceOf<HashSet<GameplayTag>>());
            Assert.That((object)combinedTags, Is.Not.InstanceOf<GameplayTagCountContainer>());
            Assert.That((object)immunityTags, Is.Not.InstanceOf<GameplayTagContainer>());
            Assert.That(combinedTags.GetExplicitTagCount(tag), Is.EqualTo(1));
            Assert.That(immunityTags.Contains(tag), Is.True);

            int checksum = EnumerateViews(attributeSets, activeEffects, abilities, dirtyNames, dirtyValues, addedTags, removedTags, combinedTags, immunityTags, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            checksum += EnumerateViews(attributeSets, activeEffects, abilities, dirtyNames, dirtyValues, addedTags, removedTags, combinedTags, immunityTags, 1_000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);

            asc.TryRemoveActiveEffect(effectResult.ActiveEffect);
            asc.ClearAbility(abilitySpec);
            asc.Dispose();
        }

        [Test]
        public void PublicRuntimeSurfaces_RejectWorkerThreadBeforeMutation()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.Throw);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GASReadOnlyListView<AttributeSet> capturedAttributeSets = asc.AttributeSets;
            GASReadOnlyTagView capturedCombinedTags = asc.CombinedTags;
            OnTagCountChangedDelegate tagCallback = StaticTagCallback;
            GameplayEventDelegate eventCallback = StaticGameplayEventCallback;

            try
            {
                AssertWorkerRejected(() => _ = asc.AttributeSets.Count);
                AssertWorkerRejected(() => _ = capturedAttributeSets.Count);
                AssertWorkerRejected(() => _ = capturedCombinedTags.IsEmpty);
                AssertWorkerRejected(() => _ = asc.ActiveEffects.Count);
                AssertWorkerRejected(() => _ = asc.GetActivatableAbilities().Count);
                AssertWorkerRejected(() => _ = asc.DirtyAttributeNames.Count);
                AssertWorkerRejected(() => asc.RegisterTagEventCallback(default, GameplayTagEventType.AnyCountChange, tagCallback));
                AssertWorkerRejected(() => asc.RemoveTagEventCallback(default, GameplayTagEventType.AnyCountChange, tagCallback));
                AssertWorkerRejected(() => asc.RegisterGameplayEventCallback(default, eventCallback));
                AssertWorkerRejected(() => asc.RemoveGameplayEventCallback(default, eventCallback));
                AssertWorkerRejected(() => asc.HandleGameplayEvent(default));
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void CapturedViewsAndRuntimeControls_RejectAccessAfterDispose()
        {
            var asc = new AbilitySystemComponent();
            GASReadOnlyListView<AttributeSet> attributeSets = asc.AttributeSets;
            GASReadOnlySetView<string> dirtyNames = asc.DirtyAttributeNames;
            GASReadOnlyTagView combinedTags = asc.CombinedTags;
            asc.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = attributeSets.Count);
            Assert.Throws<ObjectDisposedException>(() => _ = dirtyNames.Count);
            Assert.Throws<ObjectDisposedException>(() => _ = combinedTags.IsEmpty);
            Assert.Throws<ObjectDisposedException>(() => asc.RegisterGameplayEventCallback(default, StaticGameplayEventCallback));
        }

        [Test]
        public void Tick_StructuralMutationTicksInitialLiveSpecsAtMostOnceAndDefersNewSpecToNextFrame()
        {
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec victimSpec = asc.GrantAbility(new TickMutationAbility("Victim", null));
            GameplayAbilitySpec secondSpec = asc.GrantAbility(new TickMutationAbility("Second", null));
            bool mutated = false;
            bool replacementActivated = false;
            bool staleVictimActivationAccepted = false;
            GameplayAbilitySpec replacementSpec = null;
            TickMutationAbility replacement = null;
            GameplayAbilitySpec mutatorSpec = asc.GrantAbility(new TickMutationAbility("Mutator", owner =>
            {
                if (mutated)
                {
                    return;
                }

                mutated = true;
                GameplayAbilitySpec returnedLease = victimSpec;
                owner.ClearAbility(returnedLease);
                replacementSpec = owner.GrantAbility(new TickMutationAbility("Replacement", null));
                staleVictimActivationAccepted = owner.TryActivateAbility(returnedLease);
                replacement = (TickMutationAbility)replacementSpec.GetPrimaryInstance();
                replacementActivated = owner.TryActivateAbility(replacementSpec);
            }));

            TickMutationAbility victim = (TickMutationAbility)victimSpec.GetPrimaryInstance();
            TickMutationAbility second = (TickMutationAbility)secondSpec.GetPrimaryInstance();
            TickMutationAbility mutator = (TickMutationAbility)mutatorSpec.GetPrimaryInstance();

            // Put the mutator before the victim in the tick list so the victim is cleared before its
            // original snapshot slot is reached and a replacement is granted in the same frame.
            Assert.That(asc.TryActivateAbility(mutatorSpec), Is.True, "Mutator activation failed.");
            Assert.That(asc.TryActivateAbility(secondSpec), Is.True, "Second activation failed.");
            Assert.That(asc.TryActivateAbility(victimSpec), Is.True, "Victim activation failed.");

            asc.Tick(0f, isServer: true);

            Assert.That(replacementSpec, Is.Not.SameAs(victimSpec), "A cleared public spec must never become a later grant's identity.");
            Assert.That(staleVictimActivationAccepted, Is.False, "A cleared spec reference must not activate its replacement.");
            Assert.That(victim.TickCount, Is.Zero, "Returning the victim ability must reset its runtime state.");
            Assert.That(second.TickCount, Is.EqualTo(1));
            Assert.That(mutator.TickCount, Is.EqualTo(1));
            Assert.That(replacement.TickCount, Is.Zero, "A replacement spec must not inherit the victim's stale snapshot slot.");
            Assert.That(replacementActivated, Is.True, "The replacement spec must be accepted during the snapshot tick.");
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True, "Indexes diverged after the structural mutation tick.");

            asc.Tick(0f, isServer: true);

            Assert.That(second.TickCount, Is.EqualTo(2));
            Assert.That(mutator.TickCount, Is.EqualTo(2));
            Assert.That(replacement.TickCount, Is.EqualTo(1));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True, "Indexes diverged after ticking the deferred activation.");

            asc.ClearAbility(secondSpec);
            asc.ClearAbility(mutatorSpec);
            asc.ClearAbility(replacementSpec);
            asc.Dispose();
        }

        [Test]
        public void Tick_PreallocatedAbilitySnapshotDoesNotAllocatePerFrame()
        {
            var asc = new AbilitySystemComponent();
            asc.ReserveRuntimeCapacity(abilityCapacity: 4, tickingAbilityCapacity: 4);
            GameplayAbilitySpec spec = asc.GrantAbility(new TickMutationAbility("AllocationGuard", null));
            Assert.That(asc.TryActivateAbility(spec), Is.True);
            asc.Tick(0f, isServer: true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                asc.Tick(0f, isServer: true);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void AbilityActivatedObserver_RejectsDisposeAndCurrentSpecRemovalWithoutCorruptingActivation()
        {
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec spec = asc.GrantAbility(new TickMutationAbility("ObserverReentrancy", null));
            Exception disposeFailure = null;
            Exception clearFailure = null;
            int laterObserverCount = 0;

            asc.OnAbilityActivated += _ =>
            {
                try { asc.Dispose(); }
                catch (Exception exception) { disposeFailure = exception; }

                try { asc.ClearAbility(spec); }
                catch (Exception exception) { clearFailure = exception; }
            };
            asc.OnAbilityActivated += _ => laterObserverCount++;

            Assert.That(asc.TryActivateAbility(spec), Is.True);

            Assert.That(disposeFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(clearFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(laterObserverCount, Is.EqualTo(1));
            Assert.That(asc.IsDisposed, Is.False);
            Assert.That(spec.IsActive, Is.True);
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(1));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void ActivateAbilityOverride_CannotDisposeOrClearCurrentSpecUntilActivationReturns()
        {
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec spec = asc.GrantAbility(new SelfClearingAbility());
            var ability = (SelfClearingAbility)spec.GetPrimaryInstance();

            Assert.That(asc.TryActivateAbility(spec), Is.True);

            Assert.That(ability.ClearFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(ability.DisposeFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(asc.IsDisposed, Is.False);
            Assert.That(spec.IsActive, Is.True);
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(1));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void AbilityEndedObserver_CannotClearEndingSpecAndLaterGrantUsesDistinctIdentity()
        {
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec endingSpec = asc.GrantAbility(new EndReentrancyAbility("EndingLease"));
            Assert.That(asc.TryActivateAbility(endingSpec), Is.True);
            GameplayAbility endingAbility = endingSpec.AbilityInstance;
            Exception clearFailure = null;
            GameplayAbilitySpec callbackReplacement = null;
            int laterObserverCount = 0;

            asc.OnAbilityEndedEvent += _ =>
            {
                try
                {
                    asc.ClearAbility(endingSpec);
                    callbackReplacement = asc.GrantAbility(new EndReentrancyAbility("CallbackReplacement"));
                    asc.TryActivateAbility(callbackReplacement);
                }
                catch (Exception exception)
                {
                    clearFailure = exception;
                }
            };
            asc.OnAbilityEndedEvent += _ => laterObserverCount++;

            Assert.DoesNotThrow(endingAbility.EndAbility);

            Assert.That(clearFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(callbackReplacement, Is.Null);
            Assert.That(laterObserverCount, Is.EqualTo(1));
            Assert.That(endingSpec.IsActive, Is.False);
            Assert.That(endingSpec.AbilityInstance, Is.Null, "Per-execution instance cleanup must complete exactly once after observer delivery.");
            Assert.That(asc.GetActivatableAbilities().Count, Is.EqualTo(1));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);
            GASRuntimeMemoryStatistics afterEnd = context.GetMemoryStatistics();
            Assert.That(afterEnd.Abilities.Active, Is.Zero);
            Assert.That(afterEnd.AbilitySpecs.Active, Is.EqualTo(1));

            asc.ClearAbility(endingSpec);
            Assert.That(context.GetMemoryStatistics().OutstandingLeases, Is.Zero);

            GameplayAbilitySpec replacementSpec = asc.GrantAbility(new EndReentrancyAbility("PostEndReplacement"));
            Assert.That(replacementSpec, Is.Not.SameAs(endingSpec), "A cleared public spec must not be reused for a later grant.");
            Assert.That(asc.TryActivateAbility(endingSpec), Is.False, "A stale spec reference must not activate the replacement grant.");
            Assert.That(replacementSpec.IsActive, Is.False);
            Assert.That(asc.TryActivateAbility(replacementSpec), Is.True);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);
            asc.ClearAbility(replacementSpec);
            Assert.That(context.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void TagEventRegistration_RejectsUnknownEventType()
        {
            GameplayTag tag = RegisterTag("Test.GAS.ASCBoundary.UnknownTagEventType");
            var asc = new AbilitySystemComponent();
            OnTagCountChangedDelegate callback = StaticTagCallback;
            var unknownEventType = (GameplayTagEventType)int.MaxValue;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asc.RegisterTagEventCallback(tag, unknownEventType, callback));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                asc.RemoveTagEventCallback(tag, unknownEventType, callback));

            asc.Dispose();
        }

        [Test]
        public void GameplayEventObservers_IsolateFailuresRejectDisposeAndStillRunAuthorityTrigger()
        {
            GameplayTag eventTag = RegisterTag("Test.GAS.ASCBoundary.GameplayEvent");
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec triggeredSpec = asc.GrantAbility(
                new TriggeredAbility("GameplayEventTrigger", eventTag, EAbilityTriggerSource.GameplayEvent));
            var triggeredAbility = (TriggeredAbility)triggeredSpec.GetPrimaryInstance();
            Exception disposeFailure = null;
            int laterObserverCount = 0;

            asc.RegisterGameplayEventCallback(eventTag, _ =>
            {
                try { asc.Dispose(); }
                catch (Exception exception) { disposeFailure = exception; }
                throw new InvalidOperationException("Expected gameplay-event observer failure.");
            });
            asc.RegisterGameplayEventCallback(eventTag, _ => laterObserverCount++);

            Assert.DoesNotThrow(() => asc.HandleGameplayEvent(new GameplayEventData
            {
                EventTag = eventTag,
                Instigator = asc,
                Target = asc
            }));

            Assert.That(disposeFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(laterObserverCount, Is.EqualTo(1));
            Assert.That(triggeredAbility.ActivationCount, Is.EqualTo(1));
            Assert.That(triggeredSpec.IsActive, Is.True);
            Assert.That(asc.IsDisposed, Is.False);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(triggeredSpec);
            asc.Dispose();
        }

        [Test]
        public void TagObservers_IsolateFailuresAndPreserveCommittedTriggerState()
        {
            GameplayTag triggerTag = RegisterTag("Test.GAS.ASCBoundary.TagTrigger");
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec triggeredSpec = asc.GrantAbility(
                new TriggeredAbility("OwnedTagTrigger", triggerTag, EAbilityTriggerSource.OwnedTagAdded));
            var triggeredAbility = (TriggeredAbility)triggeredSpec.GetPrimaryInstance();
            int laterObserverCount = 0;
            int observedCount = -1;

            asc.RegisterTagEventCallback(triggerTag, GameplayTagEventType.NewOrRemoved, (_, __) =>
                throw new InvalidOperationException("Expected tag observer failure."));
            asc.RegisterTagEventCallback(triggerTag, GameplayTagEventType.NewOrRemoved, (_, count) =>
            {
                laterObserverCount++;
                observedCount = count;
            });

            Assert.DoesNotThrow(() => asc.AddLooseGameplayTag(triggerTag));

            Assert.That(laterObserverCount, Is.EqualTo(1));
            Assert.That(observedCount, Is.EqualTo(1));
            Assert.That(asc.CombinedTags.GetExplicitTagCount(triggerTag), Is.EqualTo(1));
            Assert.That(triggeredAbility.ActivationCount, Is.EqualTo(1));
            Assert.That(triggeredSpec.IsActive, Is.True);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(triggeredSpec);
            Assert.DoesNotThrow(() => asc.RemoveLooseGameplayTag(triggerTag));
            asc.Dispose();
        }

        [Test]
        public void AttributeObservers_IsolateFailuresRejectDisposeAndDispatchWithoutSteadyStateAllocation()
        {
            var asc = new AbilitySystemComponent();
            var attributes = new BoundaryAttributeSet();
            asc.AddAttributeSet(attributes);
            Exception disposeFailure = null;
            int laterObserverCount = 0;
            float observedOldValue = float.NaN;
            float observedNewValue = float.NaN;
            Action<float, float> rejectingObserver = (_, __) =>
            {
                try { asc.Dispose(); }
                catch (Exception exception) { disposeFailure = exception; }
                throw new InvalidOperationException("Expected attribute observer failure.");
            };
            Action<float, float> laterObserver = (oldValue, newValue) =>
            {
                laterObserverCount++;
                observedOldValue = oldValue;
                observedNewValue = newValue;
            };
            attributes.Resource.OnCurrentValueChanged += rejectingObserver;
            attributes.Resource.OnCurrentValueChanged += laterObserver;

            Assert.DoesNotThrow(() => attributes.Resource.SetCurrentValue(42f));

            Assert.That(disposeFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(laterObserverCount, Is.EqualTo(1));
            Assert.That(observedOldValue, Is.EqualTo(0f));
            Assert.That(observedNewValue, Is.EqualTo(42f));
            Assert.That(attributes.Resource.CurrentValue, Is.EqualTo(42f));
            Assert.That(asc.IsDisposed, Is.False);

            attributes.Resource.OnCurrentValueChanged -= rejectingObserver;
            attributes.Resource.SetCurrentValueRaw(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                attributes.Resource.SetCurrentValueRaw((i & 1) == 0 ? 2 : 1);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            attributes.Resource.OnCurrentValueChanged -= laterObserver;
            asc.Dispose();
        }

        [Test]
        public void EffectRemovalObserverFailure_CannotLeaveDefinitionOrDynamicGrantedTags()
        {
            GameplayTag definitionTag = RegisterTag("Test.GAS.ASCBoundary.EffectDefinitionTag");
            GameplayTag dynamicTag = RegisterTag("Test.GAS.ASCBoundary.EffectDynamicTag");
            var grantedTags = new GameplayTagContainer();
            grantedTags.AddTag(definitionTag);
            var effect = new GameplayEffect(
                "ObserverSafeRemoval",
                EDurationPolicy.HasDuration,
                duration: 10f,
                grantedTags: grantedTags);
            var asc = new AbilitySystemComponent();
            int appliedObserverCount = 0;
            int removedObserverCount = 0;
            int dynamicRemovalCount = 0;

            asc.OnGameplayEffectAppliedToSelf += _ =>
                throw new InvalidOperationException("Expected effect-applied observer failure.");
            asc.OnGameplayEffectAppliedToSelf += _ => appliedObserverCount++;
            asc.OnGameplayEffectRemovedFromSelf += _ =>
                throw new InvalidOperationException("Expected effect-removed observer failure.");
            asc.OnGameplayEffectRemovedFromSelf += _ => removedObserverCount++;
            asc.RegisterTagEventCallback(definitionTag, GameplayTagEventType.NewOrRemoved, (_, count) =>
            {
                if (count == 0)
                {
                    throw new InvalidOperationException("Expected definition-tag removal observer failure.");
                }
            });
            asc.RegisterTagEventCallback(dynamicTag, GameplayTagEventType.NewOrRemoved, (_, count) =>
            {
                if (count == 0)
                {
                    dynamicRemovalCount++;
                }
            });

            GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);
            spec.DynamicGrantedTags.AddTag(dynamicTag);
            GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(spec);

            Assert.That(application.Succeeded, Is.True);
            Assert.That(appliedObserverCount, Is.EqualTo(1));
            Assert.That(asc.CombinedTags.GetExplicitTagCount(definitionTag), Is.EqualTo(1));
            Assert.That(asc.CombinedTags.GetExplicitTagCount(dynamicTag), Is.EqualTo(1));
            Assert.DoesNotThrow(() => Assert.That(asc.TryRemoveActiveEffect(application.ActiveEffect), Is.True));

            Assert.That(removedObserverCount, Is.EqualTo(1));
            Assert.That(dynamicRemovalCount, Is.EqualTo(1));
            Assert.That(asc.CombinedTags.GetExplicitTagCount(definitionTag), Is.Zero);
            Assert.That(asc.CombinedTags.GetExplicitTagCount(dynamicTag), Is.Zero);
            Assert.That(asc.ActiveEffects.Count, Is.Zero);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);
            asc.Dispose();
        }

        [Test]
        public void CallbackListMutation_RemovalTombstonesCurrentDispatchAndAdditionStartsNextDispatch()
        {
            var asc = new AbilitySystemComponent();
            GameplayAbilitySpec firstSpec = asc.GrantAbility(new TickMutationAbility("DispatchMutationFirst", null));
            GameplayAbilitySpec secondSpec = asc.GrantAbility(new TickMutationAbility("DispatchMutationSecond", null));
            int mutatingCount = 0;
            int removedCount = 0;
            int addedCount = 0;
            Action<GameplayAbility> mutating = null;
            Action<GameplayAbility> removed = _ => removedCount++;
            Action<GameplayAbility> added = _ => addedCount++;
            mutating = _ =>
            {
                mutatingCount++;
                asc.OnAbilityActivated -= mutating;
                asc.OnAbilityActivated -= removed;
                asc.OnAbilityActivated += added;
            };
            asc.OnAbilityActivated += mutating;
            asc.OnAbilityActivated += removed;

            Assert.That(asc.TryActivateAbility(firstSpec), Is.True);
            Assert.That(mutatingCount, Is.EqualTo(1));
            Assert.That(removedCount, Is.Zero);
            Assert.That(addedCount, Is.Zero);

            Assert.That(asc.TryActivateAbility(secondSpec), Is.True);
            Assert.That(mutatingCount, Is.EqualTo(1));
            Assert.That(removedCount, Is.Zero);
            Assert.That(addedCount, Is.EqualTo(1));
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(firstSpec);
            asc.ClearAbility(secondSpec);
            asc.Dispose();
        }

        private static int EnumerateViews(
            GASReadOnlyListView<AttributeSet> attributeSets,
            GASReadOnlyListView<ActiveGameplayEffect> activeEffects,
            GASReadOnlyListView<GameplayAbilitySpec> abilities,
            GASReadOnlySetView<string> dirtyNames,
            GASReadOnlyListView<GameplayAttribute> dirtyValues,
            GASReadOnlySetView<GameplayTag> addedTags,
            GASReadOnlySetView<GameplayTag> removedTags,
            GASReadOnlyTagView combinedTags,
            GASReadOnlyTagView immunityTags,
            int iterations)
        {
            int checksum = 0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                foreach (AttributeSet value in attributeSets) checksum += value != null ? 1 : 0;
                foreach (ActiveGameplayEffect value in activeEffects) checksum += value != null ? 1 : 0;
                foreach (GameplayAbilitySpec value in abilities) checksum += value != null ? 1 : 0;
                foreach (string value in dirtyNames) checksum += value != null ? 1 : 0;
                foreach (GameplayAttribute value in dirtyValues) checksum += value != null ? 1 : 0;
                foreach (GameplayTag value in addedTags) checksum += value.IsNone ? 0 : 1;
                foreach (GameplayTag value in removedTags) checksum += value.IsNone ? 0 : 1;
                foreach (GameplayTag value in combinedTags) checksum += value.IsNone ? 0 : 1;
                foreach (GameplayTag value in immunityTags) checksum += value.IsNone ? 0 : 1;
            }

            return checksum;
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "ASC boundary test tag");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private static void AssertWorkerRejected(Action action)
        {
            Exception exception = RunOnWorkerThread(action);
            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
        }

        private static Exception RunOnWorkerThread(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            if (!thread.Join(WorkerTimeout))
            {
                throw new TimeoutException("The worker thread did not finish within the test timeout.");
            }

            return exception;
        }

        private static void StaticTagCallback(GameplayTag tag, int count)
        {
        }

        private static void StaticGameplayEventCallback(GameplayEventData data)
        {
        }

        private sealed class BoundaryAttributeSet : AttributeSet
        {
            public GameplayAttribute Resource { get; } = new GameplayAttribute("BoundaryResource");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Resource);
            }
        }

        private sealed class TickMutationAbility : GameplayAbility
        {
            private readonly Action<AbilitySystemComponent> onTick;

            public TickMutationAbility(string name, Action<AbilitySystemComponent> onTick)
            {
                this.onTick = onTick;
                Initialize(
                    name,
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

            public int TickCount { get; private set; }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                CallbackTickTask task = NewAbilityTask<CallbackTickTask>();
                task.Configure(() =>
                {
                    TickCount++;
                    onTick?.Invoke(AbilitySystemComponent);
                });
                task.Activate();
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new TickMutationAbility(Name, onTick);
            }

            protected override void ResetRuntimeState()
            {
                TickCount = 0;
            }
        }

        private sealed class TriggeredAbility : GameplayAbility
        {
            private readonly GameplayTag triggerTag;
            private readonly EAbilityTriggerSource triggerSource;

            public TriggeredAbility(string name, GameplayTag triggerTag, EAbilityTriggerSource triggerSource)
            {
                this.triggerTag = triggerTag;
                this.triggerSource = triggerSource;
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    abilityTriggers: new List<AbilityTriggerData>
                    {
                        new AbilityTriggerData(triggerTag, triggerSource)
                    });
            }

            public int ActivationCount { get; private set; }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                ActivationCount++;
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new TriggeredAbility(Name, triggerTag, triggerSource);
            }

            protected override void ResetRuntimeState()
            {
                ActivationCount = 0;
            }
        }

        private sealed class SelfClearingAbility : GameplayAbility
        {
            public SelfClearingAbility()
            {
                Initialize(
                    "SelfClearingAbility",
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

            public Exception ClearFailure { get; private set; }
            public Exception DisposeFailure { get; private set; }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                try
                {
                    AbilitySystemComponent.Dispose();
                }
                catch (Exception exception)
                {
                    DisposeFailure = exception;
                }

                try
                {
                    AbilitySystemComponent.ClearAbility(spec);
                }
                catch (Exception exception)
                {
                    ClearFailure = exception;
                }
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new SelfClearingAbility();
            }

            protected override void ResetRuntimeState()
            {
                ClearFailure = null;
                DisposeFailure = null;
            }
        }

        private sealed class EndReentrancyAbility : GameplayAbility
        {
            public EndReentrancyAbility(string name)
            {
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerExecution,
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
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new EndReentrancyAbility(Name);
            }
        }

        private sealed class CallbackTickTask : AbilityTask, IAbilityTaskTick
        {
            private Action callback;

            public void Configure(Action callback)
            {
                this.callback = callback;
            }

            public void Tick(float deltaTime)
            {
                callback?.Invoke();
            }

            protected override void OnActivate()
            {
            }

            protected override void OnDestroy()
            {
                callback = null;
                base.OnDestroy();
            }
        }
    }
}
