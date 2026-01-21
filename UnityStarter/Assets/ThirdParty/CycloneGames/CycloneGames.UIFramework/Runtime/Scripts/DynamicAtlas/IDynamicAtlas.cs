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