using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// ASC-scoped identity for one local optimistic transaction.
    /// The owning runtime creates keys and uses them to associate provisional effects,
    /// attribute snapshots, gameplay cues, and ability tasks with one commit or rollback boundary.
    /// </summary>
    public readonly struct GASPredictionKey : IEquatable<GASPredictionKey>
    {
        public readonly int Value;
        public readonly GASEntityId Owner;
        public readonly int InputSequence;
        public int Key => Value;
        public bool IsValid => Value != 0;

        public GASPredictionKey(int value)
            : this(value, default, 0)
        {
        }

        public GASPredictionKey(int value, GASEntityId owner, int inputSequence)
        {
            Value = value;
            Owner = owner;
            InputSequence = inputSequence;
        }

        public bool Equals(GASPredictionKey other)
        {
            return Value == other.Value && Owner == other.Owner && InputSequence == other.InputSequence;
        }

        public override bool Equals(object obj) => obj is GASPredictionKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Value;
                hash = (hash * 397) ^ Owner.Value;
                hash = (hash * 397) ^ InputSequence;
                return hash;
            }
        }

        public static bool operator ==(GASPredictionKey left, GASPredictionKey right) => left.Equals(right);
        public static bool operator !=(GASPredictionKey left, GASPredictionKey right) => !left.Equals(right);
    }

    public enum GASPredictionWindowStatus : byte
    {
        Open,
        Committed,
        RolledBack,
        TimedOut
    }

    [Flags]
    public enum GASPredictionRollbackFlags : ushort
    {
        None = 0,
        CorePrediction = 1 << 0,
        ActiveEffects = 1 << 1,
        AttributeSnapshots = 1 << 2,
        GameplayCues = 1 << 3,
        AbilityTasks = 1 << 4,
        AbilityCancelled = 1 << 5,
        DependentWindows = 1 << 6,
        StaleClosure = 1 << 7
    }

    /// <summary>
    /// Tracks one local optimistic transaction from opening through commit, rollback, or timeout.
    /// The cumulative effects, attribute snapshots, cues, and tasks applied under this window are
    /// counted so the owner can release or roll back exactly the provisional state it created.
    /// </summary>
    public struct GASPredictionWindowData
    {
        public GASPredictionKey PredictionKey;
        public GASPredictionKey ParentPredictionKey;
        public GASSpecHandle SpecHandle;
        public int AbilitySpecHandle;
        public long OpenFrame;
        public long TimeoutFrame;
        public int PredictedEffectCount;
        public int PredictedAttributeSnapshotCount;
        public int PredictedGameplayCueCount;
        public int PredictedAbilityTaskCount;
        public GASPredictionWindowStatus Status;
        public long CloseFrame;
        public GASPredictionRollbackFlags RollbackFlags;

        public GASPredictionWindowData(
            GASPredictionKey predictionKey,
            GASPredictionKey parentPredictionKey,
            GASSpecHandle specHandle,
            int abilitySpecHandle,
            long openFrame,
            long timeoutFrame)
        {
            PredictionKey = predictionKey;
            ParentPredictionKey = parentPredictionKey;
            SpecHandle = specHandle;
            AbilitySpecHandle = abilitySpecHandle;
            OpenFrame = openFrame;
            TimeoutFrame = timeoutFrame;
            PredictedEffectCount = 0;
            PredictedAttributeSnapshotCount = 0;
            PredictedGameplayCueCount = 0;
            PredictedAbilityTaskCount = 0;
            Status = GASPredictionWindowStatus.Open;
            CloseFrame = 0;
            RollbackFlags = GASPredictionRollbackFlags.None;
        }
    }

    public readonly struct GASPredictionTransactionRecord
    {
        public readonly GASPredictionKey PredictionKey;
        public readonly GASPredictionKey ParentPredictionKey;
        public readonly GASSpecHandle SpecHandle;
        public readonly int AbilitySpecHandle;
        public readonly GASPredictionWindowStatus Status;
        public readonly GASPredictionRollbackFlags RollbackFlags;
        public readonly long OpenFrame;
        public readonly long CloseFrame;
        public readonly long TimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly long DurationFrames;

        public GASPredictionTransactionRecord(GASPredictionWindowData window, GASPredictionWindowStatus status, GASPredictionRollbackFlags rollbackFlags, long closeFrame)
        {
            PredictionKey = window.PredictionKey;
            ParentPredictionKey = window.ParentPredictionKey;
            SpecHandle = window.SpecHandle;
            AbilitySpecHandle = window.AbilitySpecHandle;
            Status = status;
            RollbackFlags = rollbackFlags;
            OpenFrame = window.OpenFrame;
            CloseFrame = closeFrame;
            TimeoutFrame = window.TimeoutFrame;
            PredictedEffectCount = window.PredictedEffectCount;
            PredictedAttributeSnapshotCount = window.PredictedAttributeSnapshotCount;
            PredictedGameplayCueCount = window.PredictedGameplayCueCount;
            PredictedAbilityTaskCount = window.PredictedAbilityTaskCount;
            DurationFrames = closeFrame > 0 && window.OpenFrame > 0 ? closeFrame - window.OpenFrame : 0;
        }
    }

    public readonly struct GASPredictionWindowStats
    {
        public readonly int OpenCount;
        public readonly int ParentLinkedCount;
        public readonly int ExpirableCount;
        public readonly long EarliestTimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly long TotalOpenedCount;
        public readonly long TotalCommittedCount;
        public readonly long TotalRolledBackCount;
        public readonly long TotalTimedOutCount;
        public readonly long StaleCommitCount;
        public readonly long StaleRollbackCount;
        public readonly int ClosedTransactionRecordCount;
        public readonly int ClosedTransactionRecordCapacity;

        public GASPredictionWindowStats(
            int openCount,
            int parentLinkedCount,
            int expirableCount,
            long earliestTimeoutFrame,
            int predictedEffectCount,
            int predictedAttributeSnapshotCount,
            int predictedGameplayCueCount,
            int predictedAbilityTaskCount,
            long totalOpenedCount,
            long totalCommittedCount,
            long totalRolledBackCount,
            long totalTimedOutCount,
            long staleCommitCount,
            long staleRollbackCount,
            int closedTransactionRecordCount,
            int closedTransactionRecordCapacity)
        {
            OpenCount = openCount;
            ParentLinkedCount = parentLinkedCount;
            ExpirableCount = expirableCount;
            EarliestTimeoutFrame = earliestTimeoutFrame;
            PredictedEffectCount = predictedEffectCount;
            PredictedAttributeSnapshotCount = predictedAttributeSnapshotCount;
            PredictedGameplayCueCount = predictedGameplayCueCount;
            PredictedAbilityTaskCount = predictedAbilityTaskCount;
            TotalOpenedCount = totalOpenedCount;
            TotalCommittedCount = totalCommittedCount;
            TotalRolledBackCount = totalRolledBackCount;
            TotalTimedOutCount = totalTimedOutCount;
            StaleCommitCount = staleCommitCount;
            StaleRollbackCount = staleRollbackCount;
            ClosedTransactionRecordCount = closedTransactionRecordCount;
            ClosedTransactionRecordCapacity = closedTransactionRecordCapacity;
        }
    }
}
