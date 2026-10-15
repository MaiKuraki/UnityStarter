using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Owner-thread contract for a bounded runtime sprite atlas. All members must be called
    /// on the Unity main thread that created the implementation; off-thread calls throw.
    /// Acquire members return a status code because callers need the failure reason;
    /// cached lookup members return bool because only presence matters.
    /// </summary>
    public interface IDynamicAtlas : IDisposable
    {
        /// <summary>Acquires a sprite lease for the whole source texture, copying pixels on a cache miss.</summary>
        DynamicAtlasInsertStatus TryAcquire(
            string key,
            Texture2D source,
            out DynamicAtlasSpriteLease lease);

        /// <summary>Acquires a sprite lease for a sub-region of the source texture.</summary>
        DynamicAtlasInsertStatus TryAcquireRegion(
            string key,
            Texture2D source,
            RectInt sourceRect,
            out DynamicAtlasSpriteLease lease);

        /// <summary>Acquires a sprite lease for the rect of an existing sprite.</summary>
        DynamicAtlasInsertStatus TryAcquireSprite(
            string key,
            Sprite source,
            out DynamicAtlasSpriteLease lease);

        /// <summary>Acquires a sprite lease by resolving the key through the configured loader.</summary>
        DynamicAtlasInsertStatus TryAcquireLocation(
            string location,
            out DynamicAtlasSpriteLease lease);

        /// <summary>Retains an existing entry without copying pixels; false when the key is unknown.</summary>
        bool TryAcquireCached(string key, out DynamicAtlasSpriteLease lease);

        /// <summary>Reads the cached sprite without retaining a reference; false when the key is unknown.</summary>
        bool TryGetSprite(string key, out Sprite sprite);

        /// <summary>Removes up to <paramref name="maximumEntriesToRemove"/> zero-reference retained entries.</summary>
        int TrimUnused(int maximumEntriesToRemove = int.MaxValue);

        /// <summary>Returns a bounded snapshot of counters and capacity usage.</summary>
        DynamicAtlasStats GetStats();

        /// <summary>Writes per-page snapshots into the caller-owned buffer; returns the written count.</summary>
        int CopyPageSnapshots(List<DynamicAtlasPageSnapshot> destination);

        /// <summary>Writes per-entry snapshots into the caller-owned buffer; returns the written count.</summary>
        int CopyEntrySnapshots(List<DynamicAtlasEntrySnapshot> destination);

        /// <summary>Pre-allocates pages so later acquisitions avoid page creation; returns the created count.</summary>
        int PrewarmPages(int pageCount, DynamicAtlasPageMode mode);

        /// <summary>
        /// Defers texture uploads until the returned batch is disposed. Batches nest on the
        /// owner thread; disposing a stale batch after Clear is a no-op.
        /// </summary>
        DynamicAtlasWriteBatch BeginBatch();

        /// <summary>Evicts every entry and page; outstanding leases become stale no-ops.</summary>
        void Clear();
    }
}
