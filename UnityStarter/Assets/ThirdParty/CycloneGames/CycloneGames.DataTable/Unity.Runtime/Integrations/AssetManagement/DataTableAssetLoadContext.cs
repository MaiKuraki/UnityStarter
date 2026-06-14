using System;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    public readonly struct DataTableAssetLoadContext
    {
        public readonly string Bucket;
        public readonly string Tag;
        public readonly string Owner;

        public DataTableAssetLoadContext(
            string bucket,
            string tag = null,
            string owner = null)
        {
            Bucket = bucket;
            Tag = tag;
            Owner = owner;
        }

        public bool HasAnyMetadata =>
            !string.IsNullOrEmpty(Bucket) ||
            !string.IsNullOrEmpty(Tag) ||
            !string.IsNullOrEmpty(Owner);

        public DataTableAssetLoadContext Merge(in DataTableAssetLoadContext fallback)
        {
            return new DataTableAssetLoadContext(
                Bucket ?? fallback.Bucket,
                Tag ?? fallback.Tag,
                Owner ?? fallback.Owner);
        }

        public DataTableAssetLoadContext WithOwner(string owner)
        {
            return new DataTableAssetLoadContext(Bucket, Tag, owner);
        }

        public static DataTableAssetLoadContext FromScope(AssetBucketScope scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            return new DataTableAssetLoadContext(
                scope.Bucket,
                scope.Tag,
                scope.Owner);
        }
    }
}
