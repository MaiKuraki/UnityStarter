namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Additive strategy: all competing instances play and each keeps its authored weight without normalization.
    /// Providers are expected to add the contributions (e.g. an additive animation layer or a stacked audio
    /// one-shot). Use for effects that layer on top of a base rather than replace or cross-fade it.
    /// </summary>
    public sealed class AdditivePlaybackStrategy : IPlaybackStrategy
    {
        public static readonly AdditivePlaybackStrategy Instance = new AdditivePlaybackStrategy();

        public ChoreographyPlaybackMode Mode => ChoreographyPlaybackMode.Additive;

        public ChoreographyAdmission Resolve(in ChoreographyStrategyContext context)
        {
            return ChoreographyAdmission.Admit;
        }

        public float ResolveWeight(in ChoreographyWeightContext context)
        {
            return context.AuthoredWeight;
        }
    }
}
