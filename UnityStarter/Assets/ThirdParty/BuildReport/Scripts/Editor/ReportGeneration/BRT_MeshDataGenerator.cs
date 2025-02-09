
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BuildReportTool
{
	public static class MeshDataGenerator
	{
		public static void CreateForUsedAssetsOnly(MeshData data, BuildReportTool.BuildInfo buildInfo, bool debugLog = false)
		{
			if (buildInfo == null)
			{
				if (debugLog) Debug.LogError("Can't create MeshData for Used Assets, BuildInfo is null");
				return;
			}
			if (debugLog) Debug.Log("Will create MeshData for Used Assets");

			var meshDataEntries = data.GetMeshData();
			meshDataEntries.Clear();

			AppendMeshData(data, buildInfo.UsedAssets.All, false, debugLog);
		}

		public static void CreateForAllAssets(MeshData data, BuildReportTool.BuildInfo buildInfo, bool debugLog = false)
		{
			if (buildInfo == null)
			{
				if (debugLog) Debug.LogError("Can't create MeshData for Used & Unused Assets, BuildInfo is null");
				return;
			}
			if (debugLog) Debug.Log("Will create MeshData for Used & Unused Assets");

			var meshDataEntries = data.GetMeshData();
			meshDataEntries.Clear();

			AppendMeshData(data, buildInfo.UsedAssets.All, false, debugLog);
			AppendMeshData(data, buildInfo.UnusedAssets.All, false, debugLog);
		}

		static void AppendMeshData(MeshData data, IList<SizePart> assets, bool overwriteExistingEntries, bool debugLog = false)
		{
			if (debugLog) Debug.LogFormat("Creating Mesh Data for {0} assets", assets.Count.ToString());

			var meshDataEntries = data.GetMeshData();

			for (int n = 0; n < assets.Count; ++n)
			{
				if (!Util.IsMeshFile(assets[n].Name))
				{
					// this asset is not a mesh, skip it
					continue;
				}

				if (meshDataEntries.ContainsKey(assets[n].Name))
				{
					if (!overwriteExistingEntries)
					{
						continue;
					}
					else
					{
						var newEntry = CreateEntry(assets[n].Name, debugLog);
						meshDataEntries[assets[n].Name] = newEntry;
					}
				}
				else
				{
					var newEntry = CreateEntry(assets[n].Name, debugLog);
					meshDataEntries.Add(assets[n].Name, newEntry);
				}
			}
		}

		static readonly List<MeshFilter> MeshFilters = new List<MeshFilter>();
		static readonly List<SkinnedMeshRenderer> SkinnedMeshRenderers = new List<SkinnedMeshRenderer>();
#if UNITY_5_5_OR_NEWER
		static readonly List<int> TriangleBuffer = new List<int>();
#endif

		static MeshData.Entry CreateEntry(string assetPath, bool debugLog = false)
		{
			var assetImporter = AssetImporter.GetAtPath(assetPath);
			if (assetImporter == null)
			{
				if (debugLog) Debug.LogErrorFormat("AssetImporter.GetAtPath returned null for {0}", assetPath);
				return new MeshData.Entry();
			}

			var modelImporter = assetImporter as ModelImporter;
			if (modelImporter == null)
			{
				if (debugLog) Debug.LogErrorFormat("AssetImporter is not a ModelImporter for {0}", assetPath);
				return new MeshData.Entry();
			}

			// -----------------------------------------------------------------------

			if (debugLog) Debug.LogFormat("Inspecting Model: {0}", assetPath);

			var result = new MeshData.Entry();

			result.AnimationType = modelImporter.animationType.ToString();
			if (modelImporter.clipAnimations != null && modelImporter.clipAnimations.Length > 0)
			{
				result.AnimationClipCount = modelImporter.clipAnimations.Length;
			}
			else
			{
				result.AnimationClipCount = modelImporter.defaultClipAnimations.Length;
			}

			int totalSubMeshCount = 0;
			int totalVertexCount = 0;
			int totalTriangleCount = 0;
			result.MeshFilterCount = 0;
			result.SkinnedMeshRendererCount = 0;
			var loadedModel = AssetDatabase.LoadAssetAtPath(assetPath, typeof(GameObject)) as GameObject;
			if (loadedModel != null)
			{
				loadedModel.GetComponentsInChildren(true, MeshFilters);
				result.MeshFilterCount = MeshFilters.Count;
				for (int n = 0; n < MeshFilters.Count; ++n)
				{
					int subMeshCount = MeshFilters[n].sharedMesh.subMeshCount;
					totalSubMeshCount += subMeshCount;
					totalVertexCount += MeshFilters[n].sharedMesh.vertexCount;

					for (int m = 0; m < subMeshCount; ++m)
					{
#if UNITY_2017_3_OR_NEWER
						MeshFilters[n].sharedMesh.GetTriangles(TriangleBuffer, m, false);
						totalTriangleCount += TriangleBuffer.Count/3;
#elif UNITY_5_5_OR_NEWER
						MeshFilters[n].sharedMesh.GetTriangles(TriangleBuffer, m);
						totalTriangleCount += TriangleBuffer.Count/3;
#else
						var triangles = MeshFilters[n].sharedMesh.GetTriangles(m);
						totalTriangleCount += triangles.Length/3;
#endif
					}
				}

				loadedModel.GetComponentsInChildren(true, SkinnedMeshRenderers);
				result.SkinnedMeshRendererCount = SkinnedMeshRenderers.Count;
				for (int n = 0; n < SkinnedMeshRenderers.Count; ++n)
				{
					int subMeshCount = SkinnedMeshRenderers[n].sharedMesh.subMeshCount;
					totalSubMeshCount += subMeshCount;
					totalVertexCount += SkinnedMeshRenderers[n].sharedMesh.vertexCount;

					for (int m = 0; m < subMeshCount; ++m)
					{
#if UNITY_2017_3_OR_NEWER
						SkinnedMeshRenderers[n].sharedMesh.GetTriangles(TriangleBuffer, m, false);
						totalTriangleCount += TriangleBuffer.Count/3;
#elif UNITY_5_5_OR_NEWER
						SkinnedMeshRenderers[n].sharedMesh.GetTriangles(TriangleBuffer, m);
						totalTriangleCount += TriangleBuffer.Count/3;
#else
						var triangles = SkinnedMeshRenderers[n].sharedMesh.GetTriangles(m);
						totalTriangleCount += triangles.Length/3;
#endif
					}
				}
			}

			result.SubMeshCount = totalSubMeshCount;
			result.VertexCount = totalVertexCount;
			result.TriangleCount = totalTriangleCount;

			return result;
		}
	}
}