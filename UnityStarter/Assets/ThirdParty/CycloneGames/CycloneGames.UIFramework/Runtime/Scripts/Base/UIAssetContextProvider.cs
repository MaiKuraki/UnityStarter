using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Optional, synchronous default metadata for one UIRoot. It never loads assets
    /// and therefore cannot create a circular dependency on the provider it configures.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/UIFramework/UI Asset Context Provider")]
    public sealed class UIAssetContextProvider : MonoBehaviour
    {
        [SerializeField] private UIAssetContextAsset contextAsset;
        [SerializeField] private bool useEmbeddedSnapshot = true;

        [Header("Embedded Metadata Snapshot")]
        [SerializeField] private string snapshotConfigBucket;
        [SerializeField] private string snapshotConfigTag;
        [SerializeField] private string snapshotConfigOwner;
        [SerializeField] private string snapshotPrefabBucket;
        [SerializeField] private string snapshotPrefabTag;
        [SerializeField] private string snapshotPrefabOwner;

        public UIAssetContextAsset ContextAsset => contextAsset;
        public bool UseEmbeddedSnapshot => useEmbeddedSnapshot;

        public UIAssetLoadContext LoadContext
        {
            get
            {
                if (contextAsset != null)
                {
                    return contextAsset.ToLoadContext();
                }

                return useEmbeddedSnapshot
                    ? new UIAssetLoadContext(
                        snapshotConfigBucket,
                        snapshotConfigTag,
                        snapshotConfigOwner,
                        snapshotPrefabBucket,
                        snapshotPrefabTag,
                        snapshotPrefabOwner)
                    : default;
            }
        }

        public void SyncEmbeddedSnapshotFromAsset()
        {
            UIAssetLoadContext context = contextAsset != null
                ? contextAsset.ToLoadContext()
                : default;
            snapshotConfigBucket = context.ConfigBucket;
            snapshotConfigTag = context.ConfigTag;
            snapshotConfigOwner = context.ConfigOwner;
            snapshotPrefabBucket = context.PrefabBucket;
            snapshotPrefabTag = context.PrefabTag;
            snapshotPrefabOwner = context.PrefabOwner;
        }

        public void ClearEmbeddedSnapshot()
        {
            snapshotConfigBucket = null;
            snapshotConfigTag = null;
            snapshotConfigOwner = null;
            snapshotPrefabBucket = null;
            snapshotPrefabTag = null;
            snapshotPrefabOwner = null;
        }
    }
}
