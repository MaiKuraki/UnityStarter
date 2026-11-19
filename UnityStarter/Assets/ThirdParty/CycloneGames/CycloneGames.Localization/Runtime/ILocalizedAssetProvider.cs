using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// A single acquired localized asset. The consumer owns the lease and must dispose it
    /// exactly once. The provider may share the underlying asset or cache.
    /// </summary>
    public interface ILocalizedAssetLease<out TAsset> : IDisposable
        where TAsset : UnityEngine.Object
    {
        TAsset Asset { get; }
    }

    /// <summary>
    /// Provider boundary for localized assets. The location is the runtime key
    /// resolved by <see cref="ILocalizationService.ResolveAsset(string, string)"/>.
    /// </summary>
    /// <remarks>
    /// Unity objects returned by completed operations are consumed on the Unity main thread.
    /// The composition root owns the provider; localization never disposes it.
    /// </remarks>
    public interface ILocalizedAssetProvider
    {
        UniTask<ILocalizedAssetLease<TAsset>> AcquireAsync<TAsset>(
            string location,
            CancellationToken cancellationToken)
            where TAsset : UnityEngine.Object;
    }
}
