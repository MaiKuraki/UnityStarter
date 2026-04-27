namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionRequest
    {
        public readonly int RequestId;
        public readonly int InstigatorId;
        public readonly int TargetInstanceId;
        public readonly string ActionId;
        public readonly int Tick;

        public InteractionRequest(int requestId, int instigatorId, int targetInstanceId, string actionId, int tick)
        {
            RequestId = requestId;
            InstigatorId = instigatorId;
            TargetInstanceId = targetInstanceId;
            ActionId = actionId;
            Tick = tick;
        }

        public bool IsValid => RequestId > 0 && TargetInstanceId != 0;
    }
}
