// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Bounds how many asynchronous operations may run at once. Waiters are served in arrival order.
    /// </summary>
    /// <remarks>
    /// The gate is immutable. Callers that need a new capacity replace the instance while
    /// <see cref="IsIdle"/> is true, so a slot is never lost or double-released during a swap.
    /// </remarks>
    internal sealed class AudioConcurrencyGate : IDisposable
    {
        private readonly SemaphoreSlim semaphore;
        private int activeCount;
        private int waitingCount;

        internal AudioConcurrencyGate(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            semaphore = new SemaphoreSlim(capacity, capacity);
        }

        internal int Capacity { get; }

        internal int ActiveCount => Volatile.Read(ref activeCount);

        internal int WaitingCount => Volatile.Read(ref waitingCount);

        internal bool IsIdle => ActiveCount == 0 && WaitingCount == 0;

        /// <summary>
        /// Waits for a slot. Every successful call must be paired with exactly one <see cref="Release"/>.
        /// </summary>
        internal async UniTask WaitAsync()
        {
            if (!semaphore.Wait(0))
            {
                Interlocked.Increment(ref waitingCount);
                try
                {
                    await semaphore.WaitAsync();
                }
                finally
                {
                    Interlocked.Decrement(ref waitingCount);
                }
            }

            Interlocked.Increment(ref activeCount);
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref activeCount) < 0)
            {
                Interlocked.Increment(ref activeCount);
                throw new InvalidOperationException("AudioConcurrencyGate released without a matching acquire.");
            }

            semaphore.Release();
        }

        public void Dispose()
        {
            semaphore.Dispose();
        }
    }
}
