using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.AssetManagement.Runtime.Cache
{
	/// <summary>
	/// Thread-safe LRU cache for asset handles with zero-GC operations.
	/// NOTE: Get() loads assets synchronously which must be called from main thread.
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
			private const int MAX_POOL_SIZE = 256;
			private static readonly Stack<Node> _pool = new Stack<Node>(64);
			private static readonly object _lock = new object();

			public static Node Get()
			{
				lock (_lock)
				{
					return _pool.Count > 0 ? _pool.Pop() : new Node();
				}
			}

			public static void Release(Node node)
			{
				node.Key = null;
				node.Handle = null;
				node.Next = null;
				node.Prev = null;
				lock (_lock)
				{
					if (_pool.Count < MAX_POOL_SIZE)
					{
						_pool.Push(node);
					}
				}
			}
		}

		private readonly IAssetPackage _package;
		private readonly int _maxEntries;
		private readonly Dictionary<string, Node> _map;
		private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

		private Node _head;
		private Node _tail;
		private int _disposed;

		public AssetCacheService(IAssetPackage package, int maxEntries = 128)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_maxEntries = Math.Max(1, maxEntries);
			_map = new Dictionary<string, Node>(_maxEntries, StringComparer.Ordinal);
		}

		public T Get<T>(string location) where T : UnityEngine.Object
		{
			if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(location)) return null;

			_rwLock.EnterUpgradeableReadLock();
			try
			{
				if (_map.TryGetValue(location, out var node))
				{
					_rwLock.EnterWriteLock();
					try
					{
						MoveToHead(node);
					}
					finally
					{
						_rwLock.ExitWriteLock();
					}
					return node.Handle.Asset as T;
				}

				// Need to load new asset - requires write lock
				_rwLock.EnterWriteLock();
				try
				{
					// Double-check after acquiring write lock
					if (_map.TryGetValue(location, out node))
					{
						MoveToHead(node);
						return node.Handle.Asset as T;
					}

					var h = _package.LoadAssetSync<T>(location);
					if (h == null) return null;

					node = NodePool.Get();
					node.Key = location;
					node.Handle = h;

					AddToHead(node);
					_map[location] = node;

					EvictIfNeeded();

					return h.Asset;
				}
				finally
				{
					_rwLock.ExitWriteLock();
				}
			}
			finally
			{
				_rwLock.ExitUpgradeableReadLock();
			}
		}

		public bool TryRelease(string location)
		{
			if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(location)) return false;

			_rwLock.EnterWriteLock();
			try
			{
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
			finally
			{
				_rwLock.ExitWriteLock();
			}
		}

		public void Clear()
		{
			_rwLock.EnterWriteLock();
			try
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
			finally
			{
				_rwLock.ExitWriteLock();
			}
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
			if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
			Clear();
			_rwLock.Dispose();
		}
	}
}