using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BuildReportTool
{
	public static class PrefabDataGenerator
	{
		public static void CreateForUsedAssetsOnly(PrefabData data, BuildReportTool.BuildInfo buildInfo, bool debugLog = false)
		{
			if (buildInfo == null)
			{
				if (debugLog) Debug.LogError("Can't create MeshData for Used Assets, BuildInfo is null");
				return;
			}
			if (debugLog) Debug.Log("Will create MeshData for Used Assets");

			var prefabDataEntries = data.GetPrefabData();
			prefabDataEntries.Clear();

			AppendPrefabData(data, buildInfo.UsedAssets.All, false, debugLog);
		}

		static void AppendPrefabData(PrefabData data, IList<SizePart> assets, bool overwriteExistingEntries, bool debugLog = false)
		{
			var prefabDataEntries = data.GetPrefabData();

			for (int n = 0; n < assets.Count; ++n)
			{
				if (!assets[n].Name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (prefabDataEntries.ContainsKey(assets[n].Name))
				{
					if (!overwriteExistingEntries)
					{
						continue;
					}
					else
					{
						var newEntry = CreateEntry(assets[n].Name, debugLog);
						prefabDataEntries[assets[n].Name] = newEntry;
					}
				}
				else
				{
					var newEntry = CreateEntry(assets[n].Name, debugLog);
					prefabDataEntries.Add(assets[n].Name, newEntry);
				}
			}
		}

		static PrefabData.Entry CreateEntry(string assetPath, bool debugLog = false)
		{
			var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			if (prefabAsset == null)
			{
				return new PrefabData.Entry();
			}

			var newEntry = new PrefabData.Entry();
			StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(prefabAsset);
			newEntry.StaticEditorFlags = GetIntFlags(flags);

			int childStaticEditorFlags = 0;
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_CONTRIBUTE_GI, prefabAsset.transform);
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_BATCHING_STATIC, prefabAsset.transform);
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_OCCLUDER_STATIC, prefabAsset.transform);
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_OCCLUDEE_STATIC, prefabAsset.transform);
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_REFLECTION_PROBE_STATIC, prefabAsset.transform);
#if !UNITY_2022_2_OR_NEWER
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_NAVIGATION_STATIC, prefabAsset.transform);
			childStaticEditorFlags = UpdateChildStaticEditorFlags(newEntry.StaticEditorFlags, childStaticEditorFlags, PrefabData.FLAG_OFF_MESH_LINK_GENERATION, prefabAsset.transform);
#endif
			newEntry.ChildStaticEditorFlags = childStaticEditorFlags;

			return newEntry;
		}

		static int UpdateChildStaticEditorFlags(int rootLevelStaticEditorFlags, int childStaticEditorFlags, int flagValue, Transform rootTransform)
		{
			if ((rootLevelStaticEditorFlags & flagValue) != 0)
			{
				// root level is already on
				return childStaticEditorFlags;
			}

			// check children of rootTransform to see if flag is turned on for any of them
			var stack = new Stack<Transform>();
			stack.Push(rootTransform);

			while (stack.Count > 0)
			{
				Transform iterator = stack.Pop();

				for (int i = 0; i < iterator.childCount; i++)
				{
					Transform child = iterator.GetChild(i);
					StaticEditorFlags childFlags = GameObjectUtility.GetStaticEditorFlags(child.gameObject);
					int intChildFlags = GetIntFlags(childFlags);
					if ((intChildFlags & flagValue) != 0)
					{
						// this child has the flag
						childStaticEditorFlags |= flagValue;
						return childStaticEditorFlags;
					}
					stack.Push(child);
				}
			}
			return childStaticEditorFlags;
		}

		static int GetIntFlags(StaticEditorFlags flags)
		{
			// Unity might change the value of these flags in a future version, so we explicitly convert it ourselves
			int intFlags = 0;

			if (flags.Has(StaticEditorFlags.ContributeGI))
			{
				intFlags |= PrefabData.FLAG_CONTRIBUTE_GI;
			}
			if (flags.Has(StaticEditorFlags.OccluderStatic))
			{
				intFlags |= PrefabData.FLAG_OCCLUDER_STATIC;
			}
			if (flags.Has(StaticEditorFlags.BatchingStatic))
			{
				intFlags |= PrefabData.FLAG_BATCHING_STATIC;
			}
#if !UNITY_2022_2_OR_NEWER
			if (flags.Has(StaticEditorFlags.NavigationStatic))
			{
				intFlags |= PrefabData.FLAG_NAVIGATION_STATIC;
			}
#endif
			if (flags.Has(StaticEditorFlags.OccludeeStatic))
			{
				intFlags |= PrefabData.FLAG_OCCLUDEE_STATIC;
			}
#if !UNITY_2022_2_OR_NEWER
			if (flags.Has(StaticEditorFlags.OffMeshLinkGeneration))
			{
				intFlags |= PrefabData.FLAG_OFF_MESH_LINK_GENERATION;
			}
#endif
			if (flags.Has(StaticEditorFlags.ReflectionProbeStatic))
			{
				intFlags |= PrefabData.FLAG_REFLECTION_PROBE_STATIC;
			}

			return intFlags;
		}

		static bool Has(this StaticEditorFlags f, StaticEditorFlags flagToCheck)
		{
			return (f & flagToCheck) != 0;
		}
	}
}