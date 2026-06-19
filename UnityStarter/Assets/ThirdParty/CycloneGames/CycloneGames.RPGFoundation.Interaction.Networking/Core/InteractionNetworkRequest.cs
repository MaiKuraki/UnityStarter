using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    public struct InteractionNetworkRequest : IInteractionPositionProvider
    {
        public int RequestId;
        public ulong InstigatorStableId;
        public ulong TargetStableId;
        public string ActionId;
        public int Tick;
        public int WorldId;
        public NetworkVector3 InstigatorPosition;

        public InteractionNetworkRequest(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            string actionId,
            int tick,
            int worldId,
            NetworkVector3 instigatorPosition)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            ActionId = actionId;
            Tick = tick;
            WorldId = worldId;
            InstigatorPosition = instigatorPosition;
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

        public bool TryGetInteractionPosition(out InteractionVector3 position)
        {
            position = InstigatorPosition.ToInteractionVector3();
            return InstigatorPosition.IsFinite();
        }

        public static InteractionNetworkRequest From(
            in InteractionRequest request,
            NetworkVector3 instigatorPosition)
        {
            return new InteractionNetworkRequest(
                request.RequestId,
                request.InstigatorStableId,
                request.TargetStableId,
                request.ActionId,
                request.Tick,
                request.WorldId,
                instigatorPosition);
        }
    }
}
