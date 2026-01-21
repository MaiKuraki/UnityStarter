using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Rendering;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Concurrent;
#endif

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Specialized Dynamic Atlas Service for compressed textures (ASTC/ETC2/BC).
    /// Uses GPU CopyTexture for zero-GC, zero-CPU direct block copy.
    /// 
    /// CRITICAL CONSTRAINTS:
    /// 1. Source textures MUST have the same TextureFormat as the atlas
    /// 2. GPU CopyTexture must be supported (all platforms except WebGL)
    /// 3. Cannot mix compressed and uncompressed sources
    /// </summary>
    public class CompressedDynamicAtlasService : IDynamicAtlas
    {
        private class AtlasItem
        {
            public Sprite Sprite;
            public CompressedAtlasPage Page;
            public int RefCount;
            public string CacheKey;
        }

        private readonly List<CompressedAtlasPage> _pages = new List<CompressedAtlasPage>();
        private readonly Dictionary<string, AtlasItem> _itemCache = new Dictionary<string, AtlasItem>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private static int _spriteIdCounter = 0;

        // Object pool
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly ConcurrentQueue<AtlasItem> _itemPool = new ConcurrentQueue<AtlasItem>();
#else
        private readonly Stack<AtlasItem> _itemPool = new Stack<AtlasItem>(64);
        private readonly object _poolLock = new object();
#endif
        private const int MaxPoolSize = 128;
        private int _poolCount = 0;

        // Configuration
        private readonly int _pageSize;
        private readonly int _blockPadding;
        private readonly TextureFormat _format;
        private readonly int _blockSize;
        private readonly bool _gpuCopySupported;

        /// <summary>
        /// Creates a compressed dynamic atlas service.
        /// </summary>
        /// <param name="format">Compressed texture format (must match source textures)</param>
        /// <param name="pageSize">Page size in pixels (will be aligned to block size)</param>
        /// <param name="blockPadding">Padding between sprites in blocks (default: 1)</param>
        public CompressedDynamicAtlasService(TextureFormat format, int pageSize = 2048, int blockPadding = 1)
        {
            _format = format;
            _blockSize = TextureFormatHelper.GetBlockSize(format);
            _blockPadding = blockPadding;

            if (_blockSize <= 1)
            {
                throw new ArgumentException($"CompressedDynamicAtlasService requires a compressed format. {format} is not compressed. " +
                    "Use DynamicAtlasService for uncompressed formats.");
            }

            // Validate format support
            if (!TextureFormatHelper.IsFormatSupported(format))
            {
                throw new ArgumentException($"TextureFormat {format} is not supported on this platform.");
            }

            // Check GPU CopyTexture support
            var copySupport = SystemInfo.copyTextureSupport;
            _gpuCopySupported = (copySupport & CopyTextureSupport.Basic) != 0;

            if (!_gpuCopySupported)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError("[CompressedDynamicAtlasService] GPU CopyTexture not supported. " +
                    "Compressed atlas requires GPU copy capability. Use DynamicAtlasService instead.");
#endif
            }

            // Align page size
            _pageSize = TextureFormatHelper.AlignToBlockSize(pageSize, _blockSize);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CLogger.LogInfo($"[CompressedDynamicAtlasService] Initialized with format {format}, " +
                $"page size {_pageSize}x{_pageSize}, block size {_blockSize}");
#endif
        }

        /// <summary>
        /// NOT SUPPORTED for compressed atlas. Use GetSpriteFromSprite instead.
        /// </summary>
        public Sprite GetSprite(string path)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CLogger.LogWarning("[CompressedDynamicAtlasService] GetSprite(path) is not supported for compressed atlas. " +
                "Use GetSpriteFromSprite(Sprite, cacheKey) instead.");
#endif
            return null;
        }

        /// <summary>
        /// Gets or creates a sprite from an existing compressed Sprite.
        /// Uses GPU CopyTexture for zero-GC, zero-CPU direct block copy.
        /// 
        /// CRITICAL: Source sprite's texture MUST have the same TextureFormat as this atlas!
        /// </summary>
        public Sprite GetSpriteFromSprite(Sprite sourceSprite, string cacheKey = null)
        {
            if (sourceSprite == null) return null;

            string key = cacheKey ?? sourceSprite.name;
            if (string.IsNullOrEmpty(key)) key = sourceSprite.GetHashCode().ToString();

            // Validate format
            if (sourceSprite.texture.format != _format)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[CompressedDynamicAtlasService] Format mismatch! " +
                    $"Source: {sourceSprite.texture.format}, Atlas: {_format}. " +
                    "Compressed copy requires exact format match.");
