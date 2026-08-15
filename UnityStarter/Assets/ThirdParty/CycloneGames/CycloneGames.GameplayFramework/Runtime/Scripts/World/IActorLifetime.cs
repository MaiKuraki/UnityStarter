namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Creates Actor instances and permanently releases them on the Unity main thread.
    /// World owns every instance returned by <see cref="Create{T}"/> until it calls
    /// <see cref="Release"/> exactly once.
    /// </summary>
    public interface IActorLifetime
    {
        /// <summary>
        /// Creates an independent Actor instance from a prefab.
        /// </summary>
        /// <typeparam name="T">The concrete Actor type.</typeparam>
        /// <param name="prefab">The Actor prefab to instantiate.</param>
        /// <returns>A newly owned Actor instance.</returns>
        /// <remarks>
        /// An implementation that throws must reclaim every partially created resource before
        /// returning control because no Actor instance transfers to the caller.
        /// </remarks>
        T Create<T>(T prefab) where T : Actor;

        /// <summary>
        /// Permanently releases an Actor. The instance must never be returned for reuse after
        /// this call. Implementations must accept an Actor that destroyed itself during EndPlay.
        /// </summary>
        /// <param name="actor">The Actor whose lifetime has ended.</param>
        /// <remarks>
        /// World invokes this operation once for each successfully returned Actor and never
        /// retries it after an exception. Implementations must transfer or terminate their
        /// ownership before executing failure-prone callbacks.
        /// </remarks>
        void Release(Actor actor);
    }
}
