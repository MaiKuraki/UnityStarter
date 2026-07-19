using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Resolves a validated prepared wire state into process-local GAS data and commits it to one
    /// replica AbilitySystemComponent. Resolution is completed before runtime mutation begins.
    /// </summary>
    /// <remarks>
    /// The adapter creates no worker threads and takes no locks. Backend callbacks, including
    /// threaded desktop transports, must marshal to the GAS owner thread. WebGL uses that same
    /// synchronous owner-thread path.
    /// </remarks>
    public sealed class GASNetworkReplicaStateAdapter : IGASNetworkGrantResolver
    {
        private readonly AbilitySystemComponent abilitySystem;
        private readonly IGASNetworkEntityResolver entityResolver;
        private readonly IGASNetworkGrantResolver grantResolver;
        private readonly IGASNetworkRuntimeContentResolver contentResolver;
        private readonly GASNetworkRuntimeStateCapacity capacity;
        private readonly GASAbilitySystemFullStateBuffer preparedLocalState;
        private readonly GASAbilitySystemFullStateBuffer localIdentityScan;
        private readonly GASReplicaIdentityMap identityMap;
        private readonly int ownerThreadId;

        private readonly GASNetworkGrantId[] newlyBoundGrants;
        private readonly GASNetworkEffectId[] newlyBoundEffects;
        private readonly GASNetworkGrantId[] lastGrants;
        private readonly GASNetworkEffectId[] lastEffects;
        private int newlyBoundGrantCount;
        private int newlyBoundEffectCount;
        private int lastGrantCount;
        private int lastEffectCount;
        private int nextGrantLocalId = 1;
        private int nextEffectLocalId = 1;
        private bool localIdentitySequenceInitialized;

        public GASNetworkReplicaStateAdapter(
            AbilitySystemComponent abilitySystem,
            GASNetworkEntityId entity,
            uint streamEpoch,
            IGASNetworkEntityResolver entityResolver,
            IGASNetworkGrantResolver grantResolver,
            IGASNetworkRuntimeContentResolver contentResolver,
            GASNetworkRuntimeStateCapacity capacity)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            this.entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
            this.grantResolver = grantResolver ?? throw new ArgumentNullException(nameof(grantResolver));
            this.contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
            this.capacity = capacity;

            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            if (!entityResolver.TryResolveAbilitySystem(entity, out AbilitySystemComponent resolved) ||
                !ReferenceEquals(resolved, abilitySystem))
            {
                throw new ArgumentException(
                    "The network entity must resolve to the supplied AbilitySystemComponent.",
                    nameof(entity));
            }

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            identityMap = new GASReplicaIdentityMap(
                entity,
                streamEpoch,
                capacity.State.Abilities,
                capacity.State.Effects);
            preparedLocalState = CreateLocalBuffer(capacity);
            localIdentityScan = CreateLocalBuffer(capacity);
            newlyBoundGrants = new GASNetworkGrantId[capacity.State.Abilities];
            newlyBoundEffects = new GASNetworkEffectId[capacity.State.Effects];
            lastGrants = new GASNetworkGrantId[capacity.State.Abilities];
            lastEffects = new GASNetworkEffectId[capacity.State.Effects];
        }

        public int OwnerThreadId => ownerThreadId;
        public GASNetworkEntityId Entity => identityMap.Entity;
        public uint StreamEpoch
        {
            get
            {
                AssertOwnerThread();
                return identityMap.StreamEpoch;
            }
        }

        /// <summary>
        /// Applies and commits the receiver's prepared state. Any conversion or runtime rejection
        /// rejects the receiver candidate and leaves its committed wire state unchanged.
        /// </summary>
        public bool TryApplyPrepared(
            GASNetworkStateReceiver receiver,
            out GASStateAcknowledgement acknowledgement,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            return TryApplyPrepared(
                receiver,
                null,
                out acknowledgement,
                out rejectionReason);
        }

        /// <summary>
        /// Applies one prepared state while coordinating bounded local prediction against the
        /// state's last-processed command watermark.
        /// </summary>
        public bool TryApplyPrepared(
            GASNetworkStateReceiver receiver,
            GASNetworkPredictionController predictionController,
            out GASStateAcknowledgement acknowledgement,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            AssertOwnerThread();
            acknowledgement = default;
            if (receiver == null ||
                !receiver.HasPreparedState ||
                receiver.Entity != Entity ||
                receiver.StreamEpoch != identityMap.StreamEpoch)
            {
                rejectionReason = GASStateDeltaRejectionReason.MissingDelta;
                return false;
            }

            if (predictionController == null && abilitySystem.OpenPredictionWindowCount != 0)
            {
                receiver.RejectPrepared();
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }

            IGASNetworkStateView state = receiver.PreparedState;
            if (!Preflight(state, out rejectionReason) ||
                !EnsureLocalIdentitySequence(out rejectionReason))
            {
                receiver.RejectPrepared();
                return false;
            }

            int startingGrantLocalId = nextGrantLocalId;
            int startingEffectLocalId = nextEffectLocalId;
            newlyBoundGrantCount = 0;
            newlyBoundEffectCount = 0;

            if (!TryPrepare(state, out rejectionReason))
            {
                RollbackNewBindings(startingGrantLocalId, startingEffectLocalId);
                receiver.RejectPrepared();
                return false;
            }

            bool predictionPrepared = false;
            if (predictionController != null)
            {
                if (!predictionController.CanCoordinateSnapshot(
                        abilitySystem,
                        Entity,
                        identityMap.StreamEpoch) ||
                    !predictionController.TryBeginSnapshotReconciliation(
                        state.LastProcessedCommandSequence))
                {
                    RollbackNewBindings(startingGrantLocalId, startingEffectLocalId);
                    receiver.RejectPrepared();
                    rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                    return false;
                }
                predictionPrepared = true;
            }

            if (!abilitySystem.TryApplyFullStateSnapshot(preparedLocalState, out rejectionReason))
            {
                if (predictionPrepared)
                    predictionController.CompleteSnapshotReconciliation(snapshotApplied: false);
                RollbackNewBindings(startingGrantLocalId, startingEffectLocalId);
                receiver.RejectPrepared();
                return false;
            }

            if (predictionPrepared &&
                !predictionController.CompleteSnapshotReconciliation(snapshotApplied: true))
            {
                RollbackNewBindings(startingGrantLocalId, startingEffectLocalId);
                receiver.RejectPrepared();
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }

            acknowledgement = receiver.CommitPrepared();
            RemoveStaleBindings(state);
            RememberCurrentBindings(state);
            newlyBoundGrantCount = 0;
            newlyBoundEffectCount = 0;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        /// <summary>Resolves this replica's stable grant ID for local command composition.</summary>
        public bool TryGetNetworkGrantId(
            AbilitySystemComponent candidate,
            int abilitySpecHandle,
            out uint streamEpoch,
            out GASNetworkGrantId grant)
        {
            AssertOwnerThread();
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

        /// <summary>Resolves one authority-issued grant ID to this replica's local spec handle.</summary>
        public bool TryResolveAbilitySpecHandle(
            GASNetworkEntityId entity,
            uint streamEpoch,
            GASNetworkGrantId grant,
            out int abilitySpecHandle)
        {
            AssertOwnerThread();
            if (entity == Entity && streamEpoch == identityMap.StreamEpoch)
            {
                return identityMap.TryGetAbilitySpecHandle(grant, out abilitySpecHandle);
            }
            abilitySpecHandle = 0;
            return false;
        }

        /// <summary>Clears wire identity bindings for a newly authenticated stream epoch.</summary>
        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertOwnerThread();
            identityMap.ResetEpoch(newStreamEpoch);
            preparedLocalState.ClearCounts();
            localIdentityScan.ClearCounts();
            lastGrantCount = 0;
            lastEffectCount = 0;
            localIdentitySequenceInitialized = false;
            nextGrantLocalId = 1;
            nextEffectLocalId = 1;
        }

        private bool TryPrepare(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            if (!TryBindAllIdentities(state, out rejectionReason))
            {
                return false;
            }

            preparedLocalState.ClearCounts();
            preparedLocalState.StateVersion = state.StateVersion - 1UL;
            preparedLocalState.StateChecksum = 0UL;

            if (!TryBuildEffects(state, out rejectionReason) ||
                !TryBuildAbilities(state, out rejectionReason) ||
                !TryBuildAttributes(state, out rejectionReason) ||
                !TryBuildLooseTags(state, out rejectionReason))
            {
                return false;
            }

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool Preflight(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            GASNetworkStateCapacity limits = capacity.State;
            if (state == null || state.Entity != Entity || state.StateVersion == 0UL ||
                state.StateChecksum == 0UL ||
                state.AbilityCount < 0 || state.AbilityCount > limits.Abilities ||
                state.AttributeCount < 0 || state.AttributeCount > limits.Attributes ||
                state.EffectCount < 0 || state.EffectCount > limits.Effects ||
                state.EffectTagCount < 0 || state.EffectTagCount > limits.EffectTags ||
                state.EffectMagnitudeCount < 0 ||
                state.EffectMagnitudeCount > limits.EffectMagnitudes ||
                state.LooseTagCount < 0 || state.LooseTagCount > limits.LooseTags)
            {
                rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                return false;
            }

            for (int i = 0; i < state.AbilityCount; i++)
            {
                GASAbilityStateRecord record = state.GetAbility(i);
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    !contentResolver.TryResolveAbility(record.Definition, out GameplayAbility definition) ||
                    definition == null ||
                    !definition.IsConfigurationInitialized ||
                    definition.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced ||
                    record.Level <= 0 ||
                    record.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                    (record.GrantingEffect.IsValid &&
                     !ContainsEffect(state, record.GrantingEffect)))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < state.AttributeCount; i++)
            {
                GASAttributeStateRecord record = state.GetAttribute(i);
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    !contentResolver.TryResolveAttributeName(record.Attribute, out string name) ||
                    string.IsNullOrEmpty(name))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < state.EffectCount; i++)
            {
                GASEffectStateRecord record = state.GetEffect(i);
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    !contentResolver.TryResolveEffect(record.Definition, out GameplayEffect definition) ||
                    definition == null ||
                    definition.DurationPolicy == EDurationPolicy.Instant ||
                    record.Level <= 0 ||
                    record.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                    record.StackCount <= 0 ||
                    record.StackCount > GetMaximumReplicatedStackCount(definition) ||
                    (definition.DurationPolicy == EDurationPolicy.HasDuration &&
                     record.DurationRaw <= 0L) ||
                    !ValidateEffectFlags(in record, definition) ||
                    !TryResolveSource(in record, out _, out _, requireGrantBinding: false) ||
                    !ValidateSourceGrantReference(state, in record) ||
                    !CountAndValidateEffectChildren(state, record.Effect, out int tagCount, out int magnitudeCount) ||
                    tagCount > capacity.MaxDynamicGrantedTagsPerEffect +
                               capacity.MaxDynamicAssetTagsPerEffect ||
                    magnitudeCount > capacity.MaxSetByCallerTagsPerEffect +
                                     capacity.MaxSetByCallerNamesPerEffect)
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < state.EffectTagCount; i++)
            {
                GASEffectTagStateRecord record = state.GetEffectTag(i);
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    !TryResolveTag(record.Tag, out _))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < state.EffectMagnitudeCount; i++)
            {
                GASEffectMagnitudeStateRecord record = state.GetEffectMagnitude(i);
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    !TryResolveMagnitudeKey(in record, out _, out _))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            long totalLooseTagCount = 0L;
            for (int i = 0; i < state.LooseTagCount; i++)
            {
                GASLooseTagStateRecord record = state.GetLooseTag(i);
                totalLooseTagCount += record.Count;
                if (record.Operation != GASStateRecordOperation.Upsert ||
                    record.Count <= 0 ||
                    record.Count > GameplayAbilitiesNetworkProtocol.MaxExplicitLooseTagCount ||
                    record.Count > abilitySystem.Limits.MaxTagChangesPerDelta ||
                    totalLooseTagCount > abilitySystem.Limits.MaxTagChangesPerDelta ||
                    !TryResolveTag(record.Tag, out _))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryBindAllIdentities(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            for (int i = 0; i < state.EffectCount; i++)
            {
                GASNetworkEffectId wireId = state.GetEffect(i).Effect;
                if (identityMap.TryGetEffectReconciliationId(wireId, out _))
                    continue;
                if (!TryAllocateEffectLocalId(out int localId) ||
                    identityMap.BindEffect(wireId, localId) != GASReplicaIdentityBindResult.Bound)
                {
                    rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                    return false;
                }
                newlyBoundEffects[newlyBoundEffectCount++] = wireId;
            }

            for (int i = 0; i < state.AbilityCount; i++)
            {
                GASNetworkGrantId wireId = state.GetAbility(i).Grant;
                if (identityMap.TryGetAbilitySpecHandle(wireId, out _))
                    continue;
                if (!TryAllocateGrantLocalId(out int localId) ||
                    identityMap.BindGrant(wireId, localId) != GASReplicaIdentityBindResult.Bound)
                {
                    rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                    return false;
                }
                newlyBoundGrants[newlyBoundGrantCount++] = wireId;
            }

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryBuildEffects(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            GASActiveEffectStateData[] effects =
                preparedLocalState.EnsureActiveEffectCapacity(state.EffectCount);
            for (int i = 0; i < state.EffectCount; i++)
            {
                GASEffectStateRecord record = state.GetEffect(i);
                if (!identityMap.TryGetEffectReconciliationId(record.Effect, out int reconciliationId) ||
                    !contentResolver.TryResolveEffect(record.Definition, out GameplayEffect definition) ||
                    !TryResolveSource(
                        in record,
                        out AbilitySystemComponent source,
                        out int sourceAbilitySpecHandle,
                        requireGrantBinding: true))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }

                int setByCallerTagCount = CountMagnitudes(
                    state,
                    record.Effect,
                    GASEffectMagnitudeKeyKind.GameplayTag);
                int setByCallerNameCount = CountMagnitudes(
                    state,
                    record.Effect,
                    GASEffectMagnitudeKeyKind.Name);
                int dynamicGrantedTagCount = CountEffectTags(
                    state,
                    record.Effect,
                    GASEffectTagKind.Granted);
                int dynamicAssetTagCount = CountEffectTags(
                    state,
                    record.Effect,
                    GASEffectTagKind.Asset);

                GASSetByCallerTagStateData[] tagMagnitudes =
                    preparedLocalState.EnsureActiveEffectSetByCallerCapacity(i, setByCallerTagCount);
                GASSetByCallerNameStateData[] nameMagnitudes =
                    preparedLocalState.EnsureActiveEffectSetByCallerNameCapacity(i, setByCallerNameCount);
                GameplayTag[] dynamicGrantedTags =
                    preparedLocalState.EnsureActiveEffectDynamicGrantedTagCapacity(i, dynamicGrantedTagCount);
                GameplayTag[] dynamicAssetTags =
                    preparedLocalState.EnsureActiveEffectDynamicAssetTagCapacity(i, dynamicAssetTagCount);

                int tagMagnitudeIndex = 0;
                int nameMagnitudeIndex = 0;
                for (int j = 0; j < state.EffectMagnitudeCount; j++)
                {
                    GASEffectMagnitudeStateRecord child = state.GetEffectMagnitude(j);
                    if (child.Effect != record.Effect)
                        continue;
                    if (!TryResolveMagnitudeKey(in child, out GameplayTag tag, out string name))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                    if (child.Key.Kind == GASEffectMagnitudeKeyKind.GameplayTag)
                    {
                        tagMagnitudes[tagMagnitudeIndex++] =
                            GASSetByCallerTagStateData.FromRaw(tag, child.ValueRaw);
                    }
                    else
                    {
                        nameMagnitudes[nameMagnitudeIndex++] =
                            GASSetByCallerNameStateData.FromRaw(name, child.ValueRaw);
                    }
                }

                int grantedIndex = 0;
                int assetIndex = 0;
                for (int j = 0; j < state.EffectTagCount; j++)
                {
                    GASEffectTagStateRecord child = state.GetEffectTag(j);
                    if (child.Effect != record.Effect || !TryResolveTag(child.Tag, out GameplayTag tag))
                        continue;
                    if (child.Kind == GASEffectTagKind.Granted)
                        dynamicGrantedTags[grantedIndex++] = tag;
                    else
                        dynamicAssetTags[assetIndex++] = tag;
                }

                GASPredictionKey predictionKey = record.SourceCommandSequence > 0u
                    ? new GASPredictionKey(
                        (int)record.SourceCommandSequence,
                        source != null ? source.CoreEntity : abilitySystem.CoreEntity,
                        (int)record.SourceCommandSequence)
                    : default;
                long periodTimeRemainingRaw = definition.Period > 0f ? record.PeriodRaw : -1L;
                effects[i] = GASActiveEffectStateData.FromRaw(
                    reconciliationId,
                    definition,
                    source,
                    sourceAbilitySpecHandle,
                    record.Level,
                    record.StackCount,
                    (record.Flags & GASEffectStateFlags.Inhibited) != 0,
                    record.DurationRaw,
                    record.RemainingRaw,
                    periodTimeRemainingRaw,
                    predictionKey,
                    tagMagnitudes,
                    tagMagnitudeIndex,
                    nameMagnitudes,
                    nameMagnitudeIndex,
                    dynamicGrantedTags,
                    grantedIndex,
                    dynamicAssetTags,
                    assetIndex);
            }

            preparedLocalState.ActiveEffectCount = state.EffectCount;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryBuildAbilities(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            GASGrantedAbilityStateData[] abilities =
                preparedLocalState.EnsureGrantedAbilityCapacity(state.AbilityCount);
            for (int i = 0; i < state.AbilityCount; i++)
            {
                GASAbilityStateRecord record = state.GetAbility(i);
                if (!identityMap.TryGetAbilitySpecHandle(record.Grant, out int specHandle) ||
                    !contentResolver.TryResolveAbility(record.Definition, out GameplayAbility definition))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }

                int grantingEffectReconciliationId = 0;
                if (record.GrantingEffect.IsValid &&
                    !identityMap.TryGetEffectReconciliationId(
                        record.GrantingEffect,
                        out grantingEffectReconciliationId))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }

                abilities[i] = new GASGrantedAbilityStateData(
                    specHandle,
                    definition,
                    record.Level,
                    (record.Flags & GASAbilityStateFlags.Active) != 0,
                    (record.Flags & GASAbilityStateFlags.InputPressed) != 0,
                    grantingEffectReconciliationId);
            }

            preparedLocalState.GrantedAbilityCount = state.AbilityCount;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryBuildAttributes(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            GASAttributeStateData[] attributes =
                preparedLocalState.EnsureAttributeCapacity(state.AttributeCount);
            for (int i = 0; i < state.AttributeCount; i++)
            {
                GASAttributeStateRecord record = state.GetAttribute(i);
                if (!contentResolver.TryResolveAttributeName(record.Attribute, out string name))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
                attributes[i] = GASAttributeStateData.FromRaw(
                    name,
                    record.BaseValueRaw,
                    record.CurrentValueRaw);
            }

            preparedLocalState.AttributeCount = state.AttributeCount;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryBuildLooseTags(
            IGASNetworkStateView state,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            GASTagCountStateData[] tags =
                preparedLocalState.EnsureLooseTagCapacity(state.LooseTagCount);
            for (int i = 0; i < state.LooseTagCount; i++)
            {
                GASLooseTagStateRecord record = state.GetLooseTag(i);
                if (!TryResolveTag(record.Tag, out GameplayTag tag))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
                tags[i] = new GASTagCountStateData(tag, record.Count);
            }

            preparedLocalState.LooseTagCount = state.LooseTagCount;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool EnsureLocalIdentitySequence(out GASStateDeltaRejectionReason rejectionReason)
        {
            if (localIdentitySequenceInitialized)
            {
                rejectionReason = GASStateDeltaRejectionReason.None;
                return true;
            }

            try
            {
                abilitySystem.CaptureFullStateNonAlloc(localIdentityScan);
            }
            catch (InvalidOperationException)
            {
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }

            if (localIdentityScan.GrantedAbilityCount > capacity.State.Abilities ||
                localIdentityScan.ActiveEffectCount > capacity.State.Effects ||
                localIdentityScan.AttributeCount > capacity.State.Attributes ||
                localIdentityScan.LooseTagCount > capacity.State.LooseTags)
            {
                rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                return false;
            }

            int maxGrant = 0;
            for (int i = 0; i < localIdentityScan.GrantedAbilityCount; i++)
                maxGrant = Math.Max(maxGrant, localIdentityScan.GrantedAbilities[i].SpecHandle);
            int maxEffect = 0;
            for (int i = 0; i < localIdentityScan.ActiveEffectCount; i++)
                maxEffect = Math.Max(maxEffect, localIdentityScan.ActiveEffects[i].ReconciliationId);
            if (maxGrant == int.MaxValue || maxEffect == int.MaxValue)
            {
                rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                return false;
            }

            nextGrantLocalId = maxGrant + 1;
            nextEffectLocalId = maxEffect + 1;
            localIdentitySequenceInitialized = true;
            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool TryAllocateGrantLocalId(out int localId)
        {
            if (nextGrantLocalId <= 0 || nextGrantLocalId == int.MaxValue)
            {
                localId = 0;
                return false;
            }
            localId = nextGrantLocalId;
            nextGrantLocalId = nextGrantLocalId == int.MaxValue ? 0 : nextGrantLocalId + 1;
            return true;
        }

        private bool TryAllocateEffectLocalId(out int localId)
        {
            if (nextEffectLocalId <= 0 || nextEffectLocalId == int.MaxValue)
            {
                localId = 0;
                return false;
            }
            localId = nextEffectLocalId;
            nextEffectLocalId = nextEffectLocalId == int.MaxValue ? 0 : nextEffectLocalId + 1;
            return true;
        }

        private bool TryResolveSource(
            in GASEffectStateRecord record,
            out AbilitySystemComponent source,
            out int sourceAbilitySpecHandle,
            bool requireGrantBinding)
        {
            source = null;
            sourceAbilitySpecHandle = 0;
            if (!record.SourceEntity.IsValid)
            {
                return record.SourceStreamEpoch == 0u && !record.SourceGrant.IsValid;
            }
            if (!entityResolver.TryResolveAbilitySystem(record.SourceEntity, out source) ||
                source == null || source.IsDisposed ||
                !ReferenceEquals(source.RuntimeContext, abilitySystem.RuntimeContext))
            {
                return false;
            }

            if (!record.SourceGrant.IsValid)
                return record.SourceStreamEpoch == 0u;
            if (record.SourceStreamEpoch == 0u)
                return false;
            if (!requireGrantBinding)
                return true;
            if (record.SourceEntity == Entity)
            {
                return record.SourceStreamEpoch == identityMap.StreamEpoch &&
                       identityMap.TryGetAbilitySpecHandle(
                    record.SourceGrant,
                    out sourceAbilitySpecHandle);
            }

            return grantResolver.TryResolveAbilitySpecHandle(
                       record.SourceEntity,
                       record.SourceStreamEpoch,
                       record.SourceGrant,
                       out sourceAbilitySpecHandle) &&
                   sourceAbilitySpecHandle > 0;
        }

        private static bool ValidateEffectFlags(
            in GASEffectStateRecord record,
            GameplayEffect definition)
        {
            bool infinite = definition.DurationPolicy == EDurationPolicy.Infinite;
            bool wireInfinite = (record.Flags & GASEffectStateFlags.Infinite) != 0;
            bool predicted = (record.Flags & GASEffectStateFlags.Predicted) != 0;
            return infinite == wireInfinite &&
                   (!predicted || record.SourceCommandSequence > 0u) &&
                   (definition.Period > 0f || record.PeriodRaw == 0L);
        }

        private static int GetMaximumReplicatedStackCount(GameplayEffect definition)
        {
            return definition.Stacking.Type == EGameplayEffectStackingType.None
                ? 1
                : definition.Stacking.Limit;
        }

        private bool ValidateSourceGrantReference(
            IGASNetworkStateView state,
            in GASEffectStateRecord record)
        {
            if (!record.SourceGrant.IsValid)
                return record.SourceStreamEpoch == 0u;
            if (record.SourceEntity == Entity)
            {
                return record.SourceStreamEpoch == identityMap.StreamEpoch &&
                       ContainsGrant(state, record.SourceGrant);
            }
            return grantResolver.TryResolveAbilitySpecHandle(
                       record.SourceEntity,
                       record.SourceStreamEpoch,
                       record.SourceGrant,
                       out int sourceHandle) &&
                   sourceHandle > 0;
        }

        private bool CountAndValidateEffectChildren(
            IGASNetworkStateView state,
            GASNetworkEffectId effect,
            out int tagCount,
            out int magnitudeCount)
        {
            int grantedTags = CountEffectTags(state, effect, GASEffectTagKind.Granted);
            int assetTags = CountEffectTags(state, effect, GASEffectTagKind.Asset);
            int tagMagnitudes = CountMagnitudes(state, effect, GASEffectMagnitudeKeyKind.GameplayTag);
            int nameMagnitudes = CountMagnitudes(state, effect, GASEffectMagnitudeKeyKind.Name);
            tagCount = grantedTags + assetTags;
            magnitudeCount = tagMagnitudes + nameMagnitudes;
            return grantedTags <= capacity.MaxDynamicGrantedTagsPerEffect &&
                   assetTags <= capacity.MaxDynamicAssetTagsPerEffect &&
                   tagMagnitudes <= capacity.MaxSetByCallerTagsPerEffect &&
                   nameMagnitudes <= capacity.MaxSetByCallerNamesPerEffect;
        }

        private static int CountEffectTags(
            IGASNetworkStateView state,
            GASNetworkEffectId effect,
            GASEffectTagKind kind)
        {
            int count = 0;
            for (int i = 0; i < state.EffectTagCount; i++)
            {
                GASEffectTagStateRecord record = state.GetEffectTag(i);
                if (record.Effect == effect && record.Kind == kind)
                    count++;
            }
            return count;
        }

        private static int CountMagnitudes(
            IGASNetworkStateView state,
            GASNetworkEffectId effect,
            GASEffectMagnitudeKeyKind kind)
        {
            int count = 0;
            for (int i = 0; i < state.EffectMagnitudeCount; i++)
            {
                GASEffectMagnitudeStateRecord record = state.GetEffectMagnitude(i);
                if (record.Effect == effect && record.Key.Kind == kind)
                    count++;
            }
            return count;
        }

        private bool TryResolveMagnitudeKey(
            in GASEffectMagnitudeStateRecord record,
            out GameplayTag tag,
            out string name)
        {
            tag = default;
            name = null;
            if (record.Key.Kind == GASEffectMagnitudeKeyKind.GameplayTag)
                return TryResolveTag(record.Key.Tag, out tag);
            return record.Key.Kind == GASEffectMagnitudeKeyKind.Name &&
                   contentResolver.TryResolveSetByCallerName(record.Key.Name, out name) &&
                   !string.IsNullOrEmpty(name);
        }

        private static bool TryResolveTag(GASNetworkTagId id, out GameplayTag tag)
        {
            tag = default;
            return id.IsValid && GameplayTagManager.TryGetTagFromStableId(id.Value, out tag) &&
                   !tag.IsNone && tag.IsValid;
        }

        private void RollbackNewBindings(int grantLocalId, int effectLocalId)
        {
            for (int i = newlyBoundGrantCount - 1; i >= 0; i--)
                identityMap.RemoveGrant(newlyBoundGrants[i], out _);
            for (int i = newlyBoundEffectCount - 1; i >= 0; i--)
                identityMap.RemoveEffect(newlyBoundEffects[i], out _);
            newlyBoundGrantCount = 0;
            newlyBoundEffectCount = 0;
            nextGrantLocalId = grantLocalId;
            nextEffectLocalId = effectLocalId;
        }

        private void RemoveStaleBindings(IGASNetworkStateView state)
        {
            for (int i = 0; i < lastGrantCount; i++)
            {
                GASNetworkGrantId wireId = lastGrants[i];
                if (!ContainsGrant(state, wireId))
                    identityMap.RemoveGrant(wireId, out _);
            }
            for (int i = 0; i < lastEffectCount; i++)
            {
                GASNetworkEffectId wireId = lastEffects[i];
                if (!ContainsEffect(state, wireId))
                    identityMap.RemoveEffect(wireId, out _);
            }
        }

        private void RememberCurrentBindings(IGASNetworkStateView state)
        {
            lastGrantCount = state.AbilityCount;
            for (int i = 0; i < lastGrantCount; i++)
                lastGrants[i] = state.GetAbility(i).Grant;
            lastEffectCount = state.EffectCount;
            for (int i = 0; i < lastEffectCount; i++)
                lastEffects[i] = state.GetEffect(i).Effect;
        }

        private static bool ContainsGrant(IGASNetworkStateView state, GASNetworkGrantId grant)
        {
            for (int i = 0; i < state.AbilityCount; i++)
            {
                if (state.GetAbility(i).Grant == grant)
                    return true;
            }
            return false;
        }

        private static bool ContainsEffect(IGASNetworkStateView state, GASNetworkEffectId effect)
        {
            for (int i = 0; i < state.EffectCount; i++)
            {
                if (state.GetEffect(i).Effect == effect)
                    return true;
            }
            return false;
        }

        private static GASAbilitySystemFullStateBuffer CreateLocalBuffer(
            GASNetworkRuntimeStateCapacity capacity)
        {
            var buffer = new GASAbilitySystemFullStateBuffer();
            buffer.Reserve(
                capacity.State.Abilities,
                capacity.State.Effects,
                capacity.State.Attributes,
                capacity.State.LooseTags,
                capacity.MaxSetByCallerTagsPerEffect,
                capacity.MaxSetByCallerNamesPerEffect,
                capacity.MaxDynamicGrantedTagsPerEffect,
                capacity.MaxDynamicAssetTagsPerEffect);
            return buffer;
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS replica state adapter is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }
    }
}
