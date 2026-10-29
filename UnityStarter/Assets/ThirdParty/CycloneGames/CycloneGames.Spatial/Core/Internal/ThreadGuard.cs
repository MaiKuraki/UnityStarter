using System;

namespace CycloneGames.Spatial.Core.Internal
{
    /// <summary>
    /// Debug-only single-writer assertion. Compiled out without <c>DEBUG</c>.
    /// </summary>
    /// <remarks>
    /// The writing thread is captured on the first write rather than in the constructor, so an index built on a
    /// loading thread and then handed to the main thread is not rejected.
    /// </remarks>
    internal struct ThreadGuard
    {
#if DEBUG
        private int _ownerThreadId;
        private byte _hasOwner;
#endif

        /// <summary>Records the current thread as owner, or throws when a different thread writes.</summary>
        public void AssertWriteThread()
        {
#if DEBUG
            int current = Environment.CurrentManagedThreadId;
            if (_hasOwner == 0)
            {
                _ownerThreadId = current;
                _hasOwner = 1;
                return;
            }

            if (_ownerThreadId != current)
            {
                throw new InvalidOperationException(
                    "Spatial index written from thread " + current +
                    " but owned by thread " + _ownerThreadId +
                    ". The index is single-writer; synchronize externally or use one index per thread.");
            }
#endif
        }
    }
}
