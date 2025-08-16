#if NAVIGATHENA_PRESENT && NAVIGATHENA_YOOASSET
using System;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.AssetManagement.Preload;

namespace CycloneGames.AssetManagement.Integrations.Navigathena
{
	[CreateAssetMenu(menuName = "CycloneGames/AssetManagement/ScenePreloadRegistry", fileName = "ScenePreloadRegistry")]
	public sealed class ScenePreloadRegistry : ScriptableObject
	{
		[Serializable]
		public sealed class Entry
		{
			public string SceneKey; // scene location or name
			public List<PreloadManifest> Manifests = new List<PreloadManifest>();
		}

		[SerializeField]
		private List<Entry> _entries = new List<Entry>();

		public IReadOnlyList<PreloadManifest> GetManifests(string sceneKey)
		{
			if (string.IsNullOrEmpty(sceneKey)) return Array.Empty<PreloadManifest>();
			for (int i = 0; i < _entries.Count; i++)
				if (string.Equals(_entries[i].SceneKey, sceneKey, StringComparison.Ordinal))
					return _entries[i].Manifests;
			return Array.Empty<PreloadManifest>();
		}
	}
}
#endif
