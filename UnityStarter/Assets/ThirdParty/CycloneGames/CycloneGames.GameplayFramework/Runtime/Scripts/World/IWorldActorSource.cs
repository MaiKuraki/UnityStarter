namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Bounded, transaction-scoped destination for Actors discovered during World initialization.
    /// The collector must not be retained after <see cref="IWorldActorSource.CollectActors"/> returns.
    /// </summary>
    public interface IWorldActorCollector
    {
        int Count { get; }
        int RemainingCapacity { get; }

        /// <summary>
        /// Adds one candidate. Returns false when the configured World Actor budget is exhausted.
        /// Null, destroyed, already registered, and duplicate Actors are ignored successfully.
        /// </summary>
        bool TryAdd(Actor actor);
    }

    /// <summary>
    /// Supplies externally owned Actors for one World initialization transaction.
    /// </summary>
    public interface IWorldActorSource
    {
        void CollectActors(IWorldActorCollector collector);
    }
}
