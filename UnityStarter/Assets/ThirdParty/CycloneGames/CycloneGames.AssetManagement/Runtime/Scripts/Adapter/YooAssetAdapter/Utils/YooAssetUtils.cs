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

            try
            {
                await operation.Task.AsUniTask().AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // YooAsset operations can't be truly cancelled, just ignore the result.
                throw;
            }

            return operation;
        }
    }
}
#endif
