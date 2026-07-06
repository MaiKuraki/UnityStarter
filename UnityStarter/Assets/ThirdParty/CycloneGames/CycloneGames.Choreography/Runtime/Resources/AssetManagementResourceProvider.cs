using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Bridges the engine-free <see cref="IResourceProvider"/> contract to a CycloneGames.AssetManagement
    /// <see cref="IAssetPackage"/>. All choreography resources (animation, audio, VFX, external banks) load through
    /// this single path so nothing bypasses the project's asset-management ownership and caching.
    ///
    /// Ownership: each distinct reference maps to one retained <see cref="IAssetHandle{T}"/> with a reference count.
    /// <see cref="Load"/> increments the count and returns a shared handle wrapper; the wrapper's
    /// <see cref="IChoreographyResourceHandle.Release"/> decrements it and disposes the underlying handle at zero.
    /// Assets load as the base <see cref="Object"/> type; adapters downcast via <see cref="TryGetAsset{TAsset}"/>.
    /// </summary>
    public sealed class AssetManagementResourceProvider : IResourceProvider, IUnityChoreographyResourceResolver
    {
        private sealed class ResourceEntry : IChoreographyResourceHandle
        {
            private readonly AssetManagementResourceProvider _owner;

            public ChoreographyResourceReference Reference { get; }

            public IAssetHandle<Object> Handle;
            public int RefCount;

            public ResourceEntry(AssetManagementResourceProvider owner, in ChoreographyResourceReference reference)
            {
                _owner = owner;
                Reference = reference;
            }

            public bool IsDone => Handle != null && Handle.IsDone;

            public bool Succeeded => Handle != null && Handle.IsDone && string.IsNullOrEmpty(Handle.Error) && Handle.Asset != null;

            public float Progress => Handle != null ? Handle.Progress : 0f;

            public string Error => Handle != null ? Handle.Error : null;

            public void Release() => _owner.ReleaseEntry(this);
        }

        private readonly IAssetPackage _package;
        private readonly string _owner;
        private readonly Dictionary<ChoreographyResourceReference, ResourceEntry> _entries =
            new Dictionary<ChoreographyResourceReference, ResourceEntry>();

        public AssetManagementResourceProvider(IAssetPackage package, string owner = "Choreography")
        {
            _package = package;
            _owner = owner;
        }

        public IChoreographyResourceHandle Load(in ChoreographyResourceReference reference)
        {
            if (_entries.TryGetValue(reference, out ResourceEntry entry))
            {
                entry.RefCount++;
                return entry;
            }

            entry = new ResourceEntry(this, reference)
            {
                RefCount = 1
            };
            entry.Handle = _package.LoadAssetAsync<Object>(reference.Address, bucket: reference.Tag, owner: _owner);
            _entries[reference] = entry;
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

        public bool TryGetAsset<TAsset>(in ChoreographyResourceReference reference, out TAsset asset) where TAsset : Object
        {
            if (_entries.TryGetValue(reference, out ResourceEntry entry) && entry.Succeeded && entry.Handle.Asset is TAsset typed)
            {
                asset = typed;
                return true;
            }
            asset = null;
            return false;
        }

        /// <summary>Disposes every retained handle and clears the table. Call on shutdown or scene teardown.</summary>
        public void ReleaseAll()
        {
            foreach (KeyValuePair<ChoreographyResourceReference, ResourceEntry> pair in _entries)
            {
                pair.Value.Handle?.Dispose();
            }
            _entries.Clear();
        }

        private void ReleaseEntry(ResourceEntry entry)
        {
            entry.RefCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            entry.Handle?.Dispose();
            entry.Handle = null;
            _entries.Remove(entry.Reference);
        }
    }
}
