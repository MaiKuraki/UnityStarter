using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    public struct InteractionNetworkRequest : IInteractionPositionProvider
    {
        public int RequestId;
        public ulong InstigatorStableId;
        public ulong TargetStableId;
        public ulong ActionIdHash;
        public int Tick;
        public int WorldId;
        /// <summary>
        /// Client-reported instigator position. Untrusted: servers enforcing range
        /// authoritatively must resolve the position from their own simulation and
        /// use the server-position overloads on
        /// <see cref="InteractionNetworkAuthorityBridge"/>.
        /// </summary>
        public NetworkVector3 InstigatorPosition;

        public InteractionNetworkRequest(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            ulong actionIdHash,
            int tick,
            int worldId,
            NetworkVector3 instigatorPosition)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            ActionIdHash = actionIdHash;
            Tick = tick;
            WorldId = worldId;
            InstigatorPosition = instigatorPosition;
        }

        public bool IsValid => RequestId > 0 && TargetStableId != InteractionStableId.None;

        public InteractionRequest ToInteractionRequest()
        {
            return ToInteractionRequest(InteractionActionIds.Default);
        }

        public InteractionRequest ToInteractionRequest(InteractionActionIdRegistry registry)
        {
            string actionId = null;
            registry?.TryResolve(ActionIdHash, out actionId);
            return new InteractionRequest(
                RequestId,
                InstigatorStableId,
                TargetStableId,
                actionId,
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
            return From(request, instigatorPosition, InteractionActionIds.Default);
        }

        public static InteractionNetworkRequest From(
            in InteractionRequest request,
            NetworkVector3 instigatorPosition,
            InteractionActionIdRegistry registry)
        {
            ulong actionIdHash = registry != null && request.ActionId != null
                ? registry.Register(request.ActionId)
                : InteractionActionIds.ComputeHash(request.ActionId);

            return new InteractionNetworkRequest(
                request.RequestId,
                request.InstigatorStableId,
                request.TargetStableId,
                actionIdHash,
                request.Tick,
                request.WorldId,
                instigatorPosition);
        }
    }
}
