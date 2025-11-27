using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
	/// <summary>
	/// Zero-GC LRU cache for asset handles.
	/// Uses internal pooling for nodes to avoid allocations during cache operations.
	/// </summary>
	public sealed class AssetCacheService : IDisposable
	{
		private sealed class Node
		{
			public string Key;
			public IAssetHandle<UnityEngine.Object> Handle;
			public Node Next;
			public Node Prev;
		}

		private static class NodePool
		{
			private static readonly Stack<Node> _pool = new Stack<Node>(128);

			public static Node Get()
			{
				return _pool.Count > 0 ? _pool.Pop() : new Node();
			}

			public static void Release(Node node)
			{
				node.Key = null;
				node.Handle = null;
				node.Next = null;
				node.Prev = null;
				_pool.Push(node);
			}
		}

		private readonly IAssetPackage _package;
		private readonly int _maxEntries;
		private readonly Dictionary<string, Node> _map;

		private Node _head;
		private Node _tail;
		private bool _disposed;

		public AssetCacheService(IAssetPackage package, int maxEntries = 128)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_maxEntries = Math.Max(1, maxEntries);
			_map = new Dictionary<string, Node>(_maxEntries, StringComparer.Ordinal);
		}

		public T Get<T>(string location) where T : UnityEngine.Object
		{
			if (_disposed || string.IsNullOrEmpty(location)) return null;

			if (_map.TryGetValue(location, out var node))
			{
				MoveToHead(node);
				return node.Handle.Asset as T;
			}

			// Load new
			var h = _package.LoadAssetSync<T>(location);
			if (h == null) return null;

			// Create node
			node = NodePool.Get();
			node.Key = location;
			// Safe cast due to covariance (out TAsset)
			node.Handle = h;

			AddToHead(node);
			_map[location] = node;

			EvictIfNeeded();

			return h.Asset as T;
		}

		public bool TryRelease(string location)
		{
			if (_disposed || string.IsNullOrEmpty(location)) return false;
			if (_map.TryGetValue(location, out var node))
			{
				RemoveNode(node);
				_map.Remove(location);
				node.Handle.Dispose();
				NodePool.Release(node);
				return true;
			}
			return false;
		}

		public void Clear()
		{
			var current = _head;
			while (current != null)
			{
				current.Handle.Dispose();
				var next = current.Next;
				NodePool.Release(current);
				current = next;
			}
			_map.Clear();
			_head = null;
			_tail = null;
		}

		private void EvictIfNeeded()
		{
			while (_map.Count > _maxEntries && _tail != null)
			{
				var last = _tail;
				RemoveNode(last);
				_map.Remove(last.Key);
				last.Handle.Dispose();
				NodePool.Release(last);
			}
		}

		private void AddToHead(Node node)
		{
			if (_head == null)
			{
				_head = node;
				_tail = node;
			}
			else
			{
				node.Next = _head;
				_head.Prev = node;
				_head = node;
			}
		}

		private void MoveToHead(Node node)
		{
			if (node == _head) return;

			RemoveNode(node);
			AddToHead(node);
		}

		private void RemoveNode(Node node)
		{
			if (node.Prev != null) node.Prev.Next = node.Next;
			else _head = node.Next;

			if (node.Next != null) node.Next.Prev = node.Prev;
			else _tail = node.Prev;

			node.Next = null;
			node.Prev = null;
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			Clear();
		}
	}
}