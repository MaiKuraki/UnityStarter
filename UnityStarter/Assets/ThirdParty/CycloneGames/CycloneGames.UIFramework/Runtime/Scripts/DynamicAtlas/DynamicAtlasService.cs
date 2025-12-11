using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Production-grade Dynamic Atlas System with multi-page support, reference counting, and automatic page cleanup.
    /// Thread-safe for concurrent access.
    /// </summary>
    public class DynamicAtlasService : IDynamicAtlas
    {
        private class AtlasItem
        {
            public Sprite Sprite;
            public DynamicAtlasPage Page;
            public int RefCount;
            public string Path;
        }

        private readonly List<DynamicAtlasPage> _pages = new List<DynamicAtlasPage>();
        private readonly Dictionary<string, AtlasItem> _itemCache = new Dictionary<string, AtlasItem>();
        private readonly Stack<AtlasItem> _itemPool = new Stack<AtlasItem>(64);
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private const int MaxPoolSize = 128;
        private static int _spriteIdCounter = 0;

        private readonly Func<string, Texture2D> _loadFunc;
        private readonly Action<string, Texture2D> _unloadFunc;
        private readonly int _pageSize;
        private readonly bool _autoScaleLargeTextures;

        public DynamicAtlasService(int forceSize = 0, Func<string, Texture2D> loadFunc = null, Action<string, Texture2D> unloadFunc = null, bool autoScaleLargeTextures = true)
        {
            _loadFunc = loadFunc ?? Resources.Load<Texture2D>;
            _unloadFunc = unloadFunc ?? ((path, tex) => Resources.UnloadAsset(tex));
            _autoScaleLargeTextures = autoScaleLargeTextures;

            if (forceSize > 0)
            {
                _pageSize = forceSize;
            }
            else
            {
                int maxTextureSize = SystemInfo.maxTextureSize;
                long systemMemory = SystemInfo.systemMemorySize;
                if (systemMemory < 3000 || maxTextureSize < 2048) _pageSize = 1024;
                else _pageSize = 2048;
            }
        }

        public Sprite GetSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            _lock.EnterReadLock();
            AtlasItem cachedItem = null;
            bool foundValid = false;
            try
            {
                if (_itemCache.TryGetValue(path, out cachedItem))
                {
                    if (cachedItem.Sprite != null)
                    {
                        try
                        {
                            if (cachedItem.Sprite.texture != null)
                            {
                                foundValid = true;
                            }
                        }
                        catch (UnityException)
                        {
                            foundValid = true;
                        }
                    }

                    if (!foundValid)
                    {
                        cachedItem = null;
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (foundValid && cachedItem != null)
            {
                Interlocked.Increment(ref cachedItem.RefCount);
                return cachedItem.Sprite;
            }

            if (cachedItem != null)
            {
                _lock.EnterWriteLock();
                try
                {
                    if (_itemCache.TryGetValue(path, out var item))
                    {
                        bool shouldRemove = false;
                        if (item.Sprite == null)
                        {
                            shouldRemove = true;
                        }
                        else
                        {
                            try
                            {
                                if (item.Sprite.texture == null)
                                {
                                    shouldRemove = true;
                                }
                            }
                            catch (UnityException)
                            {
                            }
                        }

                        if (shouldRemove)
                        {
                            _itemCache.Remove(path);
                            ReleaseItemToPool(item);
                        }
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }

            Texture2D source = _loadFunc(path);
            if (source == null)
            {
                CLogger.LogError($"[DynamicAtlas] Failed to load: {path}");
                return null;
            }

            const int padding = 2;
            int availableSize = _pageSize - padding;
            Texture2D processedTexture = source;
            bool needsDispose = false;

            if (_autoScaleLargeTextures && (source.width > availableSize || source.height > availableSize))
            {
                processedTexture = ScaleTextureToFit(source, _pageSize);
                if (processedTexture != null && processedTexture != source)
                {
                    needsDispose = true;
                    CLogger.LogWarning($"[DynamicAtlas] Texture {path} ({source.width}x{source.height}) is too large. " +
                        $"Auto-scaled to {processedTexture.width}x{processedTexture.height} to fit page size ({_pageSize}x{_pageSize}, available: {availableSize}x{availableSize} with padding).");
                }
            }

            _lock.EnterWriteLock();
            try
            {
                if (_itemCache.TryGetValue(path, out var existingItem))
                {
                    if (existingItem.Sprite != null && existingItem.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref existingItem.RefCount);
                        _unloadFunc(path, source);
                        if (needsDispose && processedTexture != source)
                        {
                            UnityEngine.Object.DestroyImmediate(processedTexture);
                        }
                        return existingItem.Sprite;
                    }
                }

                if (!TryInsertIntoAnyPage(processedTexture, path, out var item))
                {
                    CreateNewPage();
                    if (!TryInsertIntoAnyPage(processedTexture, path, out item))
                    {
                        string errorDetails = $"Texture size: {processedTexture.width}x{processedTexture.height}, " +
                            $"Page size: {_pageSize}x{_pageSize}, " +
                            $"Format: {processedTexture.format}, " +
                            $"Readable: {processedTexture.isReadable}, " +
                            $"Max texture size: {SystemInfo.maxTextureSize}";

                        if (processedTexture.width > _pageSize || processedTexture.height > _pageSize)
                        {
                            string autoScaleStatus = _autoScaleLargeTextures ? "enabled" : "disabled";
                            CLogger.LogError($"[DynamicAtlas] Critical Failure: Cannot insert {path}. Texture ({processedTexture.width}x{processedTexture.height}) is larger than page size ({_pageSize}x{_pageSize}). " +
                                $"Auto-scaling is {autoScaleStatus}. {errorDetails}");
                        }
                        else if (!processedTexture.isReadable)
                        {
                            CLogger.LogError($"[DynamicAtlas] Critical Failure: Cannot insert {path}. Texture is not readable. {errorDetails}");
                        }
                        else
                        {
                            CLogger.LogError($"[DynamicAtlas] Critical Failure: Cannot insert {path} even after creating new page. {errorDetails}");
                        }

                        _unloadFunc(path, source);
                        if (needsDispose && processedTexture != source)
                        {
                            UnityEngine.Object.DestroyImmediate(processedTexture);
                        }
                        return null;
                    }
                }

                if (item.Page != null)
                {
                    item.Page.ApplyIfNeeded();
                }

                _unloadFunc(path, source);

                if (needsDispose && processedTexture != source)
                {
                    UnityEngine.Object.DestroyImmediate(processedTexture);
                }

                item.RefCount = 1;
                _itemCache[path] = item;

                return item.Sprite;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Scales texture to fit within the specified maximum size while maintaining aspect ratio.
        /// Uses optimized CPU-based scaling for readable textures, falls back to RenderTexture for compressed formats.
        /// </summary>
        private Texture2D ScaleTextureToFit(Texture2D source, int maxSize)
        {
            if (source == null) return null;

            int sourceWidth = source.width;
            int sourceHeight = source.height;

            const int padding = 2;
            int availableSize = maxSize - padding;

            float scale = Mathf.Min((float)availableSize / sourceWidth, (float)availableSize / sourceHeight);

            if (scale >= 1.0f)
            {
                return source;
            }

            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));

            // Ensure even dimensions for better compression and alignment
            if (targetWidth % 2 != 0 || targetHeight % 2 != 0)
            {
                float aspectRatio = (float)sourceWidth / sourceHeight;
                if (targetWidth % 2 != 0)
                {
                    targetWidth--;
                    if (targetWidth < 1) targetWidth = 2;
                    targetHeight = Mathf.RoundToInt(targetWidth / aspectRatio);
                    if (targetHeight % 2 != 0) targetHeight--;
                }
                else if (targetHeight % 2 != 0)
                {
                    targetHeight--;
                    if (targetHeight < 1) targetHeight = 2;
                    targetWidth = Mathf.RoundToInt(targetHeight * aspectRatio);
                    if (targetWidth % 2 != 0) targetWidth--;
                }
            }

            if (targetWidth < 1) targetWidth = 1;
            if (targetHeight < 1) targetHeight = 1;

            if (targetWidth > availableSize || targetHeight > availableSize)
            {
                float sizeScale = Mathf.Min((float)availableSize / targetWidth, (float)availableSize / targetHeight);
                targetWidth = Mathf.RoundToInt(targetWidth * sizeScale);
                targetHeight = Mathf.RoundToInt(targetHeight * sizeScale);

                if (targetWidth % 2 != 0) targetWidth--;
                if (targetHeight % 2 != 0) targetHeight--;
                if (targetWidth < 1) targetWidth = 1;
                if (targetHeight < 1) targetHeight = 1;
            }

            Texture2D scaled;

            bool canUseFastPath = source.isReadable &&
                source.format != TextureFormat.DXT1 &&
                source.format != TextureFormat.DXT5 &&
                source.format != TextureFormat.BC4 &&
                source.format != TextureFormat.BC5 &&
                source.format != TextureFormat.BC6H &&
                source.format != TextureFormat.BC7;

            if (canUseFastPath)
            {
                Color32[] sourcePixels = source.GetPixels32();
                Color32[] scaledPixels = new Color32[targetWidth * targetHeight];

                float xRatio = (float)sourceWidth / targetWidth;
                float yRatio = (float)sourceHeight / targetHeight;

                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int srcX = Mathf.FloorToInt(x * xRatio);
                        int srcY = Mathf.FloorToInt(y * yRatio);
                        srcX = Mathf.Clamp(srcX, 0, sourceWidth - 1);
                        srcY = Mathf.Clamp(srcY, 0, sourceHeight - 1);

                        scaledPixels[y * targetWidth + x] = sourcePixels[srcY * sourceWidth + srcX];
                    }
                }

                scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
                scaled.SetPixels32(scaledPixels);
                scaled.Apply(false);
            }
            else
            {
                RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;

                scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
                scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                scaled.Apply(false);

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            return scaled;
        }

        public void ReleaseSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            _lock.EnterReadLock();
            AtlasItem item = null;
            try
            {
                _itemCache.TryGetValue(path, out item);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (item == null) return;

            int newRefCount = Interlocked.Decrement(ref item.RefCount);

            if (newRefCount <= 0)
            {
                _lock.EnterWriteLock();
                try
                {
                    if (_itemCache.TryGetValue(path, out var verifyItem) && verifyItem == item)
                    {
                        if (item.RefCount <= 0)
                        {
                            _itemCache.Remove(path);

                            if (item.Page != null)
                            {
                                item.Page.DecrementActiveCount();
                                TryReleasePage(item.Page);
                            }

                            if (item.Sprite != null)
                            {
                                UnityEngine.Object.Destroy(item.Sprite);
                            }

                            ReleaseItemToPool(item);
                        }
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private bool TryInsertIntoAnyPage(Texture2D source, string path, out AtlasItem item)
        {
            item = null;

            int pageCount = _pages.Count;
            for (int i = pageCount - 1; i >= 0; i--)
            {
                var page = _pages[i];
                if (page.TryInsert(source, out Rect uvRect))
                {
                    item = CreateItem(page, source, uvRect, path);
                    return true;
                }
            }

            if (_pages.Count == 0)
            {
                CreateNewPage();
                return TryInsertIntoAnyPage(source, path, out item);
            }

            return false;
        }

        private void CreateNewPage()
        {
            var page = new DynamicAtlasPage(_pageSize);
            _pages.Add(page);
        }

        private AtlasItem CreateItem(DynamicAtlasPage page, Texture2D source, Rect uvRect, string path)
        {
            Rect spriteRect = new Rect(uvRect.x * page.Width, uvRect.y * page.Height, source.width, source.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect);

            string fileName = ExtractFileName(path);
            int pathHash = path.GetHashCode();
            int spriteId = System.Threading.Interlocked.Increment(ref _spriteIdCounter);
            int pageId = page.PageId;

            newSprite.name = $"Atlas_{fileName}_{pathHash:X8}_{pageId}_{spriteId}";

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.Path = path;
            item.RefCount = 0; // Will be set to 1 by caller

            return item;
        }

        /// <summary>
        /// Extracts and sanitizes the file name from a path for use in sprite names.
        /// Limits length and removes invalid characters to ensure Unity compatibility.
        /// </summary>
        private static string ExtractFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";

            int lastSlash = Mathf.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

            int lastDot = fileName.LastIndexOf('.');
            if (lastDot > 0)
            {
                fileName = fileName.Substring(0, lastDot);
            }

            const int maxLength = 32;
            char[] buffer = new char[maxLength];
            int bufferIndex = 0;

            foreach (char c in fileName)
            {
                if (bufferIndex >= maxLength) break;

                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')
                {
                    buffer[bufferIndex++] = c;
                }
                else if (c == ' ' || c == '.')
                {
                    buffer[bufferIndex++] = '_';
                }
            }

            if (bufferIndex == 0)
            {
                return "Unknown";
            }

            return new string(buffer, 0, bufferIndex);
        }

        private AtlasItem GetItemFromPool()
        {
            if (_itemPool.Count > 0)
            {
                return _itemPool.Pop();
            }
            return new AtlasItem();
        }

        private void ReleaseItemToPool(AtlasItem item)
        {
            item.Sprite = null;
            item.Page = null;
            item.Path = null;
            item.RefCount = 0;

            if (_itemPool.Count < MaxPoolSize)
            {
                _itemPool.Push(item);
            }
        }

        private void TryReleasePage(DynamicAtlasPage page)
        {
            if (page.IsEmpty)
            {
                page.Dispose();
                _pages.Remove(page);
            }
        }

        public void Reset()
        {
            _lock.EnterWriteLock();
            try
            {
                int pageCount = _pages.Count;
                for (int i = 0; i < pageCount; i++)
                {
                    _pages[i].Dispose();
                }
                _pages.Clear();
                _itemCache.Clear();
                _itemPool.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            Reset();
            _lock?.Dispose();
        }
    }
}