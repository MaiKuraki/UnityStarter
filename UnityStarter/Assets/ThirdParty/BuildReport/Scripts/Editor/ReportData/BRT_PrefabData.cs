using System.Collections.Generic;
using UnityEngine;

namespace BuildReportTool
{
	[System.Serializable, System.Xml.Serialization.XmlRoot("PrefabData")]
	public class PrefabData : BuildReportTool.IDataFile
	{
		// ==================================================================================

		/// <summary>
		/// Name of project folder.
		/// Included as part of the filename when saved.
		/// </summary>
		public string ProjectName;

		/// <summary>
		/// Type of build that the project was configured to, at the time that PrefabData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public string BuildType;

		/// <summary>
		/// When PrefabData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public System.DateTime TimeGot;

		public string GetDefaultFilename()
		{
			return BuildReportTool.Util.GetPrefabDataDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		public string GetAccompanyingBuildReportFilename()
		{
			return BuildReportTool.Util.GetBuildInfoDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		/// <summary>
		/// Full path where this PrefabData is saved in the local storage.
		/// </summary>
		string _savedPath;

		/// <inheritdoc cref="_savedPath"/>
		public string SavedPath
		{
			get { return _savedPath; }
		}

		public void SetSavedPath(string val)
		{
			_savedPath = val.Replace("\\", "/");
		}

		public bool HasContents
		{
			get { return _prefabData.Count > 0; }
		}

		// ==================================================================================

		public const int FLAG_CONTRIBUTE_GI = 1;
		public const int FLAG_OCCLUDER_STATIC = 1 << 1;
		public const int FLAG_BATCHING_STATIC = 1 << 2;
		public const int FLAG_NAVIGATION_STATIC = 1 << 3;
		public const int FLAG_OCCLUDEE_STATIC = 1 << 4;
		public const int FLAG_OFF_MESH_LINK_GENERATION = 1 << 5;
		public const int FLAG_REFLECTION_PROBE_STATIC = 1 << 6;

		public struct Entry
		{
			/// <summary>
			/// Value from <see cref="StaticEditorFlags"/> of prefab's GameObject.
			/// </summary>
			/// <remarks>
			/// ContributeGI = 1<br/>
			/// OccluderStatic = 2<br/>
			/// BatchingStatic = 4<br/>
			/// NavigationStatic = 8<br/>
			/// OccludeeStatic = 16<br/>
			/// OffMeshLinkGeneration = 32<br/>
			/// ReflectionProbeStatic = 64<br/><br/>
			/// Note: NavigationStatic and OffMeshLinkGeneration has been removed in 2022,
			/// They have since moved to a package and are in components.
			/// </remarks>
			public int StaticEditorFlags;
			public int ChildStaticEditorFlags;

			public bool HasContributeGI => (StaticEditorFlags & FLAG_CONTRIBUTE_GI) != 0;
			public bool HasBatchingStatic => (StaticEditorFlags & FLAG_BATCHING_STATIC) != 0;
			public bool HasReflectionProbeStatic => (StaticEditorFlags & FLAG_REFLECTION_PROBE_STATIC) != 0;
			public bool HasOccluderStatic => (StaticEditorFlags & FLAG_OCCLUDER_STATIC) != 0;
			public bool HasOccludeeStatic => (StaticEditorFlags & FLAG_OCCLUDEE_STATIC) != 0;
			public bool HasNavigationStatic => (StaticEditorFlags & FLAG_NAVIGATION_STATIC) != 0;
			public bool HasOffMeshLinkGeneration => (StaticEditorFlags & FLAG_OFF_MESH_LINK_GENERATION) != 0;

			bool ChildHasContributeGI => (ChildStaticEditorFlags & FLAG_CONTRIBUTE_GI) != 0;
			bool ChildHasBatchingStatic => (ChildStaticEditorFlags & FLAG_BATCHING_STATIC) != 0;
			bool ChildHasReflectionProbeStatic => (ChildStaticEditorFlags & FLAG_REFLECTION_PROBE_STATIC) != 0;
			bool ChildHasOccluderStatic => (ChildStaticEditorFlags & FLAG_OCCLUDER_STATIC) != 0;
			bool ChildHasOccludeeStatic => (ChildStaticEditorFlags & FLAG_OCCLUDEE_STATIC) != 0;
			bool ChildHasNavigationStatic => (ChildStaticEditorFlags & FLAG_NAVIGATION_STATIC) != 0;
			bool ChildHasOffMeshLinkGeneration => (ChildStaticEditorFlags & FLAG_OFF_MESH_LINK_GENERATION) != 0;

			// 2 means it has that flag at the root level, 1 means it has that flag in one of its child GameObjects
			public int ContributeGIValue => HasContributeGI ? 2 : ChildHasContributeGI ? 1 : 0;
			public int BatchingStaticValue => HasBatchingStatic ? 2 : ChildHasBatchingStatic ? 1 : 0;
			public int ReflectionProbeStaticValue => HasReflectionProbeStatic ? 2 : ChildHasReflectionProbeStatic ? 1 : 0;
			public int OccluderStaticValue => HasOccluderStatic ? 2 : ChildHasOccluderStatic ? 1 : 0;
			public int OccludeeStaticValue => HasOccludeeStatic ? 2 : ChildHasOccludeeStatic ? 1 : 0;
			public int NavigationStaticValue => HasNavigationStatic ? 2 : ChildHasNavigationStatic ? 1 : 0;
			public int OffMeshLinkGenerationValue => HasOffMeshLinkGeneration ? 2 : ChildHasOffMeshLinkGeneration ? 1 : 0;

			public string HasValue(DataId prefabDataId)
			{
				switch (prefabDataId)
				{
					case DataId.ContributeGI:
						return HasContributeGI ? "<b>Yes</b>" : ChildHasContributeGI ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.BatchingStatic:
						return HasBatchingStatic ? "<b>Yes</b>" : ChildHasBatchingStatic ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.ReflectionProbeStatic:
						return HasReflectionProbeStatic ? "<b>Yes</b>" : ChildHasReflectionProbeStatic ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.OccluderStatic:
						return HasOccluderStatic ? "<b>Yes</b>" : ChildHasOccluderStatic ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.OccludeeStatic:
						return HasOccludeeStatic ? "<b>Yes</b>" : ChildHasOccludeeStatic ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.NavigationStatic:
						return HasNavigationStatic ? "<b>Yes</b>" : ChildHasNavigationStatic ? "<b>Yes</b> (in child GameObject)" : "No";
					case DataId.OffMeshLinkGeneration:
						return HasOffMeshLinkGeneration ? "<b>Yes</b>" : ChildHasOffMeshLinkGeneration ? "<b>Yes</b> (in child GameObject)" : "No";
					default:
						return string.Empty;
				}
			}
		}

		/// <summary>
		/// Key is asset path.
		/// </summary>
		Dictionary<string, Entry> _prefabData = new Dictionary<string, Entry>();

		public List<string> Assets;
		public List<Entry> Data;

		public Dictionary<string, Entry> GetPrefabData()
		{
			return _prefabData;
		}

		public void Clear()
		{
			_prefabData.Clear();
		}

		// ==================================================================================

		public enum DataId
		{
			None,
			ContributeGI,
			BatchingStatic,
			ReflectionProbeStatic,
			OccluderStatic,
			OccludeeStatic,
			NavigationStatic,
			OffMeshLinkGeneration,
		}

		public static string GetTooltipTextFromId(DataId flag)
		{
			switch (flag)
			{
				case DataId.ContributeGI:
					return "<b><color=white>Contribute Global Illumination</color></b>\n\nWhether the Mesh Renderers inside the GameObject are included in global illumination calculations.";
				case DataId.BatchingStatic:
					return "<b><color=white>Static Batching</color></b>\n\nWhether the GameObject's Mesh is combined with other eligible Meshes, to potentially reduce runtime rendering costs.";
				case DataId.ReflectionProbeStatic:
					return "<b><color=white>Reflection Probe Static</color></b>\n\nWhether the GameObject is included when precomputing data for Reflection Probes whose Type is Baked.";
				case DataId.OccluderStatic:
					return "<b><color=white>Occluder Static</color></b>\n\nWhether the GameObject is marked as a Static Occluder in the occlusion culling system.";
				case DataId.OccludeeStatic:
					return "<b><color=white>Occludee Static</color></b>\n\nWhether the GameObject is marked as a Static Occludee in the occlusion culling system.";
				case DataId.NavigationStatic:
#if UNITY_2022_2_OR_NEWER
					return "<b><color=white>Navigation Static</color></b>\n\nWhether the GameObject is included when precomputing navigation data.\n\n<i>Note: This property is deprecated in Unity 2022.2 and beyond. The precise selection of the objects is now done using NavMeshBuilder.CollectSources() and NavMeshBuildMarkup.</i>";
#else
					return "<b><color=white>Navigation Static</color></b>\n\nWhether the GameObject is included when precomputing navigation data.";
#endif
				case DataId.OffMeshLinkGeneration:
#if UNITY_2022_2_OR_NEWER
					return "<b><color=white>Off-Mesh Link Generation</color></b>\n\nWhether to attempt generation of an Off-Mesh Link that starts from the GameObject when precomputing navigation data.\n\n<i>Note: This property is deprecated in Unity 2022.2 and beyond. You can now use NavMeshBuilder.CollectSources() and NavMeshBuildMarkup to nominate the objects that will generate Off-Mesh Links.</i>";
#else
					return "<b><color=white>Off-Mesh Link Generation</color></b>\n\nWhether to attempt generation of an Off-Mesh Link that starts from the GameObject when precomputing navigation data.";
#endif
				default:
					return null;
			}
		}

		// ==================================================================================

		public void OnBeforeSave()
		{
			if (Assets != null)
			{
				Assets.Clear();
			}
			else
			{
				Assets = new List<string>();
			}

			Assets.AddRange(_prefabData.Keys);

			if (Data != null)
			{
				Data.Clear();
			}
			else
			{
				Data = new List<Entry>();
			}

			Data.AddRange(_prefabData.Values);
		}

		public void OnAfterLoad()
		{
			_prefabData.Clear();

			var len = Mathf.Min(Assets.Count, Data.Count);
			for (int n = 0; n < len; ++n)
			{
				_prefabData.Add(Assets[n], Data[n]);
			}
		}

		// ==================================================================================
	}
}