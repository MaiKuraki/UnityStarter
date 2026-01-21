using System;
using UnityEngine;
using UnityEngine.Rendering;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Specialized atlas page for compressed textures (ASTC/ETC2/BC).
    /// Uses GPU CopyTexture for zero-GC, zero-CPU direct block copy between compressed textures.
    /// 
    /// Key constraint: Source and destination must have EXACTLY the same TextureFormat.
    /// </summary>
    public class CompressedAtlasPage : IDisposable
    {
        private static int _pageIdCounter = 0;

        public Texture2D Texture { get; private set; }
        public int Width => _width;
        public int Height => _height;
        public bool IsFull { get; private set; }
        public TextureFormat Format => _format;
        public int PageId => _pageId;

        private int _activeSpriteCount;
        public int ActiveSpriteCount => _activeSpriteCount;
        public bool IsEmpty => _activeSpriteCount == 0;

        private readonly int _width;
        private readonly int _height;
        private readonly int _padding;
        private readonly int _blockSize;
        private readonly TextureFormat _format;
        private readonly int _pageId;
        private readonly bool _gpuCopySupported;

        // Shelf packing state (in block units for compressed textures)
        private int _currentBlockX;
        private int _currentBlockY;
        private int _maxBlockYInRow;

        /// <summary>
        /// Creates a compressed atlas page with pre-allocated texture.
        /// </summary>
        /// <param name="size">Page size in pixels (must be aligned to block size)</param>
        /// <param name="format">Compressed texture format (ASTC/ETC2/BC)</param>
        /// <param name="padding">Padding between sprites in blocks (not pixels)</param>
        public CompressedAtlasPage(int size, TextureFormat format, int padding = 1)
        {
            _blockSize = TextureFormatHelper.GetBlockSize(format);

            if (_blockSize <= 1)
            {
                throw new ArgumentException($"CompressedAtlasPage requires a compressed format. {format} is not compressed.");
            }

            // Ensure size is aligned to block size
            _width = TextureFormatHelper.AlignToBlockSize(size, _blockSize);
            _height = _width;
            _format = format;
            _padding = padding; // Padding is in blocks, not pixels
            _pageId = System.Threading.Interlocked.Increment(ref _pageIdCounter);

            // Check GPU CopyTexture support
            var copySupport = SystemInfo.copyTextureSupport;
            _gpuCopySupported = (copySupport & CopyTextureSupport.Basic) != 0;

            if (!_gpuCopySupported)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogWarning($"[CompressedAtlasPage] GPU CopyTexture not supported on this platform. " +
                    "Compressed atlas will not work correctly.");
#endif
            }

            InitializeTexture();
        }

        private void InitializeTexture()
        {
            // Create compressed texture
            // For compressed formats, we need to allocate with raw data
            int blockCountX = _width / _blockSize;
            int blockCountY = _height / _blockSize;
            int bytesPerBlock = GetBytesPerBlock(_format);
            int dataSize = blockCountX * blockCountY * bytesPerBlock;

            // Create texture and set raw data to zero (transparent)
            Texture = new Texture2D(_width, _height, _format, false);
            Texture.filterMode = FilterMode.Bilinear;
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.name = $"CompressedAtlasPage_{_pageId}_{_format}";

            // Initialize with transparent data
            byte[] clearData = new byte[dataSize];
            Texture.LoadRawTextureData(clearData);
            Texture.Apply(false, false); // Don't make non-readable, we may need to update

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CLogger.LogInfo($"[CompressedAtlasPage] Created {_width}x{_height} {_format} page. " +
                $"Block size: {_blockSize}, Total blocks: {blockCountX}x{blockCountY}");
#endif
        }

        /// <summary>
        /// Gets the bytes per block for the compressed format.
        /// </summary>
        private static int GetBytesPerBlock(TextureFormat format)
        {
            switch (format)
            {
                // ASTC formats - all have 16 bytes per block regardless of block size
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_5x5:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_4x4:
                case TextureFormat.ASTC_HDR_5x5:
                case TextureFormat.ASTC_HDR_6x6:
                case TextureFormat.ASTC_HDR_8x8:
                case TextureFormat.ASTC_HDR_10x10:
                case TextureFormat.ASTC_HDR_12x12:
#endif
                    return 16;

                // ETC2 formats
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC_RGB4:
                    return 8;
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ETC2_RGBA1:
                    return 16;

                // EAC formats
                case TextureFormat.EAC_R:
                case TextureFormat.EAC_R_SIGNED:
                    return 8;
                case TextureFormat.EAC_RG:
                case TextureFormat.EAC_RG_SIGNED:
                    return 16;

                // BC formats
                case TextureFormat.DXT1:
                case TextureFormat.BC4:
                    return 8;
                case TextureFormat.DXT5:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return 16;

                default:
                    return 16; // Safe default
            }
        }

        /// <summary>
        /// Attempts to insert a region from a compressed source texture.
        /// Uses GPU CopyTexture for zero-GC, zero-CPU direct block copy.
        /// 
        /// CRITICAL: Source texture MUST have the same TextureFormat as this page!
        /// </summary>
        /// <param name="sourceTexture">Source compressed texture (must match this page's format)</param>
        /// <param name="sourceRect">Region to copy (in pixels, will be aligned to block boundaries)</param>
        /// <param name="uvRect">Output UV rectangle for the inserted region</param>
        /// <returns>True if insertion succeeded</returns>
        public bool TryInsertFromRegion(Texture2D sourceTexture, Rect sourceRect, out Rect uvRect)
        {
            uvRect = default;

            if (IsFull) return false;

            // Validate format match
            if (sourceTexture.format != _format)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[CompressedAtlasPage] Format mismatch! Source: {sourceTexture.format}, Page: {_format}. " +
                    "Compressed copy requires exact format match.");
