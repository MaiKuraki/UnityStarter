using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

using Cysharp.Threading.Tasks;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime.Batch
{
    public sealed class GroupOperation : IGroupOperation
    {
        private const int STATE_CREATED = 0;
        private const int STATE_RUNNING = 1;
        private const int STATE_COMPLETED = 2;

        private readonly struct Item
        {
            public readonly IOperation Operation;
            public readonly UniTask Task;
            public readonly float Weight;

            public Item(IOperation operation, float weight)
            {
                Operation = operation;
                Task = operation.Task;
                Weight = weight;
            }
        }

        private readonly List<Item> _items = new List<Item>(8);
        private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();
        private readonly object _gate = new object();

        private IReadOnlyList<IOperation> _itemsSnapshot;
        private double _totalWeight;
        private int _state;
        private int _cancelRequested;
        private string _error;

        public bool IsDone => Volatile.Read(ref _state) == STATE_COMPLETED;
        public float Progress { get; private set; }
        public string Error => _error;
        public UniTask Task => _completion.Task;

        public IReadOnlyList<IOperation> Items
        {
            get
            {
                lock (_gate)
                {
                    if (_itemsSnapshot != null) return _itemsSnapshot;

                    var operations = new List<IOperation>(_items.Count);
                    for (int i = 0; i < _items.Count; i++)
                    {
                        operations.Add(_items[i].Operation);
                    }

                    _itemsSnapshot = new ReadOnlyCollection<IOperation>(operations);
                    return _itemsSnapshot;
                }
            }
        }

        public void Add(IOperation operation, float weight = 1f)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            lock (_gate)
            {
                if (_state != STATE_CREATED)
                {
                    throw new InvalidOperationException("Group operations cannot be modified after execution starts.");
                }

                weight = NormalizeWeight(weight);
                _items.Add(new Item(operation, weight));
                _totalWeight += weight;
                _itemsSnapshot = null;
            }
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            bool startExecution = false;
            lock (_gate)
            {
                if (_state == STATE_CREATED)
                {
                    Volatile.Write(ref _state, STATE_RUNNING);
                    startExecution = true;
                }
            }

            if (startExecution)
            {
                ExecuteAsync().Forget();
            }

            await AssetOperationBroadcast.CreateCallerView(_completion.Task, cancellationToken);
        }

        private async UniTask ExecuteAsync()
        {
            _error = null;
            Progress = 0f;

            try
            {
                if (_items.Count == 0)
                {
                    Complete();
                    return;
                }

                while (true)
                {
                    if (Volatile.Read(ref _cancelRequested) != 0)
                    {
                        throw new OperationCanceledException("Group operation was cancelled.");
                    }

                    double weightedProgress = 0d;
                    bool allDone = true;

                    for (int i = 0; i < _items.Count; i++)
                    {
                        Item item = _items[i];
                        IOperation operation = item.Operation;
                        if (item.Task.Status != UniTaskStatus.Pending)
                        {
                            weightedProgress += item.Weight;
                            continue;
                        }

                        allDone = false;
                        weightedProgress += item.Weight * Math.Clamp(operation.Progress, 0f, 1f);
                    }

                    Progress = _totalWeight <= 0f
                        ? 1f
                        : (float)Math.Min(1d, weightedProgress / _totalWeight);
                    if (allDone)
                    {
                        await ObserveCompletedOperationsAsync();
                        Complete();
                        return;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }
            catch (OperationCanceledException cancellation)
            {
                ObserveChildrenAfterCancellation();
                Interlocked.Exchange(ref _state, STATE_COMPLETED);
                _completion.TrySetCanceled(cancellation.CancellationToken);
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                _error = exception.Message;
                Interlocked.Exchange(ref _state, STATE_COMPLETED);
                _completion.TrySetException(exception);
            }
        }

        private void ObserveChildrenAfterCancellation()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                ObserveChildAfterCancellationAsync(_items[i].Task).Forget();
            }
        }

        private static async UniTaskVoid ObserveChildAfterCancellationAsync(UniTask task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                CLogger.LogWarning(
                    $"[GroupOperation] Child operation failed after shared cancellation " +
                    $"({exception.GetType().Name}).");
            }
        }

        public void Cancel()
        {
            Interlocked.Exchange(ref _cancelRequested, 1);
        }

        public void WaitForAsyncComplete()
        {
            throw new NotSupportedException("GroupOperation is asynchronous and cannot be synchronously completed.");
        }

        private void Complete()
        {
            Progress = 1f;
            Interlocked.Exchange(ref _state, STATE_COMPLETED);
            _completion.TrySetResult();
        }

        private async UniTask ObserveCompletedOperationsAsync()
        {
            Exception firstFailure = null;
            for (int i = 0; i < _items.Count; i++)
            {
                try
                {
                    await _items[i].Task;
                }
                catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    // Observe every recoverable child failure, then report the first item failure deterministically.
                    firstFailure ??= exception;
                }
            }

            if (firstFailure != null)
            {
                throw firstFailure;
            }
        }

        private static float NormalizeWeight(float weight)
        {
            return weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight) ? 1f : weight;
        }
    }
}
