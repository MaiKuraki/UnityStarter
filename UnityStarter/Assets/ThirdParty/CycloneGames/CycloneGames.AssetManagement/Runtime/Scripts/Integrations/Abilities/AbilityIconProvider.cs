using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Integrations.Abilities
{
	/// <summary>
	/// Provides ability icons by id using an asset package. Caches handles for reuse.
	/// </summary>
	public sealed class AbilityIconProvider : IDisposable
	{
		private readonly IAssetPackage _package;
		private readonly Func<string, string> _abilityIdToLocation;
		private readonly Dictionary<string, IAssetHandle<Sprite>> _cache = new Dictionary<string, IAssetHandle<Sprite>>(64);

		public AbilityIconProvider(IAssetPackage package, Func<string, string> abilityIdToLocation)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_abilityIdToLocation = abilityIdToLocation ?? throw new ArgumentNullException(nameof(abilityIdToLocation));
		}

		public Sprite GetIcon(string abilityId)
		{
			if (string.IsNullOrEmpty(abilityId)) return null;
			if (_cache.TryGetValue(abilityId, out var handle)) return handle?.Asset as Sprite;
			string location = _abilityIdToLocation(abilityId);
			if (string.IsNullOrEmpty(location)) return null;
			var h = _package.LoadAssetSync<Sprite>(location);
			_cache[abilityId] = h;
			return h.Asset;
		}

		public void Dispose()
		{
			foreach (var kv in _cache)
				kv.Value?.Dispose();
			_cache.Clear();
		}
	}
}