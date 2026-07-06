using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Unity-facing companion to <see cref="IResourceProvider"/> that returns the concrete engine asset behind a
    /// reference. Backend adapters (audio, animation, VFX) depend on this to fetch the object they need to play,
    /// while the engine-free Core keeps its handles type-agnostic. Resolution succeeds only for references that
    /// are loaded (typically via <see cref="PreloadRunner"/>); adapters treat a false result as "not ready" and
    /// log a warning rather than stalling the frame.
    /// </summary>
    public interface IUnityChoreographyResourceResolver
    {
        bool TryGetAsset<TAsset>(in ChoreographyResourceReference reference, out TAsset asset) where TAsset : Object;
    }
}
