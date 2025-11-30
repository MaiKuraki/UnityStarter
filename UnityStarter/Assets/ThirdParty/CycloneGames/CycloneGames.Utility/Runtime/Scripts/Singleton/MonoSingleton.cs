using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T instance;
        private static readonly object lockObj = new object();
        private static bool applicationIsQuitting = false;

        /// <summary>
        /// Gets the singleton instance.
        /// If no instance exists, it finds one in the scene or creates a new GameObject.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    Debug.LogWarning($"[MonoSingleton] Instance '{typeof(T)}' already destroyed on application quit. Won't create again - returning null.");
                    return null;
                }

                lock (lockObj)
                {
                    if (instance == null)
                    {
                        // Try to find existing instance
                        instance = FindObjectOfType<T>();

                        if (instance == null)
                        {
                            // Create new instance
                            var singletonObject = new GameObject();
                            instance = singletonObject.AddComponent<T>();
                            singletonObject.name = typeof(T).Name + " (Singleton)";

                            // Make persistent if requested
                            if (instance.IsGlobal && Application.isPlaying)
                            {
                                DontDestroyOnLoad(singletonObject);
                            }
                        }
                        else
                        {
                            // If found in scene, ensure persistence if needed
                            if (instance.IsGlobal && Application.isPlaying)
                            {
                                DontDestroyOnLoad(instance.gameObject);
                            }
                        }
                    }
                    return instance;
                }
            }
        }

        /// <summary>
        /// If true, the singleton will not be destroyed when loading a new scene.
        /// Default is true. Override to return false for scene-specific singletons.
        /// </summary>
        protected virtual bool IsGlobal => true;

        /// <summary>
        /// Prevents recreation of the singleton when the application is quitting.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        /// <summary>
        /// Optional: Ensure duplicate instances are destroyed.
        /// </summary>
        protected virtual void Awake()
        {
            if (instance == null)
            {
                instance = (T)this;
                if (IsGlobal)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (instance != this)
            {
                Debug.LogWarning($"[MonoSingleton] Duplicate instance of {typeof(T)} detected. Destroying new instance.");
                Destroy(gameObject);
            }
        }
    }
}
