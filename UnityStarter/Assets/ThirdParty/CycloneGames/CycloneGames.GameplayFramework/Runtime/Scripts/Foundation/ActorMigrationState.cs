using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Immutable snapshot of <see cref="Actor"/> state sufficient to reconstruct the actor
    /// on another process. Pure data contract—serialization is handled by the networking
    /// integration layer via <see cref="Actor.CaptureMigrationState"/> and
    /// <see cref="Actor.RestoreMigrationState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This struct lives in GameplayFramework.Runtime (no networking dependency).
    /// The networking integration layer provides extension methods to serialize/deserialize
    /// this struct through <c>INetWriter</c>/<c>INetReader</c>.
    /// </para>
    /// <para>
    /// Reference-typed fields (Owner, Instigator) are stored as opaque identifiers
    /// (connection ID, actor ID) because raw UnityEngine.Object references are not
    /// valid across process boundaries.
    /// </para>
    /// </remarks>
    public readonly struct ActorMigrationState
    {
        /// <summary>World position at migration time.</summary>
        public readonly Vector3 Position;

        /// <summary>World rotation at migration time.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Local scale at migration time.</summary>
        public readonly Vector3 Scale;

        /// <summary>Asset identity for re-instantiation on the target process.</summary>
        public readonly string PrefabAssetPath;

        /// <summary>Remaining lifespan in seconds (0 = immortal).</summary>
        public readonly float RemainingLifeSpan;

        /// <summary>Whether this actor can be damaged.</summary>
        public readonly bool CanBeDamaged;

        /// <summary>Whether this actor is hidden in game.</summary>
        public readonly bool Hidden;

        /// <summary>Actor gameplay tags (copy of tag list).</summary>
        public readonly string[] Tags;

        /// <summary>Original owner's network connection ID, or 0 if none.</summary>
        public readonly int OwnerConnectionId;

        /// <summary>Instigator's globally unique actor ID, or 0 if none.</summary>
        public readonly int InstigatorActorId;

        /// <summary>Actor display name.</summary>
        public readonly string ActorName;

        /// <summary>Whether BeginPlay had already been called on the source.</summary>
        public readonly bool HasBegunPlay;

        public ActorMigrationState(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            string prefabAssetPath,
            float remainingLifeSpan,
            bool canBeDamaged,
            bool hidden,
            string[] tags,
            int ownerConnectionId,
            int instigatorActorId,
            string actorName,
            bool hasBegunPlay)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            PrefabAssetPath = prefabAssetPath;
            RemainingLifeSpan = remainingLifeSpan;
            CanBeDamaged = canBeDamaged;
            Hidden = hidden;
            Tags = tags;
            OwnerConnectionId = ownerConnectionId;
            InstigatorActorId = instigatorActorId;
            ActorName = actorName;
            HasBegunPlay = hasBegunPlay;
        }
    }
}
