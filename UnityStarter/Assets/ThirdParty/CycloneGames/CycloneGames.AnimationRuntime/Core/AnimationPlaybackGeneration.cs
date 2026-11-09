namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Slot generation counter. A handle carries the generation it was issued with; a slot that is released and
    /// reused gets a new value, so a stale handle can never address the new occupant.
    ///
    /// The counter is 32-bit and skips zero (zero means "never issued"). A 16-bit counter would wrap after
    /// 65 535 reuses of one slot, at which point a handle from the first cycle would validate again (ABA).
    /// </summary>
    public static class AnimationPlaybackGeneration
    {
        /// <summary>Advances a slot generation, skipping zero. <paramref name="wrapped"/> is true once in 4.29 B calls.</summary>
        public static uint Next(uint current, out bool wrapped)
        {
            uint next = unchecked(current + 1u);
            wrapped = next == 0u;
            return wrapped ? 1u : next;
        }
    }
}
