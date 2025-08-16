using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.AssetManagement
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

		public bool IsDone { get; private set; }
		public float Progress { get; private set; }
		public string Error => _error;
		public IReadOnlyList<IOperation> Items
		{
			get
			{
				var list = new List<IOperation>(_items.Count);
				for (int i = 0; i < _items.Count; i++) list.Add(_items[i].Op);
				return list;
			}
		}

		public void Add(IOperation op, float weight = 1f)
		{
			if (op == null) return;
			if (IsDone) throw new InvalidOperationException("GroupOperation already started");
			_weightClamp(ref weight);
			_items.Add(new Item(op, weight));
			_totalWeight += weight;
		}

		public async Task StartAsync(CancellationToken cancellationToken = default)
		{
			if (IsDone) return;
			if (_items.Count == 0) { IsDone = true; Progress = 1f; return; }
			_canceled = false; _error = null; Progress = 0f;
			float accWeight = 0f;
			for (int i = 0; i < _items.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var it = _items[i];
				await _waitOpAsync(it.Op, cancellationToken);
				if (_canceled) break;
				if (!string.IsNullOrEmpty(it.Op.Error) && string.IsNullOrEmpty(_error)) _error = it.Op.Error;
				accWeight += it.Weight;
				Progress = Math.Min(1f, _totalWeight <= 0f ? 1f : accWeight / _totalWeight);
			}
			IsDone = true;
		}

		public void Cancel() { _canceled = true; }
		public void WaitForAsyncComplete() { /* group is async-only by design */ }

		private static async Task _waitOpAsync(IOperation op, CancellationToken ct)
		{
			if (op == null) return;
			while (!op.IsDone)
			{
				ct.ThrowIfCancellationRequested();
				await YieldUtil.Next(ct);
			}
		}

		private static void _weightClamp(ref float w)
		{
			if (w <= 0f) w = 1f;
			if (float.IsNaN(w) || float.IsInfinity(w)) w = 1f;
		}
	}
}