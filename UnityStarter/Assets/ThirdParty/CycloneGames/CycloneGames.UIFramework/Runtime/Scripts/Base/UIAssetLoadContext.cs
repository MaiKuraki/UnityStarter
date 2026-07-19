using System;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Provider-neutral metadata for configuration and prefab loads. Providers that
    /// do not support a field may ignore it. Values are immutable after construction.
    /// </summary>
    [Serializable]
    public struct UIAssetLoadContext
    {
        [SerializeField] private string configBucket;
        [SerializeField] private string configTag;
        [SerializeField] private string configOwner;
        [SerializeField] private string prefabBucket;
        [SerializeField] private string prefabTag;
        [SerializeField] private string prefabOwner;

        public UIAssetLoadContext(string sharedBucket, string sharedTag = null, string sharedOwner = null)
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
            this.configBucket = configBucket;
            this.configTag = configTag;
            this.configOwner = configOwner;
            this.prefabBucket = prefabBucket;
            this.prefabTag = prefabTag;
            this.prefabOwner = prefabOwner;
        }

        public string ConfigBucket => configBucket;
        public string ConfigTag => configTag;
        public string ConfigOwner => configOwner;
        public string PrefabBucket => prefabBucket;
        public string PrefabTag => prefabTag;
        public string PrefabOwner => prefabOwner;

        public bool HasAnyMetadata =>
            !string.IsNullOrEmpty(configBucket) ||
            !string.IsNullOrEmpty(configTag) ||
            !string.IsNullOrEmpty(configOwner) ||
            !string.IsNullOrEmpty(prefabBucket) ||
            !string.IsNullOrEmpty(prefabTag) ||
            !string.IsNullOrEmpty(prefabOwner);

        public UIAssetLoadContext Merge(in UIAssetLoadContext fallback)
        {
            return new UIAssetLoadContext(
                configBucket ?? fallback.configBucket,
                configTag ?? fallback.configTag,
                configOwner ?? fallback.configOwner,
                prefabBucket ?? fallback.prefabBucket,
                prefabTag ?? fallback.prefabTag,
                prefabOwner ?? fallback.prefabOwner);
        }
    }
}
