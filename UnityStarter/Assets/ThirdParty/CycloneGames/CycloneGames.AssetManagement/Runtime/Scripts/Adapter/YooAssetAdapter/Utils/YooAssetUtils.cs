#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class YooAssetUtils
    {
        /// <summary>
        /// Wraps a YooAsset operation with cancellation support.
        /// </summary>
        public static async UniTask<T> WithCancellation<T>(this T operation, CancellationToken cancellationToken) where T : AsyncOperationBase
        {
            if (operation.IsDone)
            {
                return operation;
            }

            // Fast path: if token is already cancelled.
            if (cancellationToken.IsCancellationRequested)
            {
                // NOTE: YooAsset operations cannot be truly cancelled once started.
                // We release the handle to prevent further use and signal failure.
                (operation as IDisposable)?.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            // Slow path: poll for completion while checking the token.
            try
            {
                await UniTask.WaitUntil(() => operation.IsDone, PlayerLoopTiming.Update, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled during the wait.
                (operation as IDisposable)?.Dispose();
                throw;
            }

            return operation;
        }
    }
}
#endif
