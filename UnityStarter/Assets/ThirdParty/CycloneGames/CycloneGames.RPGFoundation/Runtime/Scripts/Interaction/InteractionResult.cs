namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionResult
    {
        public readonly int RequestId;
        public readonly int InstigatorId;
        public readonly int TargetInstanceId;
        public readonly ulong InstigatorStableId;
        public readonly ulong TargetStableId;
        public readonly bool Success;
        public readonly InteractionCancelReason CancelReason;
        public readonly int QueuePosition;
        public readonly int WorldId;

        public InteractionResult(int requestId, int instigatorId, int targetInstanceId, bool success,
            InteractionCancelReason cancelReason = InteractionCancelReason.Manual, int queuePosition = 0)
            : this(requestId, instigatorId, targetInstanceId, InteractionStableId.None, InteractionStableId.None, success, cancelReason, queuePosition, 0)
        {
        }

        public InteractionResult(int requestId, ulong instigatorStableId, ulong targetStableId, bool success,
            InteractionCancelReason cancelReason = InteractionCancelReason.Manual, int queuePosition = 0)
            : this(requestId, 0, 0, instigatorStableId, targetStableId, success, cancelReason, queuePosition, 0)
        {
        }

        public InteractionResult(int requestId, int instigatorId, int targetInstanceId,
            ulong instigatorStableId, ulong targetStableId, bool success,
            InteractionCancelReason cancelReason = InteractionCancelReason.Manual, int queuePosition = 0)
            : this(requestId, instigatorId, targetInstanceId, instigatorStableId, targetStableId, success, cancelReason, queuePosition, 0)
        {
        }

        public InteractionResult(int requestId, int instigatorId, int targetInstanceId,
            ulong instigatorStableId, ulong targetStableId, bool success,
            InteractionCancelReason cancelReason, int queuePosition, int worldId)
        {
            RequestId = requestId;
            InstigatorId = instigatorId;
            TargetInstanceId = targetInstanceId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            Success = success;
            CancelReason = cancelReason;
            QueuePosition = queuePosition;
            WorldId = worldId;
        }

        public bool IsQueued => QueuePosition > 0;
    }
}
