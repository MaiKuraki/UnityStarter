using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public enum DynamicAtlasRetentionPolicy
    {
        RemoveWhenUnused = 0,
        RetainUntilCapacityPressure = 1,
    }

    public enum DynamicAtlasOversizePolicy
    {
        Reject = 0,
        ScaleDown = 1,
    }

    public enum DynamicAtlasCopyFallback
    {
        GpuOnly = 0,
        AllowSynchronousReadback = 1,
        AllowCpuRawCopy = 2,
    }

    public enum DynamicAtlasInsertStatus
    {
        Success = 0,
        CacheHit = 1,
        InvalidKey = 2,
        InvalidSource = 3,
        InvalidRegion = 4,
        OversizedSource = 5,
        LoaderUnavailable = 6,
        UnsupportedSpritePacking = 7,
        EntryCapacityReached = 8,
        PageCapacityReached = 9,
        MemoryBudgetReached = 10,
        CopyUnsupported = 11,
        CopyFailed = 12,
        Disposed = 13,
    }

    public enum DynamicAtlasCopyPath
    {
        None = 0,
        GpuCopy = 1,
        CpuRawCopy = 2,
        SynchronousGpuReadback = 3,
    }

    public enum DynamicAtlasPageMode
    {
        GpuOnly = 0,
        CpuBacked = 1,
    }

    public readonly struct DynamicAtlasStats
    {
        public readonly int PageCount;
        public readonly int EntryCount;
        public readonly int ActiveEntryCount;
        public readonly int RetainedEntryCount;
        public readonly int ActiveReferenceCount;
        public readonly long PayloadPixelArea;
        public readonly long ReservedPixelArea;
        public readonly long TotalPixelArea;
        public readonly long EstimatedTextureBytes;
        public readonly long PendingDestructionBytes;
        public readonly long MemoryBudgetBytes;
        public readonly long CacheHitCount;
        public readonly long CacheMissCount;
        public readonly long InsertCount;
        public readonly long EvictionCount;
        public readonly long RejectionCount;
        public readonly long GpuCopyCount;
        public readonly long CpuRawCopyCount;
        public readonly long SynchronousReadbackCount;

        public float PayloadUsageRatio => TotalPixelArea > 0 ? (float)PayloadPixelArea / TotalPixelArea : 0f;
        public float ReservedUsageRatio => TotalPixelArea > 0 ? (float)ReservedPixelArea / TotalPixelArea : 0f;

        internal DynamicAtlasStats(
            int pageCount,
            int entryCount,
            int activeEntryCount,
            int retainedEntryCount,
            int activeReferenceCount,
            long payloadPixelArea,
            long reservedPixelArea,
            long totalPixelArea,
            long estimatedTextureBytes,
            long pendingDestructionBytes,
            long memoryBudgetBytes,
            long cacheHitCount,
            long cacheMissCount,
            long insertCount,
            long evictionCount,
            long rejectionCount,
            long gpuCopyCount,
            long cpuRawCopyCount,
            long synchronousReadbackCount)
        {
            PageCount = pageCount;
            EntryCount = entryCount;
            ActiveEntryCount = activeEntryCount;
            RetainedEntryCount = retainedEntryCount;
            ActiveReferenceCount = activeReferenceCount;
            PayloadPixelArea = payloadPixelArea;
            ReservedPixelArea = reservedPixelArea;
            TotalPixelArea = totalPixelArea;
            EstimatedTextureBytes = estimatedTextureBytes;
            PendingDestructionBytes = pendingDestructionBytes;
            MemoryBudgetBytes = memoryBudgetBytes;
            CacheHitCount = cacheHitCount;
            CacheMissCount = cacheMissCount;
            InsertCount = insertCount;
            EvictionCount = evictionCount;
            RejectionCount = rejectionCount;
            GpuCopyCount = gpuCopyCount;
            CpuRawCopyCount = cpuRawCopyCount;
            SynchronousReadbackCount = synchronousReadbackCount;
        }
    }

    public readonly struct DynamicAtlasPageSnapshot
    {
        public readonly int PageId;
        public readonly Texture2D Texture;
        public readonly DynamicAtlasPageMode Mode;
        public readonly int EntryCount;
        public readonly int ReleasedSlotCount;
        public readonly long PayloadPixelArea;
        public readonly long ReservedPixelArea;

        public int Width => Texture != null ? Texture.width : 0;
        public int Height => Texture != null ? Texture.height : 0;

        internal DynamicAtlasPageSnapshot(
            int pageId,
            Texture2D texture,
            DynamicAtlasPageMode mode,
            int entryCount,
            int releasedSlotCount,
            long payloadPixelArea,
            long reservedPixelArea)
        {
            PageId = pageId;
            Texture = texture;
            Mode = mode;
            EntryCount = entryCount;
            ReleasedSlotCount = releasedSlotCount;
            PayloadPixelArea = payloadPixelArea;
            ReservedPixelArea = reservedPixelArea;
        }
    }

    public readonly struct DynamicAtlasEntrySnapshot
    {
        public readonly string Key;
        public readonly Sprite Sprite;
        public readonly int PageId;
        public readonly int ReferenceCount;
        public readonly RectInt PixelRect;
        public readonly DynamicAtlasCopyPath CopyPath;
        public readonly long LastUseSequence;

        public bool IsRetained => ReferenceCount == 0;

        internal DynamicAtlasEntrySnapshot(
            string key,
            Sprite sprite,
            int pageId,
            int referenceCount,
            RectInt pixelRect,
            DynamicAtlasCopyPath copyPath,
            long lastUseSequence)
        {
            Key = key;
            Sprite = sprite;
            PageId = pageId;
            ReferenceCount = referenceCount;
            PixelRect = pixelRect;
            CopyPath = copyPath;
            LastUseSequence = lastUseSequence;
        }
    }
}
