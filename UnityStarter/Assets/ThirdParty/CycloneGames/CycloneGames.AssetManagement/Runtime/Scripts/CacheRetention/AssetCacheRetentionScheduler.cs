using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime.CacheRetention
{
    /// <summary>
    /// Optional project-layer scheduler that periodically applies an <see cref="AssetCacheRetentionPolicy"/>
    /// to an <see cref="IAssetPackage"/> idle cache.
    /// </summary>
    public sealed class AssetCacheRetentionScheduler : IDisposable
    {
        private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(1d);

        private readonly Func<IAssetPackage> _packageProvider;
        private readonly AssetCacheRetentionPolicy _policy;
        private readonly TimeSpan _checkInterval;
        private readonly bool _logEvictions;

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
            bool logEvictions = false)
            : this(WrapPackage(package), policy, checkInterval, logEvictions)
        {
        }

        /// <summary>
        /// Creates a scheduler that resolves its target package lazily on every pass.
        /// </summary>
        public AssetCacheRetentionScheduler(
            Func<IAssetPackage> packageProvider,
            AssetCacheRetentionPolicy policy,
            TimeSpan checkInterval,
            bool logEvictions = false)
        {
            _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
            _policy = policy;
            _checkInterval = checkInterval < MinCheckInterval ? MinCheckInterval : checkInterval;
            _logEvictions = logEvictions;
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

        private async UniTaskVoid RunLoopAsync(CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await UniTask.Delay(_checkInterval, DelayType.Realtime, PlayerLoopTiming.Update, token);

                    int evicted = TrimNow();
                    if (_logEvictions && evicted > 0)
                    {
                        CLogger.LogInfo($"[AssetCacheRetentionScheduler] Trimmed {evicted} idle asset handle(s).");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not AccessViolationException)
            {
                CLogger.LogError($"[AssetCacheRetentionScheduler] Retention loop stopped due to an unexpected error: {ex}");
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
