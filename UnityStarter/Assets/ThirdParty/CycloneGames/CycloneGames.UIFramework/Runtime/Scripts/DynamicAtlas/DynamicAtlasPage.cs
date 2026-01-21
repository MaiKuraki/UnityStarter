using System;
using UnityEngine;
using UnityEngine.Rendering;
using CycloneGames.Logger;

#if !UNITY_WEBGL || UNITY_EDITOR
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Represents a single Texture2D atlas page that handles texture insertion using shelf packing algorithm.
    /// Supports block alignment for compressed formats and zero-GC operations on supported platforms.
    /// </summary>
    public class DynamicAtlasPage : IDisposable
    {
        private static int _pageIdCounter = 0;

        public Texture2D Texture { get; private set; }
        public int Width => _width;
        public int Height => _height;
        public bool IsFull { get; private set; }
        public TextureFormat Format => _format;

        private int _activeSpriteCount;
        public int ActiveSpriteCount => _activeSpriteCount;

        private readonly int _width;
        private readonly int _height;
        private readonly int _padding;
        private readonly int _blockSize;
        private readonly TextureFormat _format;
        private readonly CopyTextureSupport _copySupport;
        private readonly int _pageId;
        private readonly bool _enablePlatformOptimizations;
        private bool _needsApply;

        public int PageId => _pageId;

        // Shelf packing state
        private int _currentX;
        private int _currentY;
        private int _maxYInRow;

        // Reusable conversion buffer (for format conversion path)
        private Color32[] _conversionBuffer;
        private int _conversionBufferCapacity;

        public DynamicAtlasPage(int size) : this(size, TextureFormat.RGBA32, 2, true)
        {
        }

        public DynamicAtlasPage(int size, TextureFormat format, int padding = 2, bool enablePlatformOptimizations = true)
        {
            _width = size;
            _height = size;
            _format = format;
            _padding = padding;
            _blockSize = TextureFormatHelper.GetBlockSize(format);
            _enablePlatformOptimizations = enablePlatformOptimizations;
            _copySupport = SystemInfo.copyTextureSupport;
            _pageId = System.Threading.Interlocked.Increment(ref _pageIdCounter);

            InitializeTexture();
        }

        private void InitializeTexture()
        {
            Texture = new Texture2D(_width, _height, _format, false);
            Texture.filterMode = FilterMode.Bilinear;
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.name = "DynamicAtlasPage_" + _pageId;

            // Clear texture to transparent
            ClearTexture();
            _needsApply = false;
        }

        private void ClearTexture()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_enablePlatformOptimizations && TextureFormatHelper.SupportsUnsafeCode())
            {
                try
                {
                    var rawData = Texture.GetRawTextureData<Color32>();
                    unsafe
                    {
                        UnsafeUtility.MemClear(NativeArrayUnsafeUtility.GetUnsafePtr(rawData),
                            rawData.Length * UnsafeUtility.SizeOf<Color32>());
                    }
                    Texture.Apply();
                    return;
                }
                catch (Exception)
                {
                    // Fall through to managed path
                }
            }
#endif
            // Managed fallback (WebGL or when unsafe fails)
            var clearPixels = new Color32[_width * _height];
            Texture.SetPixels32(clearPixels);
            Texture.Apply();
        }

        /// <summary>
        /// Attempts to insert a texture into this page.
        /// </summary>
        /// <param name="source">Source texture to insert</param>
        /// <param name="uvRect">Output UV rectangle for the inserted texture</param>
        /// <returns>True if insertion succeeded</returns>
        public bool TryInsert(Texture2D source, out Rect uvRect)
        {
            return TryInsert(source, out uvRect, out _);
        }

        /// <summary>
        /// Attempts to insert a texture into this page with block alignment.
        /// </summary>
        /// <param name="source">Source texture to insert</param>
        /// <param name="uvRect">Output UV rectangle (based on actual texture size)</param>
        /// <param name="allocatedSize">Actual allocated size including alignment padding</param>
        /// <returns>True if insertion succeeded</returns>
        public bool TryInsert(Texture2D source, out Rect uvRect, out Vector2Int allocatedSize)
        {
            uvRect = default;
            allocatedSize = default;

            if (IsFull) return false;

            int sourceWidth = source.width;
            int sourceHeight = source.height;

            // Calculate allocation size (aligned to block size if needed)
            int allocWidth = sourceWidth;
            int allocHeight = sourceHeight;

            if (_blockSize > 1)
            {
                allocWidth = TextureFormatHelper.AlignToBlockSize(sourceWidth, _blockSize);
                allocHeight = TextureFormatHelper.AlignToBlockSize(sourceHeight, _blockSize);
            }

            int maxWidth = _width - _padding;
            int maxHeight = _height - _padding;

            if (allocWidth > maxWidth || allocHeight > maxHeight)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[DynamicAtlasPage] Texture ({sourceWidth}x{sourceHeight}, aligned: {allocWidth}x{allocHeight}) " +
                    $"is too large for page ({_width}x{_height}, max available: {maxWidth}x{maxHeight} with padding).");
