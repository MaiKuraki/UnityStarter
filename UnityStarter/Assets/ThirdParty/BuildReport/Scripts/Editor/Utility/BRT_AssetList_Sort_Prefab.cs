using System;

namespace BuildReportTool
{
	public static partial class AssetListUtility
	{
		public static void SortAssetList(BuildReportTool.SizePart[] assetList, BuildReportTool.PrefabData prefabData, PrefabData.DataId prefabSortType, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			switch (prefabSortType)
			{
				case BuildReportTool.PrefabData.DataId.ContributeGI:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.ContributeGIValue);
					break;
				case BuildReportTool.PrefabData.DataId.BatchingStatic:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.BatchingStaticValue);
					break;
				case BuildReportTool.PrefabData.DataId.ReflectionProbeStatic:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.ReflectionProbeStaticValue);
					break;
				case BuildReportTool.PrefabData.DataId.OccluderStatic:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.OccluderStaticValue);
					break;
				case BuildReportTool.PrefabData.DataId.OccludeeStatic:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.OccludeeStaticValue);
					break;
				case BuildReportTool.PrefabData.DataId.NavigationStatic:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.NavigationStaticValue);
					break;
				case BuildReportTool.PrefabData.DataId.OffMeshLinkGeneration:
					SortPrefabData(assetList, prefabData, sortOrder, entry => entry.OffMeshLinkGenerationValue);
					break;
			}
		}

		static void SortPrefabData(BuildReportTool.SizePart[] assetList, BuildReportTool.PrefabData prefabData, BuildReportTool.AssetList.SortOrder sortOrder, Func<BuildReportTool.PrefabData.Entry, int> func)
		{
			var prefabEntries = prefabData.GetPrefabData();

			for (int n = 0; n < assetList.Length; ++n)
			{
				int intValue = 0;
				if (prefabEntries.ContainsKey(assetList[n].Name))
				{
					intValue = func(prefabEntries[assetList[n].Name]);
				}

				assetList[n].SetIntAuxData(intValue);
			}

			SortByInt(assetList, sortOrder);
		}
	}
}