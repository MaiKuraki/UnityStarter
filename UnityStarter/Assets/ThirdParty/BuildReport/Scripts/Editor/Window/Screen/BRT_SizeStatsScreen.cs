using System.Globalization;
using UnityEngine;

namespace BuildReportTool.Window.Screen
{
	public class SizeStats : BaseScreen
	{
		public override string Name
		{
			get { return Labels.SIZE_STATS_CATEGORY_LABEL; }
		}

		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
		}

		Vector2 _assetListScrollPos;

		bool _hasTotalBuildSize;
		bool _hasUsedAssetsTotalSize;
		bool _hasBuildSizes;
		bool _hasCompressedBuildSize;
		bool _hasMonoDLLsToDisplay;
		bool _hasUnityEngineDLLsToDisplay;
		bool _hasScriptDLLsToDisplay;

		public override void DrawGUI(Rect position,
			BuildInfo buildReportToDisplay, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData,
			UnityBuildReport unityBuildReport, BuildReportTool.ExtraData extraData,
			out bool requestRepaint)
		{
			if (buildReportToDisplay == null)
			{
				requestRepaint = false;
				return;
			}

			requestRepaint = false;

			if (Event.current.type == EventType.Layout)
			{
				_hasTotalBuildSize = !string.IsNullOrEmpty(buildReportToDisplay.TotalBuildSize) &&
				                     !string.IsNullOrEmpty(buildReportToDisplay.BuildFilePath);

				_hasUsedAssetsTotalSize = !string.IsNullOrEmpty(buildReportToDisplay.UsedTotalSize);
				_hasCompressedBuildSize = !string.IsNullOrEmpty(buildReportToDisplay.CompressedBuildSize);
				_hasBuildSizes = buildReportToDisplay.BuildSizes != null;
				_hasMonoDLLsToDisplay = buildReportToDisplay.MonoDLLs != null && buildReportToDisplay.MonoDLLs.Length > 0;

				_hasUnityEngineDLLsToDisplay = buildReportToDisplay.UnityEngineDLLs != null &&
				                               buildReportToDisplay.UnityEngineDLLs.Length > 0;

				_hasScriptDLLsToDisplay =
					buildReportToDisplay.ScriptDLLs != null && buildReportToDisplay.ScriptDLLs.Length > 0;
			}


			GUILayout.Space(2); // top padding for scrollbar

			_assetListScrollPos = GUILayout.BeginScrollView(_assetListScrollPos);

			GUILayout.Space(10); // top padding for content

			GUILayout.BeginHorizontal();
			GUILayout.Space(10); // extra left padding

			DrawTotalSize(buildReportToDisplay);

			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_HORIZONTAL_SPACING);
			GUILayout.BeginVertical();

