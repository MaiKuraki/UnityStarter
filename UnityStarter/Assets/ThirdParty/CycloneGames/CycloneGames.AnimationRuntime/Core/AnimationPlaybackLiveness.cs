namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// The single liveness predicate for a playback handle. Kept engine-free so the ABA behaviour can be verified
    /// headlessly, and used by the runtime graph so the verified code is the shipped code.
    /// </summary>
    public static class AnimationPlaybackLiveness
    {
        /// <summary>
        /// A handle addresses a live playback only when the graph, the slot generation and the in-use flag all
        /// still match. <paramref name="generation"/> is the slot's current generation, not the handle's.
        /// </summary>
        public static bool IsAlive(in AnimationPlaybackHandle handle, int graphId, uint generation, bool inUse)
        {
            return handle.IsValid
                && handle.GraphId == graphId
                && inUse
                && handle.Generation == generation;
        }
    }
}
