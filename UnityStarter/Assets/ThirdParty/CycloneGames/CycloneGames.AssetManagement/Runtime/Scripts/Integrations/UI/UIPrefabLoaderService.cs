using System;
using UnityEngine;

namespace CycloneGames.AssetManagement.Integrations.UI
{
	/// <summary>
	/// Simple UI prefab loader that consults a registry (key -> location) and uses an asset package to load/instantiate.
	/// </summary>
	public sealed class UIPrefabLoaderService : IDisposable
	{
		private readonly IAssetPackage _package;
		private readonly UIPrefabRegistry _registry;
		private IAssetHandle<GameObject> _cachedHandle;

		public UIPrefabLoaderService(IAssetPackage package, UIPrefabRegistry registry)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		}

		public GameObject LoadAndInstantiate(string key, Transform parent = null)
		{
			DisposeCached();
			string location = _registry.GetLocation(key);
			if (string.IsNullOrEmpty(location)) return null;
			_cachedHandle = _package.LoadAssetSync<GameObject>(location);
			return _package.InstantiateSync(_cachedHandle, parent);
		}

		private void DisposeCached()
		{
			if (_cachedHandle != null)
			{
				_cachedHandle.Dispose();
				_cachedHandle = null;
			}
		}

		public void Dispose()
		{
			DisposeCached();
		}
	}
}