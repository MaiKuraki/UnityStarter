using System;
using System.Threading;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Unique identifier assigned to a client-side predicted activation.
    /// 
    /// When an ability with <see cref="GASNetExecutionPolicy.LocalPredicted"/> activates,
    /// the client generates a new PredictionKey and runs the ability immediately.
    /// All effects applied under this key are tracked as provisional — if the server later
    /// rejects the activation (<see cref="GASAbilitySystemState.RejectPrediction"/>),
    /// every effect and attribute change linked to this key is automatically rolled back.
    /// 
    /// Thread-safe key generation via <see cref="Interlocked.Increment"/> ensures
    /// concurrent predicted activations on different threads never collide.
    /// </summary>
    public readonly struct GASPredictionKey : IEquatable<GASPredictionKey>
    {
        private static int s_NextKey = 1;

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

        public static GASPredictionKey NewKey()
        {
            return NewKey(default, 0);
        }

        /// <summary>
        /// Generates a new thread-safe prediction key.
        /// Wraps around to 1 when approaching int.MaxValue to avoid overflow in long-running sessions.
        /// </summary>
        public static GASPredictionKey NewKey(GASEntityId owner, int inputSequence)
        {
            int key = Interlocked.Increment(ref s_NextKey);
            if (key >= int.MaxValue - 1)
            {
                Interlocked.Exchange(ref s_NextKey, 1);
            }

            return new GASPredictionKey(key, owner, inputSequence);
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
        Confirmed,
        Rejected,
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
        StaleMessage = 1 << 7
    }

    /// <summary>
    /// Tracks the lifecycle of a single in-flight prediction window.
    /// Opened when a client predicts an activation; closed when the server confirms or rejects.
    /// The cumulative effects, attribute snapshots, and cues applied under this window
    /// are counted here so the rollback system knows exactly what to undo.
    /// </summary>
    public struct GASPredictionWindowData
    {
        public GASPredictionKey PredictionKey;
        public GASPredictionKey ParentPredictionKey;
        public GASSpecHandle SpecHandle;
        public int AbilitySpecHandle;
        public int OpenFrame;
        public int TimeoutFrame;
        public int PredictedEffectCount;
        public int PredictedAttributeSnapshotCount;
        public int PredictedGameplayCueCount;
        public int PredictedAbilityTaskCount;
        public GASPredictionWindowStatus Status;
        public int CloseFrame;
        public GASPredictionRollbackFlags RollbackFlags;

        public GASPredictionWindowData(
            GASPredictionKey predictionKey,
            GASPredictionKey parentPredictionKey,
            GASSpecHandle specHandle,
            int abilitySpecHandle,
            int openFrame,
            int timeoutFrame)
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
        public readonly int OpenFrame;
        public readonly int CloseFrame;
        public readonly int TimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly int DurationFrames;

        public GASPredictionTransactionRecord(GASPredictionWindowData window, GASPredictionWindowStatus status, GASPredictionRollbackFlags rollbackFlags, int closeFrame)
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
        public readonly int EarliestTimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly long TotalOpenedCount;
        public readonly long TotalConfirmedCount;
        public readonly long TotalRejectedCount;
        public readonly long TotalTimedOutCount;
        public readonly long StaleConfirmCount;
        public readonly long StaleRejectCount;
        public readonly int ClosedTransactionRecordCount;
        public readonly int ClosedTransactionRecordCapacity;

        public GASPredictionWindowStats(
            int openCount,
            int parentLinkedCount,
            int expirableCount,
            int earliestTimeoutFrame,
            int predictedEffectCount,
            int predictedAttributeSnapshotCount,
            int predictedGameplayCueCount,
            int predictedAbilityTaskCount,
            long totalOpenedCount,
            long totalConfirmedCount,
            long totalRejectedCount,
            long totalTimedOutCount,
            long staleConfirmCount,
            long staleRejectCount,
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
            TotalConfirmedCount = totalConfirmedCount;
            TotalRejectedCount = totalRejectedCount;
            TotalTimedOutCount = totalTimedOutCount;
            StaleConfirmCount = staleConfirmCount;
            StaleRejectCount = staleRejectCount;
            ClosedTransactionRecordCount = closedTransactionRecordCount;
            ClosedTransactionRecordCapacity = closedTransactionRecordCapacity;
        }
    }
}
