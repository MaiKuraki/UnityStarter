using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public interface IDynamicAtlas : IDisposable
    {
        DynamicAtlasInsertStatus TryAcquire(
            string key,
            Texture2D source,
            out DynamicAtlasSpriteLease lease);

        DynamicAtlasInsertStatus TryAcquireRegion(
            string key,
            Texture2D source,
            RectInt sourceRect,
            out DynamicAtlasSpriteLease lease);

        DynamicAtlasInsertStatus TryAcquireSprite(
            string key,
            Sprite source,
            out DynamicAtlasSpriteLease lease);

        DynamicAtlasInsertStatus TryAcquireLocation(
            string location,
            out DynamicAtlasSpriteLease lease);

        bool TryAcquireCached(string key, out DynamicAtlasSpriteLease lease);

        bool TryGetSprite(string key, out Sprite sprite);

        int TrimUnused(int maximumEntriesToRemove = int.MaxValue);

        DynamicAtlasStats GetStats();

        int CopyPageSnapshots(List<DynamicAtlasPageSnapshot> destination);

        int CopyEntrySnapshots(List<DynamicAtlasEntrySnapshot> destination);

        DynamicAtlasWriteBatch BeginBatch();

        void Clear();
    }
}
