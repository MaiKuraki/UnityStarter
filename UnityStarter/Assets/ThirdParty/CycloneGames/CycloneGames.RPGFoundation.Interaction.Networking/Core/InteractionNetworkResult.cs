using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    public struct InteractionNetworkResult
    {
        public int RequestId;
        public ulong InstigatorStableId;
        public ulong TargetStableId;
        public bool Success;
        public InteractionCancelReason CancelReason;
        public InteractionValidationFailure ValidationFailure;
        public int QueuePosition;
        public int WorldId;

        public InteractionNetworkResult(
            int requestId,
            ulong instigatorStableId,
            ulong targetStableId,
            bool success,
            InteractionCancelReason cancelReason,
            InteractionValidationFailure validationFailure,
            int queuePosition,
            int worldId)
        {
            RequestId = requestId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            Success = success;
            CancelReason = cancelReason;
            ValidationFailure = validationFailure;
            QueuePosition = queuePosition;
            WorldId = worldId;
        }

        public InteractionResult ToInteractionResult()
        {
            return new InteractionResult(
                RequestId,
                0,
                0,
                InstigatorStableId,
                TargetStableId,
                Success,
                CancelReason,
                QueuePosition,
                WorldId);
        }

        public static InteractionNetworkResult FromInteractionResult(
            in InteractionResult result,
            InteractionValidationFailure validationFailure = InteractionValidationFailure.None)
        {
            return new InteractionNetworkResult(
                result.RequestId,
                result.InstigatorStableId,
                result.TargetStableId,
                result.Success,
                result.CancelReason,
                validationFailure,
                result.QueuePosition,
                result.WorldId);
        }

        public static InteractionNetworkResult FromValidationResult(in InteractionValidationResult validation)
        {
            InteractionRequest request = validation.Request;
            return new InteractionNetworkResult(
                request.RequestId,
                request.InstigatorStableId,
                request.TargetStableId,
                validation.IsAccepted,
                validation.IsAccepted ? InteractionCancelReason.Manual : InteractionCancelReason.Rejected,
                validation.Failure,
                validation.QueuePosition,
                request.WorldId);
        }
    }
}
