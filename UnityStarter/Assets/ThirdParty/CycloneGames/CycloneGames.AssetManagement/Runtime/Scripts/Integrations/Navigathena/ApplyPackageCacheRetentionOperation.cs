#if NAVIGATHENA_PRESENT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// A Navigathena interrupt operation that applies an asset cache retention policy during a scene transition.
    /// </summary>
    public sealed class ApplyPackageCacheRetentionOperation : IAsyncOperation
    {
        private readonly IAssetPackage _assetPackage;
        private readonly AssetCacheRetentionPolicy _policy;

        /// <summary>
        /// Creates an operation that evicts every idle handle during the transition.
        /// </summary>
        public ApplyPackageCacheRetentionOperation(IAssetPackage assetPackage)
            : this(assetPackage, AssetCacheRetentionPolicy.EvictAllIdle)
        {
        }

        /// <summary>
        /// Creates an operation that applies the provided policy during the transition.
        /// </summary>
        public ApplyPackageCacheRetentionOperation(IAssetPackage assetPackage, AssetCacheRetentionPolicy policy)
        {
            _assetPackage = assetPackage ?? throw new ArgumentNullException(nameof(assetPackage));
            _policy = policy;
        }

        /// <summary>
        /// Creates a bucket-scoped transition operation. Use <paramref name="minimumIdleTime"/> to keep
        /// recently-released handles available for near-term reuse.
        /// </summary>
        public static ApplyPackageCacheRetentionOperation ForBucket(
            IAssetPackage assetPackage,
            string bucket,
            bool includeChildren = true,
            TimeSpan? minimumIdleTime = null)
        {
            var bucketRule = AssetCacheRetentionRules.Bucket(bucket, includeChildren);
            var policy = minimumIdleTime.HasValue
                ? AssetCacheRetentionPolicy.MatchingAll(
                    bucketRule,
                    AssetCacheRetentionRules.IdleForAtLeast(minimumIdleTime.Value))
                : new AssetCacheRetentionPolicy(bucketRule);

            return new ApplyPackageCacheRetentionOperation(assetPackage, policy);
        }

        public UniTask ExecuteAsync(IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _assetPackage.TrimIdleCache(_policy);
            return UniTask.CompletedTask;
        }
    }
}
#endif
