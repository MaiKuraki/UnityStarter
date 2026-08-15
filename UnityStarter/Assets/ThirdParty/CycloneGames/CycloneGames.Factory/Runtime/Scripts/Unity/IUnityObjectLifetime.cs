using UnityEngine;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Creates Unity objects and provides their permanent release operation on the Unity main
    /// thread. Release is terminal and must not return the object for reuse.
    /// </summary>
    public interface IUnityObjectLifetime : IUnityObjectSpawner
    {
        /// <summary>
        /// Permanently releases an object. A Component represents the instantiated GameObject
        /// that owns it; other Unity object types represent themselves.
        /// </summary>
        /// <param name="instance">The object whose lifetime has ended.</param>
        /// <remarks>
        /// The owner invokes this operation once and does not retry after an exception. An
        /// implementation must make the ownership transition terminal before executing
        /// failure-prone callbacks.
        /// </remarks>
        void Release(Object instance);
    }
}
