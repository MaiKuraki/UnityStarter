using CycloneGames.DynamicAtlas;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.DynamicAtlas.Samples
{
    [DisallowMultipleComponent]
    public sealed class DynamicAtlasLeaseSample : MonoBehaviour
    {
        private static readonly LogChannel Log = DynamicAtlasSampleLog.Channel;

        private const string DefaultStableKey = "samples/dynamic-atlas/icon";

        [SerializeField]
        [Tooltip("Renderer that receives the generated atlas sprite.")]
        private SpriteRenderer targetRenderer;

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
            if (_spriteLease.IsValid)
            {
                return;
            }

            if (targetRenderer == null || sourceSprite == null || string.IsNullOrEmpty(stableKey))
            {
                Log.Error(
                    "DynamicAtlasLeaseSample requires a SpriteRenderer, a source Sprite, and a stable key.");
                return;
            }

            DynamicAtlasInsertStatus status = _atlas.TryAcquireSprite(
                stableKey,
                sourceSprite,
                out _spriteLease);

            if (status == DynamicAtlasInsertStatus.Success || status == DynamicAtlasInsertStatus.CacheHit)
            {
                targetRenderer.sprite = _spriteLease.Sprite;
                return;
            }

            _spriteLease = default;
            Log.Error(
                status,
                static (value, builder) => builder
                    .Append("Dynamic atlas insertion failed with status ")
                    .Append(value)
                    .Append('.'));
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
            if (!_spriteLease.IsValid)
            {
                return;
            }

            if (targetRenderer != null && targetRenderer.sprite == _spriteLease.Sprite)
            {
                targetRenderer.sprite = null;
            }

            _spriteLease.Dispose();
            _spriteLease = default;
        }
    }
}
