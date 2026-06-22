using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Configuration for <see cref="FileIORetry"/>. Describes how many attempts to make and how the
    /// delay grows between transient failures.
    /// </summary>
    public sealed class FileIORetryPolicy
    {
        public static readonly FileIORetryPolicy Default =
            new FileIORetryPolicy(4, TimeSpan.FromMilliseconds(20), 2.0, TimeSpan.FromMilliseconds(500));

        public int MaxAttempts { get; }

        public TimeSpan InitialDelay { get; }

        public double BackoffMultiplier { get; }

        public TimeSpan MaxDelay { get; }

        public FileIORetryPolicy(int maxAttempts, TimeSpan initialDelay, double backoffMultiplier, TimeSpan maxDelay)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1.");
            }

            MaxAttempts = maxAttempts;
            InitialDelay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
            BackoffMultiplier = backoffMultiplier <= 0 ? 1.0 : backoffMultiplier;
            MaxDelay = maxDelay < TimeSpan.Zero ? TimeSpan.Zero : maxDelay;
        }
    }

    /// <summary>
    /// Opt-in retry wrapper for transient file I/O failures, such as sharing violations caused by
    /// antivirus, search indexers, or backup tools briefly locking a file (common on Windows).
    ///
    /// This is intentionally not applied automatically by <see cref="FileUtility"/> or
    /// <see cref="FileService"/>; wrap only the operations that need it. Permanent failures
    /// (missing file/directory, path too long) are not retried.
    /// </summary>
    public static class FileIORetry
    {
        public static T Execute<T>(Func<T> operation, FileIORetryPolicy policy = null)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            policy = policy ?? FileIORetryPolicy.Default;
            TimeSpan delay = policy.InitialDelay;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (IOException ex) when (attempt < policy.MaxAttempts && IsTransient(ex))
                {
                    Thread.Sleep(delay);
                    delay = NextDelay(delay, policy);
                }
            }
        }

        public static void Execute(Action operation, FileIORetryPolicy policy = null)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            Execute<object>(() => { operation(); return null; }, policy);
        }

        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, FileIORetryPolicy policy = null, CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            policy = policy ?? FileIORetryPolicy.Default;
            TimeSpan delay = policy.InitialDelay;

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (IOException ex) when (attempt < policy.MaxAttempts && IsTransient(ex))
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = NextDelay(delay, policy);
                }
            }
        }

        public static async Task ExecuteAsync(Func<Task> operation, FileIORetryPolicy policy = null, CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            await ExecuteAsync<object>(async () =>
            {
                await operation().ConfigureAwait(false);
                return null;
            }, policy, cancellationToken).ConfigureAwait(false);
        }

        private static bool IsTransient(IOException exception)
        {
            // Missing file/directory and path-too-long are permanent; retrying cannot succeed.
            if (exception is FileNotFoundException
                || exception is DirectoryNotFoundException
                || exception is PathTooLongException)
            {
                return false;
            }

            return true;
        }

        private static TimeSpan NextDelay(TimeSpan current, FileIORetryPolicy policy)
        {
            double nextMs = current.TotalMilliseconds * policy.BackoffMultiplier;
            double cappedMs = Math.Min(nextMs, policy.MaxDelay.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(cappedMs);
        }
    }
}
