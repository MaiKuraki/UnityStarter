using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.DynamicAtlas
{
    /// <summary>
    /// No-op atlas for headless and dedicated-server builds. Acquires return
    /// <see cref="DynamicAtlasInsertStatus.Disabled"/> so callers fall back to their un-atlased
    /// path instead of receiving a lease that cannot produce a sprite.
    /// </summary>
    public sealed class NullDynamicAtlas : IDynamicAtlas
    {
        /// <summary>Stateless shared instance; Dispose leaves it usable.</summary>
        public static readonly NullDynamicAtlas Shared = new NullDynamicAtlas();

        private NullDynamicAtlas()
        {
        }

        public DynamicAtlasInsertStatus TryAcquire(
            string key,
            Texture2D source,
            out DynamicAtlasSpriteLease lease)
        {
            lease = default;
            return DynamicAtlasInsertStatus.Disabled;
        }

        public DynamicAtlasInsertStatus TryAcquireRegion(
            string key,
            Texture2D source,
            RectInt sourceRect,
            out DynamicAtlasSpriteLease lease)
        {
            lease = default;
            return DynamicAtlasInsertStatus.Disabled;
        }

        public DynamicAtlasInsertStatus TryAcquireSprite(
            string key,
            Sprite source,
            out DynamicAtlasSpriteLease lease)
        {
            lease = default;
            return DynamicAtlasInsertStatus.Disabled;
        }

        public DynamicAtlasInsertStatus TryAcquireLocation(
            string location,
            out DynamicAtlasSpriteLease lease)
        {
            lease = default;
            return DynamicAtlasInsertStatus.Disabled;
        }

        public bool TryAcquireCached(string key, out DynamicAtlasSpriteLease lease)
        {
            lease = default;
            return false;
        }

        public bool TryGetSprite(string key, out Sprite sprite)
        {
            sprite = null;
            return false;
        }

        public int TrimUnused(int maximumEntriesToRemove = int.MaxValue)
        {
            return 0;
        }

        public DynamicAtlasStats GetStats()
        {
            return default;
        }

        public int CopyPageSnapshots(List<DynamicAtlasPageSnapshot> destination)
        {
            return 0;
        }

        public int CopyEntrySnapshots(List<DynamicAtlasEntrySnapshot> destination)
        {
            return 0;
        }

        public int PrewarmPages(int pageCount, DynamicAtlasPageMode mode)
        {
            return 0;
        }

        /// <summary>Shared scope whose disposal is a no-op, so <c>using</c> blocks stay unchanged.</summary>
        public DynamicAtlasWriteBatch BeginBatch()
        {
            return DynamicAtlasWriteBatch.NoOp;
        }

        public void Clear()
        {
        }

        /// <summary>No-op; disposal is idempotent.</summary>
        public void Dispose()
        {
        }
    }
}
