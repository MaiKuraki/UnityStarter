namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Slot generation counter for handle ABA protection. Zero means "never issued".</summary>
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
