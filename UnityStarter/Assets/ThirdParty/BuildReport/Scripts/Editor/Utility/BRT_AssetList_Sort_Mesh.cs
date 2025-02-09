using System;

namespace BuildReportTool
{
	public static partial class AssetListUtility
	{
		public static void SortAssetList(BuildReportTool.SizePart[] assetList, BuildReportTool.MeshData meshData, MeshData.DataId meshSortType, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			switch (meshSortType)
			{
				case BuildReportTool.MeshData.DataId.MeshFilterCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.MeshFilterCount);
					break;
				case BuildReportTool.MeshData.DataId.SkinnedMeshRendererCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.SkinnedMeshRendererCount);
					break;
				case BuildReportTool.MeshData.DataId.SubMeshCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.SubMeshCount);
					break;
				case BuildReportTool.MeshData.DataId.VertexCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.VertexCount);
					break;
				case BuildReportTool.MeshData.DataId.TriangleCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.TriangleCount);
					break;
				case BuildReportTool.MeshData.DataId.AnimationType:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.AnimationType);
					break;
				case BuildReportTool.MeshData.DataId.AnimationClipCount:
					SortMeshData(assetList, meshData, sortOrder, entry => entry.AnimationClipCount);
					break;
			}
		}

		static void SortMeshData(BuildReportTool.SizePart[] assetList, BuildReportTool.MeshData meshData, BuildReportTool.AssetList.SortOrder sortOrder, Func<BuildReportTool.MeshData.Entry, int> func)
		{
			var meshEntries = meshData.GetMeshData();

			for (int n = 0; n < assetList.Length; ++n)
			{
				int intValue = 0;
				if (meshEntries.ContainsKey(assetList[n].Name))
				{
					intValue = func(meshEntries[assetList[n].Name]);
				}

				assetList[n].SetIntAuxData(intValue);
			}

			SortByInt(assetList, sortOrder);
		}

		static void SortMeshData(BuildReportTool.SizePart[] assetList, BuildReportTool.MeshData meshData, BuildReportTool.AssetList.SortOrder sortOrder, Func<BuildReportTool.MeshData.Entry, string> func)
		{
			var meshEntries = meshData.GetMeshData();

			for (int n = 0; n < assetList.Length; ++n)
			{
				string textData = null;
				if (meshEntries.ContainsKey(assetList[n].Name))
				{
					textData = func(meshEntries[assetList[n].Name]);
				}

				assetList[n].SetTextAuxData(textData);
			}

			SortByText(assetList, sortOrder);
		}
	}
}