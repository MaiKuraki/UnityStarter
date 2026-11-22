namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Injectable policy values for the feedback runtime. Structural ceilings
    /// stay on DeviceFeedbackLimits; composition roots can tune these per
    /// platform or hardware tier.
    /// </summary>
    public readonly struct DeviceFeedbackProfile
    {
        public const int AbsoluteMinimumSampleIntervalMilliseconds = 1;
        public const int AbsoluteMaximumCacheEntries = 64;
        public const int AbsoluteMaximumCacheBudgetBytes = 8 << 20;
        public const int AbsoluteMaximumCacheableLength = DeviceFeedbackLimits.HardMaximumWaveformSampleCount;
        public const int AbsoluteMaximumClipCacheEntries = 128;

        public const int DefaultMinimumSampleIntervalMilliseconds = 5;
        public const int DefaultExactArrayCacheMaxEntries = 16;
        public const int DefaultExactArrayCacheBudgetBytes = 1 << 20;
        public const int DefaultCacheableMaximumLength = 8192;
        public const int DefaultClipCacheMaxEntries = 32;

        /// <summary>Shared immutable default.</summary>
        public static readonly DeviceFeedbackProfile Default = new DeviceFeedbackProfile(
            DefaultMinimumSampleIntervalMilliseconds,
            DefaultExactArrayCacheMaxEntries,
            DefaultExactArrayCacheBudgetBytes,
            DefaultCacheableMaximumLength,
            DefaultClipCacheMaxEntries);

        public DeviceFeedbackProfile(
            int minimumSampleIntervalMilliseconds = DefaultMinimumSampleIntervalMilliseconds,
            int exactArrayCacheMaxEntries = DefaultExactArrayCacheMaxEntries,
            int exactArrayCacheBudgetBytes = DefaultExactArrayCacheBudgetBytes,
            int cacheableMaximumLength = DefaultCacheableMaximumLength,
            int clipCacheMaxEntries = DefaultClipCacheMaxEntries)
        {
            ValidateRange(minimumSampleIntervalMilliseconds,
                          AbsoluteMinimumSampleIntervalMilliseconds,
                          DeviceFeedbackLimits.DefaultMaximumDurationMilliseconds,
                          nameof(minimumSampleIntervalMilliseconds));
            ValidateRange(exactArrayCacheMaxEntries,
                          1, AbsoluteMaximumCacheEntries,
                          nameof(exactArrayCacheMaxEntries));
            ValidateRange(exactArrayCacheBudgetBytes,
                          1, AbsoluteMaximumCacheBudgetBytes,
                          nameof(exactArrayCacheBudgetBytes));
            ValidateRange(cacheableMaximumLength,
                          1, AbsoluteMaximumCacheableLength,
                          nameof(cacheableMaximumLength));

            MinimumSampleIntervalMilliseconds = minimumSampleIntervalMilliseconds;
            ExactArrayCacheMaxEntries = exactArrayCacheMaxEntries;
            ExactArrayCacheBudgetBytes = exactArrayCacheBudgetBytes;
            CacheableMaximumLength = cacheableMaximumLength;
            ClipCacheMaxEntries = clipCacheMaxEntries;
            ValidateRange(clipCacheMaxEntries, 1, AbsoluteMaximumClipCacheEntries,
                          nameof(clipCacheMaxEntries));
        }

        public int MinimumSampleIntervalMilliseconds { get; }
        public int ExactArrayCacheMaxEntries { get; }
        public int ExactArrayCacheBudgetBytes { get; }
        public int CacheableMaximumLength { get; }
        public int ClipCacheMaxEntries { get; }

        private static void ValidateRange(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new System.ArgumentOutOfRangeException(name, value,
                    "Value must be between " + minimum + " and " + maximum + ".");
            }
        }
    }
}
