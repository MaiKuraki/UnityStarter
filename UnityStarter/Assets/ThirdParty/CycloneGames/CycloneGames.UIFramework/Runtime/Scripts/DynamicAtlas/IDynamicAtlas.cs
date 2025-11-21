using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public interface IDynamicAtlas : IDisposable
    {
        /// <summary>
        /// Get or load a sprite. Increments reference count.
        /// </summary>
        Sprite GetSprite(string path);

        /// <summary>
        /// Release a sprite. Decrements reference count.
        /// If count reaches 0, the sprite might be freed immediately or later depending on strategy.
        /// </summary>
        void ReleaseSprite(string path);

        /// <summary>
        /// Clear all pages and cache.
        /// </summary>
        void Reset();
    }
}