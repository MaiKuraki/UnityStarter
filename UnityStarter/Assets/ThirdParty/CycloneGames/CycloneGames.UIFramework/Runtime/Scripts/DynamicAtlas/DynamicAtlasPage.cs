using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Represents a single Texture2D atlas page.
    /// Handles insertion of textures onto its surface.
    /// </summary>
    public class DynamicAtlasPage : IDisposable
    {
        private const int Padding = 2;

        public Texture2D Texture { get; private set; }
        public int Width => _width;
        public int Height => _height;
        public bool IsFull { get; private set; }

        // Track how many active sprites are on this page
        public int ActiveSpriteCount { get; private set; }

        private readonly int _width;
        private readonly int _height;
        private readonly CopyTextureSupport _copySupport;

        // Shelf Packing Cursor
        private int _currentX;
        private int _currentY;
        private int _maxYInRow;

        public DynamicAtlasPage(int size)
        {
            _width = size;
            _height = size;
            _copySupport = SystemInfo.copyTextureSupport;

            InitializeTexture();
        }

        private void InitializeTexture()
        {
            Texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Bilinear;
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.name = $"DynamicAtlasPage_{Guid.NewGuid().ToString().Substring(0, 4)}";

            // Clear texture
            var rawData = Texture.GetRawTextureData<Color32>();
            unsafe
            {
                UnsafeUtility.MemClear(NativeArrayUnsafeUtility.GetUnsafePtr(rawData), rawData.Length * UnsafeUtility.SizeOf<Color32>());
            }
            Texture.Apply();
        }

        public bool TryInsert(Texture2D source, out Rect uvRect)
        {
            uvRect = default;
            if (IsFull) return false;

            int w = source.width;
            int h = source.height;

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

                uvRect.x = (float)xPos / _width;
                uvRect.y = (float)yPos / _height;
                uvRect.width = (float)w / _width;
                uvRect.height = (float)h / _height;

                ActiveSpriteCount++;
                return true;
            }

            return false;
        }

        private bool CopyPixels(Texture2D source, int x, int y, int w, int h)
        {
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0;
            bool gpuCopySuccess = false;

            // Attempt GPU Copy (Fastest, 0GC)
            // Requires: System support + Format match
            if (useCopyTexture && source.format == Texture.format)
            {
                try
                {
                    Graphics.CopyTexture(source, 0, 0, 0, 0, w, h, Texture, 0, 0, x, y);
                    gpuCopySuccess = true;
                }
                catch
                {
                    // Fallback to CPU if runtime error occurs (e.g. protection, driver bug)
                    gpuCopySuccess = false;
                }
            }

            if (gpuCopySuccess) return true;

            // Fallback to CPU (Slower, higher memory usage if source is readable)
            // Requires: Source to be Readable
            if (!source.isReadable)
            {
                Debug.LogError($"[DynamicAtlasPage] Insert failed. GPU CopyTexture failed (Supported: {useCopyTexture}, Format Match: {source.format == Texture.format}) and Source is NOT Readable.");
                return false;
            }

            // CPU Copy Implementation
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

                    for (int row = 0; row < h; row++)
                    {
                        Color32* srcRowPtr = srcPtr + (row * srcWidth);
                        Color32* dstRowPtr = dstPtr + ((y + row) * dstWidth + x);

                        UnsafeUtility.MemCpy(dstRowPtr, srcRowPtr, w * UnsafeUtility.SizeOf<Color32>());
                    }
                }
            }
            else
            {
                // Slowest path: GetPixels32 (Format conversion)
                // Handles TextureFormat mismatch (e.g. DXT5 -> RGBA32)
                var pixels = source.GetPixels32();
                Texture.SetPixels32(x, y, w, h, pixels);
            }

            Texture.Apply(); // Upload CPU changes to GPU
            return true;
        }

        public void DecrementActiveCount()
        {
            ActiveSpriteCount--;
            if (ActiveSpriteCount < 0) ActiveSpriteCount = 0;
        }
        
        /// <summary>
        /// Checks if this page is completely empty (no active sprites).
        /// </summary>
        public bool IsEmpty => ActiveSpriteCount == 0;

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