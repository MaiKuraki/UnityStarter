using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Represents a single Texture2D atlas page that handles texture insertion using shelf packing algorithm.
    /// </summary>
    public class DynamicAtlasPage : IDisposable
    {
        private const int Padding = 2;
        private static int _pageIdCounter = 0;

        public Texture2D Texture { get; private set; }
        public int Width => _width;
        public int Height => _height;
        public bool IsFull { get; private set; }

        private int _activeSpriteCount;
        public int ActiveSpriteCount => _activeSpriteCount;

        private readonly int _width;
        private readonly int _height;
        private readonly CopyTextureSupport _copySupport;
        private readonly int _pageId;
        private bool _needsApply;

        public int PageId => _pageId;

        private int _currentX;
        private int _currentY;
        private int _maxYInRow;

        public DynamicAtlasPage(int size)
        {
            _width = size;
            _height = size;
            _copySupport = SystemInfo.copyTextureSupport;
            _pageId = System.Threading.Interlocked.Increment(ref _pageIdCounter);

            InitializeTexture();
        }

        private void InitializeTexture()
        {
            Texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Bilinear;
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.name = "DynamicAtlasPage_" + _pageId;

            var rawData = Texture.GetRawTextureData<Color32>();
            unsafe
            {
                UnsafeUtility.MemClear(NativeArrayUnsafeUtility.GetUnsafePtr(rawData), rawData.Length * UnsafeUtility.SizeOf<Color32>());
            }
            Texture.Apply();
            _needsApply = false;
        }

        public bool TryInsert(Texture2D source, out Rect uvRect)
        {
            uvRect = default;
            if (IsFull) return false;

            int w = source.width;
            int h = source.height;

            int maxWidth = _width - Padding;
            int maxHeight = _height - Padding;

            if (w > maxWidth || h > maxHeight)
            {
                Debug.LogError($"[DynamicAtlasPage] Texture ({w}x{h}) is too large for page ({_width}x{_height}, max available: {maxWidth}x{maxHeight} with padding). Cannot insert.");
                return false;
            }

            if (_currentX + w + Padding > _width)
            {
                _currentY += _maxYInRow + Padding;
                _currentX = 0;
                _maxYInRow = 0;
            }

            if (_currentY + h + Padding > _height)
            {
                IsFull = true;
                return false;
            }

            int xPos = _currentX;
            int yPos = _currentY;

            if (CopyPixels(source, xPos, yPos, w, h))
            {
                _currentX += w + Padding;
                if (h > _maxYInRow) _maxYInRow = h;

                float invWidth = 1.0f / _width;
                float invHeight = 1.0f / _height;
                uvRect.x = xPos * invWidth;
                uvRect.y = yPos * invHeight;
                uvRect.width = w * invWidth;
                uvRect.height = h * invHeight;

                System.Threading.Interlocked.Increment(ref _activeSpriteCount);
                return true;
            }

            return false;
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

        private bool CopyPixels(Texture2D source, int x, int y, int w, int h)
        {
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0;
            bool gpuCopySuccess = false;

            // Attempt GPU copy (fastest, zero GC allocation)
            if (useCopyTexture && source.format == Texture.format)
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

            // Fallback to CPU copy
            if (!source.isReadable)
            {
                Debug.LogError($"[DynamicAtlasPage] Insert failed. GPU CopyTexture failed (Supported: {useCopyTexture}, Format Match: {source.format == Texture.format}) and Source is NOT Readable.");
                return false;
            }

            if (source.format == TextureFormat.RGBA32)
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
            }
            else
            {
                // Format conversion path (slower, higher GC allocation)
                var pixels = source.GetPixels32();
                Texture.SetPixels32(x, y, w, h, pixels);
                _needsApply = true;
            }

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
        }
    }
}