using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Integrations.GameplayFramework
{
	/// <summary>
	/// Maps actor type (or id) to prefab location and spawns actors using IAssetPackage.
	/// Keeps a small cache of handles for commonly used actors.
	/// </summary>
	public sealed class ActorPrefabProvider : IDisposable
	{
		private readonly IAssetPackage _package;
		private readonly Func<Type, string> _typeToLocation;
		private readonly Dictionary<Type, IAssetHandle<GameObject>> _handleCache = new Dictionary<Type, IAssetHandle<GameObject>>(16);

		public ActorPrefabProvider(IAssetPackage package, Func<Type, string> typeToLocation)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_typeToLocation = typeToLocation ?? throw new ArgumentNullException(nameof(typeToLocation));
		}

		public GameObject SpawnActor(Type actorType, Vector3 position, Quaternion rotation, Transform parent = null)
		{
			if (actorType == null) return null;
			if (!_handleCache.TryGetValue(actorType, out var handle))
			{
				string location = _typeToLocation(actorType);
				if (string.IsNullOrEmpty(location)) return null;
				handle = _package.LoadAssetSync<GameObject>(location);
				_handleCache[actorType] = handle;
			}
			var go = _package.InstantiateSync(handle, parent, worldPositionStays: false);
			if (go != null)
			{
				go.transform.SetPositionAndRotation(position, rotation);
			}
			return go;
		}

		public T SpawnActor<T>(Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
		{
			var go = SpawnActor(typeof(T), position, rotation, parent);
			return go != null ? go.GetComponent<T>() : null;
		}

		public void Dispose()
		{
			foreach (var kv in _handleCache)
				kv.Value?.Dispose();
			_handleCache.Clear();
		}
	}
}