using System;

namespace BuildReportTool
{
	public static partial class AssetListUtility
	{
		public static void SortAssetList(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortType sortType, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			switch (sortType)
			{
				case BuildReportTool.AssetList.SortType.RawSize:
					SortRawSize(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.ImportedSize:
					SortImportedSize(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.ImportedSizeOrRawSize:
					SortImportedSizeOrRawSize(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.SizeBeforeBuild:
					SortSizeBeforeBuild(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.PercentSize:
					SortPercentSize(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.AssetFullPath:
					SortAssetFullPath(assetList, sortOrder);
					break;
				case BuildReportTool.AssetList.SortType.AssetFilename:
					SortAssetName(assetList, sortOrder);
					break;
			}
		}

		static void SortRawSize(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.UsableSize > entry2.UsableSize) return -1;
					if (entry1.UsableSize < entry2.UsableSize) return 1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameDescending(entry1, entry2);
				});
			}
			else
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.UsableSize > entry2.UsableSize) return 1;
					if (entry1.UsableSize < entry2.UsableSize) return -1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameAscending(entry1, entry2);
				});
			}
		}

		static void SortImportedSizeOrRawSize(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.ImportedSizeOrRawSize > entry2.ImportedSizeOrRawSize) return -1;
					if (entry1.ImportedSizeOrRawSize < entry2.ImportedSizeOrRawSize) return 1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameDescending(entry1, entry2);
				});
			}
			else
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.ImportedSizeOrRawSize > entry2.ImportedSizeOrRawSize) return 1;
					if (entry1.ImportedSizeOrRawSize < entry2.ImportedSizeOrRawSize) return -1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameAscending(entry1, entry2);
				});
			}
		}

		static void SortImportedSize(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.ImportedSizeBytes > entry2.ImportedSizeBytes) return -1;
					if (entry1.ImportedSizeBytes < entry2.ImportedSizeBytes) return 1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameDescending(entry1, entry2);
				});
			}
			else
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.ImportedSizeBytes > entry2.ImportedSizeBytes) return 1;
					if (entry1.ImportedSizeBytes < entry2.ImportedSizeBytes) return -1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameAscending(entry1, entry2);
				});
			}
		}

		static void SortSizeBeforeBuild(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.SizeInAssetsFolderBytes > entry2.SizeInAssetsFolderBytes) return -1;
					if (entry1.SizeInAssetsFolderBytes < entry2.SizeInAssetsFolderBytes) return 1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameDescending(entry1, entry2);
				});
			}
			else
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.SizeInAssetsFolderBytes > entry2.SizeInAssetsFolderBytes) return 1;
					if (entry1.SizeInAssetsFolderBytes < entry2.SizeInAssetsFolderBytes) return -1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetNameAscending(entry1, entry2);
				});
			}
		}

		static void SortPercentSize(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.Percentage > entry2.Percentage) return -1;
					if (entry1.Percentage < entry2.Percentage) return 1;

					// same percent
					// sort by asset name for assets with same percent
					return SortByAssetFullPathDescending(entry1, entry2);
				});
			}
			else
			{
				Array.Sort(assetList, delegate(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
				{
					if (entry1.Percentage > entry2.Percentage) return 1;
					if (entry1.Percentage < entry2.Percentage) return -1;

					// same size
					// sort by asset name for assets with same sizes
					return SortByAssetFullPathAscending(entry1, entry2);
				});
			}
		}

		static void SortAssetFullPath(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, SortByAssetFullPathDescending);
			}
			else
			{
				Array.Sort(assetList, SortByAssetFullPathAscending);
			}
		}

		static int SortByAssetFullPathDescending(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
		{
			int result = string.Compare(entry1.Name, entry2.Name, StringComparison.OrdinalIgnoreCase);

			return result;
		}

		static int SortByAssetFullPathAscending(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
		{
			int result = string.Compare(entry1.Name, entry2.Name, StringComparison.OrdinalIgnoreCase);

			// invert the result
			if (result == 1) return -1;
			if (result == -1) return 1;
			return 0;
		}

		static void SortAssetName(BuildReportTool.SizePart[] assetList, BuildReportTool.AssetList.SortOrder sortOrder)
		{
			if (sortOrder == BuildReportTool.AssetList.SortOrder.Descending)
			{
				Array.Sort(assetList, SortByAssetNameDescending);
			}
			else
			{
				Array.Sort(assetList, SortByAssetNameAscending);
			}
		}

		static int SortByAssetNameDescending(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
		{
			int result = string.Compare(entry1.Name.GetFileNameOnly(), entry2.Name.GetFileNameOnly(),
				StringComparison.OrdinalIgnoreCase);

			return result;
		}

		static int SortByAssetNameAscending(BuildReportTool.SizePart entry1, BuildReportTool.SizePart entry2)
		{
			int result = string.Compare(entry1.Name.GetFileNameOnly(), entry2.Name.GetFileNameOnly(),
				StringComparison.OrdinalIgnoreCase);

			// invert the result
			if (result == 1) return -1;
			if (result == -1) return 1;

			return 0;
		}
	}
}