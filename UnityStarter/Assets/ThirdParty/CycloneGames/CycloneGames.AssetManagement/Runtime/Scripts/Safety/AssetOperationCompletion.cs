using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Separates caller-visible operation completion from the provider/adapter tail that must still be drained.
    /// The first caller-visible terminal transition wins, while the tail is always observed to completion.
    /// </summary>
    internal readonly struct AssetOperationCompletion
    {
        private static readonly CancellationToken OwnerRetirementToken =
            new CancellationToken(canceled: true);

        private readonly UniTaskCompletionSource _completion;

        private AssetOperationCompletion(UniTaskCompletionSource completion)
        {
            _completion = completion;
        }

        public UniTask Task => _completion == null ? UniTask.CompletedTask : _completion.Task;

        public static AssetOperationCompletion Start(
            UniTask operationTail,
            AssetOperationTailTracker tailTracker)
        {
            var completion = new UniTaskCompletionSource();
            tailTracker?.RegisterTail();
            ForwardAsync(operationTail, completion, tailTracker).Forget();
            return new AssetOperationCompletion(completion);
        }

        /// <summary>
        /// Publishes owner retirement to callers without aborting observation of the provider tail.
        /// </summary>
        public bool TryCancelByOwner()
        {
            return _completion != null && _completion.TrySetCanceled(OwnerRetirementToken);
        }

        private static async UniTask ForwardAsync(
            UniTask operationTail,
            UniTaskCompletionSource completion,
            AssetOperationTailTracker tailTracker)
        {
            Exception terminalException = null;
            try
            {
                await operationTail;
            }
            catch (Exception exception)
            {
                terminalException = exception;
            }

            try
            {
                // Retire a naturally completed tail before publishing its terminal state. UniTask continuations
                // may run inline, so publishing first can expose stale ownership to a continuation that begins
                // package shutdown immediately after awaiting the operation.
                tailTracker?.CompleteTail();
            }
            catch (Exception tailException)
            {
                terminalException ??= tailException;
            }

            if (terminalException == null)
            {
                completion.TrySetResult();
                return;
            }

            if (terminalException is OperationCanceledException cancellation)
            {
                completion.TrySetCanceled(cancellation.CancellationToken);
                return;
            }

            if (AssetRuntimeGuard.IsRecoverableException(terminalException))
            {
                AssetOperationBroadcast.SetExceptionAndMarkObserved(completion, terminalException);
            }
            else if (!completion.TrySetException(terminalException))
            {
                // Owner retirement must not silently consume a fatal provider failure.
                throw terminalException;
            }
        }
    }
}
