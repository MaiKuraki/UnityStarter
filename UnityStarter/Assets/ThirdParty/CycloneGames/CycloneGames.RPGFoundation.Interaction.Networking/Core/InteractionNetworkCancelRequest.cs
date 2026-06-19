using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    public struct InteractionNetworkCancelRequest
    {
        public int RequestId;
        public ulong InstigatorStableId;
        public ulong TargetStableId;
        public InteractionCancelReason CancelReason;
        public int Tick;
        public int WorldId;

        public InteractionNetworkCancelRequest(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            InteractionCancelReason cancelReason,
            int tick,
            int worldId)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            CancelReason = cancelReason;
            Tick = tick;
            WorldId = worldId;
        }

        public bool IsValid =>
            RequestId > 0 &&
            InstigatorStableId != InteractionStableId.None &&
            TargetStableId != InteractionStableId.None;

        public InteractionRequest ToInteractionRequest(string actionId = null)
        {
            return new InteractionRequest(
                RequestId,
                InstigatorStableId,
                TargetStableId,
                actionId,
                Tick,
                WorldId);
        }

        public static InteractionNetworkCancelRequest FromInteractionResult(in InteractionResult result, int tick = 0)
        {
            return new InteractionNetworkCancelRequest(
                result.RequestId,
                result.InstigatorStableId,
                result.TargetStableId,
                result.CancelReason,
                tick,
                result.WorldId);
        }
    }
}