#endif
                return false;
            }

            // Shelf packing: try current row first
            if (_currentX + allocWidth + _padding > _width)
            {
                // Move to next row
                _currentY += _maxYInRow + _padding;
                _currentX = 0;
                _maxYInRow = 0;
            }

            if (_currentY + allocHeight + _padding > _height)
            {
                IsFull = true;
                return false;
            }

            int xPos = _currentX;
            int yPos = _currentY;

            // Copy pixels to the allocated space
            if (CopyPixels(source, xPos, yPos, sourceWidth, sourceHeight, allocWidth, allocHeight))
            {
                _currentX += allocWidth + _padding;
                if (allocHeight > _maxYInRow) _maxYInRow = allocHeight;

                // UV rect is based on ACTUAL texture size, not allocated size
                // This ensures the sprite displays the original image without distortion
                float invWidth = 1.0f / _width;
                float invHeight = 1.0f / _height;
                uvRect.x = xPos * invWidth;
                uvRect.y = yPos * invHeight;
                uvRect.width = sourceWidth * invWidth;
                uvRect.height = sourceHeight * invHeight;

                allocatedSize = new Vector2Int(allocWidth, allocHeight);

                System.Threading.Interlocked.Increment(ref _activeSpriteCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to insert a region from a source texture into this page.
        /// This is optimized for copying sub-regions from SpriteAtlas textures.
        /// </summary>
        /// <param name="sourceTexture">The source texture (e.g., SpriteAtlas texture)</param>
        /// <param name="sourceRect">The region to copy (in pixels, bottom-left origin)</param>
        /// <param name="uvRect">Output UV rectangle for the inserted region</param>
        /// <returns>True if insertion succeeded</returns>
        public bool TryInsertFromRegion(Texture2D sourceTexture, Rect sourceRect, out Rect uvRect)
        {
            uvRect = default;
            if (IsFull) return false;

            int sourceWidth = Mathf.RoundToInt(sourceRect.width);
            int sourceHeight = Mathf.RoundToInt(sourceRect.height);
            int srcX = Mathf.RoundToInt(sourceRect.x);
            int srcY = Mathf.RoundToInt(sourceRect.y);

            // Calculate allocation size (aligned to block size if needed)
            int allocWidth = sourceWidth;
            int allocHeight = sourceHeight;

            if (_blockSize > 1)
            {
                allocWidth = TextureFormatHelper.AlignToBlockSize(sourceWidth, _blockSize);
                allocHeight = TextureFormatHelper.AlignToBlockSize(sourceHeight, _blockSize);
            }

            int maxWidth = _width - _padding;
            int maxHeight = _height - _padding;

            if (allocWidth > maxWidth || allocHeight > maxHeight)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[DynamicAtlasPage] Region ({sourceWidth}x{sourceHeight}, aligned: {allocWidth}x{allocHeight}) " +
                    $"is too large for page ({_width}x{_height}, max available: {maxWidth}x{maxHeight} with padding).");
#endif
                return false;
            }

            // Shelf packing: try current row first
            if (_currentX + allocWidth + _padding > _width)
            {
                _currentY += _maxYInRow + _padding;
                _currentX = 0;
                _maxYInRow = 0;
            }

            if (_currentY + allocHeight + _padding > _height)
            {
                IsFull = true;
                return false;
            }

            int xPos = _currentX;
            int yPos = _currentY;

            // Copy pixels from the source region
            if (CopyPixelsFromRegion(sourceTexture, srcX, srcY, xPos, yPos, sourceWidth, sourceHeight))
            {
                _currentX += allocWidth + _padding;
                if (allocHeight > _maxYInRow) _maxYInRow = allocHeight;

                float invWidth = 1.0f / _width;
                float invHeight = 1.0f / _height;
                uvRect.x = xPos * invWidth;
                uvRect.y = yPos * invHeight;
                uvRect.width = sourceWidth * invWidth;
                uvRect.height = sourceHeight * invHeight;

                System.Threading.Interlocked.Increment(ref _activeSpriteCount);
                return true;
            }

            return false;
        }

        private bool CopyPixelsFromRegion(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0;
            bool gpuCopySuccess = false;

            // Attempt GPU copy (fastest, zero GC allocation)
            if (useCopyTexture && source.format == _format)
            {
                try
                {
                    Graphics.CopyTexture(source, 0, 0, srcX, srcY, w, h, Texture, 0, 0, dstX, dstY);
                    gpuCopySuccess = true;
                }
                catch
                {
                    gpuCopySuccess = false;
                }
            }

            if (gpuCopySuccess) return true;

            // CPU fallback - need readable texture
            if (!source.isReadable)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[DynamicAtlasPage] Insert from region failed. GPU CopyTexture failed " +
                    $"(Supported: {useCopyTexture}, Format Match: {source.format == _format}) " +
                    $"and Source is NOT Readable. Consider enabling Read/Write on the source texture.");
