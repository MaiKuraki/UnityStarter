#if ADDLER_PRESENT
using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Integrations.Addler
{
	/// <summary>
	/// Optional Addler-based key resolver. Register mappings from Addler entries (e.g., GUID, label, group) to asset locations.
	/// Designed to avoid allocations and be thread-safe for read-only after Freeze.
	/// </summary>
	public sealed class AddlerAssetKeyResolver
	{
		private readonly Dictionary<string, string> _keyToLocation;
		private bool _frozen;

		public AddlerAssetKeyResolver(int capacity = 256)
		{
			_keyToLocation = new Dictionary<string, string>(capacity, StringComparer.Ordinal);
		}

		public void Register(string key, string location)
		{
			if (_frozen) throw new InvalidOperationException("Resolver is frozen");
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(location)) return;
			_keyToLocation[key] = location;
		}

		public void Freeze()
		{
			_frozen = true;
		}

		public bool TryResolve(string key, out string location)
		{
			if (string.IsNullOrEmpty(key)) { location = null; return false; }
			return _keyToLocation.TryGetValue(key, out location);
		}
	}
}
#endif


