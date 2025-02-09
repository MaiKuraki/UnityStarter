using System.Collections.Generic;
using UnityEngine;

namespace BuildReportTool
{
	[System.Serializable, System.Xml.Serialization.XmlRoot("MeshData")]
	public class MeshData : BuildReportTool.IDataFile
	{
		// ==================================================================================

		/// <summary>
		/// Name of project folder.
		/// Included as part of the filename when saved.
		/// </summary>
		public string ProjectName;

		/// <summary>
		/// Type of build that the project was configured to, at the time that MeshData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public string BuildType;

		/// <summary>
		/// When MeshData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public System.DateTime TimeGot;

		public string GetDefaultFilename()
		{
			return BuildReportTool.Util.GetMeshDataDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		public string GetAccompanyingBuildReportFilename()
		{
			return BuildReportTool.Util.GetBuildInfoDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		/// <summary>
		/// Full path where this MeshData is saved in the local storage.
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
			get { return _meshData.Count > 0; }
		}

		public void Clear()
		{
			_meshData.Clear();
		}

		// ==================================================================================

		public enum DataId
		{
			None,
			MeshFilterCount,
			SkinnedMeshRendererCount,
			SubMeshCount,
			VertexCount,
			TriangleCount,
			AnimationType,
			AnimationClipCount,
		}

		public const string TOOLTIP_TEXT_MESH_FILTER_COUNT = @"<b><color=white>Non-skinned Mesh Count</color></b>

Number of MeshFilter components in the asset.";

		public const string TOOLTIP_TEXT_SKINNED_MESH_RENDERER_COUNT = @"<b><color=white>Skinned Mesh Count</color></b>

Number of SkinnedMeshRenderer components in the asset.";

		public const string TOOLTIP_TEXT_SUB_MESH_COUNT = @"<b><color=white>Sub-Mesh Count</color></b>

Total number of sub-meshes in the asset.";

		public const string TOOLTIP_TEXT_VERTEX_COUNT = @"<b><color=white>Vertex Count</color></b>

Total number of vertices from all meshes in the asset.

This is the number of vertices <b>used in the triangles</b>. Some vertices are re-used, which means the number here will normally be higher than what 3d programs usually display.

For example, a regular cube will have 24 in its vertex count, instead of 8 (4 vertices for each face, 6 faces in total, 4 x 6 = 24).";

		public const string TOOLTIP_TEXT_TRIANGLE_COUNT = @"<b><color=white>Face Count</color></b>

Total number of triangles from all meshes in the asset.

If <b><color=white>Keep Quads</color></b> was turned on in the asset's Import Settings, then this count is a mix of triangles and quads.";

		public const string TOOLTIP_TEXT_ANIMATION_TYPE = @"<b><color=white>Animation Type</color></b>

Whether this asset is set to use Humanoid, Generic, or Legacy type of animation.";

		public const string TOOLTIP_TEXT_ANIMATION_CLIP_COUNT = @"<b><color=white>Animation Clip Count</color></b>

Number of imported Animation Clips in the asset.";

		public static string GetTooltipTextFromId(DataId textureDataId)
		{
			switch (textureDataId)
			{
				case DataId.MeshFilterCount:
					return TOOLTIP_TEXT_MESH_FILTER_COUNT;
				case DataId.SkinnedMeshRendererCount:
					return TOOLTIP_TEXT_SKINNED_MESH_RENDERER_COUNT;
				case DataId.SubMeshCount:
					return TOOLTIP_TEXT_SUB_MESH_COUNT;
				case DataId.VertexCount:
					return TOOLTIP_TEXT_VERTEX_COUNT;
				case DataId.TriangleCount:
					return TOOLTIP_TEXT_TRIANGLE_COUNT;
				case DataId.AnimationType:
					return TOOLTIP_TEXT_ANIMATION_TYPE;
				case DataId.AnimationClipCount:
					return TOOLTIP_TEXT_ANIMATION_CLIP_COUNT;
				default:
					return null;
			}
		}
		// ==================================================================================

		public struct Entry
		{
			/// <summary>
			/// Number of MeshFilter components in the asset.
			/// </summary>
			public int MeshFilterCount;

			/// <summary>
			/// Number of SkinnedMeshRenderer components in the asset.
			/// </summary>
			public int SkinnedMeshRendererCount;

			/// <summary>
			/// Total number of meshes in the asset.
			/// </summary>
			public int SubMeshCount;

			/// <summary>
			/// Number of vertices in the asset.
			/// </summary>
			/// <remarks>
			/// <para>This is the number of vertices <b>used in the triangles</b>.
			/// Some vertices are re-used, which means the number here will
			/// normally be higher than what 3d programs usually display.</para>
			///
			/// <para>For example, a regular cube will have 24 in its vertex count, instead of 8.</para>
			///
			/// <para>4 vertices for each face, 6 faces in total, 4 x 6 = 24</para>
			///
			/// <para>This is the total from all meshes in the asset.</para>
			/// </remarks>
			public int VertexCount;

			/// <summary>
			/// Number of triangles in the asset.
			/// </summary>
			/// <remarks>
			/// This is the total triangles from all meshes in the asset.
			/// </remarks>
			public int TriangleCount;

			public string AnimationType;
			public int AnimationClipCount;

			public string ToDisplayedValue(DataId dataId)
			{
				switch (dataId)
				{
					case DataId.MeshFilterCount:
						return MeshFilterCount.ToString("N0");
					case DataId.SkinnedMeshRendererCount:
						return SkinnedMeshRendererCount.ToString("N0");
					case DataId.SubMeshCount:
						return SubMeshCount.ToString("N0");
					case DataId.VertexCount:
						return VertexCount.ToString("N0");
					case DataId.TriangleCount:
						return TriangleCount.ToString("N0");
					case DataId.AnimationType:
						return AnimationType;
					case DataId.AnimationClipCount:
						return AnimationClipCount.ToString("N0");
					// ------------------------
					default:
						return string.Empty;
				}
			}
		}

		// ==================================================================================

		Dictionary<string, Entry> _meshData = new Dictionary<string, Entry>();

		public List<string> Assets;
		public List<Entry> Data;

		public Dictionary<string, Entry> GetMeshData()
		{
			return _meshData;
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

			Assets.AddRange(_meshData.Keys);

			if (Data != null)
			{
				Data.Clear();
			}
			else
			{
				Data = new List<Entry>();
			}

			Data.AddRange(_meshData.Values);
		}

		public void OnAfterLoad()
		{
			_meshData.Clear();

			//var platformName = TextureData.GetPlatformNameFromBuildType(BuildType);
			var len = Mathf.Min(Assets.Count, Data.Count);
			for (int n = 0; n < len; ++n)
			{
				var entryToModify = Data[n];
				//entryToModify.UpdateShownSettings(platformName);
				Data[n] = entryToModify; // have to assign it back, Entry is a struct

				_meshData.Add(Assets[n], Data[n]);
			}
		}
	}
}