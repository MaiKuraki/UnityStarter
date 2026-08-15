using CycloneGames.DeterministicMath;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework
{
    public static class GameplayFrameworkDeterministicInteractionExtensions
    {
        public static bool TryGetDeterministicInteractionPosition(
            this Actor actor,
            IInteractionDeterministicPositionProvider positionProvider,
            out FPVector3 position)
        {
            if (actor == null || positionProvider == null)
            {
                position = default;
                return false;
            }

            return positionProvider.TryGetDeterministicInteractionPosition(out position);
        }

        public static bool TryCreateDeterministicInteractionTargetSnapshot(
            this Actor actor,
            IInteractionDeterministicPositionProvider positionProvider,
            int worldId,
            ulong targetStableId,
            FPInt64 interactionRange,
            out InteractionDeterministicTargetSnapshot snapshot,
            bool isAvailable = true,
            bool allowDefaultAction = true,
            string[] enabledActionIds = null,
            int version = 0)
        {
            if (actor == null ||
                positionProvider == null ||
                targetStableId == InteractionStableId.None ||
                !positionProvider.TryGetDeterministicInteractionPosition(out FPVector3 position))
            {
                snapshot = default;
                return false;
            }

            snapshot = new InteractionDeterministicTargetSnapshot(
                worldId,
                targetStableId,
                position,
                interactionRange,
                isAvailable,
                allowDefaultAction,
                enabledActionIds,
                version);
            return true;
        }

        public static bool TryCreateDeterministicInteractionRequestPayload(
            this Actor actor,
            IInteractionDeterministicPositionProvider positionProvider,
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            out InteractionDeterministicRequestPayload payload)
        {
            if (actor == null ||
                positionProvider == null ||
                !positionProvider.TryGetDeterministicInteractionPosition(out FPVector3 position))
            {
                payload = default;
                return false;
            }

            payload = new InteractionDeterministicRequestPayload(
                requestId,
                instigatorStableId,
                targetStableId,
                actionId,
                tick,
                worldId,
                position);
            return true;
        }
    }
}
