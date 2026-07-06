namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Options controlling a <see cref="PreloadRunner"/> batch. A small value type so callers build it inline.
    /// </summary>
    public readonly struct PreloadOptions
    {
        /// <summary>How the runner reacts to an individual load failure.</summary>
        public readonly PreloadFailurePolicy FailurePolicy;

        /// <summary>Maximum concurrent in-flight loads. 0 (or less) starts every reference immediately.</summary>
        public readonly int MaxConcurrent;

        public PreloadOptions(PreloadFailurePolicy failurePolicy = PreloadFailurePolicy.Continue, int maxConcurrent = 0)
        {
            FailurePolicy = failurePolicy;
            MaxConcurrent = maxConcurrent;
        }

        public static PreloadOptions Default => new PreloadOptions(PreloadFailurePolicy.Continue, 0);
    }
}
