using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public sealed class DynamicAtlasService : IDynamicAtlas
    {
        private const TextureFormat AtlasTextureFormat = TextureFormat.RGBA32;

        private sealed class AtlasEntry
        {
            internal string Key;
            internal Sprite Sprite;
            internal DynamicAtlasPage Page;
            internal DynamicAtlasPlacement Placement;
            internal DynamicAtlasCopyPath CopyPath;
            internal int ReferenceCount;
            internal long Generation;
            internal long LastUseSequence;
            internal AtlasEntry RetainedPrevious;
            internal AtlasEntry RetainedNext;
            internal bool IsRetainedLinked;

            internal void Reset()
            {
                Key = null;
                Sprite = null;
                Page = null;
                Placement = default;
                CopyPath = DynamicAtlasCopyPath.None;
                ReferenceCount = 0;
                Generation = 0;
                LastUseSequence = 0;
                RetainedPrevious = null;
                RetainedNext = null;
                IsRetainedLinked = false;
            }
        }

        private const int MaximumPooledEntries = 256;

        private readonly DynamicAtlasConfig _config;
        private readonly DynamicAtlasThreadGuard _threadGuard;
        private readonly Dictionary<string, AtlasEntry> _entries;
        private readonly List<DynamicAtlasPage> _pages;
        private readonly Stack<AtlasEntry> _entryPool;
        private readonly Func<string, Texture2D> _loadFunc;
        private readonly Action<string, Texture2D> _unloadFunc;

        private bool _disposed;
        private int _batchDepth;
        private long _batchEpoch = 1;
        private long _nextEntryGeneration;
        private AtlasEntry _oldestRetainedEntry;
        private AtlasEntry _newestRetainedEntry;
        private long _useSequence;
        private long _estimatedTextureBytes;
        private long _pendingDestructionBytes;
        private int _pendingDestructionPageCount;
        private int _pendingDestructionFrame = -1;
        private long _cacheHitCount;
        private long _cacheMissCount;
        private long _insertCount;
        private long _evictionCount;
        private long _rejectionCount;
        private long _gpuCopyCount;
        private long _cpuRawCopyCount;
        private long _synchronousReadbackCount;

#if UNITY_EDITOR
        private static readonly List<WeakReference<DynamicAtlasService>> ActiveEditorServices =
            new List<WeakReference<DynamicAtlasService>>(8);
#endif

        public DynamicAtlasConfig Configuration => _config.Copy();
        public bool IsDisposed => _disposed;
        internal bool IsOwnerThread => _threadGuard.IsOwnerThread;

        public DynamicAtlasService(DynamicAtlasConfig config = null)
        {
            _threadGuard = new DynamicAtlasThreadGuard();
            _config = (config ?? new DynamicAtlasConfig()).Copy();

            if (!_config.Validate(out string validationError))
            {
                throw new ArgumentException(validationError, nameof(config));
            }

            if (_config.pageSize > SystemInfo.maxTextureSize)
            {
                throw new ArgumentException(
                    $"Page size {_config.pageSize} exceeds this device's maximum texture size {SystemInfo.maxTextureSize}.",
                    nameof(config));
            }

            if (!SystemInfo.SupportsTextureFormat(AtlasTextureFormat))
            {
                throw new NotSupportedException(
                    $"Texture format {AtlasTextureFormat} is not supported by the active graphics device.");
            }

            _entries = new Dictionary<string, AtlasEntry>(
                Mathf.Min(_config.maxEntries, 256),
                StringComparer.Ordinal);
            _pages = new List<DynamicAtlasPage>(_config.maxPages);
            _entryPool = new Stack<AtlasEntry>(Mathf.Min(MaximumPooledEntries, _config.maxEntries));
            _loadFunc = _config.loadFunc;
            _unloadFunc = _config.unloadFunc;

#if UNITY_EDITOR
            RegisterEditorService(this);
#endif
        }

        public DynamicAtlasInsertStatus TryAcquire(
            string key,
            Texture2D source,
            out DynamicAtlasSpriteLease lease)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryAcquire));
            RectInt sourceRect = source != null
                ? new RectInt(0, 0, source.width, source.height)
                : default;
            DynamicAtlasInsertStatus status = TryAcquireEntry(
                key,
                source,
                sourceRect,
                new Vector2(0.5f, 0.5f),
                Vector4.zero,
                _config.defaultPixelsPerUnit,
                out AtlasEntry entry);
            lease = CreateLease(entry);
            return status;
        }

        public DynamicAtlasInsertStatus TryAcquireRegion(
            string key,
            Texture2D source,
            RectInt sourceRect,
            out DynamicAtlasSpriteLease lease)
        {
            DynamicAtlasInsertStatus status = TryAcquireEntry(
                key,
                source,
                sourceRect,
                new Vector2(0.5f, 0.5f),
                Vector4.zero,
                _config.defaultPixelsPerUnit,
                out AtlasEntry entry);
            lease = CreateLease(entry);
            return status;
        }

        public DynamicAtlasInsertStatus TryAcquireSprite(
            string key,
            Sprite source,
            out DynamicAtlasSpriteLease lease)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryAcquireSprite));
            lease = null;

            DynamicAtlasInsertStatus state = ValidateOperationAndKey(key);
            if (state != DynamicAtlasInsertStatus.Success)
            {
                RecordRejection(state);
                return state;
            }

            if (TryRetainCachedEntry(key, out AtlasEntry cachedEntry))
            {
                lease = CreateLease(cachedEntry);
                return DynamicAtlasInsertStatus.CacheHit;
            }

            if (!TryPrepareCacheMiss(out state))
            {
                RecordRejection(state);
                return state;
            }

            if (source == null || source.texture == null)
            {
                RecordRejection(DynamicAtlasInsertStatus.InvalidSource);
                return DynamicAtlasInsertStatus.InvalidSource;
            }

            if (source.packed &&
                (source.packingMode == SpritePackingMode.Tight ||
                 source.packingRotation != SpritePackingRotation.None))
            {
                RecordRejection(DynamicAtlasInsertStatus.UnsupportedSpritePacking);
                return DynamicAtlasInsertStatus.UnsupportedSpritePacking;
            }

            Rect textureRect;
            try
            {
                textureRect = source.textureRect;
            }
            catch (UnityException)
            {
                RecordRejection(DynamicAtlasInsertStatus.UnsupportedSpritePacking);
                return DynamicAtlasInsertStatus.UnsupportedSpritePacking;
            }

            Rect sourceSpriteRect = source.rect;
            if (sourceSpriteRect.width <= 0f || sourceSpriteRect.height <= 0f)
            {
                RecordRejection(DynamicAtlasInsertStatus.InvalidRegion);
                return DynamicAtlasInsertStatus.InvalidRegion;
            }

            if (!Mathf.Approximately(textureRect.width, sourceSpriteRect.width) ||
                !Mathf.Approximately(textureRect.height, sourceSpriteRect.height))
            {
                RecordRejection(DynamicAtlasInsertStatus.UnsupportedSpritePacking);
                return DynamicAtlasInsertStatus.UnsupportedSpritePacking;
            }

            if (!HasFullRectGeometry(source, sourceSpriteRect))
            {
                RecordRejection(DynamicAtlasInsertStatus.UnsupportedSpritePacking);
                return DynamicAtlasInsertStatus.UnsupportedSpritePacking;
            }

            Vector2 pivot = new Vector2(
                source.pivot.x / sourceSpriteRect.width,
                source.pivot.y / sourceSpriteRect.height);
            RectInt integerTextureRect = ToPixelRect(textureRect);
            DynamicAtlasInsertStatus status = TryAcquireEntry(
                key,
                source.texture,
                integerTextureRect,
                pivot,
                source.border,
                source.pixelsPerUnit,
                out AtlasEntry entry,
                skipCacheLookup: true,
                cacheMissPrepared: true);
            lease = CreateLease(entry);
            return status;
        }

        public DynamicAtlasInsertStatus TryAcquireLocation(
            string location,
            out DynamicAtlasSpriteLease lease)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryAcquireLocation));
            lease = null;

            DynamicAtlasInsertStatus state = ValidateOperationAndKey(location);
            if (state != DynamicAtlasInsertStatus.Success)
            {
                RecordRejection(state);
                return state;
            }

            if (TryRetainCachedEntry(location, out AtlasEntry cachedEntry))
            {
                lease = CreateLease(cachedEntry);
                return DynamicAtlasInsertStatus.CacheHit;
            }

            if (!TryPrepareCacheMiss(out state))
            {
                RecordRejection(state);
                return state;
            }

            if (_loadFunc == null || _unloadFunc == null)
            {
                RecordRejection(DynamicAtlasInsertStatus.LoaderUnavailable);
                return DynamicAtlasInsertStatus.LoaderUnavailable;
            }

            Texture2D source = null;
            try
            {
                source = _loadFunc(location);
                if (source == null)
                {
                    RecordRejection(DynamicAtlasInsertStatus.InvalidSource);
                    return DynamicAtlasInsertStatus.InvalidSource;
                }

                RectInt sourceRect = new RectInt(0, 0, source.width, source.height);
                DynamicAtlasInsertStatus status = TryAcquireEntry(
                    location,
                    source,
                    sourceRect,
                    new Vector2(0.5f, 0.5f),
                    Vector4.zero,
                    _config.defaultPixelsPerUnit,
                    out AtlasEntry entry,
                    skipCacheLookup: true,
                    cacheMissPrepared: true);
                lease = CreateLease(entry);
                return status;
            }
            finally
            {
                if (source != null)
                {
                    SafeUnload(location, source);
                }
            }
        }

        public bool TryAcquireCached(string key, out DynamicAtlasSpriteLease lease)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryAcquireCached));
            lease = null;

            if (_disposed || !IsValidKey(key))
            {
                return false;
            }

            if (!TryRetainCachedEntry(key, out AtlasEntry entry))
            {
                return false;
            }

            lease = CreateLease(entry);
            return true;
        }

        public bool TryGetSprite(string key, out Sprite sprite)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryGetSprite));
            sprite = null;

            if (_disposed || !IsValidKey(key) || !_entries.TryGetValue(key, out AtlasEntry entry))
            {
                return false;
            }

            if (entry.Sprite == null)
            {
                RemoveEntry(entry, countAsEviction: false);
                return false;
            }

            sprite = entry.Sprite;
            return true;
        }

        public int TrimUnused(int maximumEntriesToRemove = int.MaxValue)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TrimUnused));
            if (_disposed || maximumEntriesToRemove <= 0)
            {
                return 0;
            }

            int removed = 0;
            while (removed < maximumEntriesToRemove && TryEvictOldestUnused())
            {
                removed++;
            }

            return removed;
        }

        public DynamicAtlasStats GetStats()
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(GetStats));
            RefreshPendingDestructionBytes();
            if (_disposed)
            {
                return default;
            }

            int activeEntries = 0;
            int retainedEntries = 0;
            int activeReferences = 0;
            long payloadArea = 0;
            long reservedArea = 0;

            foreach (KeyValuePair<string, AtlasEntry> pair in _entries)
            {
                AtlasEntry entry = pair.Value;
                if (entry.ReferenceCount > 0)
                {
                    activeEntries++;
                    activeReferences += entry.ReferenceCount;
                }
                else
                {
                    retainedEntries++;
                }
            }

            for (int i = 0; i < _pages.Count; i++)
            {
                payloadArea += _pages[i].PayloadPixelArea;
                reservedArea += _pages[i].ReservedPixelArea;
            }

            long totalArea = (long)_pages.Count * _config.pageSize * _config.pageSize;
            return new DynamicAtlasStats(
                _pages.Count,
                _entries.Count,
                activeEntries,
                retainedEntries,
                activeReferences,
                payloadArea,
                reservedArea,
                totalArea,
                _estimatedTextureBytes,
                _pendingDestructionBytes,
                _config.memoryBudgetBytes,
                _cacheHitCount,
                _cacheMissCount,
                _insertCount,
                _evictionCount,
                _rejectionCount,
                _gpuCopyCount,
                _cpuRawCopyCount,
                _synchronousReadbackCount);
        }

        public int CopyPageSnapshots(List<DynamicAtlasPageSnapshot> destination)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(CopyPageSnapshots));
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (_disposed)
            {
                return 0;
            }

            for (int i = 0; i < _pages.Count; i++)
            {
                destination.Add(_pages[i].CreateSnapshot());
            }

            return destination.Count;
        }

        public int CopyEntrySnapshots(List<DynamicAtlasEntrySnapshot> destination)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(CopyEntrySnapshots));
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (_disposed)
            {
                return 0;
            }

            foreach (KeyValuePair<string, AtlasEntry> pair in _entries)
            {
                AtlasEntry entry = pair.Value;
                destination.Add(new DynamicAtlasEntrySnapshot(
                    entry.Key,
                    entry.Sprite,
                    entry.Page.PageId,
                    entry.ReferenceCount,
                    entry.Placement.ContentRect,
                    entry.CopyPath,
                    entry.LastUseSequence));
            }

            return destination.Count;
        }

        public DynamicAtlasWriteBatch BeginBatch()
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(BeginBatch));
            ThrowIfDisposed();
            _batchDepth++;
            return new DynamicAtlasWriteBatch(this, _batchEpoch);
        }

        public int PrewarmPages(int pageCount, DynamicAtlasPageMode mode)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(PrewarmPages));
            ThrowIfDisposed();

            if (pageCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount));
            }

            if (mode != DynamicAtlasPageMode.GpuOnly &&
                mode != DynamicAtlasPageMode.CpuBacked)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown dynamic atlas page mode.");
            }

            if (mode == DynamicAtlasPageMode.CpuBacked &&
                !TextureFormatHelper.AllowsCpuBacking(_config.copyFallback))
            {
                throw new InvalidOperationException("CPU-backed pages require a CPU-copy fallback policy.");
            }

            int created = 0;
            while (created < pageCount && TryCreatePage(mode, out _, out _))
            {
                created++;
            }

            return created;
        }

        public void Clear()
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(Clear));
            if (_disposed)
            {
                return;
            }

            ClearCore();
        }

        public void Dispose()
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(Dispose));
            if (_disposed)
            {
                return;
            }

            ClearCore();
            _disposed = true;
