using UnityEngine;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    /// <summary>
    /// Identifies who initiated an interaction. An abstract class rather than an interface, so a
    /// struct cannot be boxed into it. Override <see cref="StableId"/> for networked play; the
    /// default projects <see cref="Id"/> and is not stable across processes.
    /// </summary>
    public abstract class InstigatorHandle
    {
        /// <summary>Unique integer ID for fast equality checks and dictionary keying.</summary>
        public abstract int Id { get; }

        /// <summary>
        /// Stable cross-process ID for networking, replay, analytics, and save data.
        /// Defaults to a local-only projection of <see cref="Id"/>; override for multiplayer.
        /// </summary>
        public virtual ulong StableId => unchecked((ulong)(uint)Id);

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

        public override int GetHashCode() => StableId.GetHashCode();

        public bool Equals(InstigatorHandle other) => other != null && StableId == other.StableId;

        public override bool Equals(object obj) => obj is InstigatorHandle other && StableId == other.StableId;
    }
}
