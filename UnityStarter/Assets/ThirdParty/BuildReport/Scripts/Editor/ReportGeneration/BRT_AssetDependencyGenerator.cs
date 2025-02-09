//#define BRT_ASSET_DEPENDENCY_TEST_COMMANDS

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.U2D;

namespace BuildReportTool
{
	[System.Serializable]
	public static class AssetDependencyGenerator
	{
#if BRT_ASSET_DEPENDENCY_TEST_COMMANDS
		[MenuItem("Window/Utility/BRT - Check Dependencies of Scenes in Build (Direct)")]
		public static void TestCheckAllScenesDirect()
		{
			var assetDependencies = new AssetDependencies();
			CreateFromIncludedScenes(assetDependencies, true, false);
		}

		[MenuItem("Window/Utility/BRT - Check Dependencies of Scenes in Build (Recursive)")]
		public static void TestCheckAllScenesRecursive()
		{
			var assetDependencies = new AssetDependencies();
			CreateFromIncludedScenes(assetDependencies, true, true);
		}

		[MenuItem("Assets/Check Dependencies Test (Direct)")]
		public static void CheckSelectionDependenciesDirect()
		{
			var selectedAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);

			var startingOpenSet = new Queue<string>();
			startingOpenSet.Enqueue(selectedAssetPath);

			var assetDependencies = new AssetDependencies();
			Create(assetDependencies, startingOpenSet, true, false);
		}

		[MenuItem("Assets/Check Dependencies Test (Recursive)")]
		public static void CheckSelectionDependenciesRecursive()
		{
			var selectedAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);

			var startingOpenSet = new Queue<string>();
			startingOpenSet.Enqueue(selectedAssetPath);

			var assetDependencies = new AssetDependencies();
			Create(assetDependencies, startingOpenSet, true, true);
		}

		[MenuItem("Assets/Check SerializedObject")]
		public static void CheckSerializedObject()
		{
			var serializedAsset = new SerializedObject(Selection.activeObject);
			var str = new StringBuilder();

			var propertyIterator = serializedAsset.GetIterator();
			if (propertyIterator.NextVisible(true))
			{
				do
				{
					str.AppendFormat("Property Name: {0}\n", propertyIterator.name);
					str.AppendFormat("Property Path: {0}\n", propertyIterator.propertyPath);
					str.AppendFormat("Depth: {0}\n", propertyIterator.depth.ToString());
					str.AppendFormat("Type: {0}\n", propertyIterator.propertyType);
					str.Append("------------------------\n");
				} while (propertyIterator.NextVisible(true));
			}

			Debug.Log(str.ToString());
		}
