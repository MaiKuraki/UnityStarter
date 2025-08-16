using System;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace CycloneGames.AssetManagement.Integrations.Factory
{
	/// <summary>
	/// Prefab factory backed by CycloneGames.AssetManagement (YooAsset by default).
	/// Keeps a cached handle to the prefab until disposed for repeated instantiation without extra loads.
	/// </summary>
	public sealed class YooAssetPrefabFactory<T> : IFactory<T>, IDisposable where T : MonoBehaviour
	{
		private readonly IAssetPackage _package;
		private readonly string _location;
		private readonly IUnityObjectSpawner _spawner;
		private IAssetHandle<GameObject> _prefabHandle;
		private bool _disposed;

		public YooAssetPrefabFactory(IAssetPackage package, string location, IUnityObjectSpawner spawner = null)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_location = string.IsNullOrEmpty(location) ? throw new ArgumentNullException(nameof(location)) : location;
			_spawner = spawner ?? new DefaultUnityObjectSpawner();
		}

		public T Create()
		{
			if (_disposed) return null;
			EnsurePrefabLoaded();
			var prefab = _prefabHandle != null ? _prefabHandle.Asset : null;
			if (prefab == null) return null;
			var instance = _spawner.Create(prefab);
			return instance != null ? instance.GetComponent<T>() : null;
		}

		private void EnsurePrefabLoaded()
		{
			if (_prefabHandle != null) return;
			_prefabHandle = _package.LoadAssetSync<GameObject>(_location);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_prefabHandle?.Dispose();
			_prefabHandle = null;
		}
	}
}
