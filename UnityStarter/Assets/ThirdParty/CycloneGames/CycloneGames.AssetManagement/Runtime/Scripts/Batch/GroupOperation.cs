using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.AssetManagement.Runtime.Batch
{
	public sealed class GroupOperation : IGroupOperation
	{
		private sealed class Item
		{
			public readonly IOperation Op;
			public readonly float Weight;
			public Item(IOperation op, float weight) { Op = op; Weight = weight; }
		}

		private readonly List<Item> _items = new List<Item>(8);
		private float _totalWeight;
		private bool _canceled;
		private string _error;
		private readonly UniTaskCompletionSource<object> _tcs = new UniTaskCompletionSource<object>();
		private List<IOperation> _itemsCache;

		public bool IsDone { get; private set; }
		public float Progress { get; private set; }
		public string Error => _error;
		public UniTask Task => _tcs.Task;
		public IReadOnlyList<IOperation> Items
		{
			get
			{
				if (_itemsCache == null)
				{
					_itemsCache = new List<IOperation>(_items.Count);
					for (int i = 0; i < _items.Count; i++) _itemsCache.Add(_items[i].Op);
				}
				return _itemsCache;
			}
		}

		public void Add(IOperation op, float weight = 1f)
		{
			if (op == null) return;
			if (IsDone) throw new InvalidOperationException("GroupOperation already started");
			_weightClamp(ref weight);
			_items.Add(new Item(op, weight));
			_totalWeight += weight;
			_itemsCache = null; // Invalidate cache
		}

		public async UniTask StartAsync(CancellationToken cancellationToken = default)
		{
			if (IsDone) return;
			if (_items.Count == 0) { IsDone = true; Progress = 1f; return; }
			_canceled = false; _error = null; Progress = 0f;
			float accWeight = 0f;
			for (int i = 0; i < _items.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var it = _items[i];
				if (it.Op != null)
				{
					await it.Op.Task.AttachExternalCancellation(cancellationToken);
				}
				if (_canceled) break;
				if (!string.IsNullOrEmpty(it.Op?.Error) && string.IsNullOrEmpty(_error)) _error = it.Op.Error;
				accWeight += it.Weight;
				Progress = Math.Min(1f, _totalWeight <= 0f ? 1f : accWeight / _totalWeight);
			}
			IsDone = true;
			if (!string.IsNullOrEmpty(_error))
			{
				_tcs.TrySetException(new Exception(_error));
			}
			else if (_canceled)
			{
				_tcs.TrySetCanceled();
			}
			else
			{
				_tcs.TrySetResult(null);
			}
		}

		public void Cancel() { _canceled = true; }
		public void WaitForAsyncComplete() { /* group is async-only by design */ }

		private static void _weightClamp(ref float w)
		{
			if (w <= 0f) w = 1f;
			if (float.IsNaN(w) || float.IsInfinity(w)) w = 1f;
		}
	}
}
