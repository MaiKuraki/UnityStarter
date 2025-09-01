using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime.Progressing
{
	/// <summary>
	/// Aggregates weighted progress from multiple IOperation providers.
	/// </summary>
	public sealed class ProgressAggregator
	{
		private sealed class Item { public IOperation Op; public float Weight; }
		private readonly List<Item> _items = new List<Item>(8);
		private float _totalWeight;

		public void Clear() { _items.Clear(); _totalWeight = 0f; }
		public void Add(IOperation op, float weight = 1f)
		{
			if (op == null) return;
			if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight)) weight = 1f;
			_items.Add(new Item { Op = op, Weight = weight });
			_totalWeight += weight;
		}

		public float GetProgress()
		{
			if (_items.Count == 0) return 1f;
			if (_totalWeight <= 0f) return 0f;
			float acc = 0f;
			for (int i = 0; i < _items.Count; i++)
			{
				var it = _items[i];
				acc += (it.Op?.Progress ?? 0f) * it.Weight;
			}
			return Math.Min(1f, acc / _totalWeight);
		}
	}
}