#if UNITY_EDITOR
            UnregisterEditorService(this);
#endif
        }

        internal void ReleaseLease(string key, long entryGeneration)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(ReleaseLease));
            if (_disposed || !_entries.TryGetValue(key, out AtlasEntry entry) || entry.Generation != entryGeneration)
            {
                return;
            }

            ReleaseEntryReference(entry);
        }

        internal void EndBatch(long batchEpoch)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(EndBatch));
            if (_disposed)
            {
                return;
            }

            if (batchEpoch != _batchEpoch)
            {
                return;
            }

            if (_batchDepth <= 0)
            {
                throw new InvalidOperationException("Dynamic atlas batch depth is already zero.");
            }

            _batchDepth--;
            if (_batchDepth == 0)
            {
                FlushPendingUploads();
            }
        }

#if UNITY_EDITOR
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static int CopyActiveEditorServices(List<DynamicAtlasService> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int i = ActiveEditorServices.Count - 1; i >= 0; i--)
            {
                if (!ActiveEditorServices[i].TryGetTarget(out DynamicAtlasService service) || service == null || service._disposed)
                {
                    ActiveEditorServices.RemoveAt(i);
                    continue;
                }

                destination.Add(service);
            }

            return destination.Count;
        }
#endif

        private DynamicAtlasInsertStatus TryAcquireEntry(
            string key,
            Texture2D source,
            RectInt sourceRect,
            Vector2 pivot,
            Vector4 border,
            float pixelsPerUnit,
            out AtlasEntry entry,
            bool skipCacheLookup = false,
            bool cacheMissPrepared = false)
        {
            _threadGuard.ThrowIfNotOwnerThread(nameof(TryAcquireEntry));
            entry = null;

            DynamicAtlasInsertStatus state = ValidateOperationAndKey(key);
            if (state != DynamicAtlasInsertStatus.Success)
            {
                RecordRejection(state);
                return state;
            }

            if (!skipCacheLookup && TryRetainCachedEntry(key, out entry))
            {
                return DynamicAtlasInsertStatus.CacheHit;
            }

            if (!cacheMissPrepared && !TryPrepareCacheMiss(out state))
            {
                RecordRejection(state);
                return state;
            }

            if (source == null)
            {
                RecordRejection(DynamicAtlasInsertStatus.InvalidSource);
                return DynamicAtlasInsertStatus.InvalidSource;
            }

            if (!IsValidRegion(source, sourceRect) || !IsValidSpriteMetadata(pivot, border, sourceRect, pixelsPerUnit))
            {
                RecordRejection(DynamicAtlasInsertStatus.InvalidRegion);
                return DynamicAtlasInsertStatus.InvalidRegion;
            }

            Texture2D insertionSource = source;
            RectInt insertionRect = sourceRect;
            Texture2D scaledTexture = null;
            Vector4 insertionBorder = border;
            float insertionPixelsPerUnit = pixelsPerUnit;

            int maximumContentSize = _config.pageSize - (_config.padding * 2);
            bool requiresScale = sourceRect.width > maximumContentSize || sourceRect.height > maximumContentSize;
            if (requiresScale && _config.oversizePolicy == DynamicAtlasOversizePolicy.Reject)
            {
                RecordRejection(DynamicAtlasInsertStatus.OversizedSource);
                return DynamicAtlasInsertStatus.OversizedSource;
            }

            EnsureEntryCapacity();
            if (_entries.Count >= _config.maxEntries)
            {
                RecordRejection(DynamicAtlasInsertStatus.EntryCapacityReached);
                return DynamicAtlasInsertStatus.EntryCapacityReached;
            }

            if (requiresScale)
            {
                float scale = Mathf.Min(
                    (float)maximumContentSize / sourceRect.width,
                    (float)maximumContentSize / sourceRect.height);
                int scaledWidth = Mathf.Max(1, Mathf.FloorToInt(sourceRect.width * scale));
                int scaledHeight = Mathf.Max(1, Mathf.FloorToInt(sourceRect.height * scale));
                scaledTexture = CreateScaledRegionTexture(source, sourceRect, scaledWidth, scaledHeight);
                if (scaledTexture == null)
                {
                    RecordRejection(DynamicAtlasInsertStatus.CopyFailed);
                    return DynamicAtlasInsertStatus.CopyFailed;
                }

                insertionSource = scaledTexture;
                insertionRect = new RectInt(0, 0, scaledWidth, scaledHeight);
                insertionBorder *= scale;
                insertionPixelsPerUnit *= scale;
            }

            try
            {
                DynamicAtlasInsertStatus insertStatus = TryInsert(
                    key,
                    insertionSource,
                    insertionRect,
                    pivot,
                    insertionBorder,
                    insertionPixelsPerUnit,
                    out entry);
                if (!IsSuccessful(insertStatus))
                {
                    RecordRejection(insertStatus);
                }

                return insertStatus;
            }
            finally
            {
                if (scaledTexture != null)
                {
                    DestroyUnityObject(scaledTexture);
                }
            }
        }

        private DynamicAtlasInsertStatus TryInsert(
            string key,
            Texture2D source,
            RectInt sourceRect,
            Vector2 pivot,
            Vector4 border,
            float pixelsPerUnit,
            out AtlasEntry entry)
        {
            entry = null;
            bool gpuCandidate = CanUseGpuPage(source);

            while (true)
            {
                DynamicAtlasPageInsertResult existingResult = TryInsertIntoExistingPages(
                        key,
                        source,
                        sourceRect,
                        pivot,
                        border,
                        pixelsPerUnit,
                        gpuCandidate,
                        out entry);
                if (existingResult == DynamicAtlasPageInsertResult.Success)
                {
                    return DynamicAtlasInsertStatus.Success;
                }

                if (existingResult == DynamicAtlasPageInsertResult.CopyFailed)
                {
                    return DynamicAtlasInsertStatus.CopyFailed;
                }

                if (existingResult == DynamicAtlasPageInsertResult.CopyUnsupported)
                {
                    return DynamicAtlasInsertStatus.CopyUnsupported;
                }

                if (existingResult == DynamicAtlasPageInsertResult.NoSpace && TryEvictOldestUnused())
                {
                    continue;
                }

                DynamicAtlasPageMode desiredMode = gpuCandidate
                    ? DynamicAtlasPageMode.GpuOnly
                    : DynamicAtlasPageMode.CpuBacked;

                if (desiredMode == DynamicAtlasPageMode.CpuBacked &&
                    !TextureFormatHelper.AllowsCpuBacking(_config.copyFallback))
                {
                    return DynamicAtlasInsertStatus.CopyUnsupported;
                }

                if (!TryCreatePage(desiredMode, out DynamicAtlasPage page, out DynamicAtlasInsertStatus createStatus))
                {
                    return createStatus;
                }

                DynamicAtlasPageInsertResult insertResult = TryInsertIntoPage(
                        page,
                        key,
                        source,
                        sourceRect,
                        pivot,
                        border,
                        pixelsPerUnit,
                        out entry);
                if (insertResult == DynamicAtlasPageInsertResult.Success)
                {
                    return DynamicAtlasInsertStatus.Success;
                }

                RemovePage(page);

                if (desiredMode == DynamicAtlasPageMode.GpuOnly &&
                    TextureFormatHelper.AllowsCpuBacking(_config.copyFallback))
                {
                    if (!TryCreatePage(DynamicAtlasPageMode.CpuBacked, out page, out createStatus))
                    {
                        return createStatus;
                    }

                    insertResult = TryInsertIntoPage(
                            page,
                            key,
                            source,
                            sourceRect,
                            pivot,
                            border,
                            pixelsPerUnit,
                            out entry);
                    if (insertResult == DynamicAtlasPageInsertResult.Success)
                    {
                        return DynamicAtlasInsertStatus.Success;
                    }

                    RemovePage(page);
                }

                return ToInsertStatus(insertResult);
            }
        }

        private DynamicAtlasPageInsertResult TryInsertIntoExistingPages(
            string key,
            Texture2D source,
            RectInt sourceRect,
            Vector2 pivot,
            Vector4 border,
            float pixelsPerUnit,
            bool preferGpu,
            out AtlasEntry entry)
        {
            entry = null;
            bool sawNoSpace = false;
            bool sawUnsupported = false;

            if (preferGpu)
            {
                for (int i = 0; i < _pages.Count; i++)
                {
                    DynamicAtlasPage page = _pages[i];
                    if (page.Mode != DynamicAtlasPageMode.GpuOnly)
                    {
                        continue;
                    }

                    DynamicAtlasPageInsertResult result = TryInsertIntoPage(
                        page, key, source, sourceRect, pivot, border, pixelsPerUnit, out entry);
                    if (result == DynamicAtlasPageInsertResult.Success ||
                        result == DynamicAtlasPageInsertResult.CopyFailed)
                    {
                        return result;
                    }

                    sawNoSpace |= result == DynamicAtlasPageInsertResult.NoSpace;
                    sawUnsupported |= result == DynamicAtlasPageInsertResult.CopyUnsupported;
                }
            }

            if (TextureFormatHelper.AllowsCpuBacking(_config.copyFallback))
            {
                for (int i = 0; i < _pages.Count; i++)
                {
                    DynamicAtlasPage page = _pages[i];
                    if (page.Mode != DynamicAtlasPageMode.CpuBacked)
                    {
                        continue;
                    }

                    DynamicAtlasPageInsertResult result = TryInsertIntoPage(
                        page, key, source, sourceRect, pivot, border, pixelsPerUnit, out entry);
                    if (result == DynamicAtlasPageInsertResult.Success ||
                        result == DynamicAtlasPageInsertResult.CopyFailed)
                    {
                        return result;
                    }

                    sawNoSpace |= result == DynamicAtlasPageInsertResult.NoSpace;
                    sawUnsupported |= result == DynamicAtlasPageInsertResult.CopyUnsupported;
                }
            }

            return sawNoSpace || !sawUnsupported
                ? DynamicAtlasPageInsertResult.NoSpace
                : DynamicAtlasPageInsertResult.CopyUnsupported;
        }

        private DynamicAtlasPageInsertResult TryInsertIntoPage(
            DynamicAtlasPage page,
            string key,
            Texture2D source,
            RectInt sourceRect,
            Vector2 pivot,
            Vector4 border,
            float pixelsPerUnit,
            out AtlasEntry entry)
        {
            entry = null;
            DynamicAtlasPageInsertResult pageResult = page.TryInsert(
                source,
                sourceRect,
                out DynamicAtlasPlacement placement,
                out DynamicAtlasCopyPath copyPath);
            if (pageResult != DynamicAtlasPageInsertResult.Success)
            {
                return pageResult;
            }

            Sprite sprite = null;
            try
            {
                sprite = Sprite.Create(
                    page.Texture,
                    new Rect(
                        placement.ContentRect.x,
                        placement.ContentRect.y,
                        placement.ContentRect.width,
                        placement.ContentRect.height),
                    pivot,
                    pixelsPerUnit,
                    extrude: 0,
                    SpriteMeshType.FullRect,
                    border);

                if (sprite == null)
                {
                    page.Release(placement);
                    return DynamicAtlasPageInsertResult.CopyFailed;
                }

                sprite.hideFlags = HideFlags.DontSave;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                sprite.name = key;
#endif
                if (_batchDepth == 0)
                {
                    page.FlushPendingUpload();
                }

                entry = RentEntry();
                entry.Key = key;
                entry.Sprite = sprite;
                entry.Page = page;
                entry.Placement = placement;
                entry.CopyPath = copyPath;
                entry.ReferenceCount = 1;
                entry.Generation = NextEntryGeneration();
                entry.LastUseSequence = ++_useSequence;
                _entries.Add(key, entry);
                _insertCount++;
                RecordCopyPath(copyPath);

                return DynamicAtlasPageInsertResult.Success;
            }
            catch (Exception)
            {
                if (sprite != null)
                {
                    DestroyUnityObject(sprite);
                }

                page.Release(placement);
                if (entry != null)
                {
                    ReturnEntry(entry);
                    entry = null;
                }

                return DynamicAtlasPageInsertResult.CopyFailed;
            }
        }

        private bool TryCreatePage(
            DynamicAtlasPageMode mode,
            out DynamicAtlasPage page,
            out DynamicAtlasInsertStatus failureStatus)
        {
            page = null;
            RefreshPendingDestructionBytes();
            if (_pages.Count + _pendingDestructionPageCount >= _config.maxPages)
            {
                failureStatus = DynamicAtlasInsertStatus.PageCapacityReached;
                return false;
            }

            long pageBytes = EstimatePageBytes(mode);
            if (pageBytes > _config.memoryBudgetBytes ||
                _estimatedTextureBytes > _config.memoryBudgetBytes - pageBytes)
            {
                failureStatus = DynamicAtlasInsertStatus.MemoryBudgetReached;
                return false;
            }

            try
            {
                page = new DynamicAtlasPage(
                    _config.pageSize,
                    AtlasTextureFormat,
                    _config.filterMode,
                    _config.padding,
                    _config.enableBleed,
                    _config.maxEntriesPerPage,
                    mode,
                    TextureFormatHelper.AllowsSynchronousReadback(_config.copyFallback));
                _pages.Add(page);
                _estimatedTextureBytes += pageBytes;
                failureStatus = DynamicAtlasInsertStatus.Success;
                return true;
            }
            catch (Exception)
            {
                page?.Dispose();
                page = null;
                failureStatus = DynamicAtlasInsertStatus.CopyFailed;
                return false;
            }
        }

        private bool TryRetainCachedEntry(string key, out AtlasEntry entry)
        {
            if (!_entries.TryGetValue(key, out entry))
            {
                return false;
            }

            if (entry.Sprite == null || entry.Page == null || entry.Page.Texture == null)
            {
                RemoveEntry(entry, countAsEviction: false);
                entry = null;
                return false;
            }

            entry.ReferenceCount++;
            RemoveRetainedLink(entry);
            entry.LastUseSequence = ++_useSequence;
            _cacheHitCount++;
            return true;
        }

        private void ReleaseEntryReference(AtlasEntry entry)
        {
            if (entry.ReferenceCount <= 0)
            {
                return;
            }

            entry.ReferenceCount--;
            entry.LastUseSequence = ++_useSequence;
            if (entry.ReferenceCount != 0)
            {
                return;
            }

            if (_config.retentionPolicy == DynamicAtlasRetentionPolicy.RemoveWhenUnused)
            {
                RemoveEntry(entry, countAsEviction: false);
                return;
            }

            AddRetainedLink(entry);
        }

        private void EnsureEntryCapacity()
        {
            while (_entries.Count >= _config.maxEntries && TryEvictOldestUnused())
            {
            }
        }

        private bool TryPrepareCacheMiss(out DynamicAtlasInsertStatus failureStatus)
        {
            _cacheMissCount++;
            if (_entries.Count >= _config.maxEntries && _oldestRetainedEntry == null)
            {
                failureStatus = DynamicAtlasInsertStatus.EntryCapacityReached;
                return false;
            }

            failureStatus = DynamicAtlasInsertStatus.Success;
            return true;
        }

        private bool TryEvictOldestUnused()
        {
            AtlasEntry oldest = _oldestRetainedEntry;
            if (oldest == null)
            {
                return false;
            }

            RemoveEntry(oldest, countAsEviction: true);
            return true;
        }

        private void RemoveEntry(AtlasEntry entry, bool countAsEviction)
        {
            if (entry == null || entry.Key == null || !_entries.Remove(entry.Key))
            {
                return;
            }

            RemoveRetainedLink(entry);

            DynamicAtlasPage page = entry.Page;
            if (entry.Sprite != null)
            {
                DestroyUnityObject(entry.Sprite);
            }

            page?.Release(entry.Placement);
            ReturnEntry(entry);

            if (countAsEviction)
            {
                _evictionCount++;
            }

            if (page != null && page.IsEmpty && _pages.Count > _config.minRetainedPages)
            {
                RemovePage(page);
            }
        }

        private void RemovePage(DynamicAtlasPage page)
        {
            if (page == null || !_pages.Remove(page))
            {
                return;
            }

            DisposePageAndTrackNativeLifetime(page);
        }

        private void FlushPendingUploads()
        {
            List<Exception> errors = null;
            for (int i = 0; i < _pages.Count; i++)
            {
                try
                {
                    _pages[i].FlushPendingUpload();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>(2);
                    errors.Add(exception);
                }
            }

            if (errors != null)
            {
                throw new AggregateException("One or more dynamic atlas pages failed to upload.", errors);
            }
        }

        private void ClearCore()
        {
            RefreshPendingDestructionBytes();
            foreach (KeyValuePair<string, AtlasEntry> pair in _entries)
            {
                AtlasEntry entry = pair.Value;
                if (entry.Sprite != null)
                {
                    DestroyUnityObject(entry.Sprite);
                }

                ReturnEntry(entry);
            }

            _entries.Clear();
            for (int i = 0; i < _pages.Count; i++)
            {
                DisposePageAndTrackNativeLifetime(_pages[i]);
            }

            _pages.Clear();
            _oldestRetainedEntry = null;
            _newestRetainedEntry = null;
            _estimatedTextureBytes = _pendingDestructionBytes;
            _batchDepth = 0;
            _batchEpoch++;
            if (_batchEpoch == 0)
            {
                _batchEpoch = 1;
            }
        }

        private DynamicAtlasSpriteLease CreateLease(AtlasEntry entry)
        {
            return entry == null
                ? null
                : new DynamicAtlasSpriteLease(this, entry.Key, entry.Generation, entry.Sprite);
        }

        private AtlasEntry RentEntry()
        {
            return _entryPool.Count > 0 ? _entryPool.Pop() : new AtlasEntry();
        }

        private void ReturnEntry(AtlasEntry entry)
        {
            entry.Reset();
            if (_entryPool.Count < MaximumPooledEntries)
            {
                _entryPool.Push(entry);
            }
        }

        private long NextEntryGeneration()
        {
            _nextEntryGeneration++;
            if (_nextEntryGeneration == 0)
            {
                _nextEntryGeneration = 1;
            }

            return _nextEntryGeneration;
        }

        private void AddRetainedLink(AtlasEntry entry)
        {
            if (entry == null || entry.IsRetainedLinked)
            {
                return;
            }

            entry.IsRetainedLinked = true;
            entry.RetainedPrevious = _newestRetainedEntry;
            entry.RetainedNext = null;
            if (_newestRetainedEntry != null)
            {
                _newestRetainedEntry.RetainedNext = entry;
            }
            else
            {
                _oldestRetainedEntry = entry;
            }

            _newestRetainedEntry = entry;
        }

        private void RemoveRetainedLink(AtlasEntry entry)
        {
            if (entry == null || !entry.IsRetainedLinked)
            {
                return;
            }

            AtlasEntry previous = entry.RetainedPrevious;
            AtlasEntry next = entry.RetainedNext;
            if (previous != null)
            {
                previous.RetainedNext = next;
            }
            else
            {
                _oldestRetainedEntry = next;
            }

            if (next != null)
            {
                next.RetainedPrevious = previous;
            }
            else
            {
                _newestRetainedEntry = previous;
            }

            entry.RetainedPrevious = null;
            entry.RetainedNext = null;
            entry.IsRetainedLinked = false;
        }

        private DynamicAtlasInsertStatus ValidateOperationAndKey(string key)
        {
            if (_disposed)
            {
                return DynamicAtlasInsertStatus.Disposed;
            }

            return IsValidKey(key)
                ? DynamicAtlasInsertStatus.Success
                : DynamicAtlasInsertStatus.InvalidKey;
        }

        private bool IsValidKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.Length <= _config.maxKeyLength;
        }

        private bool CanUseGpuPage(Texture2D source)
        {
            return source != null &&
                   source.format == AtlasTextureFormat &&
                   source.graphicsFormat == GraphicsFormatUtility.GetGraphicsFormat(AtlasTextureFormat, true) &&
                   (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
        }

        private long EstimatePageBytes(DynamicAtlasPageMode mode)
        {
            return TextureFormatHelper.EstimatePageBytes(
                _config.pageSize,
                mode == DynamicAtlasPageMode.CpuBacked
                    ? DynamicAtlasCopyFallback.AllowCpuRawCopy
                    : DynamicAtlasCopyFallback.GpuOnly);
        }

        private void DisposePageAndTrackNativeLifetime(DynamicAtlasPage page)
        {
            RefreshPendingDestructionBytes();
            long pageBytes = EstimatePageBytes(page.Mode);
            if (Application.isPlaying)
            {
                _pendingDestructionBytes += pageBytes;
                _pendingDestructionPageCount++;
                _pendingDestructionFrame = Time.frameCount;
            }
            else
            {
                _estimatedTextureBytes -= pageBytes;
                if (_estimatedTextureBytes < 0)
                {
                    _estimatedTextureBytes = 0;
                }
            }

            page.Dispose();
        }

        private void RefreshPendingDestructionBytes()
        {
            if (_pendingDestructionBytes <= 0)
            {
                return;
            }

            if (Application.isPlaying && Time.frameCount == _pendingDestructionFrame)
            {
                return;
            }

            _estimatedTextureBytes -= _pendingDestructionBytes;
            if (_estimatedTextureBytes < 0)
            {
                _estimatedTextureBytes = 0;
            }

            _pendingDestructionBytes = 0;
            _pendingDestructionPageCount = 0;
            _pendingDestructionFrame = -1;
        }

        private void RecordCopyPath(DynamicAtlasCopyPath path)
        {
            switch (path)
            {
                case DynamicAtlasCopyPath.GpuCopy:
                    _gpuCopyCount++;
                    break;
                case DynamicAtlasCopyPath.CpuRawCopy:
                    _cpuRawCopyCount++;
                    break;
                case DynamicAtlasCopyPath.SynchronousGpuReadback:
                    _synchronousReadbackCount++;
                    break;
            }
        }

        private void RecordRejection(DynamicAtlasInsertStatus status)
        {
            if (!IsSuccessful(status))
            {
                _rejectionCount++;
            }
        }

        private static bool IsSuccessful(DynamicAtlasInsertStatus status)
        {
            return status == DynamicAtlasInsertStatus.Success || status == DynamicAtlasInsertStatus.CacheHit;
        }

        private static DynamicAtlasInsertStatus ToInsertStatus(DynamicAtlasPageInsertResult result)
        {
            switch (result)
            {
                case DynamicAtlasPageInsertResult.CopyUnsupported:
                    return DynamicAtlasInsertStatus.CopyUnsupported;
                case DynamicAtlasPageInsertResult.NoSpace:
                    return DynamicAtlasInsertStatus.PageCapacityReached;
                default:
                    return DynamicAtlasInsertStatus.CopyFailed;
            }
        }

        private static RectInt ToPixelRect(Rect rect)
        {
            return new RectInt(
                Mathf.RoundToInt(rect.x),
                Mathf.RoundToInt(rect.y),
                Mathf.RoundToInt(rect.width),
                Mathf.RoundToInt(rect.height));
        }

        private static bool IsValidRegion(Texture2D source, RectInt sourceRect)
        {
            return sourceRect.width > 0 &&
                   sourceRect.height > 0 &&
                   sourceRect.x >= 0 &&
                   sourceRect.y >= 0 &&
                   sourceRect.width <= source.width &&
                   sourceRect.height <= source.height &&
                   sourceRect.x <= source.width - sourceRect.width &&
                   sourceRect.y <= source.height - sourceRect.height;
        }

        private static bool IsValidSpriteMetadata(
            Vector2 pivot,
            Vector4 border,
            RectInt sourceRect,
            float pixelsPerUnit)
        {
            return IsFinite(pivot.x) &&
                   IsFinite(pivot.y) &&
                   pivot.x >= 0f && pivot.x <= 1f &&
                   pivot.y >= 0f && pivot.y <= 1f &&
                   IsFinite(border.x) && border.x >= 0f &&
                   IsFinite(border.y) && border.y >= 0f &&
                   IsFinite(border.z) && border.z >= 0f &&
                   IsFinite(border.w) && border.w >= 0f &&
                   border.x + border.z <= sourceRect.width &&
                   border.y + border.w <= sourceRect.height &&
                   IsFinite(pixelsPerUnit) && pixelsPerUnit > 0f;
        }

        private static bool HasFullRectGeometry(Sprite source, Rect sourceRect)
        {
            Vector2[] vertices = source.vertices;
            ushort[] triangles = source.triangles;
            if (vertices == null || vertices.Length != 4 || triangles == null || triangles.Length != 6)
            {
                return false;
            }

            float pixelsPerUnit = source.pixelsPerUnit;
            if (!IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f)
            {
                return false;
            }

            float left = -source.pivot.x / pixelsPerUnit;
            float right = (sourceRect.width - source.pivot.x) / pixelsPerUnit;
            float bottom = -source.pivot.y / pixelsPerUnit;
            float top = (sourceRect.height - source.pivot.y) / pixelsPerUnit;
            int cornerMask = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 vertex = vertices[i];
                bool isLeft = Mathf.Approximately(vertex.x, left);
                bool isRight = Mathf.Approximately(vertex.x, right);
                bool isBottom = Mathf.Approximately(vertex.y, bottom);
                bool isTop = Mathf.Approximately(vertex.y, top);
                if ((!isLeft && !isRight) || (!isBottom && !isTop))
                {
                    return false;
                }

                int corner = (isRight ? 1 : 0) | (isTop ? 2 : 0);
                int bit = 1 << corner;
                if ((cornerMask & bit) != 0)
                {
                    return false;
                }

                cornerMask |= bit;
            }

            return cornerMask == 0b1111;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Texture2D CreateScaledRegionTexture(
            Texture2D source,
            RectInt sourceRect,
            int width,
            int height)
        {
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D scaled = null;

            try
            {
                temporary = RenderTexture.GetTemporary(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                Vector2 scale = new Vector2(
                    (float)sourceRect.width / source.width,
                    (float)sourceRect.height / source.height);
                Vector2 offset = new Vector2(
                    (float)sourceRect.x / source.width,
                    (float)sourceRect.y / source.height);
                Graphics.Blit(source, temporary, scale, offset);
                RenderTexture.active = temporary;
                scaled = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                scaled.hideFlags = HideFlags.DontSave;
                scaled.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
                scaled.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return scaled;
            }
            catch (Exception)
            {
                if (scaled != null)
                {
                    DestroyUnityObject(scaled);
                }

                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null)
                {
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }
        }

        private void SafeUnload(string location, Texture2D texture)
        {
            try
            {
                _unloadFunc(location, texture);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicAtlasService));
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

#if UNITY_EDITOR
        private static void RegisterEditorService(DynamicAtlasService service)
        {
            ActiveEditorServices.Add(new WeakReference<DynamicAtlasService>(service));
        }

        private static void UnregisterEditorService(DynamicAtlasService service)
        {
            for (int i = ActiveEditorServices.Count - 1; i >= 0; i--)
            {
                if (!ActiveEditorServices[i].TryGetTarget(out DynamicAtlasService target) || target == null || target == service)
                {
                    ActiveEditorServices.RemoveAt(i);
                }
            }
        }
#endif
    }
}
