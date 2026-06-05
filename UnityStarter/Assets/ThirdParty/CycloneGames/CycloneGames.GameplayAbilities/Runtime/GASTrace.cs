using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum GASTraceEventType : byte
    {
        AbilityActivateAttempt,
        AbilityActivateBlocked,
        AbilityActivated,
        AbilityCommitted,
        AbilityEnded,
        AbilityCancelled,
        EffectApplyAttempt,
        EffectApplyBlocked,
        EffectApplied,
        EffectExecuted,
        EffectRemoved,
        PredictionOpened,
        PredictionConfirmed,
        PredictionRejected,
        PredictionTimedOut,
        TargetDataAccepted,
        TargetDataRejected
    }

    public enum GASTraceDecision : byte
    {
        None,
        Success,
        Failed,
        Blocked,
        Rejected,
        TimedOut,
        RolledBack
    }

    public enum GASTraceReason : ushort
    {
        None,
        MissingSpec,
        AlreadyActive,
        MissingAbility,
        CanActivateFailed,
        IsEnding,
        ActivationBlockedTags,
        ActivationRequiredTags,
        SourceRequiredTags,
        SourceBlockedTags,
        BlockedByActiveAbility,
        Cooldown,
        Cost,
        TargetRequiredTags,
        TargetBlockedTags,
        ImmunityAssetTags,
        ImmunityDynamicAssetTags,
        ImmunityDynamicGrantedTags,
        ApplicationRequiredTags,
        ApplicationBlockedTags,
        CustomApplicationRequirement,
        StackingOverflow,
        ServerRejected,
        PredictionTimeout,
        TargetDataValidation
    }

    public struct GASTraceEvent
    {
        public ulong Sequence;
        public int Frame;
        public float Time;
        public GASTraceEventType Type;
        public GASTraceDecision Decision;
        public GASTraceReason Reason;
        public AbilitySystemComponent Target;
        public AbilitySystemComponent Source;
        public GameplayAbility Ability;
        public GameplayEffect Effect;
        public int AbilitySpecHandle;
        public int PredictionKey;
        public int PredictionOwner;
        public int PredictionInputSequence;
        public int Level;
        public int StackCount;
        public int NetworkId;
    }

    public static class GASTrace
    {
        public const int DefaultCapacity = 4096;

        private static GASTraceEvent[] events = new GASTraceEvent[DefaultCapacity];
        private static int writeIndex;
        private static int count;
        private static ulong nextSequence;
        private static bool enabled;

        public static bool Enabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => enabled;
            set => enabled = value;
        }

        public static int Capacity => events.Length;
        public static int Count => count;
        public static ulong LatestSequence => nextSequence;

        public static void SetCapacity(int capacity)
        {
            if (capacity <= 0 || capacity == events.Length)
            {
                return;
            }

            events = new GASTraceEvent[capacity];
            writeIndex = 0;
            count = 0;
            nextSequence = 0;
        }

        public static void Clear()
        {
            Array.Clear(events, 0, events.Length);
            writeIndex = 0;
            count = 0;
            nextSequence = 0;
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("GAS_TRACE")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Record(
            GASTraceEventType type,
            AbilitySystemComponent target,
            GameplayAbility ability = null,
            GameplayEffect effect = null,
            GASTraceDecision decision = GASTraceDecision.None,
            GASTraceReason reason = GASTraceReason.None,
            AbilitySystemComponent source = null,
            int abilitySpecHandle = 0,
            GASPredictionKey predictionKey = default,
            int level = 0,
            int stackCount = 0,
            int networkId = 0)
        {
            if (!enabled)
            {
                return;
            }

            ref var entry = ref events[writeIndex];
            entry.Sequence = ++nextSequence;
            entry.Frame = Time.frameCount;
            entry.Time = Time.unscaledTime;
            entry.Type = type;
            entry.Decision = decision;
            entry.Reason = reason;
            entry.Target = target;
            entry.Source = source;
            entry.Ability = ability;
            entry.Effect = effect;
            entry.AbilitySpecHandle = abilitySpecHandle;
            entry.PredictionKey = predictionKey.Key;
            entry.PredictionOwner = predictionKey.Owner.Value;
            entry.PredictionInputSequence = predictionKey.InputSequence;
            entry.Level = level;
            entry.StackCount = stackCount;
            entry.NetworkId = networkId;

            writeIndex++;
            if (writeIndex == events.Length)
            {
                writeIndex = 0;
            }

            if (count < events.Length)
            {
                count++;
            }
        }

        public static bool TryGetRecent(int recentIndex, out GASTraceEvent traceEvent)
        {
            if (recentIndex < 0 || recentIndex >= count)
            {
                traceEvent = default;
                return false;
            }

            int index = writeIndex - 1 - recentIndex;
            if (index < 0)
            {
                index += events.Length;
            }

            traceEvent = events[index];
            return true;
        }

        public static int CopyRecentNonAlloc(GASTraceEvent[] destination, int maxCount)
        {
            if (destination == null || maxCount <= 0)
            {
                return 0;
            }

            int copyCount = Math.Min(Math.Min(count, destination.Length), maxCount);
            for (int i = 0; i < copyCount; i++)
            {
                TryGetRecent(i, out destination[i]);
            }

            return copyCount;
        }
    }
}
