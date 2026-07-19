using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    public sealed class GASNetworkRuntimeBridgeTests
    {
        private static readonly GASNetworkEntityId Entity = new GASNetworkEntityId(41UL);
        private const uint Epoch = 7u;

        [Test]
        public void NetworkStateVersion_TracksExternalChangesWithoutMutatingAscAndEnforcesOwnerThread()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var stateVersion = new GASNetworkStateVersion(abilitySystem, Epoch);
                ulong localVersion = abilitySystem.StateVersion;
                Assert.That(stateVersion.CurrentVersion, Is.EqualTo(localVersion + 1UL));

                Assert.That(stateVersion.MarkExternalIdentityChanged(), Is.True);
                Assert.That(abilitySystem.StateVersion, Is.EqualTo(localVersion));
                Assert.That(stateVersion.CurrentVersion, Is.EqualTo(localVersion + 2UL));

                Exception offThreadFailure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        stateVersion.MarkExternalIdentityChanged();
                    }
                    catch (Exception exception)
                    {
                        offThreadFailure = exception;
                    }
                });
                worker.Start();
                worker.Join();
                Assert.That(offThreadFailure, Is.TypeOf<InvalidOperationException>());

                stateVersion.ResetEpoch(Epoch + 1u);
                Assert.That(stateVersion.StreamEpoch, Is.EqualTo(Epoch + 1u));
                Assert.That(stateVersion.IsExhausted, Is.False);
                Assert.That(stateVersion.CurrentVersion, Is.EqualTo(abilitySystem.StateVersion + 1UL));
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void AuthorityStateCapturePublishesOnlyNewCompleteVersions()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbility ability = CreateAbility(
                    "Bridge.Capture",
                    EAbilityExecutionPolicy.AuthorityOnly);
                GameplayAbilitySpec spec = abilitySystem.GrantAbility(ability);
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 4);
                var identityResolver = new TestIdentityResolver(abilitySystem, Entity, identities);
                var contentResolver = CreateContentResolver(ability, out GASNetworkContentId abilityId);
                var adapter = new GASNetworkAuthorityStateAdapter(
                    abilitySystem,
                    identityResolver,
                    identityResolver,
                    contentResolver,
                    identities,
                    new GASNetworkStateVersion(abilitySystem, Epoch),
                    SmallRuntimeCapacity());

                Assert.That(adapter.TryCapture(0u, out IGASNetworkStateView first), Is.True);
                Assert.That(first.IsComplete(), Is.True);
                Assert.That(first.AbilityCount, Is.EqualTo(1));
                Assert.That(first.GetAbility(0).Definition, Is.EqualTo(abilityId));
                Assert.That(first.StateVersion, Is.EqualTo(abilitySystem.StateVersion + 1UL));
                Assert.That(adapter.TryCapture(0u, out _), Is.False);

                Assert.That(abilitySystem.TrySetAbilityInputPressed(spec, true), Is.True);
                Assert.That(adapter.TryCapture(1u, out IGASNetworkStateView second), Is.True);
                Assert.That(second.LastProcessedCommandSequence, Is.EqualTo(1u));
                Assert.That(
                    second.GetAbility(0).Flags & GASAbilityStateFlags.InputPressed,
                    Is.EqualTo(GASAbilityStateFlags.InputPressed));
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void AuthorityCapture_NormalizesPeriodicBacklogWithoutMutatingAuthoritySchedule()
        {
            var limits = new GASRuntimeLimits(maxPeriodicEffectExecutionsPerTick: 1);
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var authority = new AbilitySystemComponent(
                context,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            try
            {
                var effect = new GameplayEffect(
                    "Bridge.PeriodicBacklog",
                    EDurationPolicy.Infinite,
                    period: 0.01f,
                    executePeriodicEffectOnApplication: false);
                GameplayEffectApplicationResult application = authority.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, authority));
                Assert.That(application.Succeeded, Is.True);

                authority.Tick(1f, isServer: true);
                long authorityBacklog = application.ActiveEffect.PeriodTimeRemainingRaw;
                Assert.That(authorityBacklog, Is.LessThan(0L));

                var builder = new GASNetworkContentCatalogBuilder();
                builder.Add(
                    GASNetworkContentKind.EffectDefinition,
                    "effect.periodic-backlog",
                    GASNetworkContentCatalogBuilder.ComputeRevisionHash("effect.periodic-backlog:1"),
                    effect);
                var contentResolver = new GASNetworkRuntimeContentResolver(builder.Build());
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 4);
                var identityResolver = new TestIdentityResolver(authority, Entity, identities);
                var adapter = new GASNetworkAuthorityStateAdapter(
                    authority,
                    identityResolver,
                    identityResolver,
                    contentResolver,
                    identities,
                    new GASNetworkStateVersion(authority, Epoch),
                    SmallRuntimeCapacity());

                Assert.That(adapter.TryCapture(0u, out IGASNetworkStateView captured), Is.True);
                var wire = (GASNetworkStateBuffer)captured;
                Assert.That(wire.EffectCount, Is.EqualTo(1));
                Assert.That(wire.Effects[0].PeriodRaw, Is.Zero);
                Assert.That(application.ActiveEffect.PeriodTimeRemainingRaw, Is.EqualTo(authorityBacklog));

                authority.Tick(0f, isServer: true);
                Assert.That(application.ActiveEffect.PeriodTimeRemainingRaw, Is.GreaterThan(authorityBacklog));
            }
            finally
            {
                authority.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void FullStateRoundTripAppliesEffectGrantAndExactLooseTagCount()
        {
            const string tagName = "Test.GAS.Networking.Bridge.RoundTrip";
            GameplayTagManager.RegisterDynamicTag(tagName, "Runtime bridge round-trip test tag.");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag looseTag = GameplayTagManager.RequestTag(tagName);
            Assert.That(looseTag.IsValid, Is.True);

            GameplayAbility ability = CreateAbility(
                "Bridge.RoundTrip",
                EAbilityExecutionPolicy.AuthorityOnly);
            var effect = new GameplayEffect(
                "Bridge.RoundTrip.Effect",
                EDurationPolicy.Infinite,
                grantedAbilities: new List<GameplayAbility> { ability });
            GASNetworkContentCatalog catalog = CreateCatalog(ability, effect, out _, out _);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();

            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayEffectApplicationResult application = authority.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, authority));
                Assert.That(application.Succeeded, Is.True);
                authority.AddLooseGameplayTag(looseTag);
                authority.AddLooseGameplayTag(looseTag);

                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 4);
                var authorityResolver = new TestIdentityResolver(
                    authority,
                    Entity,
                    authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);

                Assert.That(
                    authorityAdapter.TryCapture(0u, out IGASNetworkStateView captured),
                    Is.True);
                var wire = (GASNetworkStateBuffer)captured;
                Assert.That(wire.AbilityCount, Is.EqualTo(1));
                Assert.That(wire.EffectCount, Is.EqualTo(1));
                Assert.That(wire.Abilities[0].GrantingEffect.IsValid, Is.True);
                Assert.That(wire.Abilities[0].GrantingEffect, Is.EqualTo(wire.Effects[0].Effect));
                Assert.That(wire.LooseTagCount, Is.EqualTo(1));
                Assert.That(wire.LooseTags[0].Count, Is.EqualTo(2));

                var receiver = new GASNetworkStateReceiver(
                    Epoch,
                    Entity,
                    catalog,
                    runtimeCapacity.State);
                var header = new GASStateBatchChunk(
                    Epoch,
                    1u,
                    Entity,
                    GASStateBatchKind.Snapshot,
                    0UL,
                    wire.StateVersion,
                    wire.LastProcessedCommandSequence,
                    0,
                    1,
                    (ushort)wire.AbilityCount,
                    (ushort)wire.AttributeCount,
                    (ushort)wire.EffectCount,
                    (ushort)wire.EffectTagCount,
                    (ushort)wire.EffectMagnitudeCount,
                    (ushort)wire.LooseTagCount,
                    wire.StateChecksum);
                Assert.That(
                    receiver.ReceiveChunk(
                        in header,
                        wire.Abilities,
                        wire.Attributes,
                        wire.Effects,
                        wire.EffectTags,
                        wire.EffectMagnitudes,
                        wire.LooseTags),
                    Is.EqualTo(GASStateReceiveResult.Prepared));

                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica,
                    Entity,
                    Epoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    runtimeCapacity);
                Assert.That(
                    replicaAdapter.TryApplyPrepared(
                        receiver,
                        out GASStateAcknowledgement acknowledgement,
                        out GASStateDeltaRejectionReason rejectionReason),
                    Is.True,
                    rejectionReason.ToString());
                Assert.That(acknowledgement.IsValid, Is.True);
                Assert.That(acknowledgement.AppliedStateVersion, Is.EqualTo(wire.StateVersion));
                Assert.That(acknowledgement.StateChecksum, Is.EqualTo(wire.StateChecksum));

                var replicaState = new GASAbilitySystemFullStateBuffer();
                replica.CaptureFullStateNonAlloc(replicaState);
                Assert.That(replicaState.GrantedAbilityCount, Is.EqualTo(1));
                Assert.That(replicaState.ActiveEffectCount, Is.EqualTo(1));
                Assert.That(replicaState.GrantedAbilities[0].Level, Is.EqualTo(1));
                Assert.That(
                    replica.GetActivatableAbilities()[0].GrantingEffect,
                    Is.SameAs(replica.ActiveEffects[0]));
                Assert.That(replicaState.LooseTagCount, Is.EqualTo(1));
                Assert.That(replicaState.LooseTags[0].Tag, Is.EqualTo(looseTag));
                Assert.That(replicaState.LooseTags[0].ExplicitCount, Is.EqualTo(2));
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void ReplicaPreflightFailureRejectsPreparedStateWithoutMutatingAbilitySystem()
        {
            GameplayAbility ability = CreateAbility(
                "Bridge.Rejected",
                EAbilityExecutionPolicy.AuthorityOnly);
            GASNetworkContentCatalog catalog = CreateCatalog(ability, out GASNetworkContentId abilityId);
            GASNetworkStateCapacity wireCapacity = SmallWireCapacity();
            var wire = new GASNetworkStateBuffer(wireCapacity);
            wire.BeginWrite(Entity, 1UL, 0u);
            var abilityRecord = new GASAbilityStateRecord(
                GASStateRecordOperation.Upsert,
                new GASNetworkGrantId(1UL),
                abilityId,
                default,
                1,
                GASAbilityStateFlags.None);
            Assert.That(wire.TrySetAbility(in abilityRecord), Is.True);
            Assert.That(wire.TryCompleteWrite(), Is.True);

            var receiver = new GASNetworkStateReceiver(
                Epoch,
                Entity,
                catalog,
                wireCapacity);
            var header = new GASStateBatchChunk(
                Epoch,
                1u,
                Entity,
                GASStateBatchKind.Snapshot,
                0UL,
                wire.StateVersion,
                0u,
                0,
                1,
                (ushort)wire.AbilityCount,
                0,
                0,
                0,
                0,
                0,
                wire.StateChecksum);
            Assert.That(
                receiver.ReceiveChunk(
                    in header,
                    wire.Abilities,
                    wire.Attributes,
                    wire.Effects,
                    wire.EffectTags,
                    wire.EffectMagnitudes,
                    wire.LooseTags),
                Is.EqualTo(GASStateReceiveResult.Prepared));

            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var identityResolver = new TestIdentityResolver(abilitySystem, Entity, null);
                var adapter = new GASNetworkReplicaStateAdapter(
                    abilitySystem,
                    Entity,
                    Epoch,
                    identityResolver,
                    identityResolver,
                    new RejectingContentResolver(),
                    SmallRuntimeCapacity());

                Assert.That(
                    adapter.TryApplyPrepared(receiver, out _, out GASStateDeltaRejectionReason reason),
                    Is.False);
                Assert.That(reason, Is.EqualTo(GASStateDeltaRejectionReason.InvalidPayload));
                Assert.That(receiver.HasPreparedState, Is.False);
                Assert.That(abilitySystem.GetActivatableAbilities().Count, Is.Zero);
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void CrossEntityEffectSourceGrant_FailsWhileLiveMappingIsMissingAndDegradesAfterGrantRemoval()
        {
            const uint sourceEpoch = 17u;
            const uint targetEpoch = 29u;
            var targetEntity = new GASNetworkEntityId(42UL);
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbility sourceAbility = CreateAbility(
                    "Bridge.CrossSource",
                    EAbilityExecutionPolicy.AuthorityOnly);
                GameplayAbilitySpec sourceSpec = source.GrantAbility(sourceAbility);
                int sourceHandle = sourceSpec.Handle;
                var effect = new GameplayEffect(
                    "Bridge.CrossSource.Effect",
                    EDurationPolicy.Infinite);
                GameplayEffectContext effectContext = source.MakeEffectContext();
                effectContext.AddInstigator(source, sourceSpec.GetPrimaryInstance());
                GameplayEffectApplicationResult application = target.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, source, effectContext));
                Assert.That(application.Succeeded, Is.True);
                Assert.That(application.ActiveEffect.SourceAbilitySpecHandle, Is.EqualTo(sourceHandle));

                GASNetworkContentCatalog catalog = CreateCatalog(
                    sourceAbility,
                    effect,
                    out _,
                    out _);
                var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
                var sourceIdentities = new GASAuthorityIdentityMap(Entity, sourceEpoch, 4, 4);
                var targetIdentities = new GASAuthorityIdentityMap(targetEntity, targetEpoch, 4, 4);
                var identities = new CrossIdentityResolver(
                    source,
                    Entity,
                    sourceIdentities,
                    target,
                    targetEntity,
                    targetIdentities);
                var sourceAdapter = new GASNetworkAuthorityStateAdapter(
                    source,
                    identities,
                    identities,
                    contentResolver,
                    sourceIdentities,
                    new GASNetworkStateVersion(source, sourceEpoch),
                    SmallRuntimeCapacity());
                var targetAdapter = new GASNetworkAuthorityStateAdapter(
                    target,
                    identities,
                    identities,
                    contentResolver,
                    targetIdentities,
                    new GASNetworkStateVersion(target, targetEpoch),
                    SmallRuntimeCapacity());

                Assert.That(targetAdapter.TryCapture(0u, out _), Is.False,
                    "A live source grant without an authority-issued mapping must fail closed.");
                Assert.That(sourceAdapter.TryCapture(0u, out _), Is.True);
                Assert.That(targetAdapter.TryCapture(0u, out IGASNetworkStateView firstCapture), Is.True);
                var firstWire = (GASNetworkStateBuffer)firstCapture;
                Assert.That(firstWire.Effects[0].SourceEntity, Is.EqualTo(Entity));
                Assert.That(firstWire.Effects[0].SourceStreamEpoch, Is.EqualTo(sourceEpoch));
                Assert.That(firstWire.Effects[0].SourceGrant.IsValid, Is.True);

                source.ClearAbility(sourceSpec);
                Assert.That(sourceAdapter.TryCapture(0u, out _), Is.True);
                Assert.That(sourceIdentities.TryGetGrantId(sourceHandle, out _), Is.False);

                target.Tick(0f, isServer: true);
                Assert.That(targetAdapter.TryCapture(0u, out IGASNetworkStateView secondCapture), Is.True);
                var secondWire = (GASNetworkStateBuffer)secondCapture;
                Assert.That(secondWire.Effects[0].SourceEntity, Is.EqualTo(Entity));
                Assert.That(secondWire.Effects[0].SourceStreamEpoch, Is.Zero);
                Assert.That(secondWire.Effects[0].SourceGrant.IsValid, Is.False);
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void CrossEntityEffectSourceGrant_CapturesAndAppliesAcrossIndependentEpochRotation()
        {
            const uint sourceEpoch = 17u;
            const uint replacementSourceEpoch = 18u;
            const uint targetEpoch = 29u;
            var targetEntity = new GASNetworkEntityId(42UL);
            GameplayAbility sourceAbility = CreateAbility(
                "Bridge.CrossEpochSource",
                EAbilityExecutionPolicy.AuthorityOnly);
            var effect = new GameplayEffect(
                "Bridge.CrossEpochSource.Effect",
                EDurationPolicy.Infinite);
            GASNetworkContentCatalog catalog = CreateCatalog(
                sourceAbility,
                effect,
                out _,
                out _);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity capacity = SmallRuntimeCapacity();

            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authoritySource = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var authorityTarget = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replicaSource = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replicaTarget = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec sourceSpec = authoritySource.GrantAbility(sourceAbility);
                GameplayEffectContext effectContext = authoritySource.MakeEffectContext();
                effectContext.AddInstigator(authoritySource, sourceSpec.GetPrimaryInstance());
                GameplayEffectApplicationResult application = authorityTarget.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, authoritySource, effectContext));
                Assert.That(application.Succeeded, Is.True);

                var sourceIdentities = new GASAuthorityIdentityMap(Entity, sourceEpoch, 4, 4);
                var targetIdentities = new GASAuthorityIdentityMap(targetEntity, targetEpoch, 4, 4);
                var authorityResolver = new CrossIdentityResolver(
                    authoritySource,
                    Entity,
                    sourceIdentities,
                    authorityTarget,
                    targetEntity,
                    targetIdentities);
                var sourceStateVersion = new GASNetworkStateVersion(
                    authoritySource,
                    sourceEpoch);
                var targetStateVersion = new GASNetworkStateVersion(
                    authorityTarget,
                    targetEpoch);
                var sourceAuthorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authoritySource,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    sourceIdentities,
                    sourceStateVersion,
                    capacity);
                var targetAuthorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authorityTarget,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    targetIdentities,
                    targetStateVersion,
                    capacity);

                Assert.That(sourceAuthorityAdapter.TryCapture(0u, out IGASNetworkStateView sourceView), Is.True);
                Assert.That(targetAuthorityAdapter.TryCapture(0u, out IGASNetworkStateView targetView), Is.True);
                var sourceState = (GASNetworkStateBuffer)sourceView;
                var targetState = (GASNetworkStateBuffer)targetView;
                Assert.That(targetState.Effects[0].SourceStreamEpoch, Is.EqualTo(sourceEpoch));

                var replicaResolver = new CrossReplicaResolver(
                    replicaSource,
                    Entity,
                    replicaTarget,
                    targetEntity);
                var sourceReplicaAdapter = new GASNetworkReplicaStateAdapter(
                    replicaSource,
                    Entity,
                    sourceEpoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    capacity);
                var targetReplicaAdapter = new GASNetworkReplicaStateAdapter(
                    replicaTarget,
                    targetEntity,
                    targetEpoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    capacity);
                replicaResolver.SetGrantResolvers(sourceReplicaAdapter, targetReplicaAdapter);

                var sourceReceiver = new GASNetworkStateReceiver(
                    sourceEpoch,
                    Entity,
                    catalog,
                    capacity.State);
                var targetReceiver = new GASNetworkStateReceiver(
                    targetEpoch,
                    targetEntity,
                    catalog,
                    capacity.State);
                PrepareSnapshot(sourceReceiver, sourceState, batchSequence: 1u);
                Assert.That(sourceReplicaAdapter.TryApplyPrepared(sourceReceiver, out _, out _), Is.True);
                PrepareSnapshot(targetReceiver, targetState, batchSequence: 1u);
                Assert.That(targetReplicaAdapter.TryApplyPrepared(targetReceiver, out _, out _), Is.True);
                Assert.That(replicaTarget.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(replicaTarget.ActiveEffects[0].Spec.Source, Is.SameAs(replicaSource));

                sourceIdentities.ResetEpoch(replacementSourceEpoch);
                sourceStateVersion.ResetEpoch(replacementSourceEpoch);
                Assert.That(
                    sourceAuthorityAdapter.TryCapture(0u, out IGASNetworkStateView replacementSourceView),
                    Is.True);
                var replacementSourceState = (GASNetworkStateBuffer)replacementSourceView;
                sourceReceiver.ResetStream(replacementSourceEpoch);
                sourceReplicaAdapter.ResetEpoch(replacementSourceEpoch);
                PrepareSnapshot(sourceReceiver, replacementSourceState, batchSequence: 1u);
                Assert.That(sourceReplicaAdapter.TryApplyPrepared(sourceReceiver, out _, out _), Is.True);

                ulong unchangedTargetStateVersion = authorityTarget.StateVersion;
                Assert.That(targetStateVersion.MarkExternalIdentityChanged(), Is.True);
                Assert.That(authorityTarget.StateVersion, Is.EqualTo(unchangedTargetStateVersion));
                Assert.That(
                    targetAuthorityAdapter.TryCapture(0u, out IGASNetworkStateView replacementTargetView),
                    Is.True);
                var replacementTargetState = (GASNetworkStateBuffer)replacementTargetView;
                Assert.That(replacementTargetState.StateVersion, Is.GreaterThan(targetState.StateVersion));
                Assert.That(authorityTarget.StateVersion, Is.EqualTo(unchangedTargetStateVersion));
                Assert.That(
                    replacementTargetState.Effects[0].SourceStreamEpoch,
                    Is.EqualTo(replacementSourceEpoch));
                PrepareSnapshot(targetReceiver, replacementTargetState, batchSequence: 2u);
                bool targetApplied = targetReplicaAdapter.TryApplyPrepared(
                    targetReceiver,
                    out _,
                    out GASStateDeltaRejectionReason targetRejection);
                Assert.That(targetApplied, Is.True, targetRejection.ToString());

                GASNetworkGrantId replacementGrant = replacementSourceState.Abilities[0].Grant;
                Assert.That(
                    sourceReplicaAdapter.TryResolveAbilitySpecHandle(
                        Entity,
                        replacementSourceEpoch,
                        replacementGrant,
                        out int sourceHandle),
                    Is.True);
                Assert.That(
                    replicaTarget.ActiveEffects[0].SourceAbilitySpecHandle,
                    Is.EqualTo(sourceHandle));

                authoritySource.ClearAbility(sourceSpec);
                Assert.That(
                    sourceAuthorityAdapter.TryCapture(
                        0u,
                        out IGASNetworkStateView sourceWithoutGrantView),
                    Is.True);
                var sourceWithoutGrant = (GASNetworkStateBuffer)sourceWithoutGrantView;
                Assert.That(sourceWithoutGrant.AbilityCount, Is.Zero);
                PrepareSnapshot(sourceReceiver, sourceWithoutGrant, batchSequence: 2u);
                Assert.That(sourceReplicaAdapter.TryApplyPrepared(sourceReceiver, out _, out _), Is.True);

                Assert.That(targetStateVersion.MarkExternalIdentityChanged(), Is.True);
                Assert.That(
                    targetAuthorityAdapter.TryCapture(
                        0u,
                        out IGASNetworkStateView targetWithoutGrantView),
                    Is.True);
                var targetWithoutGrant = (GASNetworkStateBuffer)targetWithoutGrantView;
                Assert.That(targetWithoutGrant.Effects[0].SourceStreamEpoch, Is.Zero);
                Assert.That(targetWithoutGrant.Effects[0].SourceGrant.IsValid, Is.False);
                PrepareSnapshot(targetReceiver, targetWithoutGrant, batchSequence: 3u);
                bool clearedSourceApplied = targetReplicaAdapter.TryApplyPrepared(
                    targetReceiver,
                    out _,
                    out GASStateDeltaRejectionReason clearedSourceRejection);
                Assert.That(
                    clearedSourceApplied,
                    Is.True,
                    clearedSourceRejection.ToString());
                Assert.That(replicaTarget.ActiveEffects[0].SourceAbilitySpecHandle, Is.Zero);
            }
            finally
            {
                replicaTarget.Dispose();
                replicaSource.Dispose();
                authorityTarget.Dispose();
                authoritySource.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void AuthorityCommandProcessorCachesTerminalResultAndRejectsConflictingReplay()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec spec = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Command",
                    EAbilityExecutionPolicy.LocalPredicted));
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 4);
                Assert.That(
                    identities.GetOrCreateGrantId(spec.Handle, out GASNetworkGrantId grant),
                    Is.EqualTo(GASAuthorityIdentityMapResult.Created));
                var identityResolver = new TestIdentityResolver(abilitySystem, Entity, identities);
                var processor = new GASNetworkAuthorityCommandProcessor(
                    abilitySystem,
                    identityResolver,
                    identities,
                    new GASNetworkStateVersion(abilitySystem, Epoch));
                var command = new GASAbilityCommand(
                    Epoch,
                    1u,
                    Entity,
                    grant,
                    GASAbilityCommandKind.Activate);

                Assert.That(
                    processor.Process(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, out GASCommandResult first),
                    Is.EqualTo(GASCommandReplayDecision.Execute));
                Assert.That(first.Status, Is.EqualTo(GASCommandStatus.Accepted));
                Assert.That(
                    processor.Process(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, out GASCommandResult duplicate),
                    Is.EqualTo(GASCommandReplayDecision.Duplicate));
                Assert.That(duplicate.CommandSequence, Is.EqualTo(first.CommandSequence));

                var conflicting = new GASAbilityCommand(
                    Epoch,
                    1u,
                    Entity,
                    grant,
                    GASAbilityCommandKind.Cancel);
                Assert.That(
                    processor.Process(in conflicting, ReadOnlySpan<GASNetworkEntityId>.Empty, out _),
                    Is.EqualTo(GASCommandReplayDecision.ConflictingReplay));

                processor.Process(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, out _);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    processor.Process(in command, ReadOnlySpan<GASNetworkEntityId>.Empty, out _);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);

                Exception offThreadFailure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        processor.Process(
                            in command,
                            ReadOnlySpan<GASNetworkEntityId>.Empty,
                            out _);
                    }
                    catch (Exception exception)
                    {
                        offThreadFailure = exception;
                    }
                });
                worker.Start();
                worker.Join();
                Assert.That(offThreadFailure, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void AuthorityCommandProcessor_PartialCommitFailurePublishesCurrentVersionAndFailsClosed()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec spec = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Command.PartialFailure",
                    EAbilityExecutionPolicy.AuthorityOnly));
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                Assert.That(
                    identities.GetOrCreateGrantId(spec.Handle, out GASNetworkGrantId grant),
                    Is.EqualTo(GASAuthorityIdentityMapResult.Created));
                var identityResolver = new TestIdentityResolver(abilitySystem, Entity, identities);
                var targetHandler = new MutatingThrowOnceTargetHandler();
                var stateVersion = new GASNetworkStateVersion(abilitySystem, Epoch);
                var processor = new GASNetworkAuthorityCommandProcessor(
                    abilitySystem,
                    identityResolver,
                    identities,
                    stateVersion,
                    targetHandler);
                var targets = new[] { new GASNetworkEntityId(99UL) };
                var command = new GASAbilityCommand(
                    Epoch,
                    1u,
                    Entity,
                    grant,
                    GASAbilityCommandKind.ConfirmTarget,
                    GASTargetDataKind.ActorList,
                    1);

                Assert.That(
                    processor.Process(in command, targets, out GASCommandResult failed),
                    Is.EqualTo(GASCommandReplayDecision.Execute));
                Assert.That(failed.Status, Is.EqualTo(GASCommandStatus.AuthorityUnavailable));
                Assert.That(spec.IsInputPressed, Is.True);
                Assert.That(failed.AuthoritativeStateVersion, Is.EqualTo(abilitySystem.StateVersion + 1UL));
                Assert.That(processor.RequiresStreamReset, Is.True);
                Assert.That(processor.HighestCompletedSequence, Is.EqualTo(1u));
                Assert.That(targetHandler.InvocationCount, Is.EqualTo(1));

                Assert.That(
                    processor.Process(in command, targets, out GASCommandResult duplicate),
                    Is.EqualTo(GASCommandReplayDecision.Duplicate));
                Assert.That(duplicate.AuthoritativeStateVersion, Is.EqualTo(failed.AuthoritativeStateVersion));
                Assert.That(duplicate.Status, Is.EqualTo(failed.Status));

                var blocked = new GASAbilityCommand(
                    Epoch,
                    2u,
                    Entity,
                    grant,
                    GASAbilityCommandKind.ConfirmTarget,
                    GASTargetDataKind.ActorList,
                    1);
                Assert.That(
                    processor.Process(in blocked, targets, out GASCommandResult blockedResult),
                    Is.EqualTo(GASCommandReplayDecision.Execute));
                Assert.That(blockedResult.Status, Is.EqualTo(GASCommandStatus.AuthorityUnavailable));
                Assert.That(targetHandler.InvocationCount, Is.EqualTo(1));

                const uint replacementEpoch = Epoch + 1u;
                identities.ResetEpoch(replacementEpoch);
                stateVersion.ResetEpoch(replacementEpoch);
                Assert.That(
                    identities.GetOrCreateGrantId(spec.Handle, out GASNetworkGrantId replacementGrant),
                    Is.EqualTo(GASAuthorityIdentityMapResult.Created));
                processor.ResetEpoch(replacementEpoch);
                Assert.That(processor.RequiresStreamReset, Is.False);

                var recovered = new GASAbilityCommand(
                    replacementEpoch,
                    1u,
                    Entity,
                    replacementGrant,
                    GASAbilityCommandKind.ConfirmTarget,
                    GASTargetDataKind.ActorList,
                    1);
                Assert.That(
                    processor.Process(in recovered, targets, out GASCommandResult recoveredResult),
                    Is.EqualTo(GASCommandReplayDecision.Execute));
                Assert.That(recoveredResult.Status, Is.EqualTo(GASCommandStatus.Accepted));
                Assert.That(targetHandler.InvocationCount, Is.EqualTo(2));
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void PredictionControllerCommitsAcceptedAndRollsBackFailurePaths()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec spec = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Prediction",
                    EAbilityExecutionPolicy.LocalPredicted));
                GameplayAbilitySpec otherSpec = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Prediction.Other",
                    EAbilityExecutionPolicy.LocalPredicted));
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                Assert.That(
                    identities.GetOrCreateGrantId(spec.Handle, out GASNetworkGrantId grant),
                    Is.EqualTo(GASAuthorityIdentityMapResult.Created));
                var resolver = new TestIdentityResolver(abilitySystem, Entity, identities);
                using (var controller = new GASNetworkPredictionController(
                           abilitySystem,
                           Entity,
                           Epoch,
                           resolver,
                           capacity: 4))
                {
                    var rejectedCommand = new GASAbilityCommand(
                        Epoch,
                        1u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in rejectedCommand, otherSpec, out _),
                        Is.False);
                    Assert.That(otherSpec.IsActive, Is.False);
                    Assert.That(
                        controller.TryBeginActivation(in rejectedCommand, spec, out GASPredictionKey rejectedKey),
                        Is.True);
                    Assert.That(abilitySystem.HasOpenPredictionWindow(rejectedKey), Is.True);
                    var rejectedResult = new GASCommandResult(
                        Epoch,
                        1u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate,
                        GASCommandStatus.Rejected,
                        1UL);
                    Assert.That(controller.HandleResult(in rejectedResult), Is.True);
                    Assert.That(spec.IsActive, Is.False);

                    var failedSendCommand = new GASAbilityCommand(
                        Epoch,
                        2u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in failedSendCommand, spec, out _),
                        Is.True);
                    Assert.That(controller.HandleSendFailure(2u), Is.True);
                    Assert.That(spec.IsActive, Is.False);

                    var acceptedCommand = new GASAbilityCommand(
                        Epoch,
                        3u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in acceptedCommand, spec, out GASPredictionKey acceptedKey),
                        Is.True);
                    var acceptedResult = new GASCommandResult(
                        Epoch,
                        3u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate,
                        GASCommandStatus.Accepted,
                        2UL);
                    Assert.That(controller.HandleResult(in acceptedResult), Is.True);
                    Assert.That(abilitySystem.HasOpenPredictionWindow(acceptedKey), Is.False);
                    Assert.That(spec.IsActive, Is.True);
                }
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void PredictionController_RejectsRingCollisionAndCapacityOverflow()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec first = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Prediction.Ring.First",
                    EAbilityExecutionPolicy.LocalPredicted));
                GameplayAbilitySpec second = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Prediction.Ring.Second",
                    EAbilityExecutionPolicy.LocalPredicted));
                GameplayAbilitySpec third = abilitySystem.GrantAbility(CreateAbility(
                    "Bridge.Prediction.Ring.Third",
                    EAbilityExecutionPolicy.LocalPredicted));
                var identities = new GASAuthorityIdentityMap(Entity, Epoch, 3, 0);
                identities.GetOrCreateGrantId(first.Handle, out GASNetworkGrantId firstGrant);
                identities.GetOrCreateGrantId(second.Handle, out GASNetworkGrantId secondGrant);
                identities.GetOrCreateGrantId(third.Handle, out GASNetworkGrantId thirdGrant);
                var resolver = new TestIdentityResolver(abilitySystem, Entity, identities);

                using (var controller = new GASNetworkPredictionController(
                           abilitySystem,
                           Entity,
                           Epoch,
                           resolver,
                           capacity: 2))
                {
                    var firstCommand = new GASAbilityCommand(
                        Epoch, 1u, Entity, firstGrant, GASAbilityCommandKind.Activate);
                    var collision = new GASAbilityCommand(
                        Epoch, 3u, Entity, thirdGrant, GASAbilityCommandKind.Activate);
                    var secondCommand = new GASAbilityCommand(
                        Epoch, 2u, Entity, secondGrant, GASAbilityCommandKind.Activate);
                    var overflow = new GASAbilityCommand(
                        Epoch, 4u, Entity, thirdGrant, GASAbilityCommandKind.Activate);

                    Assert.That(controller.TryBeginActivation(in firstCommand, first, out _), Is.True);
                    Assert.That(controller.TryBeginActivation(in collision, third, out _), Is.False);
                    Assert.That(controller.TryBeginActivation(in secondCommand, second, out _), Is.True);
                    Assert.That(controller.TryBeginActivation(in overflow, third, out _), Is.False);
                    Assert.That(controller.Count, Is.EqualTo(2));
                    Assert.That(controller.HandleSendFailure(2u), Is.True);
                    Assert.That(controller.HandleSendFailure(1u), Is.True);
                    Assert.That(controller.Count, Is.Zero);
                }
            }
            finally
            {
                abilitySystem.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void PredictionController_ResetEpochRollsBackAndClearsSnapshotWatermark()
        {
            GameplayAbility ability = CreateAbility(
                "Bridge.Prediction.Epoch",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateCatalog(ability, out GASNetworkContentId abilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(authorityContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec authoritySpec = authority.GrantAbility(ability);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 2, 0);
                var authorityResolver = new TestIdentityResolver(authority, Entity, authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority, authorityResolver, authorityResolver, contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(Epoch, Entity, catalog, runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica, Entity, Epoch, replicaResolver, replicaResolver,
                    contentResolver, runtimeCapacity);
                var controllerResolver = new TestIdentityResolver(replica, Entity, authorityIdentities);

                using (var controller = new GASNetworkPredictionController(
                           replica, Entity, Epoch, controllerResolver, capacity: 2))
                {
                    Assert.That(authorityAdapter.TryCapture(3u, out IGASNetworkStateView initialView), Is.True);
                    var initial = (GASNetworkStateBuffer)initialView;
                    PrepareSnapshot(receiver, initial, batchSequence: 1u);
                    Assert.That(replicaAdapter.TryApplyPrepared(receiver, controller, out _, out _), Is.True);
                    GASNetworkGrantId grant = FindGrant(initial, abilityId);
                    GameplayAbilitySpec replicaSpec = ResolveSpec(replica, replicaAdapter, grant);

                    var coveredSequence = new GASAbilityCommand(
                        Epoch, 1u, Entity, grant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in coveredSequence, replicaSpec, out _),
                        Is.False);

                    Assert.That(authority.TrySetAbilityInputPressed(authoritySpec, true), Is.True);
                    Assert.That(authorityAdapter.TryCapture(2u, out IGASNetworkStateView regressedView), Is.True);
                    var regressionReceiver = new GASNetworkStateReceiver(
                        Epoch, Entity, catalog, runtimeCapacity.State);
                    PrepareSnapshot(
                        regressionReceiver,
                        (GASNetworkStateBuffer)regressedView,
                        batchSequence: 1u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(
                            regressionReceiver,
                            controller,
                            out _,
                            out GASStateDeltaRejectionReason regressionReason),
                        Is.False);
                    Assert.That(regressionReason, Is.EqualTo(GASStateDeltaRejectionReason.ApplicationFailed));

                    var pending = new GASAbilityCommand(
                        Epoch, 4u, Entity, grant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in pending, replicaSpec, out GASPredictionKey oldKey),
                        Is.True);
                    controller.ResetEpoch(2u);
                    Assert.That(controller.Count, Is.Zero);
                    Assert.That(replica.HasOpenPredictionWindow(oldKey), Is.False);
                    Assert.That(replicaSpec.IsActive, Is.False);

                    authorityIdentities.ResetEpoch(2u);
                    Assert.That(
                        authorityIdentities.GetOrCreateGrantId(
                            replicaSpec.Handle,
                            out GASNetworkGrantId newGrant),
                        Is.EqualTo(GASAuthorityIdentityMapResult.Created));
                    var newEpochCommand = new GASAbilityCommand(
                        2u, 1u, Entity, newGrant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in newEpochCommand, replicaSpec, out _),
                        Is.True);
                    Assert.That(controller.HandleSendFailure(1u), Is.True);
                }
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void ReplicaSnapshotWithoutControllerRejectsOpenPredictionWithoutStaleEntry()
        {
            GameplayAbility ability = CreateAbility(
                "Bridge.Prediction.Uncoordinated",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateCatalog(ability, out GASNetworkContentId abilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(authorityContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec authoritySpec = authority.GrantAbility(ability);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 2, 0);
                var authorityResolver = new TestIdentityResolver(authority, Entity, authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority, authorityResolver, authorityResolver, contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(Epoch, Entity, catalog, runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica, Entity, Epoch, replicaResolver, replicaResolver,
                    contentResolver, runtimeCapacity);

                Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView initialView), Is.True);
                var initial = (GASNetworkStateBuffer)initialView;
                PrepareSnapshot(receiver, initial, batchSequence: 1u);
                Assert.That(replicaAdapter.TryApplyPrepared(receiver, out _, out _), Is.True);
                GASNetworkGrantId grant = FindGrant(initial, abilityId);
                GameplayAbilitySpec replicaSpec = ResolveSpec(replica, replicaAdapter, grant);

                using (var controller = new GASNetworkPredictionController(
                           replica, Entity, Epoch, replicaAdapter, capacity: 2))
                {
                    var command = new GASAbilityCommand(
                        Epoch, 1u, Entity, grant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in command, replicaSpec, out GASPredictionKey key),
                        Is.True);

                    Assert.That(authority.TrySetAbilityInputPressed(authoritySpec, true), Is.True);
                    Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView updatedView), Is.True);
                    PrepareSnapshot(receiver, (GASNetworkStateBuffer)updatedView, batchSequence: 2u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(
                            receiver,
                            out _,
                            out GASStateDeltaRejectionReason rejectionReason),
                        Is.False);
                    Assert.That(rejectionReason, Is.EqualTo(GASStateDeltaRejectionReason.ApplicationFailed));
                    Assert.That(receiver.HasPreparedState, Is.False);
                    Assert.That(controller.Count, Is.EqualTo(1));
                    Assert.That(replica.HasOpenPredictionWindow(key), Is.True);
                    Assert.That(replica.StateDeltaResyncRequired, Is.False);
                    Assert.That(controller.HandleSendFailure(1u), Is.True);
                }
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void SnapshotReconciliation_PartialCommitFailureRequiresResync()
        {
            GameplayAbility firstAbility = CreateAbility(
                "Bridge.Prediction.Partial.First",
                EAbilityExecutionPolicy.LocalPredicted);
            GameplayAbility secondAbility = CreateAbility(
                "Bridge.Prediction.Partial.Second",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateAbilityCatalog(
                firstAbility,
                secondAbility,
                out GASNetworkContentId firstAbilityId,
                out GASNetworkContentId secondAbilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(authorityContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            Action<GASPredictionKey, GASPredictionWindowStatus> observer = null;
            try
            {
                GameplayAbilitySpec authorityFirst = authority.GrantAbility(firstAbility);
                authority.GrantAbility(secondAbility);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                var authorityResolver = new TestIdentityResolver(authority, Entity, authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority, authorityResolver, authorityResolver, contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(Epoch, Entity, catalog, runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica, Entity, Epoch, replicaResolver, replicaResolver,
                    contentResolver, runtimeCapacity);

                Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView initialView), Is.True);
                var initial = (GASNetworkStateBuffer)initialView;
                PrepareSnapshot(receiver, initial, batchSequence: 1u);
                Assert.That(replicaAdapter.TryApplyPrepared(receiver, out _, out _), Is.True);
                GASNetworkGrantId firstGrant = FindGrant(initial, firstAbilityId);
                GASNetworkGrantId secondGrant = FindGrant(initial, secondAbilityId);
                GameplayAbilitySpec replicaFirst = ResolveSpec(replica, replicaAdapter, firstGrant);
                GameplayAbilitySpec replicaSecond = ResolveSpec(replica, replicaAdapter, secondGrant);

                using (var controller = new GASNetworkPredictionController(
                           replica, Entity, Epoch, replicaAdapter, capacity: 4))
                {
                    var firstCommand = new GASAbilityCommand(
                        Epoch, 1u, Entity, firstGrant, GASAbilityCommandKind.Activate);
                    var secondCommand = new GASAbilityCommand(
                        Epoch, 2u, Entity, secondGrant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in firstCommand, replicaFirst, out GASPredictionKey firstKey),
                        Is.True);
                    Assert.That(
                        controller.TryBeginActivation(in secondCommand, replicaSecond, out GASPredictionKey secondKey),
                        Is.True);

                    observer = (closedKey, status) =>
                    {
                        if (closedKey == firstKey && status == GASPredictionWindowStatus.Committed)
                            replica.CommitPredictionWindow(secondKey);
                    };
                    replica.OnPredictionWindowClosed += observer;

                    Assert.That(authority.TryActivateAbility(authorityFirst), Is.True);
                    Assert.That(authorityAdapter.TryCapture(2u, out IGASNetworkStateView updatedView), Is.True);
                    PrepareSnapshot(receiver, (GASNetworkStateBuffer)updatedView, batchSequence: 2u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(receiver, controller, out _, out _),
                        Is.False);
                    Assert.That(receiver.HasPreparedState, Is.False);
                    Assert.That(controller.Count, Is.Zero);
                    Assert.That(replica.OpenPredictionWindowCount, Is.Zero);
                    Assert.That(replica.StateDeltaResyncRequired, Is.True);
                }
            }
            finally
            {
                if (observer != null)
                    replica.OnPredictionWindowClosed -= observer;
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void SnapshotReconciliation_CommitsCoveredActivationAndReopensNewerPrediction()
        {
            GameplayAbility firstAbility = CreateAbility(
                "Bridge.Prediction.Snapshot.First",
                EAbilityExecutionPolicy.LocalPredicted);
            GameplayAbility secondAbility = CreateAbility(
                "Bridge.Prediction.Snapshot.Second",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateAbilityCatalog(
                firstAbility,
                secondAbility,
                out GASNetworkContentId firstAbilityId,
                out GASNetworkContentId secondAbilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec authorityFirst = authority.GrantAbility(firstAbility);
                authority.GrantAbility(secondAbility);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                var authorityResolver = new TestIdentityResolver(
                    authority,
                    Entity,
                    authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(
                    Epoch,
                    Entity,
                    catalog,
                    runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica,
                    Entity,
                    Epoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    runtimeCapacity);

                Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView initialView), Is.True);
                var initial = (GASNetworkStateBuffer)initialView;
                PrepareSnapshot(receiver, initial, batchSequence: 1u);
                Assert.That(
                    replicaAdapter.TryApplyPrepared(receiver, out _, out GASStateDeltaRejectionReason initialReason),
                    Is.True,
                    initialReason.ToString());

                GASNetworkGrantId firstGrant = FindGrant(initial, firstAbilityId);
                GASNetworkGrantId secondGrant = FindGrant(initial, secondAbilityId);
                GameplayAbilitySpec replicaFirst = ResolveSpec(replica, replicaAdapter, firstGrant);
                GameplayAbilitySpec replicaSecond = ResolveSpec(replica, replicaAdapter, secondGrant);

                using (var controller = new GASNetworkPredictionController(
                           replica,
                           Entity,
                           Epoch,
                           replicaAdapter,
                           capacity: 4))
                {
                    var firstCommand = new GASAbilityCommand(
                        Epoch,
                        1u,
                        Entity,
                        firstGrant,
                        GASAbilityCommandKind.Activate);
                    var secondCommand = new GASAbilityCommand(
                        Epoch,
                        2u,
                        Entity,
                        secondGrant,
                        GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(in firstCommand, replicaFirst, out GASPredictionKey firstKey),
                        Is.True);
                    Assert.That(
                        controller.TryBeginActivation(in secondCommand, replicaSecond, out GASPredictionKey secondKey),
                        Is.True);
                    Assert.That(replica.HasOpenPredictionWindow(firstKey), Is.True);
                    Assert.That(replica.HasOpenPredictionWindow(secondKey), Is.True);

                    Assert.That(authority.TryActivateAbility(authorityFirst), Is.True);
                    Assert.That(authorityAdapter.TryCapture(1u, out IGASNetworkStateView updatedView), Is.True);
                    var updated = (GASNetworkStateBuffer)updatedView;
                    PrepareSnapshot(receiver, updated, batchSequence: 2u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(
                            receiver,
                            controller,
                            out _,
                            out GASStateDeltaRejectionReason reconciliationReason),
                        Is.True,
                        reconciliationReason.ToString());

                    Assert.That(controller.Count, Is.EqualTo(1));
                    Assert.That(replica.HasOpenPredictionWindow(firstKey), Is.False);
                    Assert.That(replicaFirst.IsActive, Is.True);
                    Assert.That(replicaFirst.IsLocallyExecuting, Is.True);
                    Assert.That(replicaSecond.IsActive, Is.True);
                    Assert.That(replicaSecond.IsLocallyExecuting, Is.True);

                    var coveredResult = new GASCommandResult(
                        Epoch,
                        1u,
                        Entity,
                        firstGrant,
                        GASAbilityCommandKind.Activate,
                        GASCommandStatus.Accepted,
                        updated.StateVersion);
                    Assert.That(controller.HandleResult(in coveredResult), Is.True);
                    Assert.That(controller.Count, Is.EqualTo(1));

                    var pendingResult = new GASCommandResult(
                        Epoch,
                        2u,
                        Entity,
                        secondGrant,
                        GASAbilityCommandKind.Activate,
                        GASCommandStatus.Rejected,
                        updated.StateVersion);
                    Assert.That(controller.HandleResult(in pendingResult), Is.True);
                    Assert.That(controller.Count, Is.Zero);
                    Assert.That(replicaSecond.IsActive, Is.False);
                }
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void SnapshotReconciliation_CoveredInactiveStateCancelsCommittedPrediction()
        {
            GameplayAbility ability = CreateAbility(
                "Bridge.Prediction.Snapshot.Rejected",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateCatalog(ability, out GASNetworkContentId abilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                GameplayAbilitySpec authoritySpec = authority.GrantAbility(ability);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                var authorityResolver = new TestIdentityResolver(authority, Entity, authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(
                    Epoch,
                    Entity,
                    catalog,
                    runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica,
                    Entity,
                    Epoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    runtimeCapacity);

                Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView initialView), Is.True);
                var initial = (GASNetworkStateBuffer)initialView;
                PrepareSnapshot(receiver, initial, batchSequence: 1u);
                Assert.That(replicaAdapter.TryApplyPrepared(receiver, out _, out _), Is.True);
                GASNetworkGrantId grant = FindGrant(initial, abilityId);
                GameplayAbilitySpec replicaSpec = ResolveSpec(replica, replicaAdapter, grant);

                using (var controller = new GASNetworkPredictionController(
                           replica,
                           Entity,
                           Epoch,
                           replicaAdapter,
                           capacity: 4))
                {
                    var command = new GASAbilityCommand(
                        Epoch,
                        1u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate);
                    Assert.That(controller.TryBeginActivation(in command, replicaSpec, out _), Is.True);
                    Assert.That(replicaSpec.IsLocallyExecuting, Is.True);

                    // The authority processed and rejected the activation. An unrelated input-state
                    // mutation publishes a newer state carrying that command watermark.
                    Assert.That(authority.TrySetAbilityInputPressed(authoritySpec, true), Is.True);
                    Assert.That(authorityAdapter.TryCapture(1u, out IGASNetworkStateView rejectedView), Is.True);
                    var rejected = (GASNetworkStateBuffer)rejectedView;
                    PrepareSnapshot(receiver, rejected, batchSequence: 2u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(receiver, controller, out _, out _),
                        Is.True);
                    Assert.That(controller.Count, Is.Zero);
                    Assert.That(replicaSpec.IsActive, Is.False);
                    Assert.That(replicaSpec.IsLocallyExecuting, Is.False);

                    var lateResult = new GASCommandResult(
                        Epoch,
                        1u,
                        Entity,
                        grant,
                        GASAbilityCommandKind.Activate,
                        GASCommandStatus.Rejected,
                        rejected.StateVersion);
                    Assert.That(controller.HandleResult(in lateResult), Is.True);
                }
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        [Test]
        public void SnapshotReconciliation_LaterReopenFailureClosesEarlierReopenedPrediction()
        {
            GameplayAbility retainedAbility = CreateAbility(
                "Bridge.Prediction.Snapshot.Retained",
                EAbilityExecutionPolicy.LocalPredicted);
            GameplayAbility removedAbility = CreateAbility(
                "Bridge.Prediction.Snapshot.Removed",
                EAbilityExecutionPolicy.LocalPredicted);
            GASNetworkContentCatalog catalog = CreateAbilityCatalog(
                retainedAbility,
                removedAbility,
                out GASNetworkContentId retainedAbilityId,
                out GASNetworkContentId removedAbilityId);
            var contentResolver = new GASNetworkRuntimeContentResolver(catalog);
            GASNetworkRuntimeStateCapacity runtimeCapacity = SmallRuntimeCapacity();
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(
                authorityContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(
                replicaContext,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                authority.GrantAbility(retainedAbility);
                GameplayAbilitySpec authorityRemoved = authority.GrantAbility(removedAbility);
                var authorityIdentities = new GASAuthorityIdentityMap(Entity, Epoch, 4, 0);
                var authorityResolver = new TestIdentityResolver(authority, Entity, authorityIdentities);
                var authorityAdapter = new GASNetworkAuthorityStateAdapter(
                    authority,
                    authorityResolver,
                    authorityResolver,
                    contentResolver,
                    authorityIdentities,
                    new GASNetworkStateVersion(authority, Epoch),
                    runtimeCapacity);
                var receiver = new GASNetworkStateReceiver(
                    Epoch,
                    Entity,
                    catalog,
                    runtimeCapacity.State);
                var replicaResolver = new TestIdentityResolver(replica, Entity, null);
                var replicaAdapter = new GASNetworkReplicaStateAdapter(
                    replica,
                    Entity,
                    Epoch,
                    replicaResolver,
                    replicaResolver,
                    contentResolver,
                    runtimeCapacity);

                Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView initialView), Is.True);
                var initial = (GASNetworkStateBuffer)initialView;
                PrepareSnapshot(receiver, initial, batchSequence: 1u);
                Assert.That(replicaAdapter.TryApplyPrepared(receiver, out _, out _), Is.True);
                GASNetworkGrantId retainedGrant = FindGrant(initial, retainedAbilityId);
                GASNetworkGrantId removedGrant = FindGrant(initial, removedAbilityId);
                GameplayAbilitySpec replicaRetained = ResolveSpec(replica, replicaAdapter, retainedGrant);
                GameplayAbilitySpec replicaRemoved = ResolveSpec(replica, replicaAdapter, removedGrant);

                using (var controller = new GASNetworkPredictionController(
                           replica,
                           Entity,
                           Epoch,
                           replicaAdapter,
                           capacity: 4))
                {
                    var retainedCommand = new GASAbilityCommand(
                        Epoch, 1u, Entity, retainedGrant, GASAbilityCommandKind.Activate);
                    var removedCommand = new GASAbilityCommand(
                        Epoch, 2u, Entity, removedGrant, GASAbilityCommandKind.Activate);
                    Assert.That(
                        controller.TryBeginActivation(
                            in retainedCommand,
                            replicaRetained,
                            out GASPredictionKey retainedKey),
                        Is.True);
                    Assert.That(
                        controller.TryBeginActivation(
                            in removedCommand,
                            replicaRemoved,
                            out GASPredictionKey removedKey),
                        Is.True);

                    authority.ClearAbility(authorityRemoved);
                    Assert.That(authorityAdapter.TryCapture(0u, out IGASNetworkStateView removedView), Is.True);
                    var removed = (GASNetworkStateBuffer)removedView;
                    PrepareSnapshot(receiver, removed, batchSequence: 2u);
                    Assert.That(
                        replicaAdapter.TryApplyPrepared(
                            receiver,
                            controller,
                            out _,
                            out GASStateDeltaRejectionReason rejectionReason),
                        Is.False);
                    Assert.That(rejectionReason, Is.EqualTo(GASStateDeltaRejectionReason.ApplicationFailed));
                    Assert.That(receiver.HasPreparedState, Is.False);
                    Assert.That(controller.Count, Is.Zero);
                    Assert.That(replica.HasOpenPredictionWindow(retainedKey), Is.False);
                    Assert.That(replica.HasOpenPredictionWindow(removedKey), Is.False);
                    Assert.That(replica.OpenPredictionWindowCount, Is.Zero);
                    Assert.That(replicaRetained.IsActive, Is.False);
                    Assert.That(replica.GetActivatableAbilities().Count, Is.EqualTo(1));
                    Assert.That(replica.StateDeltaResyncRequired, Is.True);
                }
            }
            finally
            {
                replica.Dispose();
                authority.Dispose();
                replicaContext.Dispose();
                authorityContext.Dispose();
            }
        }

        private static GASNetworkRuntimeContentResolver CreateContentResolver(
            GameplayAbility ability,
            out GASNetworkContentId abilityId)
        {
            return new GASNetworkRuntimeContentResolver(CreateCatalog(ability, out abilityId));
        }

        private static GASNetworkContentCatalog CreateAbilityCatalog(
            GameplayAbility first,
            GameplayAbility second,
            out GASNetworkContentId firstId,
            out GASNetworkContentId secondId)
        {
            var builder = new GASNetworkContentCatalogBuilder();
            firstId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.bridge.snapshot.first",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.bridge.snapshot.first:1"),
                first);
            secondId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.bridge.snapshot.second",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.bridge.snapshot.second:1"),
                second);
            return builder.Build();
        }

        private static void PrepareSnapshot(
            GASNetworkStateReceiver receiver,
            GASNetworkStateBuffer state,
            uint batchSequence)
        {
            var header = new GASStateBatchChunk(
                receiver.StreamEpoch,
                batchSequence,
                receiver.Entity,
                GASStateBatchKind.Snapshot,
                0UL,
                state.StateVersion,
                state.LastProcessedCommandSequence,
                0,
                1,
                checked((ushort)state.AbilityCount),
                checked((ushort)state.AttributeCount),
                checked((ushort)state.EffectCount),
                checked((ushort)state.EffectTagCount),
                checked((ushort)state.EffectMagnitudeCount),
                checked((ushort)state.LooseTagCount),
                state.StateChecksum);
            Assert.That(
                receiver.ReceiveChunk(
                    in header,
                    state.Abilities,
                    state.Attributes,
                    state.Effects,
                    state.EffectTags,
                    state.EffectMagnitudes,
                    state.LooseTags),
                Is.EqualTo(GASStateReceiveResult.Prepared));
        }

        private static GASNetworkGrantId FindGrant(
            GASNetworkStateBuffer state,
            GASNetworkContentId definition)
        {
            for (int i = 0; i < state.AbilityCount; i++)
            {
                GASAbilityStateRecord ability = state.Abilities[i];
                if (ability.Definition == definition)
                    return ability.Grant;
            }
            Assert.Fail($"State did not contain ability definition {definition.Value}.");
            return default;
        }

        private static GameplayAbilitySpec ResolveSpec(
            AbilitySystemComponent abilitySystem,
            GASNetworkReplicaStateAdapter adapter,
            GASNetworkGrantId grant)
        {
            Assert.That(
                adapter.TryResolveAbilitySpecHandle(Entity, Epoch, grant, out int handle),
                Is.True);
            Assert.That(abilitySystem.TryGetAbilitySpecByHandle(handle, out GameplayAbilitySpec spec), Is.True);
            return spec;
        }

        private static GASNetworkContentCatalog CreateCatalog(
            GameplayAbility ability,
            out GASNetworkContentId abilityId)
        {
            var builder = new GASNetworkContentCatalogBuilder();
            abilityId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.bridge",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.bridge:1"),
                ability);
            return builder.Build();
        }

        private static GASNetworkContentCatalog CreateCatalog(
            GameplayAbility ability,
            GameplayEffect effect,
            out GASNetworkContentId abilityId,
            out GASNetworkContentId effectId)
        {
            var builder = new GASNetworkContentCatalogBuilder();
            abilityId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.bridge",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability.bridge:1"),
                ability);
            effectId = builder.Add(
                GASNetworkContentKind.EffectDefinition,
                "effect.bridge",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("effect.bridge:1"),
                effect);
            return builder.Build();
        }

        private static GASNetworkRuntimeStateCapacity SmallRuntimeCapacity()
        {
            return new GASNetworkRuntimeStateCapacity(
                SmallWireCapacity(),
                maxSetByCallerTagsPerEffect: 0,
                maxSetByCallerNamesPerEffect: 0,
                maxDynamicGrantedTagsPerEffect: 0,
                maxDynamicAssetTagsPerEffect: 0);
        }

        private static GASNetworkStateCapacity SmallWireCapacity()
        {
            return new GASNetworkStateCapacity(
                abilities: 4,
                attributes: 0,
                effects: 4,
                effectTags: 0,
                effectMagnitudes: 0,
                looseTags: 4);
        }

        private static TestAbility CreateAbility(
            string name,
            EAbilityExecutionPolicy executionPolicy)
        {
            return new TestAbility(name, executionPolicy);
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

        private sealed class TestIdentityResolver : IGASNetworkEntityResolver, IGASNetworkGrantResolver
        {
            private readonly AbilitySystemComponent abilitySystem;
            private readonly GASNetworkEntityId entity;
            private readonly GASAuthorityIdentityMap identities;

            public TestIdentityResolver(
                AbilitySystemComponent abilitySystem,
                GASNetworkEntityId entity,
                GASAuthorityIdentityMap identities)
            {
                this.abilitySystem = abilitySystem;
                this.entity = entity;
                this.identities = identities;
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
                if (identities != null &&
                    ReferenceEquals(candidate, abilitySystem) &&
                    identities.TryGetGrantId(abilitySpecHandle, out grant))
                {
                    streamEpoch = identities.StreamEpoch;
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
                if (identities != null &&
                    candidate == entity &&
                    streamEpoch == identities.StreamEpoch)
                {
                    return identities.TryGetAbilitySpecHandle(grant, out abilitySpecHandle);
                }
                abilitySpecHandle = 0;
                return false;
            }
        }

        private sealed class MutatingThrowOnceTargetHandler : IGASNetworkTargetCommandHandler
        {
            public int InvocationCount { get; private set; }

            public GASCommandStatus HandleTargetCommand(
                AbilitySystemComponent abilitySystem,
                GameplayAbilitySpec abilitySpec,
                in GASAbilityCommand command,
                ReadOnlySpan<GASNetworkEntityId> actorTargets)
            {
                InvocationCount++;
                if (InvocationCount == 1)
                {
                    Assert.That(abilitySystem.TrySetAbilityInputPressed(abilitySpec, true), Is.True);
                    throw new InvalidOperationException("Injected failure after a committed GAS mutation.");
                }

                return GASCommandStatus.Accepted;
            }
        }

        private sealed class CrossIdentityResolver : IGASNetworkEntityResolver, IGASNetworkGrantResolver
        {
            private readonly AbilitySystemComponent first;
            private readonly GASNetworkEntityId firstEntity;
            private readonly GASAuthorityIdentityMap firstIdentities;
            private readonly AbilitySystemComponent second;
            private readonly GASNetworkEntityId secondEntity;
            private readonly GASAuthorityIdentityMap secondIdentities;

            public CrossIdentityResolver(
                AbilitySystemComponent first,
                GASNetworkEntityId firstEntity,
                GASAuthorityIdentityMap firstIdentities,
                AbilitySystemComponent second,
                GASNetworkEntityId secondEntity,
                GASAuthorityIdentityMap secondIdentities)
            {
                this.first = first;
                this.firstEntity = firstEntity;
                this.firstIdentities = firstIdentities;
                this.second = second;
                this.secondEntity = secondEntity;
                this.secondIdentities = secondIdentities;
            }

            public bool TryGetNetworkEntityId(
                AbilitySystemComponent candidate,
                out GASNetworkEntityId entity)
            {
                if (ReferenceEquals(candidate, first))
                {
                    entity = firstEntity;
                    return true;
                }
                if (ReferenceEquals(candidate, second))
                {
                    entity = secondEntity;
                    return true;
                }
                entity = default;
                return false;
            }

            public bool TryResolveAbilitySystem(
                GASNetworkEntityId entity,
                out AbilitySystemComponent abilitySystem)
            {
                if (entity == firstEntity)
                {
                    abilitySystem = first;
                    return true;
                }
                if (entity == secondEntity)
                {
                    abilitySystem = second;
                    return true;
                }
                abilitySystem = null;
                return false;
            }

            public bool TryGetNetworkGrantId(
                AbilitySystemComponent abilitySystem,
                int abilitySpecHandle,
                out uint streamEpoch,
                out GASNetworkGrantId grant)
            {
                GASAuthorityIdentityMap identities = ReferenceEquals(abilitySystem, first)
                    ? firstIdentities
                    : ReferenceEquals(abilitySystem, second)
                        ? secondIdentities
                        : null;
                if (identities != null && identities.TryGetGrantId(abilitySpecHandle, out grant))
                {
                    streamEpoch = identities.StreamEpoch;
                    return true;
                }
                streamEpoch = 0u;
                grant = default;
                return false;
            }

            public bool TryResolveAbilitySpecHandle(
                GASNetworkEntityId entity,
                uint streamEpoch,
                GASNetworkGrantId grant,
                out int abilitySpecHandle)
            {
                GASAuthorityIdentityMap identities = entity == firstEntity
                    ? firstIdentities
                    : entity == secondEntity
                        ? secondIdentities
                        : null;
                if (identities != null && streamEpoch == identities.StreamEpoch)
                    return identities.TryGetAbilitySpecHandle(grant, out abilitySpecHandle);
                abilitySpecHandle = 0;
                return false;
            }
        }

        private sealed class CrossReplicaResolver : IGASNetworkEntityResolver, IGASNetworkGrantResolver
        {
            private readonly AbilitySystemComponent first;
            private readonly GASNetworkEntityId firstEntity;
            private readonly AbilitySystemComponent second;
            private readonly GASNetworkEntityId secondEntity;
            private IGASNetworkGrantResolver firstGrants;
            private IGASNetworkGrantResolver secondGrants;

            public CrossReplicaResolver(
                AbilitySystemComponent first,
                GASNetworkEntityId firstEntity,
                AbilitySystemComponent second,
                GASNetworkEntityId secondEntity)
            {
                this.first = first;
                this.firstEntity = firstEntity;
                this.second = second;
                this.secondEntity = secondEntity;
            }

            public void SetGrantResolvers(
                IGASNetworkGrantResolver firstGrants,
                IGASNetworkGrantResolver secondGrants)
            {
                this.firstGrants = firstGrants;
                this.secondGrants = secondGrants;
            }

            public bool TryGetNetworkEntityId(
                AbilitySystemComponent candidate,
                out GASNetworkEntityId entity)
            {
                if (ReferenceEquals(candidate, first))
                {
                    entity = firstEntity;
                    return true;
                }
                if (ReferenceEquals(candidate, second))
                {
                    entity = secondEntity;
                    return true;
                }
                entity = default;
                return false;
            }

            public bool TryResolveAbilitySystem(
                GASNetworkEntityId entity,
                out AbilitySystemComponent abilitySystem)
            {
                if (entity == firstEntity)
                {
                    abilitySystem = first;
                    return true;
                }
                if (entity == secondEntity)
                {
                    abilitySystem = second;
                    return true;
                }
                abilitySystem = null;
                return false;
            }

            public bool TryGetNetworkGrantId(
                AbilitySystemComponent abilitySystem,
                int abilitySpecHandle,
                out uint streamEpoch,
                out GASNetworkGrantId grant)
            {
                IGASNetworkGrantResolver resolver = ReferenceEquals(abilitySystem, first)
                    ? firstGrants
                    : ReferenceEquals(abilitySystem, second)
                        ? secondGrants
                        : null;
                if (resolver != null)
                {
                    return resolver.TryGetNetworkGrantId(
                        abilitySystem,
                        abilitySpecHandle,
                        out streamEpoch,
                        out grant);
                }
                streamEpoch = 0u;
                grant = default;
                return false;
            }

            public bool TryResolveAbilitySpecHandle(
                GASNetworkEntityId entity,
                uint streamEpoch,
                GASNetworkGrantId grant,
                out int abilitySpecHandle)
            {
                IGASNetworkGrantResolver resolver = entity == firstEntity
                    ? firstGrants
                    : entity == secondEntity
                        ? secondGrants
                        : null;
                if (resolver != null)
                {
                    return resolver.TryResolveAbilitySpecHandle(
                        entity,
                        streamEpoch,
                        grant,
                        out abilitySpecHandle);
                }
                abilitySpecHandle = 0;
                return false;
            }
        }

        private sealed class RejectingContentResolver : IGASNetworkRuntimeContentResolver
        {
            public bool TryGetAbilityId(GameplayAbility ability, out GASNetworkContentId id)
            {
                id = default;
                return false;
            }

            public bool TryResolveAbility(GASNetworkContentId id, out GameplayAbility ability)
            {
                ability = null;
                return false;
            }

            public bool TryGetEffectId(GameplayEffect effect, out GASNetworkContentId id)
            {
                id = default;
                return false;
            }

            public bool TryResolveEffect(GASNetworkContentId id, out GameplayEffect effect)
            {
                effect = null;
                return false;
            }

            public bool TryGetAttributeId(string attributeName, out GASNetworkContentId id)
            {
                id = default;
                return false;
            }

            public bool TryResolveAttributeName(GASNetworkContentId id, out string attributeName)
            {
                attributeName = null;
                return false;
            }

            public bool TryGetSetByCallerNameId(string name, out GASNetworkContentId id)
            {
                id = default;
                return false;
            }

            public bool TryResolveSetByCallerName(GASNetworkContentId id, out string name)
            {
                name = null;
                return false;
            }

            public bool TryGetTargetSurfaceId(object surface, out GASNetworkContentId id)
            {
                id = default;
                return false;
            }

            public bool TryResolveTargetSurface(GASNetworkContentId id, out object surface)
            {
                surface = null;
                return false;
            }
        }
    }

    internal static class GASNetworkStateViewTestExtensions
    {
        public static bool IsComplete(this IGASNetworkStateView state)
        {
            return state != null && state.Entity.IsValid &&
                   state.StateVersion != 0UL && state.StateChecksum != 0UL;
        }
    }
}
