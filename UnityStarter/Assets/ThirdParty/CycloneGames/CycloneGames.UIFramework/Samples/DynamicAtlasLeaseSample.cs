using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Samples
{
    [DisallowMultipleComponent]
    public sealed class DynamicAtlasLeaseSample : MonoBehaviour
    {
        private const string DefaultStableKey = "samples/ui-framework/dynamic-atlas/icon";

        [SerializeField]
        [Tooltip("Image that receives the generated atlas sprite.")]
        private Image targetImage;

        [SerializeField]
        [Tooltip("Rectangular source sprite. SpriteAtlas rotation and Tight Packing must be disabled.")]
        private Sprite sourceSprite;

        [SerializeField]
        [Tooltip("Stable, namespaced content identity used for deduplication.")]
        private string stableKey = DefaultStableKey;

        private DynamicAtlasService _atlas;
        private DynamicAtlasSpriteLease _spriteLease;

        private void Awake()
        {
            var config = new DynamicAtlasConfig
            {
                pageSize = 512,
                maxPages = 1,
                minRetainedPages = 0,
                maxEntries = 64,
                maxEntriesPerPage = 64,
                maxKeyLength = 128,
                memoryBudgetBytes = 2L * 1024L * 1024L,
                padding = 2,
                enableBleed = true,
                filterMode = FilterMode.Bilinear,
                retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowSynchronousReadback,
                defaultPixelsPerUnit = 100f,
            };

            _atlas = new DynamicAtlasService(config);
        }

        private void OnEnable()
        {
            if (_spriteLease != null)
            {
                return;
            }

            if (targetImage == null || sourceSprite == null || string.IsNullOrEmpty(stableKey))
            {
                Debug.LogError("DynamicAtlasLeaseSample requires an Image, a source Sprite, and a stable key.", this);
                return;
            }

            DynamicAtlasInsertStatus status = _atlas.TryAcquireSprite(
                stableKey,
                sourceSprite,
                out _spriteLease);

            if (status == DynamicAtlasInsertStatus.Success || status == DynamicAtlasInsertStatus.CacheHit)
            {
                targetImage.sprite = _spriteLease.Sprite;
                return;
            }

            _spriteLease = null;
            Debug.LogError($"Dynamic atlas insertion failed with status {status}.", this);
        }

        private void OnDisable()
        {
            ReleaseSprite();
        }

        private void OnDestroy()
        {
            ReleaseSprite();
            _atlas?.Dispose();
            _atlas = null;
        }

        private void ReleaseSprite()
        {
            if (_spriteLease == null)
            {
                return;
            }

            if (targetImage != null && targetImage.sprite == _spriteLease.Sprite)
            {
                targetImage.sprite = null;
            }

            _spriteLease.Dispose();
            _spriteLease = null;
        }
    }
}
