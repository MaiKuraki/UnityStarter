namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Blend strategy: all competing instances play simultaneously and their authored weights are normalized so
    /// the channel's total weight sums to 1. Use this for cross-fading between choreographies (e.g. blending a
    /// recovery pose into the next action) or mixing overlapping ambient audio.
    /// </summary>
    public sealed class BlendPlaybackStrategy : IPlaybackStrategy
    {
        public static readonly BlendPlaybackStrategy Instance = new BlendPlaybackStrategy();

        public ChoreographyPlaybackMode Mode => ChoreographyPlaybackMode.Blend;

        public ChoreographyAdmission Resolve(in ChoreographyStrategyContext context)
        {
            return ChoreographyAdmission.Admit;
        }

        public float ResolveWeight(in ChoreographyWeightContext context)
        {
            if (context.TotalAuthoredWeight <= 0f)
            {
                return 0f;
            }
            return context.AuthoredWeight / context.TotalAuthoredWeight;
        }
    }
}