			DrawBuildSizes(buildReportToDisplay);

			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);

			DrawDLLList(buildReportToDisplay);

			GUILayout.EndVertical();
			GUILayout.Space(20); // extra right padding
			GUILayout.EndHorizontal();

			GUILayout.EndScrollView();
		}


		void DrawTotalSize(BuildReportTool.BuildInfo buildReportToDisplay)
		{
			GUILayout.BeginVertical();

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			var descStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TINY_HELP_STYLE_NAME);
			if (descStyle == null)
			{
				descStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BIG_NUMBER_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			if (buildReportToDisplay.HasOldSizeValues)
			{
				// in old sizes:
				// TotalBuildSize is really the used assets size
				// CompressedBuildSize if present is the total build size

				BuildReportTool.Window.Utility.DrawLargeSizeDisplay(Labels.USED_TOTAL_SIZE_LABEL,
					Labels.USED_TOTAL_SIZE_DESC, buildReportToDisplay.TotalBuildSize);
				GUILayout.Space(40);
				BuildReportTool.Window.Utility.DrawLargeSizeDisplay(Labels.BUILD_TOTAL_SIZE_LABEL,
					BuildReportTool.Window.Utility.GetProperBuildSizeDesc(buildReportToDisplay),
					buildReportToDisplay.CompressedBuildSize);
			}
			else
			{
				// Total Build Size
				if (_hasTotalBuildSize)
				{
					GUILayout.BeginVertical();

					var buildPlatform =
						BuildReportTool.ReportGenerator.GetBuildPlatformFromString(buildReportToDisplay.BuildType,
							buildReportToDisplay.BuildTargetUsed);

					GUILayout.Label(
						buildPlatform == BuildPlatform.iOS ? Labels.BUILD_XCODE_SIZE_LABEL : Labels.BUILD_TOTAL_SIZE_LABEL,
						bigLabelStyle);

					GUILayout.Label(BuildReportTool.Util.GetBuildSizePathDescription(buildReportToDisplay),
						descStyle);

					GUILayout.Label(buildReportToDisplay.TotalBuildSize, valueStyle);
					GUILayout.EndVertical();

					DrawAuxiliaryBuildSizes(buildReportToDisplay);
					GUILayout.Space(40);
				}


				// Used Assets
				if (_hasUsedAssetsTotalSize)
				{
					BuildReportTool.Window.Utility.DrawLargeSizeDisplay(Labels.USED_TOTAL_SIZE_LABEL,
						Labels.USED_TOTAL_SIZE_DESC, buildReportToDisplay.UsedTotalSize);
					GUILayout.Space(40);
				}


				// Unused Assets
				if (buildReportToDisplay.UnusedAssetsIncludedInCreation)
				{
					BuildReportTool.Window.Utility.DrawLargeSizeDisplay(Labels.UNUSED_TOTAL_SIZE_LABEL,
						Labels.UNUSED_TOTAL_SIZE_DESC, buildReportToDisplay.UnusedTotalSize);

					if (buildReportToDisplay.ProcessUnusedAssetsInBatches)
					{
						GUILayout.Space(10);

						GUILayout.BeginHorizontal();
						var warning = GUI.skin.FindStyle("Icon-Warning");
						if (warning != null)
						{
							var warningIcon = warning.normal.background;

							var iconWidth = warning.fixedWidth;
							var iconHeight = warning.fixedHeight;

							GUI.DrawTexture(GUILayoutUtility.GetRect(iconWidth, iconHeight), warningIcon);
						}
						GUILayout.Label(string.Format(Labels.UNUSED_TOTAL_IS_FROM_BATCH, buildReportToDisplay.UnusedAssetsEntriesPerBatch), descStyle);
						GUILayout.EndHorizontal();
					}
				}
			}

			GUILayout.EndVertical();
		}


		void DrawAuxiliaryBuildSizes(BuildReportTool.BuildInfo buildReportToDisplay)
		{
			BuildReportTool.BuildPlatform buildPlatform =
				BuildReportTool.ReportGenerator.GetBuildPlatformFromString(buildReportToDisplay.BuildType,
					buildReportToDisplay.BuildTargetUsed);

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			var medLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_SUBTITLE_BOLD_STYLE_NAME);
			if (medLabelStyle == null)
			{
				medLabelStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BIG_NUMBER_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			if (buildPlatform == BuildReportTool.BuildPlatform.Web)
			{
				GUILayout.Space(20);
				GUILayout.BeginVertical();
				GUILayout.Label(Labels.WEB_UNITY3D_FILE_SIZE_LABEL, medLabelStyle);
				GUILayout.Label(buildReportToDisplay.WebFileBuildSize, valueStyle);
				GUILayout.EndVertical();
			}
			else if (buildPlatform == BuildReportTool.BuildPlatform.Android)
			{
				if (!buildReportToDisplay.AndroidCreateProject && buildReportToDisplay.AndroidUseAPKExpansionFiles)
				{
					GUILayout.Space(20);
					GUILayout.BeginVertical();
					GUILayout.Label(Labels.ANDROID_APK_FILE_SIZE_LABEL, medLabelStyle);
					GUILayout.Label(buildReportToDisplay.AndroidApkFileBuildSize, bigLabelStyle);
					GUILayout.EndVertical();

					GUILayout.Space(20);
					GUILayout.BeginVertical();
					GUILayout.Label(Labels.ANDROID_OBB_FILE_SIZE_LABEL, medLabelStyle);
					GUILayout.Label(buildReportToDisplay.AndroidObbFileBuildSize, bigLabelStyle);
					GUILayout.EndVertical();
				}
				else if (buildReportToDisplay.AndroidCreateProject && buildReportToDisplay.AndroidUseAPKExpansionFiles)
				{
					GUILayout.Space(20);
					GUILayout.BeginVertical();
					GUILayout.Label(Labels.ANDROID_OBB_FILE_SIZE_LABEL, medLabelStyle);
					GUILayout.Label(buildReportToDisplay.AndroidObbFileBuildSize, bigLabelStyle);
					GUILayout.EndVertical();
				}
			}

			// Streaming Assets
			if (buildReportToDisplay.HasStreamingAssets)
			{
				GUILayout.Space(20);
				BuildReportTool.Window.Utility.DrawLargeSizeDisplay(Labels.STREAMING_ASSETS_TOTAL_SIZE_LABEL,
					Labels.STREAMING_ASSETS_SIZE_DESC, buildReportToDisplay.StreamingAssetsSize);
			}
		}


		void DrawBuildSizes(BuildReportTool.BuildInfo buildReportToDisplay)
		{
			if (_hasCompressedBuildSize)
			{
				GUILayout.BeginVertical();
			}

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			var medLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_SUBTITLE_BOLD_STYLE_NAME);
			if (medLabelStyle == null)
			{
				medLabelStyle = GUI.skin.label;
			}

			var labelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_SUBTITLE_STYLE_NAME);
			if (labelStyle == null)
			{
				labelStyle = GUI.skin.label;
			}

			GUILayout.Label(Labels.TOTAL_SIZE_BREAKDOWN_LABEL, bigLabelStyle);

			if (_hasCompressedBuildSize)
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label(Labels.TOTAL_SIZE_BREAKDOWN_MSG_PRE_BOLD, labelStyle);
				GUILayout.Label(Labels.TOTAL_SIZE_BREAKDOWN_MSG_BOLD, medLabelStyle);
				GUILayout.Label(Labels.TOTAL_SIZE_BREAKDOWN_MSG_POST_BOLD, labelStyle);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();

				GUILayout.EndVertical();
			}

			if (_hasBuildSizes)
			{
				GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutMaxWidth500);

				DrawNames(buildReportToDisplay.BuildSizes);
				DrawReadableSizes(buildReportToDisplay.BuildSizes);
				DrawPercentages(buildReportToDisplay.BuildSizes);

				GUILayout.EndHorizontal();
			}
		}

		void DrawDLLList(BuildReportTool.BuildInfo buildReportToDisplay)
		{
			BuildReportTool.BuildPlatform buildPlatform =
				BuildReportTool.ReportGenerator.GetBuildPlatformFromString(buildReportToDisplay.BuildType,
					buildReportToDisplay.BuildTargetUsed);

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			GUILayout.BeginHorizontal();

			// column 1
			GUILayout.BeginVertical();
			if (_hasMonoDLLsToDisplay)
			{
				GUILayout.Label(Labels.MONO_DLLS_LABEL, bigLabelStyle);
				{
					GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutMaxWidth500);
					DrawNames(buildReportToDisplay.MonoDLLs);
					DrawReadableSizes(buildReportToDisplay.MonoDLLs);
					GUILayout.EndHorizontal();
				}

				GUILayout.Space(20);
			}

			if (_hasUnityEngineDLLsToDisplay)
			{
				DrawScriptDLLsList(buildReportToDisplay, buildPlatform);
			}

			GUILayout.EndVertical();

			GUILayout.Space(15);

			// column 2
			GUILayout.BeginVertical();
			if (_hasUnityEngineDLLsToDisplay)
			{
				GUILayout.Label(Labels.UNITY_ENGINE_DLLS_LABEL, bigLabelStyle);
				{
					GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutMaxWidth500);
					DrawNames(buildReportToDisplay.UnityEngineDLLs);
					DrawReadableSizes(buildReportToDisplay.UnityEngineDLLs);
					GUILayout.EndHorizontal();
				}
			}
			else
			{
				DrawScriptDLLsList(buildReportToDisplay, buildPlatform);
			}

			GUILayout.Space(20);
			GUILayout.EndVertical();

			GUILayout.EndHorizontal();
		}

		void DrawScriptDLLsList(BuildReportTool.BuildInfo buildReportToDisplay,
			BuildReportTool.BuildPlatform buildPlatform)
		{
			if (!_hasScriptDLLsToDisplay)
			{
				return;
			}

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			GUILayout.Label(Labels.SCRIPT_DLLS_LABEL, bigLabelStyle);
			{
				GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutMaxWidth500);
				DrawNames(buildReportToDisplay.ScriptDLLs);
				DrawReadableSizes(buildReportToDisplay.ScriptDLLs);
				GUILayout.EndHorizontal();
			}
		}


		void DrawNames(BuildReportTool.SizePart[] list)
		{
			if (list == null)
			{
				return;
			}

			var listNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_STYLE_NAME);
			if (listNormalStyle == null)
			{
				listNormalStyle = GUI.skin.label;
			}

			var listAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_ALT_STYLE_NAME);
			if (listAltStyle == null)
			{
				listAltStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical();
			bool useAlt = false;
			foreach (BuildReportTool.SizePart b in list)
			{
				if (b.IsTotal) continue;
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				GUILayout.Label(b.Name, styleToUse);
				useAlt = !useAlt;
			}

			GUILayout.EndVertical();
		}

		void DrawReadableSizes(BuildReportTool.SizePart[] list)
		{
			if (list == null)
			{
				return;
			}

			var listNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_STYLE_NAME);
			if (listNormalStyle == null)
			{
				listNormalStyle = GUI.skin.label;
			}

			var listAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_ALT_STYLE_NAME);
			if (listAltStyle == null)
			{
				listAltStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical();
			bool useAlt = false;
			foreach (BuildReportTool.SizePart b in list)
			{
				if (b.IsTotal) continue;
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				GUILayout.Label(b.Size, styleToUse);
				useAlt = !useAlt;
			}

			GUILayout.EndVertical();
		}

		void DrawPercentages(BuildReportTool.SizePart[] list)
		{
			if (list == null)
			{
				return;
			}

			var listNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_STYLE_NAME);
			if (listNormalStyle == null)
			{
				listNormalStyle = GUI.skin.label;
			}

			var listAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_ALT_STYLE_NAME);
			if (listAltStyle == null)
			{
				listAltStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical();
			bool useAlt = false;
			foreach (BuildReportTool.SizePart b in list)
			{
				if (b.IsTotal) continue;
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				GUILayout.Label(string.Format("{0}%", b.Percentage.ToString(CultureInfo.InvariantCulture)), styleToUse);
				useAlt = !useAlt;
			}

			GUILayout.EndVertical();
		}
	}
}