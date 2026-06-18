namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionAuthorityOptions
    {
        public readonly int WorldId;
        public readonly bool RequireStableIds;
        public readonly int MaxFutureTickDelta;
        public readonly int MaxPastTickDelta;
        public readonly int RateLimitWindowTicks;
        public readonly int MaxRequestsPerRateLimitWindow;
        public readonly int RequestHistoryWindowTicks;
        public readonly int RequestHistoryCapacity;
        public readonly int QueueCapacityPerTarget;
        public readonly int MaxQueuedRequestsPerInstigator;

        public InteractionAuthorityOptions(
            int worldId = 0,
            bool requireStableIds = true,
            int maxFutureTickDelta = 8,
            int maxPastTickDelta = 120,
            int rateLimitWindowTicks = 30,
            int maxRequestsPerRateLimitWindow = 8,
            int requestHistoryWindowTicks = 600,
            int requestHistoryCapacity = 65536,
            int queueCapacityPerTarget = 64,
            int maxQueuedRequestsPerInstigator = 4)
        {
            WorldId = worldId;
            RequireStableIds = requireStableIds;
            MaxFutureTickDelta = maxFutureTickDelta > 0 ? maxFutureTickDelta : 0;
            MaxPastTickDelta = maxPastTickDelta > 0 ? maxPastTickDelta : 0;
            RateLimitWindowTicks = rateLimitWindowTicks > 0 ? rateLimitWindowTicks : 0;
            MaxRequestsPerRateLimitWindow = maxRequestsPerRateLimitWindow > 0 ? maxRequestsPerRateLimitWindow : 0;
            RequestHistoryWindowTicks = requestHistoryWindowTicks > 0 ? requestHistoryWindowTicks : 0;
            RequestHistoryCapacity = requestHistoryCapacity > 0 ? requestHistoryCapacity : 65536;
            QueueCapacityPerTarget = queueCapacityPerTarget > 0 ? queueCapacityPerTarget : 64;
            MaxQueuedRequestsPerInstigator = maxQueuedRequestsPerInstigator > 0 ? maxQueuedRequestsPerInstigator : 0;
        }
    }
}
