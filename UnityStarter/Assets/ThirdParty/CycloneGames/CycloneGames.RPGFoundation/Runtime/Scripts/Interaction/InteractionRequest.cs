namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public static class InteractionStableId
    {
        public const ulong None = 0UL;

        /// <summary>
        /// Deterministic FNV-1a 64-bit hash for stable authoring IDs.
        /// Not cryptographic; use transport or backend auth for trust boundaries.
        /// </summary>
        public static ulong Hash64(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return None;
            }

            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }

            return hash == None ? 1UL : hash;
        }
    }

    public readonly struct InteractionRequest
    {
        public readonly int RequestId;
        public readonly int InstigatorId;
        public readonly int TargetInstanceId;
        public readonly ulong InstigatorStableId;
        public readonly ulong TargetStableId;
        public readonly string ActionId;
        public readonly int Tick;
        public readonly int WorldId;

        public InteractionRequest(int requestId, int instigatorId, int targetInstanceId, string actionId, int tick)
            : this(requestId, instigatorId, targetInstanceId, InteractionStableId.None, InteractionStableId.None, actionId, tick, 0)
        {
        }

        public InteractionRequest(int requestId, int instigatorId, int targetInstanceId, string actionId, int tick, int worldId)
            : this(requestId, instigatorId, targetInstanceId, InteractionStableId.None, InteractionStableId.None, actionId, tick, worldId)
        {
        }

        public InteractionRequest(int requestId, ulong instigatorStableId, ulong targetStableId, string actionId, int tick)
            : this(requestId, 0, 0, instigatorStableId, targetStableId, actionId, tick, 0)
        {
        }

        public InteractionRequest(int requestId, ulong instigatorStableId, ulong targetStableId, string actionId, int tick, int worldId)
            : this(requestId, 0, 0, instigatorStableId, targetStableId, actionId, tick, worldId)
        {
        }

        public InteractionRequest(int requestId, int instigatorId, int targetInstanceId, ulong instigatorStableId, ulong targetStableId, string actionId, int tick)
            : this(requestId, instigatorId, targetInstanceId, instigatorStableId, targetStableId, actionId, tick, 0)
        {
        }

        public InteractionRequest(int requestId, int instigatorId, int targetInstanceId, ulong instigatorStableId, ulong targetStableId, string actionId, int tick, int worldId)
        {
            RequestId = requestId;
            InstigatorId = instigatorId;
            TargetInstanceId = targetInstanceId;
            InstigatorStableId = instigatorStableId;
            TargetStableId = targetStableId;
            ActionId = actionId;
            Tick = tick;
            WorldId = worldId;
        }

        public bool IsValid => RequestId > 0 && (TargetStableId != InteractionStableId.None || TargetInstanceId != 0);
    }
}
