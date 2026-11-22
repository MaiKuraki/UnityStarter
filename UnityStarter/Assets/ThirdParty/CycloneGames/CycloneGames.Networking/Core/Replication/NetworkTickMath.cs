using System;

namespace CycloneGames.Networking.Replication
{
    /// <summary>
    /// Unified 32-bit modular tick arithmetic for the replication subsystem.
    ///
    /// A server tick is a wrapping counter, not an ever-growing wall-clock value.
    /// Every "how many ticks since the last send / ack" calculation must use
    /// Delta(), which is correct across the int.MaxValue -> int.MinValue wrap.
    /// A plain int subtraction silently produces a large negative number (or an
    /// accidental large positive one) after the wrap, which either freezes an
    /// object forever or corrupts its priority.
    ///
    /// Contract: the true gap between two compared ticks must be less than
    /// 2^31 ticks. At 60 tick/s that is about 414 days between two observations
    /// of the SAME object, which no live replication state can legitimately have.
    /// Products must not compare ticks from different epochs/sessions.
    ///
    /// RollbackNetcode's simulation frame index is a separate contract: it
    /// indexes a ring buffer and fails fast at int.MaxValue.
    ///
    /// Pure value arithmetic with no allocation. unchecked integer semantics
    /// are identical on Mono, CoreCLR and IL2CPP.
    /// </summary>
    public static class NetworkTickMath
    {
        /// <summary>
        /// Signed modular difference currentTick - previousTick, valid when the
        /// true gap is smaller than 2^31 ticks. Positive means current is later.
        /// </summary>
        public static int Delta(int currentTick, int previousTick)
        {
            return unchecked((int)((uint)currentTick - (uint)previousTick));
        }

        /// <summary>True when at least intervalTicks have elapsed in modular order.</summary>
        public static bool HasElapsed(int currentTick, int previousTick, int intervalTicks)
        {
            return Delta(currentTick, previousTick) >= intervalTicks;
        }

        /// <summary>Non-negative elapsed ticks, for scoring without wrap-induced negatives.</summary>
        public static int ElapsedNonNegative(int currentTick, int previousTick)
        {
            int delta = Delta(currentTick, previousTick);
            return delta > 0 ? delta : 0;
        }

        /// <summary>True when candidateTick is strictly later than baselineTick.</summary>
        public static bool IsAfter(int candidateTick, int baselineTick)
        {
            return Delta(candidateTick, baselineTick) > 0;
        }

        /// <summary>
        /// Keeps a stored tick away from a sentinel value (e.g. NEVER_SENT = -1)
        /// so a wrapped tick that happens to equal the sentinel is not mistaken
        /// for "never sent". The introduced one-tick offset is negligible.
        /// </summary>
        public static int AvoidSentinel(int tick, int sentinel)
        {
            return tick == sentinel ? unchecked(tick + 1) : tick;
        }
    }
}
