using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
    /// <summary>
    /// Estimates the approximate runtime memory footprint of a Unity asset.
    /// Used by <see cref="AssetCacheService"/> to drive memory-budget eviction in addition
    /// to entry-count limits, so a few large assets cannot silently blow the memory budget.
    /// The Unity runtime size is queried whenever an entry becomes idle. If the platform cannot report a positive
    /// value, an allocation-free type-specific heuristic is used. Compressed formats use their real block footprint
    /// under a conservative 4x4 block assumption; HDR formats use their real channel widths. Neither value includes
    /// all transitive bundle, allocator, streaming, driver, or GPU residency costs.
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
                    return EstimateTexture(tex2D.width, tex2D.height, tex2D.mipmapCount, tex2D.graphicsFormat);
                case Cubemap cube:
                    return EstimateTexture(cube.width, cube.height, cube.mipmapCount, GraphicsFormat.None) * 6;
                case Texture tex:
                    return EstimateTexture(tex.width, tex.height, 1, GraphicsFormat.None);
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

        internal static long EstimateTexture(int width, int height, int mipmapCount, GraphicsFormat format)
        {
            long baseBytes = EstimateMipZeroBytes(width, height, format);
            // Mipmaps add ~1/3 extra.
            if (mipmapCount > 1) baseBytes += baseBytes / 3L;
            return baseBytes;
        }

        private static long EstimateMipZeroBytes(int width, int height, GraphicsFormat format)
        {
            long pixelWidth = System.Math.Max(width, 1);
            long pixelHeight = System.Math.Max(height, 1);

            int blockSize = (int)GraphicsFormatUtility.GetBlockSize(format);
            if (blockSize > 0)
            {
                if (GraphicsFormatUtility.IsCompressedFormat(format))
                {
                    // Conservative 4x4 block assumption: larger ASTC blocks are overestimated, which is the safe
                    // direction for a memory budget.
                    long blocksX = (pixelWidth + 3L) / 4L;
                    long blocksY = (pixelHeight + 3L) / 4L;
                    return blocksX * blocksY * blockSize;
                }

                return pixelWidth * pixelHeight * blockSize;
            }

            return pixelWidth * pixelHeight * FallbackPixelBytes(format);
        }

        private static int FallbackPixelBytes(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R32G32B32A32_SFloat:
                    return 16;
                case GraphicsFormat.R16G16B16A16_SFloat:
                    return 8;
                case GraphicsFormat.R32G32_SFloat:
                    return 8;
                case GraphicsFormat.R16G16_SFloat:
                    return 4;
                case GraphicsFormat.R32G32_SInt:
                case GraphicsFormat.R32G32_UInt:
                    return 8;
                default:
                    // RGBA32-equivalent conservative floor for unknown formats. Compressed and HDR formats are
                    // handled by the block-size path above.
                    return 4;
            }
        }
    }
}
