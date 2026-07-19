using System;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Coordinates AssetBundle providers whose process-global shutdown behavior cannot safely coexist.
    /// All transitions are main-thread-affine; normal asset loads never touch this guard.
    /// </summary>
    internal static class AssetBundleRuntimeOwnership
    {
        private static object _owner;
        private static string _providerName;

        internal static string CurrentProviderName => _providerName;

        internal static void Acquire(object owner, string providerName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new ArgumentException("Provider name cannot be null or empty.", nameof(providerName));
            }

            if (_owner == null)
            {
                _owner = owner;
                _providerName = providerName;
                return;
            }

            if (ReferenceEquals(_owner, owner))
            {
                return;
            }

            throw new InvalidOperationException(
                $"AssetBundle runtime ownership already belongs to '{_providerName}'. " +
                $"Provider '{providerName}' cannot start in the same Player session because process-global " +
                "bundle cleanup would invalidate another provider's assets.");
        }

        internal static void Release(object owner)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (_owner == null)
            {
                return;
            }

            if (!ReferenceEquals(_owner, owner))
            {
                throw new InvalidOperationException(
                    $"Only the current AssetBundle runtime owner '{_providerName}' may release ownership.");
            }

            Reset();
        }

        internal static void Reset()
        {
            _owner = null;
            _providerName = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            Reset();
        }
    }
}
