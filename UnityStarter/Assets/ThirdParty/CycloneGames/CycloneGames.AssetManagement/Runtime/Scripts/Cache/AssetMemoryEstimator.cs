using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
    /// <summary>
    /// Estimates the approximate runtime memory footprint of a Unity asset.
    /// Used by <see cref="AssetCacheService"/> to drive memory-budget eviction in addition
    /// to entry-count limits, so a few large assets cannot silently blow the memory budget.
    /// The Unity runtime size is queried whenever an entry becomes idle. If the platform cannot report a positive
    /// value, an allocation-free type-specific heuristic is used. Neither value includes all transitive bundle,
    /// allocator, streaming, driver, or GPU residency costs.
    /// </summary>
    internal static class AssetMemoryEstimator
    {
        public static long Estimate(Object obj)
        {
            if (obj == null) return 0;

            // This native query is kept off the acquire path and runs only when an entry becomes idle.
            long profiled = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(obj);
            if (profiled > 0) return profiled;

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
                    // Unknown is safer than inventing a small positive estimate that would let an
                    // unbounded retained payload bypass the cache's byte budget.
                    return 0L;
            }
        }

        public static bool TryAddToAggregate(Object obj, ref long total)
        {
            long estimate = Estimate(obj);
            if (estimate <= 0L || total < 0L || total > long.MaxValue - estimate)
            {
                total = 0L;
                return false;
            }

            total += estimate;
            return true;
        }

        private static long EstimateTexture(int width, int height, int mipmapCount)
        {
            long basePixels = (long)System.Math.Max(width, 1) * System.Math.Max(height, 1);
            // RGBA32-equivalent fallback. Compressed and HDR formats may differ substantially.
            long bytes = basePixels * 4L;
            // Mipmaps add ~1/3 extra.
            if (mipmapCount > 1) bytes += bytes / 3L;
            return bytes;
        }
    }
}
