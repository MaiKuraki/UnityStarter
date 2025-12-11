using System;
using UnityEngine;

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

        private Func<string, Texture2D> _loadDelegate;
        private Action<string, Texture2D> _unloadDelegate;
        private int _forcedSize = 0;
        private bool _autoScaleLargeTextures = true;

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
        /// Configures the atlas service.
        /// </summary>
        public void Configure(Func<string, Texture2D> load, Action<string, Texture2D> unload, int size = 0, bool autoScaleLargeTextures = true)
        {
            lock (_serviceLock)
            {
                if (_atlasService != null && _atlasService != _injectedService)
                {
                    Debug.LogWarning("[DynamicAtlasManager] Re-configuring atlas service. Resetting existing service.");
                    _atlasService.Dispose();
                    _atlasService = null;
                }
                _loadDelegate = load;
                _unloadDelegate = unload;
                _forcedSize = size;
                _autoScaleLargeTextures = autoScaleLargeTextures;
            }
        }

        /// <summary>
        /// Configures using DynamicAtlasConfig.
        /// </summary>
        public void Configure(DynamicAtlasConfig config)
        {
            if (config == null) return;

            Configure(
                load: config.loadFunc,
                unload: config.unloadFunc,
                size: config.pageSize,
                autoScaleLargeTextures: config.autoScaleLargeTextures
            );
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
                                var config = new DynamicAtlasConfig(
                                    _loadDelegate,
                                    _unloadDelegate,
                                    _forcedSize,
                                    _autoScaleLargeTextures
                                );
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
                            _atlasService = new DynamicAtlasService(_forcedSize, _loadDelegate, _unloadDelegate, _autoScaleLargeTextures);
                        }
                    }
                }
                return _atlasService;
            }
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
        }
    }
}