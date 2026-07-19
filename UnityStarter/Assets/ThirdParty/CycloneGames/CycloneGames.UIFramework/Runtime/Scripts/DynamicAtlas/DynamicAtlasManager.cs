using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    [DisallowMultipleComponent]
    public sealed class DynamicAtlasManager : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Creates an owned service during Awake. Disable this when a composition root injects the service.")]
        private bool autoInitialize = true;

        [SerializeField]
        private DynamicAtlasConfig configuration = new DynamicAtlasConfig();

        private IDynamicAtlas _service;
        private bool _ownsService;

        /// <summary>The configured service, or null before explicit/automatic initialization.</summary>
        public IDynamicAtlas Service => _service;

        public bool IsInitialized => _service != null;
        public DynamicAtlasConfig Configuration => configuration.Copy();

        private void Awake()
        {
            if (autoInitialize && _service == null)
            {
                Initialize();
            }
        }

        public void Configure(DynamicAtlasConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (_service != null)
            {
                throw new InvalidOperationException("Configure the manager before initialization, or replace the service explicitly at a composition boundary.");
            }

            if (!config.Validate(out string errorMessage))
            {
                throw new ArgumentException(
                    $"Dynamic atlas configuration is invalid: {errorMessage}",
                    nameof(config));
            }

            configuration = config.Copy();
        }

        /// <summary>
        /// Applies the current platform profile with an optional page-size override.
        /// Oversized sources are scaled only when that profile explicitly permits the
        /// synchronous readback path required by scaling; otherwise they are rejected.
        /// </summary>
        public void Configure(
            Func<string, Texture2D> load,
            Action<string, Texture2D> unload,
            int size = 0,
            bool autoScaleLargeTextures = true)
        {
            DynamicAtlasConfig config = DynamicAtlasConfig.CreateForCurrentPlatform(
                loadFunc: load,
                unloadFunc: unload);
            if (size > 0)
            {
                config.pageSize = size;
            }

            config.oversizePolicy = autoScaleLargeTextures &&
                                    config.copyFallback == DynamicAtlasCopyFallback.AllowSynchronousReadback
                ? DynamicAtlasOversizePolicy.ScaleDown
                : DynamicAtlasOversizePolicy.Reject;
            Configure(config);
        }

        public void ConfigurePlatformOptimized(
            Func<string, Texture2D> load = null,
            Action<string, Texture2D> unload = null)
        {
            Configure(DynamicAtlasConfig.CreateForCurrentPlatform(
                loadFunc: load,
                unloadFunc: unload));
        }

        public void Initialize(DynamicAtlasConfig config = null)
        {
            if (_service != null)
            {
                return;
            }

            if (config != null)
            {
                configuration = config.Copy();
            }

            _service = new DynamicAtlasService(configuration);
            _ownsService = true;
        }

        public void SetService(IDynamicAtlas service, bool takeOwnership = false)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (ReferenceEquals(_service, service))
            {
                _ownsService = takeOwnership;
                return;
            }

            if (_ownsService)
            {
                _service?.Dispose();
            }

            _service = service;
            _ownsService = takeOwnership;
        }

        public DynamicAtlasInsertStatus TryAcquire(
            string key,
            Texture2D source,
            out DynamicAtlasSpriteLease lease)
        {
            return GetRequiredService().TryAcquire(key, source, out lease);
        }

        public DynamicAtlasInsertStatus TryAcquireSprite(
            string key,
            Sprite source,
            out DynamicAtlasSpriteLease lease)
        {
            return GetRequiredService().TryAcquireSprite(key, source, out lease);
        }

        public DynamicAtlasStats GetStats()
        {
            return GetRequiredService().GetStats();
        }

        public int TrimUnused(int maximumEntriesToRemove = int.MaxValue)
        {
            return GetRequiredService().TrimUnused(maximumEntriesToRemove);
        }

        public void Clear()
        {
            GetRequiredService().Clear();
        }

        private IDynamicAtlas GetRequiredService()
        {
            if (_service == null)
            {
                throw new InvalidOperationException(
                    "DynamicAtlasManager is not initialized. Call Initialize or inject a service before use.");
            }

            return _service;
        }

        private void OnDestroy()
        {
            if (_ownsService)
            {
                _service?.Dispose();
            }

            _service = null;
            _ownsService = false;
        }
    }
}
