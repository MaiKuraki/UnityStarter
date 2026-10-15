using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>Controls what happens to an entry when its reference count reaches zero.</summary>
    public enum DynamicAtlasRetentionPolicy
    {
        /// <summary>The entry is destroyed immediately and its slot becomes reusable.</summary>
        RemoveWhenUnused = 0,
        /// <summary>The entry stays cached until capacity pressure evicts it in LRU order.</summary>
        RetainUntilCapacityPressure = 1,
    }

    /// <summary>Controls how sources larger than one page are handled.</summary>
    public enum DynamicAtlasOversizePolicy
    {
        /// <summary>Oversized sources are rejected with <see cref="DynamicAtlasInsertStatus.OversizedSource"/>.</summary>
        Reject = 0,
        /// <summary>Oversized sources are scaled down through the synchronous readback path.</summary>
        ScaleDown = 1,
    }

    /// <summary>Controls which pixel-copy paths may run when GPU copy is unavailable.</summary>
    public enum DynamicAtlasCopyFallback
    {
        /// <summary>Only Graphics.CopyTexture; everything else is rejected.</summary>
        GpuOnly = 0,
        /// <summary>Also permit a synchronous Blit/ReadPixels fallback.</summary>
        AllowSynchronousReadback = 1,
        /// <summary>Also permit a direct CPU raw copy between matching readable formats.</summary>
        AllowCpuRawCopy = 2,
    }

    /// <summary>Result of an acquire call. Only <see cref="Success"/> and <see cref="CacheHit"/> produce a valid lease.</summary>
    public enum DynamicAtlasInsertStatus
    {
        /// <summary>New entry inserted; the lease owns the first reference.</summary>
        Success = 0,
        /// <summary>Existing entry retained; no pixel work ran.</summary>
        CacheHit = 1,
        /// <summary>Key is null, empty, or longer than the configured maximum.</summary>
        InvalidKey = 2,
        /// <summary>Source texture or sprite is null, destroyed, or otherwise unusable.</summary>
        InvalidSource = 3,
        /// <summary>Requested region is empty or outside the source bounds.</summary>
        InvalidRegion = 4,
        /// <summary>Source exceeds one page and the oversize policy rejected it.</summary>
        OversizedSource = 5,
        /// <summary>Acquisition by location requires a configured loader.</summary>
        LoaderUnavailable = 6,
        /// <summary>Sprite packing or geometry is incompatible with region extraction.</summary>
        UnsupportedSpritePacking = 7,
        /// <summary>Entry count ceiling reached and no zero-reference entry could be evicted.</summary>
        EntryCapacityReached = 8,
        /// <summary>Page count ceiling reached, including pages pending destruction.</summary>
        PageCapacityReached = 9,
        /// <summary>Texture byte budget would be exceeded.</summary>
        MemoryBudgetReached = 10,
        /// <summary>No configured copy path supports the source/destination format pair.</summary>
        CopyUnsupported = 11,
        /// <summary>The selected copy path failed at runtime.</summary>
        CopyFailed = 12,
        /// <summary>The service is disposed.</summary>
        Disposed = 13,
        /// <summary>
        /// The per-frame miss budget was exhausted. No pixel work ran; callers should retry on a later frame.
        /// </summary>
        Throttled = 14,
    }

    /// <summary>Pixel-copy path used for an insert, reported in stats and snapshots.</summary>
    public enum DynamicAtlasCopyPath
    {
        /// <summary>No copy performed (cache hit or rejected insert).</summary>
        None = 0,
        /// <summary>Graphics.CopyTexture on the GPU.</summary>
        GpuCopy = 1,
        /// <summary>Direct CPU copy between matching readable formats.</summary>
        CpuRawCopy = 2,
        /// <summary>Blit/ReadPixels fallback.</summary>
        SynchronousGpuReadback = 3,
    }

    /// <summary>Page texture residency.</summary>
    public enum DynamicAtlasPageMode
    {
        /// <summary>GPU-only texture, not readable after upload.</summary>
        GpuOnly = 0,
        /// <summary>Texture keeps a CPU copy for raw writes and bleed padding.</summary>
        CpuBacked = 1,
    }

    /// <summary>Bounded counters snapshot; all values are read on the owner thread.</summary>
    public readonly struct DynamicAtlasStats
    {
        public readonly int PageCount;
        public readonly int PageCapacity;
        public readonly int EntryCount;
        public readonly int EntryCapacity;
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
            int pageCapacity,
            int entryCount,
            int entryCapacity,
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
            PageCapacity = pageCapacity;
            EntryCount = entryCount;
            EntryCapacity = entryCapacity;
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

    /// <summary>Point-in-time view of one atlas page, written into a caller-owned buffer.</summary>
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

    /// <summary>Point-in-time view of one atlas entry, written into a caller-owned buffer.</summary>
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
