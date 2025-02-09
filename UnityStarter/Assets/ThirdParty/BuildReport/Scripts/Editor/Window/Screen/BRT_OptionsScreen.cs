using System.IO;
using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;
using UnityEditorInternal;

namespace BuildReportTool.Window.Screen
{
	public class Options : BaseScreen
	{
		public override string Name
		{
			get { return Labels.OPTIONS_CATEGORY_LABEL; }
		}

		string[] _saveTypeLabels;

		/// <summary>
		/// 0: always use configured file filters <br/>
		/// 1: use file filters embedded in opened build report, if available
		/// </summary>
		static readonly string[] FileFilterToUseType =
			{Labels.FILTER_GROUP_TO_USE_CONFIGURED_LABEL, Labels.FILTER_GROUP_TO_USE_EMBEDDED_LABEL};

		/// <summary>
		/// 0: mouse is hovering over icon <br/>
		/// 1: mouse is hovering over icon or label
		/// </summary>
		static readonly string[] ShowThumbnailOnHoverTypeLabels =
			{"Mouse is hovering over asset's icon", "Mouse is hovering over asset's icon or label"};

		/// <summary>
		/// 0: dedicated ping button before each asset <br/>
		/// 1: double-click on asset label will ping
		/// </summary>
		static readonly string[] AssetPingTypeLabels =
			{"Dedicated ping button before each asset", "Double-clicking on asset will ping"};

		/// <summary>
		/// 0: verbose <br/>
		/// 1: standard <br/>
		/// 2: minimal
		/// </summary>
		static readonly string[] AssetUsageLabelTypeLabels =
		{
			"Verbose\n(use words only)",
			"Standard\n(use arrows when possible, show any extra info with words)",
			"Minimal\n(use arrows only, don't show any extra info even if available)"
		};

		static readonly string[] SearchTypeLabels =
		{
			"Basic",
			"Regex",
			"Fuzzy Search"
		};

		string OPEN_IN_FILE_BROWSER_OS_SPECIFIC_LABEL
		{
			get
			{
				if (BuildReportTool.Util.IsInWinOS)
					return Labels.OPEN_IN_FILE_BROWSER_WIN_LABEL;
				if (BuildReportTool.Util.IsInMacOS)
					return Labels.OPEN_IN_FILE_BROWSER_MAC_LABEL;

				return Labels.OPEN_IN_FILE_BROWSER_DEFAULT_LABEL;
			}
		}

		string SAVE_PATH_TYPE_PERSONAL_OS_SPECIFIC_LABEL
		{
			get
			{
				if (BuildReportTool.Util.IsInWinOS)
					return Labels.SAVE_PATH_TYPE_PERSONAL_WIN_LABEL;
				if (BuildReportTool.Util.IsInMacOS)
					return Labels.SAVE_PATH_TYPE_PERSONAL_MAC_LABEL;

				return Labels.SAVE_PATH_TYPE_PERSONAL_DEFAULT_LABEL;
			}
		}


		static readonly string[] CalculationTypeLabels =
		{
			Labels.CALCULATION_LEVEL_FULL_NAME,
			Labels.CALCULATION_LEVEL_NO_PREFAB_NAME,
			Labels.CALCULATION_LEVEL_NO_UNUSED_NAME,
			Labels.CALCULATION_LEVEL_MINIMAL_NAME
		};

		int _selectedCalculationLevelIdx;

		string CalculationLevelDescription
		{
			get
			{
				switch (_selectedCalculationLevelIdx)
				{
					case 0:
						return Labels.CALCULATION_LEVEL_FULL_DESC;
					case 1:
						return Labels.CALCULATION_LEVEL_NO_PREFAB_DESC;
					case 2:
						return Labels.CALCULATION_LEVEL_NO_UNUSED_DESC;
					case 3:
						return Labels.CALCULATION_LEVEL_MINIMAL_DESC;
				}

				return "";
			}
		}

		int GetCalculationLevelGuiIdxFromOptions()
		{
			if (BuildReportTool.Options.IsCurrentCalculationLevelAtFull)
			{
				return 0;
			}

			if (BuildReportTool.Options.IsCurrentCalculationLevelAtNoUnusedPrefabs)
			{
				return 1;
			}

			if (BuildReportTool.Options.IsCurrentCalculationLevelAtNoUnusedAssets)
			{
				return 2;
			}

			if (BuildReportTool.Options.IsCurrentCalculationLevelAtOverviewOnly)
			{
				return 3;
			}

			return 0;
		}

		void SetCalculationLevelFromGuiIdx(int selectedIdx)
		{
			switch (selectedIdx)
			{
				case 0:
					BuildReportTool.Options.SetCalculationLevelToFull();
					break;
				case 1:
					BuildReportTool.Options.SetCalculationLevelToNoUnusedPrefabs();
					break;
				case 2:
					BuildReportTool.Options.SetCalculationLevelToNoUnusedAssets();
					break;
				case 3:
					BuildReportTool.Options.SetCalculationLevelToOverviewOnly();
					break;
			}
		}


		Vector2 _assetListScrollPos;


		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
			if (_saveTypeLabels == null)
			{
				_saveTypeLabels = new[] {SAVE_PATH_TYPE_PERSONAL_OS_SPECIFIC_LABEL, Labels.SAVE_PATH_TYPE_PROJECT_LABEL, Labels.SAVE_PATH_TYPE_CUSTOM_LABEL};
			}

			_selectedCalculationLevelIdx = GetCalculationLevelGuiIdxFromOptions();
		}

		GUIStyle _linkStyle;
		GUIStyle _textBesideLinkStyle;

		static readonly GUILayoutOption[] LayoutMinWidth100 = new[] { GUILayout.MinWidth(100) };
		static readonly GUILayoutOption[] LayoutMinWidth200 = new[] { GUILayout.MinWidth(200) };
		static readonly GUILayoutOption[] LayoutMinWidth250 = new[] { GUILayout.MinWidth(250) };
		static readonly GUILayoutOption[] LayoutMaxWidth525 = new[] { GUILayout.MaxWidth(525) };
		static readonly GUILayoutOption[] LayoutMaxWidth593 = new[] { GUILayout.MaxWidth(593) };
		static readonly GUILayoutOption[] LayoutMaxWidth848 = new[] { GUILayout.MaxWidth(848) };
		static readonly GUILayoutOption[] LayoutMaxWidth500MinHeight75 = new[] { GUILayout.MaxWidth(500), GUILayout.MinHeight(75) };

		static readonly GUILayoutOption[] LayoutWidth300 = new[] { GUILayout.Width(300) };
		static readonly GUILayoutOption[] LayoutHeight26 = new[] { GUILayout.Height(26) };

