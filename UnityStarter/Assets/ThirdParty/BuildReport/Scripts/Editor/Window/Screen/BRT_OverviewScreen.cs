using System.Globalization;
using UnityEngine;
using UnityEditor;

namespace BuildReportTool.Window.Screen
{
	public class Overview : BaseScreen
	{
		Vector2 _scrollPos = Vector2.zero;

		bool _showTopUsed;
		bool _showTopUnused;

		int _assetUsedEntryHoveredIdx = -1;
		int _assetUnusedEntryHoveredIdx = -1;


		bool _shouldShowThumbnailOnHoveredAsset;


		public override string Name
		{
			get { return Labels.OVERVIEW_CATEGORY_LABEL; }
		}

		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
		}

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

			var titleStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.MAIN_TITLE_STYLE_NAME);
			if (titleStyle == null)
			{
				titleStyle = GUI.skin.label;
			}

			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
			}

			var bigValueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_SUBTITLE_STYLE_NAME);
			if (bigValueStyle == null)
			{
				bigValueStyle = GUI.skin.label;
			}

			var bigNumberStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BIG_NUMBER_STYLE_NAME);
			if (bigNumberStyle == null)
			{
				bigNumberStyle = GUI.skin.label;
			}

			var smallLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_NAME_STYLE_NAME);
			if (smallLabelStyle == null)
			{
				smallLabelStyle = GUI.skin.label;
			}

			var smallValueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_VALUE_STYLE_NAME);
			if (smallValueStyle == null)
			{
				smallValueStyle = GUI.skin.label;
			}

			var helpDescriptionStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TINY_HELP_STYLE_NAME);
			if (helpDescriptionStyle == null)
			{
				helpDescriptionStyle = GUI.skin.label;
			}

			var infoDescriptionStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TEXT_STYLE_NAME);
			if (infoDescriptionStyle == null)
			{
				infoDescriptionStyle = GUI.skin.label;
			}


			GUILayout.Space(2); // top padding for scrollbar

			_scrollPos = GUILayout.BeginScrollView(_scrollPos);

			GUILayout.BeginHorizontal();
			GUILayout.Space(10); // extra left padding


			GUILayout.BeginVertical();

			GUILayout.Space(10); // top padding


			// report title
			GUILayout.Label(buildReportToDisplay.SuitableTitle, titleStyle);


			GUILayout.Space(10);


			// two-column layout
			GUILayout.BeginVertical();

			GUILayout.BeginHorizontal();
			// 1st column
			GUILayout.BeginVertical(GUILayout.MaxWidth(350));
			GUILayout.Label(Labels.TIME_OF_BUILD_LABEL, bigLabelStyle);
			GUILayout.Label(buildReportToDisplay.GetTimeReadable(), bigValueStyle);

			GUILayout.Space(10);
			GUILayout.Label("Project building took:", bigLabelStyle);
			GUILayout.Label("How long the project building took. This is the time between <b>OnPreprocessBuild</b> and <b>OnPostprocessBuild</b>.", helpDescriptionStyle);
			GUILayout.Label(buildReportToDisplay.BuildDurationTime.ToString(), bigValueStyle);

			GUILayout.Space(10);
			GUILayout.Label("Report generation took:", bigLabelStyle);
			GUILayout.Label("How long the Build Report Generation took.", helpDescriptionStyle);
			GUILayout.Label(buildReportToDisplay.ReportGenerationTime.ToString(), bigValueStyle);

			if (!string.IsNullOrEmpty(buildReportToDisplay.TotalBuildSize) &&
			    !string.IsNullOrEmpty(buildReportToDisplay.BuildFilePath))
			{
				GUILayout.Space(10);
				GUILayout.BeginVertical();
				GUILayout.Label(Labels.BUILD_TOTAL_SIZE_LABEL, bigLabelStyle);

				GUILayout.Label(BuildReportTool.Util.GetBuildSizePathDescription(buildReportToDisplay), helpDescriptionStyle);

				GUILayout.Label(buildReportToDisplay.TotalBuildSize, bigNumberStyle);
				GUILayout.EndVertical();
			}

			GUILayout.Space(20);

			string emphasisColor = "black";
			if (EditorGUIUtility.isProSkin || BRT_BuildReportWindow.FORCE_USE_DARK_SKIN)
			{
				emphasisColor = "white";
			}

			string largestAssetCategoryLabel = string.Format(
				"<color={0}><size=20><b>{1}</b></size></color> are the largest,\ntaking up <color={0}><size=20><b>{2}%</b></size></color> of the build{3}",
				emphasisColor, buildReportToDisplay.BuildSizes[0].Name,
				buildReportToDisplay.BuildSizes[0].Percentage.ToString(CultureInfo.InvariantCulture),
				(!buildReportToDisplay.HasStreamingAssets
					 ? "\n<size=12>(not counting streaming assets)</size>"
					 : ""));

			GUILayout.Label(largestAssetCategoryLabel, infoDescriptionStyle);
			GUILayout.Space(20);
			GUILayout.EndVertical();

			GUILayout.Space(20);

			// 2nd column
			GUILayout.BeginVertical(GUILayout.MaxWidth(250));
			GUILayout.Label("Made for:", bigLabelStyle);
			GUILayout.Label(buildReportToDisplay.BuildType, bigValueStyle);

			GUILayout.Space(10);
			GUILayout.Label("Built in:", bigLabelStyle);
			GUILayout.Label(buildReportToDisplay.UnityVersionDisplayed, bigValueStyle);
			GUILayout.EndVertical();

			DrawScenesInBuild(buildReportToDisplay.ScenesInBuild);

			GUILayout.EndHorizontal();


			GUILayout.BeginHorizontal();

			var numberOfTopUsed =
				buildReportToDisplay.HasUsedAssets ? buildReportToDisplay.UsedAssets.NumberOfTopLargest : 0;
			var numberOfTopUnused = buildReportToDisplay.HasUnusedAssets
				                        ? buildReportToDisplay.UnusedAssets.NumberOfTopLargest
				                        : 0;
			if (Event.current.type == EventType.Layout)
			{
				_showTopUsed = numberOfTopUsed > 0 && buildReportToDisplay.UsedAssets.TopLargest != null;
				_showTopUnused = numberOfTopUnused > 0 && buildReportToDisplay.UnusedAssets.TopLargest != null;
			}

			// 1st column
			GUILayout.BeginVertical();
			if (_showTopUsed)
			{
				GUILayout.Label(string.Format("Top {0} largest in build:", numberOfTopUsed.ToString()),
					bigLabelStyle);

				GUILayout.Space(4);
				if (!BuildReportTool.Options.AutoResortAssetsWhenUnityEditorRegainsFocus &&
				    GUILayout.Button("Refresh", GUILayout.Height(20), GUILayout.MaxWidth(520)))
				{
					buildReportToDisplay.RecategorizeUsedAssets();
					buildReportToDisplay.FlagOkToRefresh();
				}

				DrawAssetList(buildReportToDisplay.UsedAssets, true, buildReportToDisplay, assetDependencies);
			}

			GUILayout.EndVertical();

			GUILayout.Space(50);

			// 2nd column
			GUILayout.BeginVertical();
			if (_showTopUnused)
			{
				GUILayout.Label(string.Format("Top {0} largest not in build:", numberOfTopUnused.ToString()),
					bigLabelStyle);

				GUILayout.Space(4);
				if (!BuildReportTool.Options.AutoResortAssetsWhenUnityEditorRegainsFocus &&
				    GUILayout.Button("Refresh", GUILayout.Height(20), GUILayout.MaxWidth(520)))
				{
					buildReportToDisplay.RecategorizeUnusedAssets();
					buildReportToDisplay.FlagOkToRefresh();
				}

				DrawAssetList(buildReportToDisplay.UnusedAssets, false, buildReportToDisplay, assetDependencies);
			}

			GUILayout.EndVertical();
			GUILayout.EndHorizontal();

			GUILayout.EndVertical();


			GUILayout.Space(20);

			if (assetDependencies != null && !string.IsNullOrEmpty(assetDependencies.SavedPath))
			{
				GUILayout.Label("Asset Dependencies file used:", smallLabelStyle);
				GUILayout.Label(assetDependencies.SavedPath, smallValueStyle);

				GUILayout.Space(10);
			}

			if (textureData != null && !string.IsNullOrEmpty(textureData.SavedPath))
			{
				GUILayout.Label("Texture Data file used:", smallLabelStyle);
				GUILayout.Label(textureData.SavedPath, smallValueStyle);

				GUILayout.Space(10);
			}

			if (unityBuildReport != null && !string.IsNullOrEmpty(unityBuildReport.SavedPath))
			{
				GUILayout.Label("Additional Unity Build Report file used:", smallLabelStyle);
				GUILayout.Label(unityBuildReport.SavedPath, smallValueStyle);

				GUILayout.Space(10);
			}

			if (!string.IsNullOrEmpty(extraData.Contents))
			{
				GUILayout.Label("Extra Data file used:", smallLabelStyle);
				GUILayout.Label(extraData.SavedPath, smallValueStyle);

				GUILayout.Space(10);
			}

			GUILayout.Space(20);

			GUILayout.EndVertical();

			GUILayout.Space(20); // extra right padding
			GUILayout.EndHorizontal();

			GUILayout.EndScrollView();

			// ------------------------------------------------------

			// Continually request repaint, since we need to check the rects
			// generated by the GUILayout (using GUILayoutUtility.GetLastRect())
			// to make the hover checks work. That's because GetLastRect() only
			// works during repaint event.
			//
			// Later checks below can set requestRepaint to false if there's no
			// need to repaint, to help lessen cpu usage.
			requestRepaint = true;

			if (Event.current.mousePosition.y >= position.height ||
			    Event.current.mousePosition.y <= 0 ||
			    Event.current.mousePosition.x <= 0 ||
			    Event.current.mousePosition.x >= position.width)
			{
				// mouse is outside the window, no need to repaint, can't show tooltip anyway
				// set requestRepaint to false to help lessen cpu usage
				requestRepaint = false;
			}

			var showThumbnailNow = BuildReportTool.Options.ShowTooltipThumbnail &&
			                       _shouldShowThumbnailOnHoveredAsset &&
			                       (_assetUsedEntryHoveredIdx != -1 || _assetUnusedEntryHoveredIdx != -1);

			var zoomInChanged = false;
			if (showThumbnailNow)
			{
				var prevZoomedIn = BRT_BuildReportWindow.ZoomedInThumbnails;

				// if thumbnail is currently showing, we process the inputs
				// (ctrl zooms in on thumbnail, alt toggles alpha blend)
				BRT_BuildReportWindow.ProcessThumbnailControls();

				if (prevZoomedIn != BRT_BuildReportWindow.ZoomedInThumbnails)
				{
					zoomInChanged = true;
				}
			}
			else
			{
				// no thumbnail currently shown. ensure the controls that
				// need to be reset to initial state are reset
				BRT_BuildReportWindow.ResetThumbnailControls();
			}

			if (!zoomInChanged && !Event.current.alt &&
			    !BRT_BuildReportWindow.MouseMovedNow && !BRT_BuildReportWindow.LastMouseMoved)
			{
				// mouse hasn't moved, and no request to zoom-in thumbnail or toggle thumbnail alpha
				// no need to repaint because nothing has changed
				// set requestRepaint to false to help lessen cpu usage
				requestRepaint = false;
			}

			var shouldShowAssetEndUsersTooltipNow = BuildReportTool.Options.ShowAssetPrimaryUsersInTooltipIfAvailable &&
			                                        BRT_BuildReportWindow.ShouldHoveredAssetShowEndUserTooltip(
				                                        assetDependencies) &&
			                                        (_assetUsedEntryHoveredIdx != -1 || _assetUnusedEntryHoveredIdx != -1);

			if (Event.current.type == EventType.Repaint)
			{
				if (showThumbnailNow && shouldShowAssetEndUsersTooltipNow)
				{
					// draw thumbnail and end users below it
					BRT_BuildReportWindow.DrawThumbnailEndUsersTooltip(position, assetDependencies, textureData);
				}
				else if (shouldShowAssetEndUsersTooltipNow)
				{
					// draw only end users in tooltip
					BRT_BuildReportWindow.DrawEndUsersTooltip(position, assetDependencies);
				}
				else if (showThumbnailNow)
				{
					// draw only thumbnail in tooltip
					BRT_BuildReportWindow.DrawThumbnailTooltip(position, textureData);
				}
			}
		}

		void DrawAssetList(BuildReportTool.AssetList assetList, bool usedAssets, BuildInfo buildReportToDisplay,
			AssetDependencies assetDependencies)
		{
			if (assetList == null || assetList.TopLargest == null)
			{
				//Debug.LogError("no top ten largest");
				return;
			}

			BuildReportTool.SizePart[] assetsToShow = assetList.TopLargest;

			if (assetsToShow == null)
			{
				//Debug.LogError("no top ten largest");
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

			var listIconNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_ICON_STYLE_NAME);
			if (listIconNormalStyle == null)
			{
				listIconNormalStyle = GUI.skin.label;
			}

			var listIconAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_ICON_ALT_STYLE_NAME);
			if (listIconAltStyle == null)
			{
				listIconAltStyle = GUI.skin.label;
			}

			var iconHoveredStyle = GUI.skin.FindStyle("IconHovered");
			if (iconHoveredStyle == null)
			{
				iconHoveredStyle = GUI.skin.label;
			}

			bool useAlt = true;

			var newEntryHoveredIdx = -1;
			BuildReportTool.SizePart newEntryHovered = null;
			Rect newEntryHoveredRect = new Rect();
			Rect iconRect = new Rect();
			var hoveringOverIcon = false;
			//var hoveringOverLabel = false;

			GUILayout.BeginHorizontal();

			// 1st column: name
			GUILayout.BeginVertical();
			for (int n = 0; n < assetsToShow.Length; ++n)
			{
				BuildReportTool.SizePart b = assetsToShow[n];

				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				var iconStyleToUse = useAlt ? listIconAltStyle : listIconNormalStyle;

				if (styleToUse == null)
				{
					styleToUse = GUI.skin.label;
				}
				if (iconStyleToUse == null)
				{
					iconStyleToUse = GUI.skin.label;
				}

				Texture icon = AssetDatabase.GetCachedIcon(b.Name);

				GUILayout.BeginHorizontal();
				if (icon == null)
				{
					// no icon, just add space so it aligns with the other entries
					GUILayout.Label(string.Empty, iconStyleToUse, BRT_BuildReportWindow.Layout28x30);
				}
				else
				{
					GUILayout.Button(icon, iconStyleToUse, BRT_BuildReportWindow.Layout28x30);
				}

				if (Event.current.type == EventType.Repaint)
				{
					iconRect = GUILayoutUtility.GetLastRect();

					// if mouse is hovering over asset entry's icon (not the label)
					// draw a border on the asset icon
					if (iconRect.Contains(Event.current.mousePosition))
					{
						hoveringOverIcon = true;
						newEntryHoveredIdx = n;
						newEntryHovered = b;
						newEntryHoveredRect = iconRect;

						GUI.Box(iconRect, icon, iconHoveredStyle);
					}
				}

				string prettyName = string.Format(" {0}. {1}", (n + 1).ToString(), BuildReportTool.Util.GetAssetFilename(b.Name));
				if (GUILayout.Button(prettyName, styleToUse, BRT_BuildReportWindow.Layout100To400x30))
				{
					Utility.PingAssetInProject(b.Name);
				}

				if (newEntryHoveredIdx == -1 && Event.current.type == EventType.Repaint)
				{
					var labelRect = GUILayoutUtility.GetLastRect();

					// if mouse is hovering over asset entry's label
					// draw a border on the asset icon
					if (labelRect.Contains(Event.current.mousePosition))
					{
						//hoveringOverLabel = true;
						newEntryHoveredIdx = n;
						newEntryHovered = b;
						newEntryHoveredRect = labelRect;

						GUI.Box(iconRect, icon, iconHoveredStyle);
					}
				}

				GUILayout.EndHorizontal();

				useAlt = !useAlt;
			}

			GUILayout.EndVertical();


			if (Event.current.type == EventType.Repaint)
			{
				if (usedAssets)
				{
					_assetUsedEntryHoveredIdx = newEntryHoveredIdx;
				}
				else
				{
					_assetUnusedEntryHoveredIdx = newEntryHoveredIdx;
				}

				if (newEntryHoveredIdx != -1)
				{
					string hoveredAssetPath = newEntryHovered != null ? newEntryHovered.Name : null;

					// ----------------
					// update what is considered the hovered asset, for use later on
					// when the tooltip will be drawn
					BRT_BuildReportWindow.UpdateHoveredAsset(hoveredAssetPath, newEntryHoveredRect,
						usedAssets, buildReportToDisplay, assetDependencies);

					// ----------------
					// if mouse is hovering over the correct area, we signify that
					// the tooltip thumbnail should be drawn
					if (BuildReportTool.Options.ShowTooltipThumbnail &&
					    (BuildReportTool.Options.ShowThumbnailOnHoverLabelToo || hoveringOverIcon) &&
					    BRT_BuildReportWindow.GetAssetPreview(hoveredAssetPath) != null)
					{
						_shouldShowThumbnailOnHoveredAsset = true;
					}
					else
					{
						_shouldShowThumbnailOnHoveredAsset = false;
					}
				}
			}

			// 2nd column: size

			var useRawSize = (usedAssets && !BuildReportTool.Options.ShowImportedSizeForUsedAssets) || !usedAssets;

			useAlt = true;
			GUILayout.BeginVertical();
			for (int n = 0; n < assetsToShow.Length; ++n)
			{
				BuildReportTool.SizePart b = assetsToShow[n];

				var styleToUse = useAlt ? listAltStyle : listNormalStyle;

				GUILayout.Label(useRawSize ? b.RawSize : b.ImportedSize, styleToUse, BRT_BuildReportWindow.LayoutTo100x30);

				useAlt = !useAlt;
			}

			GUILayout.EndVertical();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		void DrawScenesInBuild(BuildReportTool.BuildInfo.SceneInBuild[] scenesInBuild)
		{
			if (scenesInBuild == null || scenesInBuild.Length == 0)
			{
				//Debug.LogError("no top ten largest");
				return;
			}


			var bigLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (bigLabelStyle == null)
			{
				bigLabelStyle = GUI.skin.label;
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

			var listIconNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_ICON_STYLE_NAME);
			if (listIconNormalStyle == null)
			{
				listIconNormalStyle = GUI.skin.label;
			}

			var listIconAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_ICON_ALT_STYLE_NAME);
			if (listIconAltStyle == null)
			{
				listIconAltStyle = GUI.skin.label;
			}

			bool useAlt = true;

			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutExpandWidth);
			GUILayout.Label("Scenes in Build:", bigLabelStyle);

			var prevColor = GUI.contentColor;

			//GUILayout.BeginHorizontal();
			// 1st column: name
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutExpandWidth);
			var usedIdx = -1;
			for (int n = 0; n < scenesInBuild.Length; ++n)
			{
				if (scenesInBuild[n].Enabled)
				{
					++usedIdx;
				}

				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				var iconStyleToUse = useAlt ? listIconAltStyle : listIconNormalStyle;


				Texture icon = AssetDatabase.GetCachedIcon(scenesInBuild[n].Path);

				GUILayout.BeginHorizontal(styleToUse, BRT_BuildReportWindow.LayoutExpandWidth);

				// enabled status
				//GUILayout.Toggle(scenesInBuild[n].Enabled, string.Empty, GUILayout.Width(20), GUILayout.Height(30));

				// icon
				if (icon == null)
				{
					//GUILayout.Space(22);
					GUILayout.Label(string.Empty, iconStyleToUse, BRT_BuildReportWindow.Layout28x30);
				}
				else
				{
					if (!scenesInBuild[n].Enabled)
					{
						GUI.contentColor = new Color(1.0f, 1.0f, 1.0f, 0.4f);
					}

					GUILayout.Button(icon, iconStyleToUse, BRT_BuildReportWindow.Layout28x30);
					if (!scenesInBuild[n].Enabled)
					{
						GUI.contentColor = prevColor;
					}
				}


				// scene index
				if (scenesInBuild[n].Enabled)
				{
					if (GUILayout.Button(usedIdx.ToString(), styleToUse, BRT_BuildReportWindow.Layout20x30))
					{
						Utility.PingAssetInProject(scenesInBuild[n].Path);
					}
				}
				else
				{
					GUILayout.Label(string.Empty, iconStyleToUse, BRT_BuildReportWindow.Layout20x30);
				}

				// path
				string prettyName;
				if (string.IsNullOrEmpty(scenesInBuild[n].Path))
				{
					prettyName = string.Format("<color=#{0}><i>Missing</i></color>",
						BuildReportTool.Window.Screen.AssetList.GetPathColor(false));
				}
				else
				{
					var pathName = BuildReportTool.Util.GetAssetPath(scenesInBuild[n].Path);
					var fileName = scenesInBuild[n].Path.GetFileNameOnly();

					if (scenesInBuild[n].Enabled)
					{
						prettyName = string.Format("<color=#{0}>{1}</color><b>{2}</b>",
							BuildReportTool.Window.Screen.AssetList.GetPathColor(false),
							pathName, fileName);
					}
					else
					{
						prettyName = string.Format("<color=#{0}>{1}<b>{2}</b></color>",
							BuildReportTool.Window.Screen.AssetList.GetPathColor(false),
							pathName, fileName);
					}
				}

				if (GUILayout.Button(prettyName, styleToUse, BRT_BuildReportWindow.Layout100x30))
				{
					Utility.PingAssetInProject(scenesInBuild[n].Path);
				}

				GUILayout.EndHorizontal();

				useAlt = !useAlt;
			}

			GUILayout.EndVertical();


			GUILayout.FlexibleSpace();
			//GUILayout.EndHorizontal();
			GUILayout.Space(5); // bottom padding
			GUILayout.EndVertical();
		}
	}
}