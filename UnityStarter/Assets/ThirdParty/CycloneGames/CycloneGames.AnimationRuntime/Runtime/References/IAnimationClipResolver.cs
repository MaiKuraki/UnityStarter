using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Resolves an <see cref="AnimationClipReference"/> to a concrete clip. Implemented by the host: an
    /// AssetManagement adapter, a path resolver, a Resources resolver or a test stub.
    ///
    /// Calls are synchronous and are not expected to load; clips are preloaded before playback. The
    /// implementation owns its cache and unload policy, and the runtime borrows the returned clip.
    /// </summary>
    public interface IAnimationClipResolver
    {
        /// <summary>Attempts to resolve the reference. Returns false when the clip is unknown or not loaded.</summary>
        bool TryResolve(in AnimationClipReference reference, out AnimationClip clip);
    }

    /// <summary>Default resolver: resolves embedded clips only; location references return false.</summary>
    public sealed class EmbeddedAnimationClipResolver : IAnimationClipResolver
    {
        public static readonly EmbeddedAnimationClipResolver Instance = new EmbeddedAnimationClipResolver();

        private EmbeddedAnimationClipResolver()
        {
        }

        public bool TryResolve(in AnimationClipReference reference, out AnimationClip clip)
        {
            return reference.TryGetEmbeddedClip(out clip);
        }
    }
}
