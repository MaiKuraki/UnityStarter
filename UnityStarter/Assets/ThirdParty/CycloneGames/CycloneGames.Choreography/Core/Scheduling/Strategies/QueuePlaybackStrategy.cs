namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Queue strategy: only one instance plays on the channel at a time. While the channel is busy, incoming
    /// requests are queued and started (in submission order) when the current instance completes. Use for
    /// sequences that must not overlap (e.g. chained combo steps, sequential voice lines).
    /// </summary>
    public sealed class QueuePlaybackStrategy : IPlaybackStrategy
    {
        public static readonly QueuePlaybackStrategy Instance = new QueuePlaybackStrategy();

        public ChoreographyPlaybackMode Mode => ChoreographyPlaybackMode.Queue;

        public ChoreographyAdmission Resolve(in ChoreographyStrategyContext context)
        {
            return context.ActiveCountOnChannel == 0 ? ChoreographyAdmission.Admit : ChoreographyAdmission.Queue;
        }

        public float ResolveWeight(in ChoreographyWeightContext context)
        {
            return 1f;
        }
    }
}
