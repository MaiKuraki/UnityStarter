using CycloneGames.BehaviorTree.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime
{
    /// <summary>
    /// Time provider backed by <see cref="Time"/>, so timed nodes follow time scale and pause.
    /// </summary>
    public sealed class UnityBTTimeProvider : IRuntimeBTTimeProvider
    {
        public static readonly UnityBTTimeProvider Instance = new UnityBTTimeProvider();

        private UnityBTTimeProvider()
        {
        }

        public double TimeAsDouble => Time.timeAsDouble;
        public double UnscaledTimeAsDouble => Time.unscaledTimeAsDouble;
    }

    /// <summary>
    /// Unity-side context: resolves components from a <see cref="GameObject"/> owner and supplies
    /// <see cref="UnityBTTimeProvider"/> when the caller registered no time provider. A registered
    /// service always wins over the built-in fallback.
    /// </summary>
    public sealed class UnityBTContext : RuntimeBTContext
    {
        public UnityBTContext(object owner = null, IRuntimeBTServiceResolver serviceResolver = null)
            : base(owner, serviceResolver)
        {
        }

        public override T GetOwner<T>()
        {
            if (Owner is T ownerAsT)
            {
                return ownerAsT;
            }

            if (Owner is GameObject ownerGameObject &&
                ownerGameObject != null &&
                typeof(Component).IsAssignableFrom(typeof(T)))
            {
                return ownerGameObject.GetComponent(typeof(T)) as T;
            }

            return null;
        }

        public override T GetService<T>()
        {
            T service = base.GetService<T>();
            if (service != null)
            {
                return service;
            }

            if (typeof(T) == typeof(IRuntimeBTTimeProvider))
            {
                return (T)(object)UnityBTTimeProvider.Instance;
            }

            return null;
        }
    }
}
