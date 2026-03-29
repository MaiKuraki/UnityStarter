using System;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Zero-allocation VFX pool system for spawning and recycling particle effects.
    /// Manages object pooling internally so callers never need to instantiate or destroy directly.
    /// </summary>
    public interface IEffectPoolSystem : IDisposable
    {
        /// <summary>Pre-warm internal pools and prepare the system for spawning.</summary>
        void Initialize();

        /// <summary>Spawn a pooled effect at the given position and rotation with no automatic return timer.</summary>
        /// <param name="prefab">The effect prefab to spawn (must have a <see cref="PooledEffect"/> component).</param>
        /// <param name="position">World-space spawn position.</param>
        /// <param name="rotation">World-space spawn rotation.</param>
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation);

        /// <summary>Spawn a pooled effect that automatically returns to the pool after <paramref name="duration"/> seconds.</summary>
        /// <param name="prefab">The effect prefab to spawn.</param>
        /// <param name="position">World-space spawn position.</param>
        /// <param name="rotation">World-space spawn rotation.</param>
        /// <param name="duration">Time in seconds before the effect is returned to the pool.</param>
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float duration);
    }
}