using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Allocation-bounded package-owned counter for provider and adapter continuations that outlive
    /// caller-visible cancellation. No per-operation collection or cancellation source is retained.
    /// </summary>
    internal sealed class AssetOperationTailTracker
    {
        private readonly bool _deferCompletionOnePlayerLoop;
        private UniTaskCompletionSource _drained;
        private int _pendingCount;

        public AssetOperationTailTracker(bool deferCompletionOnePlayerLoop = false)
        {
            _deferCompletionOnePlayerLoop = deferCompletionOnePlayerLoop;
        }

        public int PendingCount => Volatile.Read(ref _pendingCount);

        public void RegisterTail()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_pendingCount == int.MaxValue)
            {
                throw new InvalidOperationException("Asset operation tail capacity is exhausted.");
            }

            if (_pendingCount == 0)
            {
                _drained = null;
            }

            _pendingCount++;
        }

        public void CompleteTail()
        {
            if (!PlayerLoopHelper.IsMainThread || _deferCompletionOnePlayerLoop)
            {
                CompleteTailOnPlayerLoopAsync().Forget();
                return;
            }

            CompleteTailNow();
        }

        public UniTask WaitForAllAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_pendingCount == 0)
            {
                return UniTask.CompletedTask;
            }

            _drained ??= new UniTaskCompletionSource();
            return _drained.Task;
        }

        private async UniTaskVoid CompleteTailOnPlayerLoopAsync()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToMainThread();
            }

            if (_deferCompletionOnePlayerLoop)
            {
                await UniTask.Yield();
            }

            CompleteTailNow();
        }

        private void CompleteTailNow()
        {
            int remaining = --_pendingCount;
            if (remaining < 0)
            {
                _pendingCount = 0;
                throw new InvalidOperationException("Asset operation tail completion was reported more than once.");
            }

            if (remaining == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }
}
