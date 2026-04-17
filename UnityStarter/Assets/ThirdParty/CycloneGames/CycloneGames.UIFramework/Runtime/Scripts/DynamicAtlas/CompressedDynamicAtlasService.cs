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
            public Vector2 Pivot;
            public Vector4 Border;
        }

        private readonly List<CompressedAtlasPage> _pages = new List<CompressedAtlasPage>();
        private readonly Dictionary<string, AtlasItem> _itemCache = new Dictionary<string, AtlasItem>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static int _spriteIdCounter = 0;
#endif

        // Object pool
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly ConcurrentQueue<AtlasItem> _itemPool = new ConcurrentQueue<AtlasItem>();
#else
        private readonly Stack<AtlasItem> _itemPool = new Stack<AtlasItem>(64);
        private readonly object _poolLock = new object();
#endif
        private const int MaxPoolSize = 128;
        private int _poolCount = 0;

        public event Action<string, Sprite> OnSpriteRepacked;

        // Configuration
        private readonly int _pageSize;
        private readonly int _blockPadding;
        private readonly TextureFormat _format;
        private readonly int _blockSize;
        private readonly bool _gpuCopySupported;
        private readonly int _maxPages;

        /// <summary>
        /// Creates a compressed dynamic atlas service.
        /// </summary>
        /// <param name="format">Compressed texture format (must match source textures)</param>
        /// <param name="pageSize">Page size in pixels (will be aligned to block size)</param>
        /// <param name="blockPadding">Padding between sprites in blocks (default: 1)</param>
        /// <param name="maxPages">Maximum number of atlas pages (0 = unlimited)</param>
        public CompressedDynamicAtlasService(TextureFormat format, int pageSize = 2048, int blockPadding = 1, int maxPages = 0)
        {
            _format = format;
            _blockSize = TextureFormatHelper.GetBlockSize(format);
            _blockPadding = blockPadding;
            _maxPages = maxPages;

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
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(GetSprite))) return null;
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
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(GetSpriteFromSprite))) return null;
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
                    if (!CreateNewPage() || !TryInsertIntoAnyPage(sourceSprite, key, out item))
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
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(GetSpriteFromRegion))) return null;
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
                    if (!CreateNewPage() || !TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out item))
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
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(ReleaseSprite))) return;
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

                        if (item.Page != null && item.Sprite != null)
                        {
                            int sw = Mathf.RoundToInt(item.Sprite.rect.width);
                            int sh = Mathf.RoundToInt(item.Sprite.rect.height);
                            item.Page.DecrementActiveCount(sw, sh);
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
                    item = CreateItem(page, sourceSprite, uvRect, cacheKey);
                    return true;
                }
            }

            if (_pages.Count == 0)
            {
                if (!CreateNewPage()) return false;
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
                if (!CreateNewPage()) return false;
                return TryInsertRegionIntoAnyPage(sourceTexture, sourceRect, cacheKey, out item);
            }

            return false;
        }

        private bool CreateNewPage()
        {
            if (_maxPages > 0 && _pages.Count >= _maxPages)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogError($"[CompressedDynamicAtlasService] Max page limit reached ({_maxPages}). Cannot allocate new page.");
#endif
                return false;
            }
            var page = new CompressedAtlasPage(_pageSize, _format, _blockPadding);
            _pages.Add(page);
            return true;
        }

        private AtlasItem CreateItem(CompressedAtlasPage page, Sprite sourceSprite, Rect uvRect, string cacheKey)
        {
            Rect sourceRect = sourceSprite.rect;
            Vector2 pivot = new Vector2(
                sourceSprite.pivot.x / sourceRect.width,
                sourceSprite.pivot.y / sourceRect.height);
            Vector4 border = sourceSprite.border;
            return CreateItem(page, sourceRect, uvRect, cacheKey, pivot, border);
        }

        private AtlasItem CreateItem(CompressedAtlasPage page, Rect sourceRect, Rect uvRect, string cacheKey,
            Vector2? customPivot = null, Vector4? customBorder = null)
        {
            int width = Mathf.RoundToInt(sourceRect.width);
            int height = Mathf.RoundToInt(sourceRect.height);

            Rect spriteRect = new Rect(
                uvRect.x * page.Width,
                uvRect.y * page.Height,
                width,
                height
            );
            Vector2 pivot = customPivot ?? new Vector2(0.5f, 0.5f);
            Vector4 border = customBorder ?? Vector4.zero;

            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect, border);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int spriteId = Interlocked.Increment(ref _spriteIdCounter);
            newSprite.name = $"CompressedAtlas_{cacheKey}_{page.PageId}_{spriteId}";
