using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Logging;

namespace CycloneGames.AssetManagement.Runtime.CacheRetention
{
    /// <summary>
    /// Optional project-layer scheduler that periodically applies an <see cref="AssetCacheRetentionPolicy"/>
    /// to an <see cref="IAssetPackage"/> idle cache.
    /// </summary>
    public sealed class AssetCacheRetentionScheduler : IDisposable
    {
        private static readonly LogChannel Log = AssetCacheRetentionLog.Channel;

        private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(1d);

        /// <summary>
        /// Upper bound on parked provider-release failures retried per scheduler pass. Bounds the worst-case
        /// main-thread cost of one pass independently of how large the retry queue grew.
        /// </summary>
        private const int MaxReleaseRetryWorkPerPass = 64;

        private readonly Func<IAssetPackage> _packageProvider;
        private readonly AssetCacheRetentionPolicy _policy;
        private readonly TimeSpan _checkInterval;
        private readonly bool _logEvictions;
        private readonly bool _retryPendingReleaseFailures;

        private readonly object _gate = new object();
        private CancellationTokenSource _runningCts;
        private bool _disposed;

        /// <summary>
        /// Creates a scheduler bound to an explicit package.
        /// </summary>
        public AssetCacheRetentionScheduler(
            IAssetPackage package,
            AssetCacheRetentionPolicy policy,
            TimeSpan checkInterval,
            bool logEvictions = false,
            bool retryPendingReleaseFailures = false)
            : this(WrapPackage(package), policy, checkInterval, logEvictions, retryPendingReleaseFailures)
        {
        }

        /// <summary>
        /// Creates a scheduler that resolves its target package lazily on every pass.
        /// </summary>
        /// <param name="retryPendingReleaseFailures">
        /// When true, every pass first retries parked provider-release failures through the package's
        /// <see cref="IAssetReleaseRetryDriver"/> capability (ignored when the package does not implement it),
        /// before applying the retention policy. This drains the release-retry queue even when no other cache
        /// operation runs.
        /// </summary>
        public AssetCacheRetentionScheduler(
            Func<IAssetPackage> packageProvider,
            AssetCacheRetentionPolicy policy,
            TimeSpan checkInterval,
            bool logEvictions = false,
            bool retryPendingReleaseFailures = false)
        {
            _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
            _policy = policy;
            _checkInterval = checkInterval < MinCheckInterval ? MinCheckInterval : checkInterval;
            _logEvictions = logEvictions;
            _retryPendingReleaseFailures = retryPendingReleaseFailures;
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _runningCts != null;
                }
            }
        }

        /// <summary>
        /// Starts the periodic retention loop. Idempotent; a second call while running is a no-op.
        /// </summary>
        public void Start()
        {
            CancellationTokenSource cts;

            lock (_gate)
            {
                if (_disposed || _runningCts != null)
                {
                    return;
                }

                cts = new CancellationTokenSource();
                _runningCts = cts;
            }

            RunLoopAsync(cts).Forget();
        }

        /// <summary>
        /// Stops the loop. Idempotent. The scheduler can be restarted unless disposed.
        /// </summary>
        public void Stop()
        {
            var cts = TakeRunningCts();
            if (cts == null)
            {
                return;
            }

            CancelAndDispose(cts);
        }

        /// <summary>
        /// Runs a single retention pass immediately and returns how many idle handles were evicted.
        /// </summary>
        public int TrimNow()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return 0;
                }
            }

            var package = _packageProvider();
            return package?.TrimIdleCache(_policy) ?? 0;
        }

        /// <summary>
        /// Runs one bounded, best-effort retry pass over parked provider-release failures and returns how many
        /// release failures are still parked. A no-op when the scheduler was created without release-retry
        /// driving or when the package does not implement <see cref="IAssetReleaseRetryDriver"/>.
        /// </summary>
        public int RetryNow()
        {
            if (!_retryPendingReleaseFailures)
            {
                return 0;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return 0;
                }
            }

            var package = _packageProvider();
            return package is IAssetReleaseRetryDriver driver
                ? driver.RetryPendingReleaseFailures(MaxReleaseRetryWorkPerPass)
                : 0;
        }

        private async UniTaskVoid RunLoopAsync(CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await UniTask.Delay(_checkInterval, DelayType.Realtime, PlayerLoopTiming.Update, token);

                    // Retry parked provider-release failures before trimming: a recovered release returns its
                    // node to the pool, so the following trim observes a consistent idle pool.
                    int pendingReleaseFailures = RetryNow();

                    int evicted = TrimNow();
                    if (_logEvictions && evicted > 0)
                    {
                        Log.Info(
                            evicted,
                            static (count, builder) => builder
                                .Append("[AssetCacheRetentionScheduler] Trimmed ")
                                .Append(count)
                                .Append(" idle asset handle(s)."));
                    }

                    if (pendingReleaseFailures > 0)
                    {
                        Log.Warning(
                            pendingReleaseFailures,
                            static (count, builder) => builder
                                .Append("[AssetCacheRetentionScheduler] ")
                                .Append(count)
                                .Append(" provider release failure(s) are still parked for retry."));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not AccessViolationException)
            {
                Log.Error(
                    ex,
                    "[AssetCacheRetentionScheduler] Retention loop stopped due to an unexpected error.");
            }
            finally
            {
                CompleteRun(cts);
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cts;

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                cts = _runningCts;
                _runningCts = null;
            }

            if (cts != null)
            {
                CancelAndDispose(cts);
            }
        }

        private CancellationTokenSource TakeRunningCts()
        {
            lock (_gate)
            {
                var cts = _runningCts;
                _runningCts = null;
                return cts;
            }
        }

        private void CompleteRun(CancellationTokenSource cts)
        {
            bool ownsCts;

            lock (_gate)
            {
                ownsCts = ReferenceEquals(_runningCts, cts);
                if (ownsCts)
                {
                    _runningCts = null;
                }
            }

            if (ownsCts)
            {
                cts.Dispose();
            }
        }

        private static void CancelAndDispose(CancellationTokenSource cts)
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static Func<IAssetPackage> WrapPackage(IAssetPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return () => package;
        }
    }
}
