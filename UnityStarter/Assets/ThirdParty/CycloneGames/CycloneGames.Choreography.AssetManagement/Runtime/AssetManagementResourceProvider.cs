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

            public void Release()
            {
                _owner.ReleaseEntry(this);
            }
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