#endif
                return false;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            // Try zero-GC path using NativeArray (non-WebGL platforms)
            if (_enablePlatformOptimizations && TextureFormatHelper.SupportsNativeArrays() &&
                source.format == TextureFormat.RGBA32 && _format == TextureFormat.RGBA32)
            {
                if (TryCopyPixelsFromRegionNative(source, srcX, srcY, dstX, dstY, w, h))
                {
                    return true;
                }
            }
#endif

            // Managed fallback
            return CopyPixelsFromRegionManaged(source, srcX, srcY, dstX, dstY, w, h);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private bool TryCopyPixelsFromRegionNative(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            try
            {
                var srcData = source.GetRawTextureData<Color32>();
                var dstData = Texture.GetRawTextureData<Color32>();

                unsafe
                {
                    Color32* srcPtr = (Color32*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(srcData);
                    Color32* dstPtr = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(dstData);

                    int srcWidth = source.width;
                    int dstWidth = _width;
                    int rowSize = w * UnsafeUtility.SizeOf<Color32>();

                    for (int row = 0; row < h; row++)
                    {
                        Color32* srcRowPtr = srcPtr + ((srcY + row) * srcWidth + srcX);
                        Color32* dstRowPtr = dstPtr + ((dstY + row) * dstWidth + dstX);
                        UnsafeUtility.MemCpy(dstRowPtr, srcRowPtr, rowSize);
                    }
                }

                _needsApply = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif

        private bool CopyPixelsFromRegionManaged(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            int requiredCapacity = w * h;
            if (_conversionBuffer == null || _conversionBufferCapacity < requiredCapacity)
            {
                _conversionBufferCapacity = Mathf.Max(requiredCapacity, _conversionBufferCapacity * 2, 4096);
                _conversionBuffer = new Color32[_conversionBufferCapacity];
            }

            // Get pixels from the specific region
            var regionPixels = source.GetPixels32(0);
            int srcWidth = source.width;

            // Copy region pixels to buffer
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    int srcIndex = (srcY + row) * srcWidth + (srcX + col);
                    int dstIndex = row * w + col;
                    _conversionBuffer[dstIndex] = regionPixels[srcIndex];
                }
            }

            // Create a properly sized array for SetPixels32
            var destPixels = new Color32[w * h];
            Array.Copy(_conversionBuffer, destPixels, w * h);

            Texture.SetPixels32(dstX, dstY, w, h, destPixels);
            _needsApply = true;

            return true;
        }

        /// <summary>
        /// Applies pending texture changes. Call after batch insertions for better performance.
        /// </summary>
        public void ApplyIfNeeded()
        {
            if (_needsApply)
            {
                Texture.Apply();
                _needsApply = false;
            }
        }

        private bool CopyPixels(Texture2D source, int x, int y, int w, int h, int allocW, int allocH)
        {
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0;
            bool gpuCopySuccess = false;

            // Attempt GPU copy (fastest, zero GC allocation)
            // Note: GPU copy only copies the source size, leaving padding as-is (transparent from init)
            if (useCopyTexture && source.format == _format)
            {
                try
                {
                    Graphics.CopyTexture(source, 0, 0, 0, 0, w, h, Texture, 0, 0, x, y);
                    gpuCopySuccess = true;
                }
                catch
                {
                    gpuCopySuccess = false;
                }
            }

            if (gpuCopySuccess) return true;

            // CPU fallback paths
            if (!source.isReadable)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[DynamicAtlasPage] Insert failed. GPU CopyTexture failed " +
                    $"(Supported: {useCopyTexture}, Format Match: {source.format == _format}) " +
                    $"and Source is NOT Readable.");
#endif
                return false;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            // Try zero-GC path using NativeArray (non-WebGL platforms)
            if (_enablePlatformOptimizations && TextureFormatHelper.SupportsNativeArrays() &&
                source.format == TextureFormat.RGBA32 && _format == TextureFormat.RGBA32)
            {
                if (TryCopyPixelsNative(source, x, y, w, h))
                {
                    return true;
                }
            }
#endif

            // Managed fallback with buffer reuse
            return CopyPixelsManaged(source, x, y, w, h);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private bool TryCopyPixelsNative(Texture2D source, int x, int y, int w, int h)
        {
            try
            {
                var srcData = source.GetRawTextureData<Color32>();
                var dstData = Texture.GetRawTextureData<Color32>();

                unsafe
                {
                    Color32* srcPtr = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(srcData);
                    Color32* dstPtr = (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(dstData);

                    int srcWidth = source.width;
                    int dstWidth = _width;
                    int rowSize = w * UnsafeUtility.SizeOf<Color32>();

                    for (int row = 0; row < h; row++)
                    {
                        Color32* srcRowPtr = srcPtr + (row * srcWidth);
                        Color32* dstRowPtr = dstPtr + ((y + row) * dstWidth + x);
                        UnsafeUtility.MemCpy(dstRowPtr, srcRowPtr, rowSize);
                    }
                }

                _needsApply = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif

        private bool CopyPixelsManaged(Texture2D source, int x, int y, int w, int h)
        {
            // Get or resize conversion buffer
            int requiredCapacity = w * h;
            if (_conversionBuffer == null || _conversionBufferCapacity < requiredCapacity)
            {
                // Allocate with some headroom to reduce future allocations
                _conversionBufferCapacity = Mathf.Max(requiredCapacity, _conversionBufferCapacity * 2, 4096);
                _conversionBuffer = new Color32[_conversionBufferCapacity];
            }

            // Get pixels into buffer
            var sourcePixels = source.GetPixels32();

            // Copy only required pixels
            int copyLength = Mathf.Min(sourcePixels.Length, requiredCapacity);
            Array.Copy(sourcePixels, 0, _conversionBuffer, 0, copyLength);

            // SetPixels32 with x,y offset
            Texture.SetPixels32(x, y, w, h, sourcePixels);
            _needsApply = true;

            return true;
        }

        public void DecrementActiveCount()
        {
            int newCount = System.Threading.Interlocked.Decrement(ref _activeSpriteCount);
            if (newCount < 0)
            {
                System.Threading.Interlocked.CompareExchange(ref _activeSpriteCount, 0, newCount);
            }
        }

        public bool IsEmpty => _activeSpriteCount == 0;

        public void Dispose()
        {
            if (Texture != null)
            {
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }

            _conversionBuffer = null;
            _conversionBufferCapacity = 0;
        }
    }
}