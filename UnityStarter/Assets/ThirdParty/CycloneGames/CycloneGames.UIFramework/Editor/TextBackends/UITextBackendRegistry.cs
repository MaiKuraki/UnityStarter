using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Editor-side registry of the text component technologies that UIFramework tooling recognizes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TextMeshPro is registered as a built-in backend and is always present. On Unity 2023.2 and
    /// Unity 6 it ships inside <c>com.unity.ugui</c> and cannot be removed, and a project may install
    /// a third-party text package alongside it, so the registry holds a list of backends.
    /// </para>
    /// <para>
    /// A companion package registers its own backend from a static constructor marked with
    /// <c>[InitializeOnLoad]</c>. Static state is rebuilt after every domain reload, and
    /// <see cref="Register"/> replaces an existing backend with the same id, so registration is
    /// idempotent and safe to repeat.
    /// </para>
    /// <para>
    /// Backends are consulted in descending <see cref="UITextBackend.Priority"/>, and registration
    /// order across assemblies is not deterministic, so priority is the only supported way to make
    /// one backend win over another that matches the same component type.
    /// </para>
    /// <para>
    /// Matching is cached per component type, including negative results, because template
    /// inspection walks every component in a prefab hierarchy.
    /// </para>
    /// </remarks>
    public static class UITextBackendRegistry
    {
        private static readonly object Gate = new object();
        private static readonly List<UITextBackend> Ordered = new List<UITextBackend>(4);
        private static readonly Dictionary<Type, UITextBackend> MatchCache = new Dictionary<Type, UITextBackend>(32);
        private static readonly Dictionary<string, Type> ResolvedTypes = new Dictionary<string, Type>(4);
        private static UITextBackend[] _snapshot = Array.Empty<UITextBackend>();
        private static long _version;

        /// <summary>
        /// Priority assigned to a backend that does not specify one.
        /// </summary>
        public const int DefaultPriority = 0;

        /// <summary>
        /// Built-in TextMeshPro backend. Always registered; matching simply never succeeds on a
        /// project that does not have the type.
        /// </summary>
        /// <remarks>
        /// Registered at the default priority. A companion backend that shares a base type with
        /// TextMeshPro and should win registers with a positive priority; one that should only be
        /// consulted as a fallback uses a negative priority.
        /// </remarks>
        public static UITextBackend TextMeshPro { get; } = new UITextBackend(
            id: "TextMeshPro",
            displayName: "TextMeshPro",
            baseTypeName: "TMPro.TMP_Text",
            textFieldName: "m_text",
            priority: DefaultPriority,
            titleObjectNames: "Text (TMP)");

        static UITextBackendRegistry()
        {
            Ordered.Add(TextMeshPro);
            _snapshot = Ordered.ToArray();
        }

        /// <summary>
        /// Registered backends in match priority order: the first backend that matches a component
        /// type wins.
        /// </summary>
        /// <remarks>
        /// This is an immutable snapshot taken at the time of the call, so iterating it needs no
        /// lock and cannot be invalidated by a concurrent registration. Holders that must notice
        /// later registrations compare <see cref="Version"/> against the value they read.
        /// </remarks>
        public static IReadOnlyList<UITextBackend> Backends
        {
            get
            {
                lock (Gate)
                {
                    return _snapshot;
                }
            }
        }

        /// <summary>
        /// Increments every time the registered set changes. Lets a holder of a
        /// <see cref="Backends"/> snapshot tell whether it is still current.
        /// </summary>
        public static long Version
        {
            get
            {
                lock (Gate)
                {
                    return _version;
                }
            }
        }

        /// <summary>
        /// Registers a backend, replacing any previously registered backend with the same id.
        /// </summary>
        public static void Register(UITextBackend backend)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }

            lock (Gate)
            {
                // Remove first so the priority slot is recomputed for the replacement.
                for (int i = 0; i < Ordered.Count; i++)
                {
                    if (string.Equals(Ordered[i].Id, backend.Id, StringComparison.Ordinal))
                    {
                        Ordered.RemoveAt(i);
                        break;
                    }
                }

                int insertIndex = Ordered.Count;
                for (int i = 0; i < Ordered.Count; i++)
                {
                    if (Ordered[i].Priority < backend.Priority)
                    {
                        insertIndex = i;
                        break;
                    }
                }

                Ordered.Insert(insertIndex, backend);
                Invalidate();
            }
        }

        /// <summary>
        /// Removes the backend with the given id and reports whether one was registered under it.
        /// </summary>
        /// <remarks>
        /// The built-in TextMeshPro backend can be unregistered too; callers that do so must register
        /// it again when they are done.
        /// </remarks>
        public static bool Unregister(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            lock (Gate)
            {
                for (int i = 0; i < Ordered.Count; i++)
                {
                    if (string.Equals(Ordered[i].Id, id, StringComparison.Ordinal))
                    {
                        Ordered.RemoveAt(i);
                        Invalidate();
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Finds the backend that owns <paramref name="componentType"/>.
        /// </summary>
        public static bool TryMatch(Type componentType, out UITextBackend backend)
        {
            if (componentType == null)
            {
                backend = null;
                return false;
            }

            lock (Gate)
            {
                if (MatchCache.TryGetValue(componentType, out backend))
                {
                    return backend != null;
                }

                for (int i = 0; i < Ordered.Count; i++)
                {
                    if (Ordered[i].Matches(componentType))
                    {
                        backend = Ordered[i];
                        MatchCache[componentType] = backend;
                        return true;
                    }
                }

                // Cache the miss too; template inspection asks about the same types repeatedly.
                MatchCache[componentType] = null;
                backend = null;
                return false;
            }
        }

        /// <summary>
        /// Resolves a backend's base type from the assemblies currently loaded, for diagnostics only.
        /// </summary>
        /// <returns>
        /// True when the type was found. False means the package that ships the component is not
        /// present, or <see cref="UITextBackend.BaseTypeName"/> is stale after an upstream rename.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <see cref="TryMatch"/> does not depend on this: it compares reflected type names, so a
        /// backend with a stale name simply never matches and reports no error. Tooling that finds
        /// no text component should call this and name the backends that could not be resolved
        /// instead of reporting "none found".
        /// </para>
        /// <para>
        /// Results are cached per base type name, negative ones included, and the cache is dropped
        /// whenever the registered set changes so a package that loads later is picked up.
        /// </para>
        /// </remarks>
        public static bool TryResolveBackendType(UITextBackend backend, out Type type)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }

            lock (Gate)
            {
                if (ResolvedTypes.TryGetValue(backend.BaseTypeName, out type))
                {
                    return type != null;
                }

                type = FindLoadedType(backend.BaseTypeName);
                ResolvedTypes[backend.BaseTypeName] = type;
                return type != null;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type candidate = assemblies[i].GetType(fullName, throwOnError: false, ignoreCase: false);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void Invalidate()
        {
            MatchCache.Clear();
            ResolvedTypes.Clear();
            _snapshot = Ordered.ToArray();
            _version++;
        }
    }
}
