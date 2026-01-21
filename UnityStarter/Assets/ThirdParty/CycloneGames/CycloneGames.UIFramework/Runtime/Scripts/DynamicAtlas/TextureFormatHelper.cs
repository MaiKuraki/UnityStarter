using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Utility class for texture format handling, block size calculations, and platform compatibility.
    /// </summary>
    public static class TextureFormatHelper
    {
        /// <summary>
        /// Gets the block size for a given texture format.
        /// Compressed formats require textures to be aligned to their block size.
        /// </summary>
        public static int GetBlockSize(TextureFormat format)
        {
            switch (format)
            {
                // ASTC formats
                case TextureFormat.ASTC_4x4:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_4x4:
#endif
                    return 4;

                case TextureFormat.ASTC_5x5:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_5x5:
#endif
                    return 5;

                case TextureFormat.ASTC_6x6:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_6x6:
#endif
                    return 6;

                case TextureFormat.ASTC_8x8:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_8x8:
#endif
                    return 8;

                case TextureFormat.ASTC_10x10:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_10x10:
#endif
                    return 10;

                case TextureFormat.ASTC_12x12:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_12x12:
#endif
                    return 12;

                // ETC/ETC2 formats (4x4 block)
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA1:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.EAC_R:
                case TextureFormat.EAC_R_SIGNED:
                case TextureFormat.EAC_RG:
                case TextureFormat.EAC_RG_SIGNED:
                    return 4;

                // DXT/BC formats (4x4 block)
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return 4;

                // PVRTC formats (power of 2 requirement, we treat as 4 for simplicity)
                case TextureFormat.PVRTC_RGB2:
                case TextureFormat.PVRTC_RGBA2:
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA4:
                    return 4;

                // Uncompressed formats - no block alignment needed
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGB24:
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RG32:
                case TextureFormat.RGB48:
                case TextureFormat.RGBA64:
                case TextureFormat.RHalf:
                case TextureFormat.RGHalf:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RFloat:
                case TextureFormat.RGFloat:
                case TextureFormat.RGBAFloat:
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Aligns a size to the specified block size (rounds up).
        /// </summary>
        public static int AlignToBlockSize(int size, int blockSize)
        {
            if (blockSize <= 1) return size;
            return ((size + blockSize - 1) / blockSize) * blockSize;
        }

        /// <summary>
        /// Aligns dimensions to the block size of the specified texture format.
        /// </summary>
        public static void AlignDimensions(TextureFormat format, ref int width, ref int height)
        {
            int blockSize = GetBlockSize(format);
            if (blockSize > 1)
            {
                width = AlignToBlockSize(width, blockSize);
                height = AlignToBlockSize(height, blockSize);
            }
        }

        /// <summary>
        /// Checks if the texture format requires block alignment.
        /// </summary>
        public static bool RequiresBlockAlignment(TextureFormat format)
        {
            return GetBlockSize(format) > 1;
        }

        /// <summary>
        /// Checks if the format is supported on the current platform.
        /// </summary>
        public static bool IsFormatSupported(TextureFormat format)
        {
            return SystemInfo.SupportsTextureFormat(format);
        }

        /// <summary>
        /// Gets the recommended uncompressed format for the current platform.
        /// </summary>
        public static TextureFormat GetRecommendedUncompressedFormat()
        {
            // RGBA32 is universally supported
            return TextureFormat.RGBA32;
        }

        /// <summary>
        /// Gets the recommended compressed format for the current platform.
        /// </summary>
        public static TextureFormat GetRecommendedCompressedFormat()
        {
#if UNITY_ANDROID
            // ASTC is widely supported on modern Android devices
            if (SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_4x4))
                return TextureFormat.ASTC_4x4;
            // Fallback to ETC2
            if (SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8))
                return TextureFormat.ETC2_RGBA8;
            return TextureFormat.RGBA32;
#elif UNITY_IOS || UNITY_TVOS
            // ASTC is supported on iOS 8+ (A8 chip and newer)
            if (SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_4x4))
                return TextureFormat.ASTC_4x4;
            return TextureFormat.RGBA32;
#elif UNITY_WEBGL
            // WebGL has limited format support
            return TextureFormat.RGBA32;
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            // Desktop platforms support BC7 (best quality) or DXT5
            if (SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
                return TextureFormat.BC7;
            if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT5))
                return TextureFormat.DXT5;
            return TextureFormat.RGBA32;
#else
            return TextureFormat.RGBA32;
#endif
        }

        /// <summary>
        /// Checks if GPU texture copy is supported for the given format combination.
        /// </summary>
        public static bool CanUseCopyTexture(TextureFormat srcFormat, TextureFormat dstFormat)
        {
#if UNITY_WEBGL
            // WebGL doesn't support Graphics.CopyTexture reliably
            return false;
#else
            var support = SystemInfo.copyTextureSupport;

            // Basic copy support required
            if ((support & CopyTextureSupport.Basic) == 0)
                return false;

            // Same format - basic support is enough
            if (srcFormat == dstFormat)
                return true;

            // Different formats require copy with conversion support
            if ((support & CopyTextureSupport.DifferentTypes) != 0)
                return true;

            return false;
#endif
        }

        /// <summary>
        /// Checks if the platform supports unsafe code operations.
        /// </summary>
        public static bool SupportsUnsafeCode()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return true;
#endif
        }

        /// <summary>
        /// Checks if NativeArray operations are available.
        /// </summary>
        public static bool SupportsNativeArrays()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL has limitations with NativeArray and Burst
            return false;
#else
            return true;
#endif
        }

        /// <summary>
        /// Checks if concurrent collections are available.
        /// </summary>
        public static bool SupportsConcurrentCollections()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL is single-threaded
            return false;
#else
            return true;
#endif
        }
    }
}
