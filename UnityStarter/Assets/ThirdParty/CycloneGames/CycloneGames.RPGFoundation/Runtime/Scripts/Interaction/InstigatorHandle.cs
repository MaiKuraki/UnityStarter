using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Abstract base class identifying who initiated an interaction.
    /// Uses an abstract class (not interface) to guarantee zero boxing at the compile level —
    /// only reference types can inherit from a class, eliminating any accidental struct boxing.
    /// <para/>
    /// Built-in implementation: <see cref="GameObjectInstigator"/> for MonoBehaviour-based games.
    /// <para/>
    /// Subclass for other paradigms:
    /// <list type="bullet">
    ///   <item>ECS: wrap <c>Entity</c> in a class (cache per-entity to avoid allocation)</item>
    ///   <item>Entityless: wrap a player ID or session token</item>
    ///   <item>Networking: wrap a network identity / client ID</item>
    /// </list>
    /// </summary>
    public abstract class InstigatorHandle
    {
        /// <summary>Unique integer ID for fast equality checks and dictionary keying.</summary>
        public abstract int Id { get; }

        /// <summary>
        /// Try to resolve the world-space position of this instigator.
        /// Returns false for entityless / non-spatial instigators, which disables
        /// distance-based auto-cancellation.
        /// </summary>
        public virtual bool TryGetPosition(out Vector3 position)
        {
            position = default;
            return false;
        }

        public override int GetHashCode() => Id;

        public bool Equals(InstigatorHandle other) => other != null && Id == other.Id;

        public override bool Equals(object obj) => obj is InstigatorHandle other && Id == other.Id;
    }
}
