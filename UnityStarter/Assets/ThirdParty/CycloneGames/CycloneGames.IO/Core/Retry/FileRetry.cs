using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    public static class FileRetry
    {
        private const int ERROR_SHARING_VIOLATION = 32;
        private const int ERROR_LOCK_VIOLATION = 33;

        public static T Execute<T>(
            Func<T> operation,
            FileRetryPolicy policy = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            FileRetryPolicy effectivePolicy = policy ?? FileRetryPolicy.Default;
            TimeSpan delay = effectivePolicy.InitialDelay;
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return operation();
                }
                catch (IOException exception) when (
                    attempt < effectivePolicy.MaxAttempts
                    && IsTransient(exception, effectivePolicy))
                {
                    WaitForRetry(delay, cancellationToken);

                    delay = NextDelay(delay, effectivePolicy);
                }
            }
        }

        public static void Execute(
            Action operation,
            FileRetryPolicy policy = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            Execute<object>(() =>
            {
                operation();
                return null;
            }, policy, cancellationToken);
        }

        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            FileRetryPolicy policy = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            FileRetryPolicy effectivePolicy = policy ?? FileRetryPolicy.Default;
            TimeSpan delay = effectivePolicy.InitialDelay;
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (IOException exception) when (
                    attempt < effectivePolicy.MaxAttempts
                    && IsTransient(exception, effectivePolicy))
                {
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    delay = NextDelay(delay, effectivePolicy);
                }
            }
        }

        public static async Task ExecuteAsync(
            Func<Task> operation,
            FileRetryPolicy policy = null,
            CancellationToken cancellationToken = default)
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

        public static bool IsTransient(IOException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            int nativeErrorCode = exception.HResult & 0xFFFF;
            return nativeErrorCode == ERROR_SHARING_VIOLATION
                || nativeErrorCode == ERROR_LOCK_VIOLATION;
        }

        private static bool IsTransient(IOException exception, FileRetryPolicy policy)
        {
            return policy.TransientClassifier != null
                ? policy.TransientClassifier(exception)
                : IsTransient(exception);
        }

        private static TimeSpan NextDelay(TimeSpan current, FileRetryPolicy policy)
        {
            double nextMilliseconds = current.TotalMilliseconds * policy.BackoffMultiplier;
            return TimeSpan.FromMilliseconds(Math.Min(nextMilliseconds, policy.MaxDelay.TotalMilliseconds));
        }

        private static void WaitForRetry(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            if (delay <= TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                Thread.Sleep(delay);
                return;
            }

            if (cancellationToken.WaitHandle.WaitOne(delay))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
