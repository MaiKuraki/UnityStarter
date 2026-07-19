using System;
using System.IO;

namespace CycloneGames.IO
{
    public sealed class FileRetryPolicy
    {
        private static readonly TimeSpan MaximumSupportedDelay =
            TimeSpan.FromMilliseconds(int.MaxValue);

        public static readonly FileRetryPolicy Default = new FileRetryPolicy(
            4,
            TimeSpan.FromMilliseconds(20),
            2.0,
            TimeSpan.FromMilliseconds(500));

        public FileRetryPolicy(
            int maxAttempts,
            TimeSpan initialDelay,
            double backoffMultiplier,
            TimeSpan maxDelay,
            Func<IOException, bool> transientClassifier = null)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            if (maxDelay < TimeSpan.Zero || maxDelay > MaximumSupportedDelay)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelay));
            }

            if (initialDelay < TimeSpan.Zero || initialDelay > maxDelay)
            {
                throw new ArgumentOutOfRangeException(nameof(initialDelay));
            }

            if (double.IsNaN(backoffMultiplier)
                || double.IsInfinity(backoffMultiplier)
                || backoffMultiplier < 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(backoffMultiplier));
            }

            MaxAttempts = maxAttempts;
            InitialDelay = initialDelay;
            BackoffMultiplier = backoffMultiplier;
            MaxDelay = maxDelay;
            TransientClassifier = transientClassifier;
        }

        public int MaxAttempts { get; }

        public TimeSpan InitialDelay { get; }

        public double BackoffMultiplier { get; }

        public TimeSpan MaxDelay { get; }

        public Func<IOException, bool> TransientClassifier { get; }
    }
}
