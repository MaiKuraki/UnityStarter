using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Interface for components that need to be notified when an actor is spawned with initial rotation.
    /// Components in lower layers (like RPGFoundation) can implement this interface to synchronize
    /// their initial rotation without GameplayFramework needing to know about specific implementations.
    /// </summary>
    public interface IInitialRotationSettable
    {
        /// <summary>
        /// Sets the initial rotation for the component. This is typically called after spawning an actor.
        /// </summary>
        /// <param name="rotation">The initial rotation to set</param>
        /// <param name="immediate">If true, sets rotation immediately. If false, sets as target for smooth rotation.</param>
        void SetInitialRotation(Quaternion rotation, bool immediate = true);
    }
}