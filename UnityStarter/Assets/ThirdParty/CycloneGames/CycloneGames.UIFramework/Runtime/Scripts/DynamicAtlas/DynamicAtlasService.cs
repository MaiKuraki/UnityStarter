using System;
using System.Collections.Generic;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Production-grade Dynamic Atlas System.
    /// Supports: Multi-Page, Reference Counting, Automatic Page Cleanup.
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

        private readonly Func<string, Texture2D> _loadFunc;
        private readonly Action<string, Texture2D> _unloadFunc;
        private readonly int _pageSize;

        public DynamicAtlasService(int forceSize = 0, Func<string, Texture2D> loadFunc = null, Action<string, Texture2D> unloadFunc = null)
        {
            _loadFunc = loadFunc ?? Resources.Load<Texture2D>;
            _unloadFunc = unloadFunc ?? ((path, tex) => Resources.UnloadAsset(tex));

            // Determine page size
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

            if (_itemCache.TryGetValue(path, out var item))
            {
                if (item.Sprite != null && item.Sprite.texture != null)
                {
                    item.RefCount++;
                    // CLogger.LogInfo($"[DynamicAtlas] Ref++ {path} : {item.RefCount}");
                    return item.Sprite;
                }
                // Invalid item, remove
                _itemCache.Remove(path);
                ReleaseItemToPool(item);
            }

            Texture2D source = _loadFunc(path);
            if (source == null)
            {
                CLogger.LogError($"[DynamicAtlas] Failed to load: {path}");
                return null;
            }

            if (!TryInsertIntoAnyPage(source, path, out item))
            {
                CreateNewPage();
                if (!TryInsertIntoAnyPage(source, path, out item))
                {
                    CLogger.LogError($"[DynamicAtlas] Critical Failure: Cannot insert {path} even after creating new page.");
                    _unloadFunc(path, source);
                    return null;
                }
            }

            _unloadFunc(path, source);
            
            item.RefCount = 1;
            _itemCache[path] = item;
            
            return item.Sprite;
        }

        public void ReleaseSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (_itemCache.TryGetValue(path, out var item))
            {
                item.RefCount--;
                // CLogger.LogInfo($"[DynamicAtlas] Ref-- {path} : {item.RefCount}");

                if (item.RefCount <= 0)
                {
                    _itemCache.Remove(path);
                    
                    if (item.Page != null)
                    {
                        item.Page.DecrementActiveCount();
                        TryReleasePage(item.Page);
                    }

                    // Destroy sprite metadata (not texture data)
                    if (item.Sprite != null)
                    {
                        UnityEngine.Object.Destroy(item.Sprite);
                    }
                    
                    ReleaseItemToPool(item);
                }
            }
        }

        private bool TryInsertIntoAnyPage(Texture2D source, string path, out AtlasItem item)
        {
            item = null;
            
            // Try existing pages (from last to first usually better for filling up)
            for (int i = _pages.Count - 1; i >= 0; i--)
            {
                var page = _pages[i];
                if (page.TryInsert(source, out Rect uvRect))
                {
                    item = CreateItem(page, source, uvRect, path);
                    return true;
                }
            }
            
            // Try create first page if none exist
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
            // CLogger.LogInfo($"[DynamicAtlas] Created Page {_pages.Count} ({_pageSize}x{_pageSize})");
        }

        private AtlasItem CreateItem(DynamicAtlasPage page, Texture2D source, Rect uvRect, string path)
        {
            Rect spriteRect = new Rect(uvRect.x * page.Width, uvRect.y * page.Height, source.width, source.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            
            Sprite newSprite = Sprite.Create(page.Texture, spriteRect, pivot, 100.0f, 0, SpriteMeshType.FullRect);
            newSprite.name = $"{path}_Atlas";

            AtlasItem item = GetItemFromPool();
            item.Sprite = newSprite;
            item.Page = page;
            item.Path = path;
            item.RefCount = 0; // Will be set to 1 by caller
            
            return item;
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
            _itemPool.Push(item);
        }

        private void TryReleasePage(DynamicAtlasPage page)
        {
            if (page.IsEmpty)
            {
                // CLogger.LogInfo($"[DynamicAtlas] Page Empty. Destroying page.");
                page.Dispose();
                _pages.Remove(page);
            }
        }

        public void Reset()
        {
            foreach (var page in _pages)
            {
                page.Dispose();
            }
            _pages.Clear();
            _itemCache.Clear();
            _itemPool.Clear();
        }

        public void Dispose()
        {
            Reset();
        }
    }
}