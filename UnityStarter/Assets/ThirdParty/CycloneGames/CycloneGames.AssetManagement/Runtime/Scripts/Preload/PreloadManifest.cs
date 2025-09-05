using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Preload
{
	[CreateAssetMenu(menuName = "CycloneGames/AssetManagement/PreloadManifest", fileName = "PreloadManifest")]
	public sealed class PreloadManifest : ScriptableObject
	{
		/// <summary>
		/// Defines a single asset to be preloaded.
		/// </summary>
		[Serializable]
		public sealed class Entry
		{
			/// <summary>
			/// The location (address/key) of the asset to preload.
			/// </summary>
			[UnityEngine.Tooltip("The location (address/key) of the asset to preload.")]
			public string Location;
			/// <summary>
			/// Progress weight for UI aggregation. Larger weight contributes more to overall progress.
			/// Has no effect on load order. Ignored when UseUniformWeights is true.
			/// </summary>
			[UnityEngine.Tooltip("Optional progress weight for this entry. Ignored when 'Use Uniform Weights' is enabled on manifest.")]
			public float Weight = 1f;
		}

		/// <summary>
		/// Treat all entries as equal weight (simpler). When true, individual Entry.Weight are ignored.
		/// </summary>
		[UnityEngine.Tooltip("If enabled, all entries are treated as equal weight (1.0). This is simpler for most use cases.")]
		public bool UseUniformWeights = true;
		public string ManifestName;
		public List<Entry> Assets = new List<Entry>();
	}
}