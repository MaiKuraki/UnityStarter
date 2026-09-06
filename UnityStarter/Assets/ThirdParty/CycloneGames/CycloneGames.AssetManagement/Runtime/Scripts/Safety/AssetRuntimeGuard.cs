using System;
using System.Runtime.CompilerServices;
using System.Threading;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AssetRuntimeGuard
    {
        private static long _nextHandleId;

        // Delegating wrapper: the CallerMemberName argument is filled at this call site with the
        // original caller's member name, then passed through explicitly so exception messages are
        // unchanged for existing callers.
        public static void EnsureMainThread([CallerMemberName] string operation = null)
        {
            AssetRuntimeAssertions.EnsureMainThread(operation);
        }

        public static long NextHandleId()
        {
            long id = Interlocked.Increment(ref _nextHandleId);
            if (id > 0L)
            {
                HandleTracker.NotifyHandleCreated();
                return id;
            }

            throw new InvalidOperationException("Asset handle identifier space is exhausted.");
        }

        public static bool IsRecoverableException(Exception exception)
        {
            return AssetRuntimeAssertions.IsRecoverableException(exception);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
#if UNITY_EDITOR
            int survivingCacheOwners = Cache.AssetCacheService.BeginEditorDiagnosticsEpoch();
#endif
            HandleTracker.Reset();
#if UNITY_EDITOR
            if (survivingCacheOwners > 0)
            {
                // Domain-reload-disabled Play Mode can retain externally owned services. The weak monitor does
                // not own or dispose them, but the new handle observation epoch cannot be presented as complete.
                HandleTracker.MarkObservationIncomplete();
            }
#endif
            bool survivingSceneOwners = SceneTracker.Reset();
            if (survivingSceneOwners)
            {
                SceneTracker.MarkObservationIncomplete();
            }
        }
    }
}
