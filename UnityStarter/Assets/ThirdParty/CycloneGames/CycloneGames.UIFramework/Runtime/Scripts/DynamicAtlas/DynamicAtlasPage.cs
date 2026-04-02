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

#if UNITY_EDITOR
        public int Padding => _padding;
#endif

        private int _activeSpriteCount;
        public int ActiveSpriteCount => _activeSpriteCount;

        private long _usedPixelArea;
        public long UsedPixelArea => _usedPixelArea;

        /// <summary>
        /// Returns a value between 0 and 1, where 1 means completely empty/fragmented and 0 means optimally packed.
        /// </summary>
        public float FragmentationRatio => 1.0f - (float)_usedPixelArea / ((long)_width * _height);

        private readonly int _width;
        private readonly int _height;
        private readonly int _padding;
        private readonly int _blockSize;
        private readonly TextureFormat _format;
        private readonly CopyTextureSupport _copySupport;
        private readonly int _pageId;
        private readonly bool _enablePlatformOptimizations;
        private readonly bool _enableBleed;
        private readonly bool _enableMipmap;
        private bool _needsApply;

        public int PageId => _pageId;

#if UNITY_EDITOR
        public int CurrentX => _currentX;
        public int CurrentY => _currentY;
        public int MaxYInRow => _maxYInRow;
