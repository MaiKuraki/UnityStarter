#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Resolves the provider runtime location for a generated window prefab. Implementations live in optional
    /// integration assemblies (for example, one that forwards to CycloneGames.AssetManagement), so the core
    /// editor stays free of any asset-package dependency. Return null or empty when the implementation does not
    /// own the asset.
    /// </summary>
    public interface IUIWindowLocationResolver
    {
        string ResolveLocation(string assetGuid, string assetPath);
    }

    /// <summary>
    /// Discovers <see cref="IUIWindowLocationResolver"/> implementations across loaded assemblies without the
    /// core editor assembly taking a hard dependency on any integration. Resolvers are tried in a stable
    /// (type-name) order and the first non-empty result wins.
    /// </summary>
    public static class UIWindowLocationResolverRegistry
    {
        private static readonly List<IUIWindowLocationResolver> s_resolvers =
            new List<IUIWindowLocationResolver>();
        private static bool s_initialized;

        public static string Resolve(string assetGuid, string assetPath)
        {
            EnsureInitialized();
            for (int i = 0; i < s_resolvers.Count; i++)
            {
                string location = s_resolvers[i].ResolveLocation(assetGuid, assetPath);
                if (!string.IsNullOrWhiteSpace(location))
                {
                    return location.Trim();
                }
            }

            return null;
        }

        public static void Invalidate()
        {
            s_initialized = false;
            s_resolvers.Clear();
        }

        private static void EnsureInitialized()
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
            s_resolvers.Clear();

            var types = new List<Type>();
            TypeCache.TypeCollection candidates = TypeCache.GetTypesDerivedFrom<IUIWindowLocationResolver>();
            foreach (Type type in candidates)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                types.Add(type);
            }

            types.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));

            for (int i = 0; i < types.Count; i++)
            {
                try
                {
                    if (Activator.CreateInstance(types[i]) is IUIWindowLocationResolver resolver)
                    {
                        s_resolvers.Add(resolver);
                    }
                }
                catch
                {
                    // Skip resolvers without a usable parameterless constructor so a single misconfigured
                    // integration cannot break the whole creator.
                }
            }
        }
    }
}
#endif
