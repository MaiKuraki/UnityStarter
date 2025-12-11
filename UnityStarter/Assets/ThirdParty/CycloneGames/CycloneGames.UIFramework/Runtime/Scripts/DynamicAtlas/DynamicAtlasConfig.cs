using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Configuration for Dynamic Atlas Service.
    /// </summary>
    [Serializable]
    public class DynamicAtlasConfig
    {
        [Tooltip("Page size in pixels (0 = auto-detect based on device capabilities)")]
        public int pageSize = 0;

        [Tooltip("Automatically scale textures that exceed page size")]
        public bool autoScaleLargeTextures = true;

        [Tooltip("Custom texture loader (null = Resources.Load)")]
        public Func<string, Texture2D> loadFunc;

        [Tooltip("Custom texture unloader (null = Resources.UnloadAsset)")]
        public Action<string, Texture2D> unloadFunc;

        public DynamicAtlasConfig()
        {
        }

        public DynamicAtlasConfig(int pageSize, bool autoScaleLargeTextures = true)
        {
            this.pageSize = pageSize;
            this.autoScaleLargeTextures = autoScaleLargeTextures;
        }

        public DynamicAtlasConfig(
            Func<string, Texture2D> loadFunc,
            Action<string, Texture2D> unloadFunc,
            int pageSize = 0,
            bool autoScaleLargeTextures = true)
        {
            this.loadFunc = loadFunc;
            this.unloadFunc = unloadFunc;
            this.pageSize = pageSize;
            this.autoScaleLargeTextures = autoScaleLargeTextures;
        }
    }
}