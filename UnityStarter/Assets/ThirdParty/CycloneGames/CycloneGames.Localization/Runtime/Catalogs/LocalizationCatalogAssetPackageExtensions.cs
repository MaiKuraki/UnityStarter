using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Cold-path catalog loading helpers for <see cref="IAssetPackage"/> providers.
    /// The catalog lease is always released after the service copies and validates its content.
    /// </summary>
    public static class LocalizationCatalogAssetPackageExtensions
    {
        public static async UniTask<bool> LoadAndRegisterCatalogAsync(
            this ILocalizationService localization,
            IAssetPackage assetPackage,
            string ownerId,
            string location,
            string bucket = null,
            string tag = null,
            CancellationToken cancellationToken = default)
        {
            if (localization == null) throw new ArgumentNullException(nameof(localization));
            if (assetPackage == null) throw new ArgumentNullException(nameof(assetPackage));
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("Catalog location is required.", nameof(location));

            IAssetHandle<LocalizationCatalog> handle = assetPackage.LoadAssetAsync<LocalizationCatalog>(
                location,
                bucket,
                tag,
                ownerId,
                cancellationToken);
            if (handle == null)
                throw new InvalidOperationException("The asset package returned a null catalog handle.");

            try
            {
                await handle.Task;
                LocalizationCatalog catalog = handle.Asset;
                if (catalog == null)
                    throw new InvalidOperationException("The catalog load completed without an asset.");

                return localization.TryRegisterCatalog(ownerId, catalog);
            }
            finally
            {
                handle.Dispose();
            }
        }
    }
}
