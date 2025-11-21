using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Singleton Entry Point for Dynamic Atlas System.
    /// Delegates work to DynamicAtlasService.
    /// </summary>
    public class DynamicAtlasManager : MonoBehaviour
    {
        private static DynamicAtlasManager _instance;
        
        public static DynamicAtlasManager Instance
        {
            get
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
                return _instance;
            }
        }

        private IDynamicAtlas _atlasService;
        
        private Func<string, Texture2D> _loadDelegate;
        private Action<string, Texture2D> _unloadDelegate;
        private int _forcedSize = 0;

        public void Configure(Func<string, Texture2D> load, Action<string, Texture2D> unload, int size = 0)
        {
            if (_atlasService != null)
            {
                Debug.LogWarning("[DynamicAtlasManager] Re-configuring atlas service. Resetting existing service.");
                _atlasService.Dispose();
                _atlasService = null;
            }
            _loadDelegate = load;
            _unloadDelegate = unload;
            _forcedSize = size;
        }
        
        public IDynamicAtlas Service => _atlasService ?? (_atlasService = new DynamicAtlasService(_forcedSize, _loadDelegate, _unloadDelegate));

        public Sprite GetSprite(string path)
        {
            return Service.GetSprite(path);
        }

        /// <summary>
        /// Releases a sprite reference. 
        /// MUST be called when the sprite is no longer needed (e.g. OnDisable/OnDestroy of the UI element).
        /// </summary>
        public void ReleaseSprite(string path)
        {
            Service.ReleaseSprite(path);
        }

        private void OnDestroy()
        {
            _atlasService?.Dispose();
            _atlasService = null;
        }
    }
}