#endif
                return false;
            }

            if (!_gpuCopySupported)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError("[CompressedAtlasPage] GPU CopyTexture not supported. Cannot copy compressed textures.");
#endif
                return false;
            }

            // Convert pixel coordinates to block coordinates
            int srcPixelX = Mathf.RoundToInt(sourceRect.x);
            int srcPixelY = Mathf.RoundToInt(sourceRect.y);
            int srcPixelW = Mathf.RoundToInt(sourceRect.width);
            int srcPixelH = Mathf.RoundToInt(sourceRect.height);

            // Align to block boundaries (round down for position, round up for size)
            int srcBlockX = srcPixelX / _blockSize;
            int srcBlockY = srcPixelY / _blockSize;
            int srcBlockW = (srcPixelW + _blockSize - 1) / _blockSize;
            int srcBlockH = (srcPixelH + _blockSize - 1) / _blockSize;

            // Convert back to aligned pixel coordinates
            int alignedSrcX = srcBlockX * _blockSize;
            int alignedSrcY = srcBlockY * _blockSize;
            int alignedWidth = srcBlockW * _blockSize;
            int alignedHeight = srcBlockH * _blockSize;

            int pageBlocksX = _width / _blockSize;
            int pageBlocksY = _height / _blockSize;

            // Check if it fits in current row
            if (_currentBlockX + srcBlockW + _padding > pageBlocksX)
            {
                _currentBlockY += _maxBlockYInRow + _padding;
                _currentBlockX = 0;
                _maxBlockYInRow = 0;
            }

            if (_currentBlockY + srcBlockH + _padding > pageBlocksY)
            {
                IsFull = true;
                return false;
            }

            int dstBlockX = _currentBlockX;
            int dstBlockY = _currentBlockY;
            int dstPixelX = dstBlockX * _blockSize;
            int dstPixelY = dstBlockY * _blockSize;

            // GPU CopyTexture - direct block copy, zero CPU, zero GC
            try
            {
                Graphics.CopyTexture(
                    sourceTexture, 0, 0,
                    alignedSrcX, alignedSrcY, alignedWidth, alignedHeight,
                    Texture, 0, 0,
                    dstPixelX, dstPixelY
                );
            }
            catch (Exception e)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[CompressedAtlasPage] GPU CopyTexture failed: {e.Message}");
#endif
                return false;
            }

            // Update packing state
            _currentBlockX += srcBlockW + _padding;
            if (srcBlockH > _maxBlockYInRow) _maxBlockYInRow = srcBlockH;

            // Calculate UV rect based on ORIGINAL sprite size (not aligned size)
            // This ensures the sprite displays correctly without extra padding pixels
            float invWidth = 1.0f / _width;
            float invHeight = 1.0f / _height;

            // UV position is at the aligned block boundary
            uvRect.x = dstPixelX * invWidth;
            uvRect.y = dstPixelY * invHeight;

            // UV size is based on original requested size
            // Add offset for sub-block alignment if source wasn't block-aligned
            int subBlockOffsetX = srcPixelX - alignedSrcX;
            int subBlockOffsetY = srcPixelY - alignedSrcY;
            uvRect.x += subBlockOffsetX * invWidth;
            uvRect.y += subBlockOffsetY * invHeight;
            uvRect.width = srcPixelW * invWidth;
            uvRect.height = srcPixelH * invHeight;

            System.Threading.Interlocked.Increment(ref _activeSpriteCount);
            return true;
        }

        /// <summary>
        /// Attempts to insert a Sprite from a compressed SpriteAtlas.
        /// </summary>
        public bool TryInsertSprite(Sprite sourceSprite, out Rect uvRect)
        {
            if (sourceSprite == null || sourceSprite.texture == null)
            {
                uvRect = default;
                return false;
            }

            return TryInsertFromRegion(sourceSprite.texture, sourceSprite.rect, out uvRect);
        }

        public void DecrementActiveCount()
        {
            int newCount = System.Threading.Interlocked.Decrement(ref _activeSpriteCount);
            if (newCount < 0)
            {
                System.Threading.Interlocked.CompareExchange(ref _activeSpriteCount, 0, newCount);
            }
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
        }
    }
}
