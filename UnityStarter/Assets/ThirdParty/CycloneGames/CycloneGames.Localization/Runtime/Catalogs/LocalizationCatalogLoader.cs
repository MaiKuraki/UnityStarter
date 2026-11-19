using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Cold-path catalog loading helpers for any <see cref="ILocalizedAssetProvider"/>.
    /// The catalog lease is always released after the service copies and validates its content.
    /// </summary>
    public static class LocalizationCatalogLoader
    {
        private const int MaxLocationLength = 1024;

        public static async UniTask<bool> LoadAndRegisterCatalogAsync(
            this ILocalizationService localization,
            ILocalizedAssetProvider provider,
            string ownerId,
            string location,
            CancellationToken cancellationToken = default)
        {
            if (localization == null) throw new ArgumentNullException(nameof(localization));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("Catalog location is required.", nameof(location));
            if (location.Length > MaxLocationLength || location.IndexOf('\0') >= 0)
                throw new ArgumentException(
                    "Catalog location exceeds the allowed length or contains an invalid character.",
                    nameof(location));

            ILocalizedAssetLease<LocalizationCatalog> lease =
                await provider.AcquireAsync<LocalizationCatalog>(location, cancellationToken);
            if (lease == null)
                throw new InvalidOperationException("The asset provider returned a null catalog lease.");

            try
            {
                LocalizationCatalog catalog = lease.Asset;
                if (catalog == null)
                    throw new InvalidOperationException("The catalog load completed without an asset.");

                return localization.TryRegisterCatalog(ownerId, catalog);
            }
            finally
            {
                lease.Dispose();
            }
        }
    }
}
