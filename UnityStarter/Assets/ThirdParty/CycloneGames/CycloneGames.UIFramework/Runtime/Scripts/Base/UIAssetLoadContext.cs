using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Optional metadata applied to UI configuration and prefab asset loads.
    /// Use this to attach custom bucket / tag / owner values without hard-coding lifecycle names in the framework.
    /// </summary>
    public readonly struct UIAssetLoadContext
    {
        public readonly string ConfigBucket;
        public readonly string ConfigTag;
        public readonly string ConfigOwner;
        public readonly string PrefabBucket;
        public readonly string PrefabTag;
        public readonly string PrefabOwner;

        public UIAssetLoadContext(
            string sharedBucket = null,
            string sharedTag = null,
            string sharedOwner = null)
            : this(sharedBucket, sharedTag, sharedOwner, sharedBucket, sharedTag, sharedOwner)
        {
        }

        public UIAssetLoadContext(
            string configBucket,
            string configTag,
            string configOwner,
            string prefabBucket,
            string prefabTag,
            string prefabOwner)
        {
            ConfigBucket = configBucket;
            ConfigTag = configTag;
            ConfigOwner = configOwner;
            PrefabBucket = prefabBucket;
            PrefabTag = prefabTag;
            PrefabOwner = prefabOwner;
        }

        public bool HasAnyMetadata =>
            !string.IsNullOrEmpty(ConfigBucket) ||
            !string.IsNullOrEmpty(ConfigTag) ||
            !string.IsNullOrEmpty(ConfigOwner) ||
            !string.IsNullOrEmpty(PrefabBucket) ||
            !string.IsNullOrEmpty(PrefabTag) ||
            !string.IsNullOrEmpty(PrefabOwner);

        public UIAssetLoadContext Merge(in UIAssetLoadContext fallback)
        {
            return new UIAssetLoadContext(
                ConfigBucket ?? fallback.ConfigBucket,
                ConfigTag ?? fallback.ConfigTag,
                ConfigOwner ?? fallback.ConfigOwner,
                PrefabBucket ?? fallback.PrefabBucket,
                PrefabTag ?? fallback.PrefabTag,
                PrefabOwner ?? fallback.PrefabOwner);
        }

        public static UIAssetLoadContext FromScope(AssetBucketScope scope)
        {
            return new UIAssetLoadContext(
                scope.Bucket,
                scope.Tag,
                scope.Owner,
                scope.Bucket,
                scope.Tag,
                scope.Owner);
        }

        public static UIAssetLoadContext FromScopes(AssetBucketScope configScope, AssetBucketScope prefabScope)
        {
            return new UIAssetLoadContext(
                configScope.Bucket,
                configScope.Tag,
                configScope.Owner,
                prefabScope.Bucket,
                prefabScope.Tag,
                prefabScope.Owner);
        }
    }
}
