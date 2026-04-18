using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Optional service for supplying deterministic or virtualized time to runtime nodes.
    /// Register through RuntimeBTContext.ServiceResolver when UnityEngine.Time should not be used.
    /// </summary>
    public interface IRuntimeBTTimeProvider
    {
        double TimeAsDouble { get; }
        double UnscaledTimeAsDouble { get; }
    }

    /// <summary>
    /// Optional service for supplying deterministic random values to runtime nodes.
    /// </summary>
    public interface IRuntimeBTRandomProvider
    {
        float Range(float minInclusive, float maxInclusive);
    }

    public interface IRuntimeBTContext
    {
        GameObject OwnerGameObject { get; }
        IRuntimeBTServiceResolver ServiceResolver { get; }

        T GetOwner<T>() where T : class;
        T GetService<T>() where T : class;
    }
}
