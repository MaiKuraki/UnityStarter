using CycloneGames.GameplayFramework.Runtime;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.GameplayFramework
{
    public static class GameplayFrameworkInteractionExtensions
    {
        public static bool TryGetInteractionPosition(this Actor actor, out InteractionVector3 position)
        {
            if (actor == null)
            {
                position = default;
                return false;
            }

            Vector3 actorPosition = actor.GetActorLocation();
            position = new InteractionVector3(actorPosition.x, actorPosition.y, actorPosition.z);
            return true;
        }

        public static InteractionVector3 GetInteractionPosition(this Actor actor)
        {
            return actor != null && actor.TryGetInteractionPosition(out InteractionVector3 position)
                ? position
                : InteractionVector3.Zero;
        }

        public static GameObjectInstigator CreateInteractionInstigator(
            this Actor actor,
            ulong stableId = InteractionStableId.None)
        {
            return actor != null
                ? new GameObjectInstigator(actor.gameObject, stableId)
                : new GameObjectInstigator(null, stableId);
        }

        public static bool TryCreateInteractionTargetSnapshot(
            this Actor actor,
            int worldId,
            ulong targetStableId,
            float interactionRange,
            out InteractionTargetSnapshot snapshot,
            bool isAvailable = true,
            bool allowDefaultAction = true,
            string[] enabledActionIds = null,
            int version = 0)
        {
            if (actor == null || targetStableId == InteractionStableId.None)
            {
                snapshot = default;
                return false;
            }

            Vector3 actorPosition = actor.GetActorLocation();
            snapshot = new InteractionTargetSnapshot(
                worldId,
                targetStableId,
                new InteractionVector3(actorPosition.x, actorPosition.y, actorPosition.z),
                interactionRange,
                isAvailable,
                allowDefaultAction,
                enabledActionIds,
                version);
            return true;
        }
    }
}
