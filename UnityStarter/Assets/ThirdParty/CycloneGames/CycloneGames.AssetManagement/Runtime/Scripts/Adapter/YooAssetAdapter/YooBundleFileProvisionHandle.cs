#if CYCLONEGAMES_HAS_YOOASSET
using System;

using Cysharp.Threading.Tasks;
using YooAsset;

using CycloneGames.Logging;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// YooAsset-backed one-shot bundle-file provision lease. Wraps the provider's ensure-file operation and
    /// snapshots its bundle detail once the provider reaches a successful terminal state. The lease is not
    /// cached and not reference counted: the caller owns exactly one Dispose. Completion is observed through
    /// the owning package's operation tails, so package destruction drains a still-running provision even
    /// when the caller has already disposed this wrapper.
    /// </summary>
    internal sealed class YooBundleFileProvisionHandle : IBundleFileProvisionHandle
    {
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

        private readonly long _id;
        private readonly AssetOperationCompletion _completion;
        private EnsureBundleFileOperation _operation;
        private int _releaseState;

        private string _bundleFilePath = string.Empty;
        private bool _isEncrypted;
        private int _bundleType;
        private string _error = string.Empty;
        private float _progress;

        private YooBundleFileProvisionHandle(
            long id,
            EnsureBundleFileOperation operation,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            _operation = operation;
            _completion = AssetOperationCompletion.Start(CompleteAndSnapshotAsync(operation), operationTails);
        }

        public static YooBundleFileProvisionHandle Create(
            long id,
            EnsureBundleFileOperation operation,
            AssetOperationTailTracker operationTails) =>
            new YooBundleFileProvisionHandle(id, operation, operationTails);

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;

        public float Progress
        {
            get
            {
                EnsureBundleFileOperation operation = Volatile.Read(ref _operation);
                if (operation != null && PlayerLoopHelper.IsMainThread && operation.IsDone == false)
                {
                    Volatile.Write(ref _progress, operation.Progress);
                }

                return Volatile.Read(ref _progress);
            }
        }

        public string Error => Volatile.Read(ref _error) ?? string.Empty;
        public UniTask Task => _completion.Task;
        public string BundleFilePath => Volatile.Read(ref _bundleFilePath) ?? string.Empty;
        public bool IsEncrypted => Volatile.Read(ref _isEncrypted);
        public int BundleType => Volatile.Read(ref _bundleType);

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            YooSynchronousWait.EnsureTerminal(_completion.Task.Status);
        }

        /// <summary>
        /// Retires the caller-owned wrapper exactly once. The provider operation itself is not cancelled or
        /// released: YooAsset provisions are fire-and-forget operations, and the package's operation tails
        /// keep observing the provider task until it reaches a terminal state.
        /// </summary>
        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            if (HandleTracker.Enabled)
            {
                HandleTracker.Unregister(_id);
            }

            Volatile.Write(ref _operation, null);
        }

        private async UniTask CompleteAndSnapshotAsync(EnsureBundleFileOperation operation)
        {
            try
            {
                await YooOperationTask.CompleteAsync(
                    operation,
                    "YooAsset failed to ensure the requested bundle file.");

                if (!PlayerLoopHelper.IsMainThread)
                {
                    await UniTask.SwitchToMainThread();
                }

                if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
                {
                    throw new ObjectDisposedException(nameof(YooBundleFileProvisionHandle));
                }

                EnsureBundleFileOperation.BundleDetail detail = operation.Detail;
                Volatile.Write(ref _bundleFilePath, detail.BundleFilePath ?? string.Empty);
                Volatile.Write(ref _isEncrypted, detail.IsEncrypted);
                Volatile.Write(ref _bundleType, detail.BundleType);
                Volatile.Write(ref _progress, 1f);
            }
            catch (Exception ex)
            {
                if (_completion.Task.Status != UniTaskStatus.Canceled)
                {
                    Log.Error(
                        ex,
                        "[YooBundleFileProvisionHandle] YooAsset bundle-file provision failed.");
                    Volatile.Write(ref _error, ex.Message ?? "YooAsset bundle-file provision failed.");
                }

                throw;
            }
        }
    }
}
#endif
