using System;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>Unity-facing adapters for engine-independent GameplayFramework network values.</summary>
    public static class ActorNetworkingExtensions
    {
        public static ActorMigrationState CaptureMigrationState(
            this Actor actor,
            string prefabDefinitionId,
            int ownerConnectionId,
            int instigatorActorId)
        {
            AssertActorAccessThread(actor);

            int tagCount = actor.TagCount;
            string[] tags = tagCount == 0 ? Array.Empty<string>() : new string[tagCount];
            if (tagCount > 0)
            {
                actor.CopyTagsTo(tags.AsSpan());
            }

            Vector3 position = actor.GetActorLocation();
            Quaternion rotation = actor.GetActorRotation();
            Vector3 scale = actor.GetActorScale();
            NetworkQuaternion networkRotation = ToNormalizedNetworkQuaternion(rotation);
            var state = new ActorMigrationState(
                ToNetworkVector3(position),
                networkRotation,
                ToNetworkVector3(scale),
                prefabDefinitionId,
                actor.GetRemainingLifeSpan(),
                actor.CanBeDamaged(),
                actor.IsHidden(),
                tags,
                ownerConnectionId,
                instigatorActorId,
                actor.GetName(),
                actor.HasBegunPlay);
            return state;
        }

        public static void ApplyMigrationState(this Actor actor, in ActorMigrationState state)
        {
            AssertActorAccessThread(actor);

            ActorMigrationNetworkingExtensions.ValidateMigrationState(in state);
            actor.SetActorLocationAndRotation(ToUnityVector3(state.Position), ToUnityQuaternion(state.Rotation));
            actor.SetActorScale(ToUnityVector3(state.Scale));
            actor.SetCanBeDamaged(state.CanBeDamaged);
            actor.ReplaceTags(state.Tags);
            actor.SetActorHiddenInGame(state.Hidden);
            actor.SetLifeSpan(Mathf.Max(0f, state.RemainingLifeSpan));
            if (!string.IsNullOrEmpty(state.ActorName))
            {
                actor.gameObject.name = state.ActorName;
            }
        }

        public static NetworkedGameplayActor ToNetworkedGameplayActor(
            this Actor actor,
            uint networkId,
            int ownerConnectionId,
            ulong ownerPlayerId = 0UL,
            int teamId = 0,
            uint interestLayerMask = uint.MaxValue,
            bool alwaysRelevant = false)
        {
            AssertActorAccessThread(actor);

            return new NetworkedGameplayActor(
                networkId,
                ownerConnectionId,
                ownerPlayerId,
                teamId,
                interestLayerMask,
                alwaysRelevant,
                ToNetworkVector3(actor.GetActorLocation()));
        }

        public static void SetObserver(
            this GameplayNetworkObserverRegistry registry,
            INetConnection connection,
            Vector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            int teamId = 0)
        {
            if (!TrySetObserver(registry, connection, position, radius, layerMask, teamId))
            {
                throw new InvalidOperationException(
                    $"Observer capacity reached the configured maximum of {registry.MaximumObserverCount}.");
            }
        }

        public static bool TrySetObserver(
            this GameplayNetworkObserverRegistry registry,
            INetConnection connection,
            Vector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            int teamId = 0)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            return registry.TrySetObserver(
                connection.ConnectionId,
                ToNetworkVector3(position),
                radius,
                layerMask,
                connection.PlayerId,
                teamId,
                connection);
        }

        private static NetworkVector3 ToNetworkVector3(Vector3 value)
        {
            return new NetworkVector3(value.x, value.y, value.z);
        }

        /// <summary>
        /// Bound Actors expose their authoritative thread through World. Unbound authoring Actors have no
        /// World owner to validate, so their caller remains responsible for Unity main-thread affinity.
        /// </summary>
        private static void AssertActorAccessThread(Actor actor)
        {
            if (ReferenceEquals(actor, null))
            {
                throw new ArgumentNullException(nameof(actor));
            }

            actor.World?.AssertOwnerThread();
            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }
        }

        private static Vector3 ToUnityVector3(NetworkVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static Quaternion ToUnityQuaternion(NetworkQuaternion value)
        {
            return new Quaternion(value.X, value.Y, value.Z, value.W);
        }

        internal static NetworkQuaternion ToNormalizedNetworkQuaternion(Quaternion value)
        {
            float sqrMagnitude =
                value.x * value.x +
                value.y * value.y +
                value.z * value.z +
                value.w * value.w;
            if (float.IsNaN(sqrMagnitude) ||
                float.IsInfinity(sqrMagnitude) ||
                sqrMagnitude < 1e-8f)
            {
                throw new InvalidOperationException("Actor rotation cannot be converted to a normalized network quaternion.");
            }

            float inverseMagnitude = 1f / Mathf.Sqrt(sqrMagnitude);
            return new NetworkQuaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }
    }
}
