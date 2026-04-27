namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionResult
    {
        public readonly int RequestId;
        public readonly int InstigatorId;
        public readonly int TargetInstanceId;
        public readonly bool Success;
        public readonly InteractionCancelReason CancelReason;
        public readonly int QueuePosition;

        public InteractionResult(int requestId, int instigatorId, int targetInstanceId, bool success,
            InteractionCancelReason cancelReason = InteractionCancelReason.Manual, int queuePosition = 0)
        {
            RequestId = requestId;
            InstigatorId = instigatorId;
            TargetInstanceId = targetInstanceId;
            Success = success;
            CancelReason = cancelReason;
            QueuePosition = queuePosition;
        }

        public bool IsQueued => QueuePosition > 0;
    }
}
