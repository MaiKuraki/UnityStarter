using System;
using System.Collections.Generic;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Editor-side registry of the text component technologies that UIFramework tooling recognizes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TextMeshPro is registered as a built-in backend. On Unity 2023.2 and Unity 6 it ships inside
    /// <c>com.unity.ugui</c> and cannot be removed, and a project may install a third-party text
    /// package alongside it, so the registry is deliberately a list rather than a single choice.
    /// </para>
    /// <para>
    /// A companion package registers its own backend from a static constructor marked with
    /// <c>[InitializeOnLoad]</c>. Static state is rebuilt after every domain reload, and
    /// <see cref="Register"/> replaces an existing backend with the same id, so registration is
    /// idempotent and safe to repeat.
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
        private static UITextBackend[] _snapshot = Array.Empty<UITextBackend>();

        /// <summary>
        /// Built-in TextMeshPro backend. Always registered; matching simply never succeeds on a
        /// project that does not have the type.
        /// </summary>
        public static UITextBackend TextMeshPro { get; } = new UITextBackend(
            id: "TextMeshPro",
            displayName: "TextMeshPro",
            baseTypeName: "TMPro.TMP_Text",
            textFieldName: "m_text",
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
                for (int i = 0; i < Ordered.Count; i++)
                {
                    if (string.Equals(Ordered[i].Id, backend.Id, StringComparison.Ordinal))
                    {
                        Ordered[i] = backend;
                        Invalidate();
                        return;
                    }
                }

                Ordered.Add(backend);
                Invalidate();
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

        private static void Invalidate()
        {
            MatchCache.Clear();
            _snapshot = Ordered.ToArray();
        }
    }
}
