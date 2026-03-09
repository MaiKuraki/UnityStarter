using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Production-grade Dynamic Atlas System with multi-page support, reference counting, and automatic page cleanup.
    /// Thread-safe for concurrent access with platform-specific optimizations.
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
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private static int _spriteIdCounter = 0;

#if UNITY_EDITOR
        public class EditorAtlasItem
        {
            public Sprite Sprite { get; }
            public DynamicAtlasPage Page { get; }
            public int RefCount { get; }
            public string Path { get; }

            public EditorAtlasItem(Sprite sprite, DynamicAtlasPage page, int refCount, string path)
            {
                Sprite = sprite;
                Page = page;
                RefCount = refCount;
                Path = path;
            }
        }

        public IReadOnlyList<DynamicAtlasPage> EditorGetPages()
        {
            _lock.EnterReadLock();
            try
            {
                return new List<DynamicAtlasPage>(_pages);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IReadOnlyList<EditorAtlasItem> EditorGetCachedItems()
        {
            _lock.EnterReadLock();
            try
            {
                var list = new List<EditorAtlasItem>(_itemCache.Count);
                foreach (var kvp in _itemCache)
                {
                    list.Add(new EditorAtlasItem(kvp.Value.Sprite, kvp.Value.Page, kvp.Value.RefCount, kvp.Value.Path));
                }
                return list;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
#endif

        // Platform-specific object pool
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly ConcurrentQueue<AtlasItem> _itemPoolConcurrent = new ConcurrentQueue<AtlasItem>();
#else
        private readonly Stack<AtlasItem> _itemPoolStack = new Stack<AtlasItem>(64);
#endif
        private const int MaxPoolSize = 128;
        private int _poolCount = 0;

        // Configuration
        private readonly Func<string, Texture2D> _loadFunc;
        private readonly Action<string, Texture2D> _unloadFunc;
        private readonly int _pageSize;
        private readonly int _padding;
        private readonly TextureFormat _targetFormat;
        private readonly bool _autoScaleLargeTextures;
        private readonly bool _enableBlockAlignment;
        private readonly bool _enablePlatformOptimizations;
        private readonly int _blockSize;

        public DynamicAtlasService(
            int forceSize = 0,
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null,
            bool autoScaleLargeTextures = true)
            : this(new DynamicAtlasConfig
            {
                pageSize = forceSize,
                loadFunc = loadFunc,
                unloadFunc = unloadFunc,
                autoScaleLargeTextures = autoScaleLargeTextures
            })
        {
        }

        public DynamicAtlasService(DynamicAtlasConfig config)
        {
            config = config ?? new DynamicAtlasConfig();

            _loadFunc = config.loadFunc ?? Resources.Load<Texture2D>;
            _unloadFunc = config.unloadFunc ?? ((path, tex) => Resources.UnloadAsset(tex));
            _autoScaleLargeTextures = config.autoScaleLargeTextures;
            _targetFormat = config.targetFormat;
            _enableBlockAlignment = config.enableBlockAlignment;
            _enablePlatformOptimizations = config.enablePlatformOptimizations;
            _padding = config.padding;
            _blockSize = TextureFormatHelper.GetBlockSize(_targetFormat);

            if (config.pageSize > 0)
            {
                _pageSize = config.pageSize;
            }
            else
            {
                int maxTextureSize = SystemInfo.maxTextureSize;
                long systemMemory = SystemInfo.systemMemorySize;
                if (systemMemory < 3000 || maxTextureSize < 2048) _pageSize = 1024;
                else _pageSize = 2048;
            }

            // Align page size to block size if needed
            if (_blockSize > 1)
            {
                _pageSize = TextureFormatHelper.AlignToBlockSize(_pageSize, _blockSize);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[DynamicAtlas] Failed to load: {path}");
#endif
                return null;
            }

            int availableSize = _pageSize - _padding;
            Texture2D processedTexture = source;
            bool needsDispose = false;

            if (_autoScaleLargeTextures && (source.width > availableSize || source.height > availableSize))
            {
                processedTexture = ScaleTextureToFit(source, _pageSize);
                if (processedTexture != null && processedTexture != source)
                {
                    needsDispose = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    CLogger.LogWarning($"[DynamicAtlas] Texture {path} ({source.width}x{source.height}) is too large. " +
                        $"Auto-scaled to {processedTexture.width}x{processedTexture.height} to fit page size ({_pageSize}x{_pageSize}, available: {availableSize}x{availableSize} with padding).");
#endif
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif
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
        /// Uses zero-GC GPU path (Graphics.Blit). CPU native/managed paths have been removed.
        /// </summary>
        private Texture2D ScaleTextureToFit(Texture2D source, int maxSize)
        {
            if (source == null) return null;

            int sourceWidth = source.width;
            int sourceHeight = source.height;

            int availableSize = maxSize - _padding;

            float scale = Mathf.Min((float)availableSize / sourceWidth, (float)availableSize / sourceHeight);

            if (scale >= 1.0f)
            {
                return source;
            }

            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));

            // Align to block size if needed
            if (_blockSize > 1)
            {
                targetWidth = TextureFormatHelper.AlignToBlockSize(targetWidth, _blockSize);
                targetHeight = TextureFormatHelper.AlignToBlockSize(targetHeight, _blockSize);
            }
            else
            {
                // Ensure even dimensions for better compression and alignment
                if (targetWidth % 2 != 0) targetWidth--;
                if (targetHeight % 2 != 0) targetHeight--;
            }

            if (targetWidth < 1) targetWidth = 1;
            if (targetHeight < 1) targetHeight = 1;

            if (targetWidth > availableSize || targetHeight > availableSize)
            {
                float sizeScale = Mathf.Min((float)availableSize / targetWidth, (float)availableSize / targetHeight);
                targetWidth = Mathf.RoundToInt(targetWidth * sizeScale);
                targetHeight = Mathf.RoundToInt(targetHeight * sizeScale);

                if (_blockSize > 1)
                {
                    targetWidth = TextureFormatHelper.AlignToBlockSize(Mathf.Max(1, targetWidth), _blockSize);
                    targetHeight = TextureFormatHelper.AlignToBlockSize(Mathf.Max(1, targetHeight), _blockSize);
                }

                if (targetWidth < 1) targetWidth = 1;
                if (targetHeight < 1) targetHeight = 1;
            }

            // Strictly use GPU scaling path
            return ScaleTextureGPU(source, targetWidth, targetHeight);
        }

        private Texture2D ScaleTextureGPU(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            scaled.Apply(false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

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

        /// <summary>
        /// Gets or creates a sprite from an existing Sprite (e.g., from a SpriteAtlas).
        /// Copies the sprite's pixels into the dynamic atlas using zero-copy GPU path when available.
        /// </summary>
        /// <param name="sourceSprite">The source sprite to copy from (can be from SpriteAtlas)</param>
        /// <param name="cacheKey">Optional cache key. If null, uses sourceSprite.name</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        public Sprite GetSpriteFromSprite(Sprite sourceSprite, string cacheKey = null)
        {
            if (sourceSprite == null) return null;

            string key = cacheKey ?? sourceSprite.name;
            if (string.IsNullOrEmpty(key)) key = sourceSprite.GetHashCode().ToString();

            // Check cache first
            _lock.EnterReadLock();
            try
            {
                if (_itemCache.TryGetValue(key, out var cachedItem))
                {
                    if (cachedItem.Sprite != null && cachedItem.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref cachedItem.RefCount);
                        return cachedItem.Sprite;
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // Extract region from source sprite's texture
            Texture2D sourceTexture = sourceSprite.texture;
            Rect sourceRect = sourceSprite.rect;

            return GetSpriteFromRegion(sourceTexture, sourceRect, key);
        }

        /// <summary>
        /// Gets or creates a sprite from a Texture2D region.
        /// Useful for extracting specific regions from larger textures or SpriteAtlas.
        /// Uses GPU CopyTexture when available for zero-GC operation.
        /// </summary>
        /// <param name="sourceTexture">The source texture (e.g., SpriteAtlas texture)</param>
        /// <param name="sourceRect">The region to extract (in pixels, bottom-left origin)</param>
        /// <param name="cacheKey">Cache key for this region</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        public Sprite GetSpriteFromRegion(Texture2D sourceTexture, Rect sourceRect, string cacheKey)
        {
            if (sourceTexture == null || string.IsNullOrEmpty(cacheKey)) return null;

            // Check cache first
            _lock.EnterReadLock();
            try
            {
                if (_itemCache.TryGetValue(cacheKey, out var cachedItem))
                {
                    if (cachedItem.Sprite != null && cachedItem.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref cachedItem.RefCount);
                        return cachedItem.Sprite;
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            int regionWidth = Mathf.RoundToInt(sourceRect.width);
            int regionHeight = Mathf.RoundToInt(sourceRect.height);
            int availableSize = _pageSize - _padding;

            // Check if region needs scaling
            if (regionWidth > availableSize || regionHeight > availableSize)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogWarning($"[DynamicAtlas] Region {cacheKey} ({regionWidth}x{regionHeight}) exceeds available size ({availableSize}). " +
                    "Consider using smaller source sprites or increasing page size.");
#endif
                return null;
            }

            _lock.EnterWriteLock();
            try
            {
                // Double-check cache
                if (_itemCache.TryGetValue(cacheKey, out var existingItem))
                {
                    if (existingItem.Sprite != null && existingItem.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref existingItem.RefCount);
                        return existingItem.Sprite;
                    }
                }

                // Try to insert into existing pages
                if (!TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out var item))
                {
                    CreateNewPage();
                    if (!TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out item))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        CLogger.LogError($"[DynamicAtlas] Failed to insert region {cacheKey}. " +
                            $"Region size: {regionWidth}x{regionHeight}, Page size: {_pageSize}x{_pageSize}");
#endif
                        return null;
                    }
                }

                if (item.Page != null)
                {
                    item.Page.ApplyIfNeeded();
                }

                item.RefCount = 1;
                _itemCache[cacheKey] = item;

                return item.Sprite;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private bool TryInsertRegionIntoAnyPage(Texture2D sourceTexture, Rect sourceRect, string cacheKey, out AtlasItem item)
        {
            item = null;

            int pageCount = _pages.Count;
            for (int i = pageCount - 1; i >= 0; i--)
            {
                var page = _pages[i];
                if (page.TryInsertFromRegion(sourceTexture, sourceRect, out Rect uvRect))
                {
                    item = CreateItemFromRegion(page, sourceRect, uvRect, cacheKey);
                    return true;
                }
            }

            if (_pages.Count == 0)
            {
                CreateNewPage();
                return TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out item);
            }

            return false;
        }

        private AtlasItem CreateItemFromRegion(DynamicAtlasPage page, Rect sourceRect, Rect uvRect, string cacheKey)
        {
            int width = Mathf.RoundToInt(sourceRect.width);
            int height = Mathf.RoundToInt(sourceRect.height);

            Rect spriteRect = new Rect(uvRect.x * page.Width, uvRect.y * page.Height, width, height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int spriteId = Interlocked.Increment(ref _spriteIdCounter);
            newSprite.name = $"Atlas_Region_{cacheKey}_{page.PageId}_{spriteId}";
#endif

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.Path = cacheKey;
            item.RefCount = 0;

            return item;
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
            var page = new DynamicAtlasPage(_pageSize, _targetFormat, _padding, _enablePlatformOptimizations);
            _pages.Add(page);
        }

        private AtlasItem CreateItem(DynamicAtlasPage page, Texture2D source, Rect uvRect, string path)
        {
            Rect spriteRect = new Rect(uvRect.x * page.Width, uvRect.y * page.Height, source.width, source.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string fileName = ExtractFileName(path);
            int pathHash = path.GetHashCode();
            int spriteId = Interlocked.Increment(ref _spriteIdCounter);
            int pageId = page.PageId;
            newSprite.name = $"Atlas_{fileName}_{pathHash:X8}_{pageId}_{spriteId}";
#endif

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.Path = path;
            item.RefCount = 0;

            return item;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Extracts and sanitizes the file name from a path for use in sprite names.
        /// Only compiled in editor/development builds to avoid GC in release.
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
            if (fileName.Length > maxLength)
            {
                fileName = fileName.Substring(0, maxLength);
            }

            return fileName;
        }
#endif

        private AtlasItem GetItemFromPool()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_itemPoolConcurrent.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _poolCount);
                return item;
            }
#else
            lock (_itemPoolStack)
            {
                if (_itemPoolStack.Count > 0)
                {
                    Interlocked.Decrement(ref _poolCount);
                    return _itemPoolStack.Pop();
                }
            }
#endif
            return new AtlasItem();
        }

        private void ReleaseItemToPool(AtlasItem item)
        {
            item.Sprite = null;
            item.Page = null;
            item.Path = null;
            item.RefCount = 0;

            if (_poolCount >= MaxPoolSize) return;

#if !UNITY_WEBGL || UNITY_EDITOR
            _itemPoolConcurrent.Enqueue(item);
            Interlocked.Increment(ref _poolCount);
#else
            lock (_itemPoolStack)
            {
                if (_poolCount < MaxPoolSize)
                {
                    _itemPoolStack.Push(item);
                    Interlocked.Increment(ref _poolCount);
                }
            }
#endif
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

#if !UNITY_WEBGL || UNITY_EDITOR
                while (_itemPoolConcurrent.TryDequeue(out _)) { }
#else
                lock (_itemPoolStack)
                {
                    _itemPoolStack.Clear();
                }
#endif
                _poolCount = 0;
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