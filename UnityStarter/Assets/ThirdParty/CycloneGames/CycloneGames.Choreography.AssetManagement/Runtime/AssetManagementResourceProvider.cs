using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography.AssetManagement
{
    /// <summary>
    /// Optional bridge from Choreography's engine-agnostic resource contract to CycloneGames.AssetManagement.
    /// It loads Unity Object resources by <see cref="ChoreographyResourceReference.Address"/> and keeps a retained
    /// handle per distinct reference until the choreography preload owner releases it.
    /// </summary>
    public sealed class AssetManagementResourceProvider : IResourceProvider, IUnityChoreographyResourceResolver
    {
        public const int AbsoluteMaximumRetainedRequestCount = 65_536;

        private sealed class ResourceEntry : IChoreographyResourceHandle
        {
            private readonly AssetManagementResourceProvider _owner;

            public ChoreographyResourceReference Reference { get; }

            public IAssetHandle<UnityEngine.Object> Handle;
            public int RefCount;
            public bool Released;
            public bool CompletionObserved;
            public bool PendingCounted;

            public ResourceEntry(AssetManagementResourceProvider owner, in ChoreographyResourceReference reference)
            {
                _owner = owner;
                Reference = reference;
            }

            public bool IsDone
            {
                get
                {
                    bool done = Handle != null && Handle.IsDone;
                    if (done)
                    {
                        _owner.ObserveCompletion(this);
                    }
                    return done;
                }
            }

            public bool Succeeded
            {
                get
                {
                    bool succeeded = Handle != null && Handle.IsDone && string.IsNullOrEmpty(Handle.Error) && Handle.Asset != null;
                    if (Handle != null && Handle.IsDone)
                    {
                        _owner.ObserveCompletion(this);
                    }
                    return succeeded;
                }
            }

            public float Progress => Handle != null ? Handle.Progress : 0f;

            public string Error => Handle != null ? Handle.Error : null;

            public void Release()
            {
                _owner.ReleaseEntry(this);
            }
        }

        /// <summary>
        /// Terminal failed handle returned when a request is rejected at the retained-request ceiling or the
        /// backend returns a null handle. It carries no lease or backend handle, so Release is a safe no-op.
        /// </summary>
        private sealed class FailedResourceHandle : IChoreographyResourceHandle
        {
            public ChoreographyResourceReference Reference { get; }

            public bool IsDone => true;

            public bool Succeeded => false;

            public float Progress => 0f;

            public string Error { get; }

            public FailedResourceHandle(in ChoreographyResourceReference reference, string error)
            {
                Reference = reference;
                Error = error;
            }

            public void Release()
            {
                // Nothing to release: rejected or backend-null requests never acquired a backend handle or a lease count.
            }
        }

        private readonly IAssetPackage _package;
        private readonly string _owner;
        private readonly int _maximumRetainedRequestCount;
        private readonly Dictionary<ChoreographyResourceReference, ResourceEntry> _entries =
            new Dictionary<ChoreographyResourceReference, ResourceEntry>();
        private int _activeLeaseCount;
        private int _pendingRequestCount;
        private int _peakRetainedRequestCount;
        private int _peakActiveLeaseCount;
        private int _peakPendingRequestCount;
        private long _loadRequestCount;
        private long _backendRequestCount;
        private long _reusedLeaseCount;
        private long _failedRequestCount;
        private long _rejectedRequestCount;
        private long _releasedRequestCount;

        /// <summary>Creates a provider with the compatibility default retained-request ceiling.</summary>
        public AssetManagementResourceProvider(IAssetPackage package, string owner = "Choreography")
            : this(package, owner, 4_096)
        {
        }

        /// <summary>Creates a provider with an explicit retained-request ceiling.</summary>
        public AssetManagementResourceProvider(
            IAssetPackage package,
            string owner,
            int maximumRetainedRequestCount)
        {
            if (maximumRetainedRequestCount <= 0 || maximumRetainedRequestCount > AbsoluteMaximumRetainedRequestCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetainedRequestCount));
            }

            _package = package ?? throw new ArgumentNullException(nameof(package));
            _owner = owner;
            _maximumRetainedRequestCount = maximumRetainedRequestCount;
        }

        public ChoreographyAssetManagementMemoryStats GetMemoryStats()
        {
            RefreshCompletedRequests();
            return new ChoreographyAssetManagementMemoryStats(
                _entries.Count,
                _maximumRetainedRequestCount,
                _activeLeaseCount,
                _pendingRequestCount,
                _peakRetainedRequestCount,
                _peakActiveLeaseCount,
                _peakPendingRequestCount,
                _loadRequestCount,
                _backendRequestCount,
                _reusedLeaseCount,
                _failedRequestCount,
                _rejectedRequestCount,
                _releasedRequestCount);
        }

        public IChoreographyResourceHandle Load(in ChoreographyResourceReference reference)
        {
            _loadRequestCount++;
            if (_entries.TryGetValue(reference, out ResourceEntry entry))
            {
                entry.RefCount++;
                _activeLeaseCount++;
                _reusedLeaseCount++;
                UpdateLeasePeak();
                return entry;
            }

            if (_entries.Count >= _maximumRetainedRequestCount)
            {
                _rejectedRequestCount++;
                return new FailedResourceHandle(
                    in reference,
                    $"The retained request ceiling of {_maximumRetainedRequestCount} has been reached; the request was rejected.");
            }

            entry = new ResourceEntry(this, reference)
            {
                RefCount = 1,
                PendingCounted = true
            };
            _pendingRequestCount++;
            if (_pendingRequestCount > _peakPendingRequestCount)
            {
                _peakPendingRequestCount = _pendingRequestCount;
            }
            try
            {
                _backendRequestCount++;
                entry.Handle = _package.LoadAssetAsync<UnityEngine.Object>(reference.Address, bucket: reference.Tag, owner: _owner);
            }
            catch
            {
                EndPending(entry);
                entry.CompletionObserved = true;
                _failedRequestCount++;
                throw;
            }

            if (entry.Handle == null)
            {
                EndPending(entry);
                entry.CompletionObserved = true;
                _failedRequestCount++;
                return new FailedResourceHandle(
                    in reference,
                    "The asset package returned a null backend handle for the requested reference.");
            }
            _entries[reference] = entry;
            _activeLeaseCount++;
            if (_entries.Count > _peakRetainedRequestCount)
            {
                _peakRetainedRequestCount = _entries.Count;
            }
            UpdateLeasePeak();

            if (entry.Handle.IsDone)
            {
                ObserveCompletion(entry);
            }
            return entry;
        }

        public bool TryGet(in ChoreographyResourceReference reference, out IChoreographyResourceHandle handle)
        {
            if (_entries.TryGetValue(reference, out ResourceEntry entry))
            {
                handle = entry;
                return true;
            }

            handle = null;
            return false;
        }

        public void Release(in ChoreographyResourceReference reference)
        {
            if (_entries.TryGetValue(reference, out ResourceEntry entry))
            {
                ReleaseEntry(entry);
            }
        }

        public bool TryGetAsset<TAsset>(in ChoreographyResourceReference reference, out TAsset asset) where TAsset : UnityEngine.Object
        {
            if (_entries.TryGetValue(reference, out ResourceEntry entry) && entry.Succeeded && entry.Handle.Asset is TAsset typed)
            {
                asset = typed;
                return true;
            }

            asset = null;
            return false;
        }

        public void ReleaseAll()
        {
            foreach (KeyValuePair<ChoreographyResourceReference, ResourceEntry> pair in _entries)
            {
                ResourceEntry entry = pair.Value;
                EndPending(entry);
                entry.CompletionObserved = true;
                entry.Handle?.Dispose();
                entry.Handle = null;
                entry.RefCount = 0;
                entry.Released = true;
                _releasedRequestCount++;
            }

            _entries.Clear();
            _activeLeaseCount = 0;
        }

        private void ReleaseEntry(ResourceEntry entry)
        {
            if (entry.Released || entry.RefCount <= 0)
            {
                return;
            }

            entry.RefCount--;
            _activeLeaseCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            EndPending(entry);
            entry.CompletionObserved = true;
            entry.Handle?.Dispose();
            entry.Handle = null;
            entry.Released = true;
            _entries.Remove(entry.Reference);
            _releasedRequestCount++;
        }

        private void ObserveCompletion(ResourceEntry entry)
        {
            if (entry.CompletionObserved)
            {
                return;
            }

            entry.CompletionObserved = true;
            EndPending(entry);
            if (entry.Handle == null || !string.IsNullOrEmpty(entry.Handle.Error) || entry.Handle.Asset == null)
            {
                _failedRequestCount++;
            }
        }

        private void RefreshCompletedRequests()
        {
            foreach (KeyValuePair<ChoreographyResourceReference, ResourceEntry> pair in _entries)
            {
                ResourceEntry entry = pair.Value;
                if (!entry.CompletionObserved && entry.Handle != null && entry.Handle.IsDone)
                {
                    ObserveCompletion(entry);
                }
            }
        }

        private void EndPending(ResourceEntry entry)
        {
            if (!entry.PendingCounted)
            {
                return;
            }

            entry.PendingCounted = false;
            _pendingRequestCount--;
        }

        private void UpdateLeasePeak()
        {
            if (_activeLeaseCount > _peakActiveLeaseCount)
            {
                _peakActiveLeaseCount = _activeLeaseCount;
            }
        }
    }
}
