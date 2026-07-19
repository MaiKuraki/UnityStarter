using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public static class TextureFormatHelper
    {
        internal static int GetBytesPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                    return 4;
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    return 4;
                case TextureFormat.RGBA4444:
                    return 2;
                case TextureFormat.Alpha8:
                    return 1;
                default:
                    return 0;
            }
        }

        public static long EstimatePageBytes(int pageSize, DynamicAtlasCopyFallback fallback)
        {
            if (pageSize <= 0 ||
                (fallback != DynamicAtlasCopyFallback.GpuOnly &&
                 fallback != DynamicAtlasCopyFallback.AllowSynchronousReadback &&
                 fallback != DynamicAtlasCopyFallback.AllowCpuRawCopy))
            {
                return long.MaxValue;
            }

            try
            {
                long gpuBytes = checked((long)pageSize * pageSize * 4L);
                return AllowsCpuBacking(fallback)
                    ? checked(gpuBytes * 2L)
                    : gpuBytes;
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

        internal static bool AllowsCpuBacking(DynamicAtlasCopyFallback fallback)
        {
            return fallback == DynamicAtlasCopyFallback.AllowCpuRawCopy ||
                   fallback == DynamicAtlasCopyFallback.AllowSynchronousReadback;
        }

        internal static bool AllowsSynchronousReadback(DynamicAtlasCopyFallback fallback)
        {
            return fallback == DynamicAtlasCopyFallback.AllowSynchronousReadback;
        }

        internal static bool CanUseGpuCopy(Texture source, Texture2D destination)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            CopyTextureSupport support = SystemInfo.copyTextureSupport;
            return (support & CopyTextureSupport.Basic) != 0 &&
                   source.graphicsFormat == destination.graphicsFormat;
        }
    }
}
