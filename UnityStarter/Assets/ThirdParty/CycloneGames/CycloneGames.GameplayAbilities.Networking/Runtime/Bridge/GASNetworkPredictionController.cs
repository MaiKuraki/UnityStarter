using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Bounded owner-thread correlation between outgoing activation commands and local prediction
    /// windows. Accepted results commit; send failure, rejection, timeout, and reset roll back.
    /// </summary>
    /// <remarks>
    /// The controller creates no threads and takes no locks. Transport results must be marshalled to
    /// the GAS owner thread; WebGL uses the same single-threaded path.
    /// </remarks>
    public sealed class GASNetworkPredictionController : IDisposable
    {
        private readonly AbilitySystemComponent abilitySystem;
        private readonly IGASNetworkGrantResolver grantResolver;
        private readonly GASNetworkEntityId entity;
        private readonly Entry[] entries;
        private readonly ReconciliationEntry[] reconciliationScratch;
        private readonly int ownerThreadId;
        private uint streamEpoch;
        private uint lastReconciledCommandSequence;
        private uint reconciliationCommandSequence;
        private int reconciliationCount;
        private int count;
        private bool coveredPredictionCommitted;
        private bool reconcilingSnapshot;
        private bool disposed;

        private struct Entry
        {
            public uint CommandSequence;
            public GASNetworkGrantId Grant;
            public GASPredictionKey PredictionKey;
        }

        private struct ReconciliationEntry
        {
            public uint CommandSequence;
            public GASNetworkGrantId Grant;
            public GASPredictionKey PredictionKey;
        }

        public GASNetworkPredictionController(
            AbilitySystemComponent abilitySystem,
            GASNetworkEntityId entity,
            uint streamEpoch,
            IGASNetworkGrantResolver grantResolver,
            int capacity = GASAuthorityCommandReplayWindow.DefaultCapacity)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            this.grantResolver = grantResolver ?? throw new ArgumentNullException(nameof(grantResolver));
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            if (capacity <= 0 || capacity > GASAuthorityCommandReplayWindow.MaximumCapacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            this.entity = entity;
            this.streamEpoch = streamEpoch;
            entries = new Entry[capacity];
            reconciliationScratch = new ReconciliationEntry[capacity];
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int OwnerThreadId => ownerThreadId;
        public GASNetworkEntityId Entity => entity;
        public uint StreamEpoch => streamEpoch;
        public int Capacity => entries.Length;
        public int Count => count;
        public bool IsDisposed => disposed;

        /// <summary>
        /// Opens and tracks a local prediction window for one canonical Activate command. The caller
        /// must invoke <see cref="HandleSendFailure"/> if transport enqueue fails.
        /// </summary>
        public bool TryBeginActivation(
            in GASAbilityCommand command,
            GameplayAbilitySpec abilitySpec,
            out GASPredictionKey predictionKey)
        {
            AssertUsable();
            predictionKey = default;
            if (command.StreamEpoch != streamEpoch ||
                command.Entity != entity ||
                command.Kind != GASAbilityCommandKind.Activate ||
                GASNetworkMessageValidator.ValidateHeader(in command) !=
                    GASNetworkMessageValidationResult.Valid ||
                reconcilingSnapshot ||
                command.CommandSequence <= lastReconciledCommandSequence ||
                count >= entries.Length ||
                abilitySpec == null ||
                !ReferenceEquals(abilitySpec.Owner, abilitySystem) ||
                !grantResolver.TryResolveAbilitySpecHandle(
                    entity,
                    streamEpoch,
                    command.Grant,
                    out int resolvedAbilitySpecHandle) ||
                resolvedAbilitySpecHandle != abilitySpec.Handle)
            {
                return false;
            }

            int slot = GetSlot(command.CommandSequence);
            if (entries[slot].CommandSequence != 0u)
                return false;
            if (!abilitySystem.TryActivatePredictedAbility(abilitySpec, out predictionKey) ||
                !predictionKey.IsValid)
            {
                predictionKey = default;
                return false;
            }

            entries[slot] = new Entry
            {
                CommandSequence = command.CommandSequence,
                Grant = command.Grant,
                PredictionKey = predictionKey
            };
            count++;
            return true;
        }

        public bool HandleSendFailure(uint commandSequence)
        {
            AssertUsable();
            return !reconcilingSnapshot && RollbackAndRemove(commandSequence);
        }

        public bool HandleTimeout(uint commandSequence)
        {
            AssertUsable();
            return !reconcilingSnapshot && RollbackAndRemove(commandSequence);
        }

        public bool HandleResult(in GASCommandResult result)
        {
            AssertUsable();
            if (!result.IsValid ||
                result.StreamEpoch != streamEpoch ||
                result.Entity != entity ||
                result.CommandKind != GASAbilityCommandKind.Activate ||
                reconcilingSnapshot)
            {
                return false;
            }

            // A committed authoritative snapshot supersedes its covered terminal results. The
            // result remains safe to deliver after the snapshot because it cannot mutate state.
            if (result.CommandSequence <= lastReconciledCommandSequence)
                return true;

            int slot = GetSlot(result.CommandSequence);
            Entry entry = entries[slot];
            if (entry.CommandSequence != result.CommandSequence || entry.Grant != result.Grant)
                return false;

            bool closed = result.Status == GASCommandStatus.Accepted
                ? abilitySystem.CommitPredictionWindow(entry.PredictionKey)
                : abilitySystem.RollbackPredictionWindow(entry.PredictionKey);
            entries[slot] = default;
            count--;
            return closed;
        }

        /// <summary>
        /// Closes prediction journals before one authoritative snapshot is applied. Covered
        /// commands are committed so accepted long-running abilities keep their local execution;
        /// commands newer than the authority watermark are rolled back for bounded replay.
        /// </summary>
        internal bool TryBeginSnapshotReconciliation(uint lastProcessedCommandSequence)
        {
            AssertUsable();
            if (reconcilingSnapshot ||
                lastProcessedCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence ||
                lastProcessedCommandSequence < lastReconciledCommandSequence)
            {
                return false;
            }

            reconciliationCount = 0;
            coveredPredictionCommitted = false;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry.CommandSequence == 0u)
                    continue;
                if (!entry.PredictionKey.IsValid ||
                    !abilitySystem.HasOpenPredictionWindow(entry.PredictionKey) ||
                    reconciliationCount >= reconciliationScratch.Length)
                {
                    ClearTrackedPredictions();
                    return false;
                }

                reconciliationScratch[reconciliationCount++] = new ReconciliationEntry
                {
                    CommandSequence = entry.CommandSequence,
                    Grant = entry.Grant,
                    PredictionKey = entry.PredictionKey
                };
            }

            if (reconciliationCount != count)
            {
                ClearTrackedPredictions();
                return false;
            }
            if (abilitySystem.OpenPredictionWindowCount != count)
            {
                // A different owner has open prediction state. Reject before mutating either
                // owner's journals; applying the snapshot without that owner would stale it.
                ClearReconciliationScratch();
                return false;
            }

            SortReconciliationScratch(reconciliationCount);
            reconciliationCommandSequence = lastProcessedCommandSequence;
            reconcilingSnapshot = true;
            bool runtimeMutated = false;

            // Covered commands are committed oldest-first. Committing a parent prediction does
            // not close its dependants, so newer pending predictions remain available to roll back.
            for (int i = 0; i < reconciliationCount; i++)
            {
                ReconciliationEntry entry = reconciliationScratch[i];
                if (entry.CommandSequence > lastProcessedCommandSequence)
                    break;
                if (!abilitySystem.CommitPredictionWindow(entry.PredictionKey))
                {
                    FailSnapshotReconciliation(runtimeMutated);
                    return false;
                }

                runtimeMutated = true;
                coveredPredictionCommitted = true;
                entries[GetSlot(entry.CommandSequence)] = default;
                count--;
            }

            // Roll back newest-first so a dependent prediction cannot be closed implicitly before
            // its own journal is visited.
            for (int i = reconciliationCount - 1; i >= 0; i--)
            {
                ReconciliationEntry entry = reconciliationScratch[i];
                if (entry.CommandSequence <= lastProcessedCommandSequence)
                    break;
                if (!abilitySystem.RollbackPredictionWindow(entry.PredictionKey))
                {
                    FailSnapshotReconciliation(runtimeMutated);
                    return false;
                }

                runtimeMutated = true;
                int slot = GetSlot(entry.CommandSequence);
                Entry suspended = entries[slot];
                suspended.PredictionKey = default;
                entries[slot] = suspended;
            }

            if (abilitySystem.OpenPredictionWindowCount != 0)
            {
                FailSnapshotReconciliation(runtimeMutated);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Completes one snapshot transaction. A successful snapshot reopens only commands newer
        /// than its authority watermark. Any failure clears the bounded prediction set fail-closed.
        /// </summary>
        internal bool CompleteSnapshotReconciliation(bool snapshotApplied)
        {
            AssertUsable();
            if (!reconcilingSnapshot)
                return false;

            bool success = snapshotApplied;
            try
            {
                if (!snapshotApplied)
                {
                    if (coveredPredictionCommitted)
                    {
                        abilitySystem.RequireFullStateResynchronization();
                        ClearTrackedEntries();
                    }
                    else
                    {
                        ClearTrackedPredictions();
                    }
                    return true;
                }

                lastReconciledCommandSequence = reconciliationCommandSequence;
                for (int i = 0; i < reconciliationCount; i++)
                {
                    ReconciliationEntry replay = reconciliationScratch[i];
                    if (replay.CommandSequence <= reconciliationCommandSequence)
                        continue;
                    if (!grantResolver.TryResolveAbilitySpecHandle(
                            entity,
                            streamEpoch,
                            replay.Grant,
                            out int abilitySpecHandle) ||
                        abilitySpecHandle <= 0 ||
                        !abilitySystem.TryGetAbilitySpecByHandle(
                            abilitySpecHandle,
                            out GameplayAbilitySpec abilitySpec) ||
                        !abilitySystem.TryActivatePredictedAbility(
                            abilitySpec,
                            out GASPredictionKey predictionKey) ||
                        !predictionKey.IsValid ||
                        !abilitySystem.HasOpenPredictionWindow(predictionKey))
                    {
                        success = false;
                        break;
                    }

                    int slot = GetSlot(replay.CommandSequence);
                    Entry reopened = entries[slot];
                    if (reopened.CommandSequence != replay.CommandSequence ||
                        reopened.Grant != replay.Grant)
                    {
                        abilitySystem.RollbackPredictionWindow(predictionKey);
                        success = false;
                        break;
                    }
                    reopened.PredictionKey = predictionKey;
                    entries[slot] = reopened;
                }

                if (success && !ValidateTrackedPredictionWindows())
                    success = false;

                if (!success)
                {
                    abilitySystem.RequireFullStateResynchronization();
                    ClearTrackedEntries();
                }
                return success;
            }
            catch (Exception)
            {
                abilitySystem.RequireFullStateResynchronization();
                ClearTrackedEntries();
                return false;
            }
            finally
            {
                ClearReconciliationScratch();
                reconciliationCommandSequence = 0u;
                coveredPredictionCommitted = false;
                reconcilingSnapshot = false;
            }
        }

        internal bool CanCoordinateSnapshot(
            AbilitySystemComponent candidate,
            GASNetworkEntityId candidateEntity,
            uint candidateStreamEpoch)
        {
            AssertOwnerThread();
            return !disposed &&
                   !reconcilingSnapshot &&
                   ReferenceEquals(candidate, abilitySystem) &&
                   candidateEntity == entity &&
                   candidateStreamEpoch == streamEpoch;
        }

        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertUsable();
            if (reconcilingSnapshot)
            {
                throw new InvalidOperationException(
                    "The prediction controller cannot reset its epoch during snapshot reconciliation.");
            }
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));
            RollbackAll();
            ClearReconciliationScratch();
            reconcilingSnapshot = false;
            reconciliationCommandSequence = 0u;
            coveredPredictionCommitted = false;
            lastReconciledCommandSequence = 0u;
            streamEpoch = newStreamEpoch;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            AssertOwnerThread();
            if (reconcilingSnapshot)
            {
                throw new InvalidOperationException(
                    "The prediction controller cannot be disposed during snapshot reconciliation.");
            }
            RollbackAll();
            ClearReconciliationScratch();
            reconcilingSnapshot = false;
            reconciliationCommandSequence = 0u;
            coveredPredictionCommitted = false;
            disposed = true;
        }

        private bool RollbackAndRemove(uint commandSequence)
        {
            if (commandSequence == 0u ||
                commandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                return false;
            }

            int slot = GetSlot(commandSequence);
            Entry entry = entries[slot];
            if (entry.CommandSequence != commandSequence)
                return false;

            bool closed = abilitySystem.RollbackPredictionWindow(entry.PredictionKey);
            entries[slot] = default;
            count--;
            return closed;
        }

        private void RollbackAll()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry.CommandSequence != 0u && entry.PredictionKey.IsValid)
                    abilitySystem.RollbackPredictionWindow(entry.PredictionKey);
                entries[i] = default;
            }
            count = 0;
        }

        private void ClearTrackedPredictions()
        {
            RollbackAll();
            ClearReconciliationScratch();
        }

        private void ClearTrackedEntries()
        {
            for (int i = 0; i < entries.Length; i++)
                entries[i] = default;
            count = 0;
            ClearReconciliationScratch();
        }

        private void FailSnapshotReconciliation(bool runtimeMutated)
        {
            if (runtimeMutated)
            {
                abilitySystem.RequireFullStateResynchronization();
                ClearTrackedEntries();
            }
            else
            {
                ClearTrackedPredictions();
            }
            reconciliationCommandSequence = 0u;
            coveredPredictionCommitted = false;
            reconcilingSnapshot = false;
        }

        private void ClearReconciliationScratch()
        {
            for (int i = 0; i < reconciliationCount; i++)
                reconciliationScratch[i] = default;
            reconciliationCount = 0;
        }

        private void SortReconciliationScratch(int itemCount)
        {
            // Snapshot reconciliation is a bounded cold path. Insertion sort avoids comparer,
            // delegate, and temporary-array allocations while preserving deterministic order.
            for (int i = 1; i < itemCount; i++)
            {
                ReconciliationEntry value = reconciliationScratch[i];
                int j = i - 1;
                while (j >= 0 && reconciliationScratch[j].CommandSequence > value.CommandSequence)
                {
                    reconciliationScratch[j + 1] = reconciliationScratch[j];
                    j--;
                }
                reconciliationScratch[j + 1] = value;
            }
        }

        private bool ValidateTrackedPredictionWindows()
        {
            if (abilitySystem.OpenPredictionWindowCount != count)
                return false;

            int trackedCount = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry.CommandSequence == 0u)
                    continue;
                if (!entry.PredictionKey.IsValid ||
                    !abilitySystem.HasOpenPredictionWindow(entry.PredictionKey))
                {
                    return false;
                }
                trackedCount++;
            }
            return trackedCount == count;
        }

        private int GetSlot(uint commandSequence)
        {
            return (int)((commandSequence - 1u) % (uint)entries.Length);
        }

        private void AssertUsable()
        {
            AssertOwnerThread();
            if (disposed)
                throw new ObjectDisposedException(nameof(GASNetworkPredictionController));
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS prediction controller is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }
    }
}
