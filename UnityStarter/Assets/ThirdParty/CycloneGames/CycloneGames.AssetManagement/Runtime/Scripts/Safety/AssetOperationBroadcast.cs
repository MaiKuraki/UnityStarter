using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Observes a single-consumer provider task exactly once and exposes a multi-consumer completion source.
    /// The non-auto-reset UniTaskCompletionSource supports concurrent continuations and repeated terminal reads.
    /// </summary>
    internal static class AssetOperationBroadcast
    {
        public static UniTask Create(UniTask providerTask)
        {
            var completion = new UniTaskCompletionSource();
            ForwardAsync(providerTask, completion).Forget();
            return completion.Task;
        }

        public static UniTask<T> Create<T>(UniTask<T> providerTask)
        {
            var completion = new UniTaskCompletionSource<T>();
            ForwardAsync(providerTask, completion).Forget();
            return completion.Task;
        }

        public static void Observe(UniTask task)
        {
            ObserveCompletionAsync(task).Forget();
        }

        public static void Observe<T>(UniTask<T> task)
        {
            ObserveCompletionAsync(task).Forget();
        }

        public static UniTask CreateCallerView(
            UniTask broadcastTask,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return broadcastTask;
            }

            return Create(broadcastTask.AttachExternalCancellation(cancellationToken));
        }

        private static async UniTask ForwardAsync(
            UniTask providerTask,
            UniTaskCompletionSource completion)
        {
            try
            {
                await providerTask;
                completion.TrySetResult();
            }
            catch (OperationCanceledException cancellation)
            {
                completion.TrySetCanceled(cancellation.CancellationToken);
            }
            catch (Exception exception)
            {
                if (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    SetExceptionAndMarkObserved(completion, exception);
                }
                else
                {
                    // Fatal exceptions must remain fatal, but the shared task must still become terminal so
                    // callers and shutdown drains cannot remain pending forever. Do not read the completion here:
                    // doing so would rethrow the fatal exception inside this forwarding bridge.
                    completion.TrySetException(exception);
                }
            }
        }

        private static async UniTask ForwardAsync<T>(
            UniTask<T> providerTask,
            UniTaskCompletionSource<T> completion)
        {
            try
            {
                T result = await providerTask;
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException cancellation)
            {
                completion.TrySetCanceled(cancellation.CancellationToken);
            }
            catch (Exception exception)
            {
                if (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    SetExceptionAndMarkObserved(completion, exception);
                }
                else
                {
                    completion.TrySetException(exception);
                }
            }
        }

        private static void SetExceptionAndMarkObserved(
            UniTaskCompletionSource completion,
            Exception exception)
        {
            if (!completion.TrySetException(exception))
            {
                return;
            }

            try
            {
                completion.GetResult(0);
            }
            catch (Exception observed) when (AssetRuntimeGuard.IsRecoverableException(observed))
            {
                // The shared task remains faulted and repeatable for callers. Reading it here only marks the
                // completion source as handled so an abandoned caller cannot publish the same fault from a finalizer.
            }
        }

        private static void SetExceptionAndMarkObserved<T>(
            UniTaskCompletionSource<T> completion,
            Exception exception)
        {
            if (!completion.TrySetException(exception))
            {
                return;
            }

            try
            {
                completion.GetResult(0);
            }
            catch (Exception observed) when (AssetRuntimeGuard.IsRecoverableException(observed))
            {
                // See the non-generic overload.
            }
        }

        private static async UniTask ObserveCompletionAsync(UniTask broadcastTask)
        {
            try
            {
                await broadcastTask;
            }
            catch (OperationCanceledException)
            {
                // Cancellation remains observable to every caller awaiter. This internal await only prevents an
                // intentionally abandoned caller view from surfacing later through the completion-source finalizer.
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                // Provider failure remains memoized on broadcastTask for every caller awaiter.
            }
        }

        private static async UniTask ObserveCompletionAsync<T>(UniTask<T> broadcastTask)
        {
            try
            {
                await broadcastTask;
            }
            catch (OperationCanceledException)
            {
                // See the non-generic overload.
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                // Provider failure remains memoized on broadcastTask for every caller awaiter.
            }
        }
    }
}
