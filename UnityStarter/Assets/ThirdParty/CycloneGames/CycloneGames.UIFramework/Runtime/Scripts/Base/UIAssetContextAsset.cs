using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    [CreateAssetMenu(fileName = "UIAssetContext_", menuName = "CycloneGames/UIFramework/UI Asset Context Asset")]
    public sealed class UIAssetContextAsset : ScriptableObject
    {
        [Header("Configuration Asset Metadata")]
        [SerializeField] private string configBucket;
        [SerializeField] private string configTag;
        [SerializeField] private string configOwner;

        [Header("Prefab Asset Metadata")]
        [SerializeField] private string prefabBucket;
        [SerializeField] private string prefabTag;
        [SerializeField] private string prefabOwner;

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

        public UIAssetLoadContext ToLoadContext()
        {
            return new UIAssetLoadContext(
                configBucket,
                configTag,
                configOwner,
                prefabBucket,
                prefabTag,
                prefabOwner);
        }
    }
}