#endif

		// ==================================================================================

		static string[] GetDependencies(string pathName, bool recursive = false)
		{
			// note: there's also EditorUtility.CollectDependencies() for UnityEngine.Object
#if UNITY_5_3_OR_NEWER
			return AssetDatabase.GetDependencies(pathName, recursive);
#else
			return AssetDatabase.GetDependencies(new[] {pathName});
#endif
		}

		// ==================================================================================

		public static void CreateFromIncludedScenes(AssetDependencies data, bool debugLog = false,
			bool recursiveGetDependencies = false)
		{
			var startingOpenSet = new Queue<string>();

			// we'll start with the scenes to be included in the build:
			var scenes = EditorBuildSettings.scenes;
			for (int n = 0, len = scenes.Length; n < len; ++n)
			{
				if (scenes[n].enabled && !string.IsNullOrEmpty(scenes[n].path))
				{
					startingOpenSet.Enqueue(scenes[n].path);
				}
			}

			Create(data, startingOpenSet, debugLog, recursiveGetDependencies);
		}

		public static void Create(AssetDependencies data, BuildReportTool.BuildInfo.SceneInBuild[] scenes,
			bool debugLog = false)
		{
			var startingOpenSet = new Queue<string>();

			// we'll start with the scenes to be included in the build:
			for (int n = 0, len = scenes.Length; n < len; ++n)
			{
				if (scenes[n].Enabled && !string.IsNullOrEmpty(scenes[n].Path))
				{
					startingOpenSet.Enqueue(scenes[n].Path);
				}
			}

			Create(data, startingOpenSet, debugLog);
		}

		public static void CreateForUsedAssetsOnly(AssetDependencies data, BuildReportTool.BuildInfo buildInfo,
			bool debugLog = false)
		{
			if (buildInfo == null)
			{
				return;
			}

			var startingOpenSet = new Queue<string>();

			// we'll start with the scenes to be included in the build:
			var scenes = buildInfo.ScenesInBuild;
			if (scenes != null)
			{
				for (int n = 0, len = scenes.Length; n < len; ++n)
				{
					if (scenes[n].Enabled && !string.IsNullOrEmpty(scenes[n].Path))
					{
						startingOpenSet.Enqueue(scenes[n].Path);
					}
				}
			}

			// plus all Resources assets (since they are not referred
			// to in any scenes, we have to add them explicitly)
			if (buildInfo.UsedAssets != null)
			{
				if (buildInfo.UsedAssets.All != null)
				{
					var allUsedAssets = buildInfo.UsedAssets.All;
					for (int n = 0, len = allUsedAssets.Length; n < len; ++n)
					{
						if (!string.IsNullOrEmpty(allUsedAssets[n].Name) &&
						    allUsedAssets[n].Name.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) > -1)
						{
							startingOpenSet.Enqueue(allUsedAssets[n].Name);
						}
					}
				}

				// also include sprite atlases
				int textureFilterIdx = buildInfo.FileFilters.GetFilterIdx("Textures");
				SizePart[] usedTextures;
				if (buildInfo.HasUsedAssets && textureFilterIdx != -1)
				{
					usedTextures = buildInfo.UsedAssets.PerCategory[textureFilterIdx];
				}
				else
				{
					usedTextures = null;
				}

				if (usedTextures != null)
				{
					foreach (var texture in usedTextures)
					{
						if (texture.Name.IsSpriteAtlasFile())
						{
							startingOpenSet.Enqueue(texture.Name);
						}
					}
				}
			}

			Create(data, startingOpenSet, debugLog);
			CalculateScriptDependencies(data, buildInfo);
		}

		public static void CreateForAllAssets(AssetDependencies data, BuildReportTool.BuildInfo buildInfo,
			bool debugLog = false)
		{
			if (buildInfo == null)
			{
				return;
			}

			var startingOpenSet = new Queue<string>();

			if (buildInfo.UsedAssets != null && buildInfo.UsedAssets.All != null)
			{
				var allUsedAssets = buildInfo.UsedAssets.All;
				for (int n = 0, len = allUsedAssets.Length; n < len; ++n)
				{
					if (string.IsNullOrEmpty(allUsedAssets[n].Name))
					{
						continue;
					}

					startingOpenSet.Enqueue(allUsedAssets[n].Name);
				}
			}

			if (buildInfo.UnusedAssets != null && buildInfo.UnusedAssets.All != null)
			{
				var allUnusedAssets = buildInfo.UnusedAssets.All;
				for (int n = 0, len = allUnusedAssets.Length; n < len; ++n)
				{
					if (string.IsNullOrEmpty(allUnusedAssets[n].Name))
					{
						continue;
					}

					startingOpenSet.Enqueue(allUnusedAssets[n].Name);
				}
			}

			Create(data, startingOpenSet, debugLog);
			CalculateScriptDependencies(data, buildInfo);
		}

		// ==================================================================================

		public static bool IsFileTypeBeforeAnother(List<AssetUserFlattened> list, int idx, string fileTypeInPrevious,
			string fileTypeInIdx, out int idxOfPrevious)
		{
			if (idx <= 0)
			{
				idxOfPrevious = -1;
				// idx has to be at least 1
				return false;
			}

			if (idx >= list.Count)
			{
				idxOfPrevious = -1;
				return false;
			}

			if (list[idx].IndentLevel <= 1)
			{
				idxOfPrevious = -1;
				// no previous indent level to look for
				return false;
			}

			if (string.IsNullOrEmpty(list[idx].AssetPath))
			{
				idxOfPrevious = -1;
				return false;
			}

			if (!list[idx].AssetPath.EndsWith(fileTypeInIdx, StringComparison.OrdinalIgnoreCase))
			{
				idxOfPrevious = -1;
				return false;
			}

			// search for the previous indent level
			// that's the asset using this one
			for (int n = idx - 1; n >= 0; --n)
			{
				if (list[n].IndentLevel == list[idx].IndentLevel - 1)
				{
					idxOfPrevious = n;
					return
						list[n].AssetPath.EndsWith(fileTypeInPrevious, StringComparison.OrdinalIgnoreCase);
				}
			}

			idxOfPrevious = -1;
			return false;
		}

		static bool IsInstanceOfFbxUsingMaterial(string fbxAssetPath, string assetUsingFbxAssetPath,
			string materialAssetPath,
			Dictionary<string, DependencyEntry> assetDependencies, bool debugLog = false)
		{
			if (!assetDependencies.ContainsKey(assetUsingFbxAssetPath))
			{
				return false;
			}

			var doesItUseTheMaterial =
				assetDependencies[assetUsingFbxAssetPath].Uses.IndexOf(materialAssetPath) > -1;

			var doesItUseTheFbx =
				assetDependencies[assetUsingFbxAssetPath].Uses.IndexOf(fbxAssetPath) > -1;

			if (assetUsingFbxAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
			{
				// for prefabs, check that they only ever really use the material
				if (doesItUseTheMaterial)
				{
					// yes, even though the material is assigned as the default when the fbx is
					// instantiated at first, it really is being used by the prefab in question,
					// meaning, it has not been overriden.

					if (debugLog)
					{
						Debug.LogFormat("Asset\n{0}\nuses material:\n{1}",
							assetUsingFbxAssetPath, materialAssetPath);
					}

					return true;
				}
			}
			else if (assetUsingFbxAssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
			{
				// the problem we have is that a scene that uses an fbx has no way of differentiating
				// if the fbx is just using its default materials, or have been overriden to use no materials.
				// we can open the scene and look at the instantiated fbx in question.
				//
				// other than opening the scene, our only other clue is that the Build Report has a
				// list of all assets used in the build. if it's not there, then it wasn't used.
				// if it's there, then it's been used, we're just not sure which game object
				// inside the scene is using it.

				if (doesItUseTheMaterial || doesItUseTheFbx)
				{
					// yes, even though the material is assigned as the default when the fbx is
					// instantiated at first,
					// it really is being used by the asset in question

					if (debugLog)
					{
						if (doesItUseTheMaterial)
						{
							Debug.LogFormat("Asset\n{0}\nuses material:\n{1}",
								assetUsingFbxAssetPath, materialAssetPath);
						}
						else if (doesItUseTheFbx)
						{
							Debug.LogFormat(
								"Asset\n{0}\nuses fbx (whose default material has not been overriden):\n{1}",
								assetUsingFbxAssetPath, materialAssetPath);
						}
					}

					return true;
				}
			}
			else
			{
				if (doesItUseTheMaterial || doesItUseTheFbx)
				{
					// yes, even though the material is assigned as the default when the fbx is
					// instantiated at first,
					// it really is being used by the asset in question

					if (debugLog)
					{
						if (doesItUseTheMaterial)
						{
							Debug.LogFormat("Asset\n{0}\nuses material:\n{1}",
								assetUsingFbxAssetPath, materialAssetPath);
						}
						else if (doesItUseTheFbx)
						{
							Debug.LogFormat(
								"Asset\n{0}\nuses fbx (whose default material has not been overriden):\n{1}",
								assetUsingFbxAssetPath, materialAssetPath);
						}
					}

					return true;
				}
			}

			return false;
		}

		static bool AreInstancesOfFbxUsingMaterial(string fbxAssetPath, string materialAssetPath,
			Dictionary<string, DependencyEntry> assetDependencies, bool debugLog = false)
		{
			if (!assetDependencies.ContainsKey(fbxAssetPath))
			{
				return false;
			}

			var assetsThatUseTheFbx = assetDependencies[fbxAssetPath].Users;

			for (int n = 0, len = assetsThatUseTheFbx.Count; n < len; ++n)
			{
				if (debugLog)
				{
					Debug.LogFormat("assetsThatUseTheFbx {0}: {1}", n, assetsThatUseTheFbx[n]);
				}

				if (IsInstanceOfFbxUsingMaterial(fbxAssetPath, assetsThatUseTheFbx[n], materialAssetPath,
					assetDependencies, debugLog))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Get asset that's above the one specified by the idx, above meaning
		/// it is one step higher in Indent Level.
		/// </summary>
		/// <param name="list"></param>
		/// <param name="idx"></param>
		/// <returns></returns>
		static string GetAssetUsageParent(List<AssetUserFlattened> list, int idx)
		{
			if (idx <= 0 || idx >= list.Count)
			{
				// idx has to be at least 1 and up to (count - 1)
				return null;
			}

			for (int n = idx - 1; n >= 0; --n)
			{
				if (list[n].IndentLevel == list[idx].IndentLevel - 1)
				{
					return list[n].AssetPath;
				}
			}

			return null;
		}

		/// <summary>
		/// Does list already have an entry similar to the one supplied?
		/// Similar means same AssetPath, same IndentLevel,
		/// same asset that has higher indent level above it (parent).
		/// </summary>
		/// <param name="list"></param>
		/// <param name="check"></param>
		/// <param name="assetParentOfCheck"></param>
		/// <returns></returns>
		static bool Contains(List<AssetUserFlattened> list, AssetUserFlattened check, string assetParentOfCheck)
		{
			for (int n = list.Count - 1; n >= 0; --n)
			{
				if (list[n].IndentLevel < check.IndentLevel)
				{
					// we've gone past the indent level we need to check for
					break;
				}

				if (list[n].AssetPath.Equals(check.AssetPath, StringComparison.OrdinalIgnoreCase) &&
				    list[n].IndentLevel == check.IndentLevel &&
				    GetAssetUsageParent(list, n) == assetParentOfCheck)
				{
					return true;
				}
			}

			return false;
		}

		// ==================================================================================

		static void Create(AssetDependencies data, Queue<string> openSet, bool debugLog = false,
			bool recursiveGetDependencies = false)
		{
			var assetDependencies = data.GetAssetDependencies();
			assetDependencies.Clear();

			// -------------------------------------------------------------

			// assets that we've finished inspecting
			var closedSet = new HashSet<string>();

			// ====================================================================

			// Populate the dependencies data for each asset

			while (openSet.Count > 0)
			{
				var newAssetToInspect = openSet.Dequeue();

				if (string.IsNullOrEmpty(newAssetToInspect))
				{
					continue;
				}

				closedSet.Add(newAssetToInspect);

				string[] foundDependencies = GetDependencies(newAssetToInspect, recursiveGetDependencies);

				if (foundDependencies.Length <= 0)
				{
					// this asset doesn't use others
					continue;
				}

				StringBuilder stringBuilder = null;
				if (debugLog)
				{
					stringBuilder = new StringBuilder();
					stringBuilder.Append("Assets used by ");
					stringBuilder.Append(newAssetToInspect);
					stringBuilder.Append(" (");
					stringBuilder.Append(foundDependencies.Length.ToString());
					stringBuilder.Append("):\n");
				}

				DependencyEntry assetsUsed;
				if (assetDependencies.ContainsKey(newAssetToInspect))
				{
					assetsUsed = assetDependencies[newAssetToInspect];
				}
				else
				{
					assetsUsed = new DependencyEntry();
					assetDependencies.Add(newAssetToInspect, assetsUsed);
				}

				// -------------------------------------------------------------

				// we're looping through the assets that newAssetToInspect uses
				// so add them to the `DependencyEntry.Uses` list
				for (int n = 0, len = foundDependencies.Length; n < len; ++n)
				{
					if (string.IsNullOrEmpty(foundDependencies[n]))
					{
						continue;
					}

					if (foundDependencies[n].Equals(newAssetToInspect, StringComparison.Ordinal))
					{
						// for some reason, the API reports assets to be using their own selves
						// so skip it when they have the same value
						continue;
					}

					// If newAssetToInspect is a material and foundDependencies[n] is a texture,
					// we need to check if it's really used in the current shader assigned to the material,
					// because when you assign a texture to a material, even when you change
					// the shader, that assigned texture from the previous shader is kept intact.
					// So when you do AssetDatabase.GetDependencies() on that material, it will output
					// even textures that are not for the current shader.
					//
					// In the context of a build report, such textures need to be identified as
					// NOT a dependency, because ultimately inside the build, they didn't get used
					// by that material.
					//
					if (newAssetToInspect.IsMaterialFile() && foundDependencies[n].IsTextureFile())
					{
						bool textureFound = false;
#if UNITY_5_6_OR_NEWER
						var material = AssetDatabase.LoadAssetAtPath<Material>(newAssetToInspect);
#else
						var material = (Material)AssetDatabase.LoadAssetAtPath(newAssetToInspect, typeof(Material));
#endif
						if (material == null)
						{
							// couldn't find material in current project
							continue;
						}
						var shader = material.shader;
						if (shader == null)
						{
							// no shader assigned to material
							continue;
						}
						int shaderPropertyCount = ShaderUtil.GetPropertyCount(shader);
						for (int pIdx = 0; pIdx < shaderPropertyCount; ++pIdx)
						{
							if (ShaderUtil.GetPropertyType(shader, pIdx) != ShaderUtil.ShaderPropertyType.TexEnv)
							{
								// go through texture properties only
								continue;
							}

							var texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, pIdx));
							if (texture == null)
							{
								// no texture currently assigned to this texture property
								continue;
							}

							string textureAssetPath = AssetDatabase.GetAssetPath(texture);
							if (string.IsNullOrEmpty(textureAssetPath))
							{
								// Empty/null path for a loaded texture.
								// This could mean texture was dynamically created.
								// Since there's no project asset for the texture,
								// we can ignore it.
								continue;
							}

							if (textureAssetPath == foundDependencies[n])
							{
								textureFound = true;
								break;
							}
						}

						if (!textureFound)
						{
							// Even though the texture is assigned to the material,
							// the material is currently using a shader that doesn't
							// make use of that texture.
							// So in the context of a build report, this isn't a
							// dependency, and therefore should be skipped.
							continue;
						}
					}

					if (debugLog)
					{
						stringBuilder.Append(n.ToString());
						stringBuilder.Append(". ");
						stringBuilder.Append(foundDependencies[n]);
						stringBuilder.Append("\n");
					}

					// --------------------------------------------------------

					if (!assetsUsed.Uses.Contains(foundDependencies[n]))
					{
						assetsUsed.Uses.Add(foundDependencies[n]);
					}

					// --------------------------------------------------------

					// now record that dependency as being used by `newAssetToInspect`
					DependencyEntry assetsUsedByEdit;
					if (assetDependencies.ContainsKey(foundDependencies[n]))
					{
						assetsUsedByEdit = assetDependencies[foundDependencies[n]];
					}
					else
					{
						assetsUsedByEdit = new DependencyEntry();
						assetDependencies.Add(foundDependencies[n], assetsUsedByEdit);
					}

					if (!assetsUsedByEdit.Users.Contains(newAssetToInspect))
					{
						assetsUsedByEdit.Users.Add(newAssetToInspect);
					}

					// --------------------------------------------------------

					if (closedSet.Contains(foundDependencies[n]))
					{
						// this asset was already searched through. skip it.
						continue;
					}

					if (!openSet.Contains(foundDependencies[n]))
					{
						// add to list of assets to search through
						openSet.Enqueue(foundDependencies[n]);
					}
				}

				if (debugLog)
				{
					Debug.Log(stringBuilder.ToString());
				}
			}

			// ====================================================================
			// Create the Uses List for each scene in the SceneEntries list

			List<AssetUserFlattened> openFlattenedSet = new List<AssetUserFlattened>();

			foreach (var pair in assetDependencies)
			{
				// ====================================================================
				// Create the AssetUserFlattened List
				// This is the recursively calculated users of the asset.
				// Indent Levels signify which asset uses which.

				var assetUsers = pair.Value.Users;

				if (assetUsers.Count == 0)
				{
					continue;
				}

				var usersFlattened = new List<AssetUserFlattened>(assetUsers.Count);
				pair.Value.UsersFlattened = usersFlattened;

				openFlattenedSet.Clear();

				for (int n = 0, len = assetUsers.Count; n < len; ++n)
				{
					AssetUserFlattened newEntry = new AssetUserFlattened(assetUsers[n], 1
#if BRT_ASSET_DEPENDENCY_DEBUG
					, "[initial]"
#endif
					);

					// put them in reverse since always take from the end of the openFlattenedSet
					openFlattenedSet.Insert(0, newEntry);
				}

				var endlessLoopGuard = 0;
				while (openFlattenedSet.Count > 0)
				{
					++endlessLoopGuard;
					if (endlessLoopGuard >= 9999) // that ought to be enough for real-world usage (I hope)
					{
						break;
					}


					var userAsset = openFlattenedSet[openFlattenedSet.Count - 1];
					openFlattenedSet.RemoveAt(openFlattenedSet.Count - 1);


					var assetUsed = GetAssetUsageParent(usersFlattened, usersFlattened.Count - 1);
					if (Contains(usersFlattened, userAsset, assetUsed))
					{
						// `userAsset` is already added previously in this list in same indent level and same parent
						// can't add this because it would be a duplicate entry

						continue;
					}

					usersFlattened.Add(userAsset);

					if (userAsset.AssetPath.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
					{
						// no need to go inside this further because this is
						// already what we're primarily going inside currently
						userAsset.CyclicDependency = true;
#if BRT_ASSET_DEPENDENCY_DEBUG
						userAsset.DebugInfo = "[selected cyclic]";
#endif
						usersFlattened[usersFlattened.Count - 1] = userAsset;
						continue;
					}

					var userAssetIsFbxUsingMaterial = false;
					string materialAssignedAsDefault = null;

					if (usersFlattened.Count >= 2)
					{
						// note: usersFlattened[usersFlattened.Count - 1] is userAsset
						//

						var noNeedToAddUsersOfUser = false;

						if (userAsset.IndentLevel > 1)
						{
							var currentIndentLevel = userAsset.IndentLevel - 1;

							if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
							    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
							{
								Debug.LogFormat(
									"=========================================================\nChecking for cyclic dependencies for {0} at index {1}\nLooking for Indent Level {2}",
									userAsset.AssetPath, (usersFlattened.Count - 1).ToString(), currentIndentLevel);
							}

							// start just before our own position in the list, then go backwards
							for (int traceN = usersFlattened.Count - 2; traceN >= 0; --traceN)
							{
								if (usersFlattened[traceN].IndentLevel > currentIndentLevel)
								{
									// not the indent level we're looking for

									if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
									    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
									{
										Debug.LogFormat("Skipped element {0} since its indent level is: {1} (looking for {2})",
											traceN, usersFlattened[traceN].IndentLevel.ToString(), currentIndentLevel);
									}

									continue;
								}

								// at this point, the current element is above our indent level, meaning it's what the asset uses

								if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
								    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
								{
									Debug.LogFormat("Now at element {0}: {1} at indent level {2}. Checking if same as {3}",
										traceN, usersFlattened[traceN].AssetPath, usersFlattened[traceN].IndentLevel.ToString(),
										userAsset.AssetPath);
								}

								currentIndentLevel = usersFlattened[traceN].IndentLevel - 1;

								if (usersFlattened[traceN].AssetPath
								                          .Equals(userAsset.AssetPath, StringComparison.OrdinalIgnoreCase))
								{
									// this is an asset we've been in earlier.
									// should not add users of this asset, because that includes the
									// one we've already been in, and it would be an endless recursion
									userAsset.CyclicDependency = true;
#if BRT_ASSET_DEPENDENCY_DEBUG
									userAsset.DebugInfo = "[earlier indent cyclic]";
#endif
									usersFlattened[usersFlattened.Count - 1] = userAsset;
									noNeedToAddUsersOfUser = true;


									if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
									    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
									{
										Debug.LogFormat(
											"Element {0} is found to be same so latest entry is marked as cyclic dependency. check is done.",
											traceN);
									}

									break;
								}

								if (usersFlattened[traceN].IndentLevel <= 1)
								{
									// went through the last asset in this usage chain, no need to look further

									if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
									    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
									{
										Debug.LogFormat("Element {0} has indent level of {1} so aborting the check",
											traceN, usersFlattened[traceN].IndentLevel.ToString());
									}

									break;
								}
							}

							if (debugLog && pair.Key.Contains("Runtime-Only DLL/TextMeshPro.dll") &&
							    userAsset.AssetPath.Contains("AbilityEntry Settings.asset"))
							{
								Debug.LogFormat(
									"=========================================================\nDone checking for cyclic dependencies for {0} at index {1}",
									userAsset.AssetPath, (usersFlattened.Count - 1).ToString());
							}
						}

						if (!noNeedToAddUsersOfUser)
						{
							for (int n = usersFlattened.Count - 2; n >= 0; --n)
							{
								if (usersFlattened[n].AssetPath == userAsset.AssetPath)
								{
									// users of this user has already been shown in earlier entries
									// although from a different usage chain

#if BRT_ASSET_DEPENDENCY_DEBUG
									userAsset.DebugInfo += " [already visited]";
#endif
									// update the entry
									usersFlattened[usersFlattened.Count - 1] = userAsset;

									noNeedToAddUsersOfUser = true;

									break;
								}
							}
						}

						int idxOfPrevious;
						if (!noNeedToAddUsersOfUser &&
						    IsFileTypeBeforeAnother(usersFlattened, usersFlattened.Count - 1,
							    ".mat", ".fbx", out idxOfPrevious))
						{
							// a Material being used by an FBX is only as a default, initial value for that FBX.
							// after the FBX is instantiated, we have no guarantee that Material is still being
							// used, so additional checks are needed

							// check the assets that use the FBX file, meaning check the places where that FBX
							// is instantiated or referenced, which are either prefabs, scenes, or scriptable objects.
							//
							// do those prefabs/scenes/scriptable objects really use that default material, or has it
							// been overriden?

							userAssetIsFbxUsingMaterial = true;

							materialAssignedAsDefault = usersFlattened[idxOfPrevious].AssetPath;
							var fbxUsingTheMaterialAsDefault = userAsset.AssetPath;

							if (debugLog)
							{
								Debug.LogFormat("Detected fbx using a material.\n" +
								                "Material:\n{0}\n" +
								                "Fbx:\n{1}\n" +
								                "Checking the assets that use the fbx if they really also use the material...",
									materialAssignedAsDefault, fbxUsingTheMaterialAsDefault);
							}

							var isMaterialReallyBeingUsed =
								AreInstancesOfFbxUsingMaterial(fbxUsingTheMaterialAsDefault, materialAssignedAsDefault,
									assetDependencies, debugLog);

							if (!isMaterialReallyBeingUsed)
							{
#if BRT_ASSET_DEPENDENCY_DEBUG
								userAsset.DebugInfo += " [using as default for material]";
#endif
								usersFlattened[usersFlattened.Count - 1] = userAsset;
								noNeedToAddUsersOfUser = true;
							}
						}

						if (!noNeedToAddUsersOfUser &&
						    userAsset.AssetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
						{
							// something being used by a .cs script file is only as a default, initial value for that script
							// after the script is instantiated, we have no guarantee that something is still being
							// used, so it doesn't make sense to go further
#if BRT_ASSET_DEPENDENCY_DEBUG
							userAsset.DebugInfo += " [using as default for script]";
#endif
							usersFlattened[usersFlattened.Count - 1] = userAsset;
							noNeedToAddUsersOfUser = true;
						}

						if (noNeedToAddUsersOfUser)
						{
							continue;
						}
					}

					if (assetDependencies.ContainsKey(userAsset.AssetPath))
					{
						var usersOfUser = assetDependencies[userAsset.AssetPath].Users;
						for (int n = 0, len = usersOfUser.Count; n < len; ++n)
						{
							AssetUserFlattened newEntry = new AssetUserFlattened(usersOfUser[n], userAsset.IndentLevel + 1);

							if (userAssetIsFbxUsingMaterial)
							{
								if (!IsInstanceOfFbxUsingMaterial(userAsset.AssetPath, usersOfUser[n],
									    materialAssignedAsDefault, assetDependencies, debugLog))
								{
									continue;
								}

								if (usersOfUser[n].EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
								{
									// userAsset is an fbx which uses a material
									// newEntry is a scene that uses (an instance of) that fbx
									// BUT we are not sure that
									newEntry.IndentLevel = userAsset.IndentLevel;

									if (Contains(usersFlattened, newEntry, userAsset.AssetPath))
									{
										// already added
										continue;
									}
								}

#if BRT_ASSET_DEPENDENCY_DEBUG
								newEntry.DebugInfo = " [uses fbx which uses material]";
#endif
							}
							else if (userAsset.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
							{
								if (usersOfUser[n].EndsWith("LightingData.asset", StringComparison.OrdinalIgnoreCase))
								{
									// Scenes that have baked lighting use a LightingData.asset (each scene has one).
									// The LightingData.asset has a cyclic dependency with its corresponding scene.
									// We can ignore this, since this is useless information for the `pair.Key` asset.

									if (debugLog)
									{
										Debug.LogFormat("Ignoring \"LightingData.asset\" of scene: {0}",
											userAsset.AssetPath);
									}

									continue;
								}
							}

							openFlattenedSet.Add(newEntry);
						}
					}
				}
			}

			if (debugLog)
			{
				Debug.Log("===================================================");

				foreach (var pair in assetDependencies)
				{
					var stringBuilder = new StringBuilder();

					stringBuilder.Append(pair.Key);

					stringBuilder.Append(" used by:\n");

					for (int n = 0, len = pair.Value.Users.Count; n < len; ++n)
					{
						stringBuilder.Append(n.ToString());
						stringBuilder.Append(". ");
						stringBuilder.Append(pair.Value.Users[n]);
						stringBuilder.Append("\n");
					}

					Debug.Log(stringBuilder.ToString());
				}
			}
		}

		// ==================================================================================

		static void CalculateScriptDependencies(AssetDependencies data, BuildReportTool.BuildInfo buildInfo)
		{
			int scriptsFilterIdx = buildInfo.FileFilters.GetFilterIdx("Scripts");
			SizePart[] usedScripts;
			if (buildInfo.HasUsedAssets && scriptsFilterIdx != -1)
			{
				usedScripts = buildInfo.UsedAssets.PerCategory[scriptsFilterIdx];
			}
			else
			{
				return;
			}

			var assetDependencies = data.GetAssetDependencies();

			var assemblies = CompilationPipeline.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				string assemblyFilename = Path.GetFileName(assembly.outputPath);

				if (!buildInfo.ScriptDLLs.Exists(assemblyFilename) && !buildInfo.UnityEngineDLLs.Exists(assemblyFilename))
				{
					continue;
				}

				foreach (string sourceFile in assembly.sourceFiles)
				{
					if (!usedScripts.Exists(sourceFile))
					{
						continue;
					}

					DependencyEntry assetDependency;
					if (assetDependencies.ContainsKey(sourceFile))
					{
						assetDependency = assetDependencies[sourceFile];
					}
					else
					{
						assetDependency = new DependencyEntry();
						assetDependencies.Add(sourceFile, assetDependency);
					}

					assetDependency.Users.Add(assemblyFilename);

					var usersFlattened = new List<AssetUserFlattened>(assetDependency.Users.Count);
					assetDependency.UsersFlattened = usersFlattened;

					var newEntry = new AssetUserFlattened(assemblyFilename, 1
#if BRT_ASSET_DEPENDENCY_DEBUG
					, "[initial]"
#endif
					);
					usersFlattened.Add(newEntry);
				}
			}
		}

		// ==================================================================================
	}
}