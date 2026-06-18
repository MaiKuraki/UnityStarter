using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.DeterministicMath
{
    /// <summary>
    /// Transport-friendly deterministic interaction request.
    /// Use this shape for networking, replay, save data, or backend protocols that must preserve fixed-point position.
    /// </summary>
    public readonly struct InteractionDeterministicRequestPayload : IInteractionDeterministicPositionProvider
    {
        public readonly int RequestId;
        public readonly ulong InstigatorStableId;
        public readonly ulong TargetStableId;
        public readonly string ActionId;
        public readonly int Tick;
        public readonly int WorldId;
        public readonly InteractionDeterministicVector3Payload InstigatorPosition;

        public InteractionDeterministicRequestPayload(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            InteractionDeterministicVector3Payload instigatorPosition)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            ActionId = actionId;
            Tick = tick;
            WorldId = worldId;
            InstigatorPosition = instigatorPosition;
        }

        public InteractionDeterministicRequestPayload(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            FPVector3 instigatorPosition)
            : this(
                requestId,
                instigatorStableId,
                targetStableId,
                actionId,
                tick,
                worldId,
                new InteractionDeterministicVector3Payload(instigatorPosition))
        {
        }

        public bool IsValid => RequestId > 0 && TargetStableId != InteractionStableId.None;

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

        public InteractionDeterministicRequest ToDeterministicRequest()
        {
            return new InteractionDeterministicRequest(
                RequestId,
                InstigatorStableId,
                TargetStableId,
                ActionId,
                Tick,
                WorldId,
                InstigatorPosition);
        }

        public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
        {
            return InstigatorPosition.TryGetDeterministicInteractionPosition(out position);
        }
    }
}