#endif

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.CacheKey = cacheKey;
            item.RefCount = 0;
            item.Pivot = pivot;
            item.Border = border;

            page.IncrementActiveCount(width, height);

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

        public int Defragment(float fragmentationThreshold = 0.5f)
        {
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(Defragment))) return 0;
            _lock.EnterWriteLock();
            try
            {
                int reclaimedPagesCount = 0;
                List<CompressedAtlasPage> targets = new List<CompressedAtlasPage>();
                foreach (var page in _pages)
                {
                    if (!page.IsEmpty && page.FragmentationRatio >= fragmentationThreshold)
                    {
                        targets.Add(page);
                    }
                }

                if (targets.Count == 0) return 0;

                foreach (var oldPage in targets)
                {
                    List<KeyValuePair<string, AtlasItem>> itemsToMove = new List<KeyValuePair<string, AtlasItem>>();
                    foreach (var kvp in _itemCache)
                    {
                        if (kvp.Value.Page == oldPage && kvp.Value.RefCount > 0 && kvp.Value.Sprite != null)
                        {
                            itemsToMove.Add(kvp);
                        }
                    }

                    var newPage = new CompressedAtlasPage(_pageSize, _format, _blockPadding);
                    _pages.Add(newPage);
                    var stagedUpdates = new List<PendingDefragUpdate>(itemsToMove.Count);

                    bool allMovedSuccessfully = true;

                    foreach (var kvp in itemsToMove)
                    {
                        AtlasItem oldItem = kvp.Value;
                        Sprite oldSprite = oldItem.Sprite;

                        Rect oldRect = oldSprite.textureRect;

                        if (newPage.TryInsertFromRegion(oldPage.Texture, oldRect, out Rect newUvRect))
                        {
                            int w = Mathf.RoundToInt(oldRect.width);
                            int h = Mathf.RoundToInt(oldRect.height);
                            Rect newSpriteRect = new Rect(newUvRect.x * newPage.Width, newUvRect.y * newPage.Height, w, h);

                            Sprite newSprite = Sprite.Create(newPage.Texture, newSpriteRect, oldItem.Pivot, 100.0f, 0, SpriteMeshType.FullRect, oldItem.Border);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            newSprite.name = oldSprite.name + "_Defrag";
#endif
                            stagedUpdates.Add(new PendingDefragUpdate(oldItem, oldSprite, newSprite, w, h));
                        }
                        else
                        {
                            allMovedSuccessfully = false;
                            break;
                        }
                    }

                    if (allMovedSuccessfully)
                    {
                        for (int i = 0; i < stagedUpdates.Count; i++)
                        {
                            var update = stagedUpdates[i];
                            update.Item.Sprite = update.NewSprite;
                            update.Item.Page = newPage;
                            newPage.IncrementActiveCount(update.Width, update.Height);
                            OnSpriteRepacked?.Invoke(update.Item.CacheKey, update.NewSprite);
                            UnityEngine.Object.Destroy(update.OldSprite);
                        }
                        oldPage.Dispose();
                        _pages.Remove(oldPage);
                        reclaimedPagesCount++;
                    }
                    else
                    {
                        for (int i = 0; i < stagedUpdates.Count; i++)
                        {
                            if (stagedUpdates[i].NewSprite != null)
                            {
                                UnityEngine.Object.Destroy(stagedUpdates[i].NewSprite);
                            }
                        }
                        newPage.Dispose();
                        _pages.Remove(newPage);
                    }
                }

                return reclaimedPagesCount;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Reset()
        {
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(Reset))) return;
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
            if (!DynamicAtlasThreadGuard.EnsureMainThread(nameof(Dispose))) return;
            Reset();
            _lock?.Dispose();
        }

        private readonly struct PendingDefragUpdate
        {
            public readonly AtlasItem Item;
            public readonly Sprite OldSprite;
            public readonly Sprite NewSprite;
            public readonly int Width;
            public readonly int Height;

            public PendingDefragUpdate(AtlasItem item, Sprite oldSprite, Sprite newSprite, int width, int height)
            {
                Item = item;
                OldSprite = oldSprite;
                NewSprite = newSprite;
                Width = width;
                Height = height;
            }
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
