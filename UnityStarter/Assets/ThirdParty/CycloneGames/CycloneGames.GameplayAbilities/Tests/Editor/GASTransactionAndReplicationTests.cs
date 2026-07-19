using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASTransactionAndReplicationTests
    {
        [Test]
        public void PredictionScope_CopiesAreIdempotentAndOutOfOrderDisposeFailsFast()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TestAbility());
            GASPredictionKey firstKey = asc.OpenPredictionWindow(spec);
            GASPredictionKey secondKey = asc.OpenPredictionWindow(spec, firstKey);

            var firstScope = asc.BeginPredictionScope(firstKey);
            var firstScopeCopy = firstScope;
            var secondScope = asc.BeginPredictionScope(secondKey);
            var secondScopeCopy = secondScope;

            Assert.Throws<InvalidOperationException>(() => firstScope.Dispose());
            Assert.That(asc.CurrentPredictionKey, Is.EqualTo(secondKey));

            secondScope.Dispose();
            secondScopeCopy.Dispose();
            Assert.That(asc.CurrentPredictionKey, Is.EqualTo(firstKey));

            firstScope.Dispose();
            firstScopeCopy.Dispose();
            Assert.That(asc.CurrentPredictionKey, Is.EqualTo(default(GASPredictionKey)));

            asc.RollbackPredictionWindow(firstKey);
            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void PublicPredictionClosure_CompletesAttributeTransactions()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new TestAbility());
            GameplayEffect instant = CreateInstantAdd("PredictedInstant", 10f);

            GASPredictionKey committedKey = asc.OpenPredictionWindow(spec);
            using (asc.BeginPredictionScope(committedKey))
            {
                Assert.That(
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc)).Code,
                    Is.EqualTo(GameplayEffectApplicationResultCode.Executed));
            }
            Assert.That(asc.CommitPredictionWindow(committedKey), Is.True);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            GASPredictionKey rolledBackKey = asc.OpenPredictionWindow(spec);
            using (asc.BeginPredictionScope(rolledBackKey))
            {
                Assert.That(
                    asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc)).Code,
                    Is.EqualTo(GameplayEffectApplicationResultCode.Executed));
            }
            Assert.That(asc.RollbackPredictionWindow(rolledBackKey), Is.True);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void Prediction_RejectsStackingAndOverlappingAttributeWindowsBeforeMutation()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new TestAbility());
            var stacking = new GameplayEffect(
                "PredictedStack",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
            GameplayEffectApplicationResult initial = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(stacking, asc));
            Assert.That(initial.Succeeded, Is.True);

            GASPredictionKey stackingKey = asc.OpenPredictionWindow(spec);
            GameplayEffectApplicationResult stackingResult;
            using (asc.BeginPredictionScope(stackingKey))
            {
                stackingResult = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(stacking, asc));
            }
            Assert.That(stackingResult.Code, Is.EqualTo(GameplayEffectApplicationResultCode.PredictionUnsupported));
            Assert.That(initial.ActiveEffect.StackCount, Is.EqualTo(1));
            asc.RollbackPredictionWindow(stackingKey);

            GameplayEffect instant = CreateInstantAdd("OverlappingPrediction", 10f);
            GASPredictionKey firstKey = asc.OpenPredictionWindow(spec);
            using (asc.BeginPredictionScope(firstKey))
            {
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc)).Succeeded, Is.True);
            }

            GASPredictionKey secondKey = asc.OpenPredictionWindow(spec);
            GameplayEffectApplicationResult overlapResult;
            using (asc.BeginPredictionScope(secondKey))
            {
                overlapResult = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc));
            }
            Assert.That(overlapResult.Code, Is.EqualTo(GameplayEffectApplicationResultCode.PredictionUnsupported));
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            asc.RollbackPredictionWindow(secondKey);
            asc.RollbackPredictionWindow(firstKey);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void RuntimeContext_AuthorityModeIsExplicitAndInvalidModeFailsClosed()
        {
            var authority = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replica = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);

            Assert.That(authority.AuthorityMode, Is.EqualTo(GASRuntimeAuthorityMode.Authority));
            Assert.That(authority.HasAuthority, Is.True);
            Assert.That(replica.AuthorityMode, Is.EqualTo(GASRuntimeAuthorityMode.Replica));
            Assert.That(replica.HasAuthority, Is.False);
            Assert.That(default(GASAuthorityActivationResult).Activated, Is.False);
            Assert.That(default(GASAuthorityActivationResult).Status, Is.EqualTo(GASAuthorityActivationStatus.Invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GASRuntimeContext(GASRuntimeAuthorityMode.Invalid));

            authority.Dispose();
            replica.Dispose();
        }

        [Test]
        public void Replica_RejectsAuthorityOnlyBeforeMutation()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec authoritySpec = asc.GrantAbility(new CountingAuthorityAbility());
            var authorityAbility = (CountingAuthorityAbility)authoritySpec.GetPrimaryInstance();
            ulong versionBeforeAttempts = asc.StateVersion;

            Assert.That(asc.TryActivateAbility(authoritySpec), Is.False);
            GASAuthorityActivationResult directResult = asc.TryExecuteAuthorityAbility(authoritySpec);

            Assert.That(directResult.Status, Is.EqualTo(GASAuthorityActivationStatus.RuntimeUnavailable));
            Assert.That(directResult.AuthoritativeStateVersion, Is.EqualTo(versionBeforeAttempts));
            Assert.That(asc.StateVersion, Is.EqualTo(versionBeforeAttempts));
            Assert.That(asc.OpenPredictionWindowCount, Is.EqualTo(0));
            Assert.That(authoritySpec.IsActive, Is.False);
            Assert.That(authorityAbility.ActivationCount, Is.EqualTo(0));

            asc.ClearAbility(authoritySpec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void AuthorityExecution_ReturnsTerminalStatusesAndAuthoritativeVersion()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec wrongPolicy = asc.GrantAbility(new TestAbility("WrongPolicy"));
            GameplayAbilitySpec rejected = asc.GrantAbility(new CountingAuthorityAbility(canActivate: false));
            GameplayAbilitySpec activated = asc.GrantAbility(new CountingAuthorityAbility());
            GameplayAbilitySpec stale = asc.GrantAbility(new CountingAuthorityAbility());
            asc.ClearAbility(stale);

            GASAuthorityActivationResult missingResult = asc.TryExecuteAuthorityAbility(stale);
            GASAuthorityActivationResult wrongPolicyResult = asc.TryExecuteAuthorityAbility(wrongPolicy);
            GASAuthorityActivationResult rejectedResult = asc.TryExecuteAuthorityAbility(rejected);
            GASAuthorityActivationResult activatedResult = asc.TryExecuteAuthorityAbility(activated);
            GASAuthorityActivationResult alreadyActiveResult = asc.TryExecuteAuthorityAbility(activated);

            Assert.That(missingResult.Status, Is.EqualTo(GASAuthorityActivationStatus.MissingOrStaleGrant));
            Assert.That(wrongPolicyResult.Status, Is.EqualTo(GASAuthorityActivationStatus.WrongExecutionPolicy));
            Assert.That(rejectedResult.Status, Is.EqualTo(GASAuthorityActivationStatus.AbilityRejected));
            Assert.That(activatedResult.Status, Is.EqualTo(GASAuthorityActivationStatus.Activated));
            Assert.That(activatedResult.Activated, Is.True);
            Assert.That(activatedResult.AuthoritativeStateVersion, Is.EqualTo(asc.StateVersion));
            Assert.That(alreadyActiveResult.Status, Is.EqualTo(GASAuthorityActivationStatus.AbilityRejected));
            Assert.That(((CountingAuthorityAbility)activated.GetPrimaryInstance()).ActivationCount, Is.EqualTo(1));
            Assert.That(((CountingAuthorityAbility)rejected.GetPrimaryInstance()).ActivationCount, Is.EqualTo(0));

            activated.GetPrimaryInstance().EndAbility();
            asc.ClearAbility(activated);
            asc.ClearAbility(rejected);
            asc.ClearAbility(wrongPolicy);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_RejectsDisposedSourceReferenceBeforeMutation()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new GameplayEffect("DisposedStateDeltaSource", EDurationPolicy.Infinite);
            source.Dispose();

            var delta = new GASAbilitySystemStateDeltaBuffer
            {
                Sequence = 1u,
                BaseVersion = 0UL,
                CurrentVersion = 1UL,
                StateChecksum = 1UL,
                ChangeMask = AbilitySystemStateChangeMask.ActiveEffects,
                ActiveEffects = new[]
                {
                    GASActiveEffectStateData.FromRaw(
                        301,
                        definition,
                        source,
                        1,
                        1,
                        0L,
                        0L,
                        -1L,
                        default,
                        Array.Empty<GASSetByCallerTagStateData>(),
                        0)
                },
                ActiveEffectCount = 1
            };

            Assert.That(target.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));
            Assert.That(target.ActiveEffects, Is.Empty);
            Assert.That(target.StateDeltaResyncRequired, Is.False);

            target.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_DoesNotConfirmCommittedLocalEffectByPredictionKey()
        {
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec replicaAbility = replica.GrantAbility(new TestAbility("StateDeltaLocalTransaction"));
            var definition = new GameplayEffect("StateDeltaDoesNotConfirmLocalEffect", EDurationPolicy.Infinite);
            GASPredictionKey predictionKey = replica.OpenPredictionWindow(replicaAbility);
            GameplayEffectApplicationResult localEffect;
            using (replica.BeginPredictionScope(predictionKey))
            {
                localEffect = replica.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(definition, replica));
            }

            Assert.That(localEffect.Succeeded, Is.True);
            Assert.That(replica.CommitPredictionWindow(predictionKey), Is.True);
            Assert.That(replica.PredictionManager.PendingPredictedEffects, Is.Empty);

            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var authority = new AbilitySystemComponent(authorityContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            authority.GrantAbility(new TestAbility("StateDeltaLocalTransaction"));
            GameplayEffectContext authorityEffectContext = authority.MakeEffectContext();
            authorityEffectContext.PredictionKey = predictionKey;
            GameplayEffectApplicationResult authorityEffect = authority.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(definition, authority, authorityEffectContext));
            Assert.That(authorityEffect.Succeeded, Is.True);
            Assert.That(authorityEffect.ActiveEffect.ReconciliationId, Is.GreaterThan(0));

            var delta = new GASAbilitySystemStateDeltaBuffer
            {
                Sequence = 1u,
                BaseVersion = 0UL,
                CurrentVersion = 1UL,
                StateChecksum = authority.ComputeReplicatedStateChecksum(),
                ChangeMask = AbilitySystemStateChangeMask.ActiveEffects,
                ActiveEffects = new[]
                {
                    GASActiveEffectStateData.FromRaw(
                        302,
                        definition,
                        replica,
                        1,
                        1,
                        0L,
                        0L,
                        -1L,
                        predictionKey,
                        Array.Empty<GASSetByCallerTagStateData>(),
                        0)
                },
                ActiveEffectCount = 1
            };

            Assert.That(replica.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.ChecksumMismatch));
            Assert.That(replica.StateDeltaResyncRequired, Is.True);
            Assert.That(replica.ActiveEffects.Count, Is.EqualTo(2));
            Assert.That(localEffect.ActiveEffect.ReconciliationId, Is.Zero,
                "A committed local prediction must not allocate an authority reconciliation identity.");
            Assert.That(replica.ActiveEffects[0], Is.SameAs(localEffect.ActiveEffect));
            Assert.That(replica.ActiveEffects[1], Is.Not.SameAs(localEffect.ActiveEffect));
            Assert.That(replica.ActiveEffects[1].ReconciliationId, Is.EqualTo(302));

            authority.Dispose();
            authorityContext.Dispose();
            replica.Dispose();
            replicaContext.Dispose();
        }

        [Test]
        public void PredictedEffectRemoval_InvalidatesReferenceWithoutAffectingReplacement()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new TestAbility());
            var definition = new GameplayEffect("RemovedPredictedEffect", EDurationPolicy.Infinite);
            GASPredictionKey predictionKey = asc.OpenPredictionWindow(abilitySpec);
            GameplayEffectApplicationResult predicted;
            using (asc.BeginPredictionScope(predictionKey))
            {
                predicted = asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(definition, asc));
            }

            Assert.That(predicted.Succeeded, Is.True);
            ActiveGameplayEffect removedEffect = predicted.ActiveEffect;
            Assert.That(asc.PredictionManager.PendingPredictedEffects.Count, Is.EqualTo(1));
            Assert.That(asc.TryRemoveActiveEffect(predicted.ActiveEffect), Is.True);
            Assert.That(asc.PredictionManager.PendingPredictedEffects, Is.Empty);

            GameplayEffectApplicationResult replacement = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(new GameplayEffect("ReplacementEffect", EDurationPolicy.Infinite), asc));
            Assert.That(replacement.Succeeded, Is.True);
            Assert.That(replacement.ActiveEffect, Is.Not.SameAs(removedEffect));
            Assert.That(asc.TryRemoveActiveEffect(removedEffect), Is.False, "A removed effect reference must not remove its replacement.");
            Assert.That(asc.RollbackPredictionWindow(predictionKey), Is.True);
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0], Is.SameAs(replacement.ActiveEffect));

            Assert.That(asc.TryRemoveActiveEffect(replacement.ActiveEffect), Is.True);
            asc.ClearAbility(abilitySpec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void PredictionRollbackObserver_SeesRestoredCurrentValue()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.AddAttributeSet(attributes);
            GameplayAbilitySpec spec = asc.GrantAbility(new TestAbility());
            GASPredictionKey predictionKey = asc.OpenPredictionWindow(spec);
            using (asc.BeginPredictionScope(predictionKey))
            {
                Assert.That(
                    asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(CreateInstantAdd("ObserverRollback", 10f), asc)).Succeeded,
                    Is.True);
            }
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue));

            long observedBaseRaw = long.MinValue;
            long observedCurrentRaw = long.MinValue;
            asc.OnPredictionWindowClosed += (key, status) =>
            {
                if (key.Equals(predictionKey) && status == GASPredictionWindowStatus.RolledBack)
                {
                    observedBaseRaw = attributes.Health.BaseValueRaw;
                    observedCurrentRaw = attributes.Health.CurrentValueRaw;
                }
            };

            Assert.That(asc.RollbackPredictionWindow(predictionKey), Is.True);
            Assert.That(observedBaseRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(observedCurrentRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void PredictionKeySequence_WrapsToOneAndSkipsAnOpenCollision()
        {
            var manager = new PredictionManager();
            var owner = new GASEntityId(71);
            manager.LocalPredictionInputSequence = int.MaxValue;

            GASPredictionKey first = manager.CreatePredictionKey(owner);

            Assert.That(first.Owner, Is.EqualTo(owner));
            Assert.That(first.InputSequence, Is.EqualTo(1));
            Assert.That(first.Value, Is.EqualTo(1));
            Assert.That(manager.RegisterWindow(new GASPredictionWindowData(first, default, default, 1, 0L, 60L)), Is.True);

            manager.LocalPredictionInputSequence = int.MaxValue;
            GASPredictionKey collisionSkipped = manager.CreatePredictionKey(owner);

            Assert.That(collisionSkipped.Owner, Is.EqualTo(owner));
            Assert.That(collisionSkipped.InputSequence, Is.EqualTo(2));
            Assert.That(collisionSkipped.Value, Is.EqualTo(2));
            Assert.That(collisionSkipped, Is.Not.EqualTo(first));
        }

        [Test]
        public void StateDelta_InboundApplyDoesNotPolluteOutboundTracking()
        {
            GameplayTag looseTag = RegisterTag("Test.GAS.Transaction.Loose");
            GameplayTag effectTag = RegisterTag("Test.GAS.Transaction.Effect");
            var server = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var client = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            server.AddLooseGameplayTag(looseTag);
            var delta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(delta);
            delta.Sequence = 1u;

            Assert.That(client.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.PendingStateChangeMask, Is.EqualTo(AbilitySystemStateChangeMask.None));

            var grantedTags = new GameplayTagContainer();
            grantedTags.AddTag(effectTag);
            GameplayEffectApplicationResult effectResult = server.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect("EffectTagSource", EDurationPolicy.Infinite, grantedTags: grantedTags),
                    server));
            Assert.That(effectResult.Succeeded, Is.True);
            Assert.That(server.PendingAddedTags, Is.Empty);

            server.Dispose();
            client.Dispose();
        }

        [Test]
        public void StateDelta_ReassignsAbilityHandleWithoutCorruptingIndexes()
        {
            var context = new GASRuntimeContext();
            var server = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var client = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new TestAbility("ReplicatedHandle");

            GameplayAbilitySpec serverSpec = server.GrantAbility(definition);
            GameplayAbilitySpec consumedClientHandle = client.GrantAbility(new TestAbility("ConsumedHandle"));
            int oldConsumedHandle = consumedClientHandle.Handle;
            client.ClearAbility(consumedClientHandle);
            GameplayAbilitySpec clientSpec = client.GrantAbility(definition);
            int oldClientHandle = clientSpec.Handle;
            Assert.That(oldClientHandle, Is.Not.EqualTo(serverSpec.Handle));

            var delta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(delta);
            delta.Sequence = 1u;

            Assert.That(client.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(serverSpec.Handle, out GameplayAbilitySpec remapped), Is.True);
            Assert.That(remapped, Is.SameAs(clientSpec));
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(oldClientHandle, out _), Is.False);
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(oldConsumedHandle, out GameplayAbilitySpec reusedHandle), Is.True);
            Assert.That(reusedHandle, Is.SameAs(clientSpec));
            Assert.That(client.ValidateRuntimeIndexes(), Is.True);

            server.Dispose();
            client.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_RemovesTheExactSpecWhenOneDefinitionIsGrantedMoreThanOnce()
        {
            var context = new GASRuntimeContext();
            var server = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var client = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new TestAbility("RepeatedDefinition");
            GameplayAbilitySpec firstServerSpec = server.GrantAbility(definition, 1);
            GameplayAbilitySpec secondServerSpec = server.GrantAbility(definition, 2);
            int removedHandle = firstServerSpec.Handle;
            int retainedHandle = secondServerSpec.Handle;
            var baseline = new GASAbilitySystemStateDeltaBuffer();

            server.CapturePendingStateDeltaNonAlloc(baseline);
            Assert.That(client.TryApplyStateDelta(baseline, out GASStateDeltaRejectionReason baselineReason), Is.True);
            Assert.That(baselineReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(removedHandle, out _), Is.True);
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(retainedHandle, out _), Is.True);

            server.ClearAbility(firstServerSpec);
            var removal = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(removal);

            Assert.That(removal.RemovedAbilitySpecHandleCount, Is.EqualTo(1));
            Assert.That(removal.RemovedAbilitySpecHandles[0], Is.EqualTo(removedHandle));
            Assert.That(removal.GrantedAbilityCount, Is.EqualTo(1));
            Assert.That(removal.GrantedAbilities[0].SpecHandle, Is.EqualTo(retainedHandle));
            Assert.That(client.TryApplyStateDelta(removal, out GASStateDeltaRejectionReason removalReason), Is.True);
            Assert.That(removalReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(removedHandle, out _), Is.False);
            Assert.That(client.AbilitySpecs.TryGetSpecByHandle(retainedHandle, out GameplayAbilitySpec retainedSpec), Is.True);
            Assert.That(retainedSpec.AbilityCDO, Is.SameAs(definition));
            Assert.That(retainedSpec.Level, Is.EqualTo(2));
            Assert.That(client.GetActivatableAbilities().Count, Is.EqualTo(1));
            Assert.That(client.ValidateRuntimeIndexes(), Is.True);

            server.Dispose();
            client.Dispose();
            context.Dispose();
        }

        [Test]
        public void FullStateCapture_CopiesExactProcessLocalStateAndReusesReservedStorage()
        {
            GameplayTag looseTag = RegisterTag("Test.GAS.Transaction.FullState.Loose");
            GameplayTag setByCallerTag = RegisterTag("Test.GAS.Transaction.FullState.SetByCaller");
            GameplayTag dynamicGrantedTag = RegisterTag("Test.GAS.Transaction.FullState.DynamicGranted");
            GameplayTag dynamicAssetTag = RegisterTag("Test.GAS.Transaction.FullState.DynamicAsset");
            GameplayTag missingOngoingTag = RegisterTag("Test.GAS.Transaction.FullState.MissingOngoing");
            var context = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(75f);
            asc.AddAttributeSet(attributes);

            var abilityDefinition = new TestAbility("FullStateAbility");
            GameplayAbilitySpec firstAbility = asc.GrantAbility(abilityDefinition, 1);
            GameplayAbilitySpec secondAbility = asc.GrantAbility(abilityDefinition, 3);
            asc.AddLooseGameplayTag(looseTag);
            asc.AddLooseGameplayTag(looseTag);

            var requiredTags = new GameplayTagContainer();
            requiredTags.AddTag(missingOngoingTag);
            var effectDefinition = new GameplayEffect(
                "FullStateEffect",
                EDurationPolicy.Infinite,
                ongoingTagRequirements: new GameplayTagRequirements(new GameplayTagContainer(), requiredTags));
            GameplayEffectContext effectContext = asc.MakeEffectContext();
            effectContext.AddInstigator(asc, firstAbility.GetPrimaryInstance());
            GameplayEffectSpec effectSpec = GameplayEffectSpec.Create(effectDefinition, asc, effectContext, 2);
            effectSpec.SetSetByCallerMagnitude(setByCallerTag, 7f);
            effectSpec.SetSetByCallerMagnitude("Power", 11f);
            effectSpec.DynamicGrantedTags.AddTag(dynamicGrantedTag);
            effectSpec.DynamicAssetTags.AddTag(dynamicAssetTag);
            GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(effectSpec);
            Assert.That(application.Succeeded, Is.True);
            Assert.That(application.ActiveEffect.IsInhibited, Is.True);
            Assert.That(application.ActiveEffect.SourceAbilitySpecHandle, Is.EqualTo(firstAbility.Handle));

            var buffer = new GASAbilitySystemFullStateBuffer();
            buffer.Reserve(
                grantedAbilityCapacity: 2,
                activeEffectCapacity: 1,
                attributeCapacity: 1,
                looseTagCapacity: 1,
                maxSetByCallerTagsPerEffect: 1,
                maxSetByCallerNamesPerEffect: 1,
                maxDynamicGrantedTagsPerEffect: 1,
                maxDynamicAssetTagsPerEffect: 1);
            GASGrantedAbilityStateData[] grantedStorage = buffer.GrantedAbilities;
            GASActiveEffectStateData[] effectStorage = buffer.ActiveEffects;
            GASAttributeStateData[] attributeStorage = buffer.Attributes;
            GASTagCountStateData[] tagStorage = buffer.LooseTags;

            asc.CaptureFullStateNonAlloc(buffer);

            Assert.That(buffer.SchemaVersion, Is.EqualTo(GASRuntimeDataContract.ReconciliationSchemaVersion));
            Assert.That(buffer.StateVersion, Is.EqualTo(asc.StateVersion));
            Assert.That(buffer.StateChecksum, Is.EqualTo(asc.ComputeReplicatedStateChecksum()));
            Assert.That(buffer.GrantedAbilityCount, Is.EqualTo(2));
            Assert.That(buffer.GrantedAbilities[0].SpecHandle, Is.EqualTo(firstAbility.Handle));
            Assert.That(buffer.GrantedAbilities[1].SpecHandle, Is.EqualTo(secondAbility.Handle));
            Assert.That(buffer.GrantedAbilities[1].Level, Is.EqualTo(3));
            Assert.That(buffer.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveEffects[0].ReconciliationId, Is.EqualTo(application.ActiveEffect.ReconciliationId));
            Assert.That(buffer.ActiveEffects[0].EffectDefinition, Is.SameAs(effectDefinition));
            Assert.That(buffer.ActiveEffects[0].SourceAbilitySpecHandle, Is.EqualTo(firstAbility.Handle));
            Assert.That(buffer.ActiveEffects[0].IsInhibited, Is.True);
            Assert.That(buffer.ActiveEffects[0].SetByCallerTagMagnitudeCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveEffects[0].SetByCallerTagMagnitudes[0].Tag, Is.EqualTo(setByCallerTag));
            Assert.That(buffer.ActiveEffects[0].SetByCallerTagMagnitudes[0].ValueRaw, Is.EqualTo(GASFixedValue.FromInt(7).RawValue));
            Assert.That(buffer.ActiveEffects[0].SetByCallerNameMagnitudeCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveEffects[0].SetByCallerNameMagnitudes[0].Name, Is.EqualTo("Power"));
            Assert.That(buffer.ActiveEffects[0].SetByCallerNameMagnitudes[0].ValueRaw, Is.EqualTo(GASFixedValue.FromInt(11).RawValue));
            Assert.That(buffer.ActiveEffects[0].DynamicGrantedTagCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveEffects[0].DynamicGrantedTags[0], Is.EqualTo(dynamicGrantedTag));
            Assert.That(buffer.ActiveEffects[0].DynamicAssetTagCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveEffects[0].DynamicAssetTags[0], Is.EqualTo(dynamicAssetTag));
            Assert.That(buffer.AttributeCount, Is.EqualTo(1));
            Assert.That(buffer.Attributes[0].AttributeName, Is.EqualTo("Health"));
            Assert.That(buffer.Attributes[0].BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
            Assert.That(buffer.Attributes[0].CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(75).RawValue));
            Assert.That(buffer.LooseTagCount, Is.EqualTo(1));
            Assert.That(buffer.LooseTags[0].Tag, Is.EqualTo(looseTag));
            Assert.That(buffer.LooseTags[0].ExplicitCount, Is.EqualTo(2));

            asc.CaptureFullStateNonAlloc(buffer);
            Assert.That(buffer.GrantedAbilities, Is.SameAs(grantedStorage));
            Assert.That(buffer.ActiveEffects, Is.SameAs(effectStorage));
            Assert.That(buffer.Attributes, Is.SameAs(attributeStorage));
            Assert.That(buffer.LooseTags, Is.SameAs(tagStorage));

            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_RejectsLooseTagEdgesThatDoNotMatchLocalBaseline()
        {
            GameplayTag tag = RegisterTag("Test.GAS.Transaction.TagEdgeBaseline");
            var context = new GASRuntimeContext();
            var server = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var duplicateClient = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var missingClient = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            server.AddLooseGameplayTag(tag);
            duplicateClient.AddLooseGameplayTag(tag);
            var addedDelta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(addedDelta);
            addedDelta.Sequence = 1u;

            Assert.That(duplicateClient.TryApplyStateDelta(addedDelta, out GASStateDeltaRejectionReason duplicateReason), Is.False);
            Assert.That(duplicateReason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));
            Assert.That(duplicateClient.CombinedTags.GetExplicitTagCount(tag), Is.EqualTo(1));

            Assert.That(missingClient.TryApplyStateDelta(addedDelta, out _), Is.True);
            missingClient.RemoveLooseGameplayTag(tag);

            server.RemoveLooseGameplayTag(tag);
            var removedDelta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(removedDelta);
            removedDelta.Sequence = 2u;

            Assert.That(missingClient.TryApplyStateDelta(removedDelta, out GASStateDeltaRejectionReason missingReason), Is.False);
            Assert.That(missingReason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));

            server.Dispose();
            duplicateClient.Dispose();
            missingClient.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_CapacityProjectionAllowsEffectAndGrantedAbilityReplacement()
        {
            var context = new GASRuntimeContext();
            var limits = new GASRuntimeLimits(maxGrantedAbilities: 1, maxActiveEffects: 1);
            var options = new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits);
            var server = new AbilitySystemComponent(context, options);
            var client = new AbilitySystemComponent(context, options);
            var firstDefinition = new GameplayEffect(
                "FirstCapacityEffect",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { new TestAbility("FirstGranted") });
            var secondDefinition = new GameplayEffect(
                "SecondCapacityEffect",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { new TestAbility("SecondGranted") });
            GameplayEffectApplicationResult first = server.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(firstDefinition, server));
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.ActiveEffect.ReconciliationId, Is.EqualTo(1));

            var buffer = new GASAbilitySystemStateDeltaBuffer();
            server.PreparePendingStateDeltaNonAlloc(buffer);
            Assert.That(buffer.HasChanges, Is.True);
            Assert.That(client.TryApplyStateDelta(buffer, out GASStateDeltaRejectionReason baselineReason), Is.True);
            Assert.That(baselineReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(server.CommitPreparedStateDelta(buffer), Is.True);
            Assert.That(client.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(client.AbilitySpecs.Count, Is.EqualTo(1));
            Assert.That(client.GetActivatableAbilities()[0].GrantingEffect, Is.SameAs(client.ActiveEffects[0]));

            var clientState = new GASAbilitySystemFullStateBuffer();
            client.CaptureFullStateNonAlloc(clientState);
            Assert.That(
                clientState.GrantedAbilities[0].GrantingEffectReconciliationId,
                Is.EqualTo(client.ActiveEffects[0].ReconciliationId));

            Assert.That(server.TryRemoveActiveEffect(first.ActiveEffect), Is.True);
            GameplayEffectApplicationResult second = server.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(secondDefinition, server));
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.ActiveEffect.ReconciliationId, Is.EqualTo(2));

            server.PreparePendingStateDeltaNonAlloc(buffer);
            Assert.That(buffer.HasChanges, Is.True);
            Assert.That(client.TryApplyStateDelta(buffer, out GASStateDeltaRejectionReason replacementReason), Is.True);
            Assert.That(replacementReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(server.CommitPreparedStateDelta(buffer), Is.True);
            Assert.That(client.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(client.ActiveEffects[0].Spec.Def, Is.SameAs(secondDefinition));
            Assert.That(client.AbilitySpecs.Count, Is.EqualTo(1));
            Assert.That(client.GetActivatableAbilities()[0].Ability.Name, Is.EqualTo("SecondGranted"));
            Assert.That(client.GetActivatableAbilities()[0].GrantingEffect, Is.SameAs(client.ActiveEffects[0]));
            Assert.That(client.ValidateRuntimeIndexes(), Is.True);

            server.Dispose();
            client.Dispose();
            context.Dispose();
        }

        [Test]
        public void FullState_AtAbilityCapacityDoesNotDuplicateEffectGrantedAbility()
        {
            var context = new GASRuntimeContext();
            var limits = new GASRuntimeLimits(maxGrantedAbilities: 1, maxActiveEffects: 1);
            var options = new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits);
            var authority = new AbilitySystemComponent(context, options);
            var replica = new AbilitySystemComponent(context, options);
            var grantedAbility = new TestAbility("FullStateEffectGrant");
            var effectDefinition = new GameplayEffect(
                "FullStateCapacityEffect",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { grantedAbility });

            GameplayEffectApplicationResult application = authority.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effectDefinition, authority));
            Assert.That(application.Succeeded, Is.True);
            Assert.That(authority.AbilitySpecs.Count, Is.EqualTo(1));

            var snapshot = new GASAbilitySystemFullStateBuffer();
            authority.CaptureFullStateNonAlloc(snapshot);

            Assert.That(
                replica.TryApplyFullStateSnapshot(snapshot, out GASStateDeltaRejectionReason reason),
                Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(replica.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(replica.AbilitySpecs.Count, Is.EqualTo(1));
            Assert.That(
                replica.GetActivatableAbilities()[0].GrantingEffect,
                Is.SameAs(replica.ActiveEffects[0]));
            Assert.That(replica.ComputeReplicatedStateChecksum(), Is.EqualTo(snapshot.StateChecksum));
            Assert.That(replica.ValidateRuntimeIndexes(), Is.True);

            authority.Dispose();
            replica.Dispose();
            context.Dispose();
        }

        [Test]
        public void FullState_PerExecutionSourceProvenanceRoundTripsWithoutAbilityInstance()
        {
            var context = new GASRuntimeContext();
            var replica = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var sourceAbility = new PerExecutionTestAbility();
            var effectDefinition = new GameplayEffect(
                "PerExecutionSourceEffect",
                EDurationPolicy.Infinite);
            var snapshot = new GASAbilitySystemFullStateBuffer
            {
                GrantedAbilities = new[]
                {
                    new GASGrantedAbilityStateData(41, sourceAbility, 1, false, false, 0)
                },
                GrantedAbilityCount = 1,
                ActiveEffects = new[]
                {
                    GASActiveEffectStateData.FromRaw(
                        73,
                        effectDefinition,
                        replica,
                        41,
                        1,
                        1,
                        false,
                        0L,
                        0L,
                        -1L,
                        default,
                        Array.Empty<GASSetByCallerTagStateData>(),
                        0,
                        Array.Empty<GASSetByCallerNameStateData>(),
                        0,
                        Array.Empty<GameplayTag>(),
                        0,
                        Array.Empty<GameplayTag>(),
                        0)
                },
                ActiveEffectCount = 1
            };

            Assert.That(
                replica.TryApplyFullStateSnapshot(snapshot, out GASStateDeltaRejectionReason reason),
                Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(replica.AbilitySpecs.TryGetSpecByHandle(41, out GameplayAbilitySpec spec), Is.True);
            Assert.That(spec.AbilityInstance, Is.Null);
            Assert.That(replica.ActiveEffects[0].SourceAbilitySpecHandle, Is.EqualTo(41));

            var roundTrip = new GASAbilitySystemFullStateBuffer();
            replica.CaptureFullStateNonAlloc(roundTrip);
            Assert.That(roundTrip.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(roundTrip.ActiveEffects[0].SourceAbilitySpecHandle, Is.EqualTo(41));
            Assert.That(roundTrip.StateChecksum, Is.EqualTo(replica.ComputeReplicatedStateChecksum()));

            replica.Dispose();
            context.Dispose();
        }

        [Test]
        public void FullState_RejectsFalseEffectGrantedAbilityAssociationBeforeMutation()
        {
            var context = new GASRuntimeContext();
            var replica = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var abilityDefinition = new TestAbility("InvalidEffectGrantAssociation");
            var effectDefinition = new GameplayEffect(
                "EffectThatDoesNotGrantTheAbility",
                EDurationPolicy.Infinite);
            var snapshot = new GASAbilitySystemFullStateBuffer
            {
                GrantedAbilities = new[]
                {
                    new GASGrantedAbilityStateData(
                        11,
                        abilityDefinition,
                        1,
                        false,
                        false,
                        17)
                },
                GrantedAbilityCount = 1,
                ActiveEffects = new[]
                {
                    GASActiveEffectStateData.FromRaw(
                        17,
                        effectDefinition,
                        null,
                        0,
                        1,
                        1,
                        false,
                        0L,
                        0L,
                        -1L,
                        default,
                        Array.Empty<GASSetByCallerTagStateData>(),
                        0,
                        Array.Empty<GASSetByCallerNameStateData>(),
                        0,
                        Array.Empty<GameplayTag>(),
                        0,
                        Array.Empty<GameplayTag>(),
                        0)
                },
                ActiveEffectCount = 1
            };

            Assert.That(
                replica.TryApplyFullStateSnapshot(
                    snapshot,
                    out GASStateDeltaRejectionReason reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));
            Assert.That(replica.AbilitySpecs.Count, Is.Zero);
            Assert.That(replica.ActiveEffects.Count, Is.Zero);

            replica.Dispose();
            context.Dispose();
        }

        [Test]
        public void StateDelta_PrepareWithoutCommitRetainsDirtyStateUntilSuccessfulCommit()
        {
            GameplayTag tag = RegisterTag("Test.GAS.Transaction.PrepareWithoutCommit");
            var context = new GASRuntimeContext();
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var buffer = new GASAbilitySystemStateDeltaBuffer();
            source.AddLooseGameplayTag(tag);

            source.PreparePendingStateDeltaNonAlloc(buffer);
            Assert.That(buffer.HasChanges, Is.True);
            Assert.That(source.PendingStateChangeMask, Is.EqualTo(AbilitySystemStateChangeMask.Tags));

            source.PreparePendingStateDeltaNonAlloc(buffer);
            Assert.That(source.PendingStateChangeMask, Is.EqualTo(AbilitySystemStateChangeMask.Tags));
            Assert.That(source.CommitPreparedStateDelta(buffer), Is.True);
            Assert.That(source.PendingStateChangeMask, Is.EqualTo(AbilitySystemStateChangeMask.None));

            source.Dispose();
            context.Dispose();
        }

        [Test]
        public void ReplicatedChecksum_UsesSemanticStateRatherThanTimersOrTagReferenceCounts()
        {
            GameplayTag tag = RegisterTag("Test.GAS.Transaction.Checksum");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectApplicationResult effect = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(new GameplayEffect("ChecksumDuration", EDurationPolicy.HasDuration, duration: 5f), asc));
            Assert.That(effect.Succeeded, Is.True);
            ulong beforeTick = asc.ComputeReplicatedStateChecksum();

            asc.Tick(1f, true);
            Assert.That(asc.ComputeReplicatedStateChecksum(), Is.EqualTo(beforeTick));

            asc.AddLooseGameplayTag(tag);
            ulong oneReference = asc.ComputeReplicatedStateChecksum();
            asc.AddLooseGameplayTag(tag);
            Assert.That(asc.ComputeReplicatedStateChecksum(), Is.EqualTo(oneReference));
            asc.RemoveLooseGameplayTag(tag);
            Assert.That(asc.ComputeReplicatedStateChecksum(), Is.EqualTo(oneReference));
            asc.RemoveLooseGameplayTag(tag);
            Assert.That(asc.ComputeReplicatedStateChecksum(), Is.Not.EqualTo(oneReference));
            asc.Dispose();
        }

        [Test]
        public void PredictionCommit_PreservesLiveLocalEffectAndClearsPendingBookkeeping()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(new TestAbility("LocalChecksumTransaction"));
            GASPredictionKey predictionKey = asc.OpenPredictionWindow(abilitySpec);
            GameplayEffectApplicationResult localEffect;
            using (asc.BeginPredictionScope(predictionKey))
            {
                localEffect = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(
                        new GameplayEffect("CommittedLocalChecksumEffect", EDurationPolicy.Infinite),
                        asc));
            }

            Assert.That(localEffect.Succeeded, Is.True);
            Assert.That(localEffect.ActiveEffect.ReconciliationId, Is.EqualTo(0));
            Assert.That(asc.PredictionManager.PendingPredictedEffects.Count, Is.EqualTo(1));

            Assert.That(asc.CommitPredictionWindow(predictionKey), Is.True);
            Assert.That(asc.PredictionManager.PendingPredictedEffects, Is.Empty);
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0], Is.SameAs(localEffect.ActiveEffect));

            Assert.That(asc.TryRemoveActiveEffect(localEffect.ActiveEffect), Is.True);
            asc.ClearAbility(abilitySpec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void Dispose_RemovesAttributeBackReferencesBeforeReturningEffects()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            asc.AddAttributeSet(attributes);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect(
                        "DisposeBackReference",
                        EDurationPolicy.Infinite,
                        modifiers: new List<ModifierInfo>
                        {
                            new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                        }),
                    asc));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(attributes.Health.ActiveModifierSourceCount, Is.EqualTo(1));

            asc.Dispose();

            Assert.That(attributes.Health.ActiveModifierSourceCount, Is.EqualTo(0));
            Assert.That(attributes.OwningAbilitySystemComponent, Is.Null);
        }

        [Test]
        public void AttributeSetDetach_IsTransactionalAcrossRuntimeAndCore()
        {
            var first = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.MirrorRuntime);
            var second = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new TestAttributeSet();
            first.AddAttributeSet(attributes);
            Assert.That(first.CoreState.AttributeCount, Is.EqualTo(1));
            GameplayEffectApplicationResult result = first.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    new GameplayEffect(
                        "DetachGuard",
                        EDurationPolicy.Infinite,
                        modifiers: new List<ModifierInfo>
                        {
                            new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                        }),
                    first));
            Assert.That(result.Succeeded, Is.True);

            Assert.Throws<InvalidOperationException>(() => first.RemoveAttributeSet(attributes));
            Assert.Throws<InvalidOperationException>(() => second.MarkAttributeDirty(attributes.Health));
            Assert.That(first.AttributeSets.Count, Is.EqualTo(1));
            Assert.That(first.CoreState.AttributeCount, Is.EqualTo(1));

            Assert.That(first.TryRemoveActiveEffect(result.ActiveEffect), Is.True);
            first.RemoveAttributeSet(attributes);
            Assert.That(first.AttributeSets, Is.Empty);
            Assert.That(first.CoreState.AttributeCount, Is.EqualTo(0));
            Assert.That(attributes.OwningAbilitySystemComponent, Is.Null);

            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void ReplicatedSpecUpdate_IsAtomicAndCarriesNameAndDynamicTagState()
        {
            GameplayTag dynamicGranted = RegisterTag("Test.GAS.Transaction.DynamicGranted");
            GameplayTag dynamicAsset = RegisterTag("Test.GAS.Transaction.DynamicAsset");
            var throwingDefinition = new GameplayEffect(
                "AtomicReplication",
                EDurationPolicy.HasDuration,
                duration: 5f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new LevelTwoThrowsMagnitude())
                });
            GameplayEffectSpec throwingSpec = GameplayEffectSpec.Create(throwingDefinition, null, 1);
            long originalMagnitude = throwingSpec.GetCalculatedMagnitudeRaw(0);

            Assert.Throws<InvalidOperationException>(() => throwingSpec.ApplyReplicatedStateRaw(
                2,
                GASFixedValue.FromInt(9).RawValue,
                Array.Empty<GameplayTag>(),
                Array.Empty<long>(),
                0,
                Array.Empty<string>(),
                Array.Empty<long>(),
                0,
                Array.Empty<GameplayTag>(),
                0,
                Array.Empty<GameplayTag>(),
                0));
            Assert.That(throwingSpec.Level, Is.EqualTo(1));
            Assert.That(throwingSpec.DurationRaw, Is.EqualTo(GASFixedValue.FromInt(5).RawValue));
            Assert.That(throwingSpec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(originalMagnitude));
            throwingSpec.Discard();

            var nameDefinition = new GameplayEffect(
                "NameReplication",
                EDurationPolicy.HasDuration,
                duration: 5f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new SetByCallerMagnitude("Damage"))
                });
            GameplayEffectSpec nameSpec = GameplayEffectSpec.Create(nameDefinition, null, 1);
            nameSpec.ApplyReplicatedStateRaw(
                2,
                GASFixedValue.FromInt(7).RawValue,
                Array.Empty<GameplayTag>(),
                Array.Empty<long>(),
                0,
                new[] { "Damage" },
                new[] { GASFixedValue.FromInt(13).RawValue },
                1,
                new[] { dynamicGranted },
                1,
                new[] { dynamicAsset },
                1);

            Assert.That(nameSpec.GetCalculatedMagnitudeRaw(0), Is.EqualTo(GASFixedValue.FromInt(13).RawValue));
            Assert.That(nameSpec.DynamicGrantedTags.HasTagExact(dynamicGranted), Is.True);
            Assert.That(nameSpec.DynamicAssetTags.HasTagExact(dynamicAsset), Is.True);
            nameSpec.Discard();

            var asc = new AbilitySystemComponent();
            GameplayEffectSpec activeSpec = GameplayEffectSpec.Create(nameDefinition, asc, 1);
            activeSpec.SetTarget(asc);
            activeSpec.SetSetByCallerMagnitude("Damage", 3f);
            ActiveGameplayEffect activeEffect = ActiveGameplayEffect.Create(activeSpec);

            Assert.Throws<ArgumentOutOfRangeException>(() => activeEffect.ApplyReplicatedStateRaw(
                2,
                2,
                GASFixedValue.FromInt(8).RawValue,
                GASFixedValue.FromInt(8).RawValue,
                -1L,
                Array.Empty<GameplayTag>(),
                Array.Empty<long>(),
                0));
            Assert.That(activeEffect.Spec.Level, Is.EqualTo(1));
            Assert.That(activeEffect.Spec.DurationRaw, Is.EqualTo(GASFixedValue.FromInt(5).RawValue));
            Assert.That(activeEffect.Spec.GetSetByCallerMagnitudeRaw("Damage"), Is.EqualTo(GASFixedValue.FromInt(3).RawValue));
            Assert.That(activeEffect.StackCount, Is.EqualTo(1));

            activeEffect.ReleaseRuntimeLease();
            asc.Dispose();
        }

        [Test]
        public void StateDelta_RoundTripsNameSetByCallerAndDynamicTags()
        {
            GameplayTag dynamicGranted = RegisterTag("Test.GAS.Transaction.RoundTripGranted");
            GameplayTag dynamicAsset = RegisterTag("Test.GAS.Transaction.RoundTripAsset");
            var context = new GASRuntimeContext();
            var server = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var client = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var serverAttributes = new TestAttributeSet();
            var clientAttributes = new TestAttributeSet();
            server.AddAttributeSet(serverAttributes);
            client.AddAttributeSet(clientAttributes);
            var definition = new GameplayEffect(
                "ExtendedStateDelta",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new SetByCallerMagnitude("Damage"))
                });
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, server);
            spec.SetSetByCallerMagnitude("Damage", 13f);
            spec.DynamicGrantedTags.AddTag(dynamicGranted);
            spec.DynamicAssetTags.AddTag(dynamicAsset);
            GameplayEffectApplicationResult application = server.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(application.Succeeded, Is.True);
            Assert.That(application.ActiveEffect.ReconciliationId, Is.GreaterThan(0));
            server.Tick(0f, true);

            var delta = new GASAbilitySystemStateDeltaBuffer();
            server.CapturePendingStateDeltaNonAlloc(delta);
            delta.Sequence = 1u;

            Assert.That(client.TryApplyStateDelta(delta, out GASStateDeltaRejectionReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(client.ActiveEffects.Count, Is.EqualTo(1));
            GameplayEffectSpec replicatedSpec = client.ActiveEffects[0].Spec;
            Assert.That(replicatedSpec.GetSetByCallerMagnitudeRaw("Damage"), Is.EqualTo(GASFixedValue.FromInt(13).RawValue));
            Assert.That(replicatedSpec.DynamicGrantedTags.HasTagExact(dynamicGranted), Is.True);
            Assert.That(replicatedSpec.DynamicAssetTags.HasTagExact(dynamicAsset), Is.True);
            Assert.That(client.HasMatchingGameplayTag(dynamicGranted), Is.True);
            Assert.That(clientAttributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(13).RawValue));

            server.Dispose();
            client.Dispose();
            context.Dispose();
        }

        [Test]
        public void EffectSpecOwnership_RejectsContextAliasAndPostSubmissionMutation()
        {
            GameplayTag dynamicTag = RegisterTag("Test.GAS.Transaction.OwnedDynamicTag");
            var runtimeContext = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(runtimeContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new GameplayEffect("OwnedSpec", EDurationPolicy.Infinite);
            GameplayEffectContext effectContext = asc.MakeEffectContext();
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc, effectContext);

            Assert.Throws<InvalidOperationException>(() => GameplayEffectSpec.Create(definition, asc, effectContext));
            Assert.Throws<InvalidOperationException>(() => effectContext.Dispose());

            spec.DynamicGrantedTags.AddTag(dynamicTag);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);

            Assert.That(result.Succeeded, Is.True);
            Assert.Throws<InvalidOperationException>(() => spec.DynamicGrantedTags.AddTag(dynamicTag));
            Assert.Throws<InvalidOperationException>(() => spec.DynamicGrantedTags.Clear());
            Assert.Throws<InvalidOperationException>(() => spec.Discard());

            Assert.That(asc.TryRemoveActiveEffect(result.ActiveEffect), Is.True);
            Assert.That(runtimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            asc.Dispose();
            runtimeContext.Dispose();
        }

        [Test]
        public void EffectSpecEvaluation_RejectsLeaseMutationAndRecursiveSubmission()
        {
            var magnitude = new LeaseMutationMagnitude();
            var magnitudeDefinition = new GameplayEffect(
                "GuardedMagnitude",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, magnitude)
                });

            GameplayEffectSpec magnitudeSpec = GameplayEffectSpec.Create(magnitudeDefinition, null);
            Assert.That(magnitude.DiscardRejected, Is.True);
            magnitudeSpec.Discard();

            var requirement = new LeaseMutationRequirement();
            var requirementDefinition = new GameplayEffect(
                "GuardedRequirement",
                EDurationPolicy.Instant,
                customApplicationRequirements: new List<ICustomApplicationRequirement> { requirement });
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectSpec requirementSpec = GameplayEffectSpec.Create(requirementDefinition, asc);

            Assert.That(
                asc.CanApplyGameplayEffectSpec(requirementSpec),
                Is.EqualTo(GameplayEffectApplicationResultCode.Applied));
            Assert.That(requirement.DiscardRejected, Is.True);
            Assert.That(requirement.RecursiveSubmissionRejected, Is.True);
            Assert.That(requirement.RecursiveValidationRejected, Is.True);

            requirementSpec.Discard();
            asc.Dispose();
        }

        [Test]
        public void EffectCommit_RejectsSynchronousStructuralReentryAndConsumesNestedSpec()
        {
            var runtimeContext = new GASRuntimeContext();
            var asc = new AbilitySystemComponent(runtimeContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var outerDefinition = new GameplayEffect("OuterCommit", EDurationPolicy.Infinite);
            var nestedDefinition = new GameplayEffect("NestedCommit", EDurationPolicy.Instant);
            bool removeAccepted = true;
            GameplayEffectApplicationResultCode nestedCode = GameplayEffectApplicationResultCode.Applied;

            asc.OnGameplayEffectAppliedToSelf += effect =>
            {
                removeAccepted = asc.TryRemoveActiveEffect(effect);
                GameplayEffectSpec nestedSpec = GameplayEffectSpec.Create(nestedDefinition, asc);
                nestedCode = asc.ApplyGameplayEffectSpecToSelf(nestedSpec).Code;
            };

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(outerDefinition, asc));

            Assert.That(result.Code, Is.EqualTo(GameplayEffectApplicationResultCode.Applied));
            Assert.That(removeAccepted, Is.False);
            Assert.That(nestedCode, Is.EqualTo(GameplayEffectApplicationResultCode.ReentrantMutationRejected));
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.TryRemoveActiveEffect(result.ActiveEffect), Is.True);
            Assert.That(runtimeContext.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));

            asc.Dispose();
            runtimeContext.Dispose();
        }

        private static GameplayEffect CreateInstantAdd(string name, float magnitude)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(magnitude))
                });
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities transaction test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private sealed class TestAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }

        private class TestAbility : GameplayAbility
        {
            private readonly string abilityName;

            public TestAbility(string abilityName = "TransactionTest")
            {
                this.abilityName = abilityName;
                Initialize(
                    abilityName,
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

            public override GameplayAbility CreateRuntimeInstance() => new TestAbility(abilityName);
        }

        private sealed class PerExecutionTestAbility : GameplayAbility
        {
            public PerExecutionTestAbility()
            {
                Initialize(
                    "PerExecutionSource",
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

            public override GameplayAbility CreateRuntimeInstance() => new PerExecutionTestAbility();
        }

        private sealed class CountingAuthorityAbility : GameplayAbility
        {
            private readonly bool canActivate;

            public int ActivationCount { get; private set; }

            public CountingAuthorityAbility(bool canActivate = true)
            {
                this.canActivate = canActivate;
                Initialize(
                    "CountingAuthority",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.AuthorityOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
            {
                return canActivate && base.CanActivate(actorInfo, spec);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                ActivationCount++;
            }

            public override GameplayAbility CreateRuntimeInstance() => new CountingAuthorityAbility(canActivate);
        }

        private sealed class LevelTwoThrowsMagnitude : GameplayModMagnitudeCalculation
        {
            public override float CalculateMagnitude(GameplayEffectSpec spec)
            {
                if (spec.Level == 2)
                {
                    throw new InvalidOperationException("Expected replicated magnitude failure.");
                }
                return 3f;
            }
        }

        private sealed class LeaseMutationMagnitude : GameplayModMagnitudeCalculation
        {
            public bool DiscardRejected { get; private set; }

            public override float CalculateMagnitude(GameplayEffectSpec spec)
            {
                try
                {
                    spec.Discard();
                }
                catch (InvalidOperationException)
                {
                    DiscardRejected = true;
                }

                return 1f;
            }
        }

        private sealed class LeaseMutationRequirement : ICustomApplicationRequirement
        {
            public bool DiscardRejected { get; private set; }
            public bool RecursiveSubmissionRejected { get; private set; }
            public bool RecursiveValidationRejected { get; private set; }

            public bool CanApplyGameplayEffect(GameplayEffectSpec spec, AbilitySystemComponent target)
            {
                try
                {
                    spec.Discard();
                }
                catch (InvalidOperationException)
                {
                    DiscardRejected = true;
                }

                RecursiveSubmissionRejected =
                    target.ApplyGameplayEffectSpecToSelf(spec).Code == GameplayEffectApplicationResultCode.InvalidSpec;
                RecursiveValidationRejected =
                    target.CanApplyGameplayEffectSpec(spec) == GameplayEffectApplicationResultCode.InvalidSpec;
                return true;
            }
        }

    }
}
