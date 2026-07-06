namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Priority strategy: a higher-priority request interrupts the dominant instance only when that instance's
    /// current section is interruptible; otherwise the incoming request is queued. Equal or lower priority is
    /// rejected. Only the highest-priority active instance contributes output (weight 1); others are muted.
    /// Use this for exclusive actions where interrupts must respect committed windows (e.g. attack windups).
    /// </summary>
    public sealed class PriorityPlaybackStrategy : IPlaybackStrategy
    {
        public static readonly PriorityPlaybackStrategy Instance = new PriorityPlaybackStrategy();

        public ChoreographyPlaybackMode Mode => ChoreographyPlaybackMode.Priority;

        public ChoreographyAdmission Resolve(in ChoreographyStrategyContext context)
        {
            if (context.ActiveCountOnChannel == 0)
            {
                return ChoreographyAdmission.Admit;
            }

            if (context.IncomingPriority > context.HighestActivePriority)
            {
                return context.DominantInterruptible ? ChoreographyAdmission.Replace : ChoreographyAdmission.Queue;
            }

            return ChoreographyAdmission.Reject;
        }

        public float ResolveWeight(in ChoreographyWeightContext context)
        {
            return context.InstancePriority >= context.HighestPriorityOnChannel ? 1f : 0f;
        }
    }
}
