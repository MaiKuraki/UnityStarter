using System;
using System.Runtime.CompilerServices;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Leaf safety primitives shared by the runtime guard and the diagnostics trackers.
    /// This type must stay dependency-free: any static-class dependency added here re-closes the
    /// Guard &lt;-&gt; Tracker call cycles that this extraction broke (analyzer rule CG0048).
    /// </summary>
    internal static class AssetRuntimeAssertions
    {
        public static void EnsureMainThread([CallerMemberName] string operation = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    $"Asset operation '{operation}' must run on the Unity main thread.");
            }
        }

        public static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not AccessViolationException;
        }
    }
}
