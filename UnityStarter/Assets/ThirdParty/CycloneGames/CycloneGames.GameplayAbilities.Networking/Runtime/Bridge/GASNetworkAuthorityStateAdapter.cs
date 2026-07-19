using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Converts one authority-owned AbilitySystemComponent into a complete stable-identity state.
    /// The adapter is fixed-capacity, owner-thread-affine, and publishes only fully sealed buffers.
    /// </summary>
    /// <remarks>
    /// The adapter creates no worker threads and takes no locks. On WebGL it runs on the Unity
    /// player thread; threaded backends must marshal delivery to this owner before capture.
    /// </remarks>
    public sealed class GASNetworkAuthorityStateAdapter
    {
        private readonly AbilitySystemComponent abilitySystem;
        private readonly IGASNetworkEntityResolver entityResolver;
        private readonly IGASNetworkGrantResolver grantResolver;
        private readonly IGASNetworkRuntimeContentResolver contentResolver;
        private readonly GASAuthorityIdentityMap identityMap;
        private readonly GASNetworkStateVersion stateVersion;
        private readonly GASNetworkRuntimeStateCapacity capacity;
        private readonly GASAbilitySystemFullStateBuffer localState;
        private readonly int ownerThreadId;

        private GASNetworkStateBuffer publishedState;
        private GASNetworkStateBuffer stagingState;
        private readonly int[] newlyCreatedGrantHandles;
        private readonly int[] newlyCreatedEffectIds;
        private readonly int[] lastGrantHandles;
        private readonly int[] lastEffectIds;
        private int newlyCreatedGrantCount;
        private int newlyCreatedEffectCount;
        private int lastGrantCount;
        private int lastEffectCount;
        private uint observedStreamEpoch;
        private ulong lastPublishedWireVersion;
        private bool hasPublishedVersion;

        public GASNetworkAuthorityStateAdapter(
            AbilitySystemComponent abilitySystem,
            IGASNetworkEntityResolver entityResolver,
            IGASNetworkGrantResolver grantResolver,
            IGASNetworkRuntimeContentResolver contentResolver,
            GASAuthorityIdentityMap identityMap,
            GASNetworkStateVersion stateVersion,
            GASNetworkRuntimeStateCapacity capacity)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            this.entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
            this.grantResolver = grantResolver ?? throw new ArgumentNullException(nameof(grantResolver));
            this.contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
            this.identityMap = identityMap ?? throw new ArgumentNullException(nameof(identityMap));
            this.stateVersion = stateVersion ?? throw new ArgumentNullException(nameof(stateVersion));
            this.capacity = capacity;

            if (!identityMap.IsOwnerThread)
                throw new InvalidOperationException("The authority identity map must be created on the adapter owner thread.");
            if (!stateVersion.IsOwnerThread)
                throw new InvalidOperationException("The network state version must be created on the adapter owner thread.");
            if (!ReferenceEquals(stateVersion.AbilitySystem, abilitySystem))
            {
                throw new ArgumentException(
                    "The network state version must own the supplied AbilitySystemComponent.",
                    nameof(stateVersion));
            }
            if (stateVersion.StreamEpoch != identityMap.StreamEpoch)
            {
                throw new ArgumentException(
                    "The network state version and authority identity map must use the same stream epoch.",
                    nameof(stateVersion));
            }
            if (!entityResolver.TryGetNetworkEntityId(abilitySystem, out GASNetworkEntityId entity) ||
                entity != identityMap.Entity)
            {
                throw new ArgumentException(
                    "The AbilitySystemComponent must resolve to the authority identity map entity.",
                    nameof(identityMap));
            }

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            observedStreamEpoch = identityMap.StreamEpoch;
            publishedState = new GASNetworkStateBuffer(capacity.State);
            stagingState = new GASNetworkStateBuffer(capacity.State);
            localState = new GASAbilitySystemFullStateBuffer();
            localState.Reserve(
                capacity.State.Abilities,
                capacity.State.Effects,
                capacity.State.Attributes,
                capacity.State.LooseTags,
                capacity.MaxSetByCallerTagsPerEffect,
                capacity.MaxSetByCallerNamesPerEffect,
                capacity.MaxDynamicGrantedTagsPerEffect,
                capacity.MaxDynamicAssetTagsPerEffect);

            newlyCreatedGrantHandles = new int[capacity.State.Abilities];
            newlyCreatedEffectIds = new int[capacity.State.Effects];
            lastGrantHandles = new int[capacity.State.Abilities];
            lastEffectIds = new int[capacity.State.Effects];
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
        /// Captures a newer complete network-visible state. A repeated or decreasing wire version is
        /// rejected; callers rotate the stream epoch before the version reaches <see cref="ulong.MaxValue"/>.
        /// The returned view remains valid until the next successful capture.
        /// </summary>
        public bool TryCapture(
            uint lastProcessedCommandSequence,
            out IGASNetworkStateView state)
        {
            AssertOwnerThread();
            state = null;
            if (lastProcessedCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
                return false;

            if (!ObserveEpochChange())
                return false;
            try
            {
                abilitySystem.CaptureFullStateNonAlloc(localState);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            ulong localVersion = localState.StateVersion;
            if (!stateVersion.TryObserveLocalStateVersion(localVersion, out ulong wireVersion) ||
                (hasPublishedVersion && wireVersion <= lastPublishedWireVersion) ||
                !PreflightLocalState())
            {
                return false;
            }

            newlyCreatedGrantCount = 0;
            newlyCreatedEffectCount = 0;
            stagingState.BeginWrite(Entity, wireVersion, lastProcessedCommandSequence);

            if (!TryWriteEffects() ||
                !TryWriteAbilities() ||
                !TryWriteAttributes() ||
                !TryWriteLooseTags() ||
                !stagingState.TryCompleteWrite())
            {
                stagingState.Clear();
                RollbackNewIdentityBindings();
                return false;
            }

            GASNetworkStateBuffer previousPublished = publishedState;
            publishedState = stagingState;
            stagingState = previousPublished;
            stagingState.Clear();

            RemoveStaleIdentityBindings();
            RememberCurrentIdentityBindings();
            lastPublishedWireVersion = wireVersion;
            hasPublishedVersion = true;
            newlyCreatedGrantCount = 0;
            newlyCreatedEffectCount = 0;
            state = publishedState;
            return true;
        }

        private bool PreflightLocalState()
        {
            GASNetworkStateCapacity stateCapacity = publishedState.Capacity;
            if (localState.GrantedAbilityCount < 0 ||
                localState.GrantedAbilityCount > stateCapacity.Abilities ||
                localState.ActiveEffectCount < 0 ||
                localState.ActiveEffectCount > stateCapacity.Effects ||
                localState.AttributeCount < 0 ||
                localState.AttributeCount > stateCapacity.Attributes ||
                localState.LooseTagCount < 0 ||
                localState.LooseTagCount > stateCapacity.LooseTags)
            {
                return false;
            }

            long effectTagCount = 0L;
            long effectMagnitudeCount = 0L;
            for (int i = 0; i < localState.GrantedAbilityCount; i++)
            {
                ref readonly GASGrantedAbilityStateData ability = ref localState.GrantedAbilities[i];
                if (ability.SpecHandle <= 0 ||
                    !(ability.AbilityDefinition is GameplayAbility definition) ||
                    !contentResolver.TryGetAbilityId(definition, out GASNetworkContentId definitionId) ||
                    !definitionId.IsValid ||
                    ability.Level <= 0 ||
                    ability.GrantingEffectReconciliationId < 0 ||
                    (ability.GrantingEffectReconciliationId > 0 &&
                     !ContainsEffect(ability.GrantingEffectReconciliationId)))
                {
                    return false;
                }
            }

            for (int i = 0; i < localState.ActiveEffectCount; i++)
            {
                ref readonly GASActiveEffectStateData effect = ref localState.ActiveEffects[i];
                if (effect.ReconciliationId <= 0 ||
                    !(effect.EffectDefinition is GameplayEffect definition) ||
                    !contentResolver.TryGetEffectId(definition, out GASNetworkContentId definitionId) ||
                    !definitionId.IsValid ||
                    effect.Level <= 0 || effect.StackCount <= 0 ||
                    effect.DurationRaw < 0L || effect.TimeRemainingRaw < 0L ||
                    (definition.Period > 0f
                        ? effect.PeriodTimeRemainingRaw > GASFixedValue.FromFloat(definition.Period).RawValue
                        : effect.PeriodTimeRemainingRaw != -1L) ||
                    !ValidatePredictionKey(effect.PredictionKey) ||
                    !ValidateEffectArrays(in effect) ||
                    effect.SetByCallerTagMagnitudeCount > capacity.MaxSetByCallerTagsPerEffect ||
                    effect.SetByCallerNameMagnitudeCount > capacity.MaxSetByCallerNamesPerEffect ||
                    effect.DynamicGrantedTagCount > capacity.MaxDynamicGrantedTagsPerEffect ||
                    effect.DynamicAssetTagCount > capacity.MaxDynamicAssetTagsPerEffect)
                {
                    return false;
                }

                if (!TryResolveSource(in effect, createOwnGrant: false, out _, out _, out _))
                    return false;

                effectMagnitudeCount += effect.SetByCallerTagMagnitudeCount;
                effectMagnitudeCount += effect.SetByCallerNameMagnitudeCount;
                effectTagCount += effect.DynamicGrantedTagCount;
                effectTagCount += effect.DynamicAssetTagCount;
                if (effectMagnitudeCount > stateCapacity.EffectMagnitudes ||
                    effectTagCount > stateCapacity.EffectTags)
                {
                    return false;
                }

                for (int j = 0; j < effect.SetByCallerTagMagnitudeCount; j++)
                {
                    if (!TryGetTagId(effect.SetByCallerTagMagnitudes[j].Tag, out _))
                        return false;
                }
                for (int j = 0; j < effect.SetByCallerNameMagnitudeCount; j++)
                {
                    string name = effect.SetByCallerNameMagnitudes[j].Name;
                    if (!contentResolver.TryGetSetByCallerNameId(name, out GASNetworkContentId id) ||
                        !id.IsValid)
                    {
                        return false;
                    }
                }
                for (int j = 0; j < effect.DynamicGrantedTagCount; j++)
                {
                    if (!TryGetTagId(effect.DynamicGrantedTags[j], out _))
                        return false;
                }
                for (int j = 0; j < effect.DynamicAssetTagCount; j++)
                {
                    if (!TryGetTagId(effect.DynamicAssetTags[j], out _))
                        return false;
                }
            }

            for (int i = 0; i < localState.AttributeCount; i++)
            {
                string name = localState.Attributes[i].AttributeName;
                if (!contentResolver.TryGetAttributeId(name, out GASNetworkContentId id) || !id.IsValid)
                    return false;
            }

            for (int i = 0; i < localState.LooseTagCount; i++)
            {
                ref readonly GASTagCountStateData tag = ref localState.LooseTags[i];
                if (tag.ExplicitCount <= 0 || !TryGetTagId(tag.Tag, out _))
                    return false;
            }

            return true;
        }

        private bool TryWriteEffects()
        {
            for (int i = 0; i < localState.ActiveEffectCount; i++)
            {
                ref readonly GASActiveEffectStateData effect = ref localState.ActiveEffects[i];
                if (!TryGetOrCreateEffectId(effect.ReconciliationId, out GASNetworkEffectId effectId) ||
                    !(effect.EffectDefinition is GameplayEffect definition) ||
                    !contentResolver.TryGetEffectId(definition, out GASNetworkContentId definitionId) ||
                    !TryResolveSource(
                        in effect,
                        createOwnGrant: true,
                        out GASNetworkEntityId sourceEntity,
                        out uint sourceStreamEpoch,
                        out GASNetworkGrantId sourceGrant))
                {
                    return false;
                }

                GASEffectStateFlags flags = GASEffectStateFlags.None;
                if (definition.DurationPolicy == EDurationPolicy.Infinite)
                    flags |= GASEffectStateFlags.Infinite;
                if (effect.IsInhibited)
                    flags |= GASEffectStateFlags.Inhibited;
                if (IsPredictedSource(in effect))
                    flags |= GASEffectStateFlags.Predicted;

                uint sourceCommandSequence = effect.PredictionKey.IsValid
                    ? (uint)effect.PredictionKey.InputSequence
                    : 0u;
                var record = new GASEffectStateRecord(
                    GASStateRecordOperation.Upsert,
                    effectId,
                    definitionId,
                    sourceEntity,
                    sourceStreamEpoch,
                    sourceGrant,
                    effect.Level,
                    effect.StackCount,
                    effect.DurationRaw,
                    effect.TimeRemainingRaw,
                    effect.PeriodTimeRemainingRaw < 0L ? 0L : effect.PeriodTimeRemainingRaw,
                    sourceCommandSequence,
                    flags);
                if (!stagingState.TrySetEffect(in record))
                    return false;

                for (int j = 0; j < effect.SetByCallerTagMagnitudeCount; j++)
                {
                    ref readonly GASSetByCallerTagStateData magnitude =
                        ref effect.SetByCallerTagMagnitudes[j];
                    if (!TryGetTagId(magnitude.Tag, out GASNetworkTagId tag))
                        return false;
                    var child = new GASEffectMagnitudeStateRecord(
                        GASStateRecordOperation.Upsert,
                        effectId,
                        GASNetworkMagnitudeKey.FromTag(tag),
                        magnitude.ValueRaw);
                    if (!stagingState.TrySetEffectMagnitude(in child))
                        return false;
                }

                for (int j = 0; j < effect.SetByCallerNameMagnitudeCount; j++)
                {
                    ref readonly GASSetByCallerNameStateData magnitude =
                        ref effect.SetByCallerNameMagnitudes[j];
                    if (!contentResolver.TryGetSetByCallerNameId(
                            magnitude.Name,
                            out GASNetworkContentId nameId))
                    {
                        return false;
                    }
                    var child = new GASEffectMagnitudeStateRecord(
                        GASStateRecordOperation.Upsert,
                        effectId,
                        GASNetworkMagnitudeKey.FromName(nameId),
                        magnitude.ValueRaw);
                    if (!stagingState.TrySetEffectMagnitude(in child))
                        return false;
                }

                if (!TryWriteEffectTags(
                        effectId,
                        effect.DynamicGrantedTags,
                        effect.DynamicGrantedTagCount,
                        GASEffectTagKind.Granted) ||
                    !TryWriteEffectTags(
                        effectId,
                        effect.DynamicAssetTags,
                        effect.DynamicAssetTagCount,
                        GASEffectTagKind.Asset))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryWriteAbilities()
        {
            for (int i = 0; i < localState.GrantedAbilityCount; i++)
            {
                ref readonly GASGrantedAbilityStateData ability = ref localState.GrantedAbilities[i];
                if (!TryGetOrCreateGrantId(ability.SpecHandle, out GASNetworkGrantId grant) ||
                    !(ability.AbilityDefinition is GameplayAbility definition) ||
                    !contentResolver.TryGetAbilityId(definition, out GASNetworkContentId definitionId))
                {
                    return false;
                }

                GASNetworkEffectId grantingEffect = default;
                if (ability.GrantingEffectReconciliationId > 0 &&
                    !identityMap.TryGetEffectId(
                        ability.GrantingEffectReconciliationId,
                        out grantingEffect))
                {
                    return false;
                }

                GASAbilityStateFlags flags = GASAbilityStateFlags.None;
                if (ability.IsActive)
                    flags |= GASAbilityStateFlags.Active;
                if (ability.IsInputPressed)
                    flags |= GASAbilityStateFlags.InputPressed;
                if (ability.IsActive &&
                    definition.ExecutionPolicy == EAbilityExecutionPolicy.LocalPredicted)
                {
                    flags |= GASAbilityStateFlags.Predicted;
                }

                var record = new GASAbilityStateRecord(
                    GASStateRecordOperation.Upsert,
                    grant,
                    definitionId,
                    grantingEffect,
                    ability.Level,
                    flags);
                if (!stagingState.TrySetAbility(in record))
                    return false;
            }

            return true;
        }

        private bool TryWriteAttributes()
        {
            for (int i = 0; i < localState.AttributeCount; i++)
            {
                ref readonly GASAttributeStateData attribute = ref localState.Attributes[i];
                if (!contentResolver.TryGetAttributeId(
                        attribute.AttributeName,
                        out GASNetworkContentId attributeId))
                {
                    return false;
                }
                var record = new GASAttributeStateRecord(
                    GASStateRecordOperation.Upsert,
                    attributeId,
                    attribute.BaseValueRaw,
                    attribute.CurrentValueRaw);
                if (!stagingState.TrySetAttribute(in record))
                    return false;
            }

            return true;
        }

        private bool TryWriteLooseTags()
        {
            for (int i = 0; i < localState.LooseTagCount; i++)
            {
                ref readonly GASTagCountStateData tag = ref localState.LooseTags[i];
                if (!TryGetTagId(tag.Tag, out GASNetworkTagId tagId))
                    return false;
                var record = new GASLooseTagStateRecord(
                    GASStateRecordOperation.Upsert,
                    tagId,
                    tag.ExplicitCount);
                if (!stagingState.TrySetLooseTag(in record))
                    return false;
            }

            return true;
        }

        private bool TryWriteEffectTags(
            GASNetworkEffectId effect,
            GameplayTag[] tags,
            int count,
            GASEffectTagKind kind)
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryGetTagId(tags[i], out GASNetworkTagId tag))
                    return false;
                var record = new GASEffectTagStateRecord(
                    GASStateRecordOperation.Upsert,
                    effect,
                    tag,
                    kind);
                if (!stagingState.TrySetEffectTag(in record))
                    return false;
            }

            return true;
        }

        private bool TryResolveSource(
            in GASActiveEffectStateData effect,
            bool createOwnGrant,
            out GASNetworkEntityId sourceEntity,
            out uint sourceStreamEpoch,
            out GASNetworkGrantId sourceGrant)
        {
            sourceEntity = default;
            sourceStreamEpoch = 0u;
            sourceGrant = default;
            if (effect.SourceComponent == null)
                return effect.SourceAbilitySpecHandle == 0;
            if (!(effect.SourceComponent is AbilitySystemComponent source) ||
                source.IsDisposed ||
                !ReferenceEquals(source.RuntimeContext, abilitySystem.RuntimeContext) ||
                !entityResolver.TryGetNetworkEntityId(source, out sourceEntity) ||
                !sourceEntity.IsValid)
            {
                return false;
            }

            if (effect.SourceAbilitySpecHandle == 0)
                return true;
            if (effect.SourceAbilitySpecHandle < 0)
                return false;

            if (ReferenceEquals(source, abilitySystem))
            {
                if (!ContainsGrant(effect.SourceAbilitySpecHandle))
                    return true;
                if (!createOwnGrant)
                    return true;
                if (!TryGetOrCreateGrantId(effect.SourceAbilitySpecHandle, out sourceGrant))
                    return false;
                sourceStreamEpoch = identityMap.StreamEpoch;
                return true;
            }

            // A persistent effect can outlive the source grant. Keep the stable source entity,
            // but do not retain an unbounded tombstone grant mapping for the rest of the epoch.
            if (!source.TryGetAbilitySpecByHandle(effect.SourceAbilitySpecHandle, out _))
                return true;

            return grantResolver.TryGetNetworkGrantId(
                       source,
                       effect.SourceAbilitySpecHandle,
                       out sourceStreamEpoch,
                       out sourceGrant) &&
                   sourceStreamEpoch != 0u &&
                   sourceGrant.IsValid;
        }

        private bool TryGetOrCreateGrantId(int localId, out GASNetworkGrantId grant)
        {
            GASAuthorityIdentityMapResult result = identityMap.GetOrCreateGrantId(localId, out grant);
            if (result == GASAuthorityIdentityMapResult.Created)
                newlyCreatedGrantHandles[newlyCreatedGrantCount++] = localId;
            return result == GASAuthorityIdentityMapResult.Created ||
                   result == GASAuthorityIdentityMapResult.Existing;
        }

        private bool TryGetOrCreateEffectId(int localId, out GASNetworkEffectId effect)
        {
            GASAuthorityIdentityMapResult result = identityMap.GetOrCreateEffectId(localId, out effect);
            if (result == GASAuthorityIdentityMapResult.Created)
                newlyCreatedEffectIds[newlyCreatedEffectCount++] = localId;
            return result == GASAuthorityIdentityMapResult.Created ||
                   result == GASAuthorityIdentityMapResult.Existing;
        }

        private void RollbackNewIdentityBindings()
        {
            for (int i = newlyCreatedGrantCount - 1; i >= 0; i--)
                identityMap.RemoveGrantBySpecHandle(newlyCreatedGrantHandles[i], out _);
            for (int i = newlyCreatedEffectCount - 1; i >= 0; i--)
                identityMap.RemoveEffectByReconciliationId(newlyCreatedEffectIds[i], out _);
            newlyCreatedGrantCount = 0;
            newlyCreatedEffectCount = 0;
        }

        private void RemoveStaleIdentityBindings()
        {
            for (int i = 0; i < lastGrantCount; i++)
            {
                int localId = lastGrantHandles[i];
                if (!ContainsGrant(localId))
                    identityMap.RemoveGrantBySpecHandle(localId, out _);
            }
            for (int i = 0; i < lastEffectCount; i++)
            {
                int localId = lastEffectIds[i];
                if (!ContainsEffect(localId))
                    identityMap.RemoveEffectByReconciliationId(localId, out _);
            }
        }

        private void RememberCurrentIdentityBindings()
        {
            lastGrantCount = localState.GrantedAbilityCount;
            for (int i = 0; i < lastGrantCount; i++)
                lastGrantHandles[i] = localState.GrantedAbilities[i].SpecHandle;
            lastEffectCount = localState.ActiveEffectCount;
            for (int i = 0; i < lastEffectCount; i++)
                lastEffectIds[i] = localState.ActiveEffects[i].ReconciliationId;
        }

        private bool ContainsGrant(int localId)
        {
            for (int i = 0; i < localState.GrantedAbilityCount; i++)
            {
                if (localState.GrantedAbilities[i].SpecHandle == localId)
                    return true;
            }
            return false;
        }

        private bool ContainsEffect(int localId)
        {
            for (int i = 0; i < localState.ActiveEffectCount; i++)
            {
                if (localState.ActiveEffects[i].ReconciliationId == localId)
                    return true;
            }
            return false;
        }

        private static bool ValidatePredictionKey(GASPredictionKey key)
        {
            return !key.IsValid ||
                   (key.Value > 0 && key.InputSequence > 0 &&
                    (uint)key.InputSequence <= GameplayAbilitiesNetworkProtocol.MaxSequence);
        }

        private static bool IsPredictedSource(in GASActiveEffectStateData effect)
        {
            if (!effect.PredictionKey.IsValid ||
                effect.SourceAbilitySpecHandle <= 0 ||
                !(effect.SourceComponent is AbilitySystemComponent source) ||
                source.IsDisposed ||
                !source.TryGetAbilitySpecByHandle(
                    effect.SourceAbilitySpecHandle,
                    out GameplayAbilitySpec spec))
            {
                return false;
            }

            GameplayAbility ability = spec.GetPrimaryInstance();
            return ability != null &&
                   ability.ExecutionPolicy == EAbilityExecutionPolicy.LocalPredicted;
        }

        private static bool ValidateEffectArrays(in GASActiveEffectStateData effect)
        {
            return IsValidCount(effect.SetByCallerTagMagnitudeCount, effect.SetByCallerTagMagnitudes) &&
                   IsValidCount(effect.SetByCallerNameMagnitudeCount, effect.SetByCallerNameMagnitudes) &&
                   IsValidCount(effect.DynamicGrantedTagCount, effect.DynamicGrantedTags) &&
                   IsValidCount(effect.DynamicAssetTagCount, effect.DynamicAssetTags);
        }

        private static bool IsValidCount<T>(int count, T[] values)
        {
            return count >= 0 && (count == 0 || (values != null && values.Length >= count));
        }

        private static bool TryGetTagId(GameplayTag tag, out GASNetworkTagId id)
        {
            if (!tag.IsNone && tag.IsValid)
            {
                ulong stableId = tag.StableId;
                if (stableId != 0UL)
                {
                    id = new GASNetworkTagId(stableId);
                    return true;
                }
            }

            id = default;
            return false;
        }

        private bool ObserveEpochChange()
        {
            uint epoch = identityMap.StreamEpoch;
            if (stateVersion.StreamEpoch != epoch)
                return false;
            if (epoch == observedStreamEpoch)
                return true;

            observedStreamEpoch = epoch;
            publishedState.Clear();
            stagingState.Clear();
            lastGrantCount = 0;
            lastEffectCount = 0;
            lastPublishedWireVersion = 0UL;
            hasPublishedVersion = false;
            return true;
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS authority state adapter is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }
    }
}
