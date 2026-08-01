using System.Threading;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Separates caller-visible owner retirement from retryable provider release completion.
    /// </summary>
    internal static class ProviderReleaseStateMachine
    {
        private const int STATE_ACTIVE = 0;
        private const int STATE_RELEASING = 1;
        private const int STATE_RELEASE_FAILED = 2;
        private const int STATE_RELEASED = 3;

        public static bool IsOwnerRetired(ref int state)
        {
            return Volatile.Read(ref state) != STATE_ACTIVE;
        }

        public static bool IsReleased(ref int state)
        {
            return Volatile.Read(ref state) == STATE_RELEASED;
        }

        public static bool TryBeginRelease(ref int state)
        {
            while (true)
            {
                int observed = Volatile.Read(ref state);
                if (observed == STATE_RELEASING || observed == STATE_RELEASED)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref state, STATE_RELEASING, observed) == observed)
                {
                    return true;
                }
            }
        }

        public static void MarkReleaseFailed(ref int state)
        {
            Volatile.Write(ref state, STATE_RELEASE_FAILED);
        }

        public static void MarkReleased(ref int state)
        {
            Volatile.Write(ref state, STATE_RELEASED);
        }
    }
}
