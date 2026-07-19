using System;
using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal interface IAssetHandleLease
    {
        bool TryGetBackend<TBackend>(out TBackend backend) where TBackend : class;
    }

    /// <summary>
    /// Creates caller-owned leases around shared provider handles. Lease objects are intentionally never pooled:
    /// a stale reference must never become capable of observing or releasing a later provider operation.
    /// </summary>
    internal static class AssetHandleLeases
    {
        public static IAssetHandle<TAsset> Create<TAsset>(
            IAssetHandle<TAsset> handle,
            CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            return new AssetHandleLease<TAsset>(handle, cancellationToken);
        }

        public static IAllAssetsHandle<TAsset> Create<TAsset>(
            IAllAssetsHandle<TAsset> handle,
            CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            return new AllAssetsHandleLease<TAsset>(handle, cancellationToken);
        }

        public static IRawFileHandle Create(
            IRawFileHandle handle,
            CancellationToken cancellationToken = default)
        {
            return new RawFileHandleLease(handle, cancellationToken);
        }

        private static UniTask CreateWaitTask(IOperation operation, CancellationToken cancellationToken)
        {
            return AssetOperationBroadcast.CreateCallerView(operation.Task, cancellationToken);
        }

        private sealed class AssetHandleLease<TAsset> : IAssetHandle<TAsset>, IAssetHandleLease
            where TAsset : UnityEngine.Object
        {
            private IAssetHandle<TAsset> _handle;
            private readonly UniTask _task;

            public AssetHandleLease(IAssetHandle<TAsset> handle, CancellationToken cancellationToken)
            {
                _handle = handle ?? throw new ArgumentNullException(nameof(handle));
                _task = CreateWaitTask(handle, cancellationToken);
            }

            public bool IsDone
            {
                get
                {
                    GetHandle();
                    return _task.Status != UniTaskStatus.Pending;
                }
            }
            public float Progress => GetHandle().Progress;
            public string Error => GetHandle().Error;
            public UniTask Task
            {
                get
                {
                    GetHandle();
                    return _task;
                }
            }
            public TAsset Asset => GetHandle().Asset;
            public UnityEngine.Object AssetObject => GetHandle().AssetObject;

            public void WaitForAsyncComplete()
            {
                GetHandle().WaitForAsyncComplete();
            }

            public void Dispose()
            {
                AssetRuntimeGuard.EnsureMainThread();
                Interlocked.Exchange(ref _handle, null)?.Dispose();
            }

            public bool TryGetBackend<TBackend>(out TBackend backend) where TBackend : class
            {
                AssetRuntimeGuard.EnsureMainThread();
                backend = _handle as TBackend;
                return backend != null;
            }

            private IAssetHandle<TAsset> GetHandle()
            {
                IAssetHandle<TAsset> handle = _handle ?? throw new ObjectDisposedException(
                    nameof(AssetHandleLease<TAsset>));
                ThrowIfBackendDisposed(handle, nameof(AssetHandleLease<TAsset>));
                return handle;
            }
        }

        private sealed class AllAssetsHandleLease<TAsset> : IAllAssetsHandle<TAsset>
            where TAsset : UnityEngine.Object
        {
            private IAllAssetsHandle<TAsset> _handle;
            private readonly UniTask _task;

            public AllAssetsHandleLease(IAllAssetsHandle<TAsset> handle, CancellationToken cancellationToken)
            {
                _handle = handle ?? throw new ArgumentNullException(nameof(handle));
                _task = CreateWaitTask(handle, cancellationToken);
            }

            public bool IsDone
            {
                get
                {
                    GetHandle();
                    return _task.Status != UniTaskStatus.Pending;
                }
            }
            public float Progress => GetHandle().Progress;
            public string Error => GetHandle().Error;
            public UniTask Task
            {
                get
                {
                    GetHandle();
                    return _task;
                }
            }
            public IReadOnlyList<TAsset> Assets => GetHandle().Assets;

            public void WaitForAsyncComplete()
            {
                GetHandle().WaitForAsyncComplete();
            }

            public void Dispose()
            {
                AssetRuntimeGuard.EnsureMainThread();
                Interlocked.Exchange(ref _handle, null)?.Dispose();
            }

            private IAllAssetsHandle<TAsset> GetHandle()
            {
                IAllAssetsHandle<TAsset> handle = _handle ?? throw new ObjectDisposedException(
                    nameof(AllAssetsHandleLease<TAsset>));
                ThrowIfBackendDisposed(handle, nameof(AllAssetsHandleLease<TAsset>));
                return handle;
            }
        }

        private sealed class RawFileHandleLease : IRawFileHandle
        {
            private IRawFileHandle _handle;
            private readonly UniTask _task;

            public RawFileHandleLease(IRawFileHandle handle, CancellationToken cancellationToken)
            {
                _handle = handle ?? throw new ArgumentNullException(nameof(handle));
                _task = CreateWaitTask(handle, cancellationToken);
            }

            public bool IsDone
            {
                get
                {
                    GetHandle();
                    return _task.Status != UniTaskStatus.Pending;
                }
            }
            public float Progress => GetHandle().Progress;
            public string Error => GetHandle().Error;
            public UniTask Task
            {
                get
                {
                    GetHandle();
                    return _task;
                }
            }
            public string FilePath => GetHandle().FilePath;

            public void WaitForAsyncComplete()
            {
                GetHandle().WaitForAsyncComplete();
            }

            public string ReadText()
            {
                return GetHandle().ReadText();
            }

            public byte[] ReadBytes()
            {
                return GetHandle().ReadBytes();
            }

            public void Dispose()
            {
                AssetRuntimeGuard.EnsureMainThread();
                Interlocked.Exchange(ref _handle, null)?.Dispose();
            }

            private IRawFileHandle GetHandle()
            {
                IRawFileHandle handle = _handle ?? throw new ObjectDisposedException(
                    nameof(RawFileHandleLease));
                ThrowIfBackendDisposed(handle, nameof(RawFileHandleLease));
                return handle;
            }
        }

        private static void ThrowIfBackendDisposed(object handle, string ownerName)
        {
            if (handle is IAssetBackendLifetime lifetime && lifetime.IsDisposed)
            {
                throw new ObjectDisposedException(
                    ownerName,
                    "The provider backend was disposed by its owning package or module.");
            }
        }
    }
}
