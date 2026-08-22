#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// Resolves a provider-specific runtime location for an asset selected in an <c>AssetRef</c> field.
    /// The GUID and asset path are Editor authoring inputs; the returned location is the exact string the
    /// configured provider (an Addressables address, a YooAsset address or asset path, ...) will consume at
    /// runtime. Return null or empty when this resolver does not own the asset so the registry can try the
    /// next one.
    /// </summary>
    /// <remarks>
    /// Implementations must expose a public parameterless constructor so the registry can instantiate them
    /// without a hard reference to any specific provider assembly.
    /// </remarks>
    public interface IAssetRefLocationResolver
    {
        /// <summary>
        /// Ordering tiebreaker when several providers are installed and more than one claims the same asset.
        /// Higher priority runs first. The default is 0; a project with multiple active providers should raise
        /// the priority of the one that owns a given asset class. When priorities tie, type full-name order
        /// keeps the result deterministic.
        /// </summary>
        int Priority { get; }

        string ResolveLocation(string assetGuid, string assetPath);
    }

    /// <summary>
    /// Discovers <see cref="IAssetRefLocationResolver"/> implementations across loaded assemblies without the
    /// core editor assembly taking a hard dependency on any provider. Resolvers are tried in a stable
    /// (type-name) order and the first non-empty result wins.
    /// </summary>
    public static class AssetRefLocationResolverRegistry
    {
        private static readonly List<IAssetRefLocationResolver> s_resolvers =
            new List<IAssetRefLocationResolver>();
        private static bool s_initialized;

        /// <summary>
        /// Returns the provider runtime location for an asset, or null when no registered resolver owns it.
        /// </summary>
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

        /// <summary>
        /// Clears the cached resolver list. Resolver instances are types, not assets, so callers rarely need
        /// this; it is exposed for symmetry with other editor caches and for tests.
        /// </summary>
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
            TypeCache.TypeCollection candidates = TypeCache.GetTypesDerivedFrom<IAssetRefLocationResolver>();
            foreach (Type type in candidates)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                types.Add(type);
            }

            var instances = new List<IAssetRefLocationResolver>(types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                try
                {
                    if (Activator.CreateInstance(types[i]) is IAssetRefLocationResolver resolver)
                    {
                        instances.Add(resolver);
                    }
                }
                catch
                {
                    // A resolver without a usable parameterless constructor is skipped. The remaining
                    // resolvers stay available so a single misconfigured provider cannot break authoring.
                }
            }

            instances.Sort((left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0
                    ? priority
                    : string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
            });

            for (int i = 0; i < instances.Count; i++)
            {
                s_resolvers.Add(instances[i]);
            }
        }
    }
}
#endif
