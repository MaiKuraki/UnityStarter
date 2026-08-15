using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    public readonly struct InteractionDeterministicRequest : IInteractionDeterministicPositionProvider
    {
        public readonly int RequestId;
        public readonly ulong InstigatorStableId;
        public readonly ulong TargetStableId;
        public readonly string ActionId;
        public readonly int Tick;
        public readonly int WorldId;
        public readonly FPVector3 InstigatorPosition;

        public InteractionDeterministicRequest(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            FPVector3 instigatorPosition)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            ActionId = actionId;
            Tick = tick;
            WorldId = worldId;
            InstigatorPosition = instigatorPosition;
        }

        public InteractionDeterministicRequest(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            InteractionDeterministicVector3Payload instigatorPosition)
            : this(
                requestId,
                instigatorStableId,
                targetStableId,
                actionId,
                tick,
                worldId,
                instigatorPosition.ToFPVector3())
        {
        }

        public InteractionRequest ToInteractionRequest()
        {
            return new InteractionRequest(
                RequestId,
                InstigatorStableId,
                TargetStableId,
                ActionId,
                Tick,
                WorldId);
        }

        public InteractionDeterministicVector3Payload ToInstigatorPositionPayload()
        {
            return new InteractionDeterministicVector3Payload(InstigatorPosition);
        }

        public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
        {
            position = InstigatorPosition;
            return true;
        }
    }
}
