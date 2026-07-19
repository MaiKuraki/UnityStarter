using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owner-thread callback storage with allocation-free steady-state dispatch.
    /// Subscriptions added during dispatch are visible on the next dispatch. Removals tombstone entries
    /// until the outermost dispatch finishes, then compact the existing buffer in place.
    /// </summary>
    internal sealed class GASCallbackList<TDelegate> where TDelegate : Delegate
    {
        private readonly List<TDelegate> callbacks;
        private int dispatchDepth;
        private int tombstoneCount;
        private int activeCount;

        public GASCallbackList(int capacity = 0)
        {
            callbacks = capacity > 0 ? new List<TDelegate>(capacity) : new List<TDelegate>();
        }

        public int ActiveCount => activeCount;

        public void Add(TDelegate callback)
        {
            if (callback == null)
            {
                return;
            }

            Delegate[] invocationList = callback.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                callbacks.Add((TDelegate)invocationList[i]);
                activeCount++;
            }
        }

        public void RemoveLast(TDelegate callback)
        {
            if (callback == null || activeCount == 0)
            {
                return;
            }

            Delegate[] removal = callback.GetInvocationList();
            for (int start = callbacks.Count - removal.Length; start >= 0; start--)
            {
                bool matches = true;
                for (int offset = 0; offset < removal.Length; offset++)
                {
                    TDelegate current = callbacks[start + offset];
                    if (current == null || !current.Equals(removal[offset]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                if (dispatchDepth == 0)
                {
                    callbacks.RemoveRange(start, removal.Length);
                }
                else
                {
                    for (int offset = 0; offset < removal.Length; offset++)
                    {
                        callbacks[start + offset] = null;
                        tombstoneCount++;
                    }
                }

                activeCount -= removal.Length;
                return;
            }
        }

        public int BeginDispatch()
        {
            dispatchDepth++;
            return callbacks.Count;
        }

        public TDelegate GetCallback(int index)
        {
            return callbacks[index];
        }

        public void EndDispatch()
        {
            if (dispatchDepth <= 0)
            {
                throw new InvalidOperationException("GAS callback dispatch scopes must be balanced.");
            }

            dispatchDepth--;
            if (dispatchDepth == 0 && tombstoneCount > 0)
            {
                CompactInPlace();
            }
        }

        public void Clear()
        {
            activeCount = 0;
            if (dispatchDepth == 0)
            {
                callbacks.Clear();
                tombstoneCount = 0;
                return;
            }

            for (int i = 0; i < callbacks.Count; i++)
            {
                if (callbacks[i] != null)
                {
                    callbacks[i] = null;
                    tombstoneCount++;
                }
            }
        }

        private void CompactInPlace()
        {
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < callbacks.Count; readIndex++)
            {
                TDelegate callback = callbacks[readIndex];
                if (callback == null)
                {
                    continue;
                }

                if (writeIndex != readIndex)
                {
                    callbacks[writeIndex] = callback;
                }
                writeIndex++;
            }

            if (writeIndex < callbacks.Count)
            {
                callbacks.RemoveRange(writeIndex, callbacks.Count - writeIndex);
            }
            tombstoneCount = 0;
        }
    }
}
