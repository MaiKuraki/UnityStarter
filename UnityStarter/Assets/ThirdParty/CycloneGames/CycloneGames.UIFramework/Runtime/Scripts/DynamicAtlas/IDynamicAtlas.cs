using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public interface IDynamicAtlas : IDisposable
    {
        /// <summary>
        /// Get or load a sprite by path. Increments reference count.
        /// </summary>
        Sprite GetSprite(string path);

        /// <summary>
        /// Get or create a sprite from an existing Sprite (e.g., from a SpriteAtlas).
        /// Copies the sprite's pixels into the dynamic atlas. Increments reference count.
        /// </summary>
        /// <param name="sourceSprite">The source sprite to copy from (can be from SpriteAtlas)</param>
        /// <param name="cacheKey">Optional cache key. If null, uses sourceSprite.name</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        Sprite GetSpriteFromSprite(Sprite sourceSprite, string cacheKey = null);

        /// <summary>
        /// Get or create a sprite from a Texture2D region.
        /// Useful for extracting specific regions from larger textures.
        /// </summary>
        /// <param name="sourceTexture">The source texture</param>
        /// <param name="sourceRect">The region to extract (in pixels)</param>
        /// <param name="cacheKey">Cache key for this region</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        Sprite GetSpriteFromRegion(Texture2D sourceTexture, Rect sourceRect, string cacheKey);

        /// <summary>
        /// Fires when a heavily fragmented page is repacked and a new Sprite replaces the old one.
        /// UI Frameworks or Image components should subscribe to this to hot-swap their sprite reference seamlessly.
        /// </summary>
        event Action<string, Sprite> OnSpriteRepacked;

        /// <summary>
        /// Performs a double-buffering defragmentation of heavily fragmented atlas pages.
        /// </summary>
        /// <param name="fragmentationThreshold">The minimum ratio of wasted space (0.0 to 1.0) to trigger a repack on a page. default is 0.5 (50% wasted).</param>
        /// <returns>The number of pages successfully destroyed and reclaimed.</returns>
        int Defragment(float fragmentationThreshold = 0.5f);

        /// <summary>
        /// Release a sprite by cache key. Decrements reference count.
        /// If count reaches 0, the sprite might be freed immediately.
        /// </summary>
        void ReleaseSprite(string cacheKey);

        /// <summary>
        /// Clear all pages and cache.
        /// </summary>
        void Reset();
    }
}