using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
    /// <summary>
    /// Estimates the approximate runtime memory footprint of a Unity asset.
    /// Used by <see cref="AssetCacheService"/> to drive memory-budget eviction in addition
    /// to entry-count limits, so a few large assets cannot silently blow the memory budget.
    /// <para>
    /// In Editor/Development builds the precise profiler value is used. In release builds the
    /// profiler value is unreliable, so a cheap allocation-free heuristic per asset kind is used.
    /// </para>
    /// </summary>
    internal static class AssetMemoryEstimator
    {
        // Conservative default for unknown asset kinds (e.g. ScriptableObject configs).
        private const long DefaultBytes = 64L * 1024;

        public static long Estimate(Object obj)
        {
            if (obj == null) return 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // The profiler reports the real GPU+CPU footprint, but is only reliable in dev builds.
            long profiled = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(obj);
            if (profiled > 0) return profiled;
#endif

            switch (obj)
            {
                case Texture2D tex2D:
                    return EstimateTexture(tex2D.width, tex2D.height, tex2D.mipmapCount);
                case Cubemap cube:
                    return EstimateTexture(cube.width, cube.height, cube.mipmapCount) * 6;
                case Texture tex:
                    return EstimateTexture(tex.width, tex.height, 1);
                case Mesh mesh:
                    // ~48 bytes/vertex covers position+normal+tangent+uv+color on average.
                    return (long)System.Math.Max(mesh.vertexCount, 1) * 48L;
                case AudioClip clip:
                    return (long)System.Math.Max(clip.samples, 1) * System.Math.Max(clip.channels, 1) * 2L;
                default:
                    return DefaultBytes;
            }
        }

        private static long EstimateTexture(int width, int height, int mipmapCount)
        {
            long basePixels = (long)System.Math.Max(width, 1) * System.Math.Max(height, 1);
            // Assume 4 bytes/pixel (RGBA32-equivalent) as a platform-agnostic upper bound.
            long bytes = basePixels * 4L;
            // Mipmaps add ~1/3 extra.
            if (mipmapCount > 1) bytes += bytes / 3L;
            return bytes;
        }
    }
}
