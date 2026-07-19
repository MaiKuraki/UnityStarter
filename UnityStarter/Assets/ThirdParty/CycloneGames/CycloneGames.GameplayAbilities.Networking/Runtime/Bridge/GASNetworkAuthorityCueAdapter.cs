using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Persistent fail-closed condition of an authority cue adapter.</summary>
    public enum GASNetworkAuthorityCueFault : byte
    {
        None = 0,
        QueueCapacityExceeded = 1,
        InvalidCommittedCue = 2,
        SourceEntityUnavailable = 3,
        IdentityEpochMismatch = 4,
        StateVersionExhausted = 5
    }

    /// <summary>Outcome of preparing the queue head for transport.</summary>
    public enum GASNetworkAuthorityCuePrepareResult : byte
    {
        Invalid = 0,
        Prepared = 1,
        Empty = 2,
        Faulted = 3,
        SequenceExhausted = 4
    }

    /// <summary>
    /// Converts committed Gameplay Cues from one authority ASC into a bounded, backend-neutral wire
    /// queue. The adapter is owner-thread only, creates no threads, takes no locks, and performs no
    /// managed allocation after construction and subscription.
    /// </summary>
    public sealed class GASNetworkAuthorityCueAdapter : IDisposable
    {
        public const int DefaultCueCapacity = 64;
        public const int MaximumCueCapacity = 4096;

        private readonly AbilitySystemComponent abilitySystem;
        private readonly IGASNetworkEntityResolver entityResolver;
        private readonly GASAuthorityIdentityMap identityMap;
        private readonly GASNetworkStateVersion stateVersion;
        private readonly PendingCue[] pendingCues;
        private readonly GameplayCueCommittedDelegate committedCueHandler;
        private readonly GASNetworkEntityId entity;
        private readonly int ownerThreadId;

        private uint streamEpoch;
        private uint nextCueSequence;
        private int head;
        private int count;
        private long droppedCueCount;
        private GASNetworkAuthorityCueFault fault;
        private bool hasPreparedCue;
        private bool disposed;
        private GASCueExecuted preparedCue;

        private struct PendingCue
        {
            public GASNetworkTagId Cue;
            public GASNetworkEntityId Instigator;
            public GASNetworkEffectId SourceEffect;
            public uint SourceCommandSequence;
            public ulong StateVersion;
            public GASCueEvent Event;
            public GASCueFlags Flags;
            public int ActiveEffectReconciliationId;
        }

        public GASNetworkAuthorityCueAdapter(
            AbilitySystemComponent abilitySystem,
            IGASNetworkEntityResolver entityResolver,
            GASAuthorityIdentityMap identityMap,
            GASNetworkStateVersion stateVersion,
            int cueCapacity = DefaultCueCapacity,
            uint firstCueSequence = 1u)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            this.entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
            this.identityMap = identityMap ?? throw new ArgumentNullException(nameof(identityMap));
            this.stateVersion = stateVersion ?? throw new ArgumentNullException(nameof(stateVersion));
            if (cueCapacity <= 0 || cueCapacity > MaximumCueCapacity)
                throw new ArgumentOutOfRangeException(nameof(cueCapacity));
            if (!IsValidSequence(firstCueSequence))
                throw new ArgumentOutOfRangeException(nameof(firstCueSequence));
            if (!abilitySystem.RuntimeContext.HasAuthority)
                throw new ArgumentException("The cue adapter requires an authority AbilitySystemComponent.", nameof(abilitySystem));
            if (!identityMap.IsOwnerThread)
                throw new InvalidOperationException("The authority identity map must be owned by the cue adapter thread.");
            if (!stateVersion.IsOwnerThread)
                throw new InvalidOperationException("The network state version must be owned by the cue adapter thread.");
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

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            entity = identityMap.Entity;
            streamEpoch = identityMap.StreamEpoch;
            nextCueSequence = firstCueSequence;
            pendingCues = new PendingCue[cueCapacity];

            if (!TryResolveEntity(abilitySystem, out GASNetworkEntityId resolvedEntity) ||
                resolvedEntity != entity)
            {
                throw new ArgumentException(
                    "The entity resolver must map the authority AbilitySystemComponent to the identity map entity.",
                    nameof(entityResolver));
            }

            committedCueHandler = OnGameplayCueCommitted;
            abilitySystem.OnGameplayCueCommitted += committedCueHandler;
        }

        public int OwnerThreadId => ownerThreadId;
        public GASNetworkEntityId Entity => entity;
        public int Capacity => pendingCues.Length;
        public bool IsDisposed => disposed;

        public uint StreamEpoch
        {
            get
            {
                AssertUsable();
                return streamEpoch;
            }
        }

        public uint NextCueSequence
        {
            get
            {
                AssertUsable();
                return nextCueSequence;
            }
        }

        public int Count
        {
            get
            {
                AssertUsable();
                return count;
            }
        }

        public bool HasPreparedCue
        {
            get
            {
                AssertUsable();
                return hasPreparedCue;
            }
        }

        public GASNetworkAuthorityCueFault Fault
        {
            get
            {
                AssertUsable();
                return fault;
            }
        }

        public bool IsFaulted
        {
            get
            {
                AssertUsable();
                return fault != GASNetworkAuthorityCueFault.None;
            }
        }

        public long DroppedCueCount
        {
            get
            {
                AssertUsable();
                return droppedCueCount;
            }
        }

        /// <summary>
        /// Returns an idempotent wire view of the queue head. A transport must call
        /// <see cref="CommitPrepared"/> only after ownership of the message has been accepted.
        /// </summary>
        public GASNetworkAuthorityCuePrepareResult PrepareNext(out GASCueExecuted cue)
        {
            AssertUsable();
            cue = default;
            if (!EnsureIdentityEpoch())
                return GASNetworkAuthorityCuePrepareResult.Faulted;
            if (fault != GASNetworkAuthorityCueFault.None)
                return GASNetworkAuthorityCuePrepareResult.Faulted;
            if (hasPreparedCue)
            {
                cue = preparedCue;
                return GASNetworkAuthorityCuePrepareResult.Prepared;
            }
            if (count == 0)
                return GASNetworkAuthorityCuePrepareResult.Empty;
            if (nextCueSequence == 0u)
                return GASNetworkAuthorityCuePrepareResult.SequenceExhausted;

            ref PendingCue pending = ref pendingCues[head];
            if (!pending.SourceEffect.IsValid && pending.ActiveEffectReconciliationId > 0 &&
                identityMap.TryGetEffectId(
                    pending.ActiveEffectReconciliationId,
                    out GASNetworkEffectId sourceEffect))
            {
                pending.SourceEffect = sourceEffect;
            }

            preparedCue = new GASCueExecuted(
                streamEpoch,
                nextCueSequence,
                entity,
                pending.Cue,
                pending.Instigator,
                pending.SourceEffect,
                pending.SourceCommandSequence,
                pending.StateVersion,
                pending.Event,
                pending.Flags,
                magnitude: 0f,
                location: default,
                normal: default);
            if (GASNetworkMessageValidator.Validate(in preparedCue) !=
                GASNetworkMessageValidationResult.Valid)
            {
                SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                preparedCue = default;
                return GASNetworkAuthorityCuePrepareResult.Faulted;
            }

            hasPreparedCue = true;
            cue = preparedCue;
            return GASNetworkAuthorityCuePrepareResult.Prepared;
        }

        /// <summary>Commits the prepared head after a successful transport handoff.</summary>
        public bool CommitPrepared(uint cueSequence)
        {
            AssertUsable();
            if (!EnsureIdentityEpoch() ||
                fault != GASNetworkAuthorityCueFault.None ||
                !hasPreparedCue ||
                preparedCue.CueSequence != cueSequence)
            {
                return false;
            }

            pendingCues[head] = default;
            head++;
            if (head == pendingCues.Length)
                head = 0;
            count--;
            hasPreparedCue = false;
            preparedCue = default;
            nextCueSequence = nextCueSequence == GameplayAbilitiesNetworkProtocol.MaxSequence
                ? 0u
                : nextCueSequence + 1u;
            return true;
        }

        /// <summary>Keeps the queue head for retry and invalidates only the prepared wire view.</summary>
        public bool RejectPrepared()
        {
            AssertUsable();
            if (!hasPreparedCue)
                return false;

            hasPreparedCue = false;
            preparedCue = default;
            return true;
        }

        /// <summary>
        /// Clears queued work after the shared identity map has already moved to a new stream epoch.
        /// </summary>
        public void ResetEpoch(uint newStreamEpoch, uint firstCueSequence = 1u)
        {
            AssertUsable();
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));
            if (!IsValidSequence(firstCueSequence))
                throw new ArgumentOutOfRangeException(nameof(firstCueSequence));
            if (identityMap.StreamEpoch != newStreamEpoch)
            {
                throw new InvalidOperationException(
                    "Reset the shared GASAuthorityIdentityMap to the new epoch before resetting the cue adapter.");
            }
            if (stateVersion.StreamEpoch != newStreamEpoch)
            {
                throw new InvalidOperationException(
                    "Reset the shared GASNetworkStateVersion to the new epoch before resetting the cue adapter.");
            }

            Array.Clear(pendingCues, 0, pendingCues.Length);
            streamEpoch = newStreamEpoch;
            nextCueSequence = firstCueSequence;
            head = 0;
            count = 0;
            droppedCueCount = 0L;
            fault = GASNetworkAuthorityCueFault.None;
            hasPreparedCue = false;
            preparedCue = default;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            AssertOwnerThread();
            if (!abilitySystem.IsDisposed)
            {
                abilitySystem.OnGameplayCueCommitted -= committedCueHandler;
            }
            Array.Clear(pendingCues, 0, pendingCues.Length);
            head = 0;
            count = 0;
            hasPreparedCue = false;
            preparedCue = default;
            disposed = true;
        }

        private void OnGameplayCueCommitted(in GameplayCueCommitted cue)
        {
            if (disposed)
                return;
            if (fault != GASNetworkAuthorityCueFault.None)
            {
                IncrementDroppedCueCount();
                return;
            }
            if (!EnsureIdentityEpoch())
            {
                IncrementDroppedCueCount();
                return;
            }
            if (count >= pendingCues.Length)
            {
                SetFault(GASNetworkAuthorityCueFault.QueueCapacityExceeded);
                IncrementDroppedCueCount();
                return;
            }
            if (!ReferenceEquals(cue.Target, abilitySystem) ||
                !cue.Cue.IsValid ||
                cue.Cue.IsNone ||
                cue.ActiveEffectReconciliationId < 0 ||
                cue.SourceAbilitySpecHandle < 0 ||
                !IsCanonicalPredictionKey(cue.PredictionKey) ||
                !TryMapEvent(cue.Event, out GASCueEvent cueEvent))
            {
                SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                IncrementDroppedCueCount();
                return;
            }
            if (RequiresActiveEffectIdentity(cueEvent) &&
                cue.ActiveEffectReconciliationId == 0)
            {
                SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                IncrementDroppedCueCount();
                return;
            }

            if (!stateVersion.TryObserveLocalStateVersion(
                    cue.StateVersion,
                    out ulong wireStateVersion))
            {
                SetFault(GASNetworkAuthorityCueFault.StateVersionExhausted);
                IncrementDroppedCueCount();
                return;
            }

            ulong stableCueId;
            try
            {
                stableCueId = cue.Cue.StableId;
            }
            catch (InvalidOperationException)
            {
                SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                IncrementDroppedCueCount();
                return;
            }
            if (stableCueId == 0UL)
            {
                SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                IncrementDroppedCueCount();
                return;
            }

            GASNetworkEntityId instigator = default;
            if (cue.Source != null)
            {
                if (ReferenceEquals(cue.Source, abilitySystem))
                {
                    instigator = entity;
                }
                else if (!TryResolveEntity(cue.Source, out instigator))
                {
                    SetFault(GASNetworkAuthorityCueFault.SourceEntityUnavailable);
                    IncrementDroppedCueCount();
                    return;
                }
            }

            uint sourceCommandSequence = 0u;
            int inputSequence = cue.PredictionKey.InputSequence;
            if (inputSequence > 0 &&
                (uint)inputSequence <= GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                sourceCommandSequence = (uint)inputSequence;
            }

            GASCueFlags flags = GASCueFlags.None;
            if (cue.SourceAbilityExecutionPolicy == EAbilityExecutionPolicy.LocalPredicted &&
                cue.PredictionKey.IsValid)
            {
                if (sourceCommandSequence == 0u)
                {
                    SetFault(GASNetworkAuthorityCueFault.InvalidCommittedCue);
                    IncrementDroppedCueCount();
                    return;
                }
                flags |= GASCueFlags.Predicted;
            }

            GASNetworkEffectId sourceEffect = default;
            if (cue.ActiveEffectReconciliationId > 0)
            {
                identityMap.TryGetEffectId(
                    cue.ActiveEffectReconciliationId,
                    out sourceEffect);
            }

            int tail = head + count;
            if (tail >= pendingCues.Length)
                tail -= pendingCues.Length;
            pendingCues[tail] = new PendingCue
            {
                Cue = new GASNetworkTagId(stableCueId),
                Instigator = instigator,
                SourceEffect = sourceEffect,
                SourceCommandSequence = sourceCommandSequence,
                StateVersion = wireStateVersion,
                Event = cueEvent,
                Flags = flags,
                ActiveEffectReconciliationId = cue.ActiveEffectReconciliationId
            };
            count++;
        }

        private bool EnsureIdentityEpoch()
        {
            if (identityMap.StreamEpoch == streamEpoch &&
                stateVersion.StreamEpoch == streamEpoch)
                return true;

            SetFault(GASNetworkAuthorityCueFault.IdentityEpochMismatch);
            return false;
        }

        private bool TryResolveEntity(
            AbilitySystemComponent source,
            out GASNetworkEntityId resolvedEntity)
        {
            try
            {
                return entityResolver.TryGetNetworkEntityId(source, out resolvedEntity) &&
                       resolvedEntity.IsValid;
            }
            catch (Exception)
            {
                resolvedEntity = default;
                return false;
            }
        }

        private void SetFault(GASNetworkAuthorityCueFault value)
        {
            if (fault == GASNetworkAuthorityCueFault.None)
                fault = value;
        }

        private void IncrementDroppedCueCount()
        {
            if (droppedCueCount < long.MaxValue)
                droppedCueCount++;
        }

        private void AssertUsable()
        {
            AssertOwnerThread();
            if (disposed)
                throw new ObjectDisposedException(nameof(GASNetworkAuthorityCueAdapter));
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS authority cue adapter is owned by thread {ownerThreadId} and cannot be accessed from thread {Thread.CurrentThread.ManagedThreadId}.");
            }
        }

        private static bool TryMapEvent(EGameplayCueEvent source, out GASCueEvent destination)
        {
            switch (source)
            {
                case EGameplayCueEvent.Executed:
                    destination = GASCueEvent.Execute;
                    return true;
                case EGameplayCueEvent.OnActive:
                    destination = GASCueEvent.OnActive;
                    return true;
                case EGameplayCueEvent.WhileActive:
                    destination = GASCueEvent.WhileActive;
                    return true;
                case EGameplayCueEvent.Removed:
                    destination = GASCueEvent.Removed;
                    return true;
                default:
                    destination = GASCueEvent.Invalid;
                    return false;
            }
        }

        private static bool RequiresActiveEffectIdentity(GASCueEvent cueEvent)
        {
            return cueEvent == GASCueEvent.OnActive ||
                   cueEvent == GASCueEvent.WhileActive ||
                   cueEvent == GASCueEvent.Removed;
        }

        private static bool IsCanonicalPredictionKey(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return predictionKey.Value == 0 &&
                       !predictionKey.Owner.IsValid &&
                       predictionKey.InputSequence == 0;
            }

            return predictionKey.Value > 0 &&
                   predictionKey.Owner.IsValid &&
                   predictionKey.InputSequence > 0;
        }

        private static bool IsValidSequence(uint value)
        {
            return value > 0u && value <= GameplayAbilitiesNetworkProtocol.MaxSequence;
        }
    }
}
