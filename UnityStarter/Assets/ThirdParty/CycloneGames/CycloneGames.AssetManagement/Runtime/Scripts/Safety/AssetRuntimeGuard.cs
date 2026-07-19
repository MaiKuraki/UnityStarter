using System;
using System.Runtime.CompilerServices;
using System.Threading;

using Cysharp.Threading.Tasks;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AssetRuntimeGuard
    {
        private static long _nextHandleId;

        public static void EnsureMainThread([CallerMemberName] string operation = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    $"Asset operation '{operation}' must run on the Unity main thread.");
            }
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
            return exception is not OutOfMemoryException &&
                   exception is not AccessViolationException;
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