#endif

        // Shelf packing state
        private int _currentX;
        private int _currentY;
        private int _maxYInRow;

        public DynamicAtlasPage(int size) : this(size, TextureFormat.RGBA32, 2, true, true, false)
        {
        }

        public DynamicAtlasPage(int size, TextureFormat format, int padding = 2, bool enablePlatformOptimizations = true, bool enableBleed = true, bool enableMipmap = false)
        {
            _width = size;
            _height = size;
            _format = format;
            _blockSize = TextureFormatHelper.GetBlockSize(format);
            _enablePlatformOptimizations = enablePlatformOptimizations;
            _enableMipmap = enableMipmap;
            _copySupport = SystemInfo.copyTextureSupport;
            _pageId = System.Threading.Interlocked.Increment(ref _pageIdCounter);

            // Bleed writes 1px outside sprite bounds; padding >= 2 prevents inter-sprite bleed overlap
            if (enableBleed && padding < 2 && _blockSize <= 1)
            {
                padding = 2;
            }
            _padding = padding;
            _enableBleed = enableBleed && (padding >= 2) && (_blockSize <= 1);

            InitializeTexture();
        }

        private void InitializeTexture()
        {
            Texture = new Texture2D(_width, _height, _format, _enableMipmap);
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

                return true;
            }

            return false;
        }

        private bool CopyPixelsFromRegion(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            // When mipmap enabled, use Blit+ReadPixels path so Apply() regenerates mip levels
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0 && !_enableMipmap;
            bool gpuCopySuccess = false;

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

            if (gpuCopySuccess)
            {
                if (_enableBleed) GenerateBleedGPU(source, srcX, srcY, dstX, dstY, w, h);
                return true;
            }

            bool result = CopyPixelsFromRegionViaRT(source, srcX, srcY, dstX, dstY, w, h);
            if (result && _enableBleed) GenerateBleedCPU(dstX, dstY, w, h);
            return result;
        }

        private bool CopyPixelsFromRegionViaRT(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            // Create a temporary RT of the exact region size
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);

            // Blit using scale and offset to extract just the region
            Vector2 scale = new Vector2((float)w / source.width, (float)h / source.height);
            Vector2 offset = new Vector2((float)srcX / source.width, (float)srcY / source.height);
            Graphics.Blit(source, rt, scale, offset);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            // Read directly into our Texture2D (CPU side)
            Texture.ReadPixels(new Rect(0, 0, w, h), dstX, dstY);
            _needsApply = true;

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return true;
        }

        /// <summary>
        /// Generates 1px bleed (gutter) around a sprite via GPU CopyTexture.
        /// Copies edge pixel rows/columns into the adjacent padding region to prevent
        /// bilinear filtering artifacts at sprite boundaries.
        /// </summary>
        private void GenerateBleedGPU(Texture2D source, int srcX, int srcY, int dstX, int dstY, int w, int h)
        {
            try
            {
                // Left edge: copy 1px column from left side of sprite to padding area
                if (dstX > 0)
                    Graphics.CopyTexture(source, 0, 0, srcX, srcY, 1, h, Texture, 0, 0, dstX - 1, dstY);
                // Right edge
                if (dstX + w < _width)
                    Graphics.CopyTexture(source, 0, 0, srcX + w - 1, srcY, 1, h, Texture, 0, 0, dstX + w, dstY);
                // Bottom edge
                if (dstY > 0)
                    Graphics.CopyTexture(source, 0, 0, srcX, srcY, w, 1, Texture, 0, 0, dstX, dstY - 1);
                // Top edge
                if (dstY + h < _height)
                    Graphics.CopyTexture(source, 0, 0, srcX, srcY + h - 1, w, 1, Texture, 0, 0, dstX, dstY + h);
            }
            catch
            {
                // Non-critical: bleed failure only causes minor visual artifacts
            }
        }

        /// <summary>
        /// Generates 1px bleed around a sprite in CPU/ReadPixels path.
        /// Reads edge pixels from the already-written region in the atlas texture.
        /// </summary>
        private void GenerateBleedCPU(int dstX, int dstY, int w, int h)
        {
            try
            {
                // Left edge
                if (dstX > 0)
                {
                    var col = Texture.GetPixels(dstX, dstY, 1, h);
                    Texture.SetPixels(dstX - 1, dstY, 1, h, col);
                }
                // Right edge
                if (dstX + w < _width)
                {
                    var col = Texture.GetPixels(dstX + w - 1, dstY, 1, h);
                    Texture.SetPixels(dstX + w, dstY, 1, h, col);
                }
                // Bottom edge
                if (dstY > 0)
                {
                    var row = Texture.GetPixels(dstX, dstY, w, 1);
                    Texture.SetPixels(dstX, dstY - 1, w, 1, row);
                }
                // Top edge
                if (dstY + h < _height)
                {
                    var row = Texture.GetPixels(dstX, dstY + h - 1, w, 1);
                    Texture.SetPixels(dstX, dstY + h, w, 1, row);
                }
                _needsApply = true;
            }
            catch
            {
                // Non-critical: bleed failure only causes minor visual artifacts
            }
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
            // When mipmap enabled, use Blit+ReadPixels path so Apply() regenerates mip levels
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0 && !_enableMipmap;
            bool gpuCopySuccess = false;

            // Attempt GPU copy (fastest, zero GC allocation)
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

            if (gpuCopySuccess)
            {
                if (_enableBleed) GenerateBleedGPU(source, 0, 0, x, y, w, h);
                return true;
            }

            // GPU Fallback via RT Bridge (always works regardless of readability)
            bool result = CopyPixelsViaRT(source, x, y, w, h);
            if (result && _enableBleed) GenerateBleedCPU(x, y, w, h);
            return result;
        }

        private bool CopyPixelsViaRT(Texture2D source, int x, int y, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            // Read directly into our Texture2D (CPU side)
            Texture.ReadPixels(new Rect(0, 0, w, h), x, y);
            _needsApply = true;

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return true;
        }

        public void IncrementActiveCount(int width, int height)
        {
            System.Threading.Interlocked.Increment(ref _activeSpriteCount);
            System.Threading.Interlocked.Add(ref _usedPixelArea, (long)width * height);
        }

        public void DecrementActiveCount(int width, int height)
        {
            int newCount = System.Threading.Interlocked.Decrement(ref _activeSpriteCount);
            if (newCount < 0)
            {
                System.Threading.Interlocked.CompareExchange(ref _activeSpriteCount, 0, newCount);
            }
            long newArea = System.Threading.Interlocked.Add(ref _usedPixelArea, -((long)width * height));
            if (newArea < 0)
            {
                System.Threading.Interlocked.CompareExchange(ref _usedPixelArea, 0, newArea);
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
        }
    }
}