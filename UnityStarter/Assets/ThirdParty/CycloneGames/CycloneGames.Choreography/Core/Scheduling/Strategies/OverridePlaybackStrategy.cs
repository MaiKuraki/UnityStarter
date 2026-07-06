namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Override strategy: the incoming request always takes the channel; the highest-priority instance emits full
    /// weight and all others are muted (weight 0). Unlike <see cref="PriorityPlaybackStrategy"/>, it never queues
    /// or rejects and ignores interruptibility. Use for hard, unconditional takeovers (e.g. a hit-react that must
    /// visually replace whatever is playing).
    /// </summary>
    public sealed class OverridePlaybackStrategy : IPlaybackStrategy
    {
        public static readonly OverridePlaybackStrategy Instance = new OverridePlaybackStrategy();

        public ChoreographyPlaybackMode Mode => ChoreographyPlaybackMode.Override;

        public ChoreographyAdmission Resolve(in ChoreographyStrategyContext context)
        {
            return context.ActiveCountOnChannel == 0 ? ChoreographyAdmission.Admit : ChoreographyAdmission.Replace;
        }

        public float ResolveWeight(in ChoreographyWeightContext context)
        {
            return context.InstancePriority >= context.HighestPriorityOnChannel ? 1f : 0f;
        }
    }
}
