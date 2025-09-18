#if ADDRESSABLES_PRESENT
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AddressablesUtils
    {
        /// <summary>
        /// Attaches a CancellationToken to an Addressables operation.
        /// If the token is cancelled, the handle is released.
        /// </summary>
        public static async UniTask<AsyncOperationHandle<T>> WithCancellation<T>(this AsyncOperationHandle<T> handle, CancellationToken cancellationToken)
        {
            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            return handle;
        }
        
        public static async UniTask<AsyncOperationHandle> WithCancellation(this AsyncOperationHandle handle, CancellationToken cancellationToken)
        {
            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            return handle;
        }
    }
}
#endif
