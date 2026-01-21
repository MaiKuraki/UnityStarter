using System;
using UnityEngine;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Unity MonoBehaviour wrapper for Dynamic Atlas System.
    /// Supports singleton pattern and dependency injection.
    /// </summary>
    public class DynamicAtlasManager : MonoBehaviour
    {
        private static DynamicAtlasManager _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static DynamicAtlasManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = FindObjectOfType<DynamicAtlasManager>();
                            if (_instance == null)
                            {
                                GameObject go = new GameObject("DynamicAtlasManager");
                                _instance = go.AddComponent<DynamicAtlasManager>();
                                DontDestroyOnLoad(go);
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        private IDynamicAtlas _injectedService;
        private IDynamicAtlasFactory _factory;

        private IDynamicAtlas _atlasService;
        private readonly object _serviceLock = new object();

        // Configuration state
        private DynamicAtlasConfig _config;
        private Func<string, Texture2D> _loadDelegate;
        private Action<string, Texture2D> _unloadDelegate;
        private int _forcedSize = 0;
        private bool _autoScaleLargeTextures = true;
        private TextureFormat _targetFormat = TextureFormat.RGBA32;
        private bool _enableBlockAlignment = true;
        private bool _enablePlatformOptimizations = true;

        public DynamicAtlasManager() { }

        public DynamicAtlasManager(IDynamicAtlas service)
        {
            _injectedService = service;
        }

        public DynamicAtlasManager(IDynamicAtlasFactory factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Sets the injected service.
        /// </summary>
        public void SetService(IDynamicAtlas service)
        {
            lock (_serviceLock)
            {
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    _atlasService.Dispose();
                }
                _injectedService = service;
                _atlasService = service;
            }
        }

        /// <summary>
        /// Sets the factory.
        /// </summary>
        public void SetFactory(IDynamicAtlasFactory factory)
        {
            lock (_serviceLock)
            {
                _factory = factory;
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    _atlasService.Dispose();
                }
                _atlasService = null;
            }
        }

        /// <summary>
        /// Configures the atlas service with basic options.
        /// </summary>
        public void Configure(Func<string, Texture2D> load, Action<string, Texture2D> unload, int size = 0, bool autoScaleLargeTextures = true)
        {
            lock (_serviceLock)
            {
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    CLogger.LogWarning("[DynamicAtlasManager] Re-configuring atlas service. Resetting existing service.");
                    _atlasService.Dispose();
                    _atlasService = null;
                }
                _loadDelegate = load;
                _unloadDelegate = unload;
                _forcedSize = size;
                _autoScaleLargeTextures = autoScaleLargeTextures;
                _config = null; // Clear cached config
            }
        }

        /// <summary>
        /// Configures using DynamicAtlasConfig.
        /// </summary>
        public void Configure(DynamicAtlasConfig config)
        {
            if (config == null) return;

            lock (_serviceLock)
            {
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    CLogger.LogWarning("[DynamicAtlasManager] Re-configuring atlas service. Resetting existing service.");
                    _atlasService.Dispose();
                    _atlasService = null;
                }

                _config = config;
                _loadDelegate = config.loadFunc;
                _unloadDelegate = config.unloadFunc;
                _forcedSize = config.pageSize;
                _autoScaleLargeTextures = config.autoScaleLargeTextures;
                _targetFormat = config.targetFormat;
                _enableBlockAlignment = config.enableBlockAlignment;
                _enablePlatformOptimizations = config.enablePlatformOptimizations;
            }
        }

        /// <summary>
        /// Configures with platform-optimized settings.
        /// </summary>
        /// <param name="load">Custom texture loader</param>
        /// <param name="unload">Custom texture unloader</param>
        /// <param name="useCompression">Whether to use compressed texture format (ASTC/ETC2/BC7)</param>
        public void ConfigurePlatformOptimized(Func<string, Texture2D> load = null, Action<string, Texture2D> unload = null, bool useCompression = false)
        {
            var config = DynamicAtlasConfig.CreatePlatformOptimized(load, unload, useCompression);
            Configure(config);
        }

        /// <summary>
        /// Gets the atlas service instance.
        /// Priority: Injected Service > Factory > Internal Service
        /// </summary>
        public IDynamicAtlas Service
        {
            get
            {
                if (_injectedService != null)
                {
                    return _injectedService;
                }

                if (_factory != null)
                {
                    if (_atlasService == null)
                    {
                        lock (_serviceLock)
                        {
                            if (_atlasService == null)
                            {
                                var config = BuildConfig();
                                _atlasService = _factory.Create(config);
                            }
                        }
                    }
                    return _atlasService;
                }

                if (_atlasService == null)
                {
                    lock (_serviceLock)
                    {
                        if (_atlasService == null)
                        {
                            var config = BuildConfig();
                            _atlasService = new DynamicAtlasService(config);
                        }
                    }
                }
                return _atlasService;
            }
        }

        private DynamicAtlasConfig BuildConfig()
        {
            if (_config != null) return _config;

            return new DynamicAtlasConfig
            {
                loadFunc = _loadDelegate,
                unloadFunc = _unloadDelegate,
                pageSize = _forcedSize,
                autoScaleLargeTextures = _autoScaleLargeTextures,
                targetFormat = _targetFormat,
                enableBlockAlignment = _enableBlockAlignment,
                enablePlatformOptimizations = _enablePlatformOptimizations
            };
        }

        /// <summary>
        /// Gets a sprite.
        /// </summary>
        public Sprite GetSprite(string path)
        {
            return Service.GetSprite(path);
        }

        /// <summary>
        /// Releases a sprite reference. Must be called when the sprite is no longer needed.
        /// </summary>
        public void ReleaseSprite(string path)
        {
            Service.ReleaseSprite(path);
        }

        /// <summary>
        /// Gets or creates a sprite from an existing Sprite (e.g., from a SpriteAtlas).
        /// Copies the sprite's pixels into the dynamic atlas.
        /// </summary>
        /// <param name="sourceSprite">The source sprite to copy from (can be from SpriteAtlas)</param>
        /// <param name="cacheKey">Optional cache key. If null, uses sourceSprite.name</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        public Sprite GetSpriteFromSprite(Sprite sourceSprite, string cacheKey = null)
        {
            return Service.GetSpriteFromSprite(sourceSprite, cacheKey);
        }

        /// <summary>
        /// Gets or creates a sprite from a Texture2D region.
        /// Useful for extracting specific regions from larger textures.
        /// </summary>
        /// <param name="sourceTexture">The source texture</param>
        /// <param name="sourceRect">The region to extract (in pixels)</param>
        /// <param name="cacheKey">Cache key for this region</param>
        /// <returns>A new sprite referencing the dynamic atlas</returns>
        public Sprite GetSpriteFromRegion(Texture2D sourceTexture, Rect sourceRect, string cacheKey)
        {
            return Service.GetSpriteFromRegion(sourceTexture, sourceRect, cacheKey);
        }

        /// <summary>
        /// Gets the current texture format being used.
        /// </summary>
        public TextureFormat CurrentFormat => _targetFormat;

        /// <summary>
        /// Checks if the current configuration uses compressed textures.
        /// </summary>
        public bool IsUsingCompression => TextureFormatHelper.RequiresBlockAlignment(_targetFormat);

        private void OnDestroy()
        {
            lock (_serviceLock)
            {
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    _atlasService.Dispose();
                    _atlasService = null;
                }
            }

            if (_instance == this)
            {
                lock (_lock)
                {
                    if (_instance == this)
                    {
                        _instance = null;
                    }
                }
            }

            // Clear buffer pool when manager is destroyed
            DynamicAtlasBufferPool.ResetInstance();
        }
    }
}