		static readonly GUILayoutOption[] LayoutNoExpandWidth = new[] { GUILayout.ExpandWidth(false) };

		string _hoveredControlTooltipText;
		readonly GUIContent _tooltipLabel = new GUIContent();

		ReorderableList _ignorePatternList;
		readonly GUIContent _basicSearchRadioLabel = new GUIContent("Basic");
		readonly GUIContent _regexSearchRadioLabel = new GUIContent("Regex");

		readonly GUIContent _infoMessage = new GUIContent();

		Texture2D _iconValid;
		Texture2D _iconInvalid;

		public override void DrawGUI(Rect position,
			BuildInfo buildReportToDisplay, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData,
			UnityBuildReport unityBuildReport, BuildReportTool.ExtraData extraData,
			out bool requestRepaint)
		{
			if (Event.current.type == EventType.Repaint)
			{
				_hoveredControlTooltipText = null;
			}

			var validityStyle = GUI.skin.FindStyle("IconValidity");
			if (validityStyle != null)
			{
				_iconValid = validityStyle.normal.background;
				_iconInvalid = validityStyle.hover.background;
			}

			requestRepaint = true;

			if (!BRT_BuildReportWindow.MouseMovedNow && !BRT_BuildReportWindow.LastMouseMoved)
			{
				// mouse hasn't moved
				// no need to repaint because nothing has changed
				// set requestRepaint to false to help lessen cpu usage
				requestRepaint = false;
			}

			var boxedLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BOXED_LABEL_STYLE_NAME);
			if (boxedLabelStyle == null)
			{
				boxedLabelStyle = GUI.skin.box;
			}

			var header1Style = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (header1Style == null)
			{
				header1Style = GUI.skin.label;
			}