#endif
                return null;
            }

            // Check cache (read lock)
            _lock.EnterReadLock();
            try
            {
                if (_itemCache.TryGetValue(key, out var cached))
                {
                    if (cached.Sprite != null && cached.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref cached.RefCount);
                        return cached.Sprite;
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // Insert (write lock)
            _lock.EnterWriteLock();
            try
            {
                // Double-check
                if (_itemCache.TryGetValue(key, out var existing))
                {
                    if (existing.Sprite != null && existing.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref existing.RefCount);
                        return existing.Sprite;
                    }
                }

                // Try insert into existing pages
                if (!TryInsertIntoAnyPage(sourceSprite, key, out var item))
                {
                    CreateNewPage();
                    if (!TryInsertIntoAnyPage(sourceSprite, key, out item))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        CLogger.LogError($"[CompressedDynamicAtlasService] Failed to insert sprite {key}. " +
                            $"Sprite size: {sourceSprite.rect.width}x{sourceSprite.rect.height}");
#endif
                        return null;
                    }
                }

                item.RefCount = 1;
                _itemCache[key] = item;
                return item.Sprite;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets or creates a sprite from a compressed texture region.
        /// </summary>
        public Sprite GetSpriteFromRegion(Texture2D sourceTexture, Rect sourceRect, string cacheKey)
        {
            if (sourceTexture == null || string.IsNullOrEmpty(cacheKey)) return null;

            // Validate format
            if (sourceTexture.format != _format)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[CompressedDynamicAtlasService] Format mismatch! " +
                    $"Source: {sourceTexture.format}, Atlas: {_format}.");
#endif
                return null;
            }

            // Check cache
            _lock.EnterReadLock();
            try
            {
                if (_itemCache.TryGetValue(cacheKey, out var cached))
                {
                    if (cached.Sprite != null && cached.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref cached.RefCount);
                        return cached.Sprite;
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // Insert
            _lock.EnterWriteLock();
            try
            {
                if (_itemCache.TryGetValue(cacheKey, out var existing))
                {
                    if (existing.Sprite != null && existing.Sprite.texture != null)
                    {
                        Interlocked.Increment(ref existing.RefCount);
                        return existing.Sprite;
                    }
                }

                if (!TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out var item))
                {
                    CreateNewPage();
                    if (!TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out item))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        CLogger.LogError($"[CompressedDynamicAtlasService] Failed to insert region {cacheKey}.");
#endif
                        return null;
                    }
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

        public void ReleaseSprite(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey)) return;

            _lock.EnterReadLock();
            AtlasItem item = null;
            try
            {
                _itemCache.TryGetValue(cacheKey, out item);
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
                    if (_itemCache.TryGetValue(cacheKey, out var verify) && verify == item && item.RefCount <= 0)
                    {
                        _itemCache.Remove(cacheKey);

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
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private bool TryInsertIntoAnyPage(Sprite sourceSprite, string cacheKey, out AtlasItem item)
        {
            item = null;

            for (int i = _pages.Count - 1; i >= 0; i--)
            {
                var page = _pages[i];
                if (page.TryInsertSprite(sourceSprite, out Rect uvRect))
                {
                    item = CreateItem(page, sourceSprite.rect, uvRect, cacheKey);
                    return true;
                }
            }

            if (_pages.Count == 0)
            {
                CreateNewPage();
                return TryInsertIntoAnyPage(sourceSprite, cacheKey, out item);
            }

            return false;
        }

        private bool TryInsertRegionIntoAnyPage(Texture2D sourceTexture, Rect sourceRect, string cacheKey, out AtlasItem item)
        {
            item = null;

            for (int i = _pages.Count - 1; i >= 0; i--)
            {
                var page = _pages[i];
                if (page.TryInsertFromRegion(sourceTexture, sourceRect, out Rect uvRect))
                {
                    item = CreateItem(page, sourceRect, uvRect, cacheKey);
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

        private void CreateNewPage()
        {
            var page = new CompressedAtlasPage(_pageSize, _format, _blockPadding);
            _pages.Add(page);
        }

        private AtlasItem CreateItem(CompressedAtlasPage page, Rect sourceRect, Rect uvRect, string cacheKey)
        {
            int width = Mathf.RoundToInt(sourceRect.width);
            int height = Mathf.RoundToInt(sourceRect.height);

            Rect spriteRect = new Rect(
                uvRect.x * page.Width,
                uvRect.y * page.Height,
                width,
                height
            );
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int spriteId = Interlocked.Increment(ref _spriteIdCounter);
            newSprite.name = $"CompressedAtlas_{cacheKey}_{page.PageId}_{spriteId}";
#endif

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.CacheKey = cacheKey;
            item.RefCount = 0;

            return item;
        }

        private AtlasItem GetItemFromPool()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_itemPool.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _poolCount);
                return item;
            }
#else
            lock (_poolLock)
            {
                if (_itemPool.Count > 0)
                {
                    Interlocked.Decrement(ref _poolCount);
                    return _itemPool.Pop();
                }
            }
#endif
            return new AtlasItem();
        }

        private void ReleaseItemToPool(AtlasItem item)
        {
            item.Sprite = null;
            item.Page = null;
            item.CacheKey = null;
            item.RefCount = 0;

            if (_poolCount >= MaxPoolSize) return;

#if !UNITY_WEBGL || UNITY_EDITOR
            _itemPool.Enqueue(item);
            Interlocked.Increment(ref _poolCount);
#else
            lock (_poolLock)
            {
                if (_poolCount < MaxPoolSize)
                {
                    _itemPool.Push(item);
                    Interlocked.Increment(ref _poolCount);
                }
            }
#endif
        }

        private void TryReleasePage(CompressedAtlasPage page)
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
                foreach (var page in _pages)
                {
                    page.Dispose();
                }
                _pages.Clear();
                _itemCache.Clear();

#if !UNITY_WEBGL || UNITY_EDITOR
                while (_itemPool.TryDequeue(out _)) { }
#else
                lock (_poolLock) { _itemPool.Clear(); }
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

        /// <summary>
        /// Gets the required texture format for source textures.
        /// </summary>
        public TextureFormat RequiredFormat => _format;

        /// <summary>
        /// Gets the current page count.
        /// </summary>
        public int PageCount => _pages.Count;

        /// <summary>
        /// Checks if GPU CopyTexture is supported.
        /// </summary>
        public bool IsGpuCopySupported => _gpuCopySupported;
    }
}
