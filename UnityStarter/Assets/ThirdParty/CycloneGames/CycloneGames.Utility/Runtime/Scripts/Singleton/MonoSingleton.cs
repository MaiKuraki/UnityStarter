using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    internal static class MonoSingletonRuntimeReset
    {
        private static readonly HashSet<Action> ResetActions = new HashSet<Action>();

        internal static void Register(Action resetAction)
        {
            if (resetAction != null)
            {
                ResetActions.Add(resetAction);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (var resetAction in ResetActions)
            {
                resetAction();
            }
        }
    }

    /// <summary>
    /// Thread-safe MonoBehaviour singleton base class with zero per-access GC.
    /// <para>Design decisions:</para>
    /// <list type="bullet">
    /// <item>No lock: Unity API (Find/AddComponent) is main-thread only. Lock gives false thread-safety and adds overhead.</item>
    /// <item>Uses FindAnyObjectByType (Unity 2023+) / FindObjectOfType (older) for scene search.</item>
    /// <item>Resets statics via [RuntimeInitializeOnLoadMethod] for safe domain-reload in Editor.</item>
    /// <item>Cached singleton name avoids per-access string allocation.</item>
    /// </list>
    /// </summary>
    /// <example>
    /// // --- Any MonoBehaviour can become a singleton by simply inheriting ---
    /// public class AudioManager : MonoSingleton<AudioManager>
    /// {
    ///     public void PlayBGM() { ... }  // your existing logic stays the same
    /// }
    /// // Usage: AudioManager.Instance.PlayBGM();
    ///
    /// // --- Scene-specific singleton (destroyed on scene change) ---
    /// public class LevelManager : MonoSingleton<LevelManager>
    /// {
    ///     protected override bool IsGlobal => false;
    /// }
    /// </example>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        static MonoSingleton()
        {
            MonoSingletonRuntimeReset.Register(ResetStaticsForType);
        }

        private static T instance;
        private static bool applicationIsQuitting;

        // Cache the name once per generic specialization to avoid repeated string allocations.
        private static string CachedName;

        /// <summary>
        /// Ensures static state is clean on every fresh play-mode entry,
        /// even when Domain Reload is disabled in Editor.
        /// </summary>
        private static void ResetStaticsForType()
        {
            instance = null;
            applicationIsQuitting = false;
            CachedName = null;
        }

        /// <summary>
        /// Gets the singleton instance.
        /// If no instance exists, it finds one in the scene or creates a new GameObject.
        /// Must be called from the main thread (Unity API requirement).
        /// </summary>
        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
#if UNITY_EDITOR
                    Debug.LogWarning(string.Concat("[MonoSingleton] Instance '", typeof(T).Name, "' already destroyed on application quit. Returning null."));
#endif
                    return null;
                }

                if (instance != null) return instance;

                // Search scene for existing instance
#if UNITY_6000_0_OR_NEWER
                instance = FindAnyObjectByType<T>();
#elif UNITY_2023_1_OR_NEWER
                instance = FindAnyObjectByType<T>();
#else
                instance = FindObjectOfType<T>();
#endif

                if (instance == null)
                {
                    // Create new instance
                    var singletonObject = new GameObject();
                    instance = singletonObject.AddComponent<T>();

                    if (CachedName == null)
                    {
                        CachedName = string.Concat(typeof(T).Name, " (Singleton)");
                    }
                    singletonObject.name = CachedName;
                }

                // Make persistent if requested
                if (instance.IsGlobal && Application.isPlaying)
                {
                    DontDestroyOnLoad(instance.gameObject);
                }

                return instance;
            }
        }

        /// <summary>
        /// If true, the singleton will not be destroyed when loading a new scene.
        /// Default is true. Override to return false for scene-specific singletons.
        /// </summary>
        protected virtual bool IsGlobal => true;

        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        protected virtual void Awake()
        {
            if (instance == null)
            {
                instance = (T)this;
                if (IsGlobal && Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (instance != this)
            {
#if UNITY_EDITOR
                Debug.LogWarning(string.Concat("[MonoSingleton] Duplicate instance of ", typeof(T).Name, " detected. Destroying new instance."));
#endif
                Destroy(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }

    /*
    // NOTE: Unlike Singleton<T>, MonoSingleton<T> requires inheritance.
    // MonoBehaviour cannot be new()'d — it needs Unity lifecycle hooks
    // (Awake, OnDestroy, OnApplicationQuit) to manage the singleton properly.
    // Just change your base class from MonoBehaviour to MonoSingleton<T>:

    // Before:  public class AudioManager : MonoBehaviour { ... }
    // After:   public class AudioManager : MonoSingleton<AudioManager> { ... }
    //          AudioManager.Instance.PlayBGM();

    // Global singleton (default, persists across scenes via DontDestroyOnLoad)
    public class AudioManager : MonoSingleton<AudioManager>
    {
        public void PlayBGM() { ... }
    }
    AudioManager.Instance.PlayBGM();

    // Scene-specific singleton (destroyed on scene change)
    public class LevelManager : MonoSingleton<LevelManager>
    {
        protected override bool IsGlobal => false;
    }
    LevelManager.Instance.LoadLevel(1);
    */
}