			var header2Style = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SUB_TITLE_STYLE_NAME);
			if (header2Style == null)
			{
				header2Style = GUI.skin.label;
			}

			var prevEnabled = GUI.enabled;

			GUILayout.Space(10); // extra top padding


			_assetListScrollPos = GUILayout.BeginScrollView(_assetListScrollPos, BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20); // extra left padding
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			if (!string.IsNullOrEmpty(BuildReportTool.Options.FoundPathForSavedOptions))
			{
				GUILayout.BeginHorizontal(boxedLabelStyle, BRT_BuildReportWindow.LayoutNone);
				GUILayout.Label(string.Format("Using options file in: {0}",
					BuildReportTool.Options.FoundPathForSavedOptions), BRT_BuildReportWindow.LayoutNone);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Reload", BRT_BuildReportWindow.LayoutNone))
				{
					BuildReportTool.Options.RefreshOptions();
				}

				GUILayout.EndHorizontal();

				GUILayout.Space(10);
			}

			// === Main Options ===

			GUILayout.Label("Main Options", header1Style, BRT_BuildReportWindow.LayoutNone);


			BuildReportTool.Options.CollectBuildInfo = GUILayout.Toggle(BuildReportTool.Options.CollectBuildInfo,
				"Automatically generate and save a Build Report file after building (does not include batchmode builds)", BRT_BuildReportWindow.LayoutNone);
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);
			GUILayout.Label(
				"Note: For batchmode builds, to create build reports, call <b>BuildReportTool.ReportGenerator.CreateReport()</b> after <b>BuildPipeline.BuildPlayer()</b> in your build scripts.\n\nThe Build Report is automatically saved as an XML file.",
				boxedLabelStyle, LayoutMaxWidth593);
			GUILayout.EndHorizontal();
			GUILayout.Space(10);

			BuildReportTool.Options.AutoShowWindowAfterNormalBuild = GUILayout.Toggle(
				BuildReportTool.Options.AutoShowWindowAfterNormalBuild,
				"Automatically show Build Report Window after building (if it is not open yet)", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.AutoResortAssetsWhenUnityEditorRegainsFocus = GUILayout.Toggle(
				BuildReportTool.Options.AutoResortAssetsWhenUnityEditorRegainsFocus,
				"Re-sort assets automatically whenever the Unity Editor regains focus", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.AllowDeletingOfUsedAssets = GUILayout.Toggle(
				BuildReportTool.Options.AllowDeletingOfUsedAssets,
				"Allow deleting of Used Assets (practice caution!)", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(20);

			BuildReportTool.Options.KeepCopyOfLogOfLastSuccessfulBuild = GUILayout.Toggle(
				BuildReportTool.Options.KeepCopyOfLogOfLastSuccessfulBuild,
				"Keep a copy of the last successful build's Editor.log, which will be re-used if no build was detected in the current Editor.log.",
				BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(20);

			BuildReportTool.Options.UseThreadedReportGeneration = GUILayout.Toggle(
				BuildReportTool.Options.UseThreadedReportGeneration,
				"When generating Build Reports, use a separate thread", BRT_BuildReportWindow.LayoutNone);
			GUILayout.BeginHorizontal(LayoutNoExpandWidth);
			GUILayout.Space(20);
			GUILayout.Label(
				"Note: For batchmode builds, report generation with <b>BuildReportTool.ReportGenerator.CreateReport()</b> is always non-threaded.",
				boxedLabelStyle, LayoutMaxWidth593);
			GUILayout.EndHorizontal();
			GUILayout.Space(10);

			BuildReportTool.Options.UseThreadedFileLoading = GUILayout.Toggle(
				BuildReportTool.Options.UseThreadedFileLoading,
				"When loading Build Report files, use a separate thread", BRT_BuildReportWindow.LayoutNone);

			//GUILayout.Space(20);

			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);

			// === Data to include in the Build Report ===

			GUILayout.Label("Data to include in the Build Report", header1Style, BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(5);

			#region Calculation Level
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Calculation Level: ", BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);
			int newSelectedCalculationLevelIdx = EditorGUILayout.Popup(_selectedCalculationLevelIdx, CalculationTypeLabels,
				"Popup", LayoutWidth300);
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);
			GUILayout.Label(CalculationLevelDescription, LayoutMaxWidth500MinHeight75);
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			if (newSelectedCalculationLevelIdx != _selectedCalculationLevelIdx)
			{
				_selectedCalculationLevelIdx = newSelectedCalculationLevelIdx;
				SetCalculationLevelFromGuiIdx(newSelectedCalculationLevelIdx);
			}
			#endregion

			GUILayout.Space(10);
			GUILayout.Label("Sizes", header2Style, BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.IncludeBuildSizeInReportCreation = GUILayout.Toggle(
				BuildReportTool.Options.IncludeBuildSizeInReportCreation,
				"Get build's file size upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(10);

			//BuildReportTool.Options.GetImportedSizesForUsedAssets = GUILayout.Toggle(BuildReportTool.Options.GetImportedSizesForUsedAssets,
			//	"Get imported sizes of Used Assets upon creation of a build report");

			BuildReportTool.Options.GetImportedSizesForUnusedAssets = GUILayout.Toggle(
				BuildReportTool.Options.GetImportedSizesForUnusedAssets,
				"Get imported sizes of Unused Assets upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.GetSizeBeforeBuildForUsedAssets = GUILayout.Toggle(
				BuildReportTool.Options.GetSizeBeforeBuildForUsedAssets,
				"Get size-before-build of Used Assets upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			#region ShowCalcSizesForUsed
			BuildReportTool.Options.ShowImportedSizeForUsedAssets = GUILayout.Toggle(
				BuildReportTool.Options.ShowImportedSizeForUsedAssets,
				"Show calculated sizes of Used Assets instead of reported sizes", BRT_BuildReportWindow.LayoutNone);

			if (_linkStyle == null)
			{
				_linkStyle = new GUIStyle(GUI.skin.label);
				_linkStyle.normal.textColor = new Color(0.266f, 0.533f, 1);
				_linkStyle.hover.textColor = new Color(0.118f, 0.396f, 1);
				_linkStyle.stretchWidth = false;
				_linkStyle.margin.bottom = 0;
				_linkStyle.padding.bottom = 0;
			}

			if (_textBesideLinkStyle == null)
			{
				_textBesideLinkStyle = new GUIStyle(GUI.skin.label);
				_textBesideLinkStyle.stretchWidth = false;
				_textBesideLinkStyle.margin.right = 0;
				_textBesideLinkStyle.padding.right = 0;
				_textBesideLinkStyle.margin.bottom = 0;
				_textBesideLinkStyle.padding.bottom = 0;
			}

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Note: This option is a workaround for the ", _textBesideLinkStyle, BRT_BuildReportWindow.LayoutNone);
			if (GUILayout.Button("Unity bug with Issue ID 885258", _linkStyle, BRT_BuildReportWindow.LayoutNone))
			{
				Application.OpenURL(
					"https://issuetracker.unity3d.com/issues/unity-reports-incorrect-textures-size-in-the-editor-dot-log-after-building-on-standalone");
			}

			GUILayout.EndHorizontal();
			GUILayout.Label(
				"This bug has already been fixed in Unity 2017.1, 5.5.3p1 and 5.6.0p1. Only enable this if you are affected by the bug.", BRT_BuildReportWindow.LayoutNone);
			#endregion

			GUILayout.Space(15);
			GUILayout.Label("In Unused Assets List", header2Style, BRT_BuildReportWindow.LayoutNone);

			// process unused assets in batches?

			BuildReportTool.Options.ProcessUnusedAssetsInBatches =
				GUILayout.Toggle(BuildReportTool.Options.ProcessUnusedAssetsInBatches, "Process unused assets in batches (Warning: report generation can become slow for large projects when turned off)", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(2);

			// unused assets entries per batch
			GUI.enabled = prevEnabled && BuildReportTool.Options.ProcessUnusedAssetsInBatches;
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Batch count (only process this much files at a time when checking for unused assets):", BRT_BuildReportWindow.LayoutNone);
			string entriesPerBatchInput =
				GUILayout.TextField(BuildReportTool.Options.UnusedAssetsEntriesPerBatch.ToString(), LayoutMinWidth100);
			entriesPerBatchInput =
				Regex.Replace(entriesPerBatchInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(entriesPerBatchInput))
			{
				entriesPerBatchInput = "0";
			}

			BuildReportTool.Options.UnusedAssetsEntriesPerBatch = int.Parse(entriesPerBatchInput);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUI.enabled = prevEnabled;
			if (BuildReportTool.Options.ProcessUnusedAssetsInBatches)
			{
				GUILayout.BeginHorizontal(LayoutNoExpandWidth);
				GUILayout.Space(20);
				GUILayout.Label(
					string.Format("Note: Due to the batch processing, only the first {0:N0} assets at most will be included for the Unused Assets List in the saved Build Report file.", BuildReportTool.Options.UnusedAssetsEntriesPerBatch),
					boxedLabelStyle, LayoutMaxWidth593);
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(10);

			BuildReportTool.Options.IncludeSvnInUnused =
				GUILayout.Toggle(BuildReportTool.Options.IncludeSvnInUnused, Labels.INCLUDE_SVN_LABEL, BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.IncludeGitInUnused =
				GUILayout.Toggle(BuildReportTool.Options.IncludeGitInUnused, Labels.INCLUDE_GIT_LABEL, BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.IncludeBuildReportToolAssetsInUnused =
				GUILayout.Toggle(BuildReportTool.Options.IncludeBuildReportToolAssetsInUnused, Labels.INCLUDE_BRT_LABEL, BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(10);

			// -------------------------------

			#region Ignore Patterns
			if (_ignorePatternList == null)
			{
				_ignorePatternList = new ReorderableList(BuildReportTool.Options.IgnorePatternsForUnused, typeof(SavedOptions.IgnorePattern));
				_ignorePatternList.onAddCallback = OnAddPattern;
				_ignorePatternList.drawHeaderCallback = rect => GUI.Label(rect, "Ignore Patterns for Unused Assets");
				_ignorePatternList.elementHeight = 25;
				_ignorePatternList.drawElementCallback =
					(elementRect, index, isActive, isFocused) =>
					{
						var element = BuildReportTool.Options.IgnorePatternsForUnused[index];

						var radioLeftStyle = GUI.skin.FindStyle("RadioLeft");
						if (radioLeftStyle == null)
						{
							radioLeftStyle = GUI.skin.toggle;
						}
						var radioRightStyle = GUI.skin.FindStyle("RadioRight");
						if (radioRightStyle == null)
						{
							radioRightStyle = GUI.skin.toggle;
						}

						var basicSearchSize = radioLeftStyle.CalcSize(_basicSearchRadioLabel);
						var regexSearchSize = radioRightStyle.CalcSize(_regexSearchRadioLabel);

						int spacing = 3;

						Rect textFieldRect = new Rect(elementRect);
						textFieldRect.y += 4;
						textFieldRect.height = GUI.skin.textField.lineHeight + GUI.skin.textField.padding.vertical + 1;
						textFieldRect.width -= basicSearchSize.x + regexSearchSize.x + spacing;


						if (element.SearchType == SavedOptions.SEARCH_METHOD_REGEX)
						{
							if (_iconValid != null && _iconInvalid != null)
							{
								spacing += 18;
								textFieldRect.width -= spacing;
								EditorGUI.DrawTextureTransparent(new Rect(textFieldRect.xMax + 3, elementRect.y + 5, 16, 16),
									BuildReportTool.Util.IsRegexValid(element.Pattern) ? _iconValid : _iconInvalid);
							}
							else
							{
								spacing += 50;
								textFieldRect.width -= spacing;
								GUI.Label(new Rect(textFieldRect.xMax + 3, elementRect.y + 5, 50, 16),
									BuildReportTool.Util.IsRegexValid(element.Pattern) ? "Valid" : "Invalid");
							}
						}

						element.Pattern = GUI.TextField(textFieldRect, element.Pattern);
						var patternChanged = element.Pattern != BuildReportTool.Options.IgnorePatternsForUnused[index].Pattern;

						Rect basicToggleRect = new Rect(textFieldRect.xMax + spacing, elementRect.y + 2, basicSearchSize.x, basicSearchSize.y);
						var pressedBasic = GUI.Toggle(basicToggleRect,
							element.SearchType == SavedOptions.SEARCH_METHOD_BASIC, _basicSearchRadioLabel, radioLeftStyle);
						var basicChanged = pressedBasic && element.SearchType != SavedOptions.SEARCH_METHOD_BASIC;
						if (basicChanged)
						{
							element.SearchType = SavedOptions.SEARCH_METHOD_BASIC;
						}

						Rect regexToggleRect = new Rect(textFieldRect.xMax + spacing + basicSearchSize.x, elementRect.y + 2, regexSearchSize.x, regexSearchSize.y);
						var pressedRegex = GUI.Toggle(regexToggleRect,
							element.SearchType == SavedOptions.SEARCH_METHOD_REGEX, _regexSearchRadioLabel, radioRightStyle);
						var regexChanged = pressedRegex && element.SearchType != SavedOptions.SEARCH_METHOD_REGEX;
						if (regexChanged)
						{
							element.SearchType = SavedOptions.SEARCH_METHOD_REGEX;
						}

						if (patternChanged || basicChanged || regexChanged)
						{
							BuildReportTool.Options.IgnorePatternsForUnused[index] = element;
							BuildReportTool.Options.SaveOptions();
						}
					};
				_ignorePatternList.onChangedCallback = OnIgnorePatternChanged;
			}

			GUILayout.BeginVertical(LayoutMaxWidth848);
			_ignorePatternList.DoLayoutList();
			GUILayout.EndVertical();
			GUILayout.Space(1);
			GUILayout.Label("Assets that match these search patterns will not be included in the Unused Assets list. The search will be performed on the asset's relative path, starting from the top \"Assets\" folder.",
				boxedLabelStyle, LayoutMaxWidth848);
			#endregion

			// -------------------------------

			GUILayout.Space(15);
			GUILayout.Label("Extra Data to include", header2Style, BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.GetProjectSettings = GUILayout.Toggle(BuildReportTool.Options.GetProjectSettings,
				"Get Unity project settings upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(10);

			BuildReportTool.Options.CalculateAssetDependencies = GUILayout.Toggle(
				BuildReportTool.Options.CalculateAssetDependencies,
				"Calculate Asset Dependencies upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.CalculateAssetDependenciesOnUnusedToo = GUILayout.Toggle(
				BuildReportTool.Options.CalculateAssetDependenciesOnUnusedToo,
				"Include Unused Assets in Asset Dependency calculations", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(10);

			BuildReportTool.Options.CollectTextureImportSettings = GUILayout.Toggle(
				BuildReportTool.Options.CollectTextureImportSettings,
				"Collect Texture Import Settings upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.CollectTextureImportSettingsOnUnusedToo = GUILayout.Toggle(
				BuildReportTool.Options.CollectTextureImportSettingsOnUnusedToo,
				"Include Unused Assets in Texture Import Settings collecting", BRT_BuildReportWindow.LayoutNone);

			GUILayout.Space(10);

			BuildReportTool.Options.CollectMeshData = GUILayout.Toggle(
				BuildReportTool.Options.CollectMeshData,
				"Collect Mesh Data upon creation of a build report", BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.CollectMeshDataOnUnusedToo = GUILayout.Toggle(
				BuildReportTool.Options.CollectMeshDataOnUnusedToo,
				"Include Unused Assets in Mesh Data collecting", BRT_BuildReportWindow.LayoutNone);


			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);
			// === Editor Log File ===

			GUILayout.Label("Editor Log File", header1Style, BRT_BuildReportWindow.LayoutNone);

			// which Editor.log is used
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label(string.Format("{0}{1}: {2}", Labels.EDITOR_LOG_LABEL, BuildReportTool.Util.EditorLogPathOverrideMessage, BuildReportTool.Util.UsedEditorLogPath),
				BRT_BuildReportWindow.LayoutNone);
			if (GUILayout.Button(OPEN_IN_FILE_BROWSER_OS_SPECIFIC_LABEL, BRT_BuildReportWindow.LayoutNone) && BuildReportTool.Util.UsedEditorLogExists)
			{
				BuildReportTool.Util.OpenInFileBrowser(BuildReportTool.Util.UsedEditorLogPath);
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!BuildReportTool.Util.UsedEditorLogExists)
			{
				if (BuildReportTool.Util.IsDefaultEditorLogPathOverridden)
				{
					GUILayout.Label(Labels.OVERRIDE_LOG_NOT_FOUND_MSG, BRT_BuildReportWindow.LayoutNone);
				}
				else
				{
					GUILayout.Label(Labels.DEFAULT_EDITOR_LOG_NOT_FOUND_MSG, BRT_BuildReportWindow.LayoutNone);
				}
			}

			// override which log is opened
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			if (GUILayout.Button(Labels.SET_OVERRIDE_LOG_LABEL, BRT_BuildReportWindow.LayoutNone))
			{
				string filepath = EditorUtility.OpenFilePanel(
					"", // title
					"", // default path
					""); // file type (only one type allowed?)

				if (!string.IsNullOrEmpty(filepath))
				{
					BuildReportTool.Options.EditorLogOverridePath = filepath;
				}
			}

			if (GUILayout.Button(Labels.CLEAR_OVERRIDE_LOG_LABEL, BRT_BuildReportWindow.LayoutNone))
			{
				BuildReportTool.Options.EditorLogOverridePath = "";
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);


			// === Asset Lists ===

			GUILayout.Label("Asset Lists", header1Style, BRT_BuildReportWindow.LayoutNone);


			// top largest used
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Number of Top Largest Used Assets to display in Overview Tab:", BRT_BuildReportWindow.LayoutNone);
			string numberOfTopUsedInput =
				GUILayout.TextField(BuildReportTool.Options.NumberOfTopLargestUsedAssetsToShow.ToString(), LayoutMinWidth100);
			numberOfTopUsedInput =
				Regex.Replace(numberOfTopUsedInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(numberOfTopUsedInput))
			{
				numberOfTopUsedInput = "0";
			}

			BuildReportTool.Options.NumberOfTopLargestUsedAssetsToShow = int.Parse(numberOfTopUsedInput);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();


			// top largest unused
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Number of Top Largest Unused Assets to display in Overview Tab:", BRT_BuildReportWindow.LayoutNone);
			string numberOfTopUnusedInput =
				GUILayout.TextField(BuildReportTool.Options.NumberOfTopLargestUnusedAssetsToShow.ToString(), LayoutMinWidth100);
			numberOfTopUnusedInput =
				Regex.Replace(numberOfTopUnusedInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(numberOfTopUnusedInput))
			{
				numberOfTopUnusedInput = "0";
			}

			BuildReportTool.Options.NumberOfTopLargestUnusedAssetsToShow = int.Parse(numberOfTopUnusedInput);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();


			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);
			GUILayout.Label(
				"Note: To disable the display of Top Largest Assets, use a value of 0.",
				boxedLabelStyle, LayoutMaxWidth525);
			GUILayout.EndHorizontal();

			// --------------------------------------------

			GUILayout.Space(10);
			GUILayout.Label("Texture Data", header2Style, BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Name of File Filter where Texture Import Settings will be shown:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.FileFilterNameForTextureData =
				GUILayout.TextField(BuildReportTool.Options.FileFilterNameForTextureData, LayoutMinWidth200);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(3);
			GUILayout.Label("Texture Import Settings To Show in Asset Lists:", BRT_BuildReportWindow.LayoutNone);
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(10);

			#region Column 1
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowTextureColumnTextureType = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnTextureType, "Texture Type", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.TextureType);
				requestRepaint = true;
			}

			BuildReportTool.Options.ShowTextureColumnIsSRGB = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnIsSRGB, "Is sRGB (Color Texture)", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.IsSRGB);
			}

			BuildReportTool.Options.ShowTextureColumnAlphaSource = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnAlphaSource, "Alpha Source", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.AlphaSource);
			}

			BuildReportTool.Options.ShowTextureColumnAlphaIsTransparency = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnAlphaIsTransparency, "Alpha Is Transparency", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.AlphaIsTransparency);
			}

			BuildReportTool.Options.ShowTextureColumnIgnorePngGamma = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnIgnorePngGamma, "Ignore PNG Gamma", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.IgnorePngGamma);
			}

			BuildReportTool.Options.ShowTextureColumnNPotScale = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnNPotScale, "Non-Power of 2 Scale", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.NPotScale);
			}

			BuildReportTool.Options.ShowTextureColumnIsReadable = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnIsReadable, "Read/Write Enabled", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.IsReadable);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 2
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowTextureColumnMipMapGenerated = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnMipMapGenerated, "Mip Map Generated", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.MipMapGenerated);
			}

			BuildReportTool.Options.ShowTextureColumnMipMapFilter = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnMipMapFilter, "Mip Map Filter", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.MipMapFilter);
			}

			BuildReportTool.Options.ShowTextureColumnStreamingMipMaps = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnStreamingMipMaps, "Streaming Mip Maps", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.StreamingMipMaps);
			}

			BuildReportTool.Options.ShowTextureColumnBorderMipMaps = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnBorderMipMaps, "Border Mip Maps", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.BorderMipMaps);
			}

			BuildReportTool.Options.ShowTextureColumnPreserveCoverageMipMaps = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnPreserveCoverageMipMaps, "Preserve Coverage Mip Maps", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.PreserveCoverageMipMaps);
			}

			BuildReportTool.Options.ShowTextureColumnFadeOutMipMaps = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnFadeOutMipMaps, "Fade Mip Maps", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.FadeOutMipMaps);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 3
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowTextureColumnSpriteImportMode = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnSpriteImportMode, "Sprite Mode", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.SpriteImportMode);
			}

			BuildReportTool.Options.ShowTextureColumnSpritePackingTag = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnSpritePackingTag, "Sprite Packing Tag", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.SpritePackingTag);
			}

			BuildReportTool.Options.ShowTextureColumnSpritePixelsPerUnit = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnSpritePixelsPerUnit, "Sprite Pixels Per Unit", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.SpritePixelsPerUnit);
			}

			BuildReportTool.Options.ShowTextureColumnQualifiesForSpritePacking = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnQualifiesForSpritePacking, "Qualifies for Sprite Packing", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.QualifiesForSpritePacking);
			}

			BuildReportTool.Options.ShowTextureColumnWrapMode = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnWrapMode, "Wrap Mode", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.WrapMode);
			}

			BuildReportTool.Options.ShowTextureColumnWrapModeU = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnWrapModeU, "Wrap Mode U", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.WrapModeU);
			}

			BuildReportTool.Options.ShowTextureColumnWrapModeV = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnWrapModeV, "Wrap Mode V", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.WrapModeV);
			}

			BuildReportTool.Options.ShowTextureColumnWrapModeW = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnWrapModeW, "Wrap Mode W", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.WrapModeW);
			}

			BuildReportTool.Options.ShowTextureColumnFilterMode = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnFilterMode, "Filter Mode", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.FilterMode);
			}

			BuildReportTool.Options.ShowTextureColumnAnisoLevel = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnAnisoLevel, "Anisotropic Filtering Level", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.AnisoLevel);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 4
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowTextureColumnMaxTextureSize = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnMaxTextureSize, "Max Texture Size", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.MaxTextureSize);
			}

			BuildReportTool.Options.ShowTextureColumnResizeAlgorithm = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnResizeAlgorithm, "Resize Algorithm", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.TextureResizeAlgorithm);
			}

			BuildReportTool.Options.ShowTextureColumnTextureFormat = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnTextureFormat, "Texture Format", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.TextureFormat);
			}

			BuildReportTool.Options.ShowTextureColumnCompressionType = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnCompressionType, "Compression Type", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.CompressionType);
			}

			BuildReportTool.Options.ShowTextureColumnCompressionIsCrunched = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnCompressionIsCrunched, "Compression Crunched", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.CompressionIsCrunched);
			}

			BuildReportTool.Options.ShowTextureColumnCompressionQuality = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnCompressionQuality, "Compression Quality", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.CompressionQuality);
			}

			BuildReportTool.Options.ShowTextureColumnImportedWidthAndHeight = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnImportedWidthAndHeight, "Imported Width & Height", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.ImportedWidthAndHeight);
			}

			BuildReportTool.Options.ShowTextureColumnRealWidthAndHeight = GUILayout.Toggle(
				BuildReportTool.Options.ShowTextureColumnRealWidthAndHeight, "Source Width & Height", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.TextureData.GetTooltipTextFromId(BuildReportTool.TextureData.DataId.RealWidthAndHeight);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.FlexibleSpace();

			GUILayout.EndHorizontal();

			// --------------------------------------------

			GUILayout.Space(10);
			GUILayout.Label("Mesh Data", header2Style, BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Name of File Filter where Mesh Data will be shown:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.FileFilterNameForMeshData =
				GUILayout.TextField(BuildReportTool.Options.FileFilterNameForMeshData, LayoutMinWidth200);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(3);
			GUILayout.Label("Texture Import Settings To Show in Asset Lists:", BRT_BuildReportWindow.LayoutNone);
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(10);

			#region Column 1
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowMeshColumnMeshFilterCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnMeshFilterCount, "Non-Skinned Mesh Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.MeshFilterCount);
			}

			BuildReportTool.Options.ShowMeshColumnSkinnedMeshRendererCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnSkinnedMeshRendererCount, "Skinned Mesh Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.SkinnedMeshRendererCount);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 2
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowMeshColumnSubMeshCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnSubMeshCount, "Sub-mesh Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.SubMeshCount);
			}

			BuildReportTool.Options.ShowMeshColumnVertexCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnVertexCount, "Vertex Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.VertexCount);
			}

			BuildReportTool.Options.ShowMeshColumnTriangleCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnTriangleCount, "Face Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.TriangleCount);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 3
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowMeshColumnAnimationType = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnAnimationType, "Animation Type", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.AnimationType);
			}

			BuildReportTool.Options.ShowMeshColumnAnimationClipCount = GUILayout.Toggle(
				BuildReportTool.Options.ShowMeshColumnAnimationClipCount, "Animation Clip Count", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.MeshData.GetTooltipTextFromId(BuildReportTool.MeshData.DataId.AnimationClipCount);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.FlexibleSpace();

			GUILayout.EndHorizontal();

			// --------------------------------------------

			GUILayout.Space(10);
			GUILayout.Label("Prefab Data", header2Style, BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Name of File Filter where Prefab Data will be shown:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.FileFilterNameForPrefabData =
				GUILayout.TextField(BuildReportTool.Options.FileFilterNameForPrefabData, LayoutMinWidth200);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(3);
			GUILayout.Label("Prefab Data To Show in Asset Lists:", BRT_BuildReportWindow.LayoutNone);
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(10);

			#region Column 1
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowPrefabColumnContributeGI = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnContributeGI, "Contribute Global Illumination", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.ContributeGI);
			}

			BuildReportTool.Options.ShowPrefabColumnBatchingStatic = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnBatchingStatic, "Static Batching", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.BatchingStatic);
			}

			BuildReportTool.Options.ShowPrefabColumnReflectionProbeStatic = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnReflectionProbeStatic, "Reflection Probe Static", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.ReflectionProbeStatic);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.Space(30);

			#region Column 2
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowPrefabColumnOccluderStatic = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnOccluderStatic, "Occluder Static", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.OccluderStatic);
			}

			BuildReportTool.Options.ShowPrefabColumnOccludeeStatic = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnOccludeeStatic, "Occludee Static", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.OccludeeStatic);
			}

			BuildReportTool.Options.ShowPrefabColumnNavigationStatic = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnNavigationStatic, "Navigation Static", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.NavigationStatic);
			}

			BuildReportTool.Options.ShowPrefabColumnOffMeshLinkGeneration = GUILayout.Toggle(
				BuildReportTool.Options.ShowPrefabColumnOffMeshLinkGeneration, "Off-Mesh Link Generation", BRT_BuildReportWindow.LayoutNone);
			if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
			{
				_hoveredControlTooltipText = BuildReportTool.PrefabData.GetTooltipTextFromId(BuildReportTool.PrefabData.DataId.OffMeshLinkGeneration);
			}

			GUILayout.EndVertical();
			#endregion

			GUILayout.FlexibleSpace();

			GUILayout.EndHorizontal();

			// --------------------------------------------

			GUILayout.Space(10);
			GUILayout.Label("List Pagination", header2Style, BRT_BuildReportWindow.LayoutNone);

			// pagination length
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Asset List entries per page:", BRT_BuildReportWindow.LayoutNone);
			string pageInput = GUILayout.TextField(BuildReportTool.Options.AssetListPaginationLength.ToString(), LayoutMinWidth100);
			pageInput = Regex.Replace(pageInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(pageInput))
			{
				pageInput = "0";
			}

			BuildReportTool.Options.AssetListPaginationLength = int.Parse(pageInput);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(2);

			// log messages
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Log Messages per page:", BRT_BuildReportWindow.LayoutNone);
			string logMessagesInput =
				GUILayout.TextField(BuildReportTool.Options.LogMessagePaginationLength.ToString(), LayoutMinWidth100);
			logMessagesInput =
				Regex.Replace(logMessagesInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(logMessagesInput))
			{
				logMessagesInput = "0";
			}

			BuildReportTool.Options.LogMessagePaginationLength = int.Parse(logMessagesInput);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);

			GUILayout.Label("Asset Search", header2Style, BRT_BuildReportWindow.LayoutNone);


			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Search Method:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.SearchTypeInt = GUILayout.SelectionGrid(
				BuildReportTool.Options.SearchTypeInt, SearchTypeLabels, 3, BRT_BuildReportWindow.LayoutNone);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			BuildReportTool.Options.SearchFilenameOnly = GUILayout.Toggle(
				BuildReportTool.Options.SearchFilenameOnly,
				"Search through filenames only (ignore path when searching)", BRT_BuildReportWindow.LayoutNone);

			var usingFuzzy = BuildReportTool.Options.SearchType == SearchType.Fuzzy;
			var caseSensitiveLabel = usingFuzzy ? "Case Sensitive Search (Not applicable to Fuzzy Search. Fuzzy Search is always Case Insensitive.)" : "Case Sensitive Search";
			GUI.enabled = prevEnabled && !usingFuzzy;
			BuildReportTool.Options.SearchCaseSensitive = GUILayout.Toggle(
				BuildReportTool.Options.SearchCaseSensitive,
				caseSensitiveLabel, BRT_BuildReportWindow.LayoutNone);
			GUI.enabled = prevEnabled;

			GUILayout.Space(10);
			GUILayout.Label("File Filters", header2Style, BRT_BuildReportWindow.LayoutNone);

			// choose which file filter group to use
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label(Labels.FILTER_GROUP_TO_USE_LABEL, BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.FilterToUseInt = GUILayout.SelectionGrid(BuildReportTool.Options.FilterToUseInt,
				FileFilterToUseType, FileFilterToUseType.Length, BRT_BuildReportWindow.LayoutNone);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			// display which file filter group is used
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label(string.Format("{0}{1}", Labels.FILTER_GROUP_FILE_PATH_LABEL, BuildReportTool.FiltersUsed.GetProperFileFilterGroupToUseFilePath()),
				BRT_BuildReportWindow.LayoutNone); // display path to used file filter
			if (GUILayout.Button(OPEN_IN_FILE_BROWSER_OS_SPECIFIC_LABEL, BRT_BuildReportWindow.LayoutNone))
			{
				BuildReportTool.Util.OpenInFileBrowser(BuildReportTool.FiltersUsed.GetProperFileFilterGroupToUseFilePath());
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			GUILayout.Label("Asset Pinging", header2Style, BRT_BuildReportWindow.LayoutNone);


			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Asset Ping method:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.DoubleClickOnAssetWillPing = GUILayout.SelectionGrid(
				                                                     BuildReportTool.Options.DoubleClickOnAssetWillPing
					                                                     ? 1
					                                                     : 0,
				                                                     AssetPingTypeLabels, 2, LayoutHeight26) == 1;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);

			GUILayout.Label(
				BuildReportTool.Options.DoubleClickOnAssetWillPing
					? "Note: To ping multiple assets, select the assets, and hold Alt while double-clicking one of them."
					: "Note: To ping multiple assets, select the assets, and hold Alt while pressing one of their Ping buttons.",
				boxedLabelStyle, LayoutMaxWidth593);

			GUILayout.EndHorizontal();

			GUILayout.Space(10);

			//AssetUsageLabelTypeLabels
			GUILayout.Label("Asset Usages/Dependencies", header2Style, BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Asset usage labels:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.AssetUsageLabelType = GUILayout.SelectionGrid(
				BuildReportTool.Options.AssetUsageLabelType, AssetUsageLabelTypeLabels, 1, BRT_BuildReportWindow.LayoutNone);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);

			BuildReportTool.Options.ShowAssetPrimaryUsersInTooltipIfAvailable = GUILayout.Toggle(
				BuildReportTool.Options.ShowAssetPrimaryUsersInTooltipIfAvailable,
				"Show end users in asset tooltip (if available)", BRT_BuildReportWindow.LayoutNone);

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);
			GUILayout.Label(
				"Note: \"End users\" are the scenes (or Resources assets) that use a given asset (directly or indirectly), they are the main reason why that asset got included in the build.",
				boxedLabelStyle, LayoutMaxWidth525);
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			GUILayout.Label("Thumbnails", header2Style, BRT_BuildReportWindow.LayoutNone);

			BuildReportTool.Options.ShowTooltipThumbnail = GUILayout.Toggle(
				BuildReportTool.Options.ShowTooltipThumbnail,
				"Show thumbnail in asset tooltip", BRT_BuildReportWindow.LayoutNone);

			GUI.enabled = prevEnabled && BuildReportTool.Options.ShowTooltipThumbnail;

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Show thumbnail when:", BRT_BuildReportWindow.LayoutNone);
			BuildReportTool.Options.ShowThumbnailOnHoverType = GUILayout.SelectionGrid(
				BuildReportTool.Options.ShowThumbnailOnHoverType, ShowThumbnailOnHoverTypeLabels,
				ShowThumbnailOnHoverTypeLabels.Length, LayoutHeight26);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);

			GUILayout.Label("Thumbnail Tooltip Width:", BRT_BuildReportWindow.LayoutNone);
			string tooltipThumbnailWidthInput =
				GUILayout.TextField(BuildReportTool.Options.TooltipThumbnailWidth.ToString(), LayoutMinWidth100);
			tooltipThumbnailWidthInput =
				Regex.Replace(tooltipThumbnailWidthInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(tooltipThumbnailWidthInput))
			{
				tooltipThumbnailWidthInput = "0";
			}

			BuildReportTool.Options.TooltipThumbnailWidth = int.Parse(tooltipThumbnailWidthInput);

			GUILayout.Space(3);

			GUILayout.Label("Height:", BRT_BuildReportWindow.LayoutNone);
			string tooltipThumbnailHeightInput =
				GUILayout.TextField(BuildReportTool.Options.TooltipThumbnailHeight.ToString(), LayoutMinWidth100);
			tooltipThumbnailHeightInput =
				Regex.Replace(tooltipThumbnailHeightInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(tooltipThumbnailHeightInput))
			{
				tooltipThumbnailHeightInput = "0";
			}

			BuildReportTool.Options.TooltipThumbnailHeight = int.Parse(tooltipThumbnailHeightInput);

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();


			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);

			GUILayout.Label("Thumbnail Tooltip Zoomed-in Width:", BRT_BuildReportWindow.LayoutNone);
			string tooltipThumbnailZoomedInWidthInput =
				GUILayout.TextField(BuildReportTool.Options.TooltipThumbnailZoomedInWidth.ToString(), LayoutMinWidth100);
			tooltipThumbnailZoomedInWidthInput =
				Regex.Replace(tooltipThumbnailZoomedInWidthInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(tooltipThumbnailZoomedInWidthInput))
			{
				tooltipThumbnailZoomedInWidthInput = "0";
			}

			BuildReportTool.Options.TooltipThumbnailZoomedInWidth = int.Parse(tooltipThumbnailZoomedInWidthInput);

			GUILayout.Space(3);

			GUILayout.Label("Height:", BRT_BuildReportWindow.LayoutNone);
			string tooltipThumbnailZoomedInHeightInput =
				GUILayout.TextField(BuildReportTool.Options.TooltipThumbnailZoomedInHeight.ToString(), LayoutMinWidth100);
			tooltipThumbnailZoomedInHeightInput =
				Regex.Replace(tooltipThumbnailZoomedInHeightInput, @"[^0-9]", ""); // positive numbers only, no fractions
			if (string.IsNullOrEmpty(tooltipThumbnailZoomedInHeightInput))
			{
				tooltipThumbnailZoomedInHeightInput = "0";
			}

			BuildReportTool.Options.TooltipThumbnailZoomedInHeight = int.Parse(tooltipThumbnailZoomedInHeightInput);

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUI.enabled = prevEnabled;

			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(20);
			GUILayout.Label(
				"Note: Hold Ctrl while a thumbnail tooltip is shown to zoom-in.",
				boxedLabelStyle, LayoutMaxWidth525);
			GUILayout.EndHorizontal();


			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);


			// === Build Report Files ===

			GUILayout.Label("Build Report Files", header1Style, BRT_BuildReportWindow.LayoutNone);

			// where to save build reports (my docs/home, or beside project)
			GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label(Labels.SAVE_PATH_TYPE_LABEL, BRT_BuildReportWindow.LayoutNone);

			if (_saveTypeLabels == null)
			{
				_saveTypeLabels = new[]
					{SAVE_PATH_TYPE_PERSONAL_OS_SPECIFIC_LABEL, Labels.SAVE_PATH_TYPE_PROJECT_LABEL, Labels.SAVE_PATH_TYPE_CUSTOM_LABEL};
			}

			BuildReportTool.Options.SaveType = GUILayout.SelectionGrid(BuildReportTool.Options.SaveType, _saveTypeLabels,
				_saveTypeLabels.Length, BRT_BuildReportWindow.LayoutNone);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);

			if (BuildReportTool.Options.SaveType == BuildReportTool.Options.SAVE_TYPE_CUSTOM)
			{
				GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
				GUILayout.Label(Labels.SAVE_PATH_LABEL, BRT_BuildReportWindow.LayoutNone);
				BuildReportTool.Options.BuildReportCustomOutputPath =
					GUILayout.TextField(BuildReportTool.Options.BuildReportCustomOutputPath, LayoutMinWidth250);
				if (GUILayout.Button("Change", BRT_BuildReportWindow.LayoutNone))
				{
					bool isRelative;
					string fullPath;
					if (Path.IsPathRooted(BuildReportTool.Options.BuildReportCustomOutputPath))
					{
						isRelative = false;
						fullPath = BuildReportTool.Options.BuildReportCustomOutputPath;
					}
					else
					{
						isRelative = true;
						fullPath = Path.GetFullPath(Path.Combine(BuildReportTool.Util.GetProjectPath(Application.dataPath),
							BuildReportTool.Options.BuildReportCustomOutputPath));
					}

					string gotPath = EditorUtility.OpenFolderPanel(
						"Choose output path for Build Reports", fullPath, "");
					if (isRelative && !string.IsNullOrEmpty(gotPath))
					{
						// convert it back to being relative to project folder
						gotPath = BuildReportTool.Util.MakeRelativePath(BuildReportTool.Util.GetProjectPath(Application.dataPath), gotPath);
					}
					if (!string.IsNullOrEmpty(gotPath))
					{
						BuildReportTool.Options.BuildReportCustomOutputPath = gotPath;
					}
				}
				if (GUILayout.Button(OPEN_IN_FILE_BROWSER_OS_SPECIFIC_LABEL, BRT_BuildReportWindow.LayoutNone))
				{
					BuildReportTool.Util.OpenFolderInFileBrowser(BuildReportTool.Options.BuildReportSavePath);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();

				if (!Path.IsPathRooted(BuildReportTool.Options.BuildReportCustomOutputPath))
				{
					GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
					GUILayout.BeginVertical(EditorStyles.helpBox);
					_infoMessage.image = EditorGUIUtility.IconContent("console.infoicon").image;
					string fullPath = Path.GetFullPath(Path.Combine(BuildReportTool.Util.GetProjectPath(Application.dataPath),
						BuildReportTool.Options.BuildReportCustomOutputPath));
					_infoMessage.text = $"Relative path detected. Path is:\n{fullPath}";
					GUILayout.Label(_infoMessage);
					GUILayout.EndVertical();
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}
			}
			else // non-custom save path type
			{
				// build report files save path
				GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
				GUILayout.Label(string.Format("{0}{1}", Labels.SAVE_PATH_LABEL, BuildReportTool.Options.BuildReportSavePath), BRT_BuildReportWindow.LayoutNone);
				if (GUILayout.Button(OPEN_IN_FILE_BROWSER_OS_SPECIFIC_LABEL, BRT_BuildReportWindow.LayoutNone))
				{
					BuildReportTool.Util.OpenFolderInFileBrowser(BuildReportTool.Options.BuildReportSavePath);
				}

				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();

				// change name of build reports folder
				GUILayout.BeginHorizontal(BRT_BuildReportWindow.LayoutNone);
				GUILayout.Label(Labels.SAVE_FOLDER_NAME_LABEL, BRT_BuildReportWindow.LayoutNone);
				BuildReportTool.Options.BuildReportFolderName =
					GUILayout.TextField(BuildReportTool.Options.BuildReportFolderName, LayoutMinWidth250);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(BuildReportTool.Window.Settings.CATEGORY_VERTICAL_SPACING);


			GUILayout.EndVertical();
			GUILayout.Space(20); // extra right padding
			GUILayout.EndHorizontal();

			GUILayout.EndScrollView();

			//if (BuildReportTool.Options.SaveType == BuildReportTool.Options.SAVE_TYPE_PERSONAL)
			//{
			// changed to user's personal folder
			//BuildReportTool.ReportGenerator.ChangeSavePathToUserPersonalFolder();
			//}
			//else if (BuildReportTool.Options.SaveType == BuildReportTool.Options.SAVE_TYPE_PROJECT)
			//{
			// changed to project folder
			//BuildReportTool.ReportGenerator.ChangeSavePathToProjectFolder();
			//}

			if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(_hoveredControlTooltipText))
			{
				_tooltipLabel.text = _hoveredControlTooltipText;
				var tooltipTextStyle = GUI.skin.FindStyle("TooltipText");
				if (tooltipTextStyle == null)
				{
					tooltipTextStyle = GUI.skin.label;
				}

				const int MAX_TOOLTIP_WIDTH = 240;
				var tooltipSize = tooltipTextStyle.CalcSize(_tooltipLabel);
				if (tooltipSize.x > MAX_TOOLTIP_WIDTH)
				{
					tooltipSize.x = MAX_TOOLTIP_WIDTH;
					tooltipSize.y = tooltipTextStyle.CalcHeight(_tooltipLabel, tooltipSize.x);
				}

				var tooltipRect = BRT_BuildReportWindow.DrawTooltip(position, tooltipSize.x, tooltipSize.y, 5);
				GUI.Label(tooltipRect, _tooltipLabel, tooltipTextStyle);
			}
		}

		static void OnAddPattern(ReorderableList list)
		{
			SavedOptions.IgnorePattern newEntry;
			newEntry.Pattern = "";
			newEntry.SearchType = SavedOptions.SEARCH_METHOD_BASIC;
			BuildReportTool.Options.IgnorePatternsForUnused.Add(newEntry);
		}

		static void OnIgnorePatternChanged(ReorderableList list)
		{
			BuildReportTool.Options.SaveOptions();
		}
	}
}