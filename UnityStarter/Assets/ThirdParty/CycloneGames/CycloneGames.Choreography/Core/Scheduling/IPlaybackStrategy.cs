namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Composable rule that decides whether a new choreography may start on a channel and how the effective mix
    /// weight of each active instance is computed. Strategies must be stateless and allocation-free so a single
    /// instance can be shared across every scheduler and channel.
    /// </summary>
    public interface IPlaybackStrategy
    {
        /// <summary>The mode this strategy handles. The scheduler routes requests by matching this value.</summary>
        ChoreographyPlaybackMode Mode { get; }

        /// <summary>Decides admission for an incoming request competing on a channel.</summary>
        ChoreographyAdmission Resolve(in ChoreographyStrategyContext context);

        /// <summary>Computes the effective mix weight in [0, 1] for one active sample on the channel.</summary>
        float ResolveWeight(in ChoreographyWeightContext context);
    }
}
