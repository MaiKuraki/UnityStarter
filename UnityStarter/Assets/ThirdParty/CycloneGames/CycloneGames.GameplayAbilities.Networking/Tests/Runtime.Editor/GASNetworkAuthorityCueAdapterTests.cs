using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    public sealed class GASNetworkAuthorityCueAdapterTests
    {
        private const string CueTagName = "Test.GAS.Networking.AuthorityCue";
        private static readonly GASNetworkEntityId Entity = new GASNetworkEntityId(701UL);
        private const uint Epoch = 19u;

        private GameplayTag cueTag;
        private GASRuntimeContext context;
        private AbilitySystemComponent abilitySystem;
        private GASAuthorityIdentityMap identityMap;
        private GASNetworkStateVersion stateVersion;
        private TestEntityResolver entityResolver;
        private GASNetworkAuthorityCueAdapter adapter;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.RegisterDynamicTag(CueTagName, "Authority cue adapter test tag.");
            GameplayTagManager.InitializeIfNeeded();
            cueTag = GameplayTagManager.RequestTag(CueTagName);
            context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            identityMap = new GASAuthorityIdentityMap(Entity, Epoch, 4, 4);
            stateVersion = new GASNetworkStateVersion(abilitySystem, Epoch);
            entityResolver = new TestEntityResolver(abilitySystem, Entity, identityMap);
        }

        [TearDown]
        public void TearDown()
        {
            adapter?.Dispose();
            abilitySystem?.Dispose();
            context?.Dispose();
        }

        [Test]
        public void PrepareRejectCommit_RetriesHeadAndNeverCreatesEffectIdentity()
        {
            adapter = CreateAdapter(capacity: 2);
            GameplayEffectApplicationResult application = ApplyCueEffect(
                CreateCueEffect("Cue.Authority.Persistent", EDurationPolicy.Infinite));
            Assert.That(application.Succeeded, Is.True);
            Assert.That(identityMap.EffectCount, Is.Zero,
                "The cue adapter must not allocate authority effect identities.");

            Assert.That(
                adapter.PrepareNext(out GASCueExecuted withoutIdentity),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(withoutIdentity.SourceEffect.IsValid, Is.False);
            Assert.That(adapter.RejectPrepared(), Is.True);
            Assert.That(adapter.Count, Is.EqualTo(1));

            GASNetworkAuthorityStateAdapter stateAdapter = CreateStateAdapter(
                application.ActiveEffect.Spec.Def);
            Assert.That(stateAdapter.TryCapture(0u, out IGASNetworkStateView state), Is.True);
            Assert.That(
                withoutIdentity.AuthoritativeStateVersion,
                Is.EqualTo(state.StateVersion),
                "Cue and full-state publishing must share the same local-to-wire version domain.");
            Assert.That(
                identityMap.TryGetEffectId(
                    application.ActiveEffect.ReconciliationId,
                    out GASNetworkEffectId effectId),
                Is.True);
            Assert.That(
                adapter.PrepareNext(out GASCueExecuted first),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(first.SourceEffect, Is.EqualTo(effectId));
            Assert.That(
                adapter.PrepareNext(out GASCueExecuted retry),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(retry.CueSequence, Is.EqualTo(first.CueSequence));
            Assert.That(retry.Cue, Is.EqualTo(first.Cue));
            Assert.That(adapter.CommitPrepared(first.CueSequence + 1u), Is.False);
            Assert.That(adapter.CommitPrepared(first.CueSequence), Is.True);
            Assert.That(adapter.Count, Is.Zero);
            Assert.That(adapter.NextCueSequence, Is.EqualTo(2u));
        }

        [Test]
        public void InitialStateCapture_MapsLocalZeroToWireOne()
        {
            GASNetworkAuthorityStateAdapter stateAdapter = CreateStateAdapter(effect: null);

            Assert.That(abilitySystem.StateVersion, Is.Zero);
            Assert.That(stateAdapter.TryCapture(0u, out IGASNetworkStateView state), Is.True);
            Assert.That(state.StateVersion, Is.EqualTo(1UL));
        }

        [Test]
        public void RemovedCue_RetainsExistingWireIdentityAfterMapRemoval()
        {
            adapter = CreateAdapter(capacity: 2);
            GameplayEffectApplicationResult application = ApplyCueEffect(
                CreateCueEffect("Cue.Authority.Removed", EDurationPolicy.Infinite));
            Assert.That(
                identityMap.GetOrCreateEffectId(
                    application.ActiveEffect.ReconciliationId,
                    out GASNetworkEffectId effectId),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(adapter.PrepareNext(out GASCueExecuted active),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(adapter.CommitPrepared(active.CueSequence), Is.True);

            int reconciliationId = application.ActiveEffect.ReconciliationId;
            Assert.That(abilitySystem.TryRemoveActiveEffect(application.ActiveEffect), Is.True);
            Assert.That(
                identityMap.RemoveEffectByReconciliationId(reconciliationId, out GASNetworkEffectId removed),
                Is.True);
            Assert.That(removed, Is.EqualTo(effectId));

            Assert.That(adapter.PrepareNext(out GASCueExecuted removedCue),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(removedCue.Event, Is.EqualTo(GASCueEvent.Removed));
            Assert.That(removedCue.SourceEffect, Is.EqualTo(effectId));
        }

        [Test]
        public void CapacityExhaustion_FaultsWithoutOverwritingAndRequiresEpochReset()
        {
            adapter = CreateAdapter(capacity: 1);
            Assert.That(ApplyCueEffect(CreateCueEffect("Cue.Authority.First", EDurationPolicy.Instant)).Succeeded, Is.True);
            Assert.That(ApplyCueEffect(CreateCueEffect("Cue.Authority.Second", EDurationPolicy.Instant)).Succeeded, Is.True);

            Assert.That(adapter.Count, Is.EqualTo(1));
            Assert.That(adapter.Fault, Is.EqualTo(GASNetworkAuthorityCueFault.QueueCapacityExceeded));
            Assert.That(adapter.DroppedCueCount, Is.EqualTo(1L));
            Assert.That(adapter.PrepareNext(out _), Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Faulted));

            identityMap.ResetEpoch(Epoch + 1u);
            stateVersion.ResetEpoch(Epoch + 1u);
            adapter.ResetEpoch(Epoch + 1u);
            Assert.That(adapter.IsFaulted, Is.False);
            Assert.That(adapter.Count, Is.Zero);
            Assert.That(adapter.DroppedCueCount, Is.Zero);
            Assert.That(ApplyCueEffect(CreateCueEffect("Cue.Authority.AfterReset", EDurationPolicy.Instant)).Succeeded, Is.True);
            Assert.That(adapter.PrepareNext(out GASCueExecuted afterReset),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(afterReset.StreamEpoch, Is.EqualTo(Epoch + 1u));
        }

        [Test]
        public void ResetEpoch_RequiresBothSharedOwnersToReachTheNewEpoch()
        {
            adapter = CreateAdapter(capacity: 1);

            stateVersion.ResetEpoch(Epoch + 1u);
            Assert.Throws<InvalidOperationException>(() => adapter.ResetEpoch(Epoch + 1u));
            identityMap.ResetEpoch(Epoch + 1u);
            Assert.DoesNotThrow(() => adapter.ResetEpoch(Epoch + 1u));

            identityMap.ResetEpoch(Epoch + 2u);
            Assert.Throws<InvalidOperationException>(() => adapter.ResetEpoch(Epoch + 2u));
            stateVersion.ResetEpoch(Epoch + 2u);
            Assert.DoesNotThrow(() => adapter.ResetEpoch(Epoch + 2u));
        }

        [Test]
        public void PeriodicExecuted_UsesActiveIdentityWithoutAllocatingIt()
        {
            adapter = CreateAdapter(capacity: 2);
            GameplayEffectApplicationResult application = ApplyCueEffect(CreateCueEffect(
                "Cue.Authority.Periodic",
                EDurationPolicy.Infinite,
                period: 0.25f,
                executePeriodicEffectOnApplication: true));
            Assert.That(adapter.PrepareNext(out GASCueExecuted active),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(adapter.CommitPrepared(active.CueSequence), Is.True);
            Assert.That(identityMap.EffectCount, Is.Zero);

            Assert.That(
                identityMap.GetOrCreateEffectId(
                    application.ActiveEffect.ReconciliationId,
                    out GASNetworkEffectId effectId),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            abilitySystem.Tick(0f, isServer: true);

            Assert.That(adapter.PrepareNext(out GASCueExecuted periodic),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(periodic.Event, Is.EqualTo(GASCueEvent.Execute));
            Assert.That(periodic.SourceEffect, Is.EqualTo(effectId));
            Assert.That(identityMap.EffectCount, Is.EqualTo(1));
        }

        [Test]
        public void LocalPredictedCue_CarriesCommandCorrelation()
        {
            adapter = CreateAdapter(capacity: 1);
            GameplayAbilitySpec sourceSpec = abilitySystem.GrantAbility(
                new TestAbility("Cue.Authority.Predicted", EAbilityExecutionPolicy.LocalPredicted));
            GameplayEffectContext effectContext = abilitySystem.MakeEffectContext();
            effectContext.AddInstigator(abilitySystem, sourceSpec.GetPrimaryInstance());
            effectContext.PredictionKey = new GASPredictionKey(
                value: 37,
                owner: abilitySystem.CoreEntity,
                inputSequence: 23);
            GameplayEffectApplicationResult application = abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    CreateCueEffect("Cue.Authority.Predicted.Instant", EDurationPolicy.Instant),
                    abilitySystem,
                    effectContext));
            Assert.That(application.Succeeded, Is.True);

            Assert.That(adapter.PrepareNext(out GASCueExecuted cue),
                Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(cue.SourceCommandSequence, Is.EqualTo(23u));
            Assert.That(cue.Flags & GASCueFlags.Predicted, Is.EqualTo(GASCueFlags.Predicted));
            Assert.That(cue.Instigator, Is.EqualTo(Entity));
            Assert.That(cue.SourceEffect.IsValid, Is.False);
        }

        [Test]
        public void PreparedRetryPath_IsAllocationFreeAfterWarmup()
        {
            adapter = CreateAdapter(capacity: 1);
            Assert.That(ApplyCueEffect(CreateCueEffect("Cue.Authority.Allocation", EDurationPolicy.Instant)).Succeeded, Is.True);
            Assert.That(adapter.PrepareNext(out _), Is.EqualTo(GASNetworkAuthorityCuePrepareResult.Prepared));
            Assert.That(adapter.RejectPrepared(), Is.True);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                adapter.PrepareNext(out _);
                adapter.RejectPrepared();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Dispose_UnsubscribesBeforeAbilitySystemLifetimeEnds()
        {
            adapter = CreateAdapter(capacity: 1);
            adapter.Dispose();

            Assert.That(adapter.IsDisposed, Is.True);
            Assert.DoesNotThrow(() => adapter.Dispose());
            Assert.That(
                ApplyCueEffect(CreateCueEffect("Cue.Authority.AfterDispose", EDurationPolicy.Instant)).Succeeded,
                Is.True);
        }

        private GASNetworkAuthorityCueAdapter CreateAdapter(int capacity)
        {
            return new GASNetworkAuthorityCueAdapter(
                abilitySystem,
                entityResolver,
                identityMap,
                stateVersion,
                capacity);
        }

        private GASNetworkAuthorityStateAdapter CreateStateAdapter(GameplayEffect effect)
        {
            var builder = new GASNetworkContentCatalogBuilder();
            if (effect != null)
            {
                builder.Add(
                    GASNetworkContentKind.EffectDefinition,
                    "effect.authority-cue",
                    GASNetworkContentCatalogBuilder.ComputeRevisionHash("effect.authority-cue:1"),
                    effect);
            }

            var contentResolver = new GASNetworkRuntimeContentResolver(builder.Build());
            return new GASNetworkAuthorityStateAdapter(
                abilitySystem,
                entityResolver,
                entityResolver,
                contentResolver,
                identityMap,
                stateVersion,
                new GASNetworkRuntimeStateCapacity(
                    new GASNetworkStateCapacity(
                        abilities: 4,
                        attributes: 0,
                        effects: 4,
                        effectTags: 0,
                        effectMagnitudes: 0,
                        looseTags: 0),
                    maxSetByCallerTagsPerEffect: 0,
                    maxSetByCallerNamesPerEffect: 0,
                    maxDynamicGrantedTagsPerEffect: 0,
                    maxDynamicAssetTagsPerEffect: 0));
        }

        private GameplayEffectApplicationResult ApplyCueEffect(GameplayEffect effect)
        {
            return abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, abilitySystem));
        }

        private GameplayEffect CreateCueEffect(
            string name,
            EDurationPolicy durationPolicy,
            float period = 0f,
            bool executePeriodicEffectOnApplication = true)
        {
            var cues = new GameplayTagContainer();
            cues.AddTag(cueTag);
            return new GameplayEffect(
                name,
                durationPolicy,
                period: period,
                gameplayCues: cues,
                executePeriodicEffectOnApplication: executePeriodicEffectOnApplication);
        }

        private sealed class TestEntityResolver : IGASNetworkEntityResolver, IGASNetworkGrantResolver
        {
            private readonly AbilitySystemComponent abilitySystem;
            private readonly GASNetworkEntityId entity;
            private readonly GASAuthorityIdentityMap identityMap;

            public TestEntityResolver(
                AbilitySystemComponent abilitySystem,
                GASNetworkEntityId entity,
                GASAuthorityIdentityMap identityMap)
            {
                this.abilitySystem = abilitySystem;
                this.entity = entity;
                this.identityMap = identityMap;
            }

            public bool TryGetNetworkEntityId(
                AbilitySystemComponent candidate,
                out GASNetworkEntityId resolved)
            {
                resolved = ReferenceEquals(candidate, abilitySystem) ? entity : default;
                return resolved.IsValid;
            }

            public bool TryResolveAbilitySystem(
                GASNetworkEntityId candidate,
                out AbilitySystemComponent resolved)
            {
                resolved = candidate == entity ? abilitySystem : null;
                return resolved != null;
            }

            public bool TryGetNetworkGrantId(
                AbilitySystemComponent candidate,
                int abilitySpecHandle,
                out uint streamEpoch,
                out GASNetworkGrantId grant)
            {
                if (ReferenceEquals(candidate, abilitySystem) &&
                    identityMap.TryGetGrantId(abilitySpecHandle, out grant))
                {
                    streamEpoch = identityMap.StreamEpoch;
                    return true;
                }

                streamEpoch = 0u;
                grant = default;
                return false;
            }

            public bool TryResolveAbilitySpecHandle(
                GASNetworkEntityId candidate,
                uint streamEpoch,
                GASNetworkGrantId grant,
                out int abilitySpecHandle)
            {
                if (candidate == entity && streamEpoch == identityMap.StreamEpoch)
                {
                    return identityMap.TryGetAbilitySpecHandle(grant, out abilitySpecHandle);
                }

                abilitySpecHandle = 0;
                return false;
            }
        }

        private sealed class TestAbility : GameplayAbility
        {
            private readonly string configuredName;
            private readonly EAbilityExecutionPolicy configuredPolicy;

            public TestAbility(string name, EAbilityExecutionPolicy executionPolicy)
            {
                configuredName = name;
                configuredPolicy = executionPolicy;
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    executionPolicy,
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
                return new TestAbility(configuredName, configuredPolicy);
            }
        }
    }
}
