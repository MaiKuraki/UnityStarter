using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Cache
{
	/// <summary>
	/// Simple count-based LRU cache for asset handles. Keeps handles alive until evicted or cleared.
	/// Return values are raw assets; cache owns the handle lifecycle.
	/// </summary>
	public sealed class AssetCacheService : IDisposable
	{
		private sealed class Entry
		{
			public readonly IAssetHandle<UnityEngine.Object> Handle;
			public LinkedListNode<string> Node; // for LRU
			public Entry(IAssetHandle<UnityEngine.Object> h) { Handle = h; }
		}

		private readonly IAssetPackage _package;
		private readonly int _maxEntries;
		private readonly Dictionary<string, Entry> _map = new Dictionary<string, Entry>(128, StringComparer.Ordinal);
		private readonly LinkedList<string> _lru = new LinkedList<string>();
		private bool _disposed;

		public AssetCacheService(IAssetPackage package, int maxEntries = 128)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_maxEntries = Math.Max(1, maxEntries);
		}

		public T Get<T>(string location) where T : UnityEngine.Object
		{
			if (_disposed || string.IsNullOrEmpty(location)) return null;
			if (_map.TryGetValue(location, out var e))
			{
				TouchLRU(e, location);
				return e.Handle.Asset as T;
			}
			var h = _package.LoadAssetSync<T>(location) as IAssetHandle<UnityEngine.Object>;
			if (h == null) return null;
			var entry = new Entry(h) { Node = _lru.AddFirst(location) };
			_map[location] = entry;
			EvictIfNeeded();
			return h.Asset as T;
		}

		public bool TryRelease(string location)
		{
			if (_disposed || string.IsNullOrEmpty(location)) return false;
			if (_map.TryGetValue(location, out var e))
			{
				_map.Remove(location);
				if (e.Node != null) _lru.Remove(e.Node);
				e.Handle.Dispose();
				return true;
			}
			return false;
		}

		public void Clear()
		{
			foreach (var kv in _map) kv.Value.Handle.Dispose();
			_map.Clear();
			_lru.Clear();
		}

		private void EvictIfNeeded()
		{
			while (_map.Count > _maxEntries)
			{
				var last = _lru.Last;
				if (last == null) break;
				string key = last.Value;
				_lru.RemoveLast();
				if (_map.TryGetValue(key, out var e))
				{
					_map.Remove(key);
					e.Handle.Dispose();
				}
			}
		}

		private void TouchLRU(Entry e, string location)
		{
			if (e.Node != null) _lru.Remove(e.Node);
			e.Node = _lru.AddFirst(location);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			Clear();
		}
	}
}
