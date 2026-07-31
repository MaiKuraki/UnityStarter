using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Drives a batch of resource loads through an <see cref="IResourceProvider"/> using a poll-based model that
    /// carries no engine or third-party async type. Call <see cref="Begin"/>, then <see cref="Update"/> once per
    /// frame (or in a loop) until <see cref="IsDone"/>. The runner retains successful handles so the referenced
    /// resources stay resident; call <see cref="ReleaseAll"/> when the caller no longer needs them.
    ///
    /// Supports concurrency throttling, progress reporting, cancellation, and per-reference failure policy. All
    /// buffers are reused across batches, so repeated preloads on a pooled runner do not allocate after warm-up.
    /// </summary>
    public sealed class PreloadRunner
    {
        public const int DefaultMaximumReferenceCount = 4_096;
        public const int DefaultMaximumConcurrentLoadCount = 256;
        public const int DefaultMaximumAssetNodeScanCount = 65_536;
        public const int AbsoluteMaximumReferenceCount = 65_536;
        public const int AbsoluteMaximumConcurrentLoadCount = 4_096;
        public const int AbsoluteMaximumAssetNodeScanCount = 1_048_576;

        private readonly IResourceProvider _provider;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly int _maximumReferenceCount;
        private readonly int _maximumConcurrentLoadCount;
        private readonly int _maximumAssetNodeScanCount;

        private readonly List<ChoreographyResourceReference> _references = new List<ChoreographyResourceReference>(16);
        private readonly HashSet<ChoreographyResourceReference> _referenceSet = new HashSet<ChoreographyResourceReference>();
        private readonly List<IChoreographyResourceHandle> _active = new List<IChoreographyResourceHandle>(16);
        private readonly List<IChoreographyResourceHandle> _completed = new List<IChoreographyResourceHandle>(16);
        private readonly List<ChoreographyResourceReference> _failed = new List<ChoreographyResourceReference>(4);

        private PreloadOptions _options;
        private PreloadStatus _status = PreloadStatus.Idle;
        private int _nextToStart;
        private int _succeededCount;
        private int _failedCount;
        private int _batchTotalCount;
        private int _effectiveMaximumConcurrentLoadCount;
        private int _peakActiveHandleCount;
        private int _peakRetainedHandleCount;
        private float _progress;
        private long _startedLoadCount;
        private long _succeededLoadCount;
        private long _failedLoadCount;
        private long _rejectedReferenceCount;
        private long _releasedHandleCount;
        private long _cancelledBatchCount;

        /// <summary>Raised whenever <see cref="Progress"/> changes during <see cref="Update"/>.</summary>
        public event Action<float> ProgressChanged;

        /// <summary>Raised once when the batch finishes (completed, failed, or cancelled).</summary>
        public event Action<PreloadResult> Completed;

        /// <summary>Creates a runner with the compatibility defaults used before capacity tuning was exposed.</summary>
        public PreloadRunner(IResourceProvider provider)
            : this(
                provider,
                NullChoreographyDiagnostics.Instance,
                DefaultMaximumReferenceCount,
                DefaultMaximumConcurrentLoadCount,
                DefaultMaximumAssetNodeScanCount)
        {
        }

        public PreloadRunner(IResourceProvider provider, IChoreographyDiagnostics diagnostics)
            : this(
                provider,
                diagnostics,
                DefaultMaximumReferenceCount,
                DefaultMaximumConcurrentLoadCount,
                DefaultMaximumAssetNodeScanCount)
        {
        }

        /// <summary>Creates a bounded runner without requiring a diagnostics implementation.</summary>
        public PreloadRunner(
            IResourceProvider provider,
            int maximumReferenceCount,
            int maximumConcurrentLoadCount)
            : this(
                provider,
                NullChoreographyDiagnostics.Instance,
                maximumReferenceCount,
                maximumConcurrentLoadCount,
                DeriveAssetNodeScanCount(maximumReferenceCount))
        {
        }

        /// <summary>Creates a bounded runner with an explicitly supplied diagnostic sink.</summary>
        public PreloadRunner(
            IResourceProvider provider,
            IChoreographyDiagnostics diagnostics,
            int maximumReferenceCount,
            int maximumConcurrentLoadCount)
            : this(
                provider,
                diagnostics,
                maximumReferenceCount,
                maximumConcurrentLoadCount,
                DeriveAssetNodeScanCount(maximumReferenceCount))
        {
        }

        /// <summary>
        /// Creates a bounded runner. The asset-node scan ceiling covers sections, tracks, and clips visited while
        /// collecting references, preventing an asset from causing unbounded traversal before admission.
        /// </summary>
        public PreloadRunner(
            IResourceProvider provider,
            int maximumReferenceCount,
            int maximumConcurrentLoadCount,
            int maximumAssetNodeScanCount)
            : this(
                provider,
                NullChoreographyDiagnostics.Instance,
                maximumReferenceCount,
                maximumConcurrentLoadCount,
                maximumAssetNodeScanCount)
        {
        }

        public PreloadRunner(
            IResourceProvider provider,
            IChoreographyDiagnostics diagnostics,
            int maximumReferenceCount,
            int maximumConcurrentLoadCount,
            int maximumAssetNodeScanCount)
        {
            if (maximumReferenceCount <= 0 || maximumReferenceCount > AbsoluteMaximumReferenceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumReferenceCount));
            }
            if (maximumConcurrentLoadCount <= 0 || maximumConcurrentLoadCount > AbsoluteMaximumConcurrentLoadCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentLoadCount));
            }
            if (maximumAssetNodeScanCount < maximumReferenceCount
                || maximumAssetNodeScanCount > AbsoluteMaximumAssetNodeScanCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAssetNodeScanCount));
            }

            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _maximumReferenceCount = maximumReferenceCount;
            _maximumConcurrentLoadCount = maximumConcurrentLoadCount;
            _maximumAssetNodeScanCount = maximumAssetNodeScanCount;
        }

        public PreloadStatus Status => _status;

        public bool IsDone => _status == PreloadStatus.Completed || _status == PreloadStatus.Failed || _status == PreloadStatus.Cancelled;

        public float Progress => _progress;

        public int TotalCount => _references.Count;

        public ChoreographyPreloadMemoryStats GetMemoryStats()
        {
            return new ChoreographyPreloadMemoryStats(
                _references.Count,
                _maximumReferenceCount,
                _active.Count,
                _completed.Count,
                _failedCount,
                _effectiveMaximumConcurrentLoadCount,
                _peakActiveHandleCount,
                _peakRetainedHandleCount,
                _startedLoadCount,
                _succeededLoadCount,
                _failedLoadCount,
                _rejectedReferenceCount,
                _releasedHandleCount,
                _cancelledBatchCount);
        }

        /// <summary>
        /// Begins loading the supplied references. Any handles retained by a previous batch are released first.
        /// Passing an empty list completes immediately with <see cref="PreloadStatus.Completed"/>.
        /// </summary>
        public void Begin(IReadOnlyList<ChoreographyResourceReference> references, PreloadOptions options)
        {
            TryBegin(references, options);
        }

        /// <summary>Begins a batch and returns false when the distinct-reference ceiling rejects it.</summary>
        public bool TryBegin(IReadOnlyList<ChoreographyResourceReference> references, PreloadOptions options)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            PrepareBegin(options);
            if (references.Count > _maximumAssetNodeScanCount)
            {
                RejectPreparedBatch(references.Count);
                return false;
            }

            for (int i = 0; i < references.Count; i++)
            {
                ChoreographyResourceReference reference = references[i];
                if (!_referenceSet.Contains(reference))
                {
                    if (_references.Count >= _maximumReferenceCount)
                    {
                        RejectPreparedBatch(references.Count);
                        return false;
                    }
                    _referenceSet.Add(reference);
                    _references.Add(reference);
                }
            }

            _batchTotalCount = _references.Count;
            StartPreparedBatch();
            return true;
        }

        /// <summary>
        /// Collects resource references from an asset and begins loading them without requiring a caller-owned
        /// temporary list. References are deduplicated before any backend load is requested.
        /// </summary>
        public void Begin(IChoreographyAsset asset, PreloadOptions options)
        {
            TryBegin(asset, options);
        }

        /// <summary>
        /// Collects and begins an asset batch, failing closed before backend work when either configured ceiling is
        /// exceeded. Assets implementing <see cref="IBoundedChoreographyResourceCollector"/> enforce both limits
        /// during collection; legacy assets are validated after their public collection method returns.
        /// </summary>
        public bool TryBegin(IChoreographyAsset asset, PreloadOptions options)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            PrepareBegin(options);
            if (!TryCollectAssetReferences(asset))
            {
                return false;
            }

            _batchTotalCount = _references.Count;
            StartPreparedBatch();
            return true;
        }

        private void PrepareBegin(PreloadOptions options)
        {
            ReleaseAll();

            _options = options;
            _references.Clear();
            _referenceSet.Clear();
            _failed.Clear();
            _nextToStart = 0;
            _succeededCount = 0;
            _failedCount = 0;
            _batchTotalCount = 0;
            int requestedConcurrent = options.MaxConcurrent > 0 ? options.MaxConcurrent : _maximumConcurrentLoadCount;
            _effectiveMaximumConcurrentLoadCount = Math.Min(requestedConcurrent, _maximumConcurrentLoadCount);
            _progress = 0f;
        }

        private void StartPreparedBatch()
        {
            if (_references.Count == 0)
            {
                _progress = 1f;
                _status = PreloadStatus.Completed;
                RaiseCompleted();
                return;
            }

            _status = PreloadStatus.Loading;
            int startBudget = Math.Min(_effectiveMaximumConcurrentLoadCount, _references.Count);
            StartUpTo(startBudget);
        }

        /// <summary>Polls in-flight handles, advances progress, promotes pending loads, and detects completion.</summary>
        public void Update()
        {
            if (_status != PreloadStatus.Loading)
            {
                return;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                IChoreographyResourceHandle handle = _active[i];
                if (!handle.IsDone)
                {
                    continue;
                }

                _active.RemoveAt(i);
                if (handle.Succeeded)
                {
                    _completed.Add(handle);
                    _succeededCount++;
                    _succeededLoadCount++;
                }
                else
                {
                    _failed.Add(handle.Reference);
                    _failedCount++;
                    _failedLoadCount++;
                    handle.Release();
                    _releasedHandleCount++;
                    if (ChoreographyDiagnosticsGuard.IsEnabled(
                        _diagnostics,
                        ChoreographyDiagnosticLevel.Warning,
                        ChoreographyDiagnosticCategories.Preload))
                    {
                        ChoreographyDiagnosticsGuard.Write(
                            _diagnostics,
                            ChoreographyDiagnosticLevel.Warning,
                            ChoreographyDiagnosticCategories.Preload,
                            "Preload failed for '" + handle.Reference.Address + "': " + (handle.Error ?? "unknown error"));
                    }

                    if (_options.FailurePolicy == PreloadFailurePolicy.Abort)
                    {
                        Abort();
                        return;
                    }
                }
            }

            int startBudget = _effectiveMaximumConcurrentLoadCount - _active.Count;
            if (startBudget > 0)
            {
                StartUpTo(startBudget);
            }

            UpdateProgress();

            if (_active.Count == 0 && _nextToStart >= _references.Count)
            {
                _status = PreloadStatus.Completed;
                _progress = 1f;
                ProgressChanged?.Invoke(_progress);
                RaiseCompleted();
            }
        }

        /// <summary>Cancels an in-flight batch, releasing every in-flight and completed handle.</summary>
        public void Cancel()
        {
            if (_status != PreloadStatus.Loading)
            {
                return;
            }

            ReleaseHandles();
            _status = PreloadStatus.Cancelled;
            _cancelledBatchCount++;
            RaiseCompleted();
        }

        /// <summary>Releases all retained handles from the current/last batch. Safe to call multiple times.</summary>
        public void ReleaseAll()
        {
            ReleaseHandles();
            if (_status == PreloadStatus.Loading)
            {
                _status = PreloadStatus.Cancelled;
            }
        }

        private void StartUpTo(int budget)
        {
            int started = 0;
            while (started < budget && _nextToStart < _references.Count)
            {
                ChoreographyResourceReference reference = _references[_nextToStart];
                _nextToStart++;

                if (!reference.IsValid)
                {
                    FailReference(in reference, "invalid reference");
                    if (_options.FailurePolicy == PreloadFailurePolicy.Abort)
                    {
                        Abort();
                        return;
                    }
                    continue;
                }

                IChoreographyResourceHandle handle = _provider.Load(in reference);
                if (handle == null)
                {
                    FailReference(in reference, "provider returned null handle");
                    if (_options.FailurePolicy == PreloadFailurePolicy.Abort)
                    {
                        Abort();
                        return;
                    }
                    continue;
                }

                _active.Add(handle);
                _startedLoadCount++;
                if (_active.Count > _peakActiveHandleCount)
                {
                    _peakActiveHandleCount = _active.Count;
                }
                int retainedCount = _active.Count + _completed.Count;
                if (retainedCount > _peakRetainedHandleCount)
                {
                    _peakRetainedHandleCount = retainedCount;
                }
                started++;
            }
        }

        private void FailReference(in ChoreographyResourceReference reference, string error)
        {
            _failed.Add(reference);
            _failedCount++;
            _failedLoadCount++;
            if (ChoreographyDiagnosticsGuard.IsEnabled(
                _diagnostics,
                ChoreographyDiagnosticLevel.Warning,
                ChoreographyDiagnosticCategories.Preload))
            {
                ChoreographyDiagnosticsGuard.Write(
                    _diagnostics,
                    ChoreographyDiagnosticLevel.Warning,
                    ChoreographyDiagnosticCategories.Preload,
                    "Preload failed for '" + reference.Address + "': " + error);
            }
        }

        private void UpdateProgress()
        {
            int total = _references.Count;
            if (total == 0)
            {
                return;
            }

            float accumulated = _succeededCount + _failedCount;
            for (int i = 0; i < _active.Count; i++)
            {
                accumulated += _active[i].Progress;
            }

            float next = accumulated / total;
            if (next > 1f)
            {
                next = 1f;
            }

            if (next != _progress)
            {
                _progress = next;
                ProgressChanged?.Invoke(_progress);
            }
        }

        private void Abort()
        {
            ReleaseHandles();
            _status = PreloadStatus.Failed;
            RaiseCompleted();
        }

        private void ReleaseHandles()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].Release();
                _releasedHandleCount++;
            }
            _active.Clear();

            for (int i = 0; i < _completed.Count; i++)
            {
                _completed[i].Release();
                _releasedHandleCount++;
            }
            _completed.Clear();
        }

        private void RaiseCompleted()
        {
            Completed?.Invoke(new PreloadResult(_status, _batchTotalCount, _succeededCount, _failedCount, _failed));
        }

        private void RejectPreparedBatch(int requestedCount)
        {
            _references.Clear();
            _referenceSet.Clear();
            _batchTotalCount = requestedCount;
            _failedCount = requestedCount;
            _rejectedReferenceCount += requestedCount;
            _status = PreloadStatus.Failed;
            _progress = 1f;
            RaiseCompleted();
        }

        private bool TryCollectAssetReferences(IChoreographyAsset asset)
        {
            if (asset is IBoundedChoreographyResourceCollector boundedCollector)
            {
                bool collected = boundedCollector.TryCollectResourceReferences(
                    _references,
                    _maximumReferenceCount,
                    _maximumAssetNodeScanCount,
                    out int addedCount,
                    out int scannedNodeCount);

                if (!collected
                    || addedCount < 0
                    || scannedNodeCount < 0
                    || scannedNodeCount > _maximumAssetNodeScanCount
                    || _references.Count > _maximumReferenceCount)
                {
                    RejectPreparedBatch(GetRejectedRequestCount(_references.Count));
                    return false;
                }

                return TryNormalizeCollectedReferences();
            }

            asset.CollectResourceReferences(_references);
            if (_references.Count > _maximumAssetNodeScanCount)
            {
                RejectPreparedBatch(_references.Count);
                return false;
            }

            return TryNormalizeCollectedReferences();
        }

        private bool TryNormalizeCollectedReferences()
        {
            _referenceSet.Clear();
            int writeIndex = 0;
            int collectedCount = _references.Count;
            for (int readIndex = 0; readIndex < collectedCount; readIndex++)
            {
                ChoreographyResourceReference reference = _references[readIndex];
                if (!reference.IsValid || !_referenceSet.Add(reference))
                {
                    continue;
                }

                if (writeIndex >= _maximumReferenceCount)
                {
                    RejectPreparedBatch(GetRejectedRequestCount(writeIndex));
                    return false;
                }

                _references[writeIndex] = reference;
                writeIndex++;
            }

            if (writeIndex < collectedCount)
            {
                _references.RemoveRange(writeIndex, collectedCount - writeIndex);
            }

            return true;
        }

        private static int GetRejectedRequestCount(int observedCount)
        {
            return observedCount < int.MaxValue ? Math.Max(observedCount + 1, 1) : int.MaxValue;
        }

        private static int DeriveAssetNodeScanCount(int maximumReferenceCount)
        {
            long derived = Math.Max(DefaultMaximumAssetNodeScanCount, (long)maximumReferenceCount * 4L);
            return (int)Math.Min(derived, AbsoluteMaximumAssetNodeScanCount);
        }
    }
}
