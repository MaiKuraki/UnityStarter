using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Integrations.UI
{
	[CreateAssetMenu(menuName = "CycloneGames/AssetManagement/UIPrefabRegistry", fileName = "UIPrefabRegistry")]
	public sealed class UIPrefabRegistry : ScriptableObject
	{
		[Serializable]
		public sealed class Entry
		{
			public string Key;
			public string Location;
		}

		[SerializeField]
		private List<Entry> _entries = new List<Entry>();

		public string GetLocation(string key)
		{
			if (string.IsNullOrEmpty(key)) return null;
			for (int i = 0; i < _entries.Count; i++)
			{
				if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
					return _entries[i].Location;
			}
			return null;
		}
	}
}