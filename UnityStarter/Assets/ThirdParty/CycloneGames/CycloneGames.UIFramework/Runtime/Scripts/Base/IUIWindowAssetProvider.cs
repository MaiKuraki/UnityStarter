using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// A single acquired reference to a UI asset. The consumer owns the lease and
    /// must dispose it exactly once. Providers may share the underlying asset/cache.
    /// </summary>
    public interface IUIAssetLease<out TAsset> : IDisposable where TAsset : UnityEngine.Object
    {
        TAsset Asset { get; }
    }

    /// <summary>
    /// Provider boundary for configuration and prefab loading. Implementations may
    /// use Addressables, YooAsset, Resources, or a project-specific content system.
    /// Unity objects returned by completed operations are consumed on the main thread.
    /// </summary>
    public interface IUIWindowAssetProvider
    {
        UniTask<IUIAssetLease<UIWindowConfiguration>> AcquireConfigurationAsync(
            string windowId,
            UIAssetLoadContext context,
            CancellationToken cancellationToken);

        UniTask<IUIAssetLease<GameObject>> AcquirePrefabAsync(
            UIAssetReference reference,
            UIAssetLoadContext context,
            CancellationToken cancellationToken);
    }
}
