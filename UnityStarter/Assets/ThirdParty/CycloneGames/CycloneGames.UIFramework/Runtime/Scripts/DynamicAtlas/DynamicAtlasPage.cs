using System;
using UnityEngine;
using UnityEngine.Rendering;

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

            // Clear to transparent
            Color32[] clear = new Color32[_width * _height];
            Texture.SetPixels32(clear);
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

            CopyPixels(source, xPos, yPos, w, h);

            _currentX += w + Padding;
            if (h > _maxYInRow) _maxYInRow = h;

            uvRect.x = (float)xPos / _width;
            uvRect.y = (float)yPos / _height;
            uvRect.width = (float)w / _width;
            uvRect.height = (float)h / _height;

            ActiveSpriteCount++;
            return true;
        }

        private void CopyPixels(Texture2D source, int x, int y, int w, int h)
        {
            bool useCopyTexture = (_copySupport & CopyTextureSupport.Basic) != 0;

            if (useCopyTexture && source.format == Texture.format)
            {
                try
                {
                    Graphics.CopyTexture(source, 0, 0, 0, 0, w, h, Texture, 0, 0, x, y);
                    return;
                }
                catch
                {
                    // Fallback
                }
            }

            // CPU Fallback
            if (source.isReadable)
            {
                var pixels = source.GetPixels32();
                Texture.SetPixels32(x, y, w, h, pixels);
                Texture.Apply();
            }
            else
            {
                Debug.LogError($"[DynamicAtlasPage] Cannot copy texture '{source.name}' (Not Readable & CopyTexture failed).");
            }
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