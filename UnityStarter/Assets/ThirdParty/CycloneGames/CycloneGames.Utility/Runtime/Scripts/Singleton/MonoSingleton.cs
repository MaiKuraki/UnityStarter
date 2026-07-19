using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Coordinates play-session reset and main-thread validation for generic MonoBehaviour singletons.
    /// Unity does not reliably discover runtime initialization methods on every closed generic type, so the
    /// callback must remain on this non-generic type.
    /// </summary>
    internal static class MonoSingletonRuntime
    {
        private static readonly object ResetActionsLock = new object();
        private static readonly HashSet<Action> ResetActions = new HashSet<Action>();

        private static volatile int _mainThreadId;
        private static volatile bool _runtimeInitialized;
        private static volatile bool _applicationIsQuitting;

        internal static bool IsApplicationQuitting => _applicationIsQuitting;

        internal static void RegisterReset(Action resetAction)
        {
            if (resetAction == null)
            {
                throw new ArgumentNullException(nameof(resetAction));
            }

            lock (ResetActionsLock)
            {
                ResetActions.Add(resetAction);
            }
        }

        internal static void EnsureMainThread()
        {
            if (!_runtimeInitialized)
            {
                throw new InvalidOperationException(
                    "MonoSingleton access is unavailable before Unity runtime subsystem registration.");
            }

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException("MonoSingleton may only be accessed from the Unity main thread.");
            }
        }

        internal static void EnsurePlayMode()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("MonoSingleton may only create or query instances in Play Mode.");
            }
        }

        internal static void MarkApplicationQuitting()
        {
            _applicationIsQuitting = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _applicationIsQuitting = false;
            _runtimeInitialized = true;

            Application.quitting -= MarkApplicationQuitting;
            Application.quitting += MarkApplicationQuitting;

            lock (ResetActionsLock)
            {
                foreach (Action resetAction in ResetActions)
                {
                    resetAction();
                }
            }
        }
    }

    /// <summary>
    /// Main-thread-confined MonoBehaviour singleton with explicit Scene or application lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Instance"/> returns the cached component, locates one loaded Scene component (including an
    /// inactive component), or creates a dedicated GameObject. A cache miss is a cold path and may allocate.
    /// Cached access does not allocate managed memory.
    /// </para>
    /// <para>
    /// This type does not make the component thread-safe. Every public static access must occur on the Unity
    /// main thread in Play Mode. Complex services that require constructor injection, cancellation, ordered
    /// shutdown, or multiple worlds should use an explicit owner instead.
    /// </para>
    /// <para>
    /// Derived Unity lifecycle overrides must call the base implementation. A duplicate component is disabled
    /// and destroyed without destroying its GameObject or sibling components.
    /// </para>
    /// <para>
    /// A global singleton must be attached to a root GameObject. A configured child component is retained with
    /// Scene lifetime and reports an error instead of relying on an invalid DontDestroyOnLoad call.
    /// </para>
    /// </remarks>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T _instance;
        private static string _cachedObjectName;

        static MonoSingleton()
        {
            MonoSingletonRuntime.RegisterReset(ResetStaticsForType);
        }

        /// <summary>
        /// Gets the current component, locates a unique loaded component, or creates a dedicated component.
        /// Returns <see langword="null"/> while the application is quitting.
        /// </summary>
        public static T Instance
        {
            get
            {
                MonoSingletonRuntime.EnsureMainThread();
                if (MonoSingletonRuntime.IsApplicationQuitting)
                {
                    return null;
                }

                MonoSingletonRuntime.EnsurePlayMode();

                if (_instance != null)
                {
                    return _instance;
                }

                T[] candidates = UnityEngine.Object.FindObjectsByType<T>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                if (candidates.Length > 1)
                {
                    throw new InvalidOperationException(
                        string.Concat(
                            "MonoSingleton<",
                            typeof(T).FullName,
                            "> found ",
                            candidates.Length.ToString(),
                            " loaded components. Keep exactly one configured instance."));
                }

                if (candidates.Length == 1)
                {
                    _instance = candidates[0];
                    MakePersistentIfRequired(_instance);
                    return _instance;
                }

                return CreateInstance();
            }
        }

        /// <summary>
        /// Gets whether a live instance has already been registered without searching or creating one.
        /// </summary>
        public static bool HasInstance
        {
            get
            {
                return TryGetInstance(out _);
            }
        }

        /// <summary>
        /// Tries to get the registered live instance without searching or creating one.
        /// </summary>
        public static bool TryGetInstance(out T singleton)
        {
            MonoSingletonRuntime.EnsureMainThread();
            if (MonoSingletonRuntime.IsApplicationQuitting)
            {
                singleton = null;
                return false;
            }

            MonoSingletonRuntime.EnsurePlayMode();
            singleton = _instance;
            if (singleton != null)
            {
                return true;
            }

            // Normalize Unity's destroyed-object pseudo-null so callers never receive a stale wrapper.
            _instance = null;
            singleton = null;
            return false;
        }

        /// <summary>
        /// Gets whether this component moves to the DontDestroyOnLoad Scene when it becomes the instance.
        /// </summary>
        protected virtual bool IsGlobal => true;

        /// <summary>Claims this component or rejects it as a duplicate.</summary>
        protected virtual void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_instance == null)
            {
                _instance = (T)this;
                MakePersistentIfRequired(_instance);
                return;
            }

            if (_instance == this)
            {
                return;
            }

            enabled = false;
            Debug.LogError(
                string.Concat(
                    "[MonoSingleton] Duplicate ",
                    typeof(T).FullName,
                    " component was disabled and will be destroyed. Its GameObject and sibling components are preserved."),
                this);
            Destroy(this);
        }

        /// <summary>Blocks singleton recreation during application shutdown.</summary>
        protected virtual void OnApplicationQuit()
        {
            MonoSingletonRuntime.MarkApplicationQuitting();
        }

        /// <summary>Releases the registration when the owning component is destroyed.</summary>
        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static T CreateInstance()
        {
            if (_cachedObjectName == null)
            {
                _cachedObjectName = string.Concat(typeof(T).Name, " (Singleton)");
            }

            var owner = new GameObject(_cachedObjectName);
            try
            {
                T created = owner.AddComponent<T>();
                if (created == null)
                {
                    throw new InvalidOperationException(
                        string.Concat("Unity could not add singleton component ", typeof(T).FullName, "."));
                }

                // Awake normally claims the instance. This fallback also supports legacy overrides that did
                // not call base.Awake, while the documentation and tests require calling the base method.
                if (_instance == null)
                {
                    _instance = created;
                    MakePersistentIfRequired(_instance);
                }

                if (_instance != created)
                {
                    throw new InvalidOperationException(
                        string.Concat("Another ", typeof(T).FullName, " instance was registered during creation."));
                }

                return _instance;
            }
            catch
            {
                if (_instance != null && _instance.gameObject == owner)
                {
                    _instance = null;
                }

                if (owner != null)
                {
                    Destroy(owner);
                }

                throw;
            }
        }

        private static void MakePersistentIfRequired(T singleton)
        {
            if (singleton == null || !singleton.IsGlobal)
            {
                return;
            }

            if (singleton.transform.parent != null)
            {
                Debug.LogError(
                    string.Concat(
                        "[MonoSingleton] Global ",
                        typeof(T).FullName,
                        " must be attached to a root GameObject. The configured component will keep Scene lifetime."),
                    singleton);
                return;
            }

            DontDestroyOnLoad(singleton.gameObject);
        }

        private static void ResetStaticsForType()
        {
            _instance = null;
        }
    }
}
