//#define BRT_SHOW_MINOR_WARNINGS

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Threading;
using BuildReportTool;
using BuildReportTool.Window;
using UnityEditor.U2D;
using UnityEngine.U2D;

// can't put this in a namespace since older versions of Unity doesn't allow that
public class BRT_BuildReportWindow : EditorWindow
{
	const int ICON_WIDTH = 16;
	public const int ICON_WIDTH_WITH_PADDING = 20;
	public const int LIST_HEIGHT = 20;

	const int TOOLTIP_END_USERS_MAX_COUNT = 10;
	const int TOOLTIP_PADDING_T = 2;
	const int TOOLTIP_PADDING_B = 4;
	const int TOOLTIP_PADDING_L = 2;
	const int TOOLTIP_PADDING_R = 2;

	public static Vector2 IconSize = new Vector2(15, 15);

	public static readonly GUILayoutOption[] LayoutNone = { };

	public static readonly GUILayoutOption[] LayoutListHeight =
		{GUILayout.Height(LIST_HEIGHT), GUILayout.ExpandHeight(false)};

	public static readonly GUILayoutOption[] LayoutListHeightMinWidth90 =
		{GUILayout.MinWidth(90), GUILayout.Height(LIST_HEIGHT)};

	public static readonly GUILayoutOption[] LayoutNoExpandWidth =
		{GUILayout.ExpandWidth(false)};
	public static readonly GUILayoutOption[] LayoutExpandWidth =
		{GUILayout.ExpandWidth(true)};

	public static readonly GUILayoutOption[] LayoutMinHeight30 =
		{GUILayout.MinHeight(30), GUILayout.ExpandHeight(true)};
	public static readonly GUILayoutOption[] LayoutHeight11 = {GUILayout.Height(11)};
	public static readonly GUILayoutOption[] LayoutHeight18 = {GUILayout.Height(18)};
	public static readonly GUILayoutOption[] LayoutHeight21 = {GUILayout.Height(21)};
	public static readonly GUILayoutOption[] LayoutHeight25 = {GUILayout.Height(25)};
	public static readonly GUILayoutOption[] LayoutMinWidth200 = {GUILayout.MinWidth(200)};
	public static readonly GUILayoutOption[] LayoutPingButton = {GUILayout.Width(37)};
	public static readonly GUILayoutOption[] LayoutIconWidth = {GUILayout.Width(ICON_WIDTH)};
	public static readonly GUILayoutOption[] Layout20x16 = {GUILayout.Width(20), GUILayout.Height(16)};
	public static readonly GUILayoutOption[] Layout20x25 = {GUILayout.Width(20), GUILayout.Height(25)};
	public static readonly GUILayoutOption[] Layout20x30 = {GUILayout.Width(20), GUILayout.Height(30)};
	public static readonly GUILayoutOption[] Layout28x30 = {GUILayout.Width(28), GUILayout.Height(30)};
	public static readonly GUILayoutOption[] Layout100To400x30 = {GUILayout.MinWidth(100), GUILayout.MaxWidth(400), GUILayout.Height(30)};
	public static readonly GUILayoutOption[] LayoutTo100x30 = {GUILayout.MaxWidth(100), GUILayout.Height(30)};

	public static readonly GUILayoutOption[] Layout100x30 = {GUILayout.MinWidth(100), GUILayout.Height(30), GUILayout.ExpandWidth(true)};
	public static readonly GUILayoutOption[] LayoutMinWidth44 = {GUILayout.MinWidth(44)};
	public static readonly GUILayoutOption[] LayoutMinWidth63 = {GUILayout.MinWidth(63)};
	public static readonly GUILayoutOption[] LayoutMinWidth84 = {GUILayout.MinWidth(84)};
	public static readonly GUILayoutOption[] LayoutMaxWidth500 = {GUILayout.MaxWidth(500)};

	public const string STYLE_BREADCRUMB_LEFT = "GUIEditor.BreadcrumbLeft";
	public const string STYLE_BREADCRUMB_MID = "GUIEditor.BreadcrumbMid";

	void OnDisable()
	{
		ForceStopFileLoadThread();
		IsOpen = false;
	}

	void OnFocus()
	{
		if (BuildReportTool.Options.AutoResortAssetsWhenUnityEditorRegainsFocus)
		{
			_usedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_unusedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);

			// check if configured file filters changed and only then do we need to recategorize

			if (BuildReportTool.Options.ShouldUseConfiguredFileFilters())
			{
				RecategorizeDisplayedBuildInfo();
			}
		}
	}

	void OnEnable()
	{
		//Debug.Log("BuildReportWindow.OnEnable() " + System.DateTime.Now);

#if UNITY_5_6_OR_NEWER
		wantsMouseEnterLeaveWindow = true;
#endif
		wantsMouseMove = true;

		IsOpen = true;

		InitGUISkin();

		if (BuildReportTool.Util.BuildInfoHasContents(_buildInfo))
		{
			//Debug.Log("recompiled " + _buildInfo.SavedPath);
			if (!string.IsNullOrEmpty(_buildInfo.SavedPath))
			{
				BuildReportTool.BuildInfo loadedBuild = BuildReportTool.Util.OpenSerializedBuildInfo(_buildInfo.SavedPath);
				if (BuildReportTool.Util.BuildInfoHasContents(loadedBuild))
				{
					_buildInfo = loadedBuild;
				}
			}
			else
			{
				if (_buildInfo.HasUsedAssets)
				{
					_buildInfo.UsedAssets.AssignPerCategoryList(
						BuildReportTool.ReportGenerator.SegregateAssetSizesPerCategory(_buildInfo.UsedAssets.All,
							_buildInfo.FileFilters));
				}

				if (_buildInfo.HasUnusedAssets)
				{
					_buildInfo.UnusedAssets.AssignPerCategoryList(
						BuildReportTool.ReportGenerator.SegregateAssetSizesPerCategory(_buildInfo.UnusedAssets.All,
							_buildInfo.FileFilters));
				}
			}
		}

		_usedAssetsScreen.SetListToDisplay(BuildReportTool.Window.Screen.AssetList.ListToDisplay.UsedAssets);
		_unusedAssetsScreen.SetListToDisplay(BuildReportTool.Window.Screen.AssetList.ListToDisplay.UnusedAssets);

		_overviewScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_buildSettingsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_buildStepsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_sizeStatsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_usedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_unusedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_extraDataScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);

		_optionsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_helpScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
	}

	double _lastTime;

	void OnInspectorUpdate()
	{
		var deltaTime = EditorApplication.timeSinceStartup - _lastTime;
		_lastTime = EditorApplication.timeSinceStartup;

		if (IsInUsedAssetsCategory)
		{
			_usedAssetsScreen.Update(EditorApplication.timeSinceStartup, deltaTime, _buildInfo, _assetDependencies);
		}
		else if (IsInUnusedAssetsCategory)
		{
			_unusedAssetsScreen.Update(EditorApplication.timeSinceStartup, deltaTime, _buildInfo, _assetDependencies);
		}

		if (_buildInfo != null && BuildReportTool.ReportGenerator.IsFinishedGettingValues)
		{
			OnFinishGeneratingBuildReport();
		}

		// if Unity Editor has finished making a build and we are scheduled to create a Build Report...
		if (!BuildReportTool.ReportGenerator.IsStillGettingValues &&
		    !EditorApplication.isCompiling &&
		    BuildReportTool.Util.ShouldGetBuildReportNow)
		{
			//Debug.Log("BuildReportWindow getting build info right after the build... " + System.DateTime.Now);
			Refresh(true);
		}

		if (_finishedOpeningFromThread)
		{
			OnFinishOpeningBuildReportFile();
		}
	}

	void Update()
	{
		if (_buildInfo != null)
		{
			if (_buildInfo.RequestedToRefresh)
			{
				Repaint();
				_buildInfo.FlagFinishedRefreshing();
			}
		}
	}

	// ==========================================================================================
	// sub-screens

	readonly BuildReportTool.Window.Screen.Overview _overviewScreen = new BuildReportTool.Window.Screen.Overview();

	readonly BuildReportTool.Window.Screen.BuildSettings _buildSettingsScreen =
		new BuildReportTool.Window.Screen.BuildSettings();

	readonly BuildReportTool.Window.Screen.BuildStepsScreen _buildStepsScreen =
		new BuildReportTool.Window.Screen.BuildStepsScreen();

	readonly BuildReportTool.Window.Screen.SizeStats _sizeStatsScreen = new BuildReportTool.Window.Screen.SizeStats();
	readonly BuildReportTool.Window.Screen.AssetList _usedAssetsScreen = new BuildReportTool.Window.Screen.AssetList();
	readonly BuildReportTool.Window.Screen.AssetList _unusedAssetsScreen = new BuildReportTool.Window.Screen.AssetList();
	readonly BuildReportTool.Window.Screen.ExtraData _extraDataScreen = new BuildReportTool.Window.Screen.ExtraData();

	readonly BuildReportTool.Window.Screen.Options _optionsScreen = new BuildReportTool.Window.Screen.Options();
	readonly BuildReportTool.Window.Screen.Help _helpScreen = new BuildReportTool.Window.Screen.Help();


	// ==========================================================================================


	public static string GetValueMessage { set; get; }

	static bool _loadingValuesFromThread;

	public static bool LoadingValuesFromThread
	{
		get { return _loadingValuesFromThread; }
	}

	static bool _noGuiSkinFound;

	/// <summary>
	/// The Build Report we're displaying.
	/// </summary>
	static BuildReportTool.BuildInfo _buildInfo;

	/// <summary>
	/// The Asset Dependencies data being used
	/// for whichever Build Report is displayed.
	/// </summary>
	static BuildReportTool.AssetDependencies _assetDependencies;

	/// <summary>
	/// The TextureData being used
	/// for whichever Build Report is displayed.
	/// </summary>
	static BuildReportTool.TextureData _textureData;

	/// <summary>
	/// The MeshData being used
	/// for whichever Build Report is displayed.
	/// </summary>
	static BuildReportTool.MeshData _meshData;

	/// <summary>
	/// The PrefabData being used
	/// for whichever Build Report is displayed.
	/// </summary>
	static BuildReportTool.PrefabData _prefabData;

	static BuildReportTool.UnityBuildReport _unityBuildReport;

	static ExtraData _extraData;

	public const bool FORCE_USE_DARK_SKIN = false;

	GUISkin _usedSkin = null;

	public static bool IsOpen { get; set; }

	public static bool ZoomedInThumbnails { get; set; }
	public static bool ShowThumbnailsWithAlphaBlend { get; set; }

	static Vector2 _lastMousePos;
	static bool _lastMouseMoved;

	public enum AssetInfoType
	{
		None,
		InStreamingAssetsFolder,
		InAPackage,
		InAResourcesFolder,
		ASceneInBuild,
	}

	/// <summary>
	/// Rect of whatever asset is hovered on
	/// </summary>
	public static Rect HoveredAssetEntryRect;

	/// <summary>
	/// Asset path of whatever asset is hovered
	/// </summary>
	public static string HoveredAssetEntryPath;

	public static List<GUIContent> HoveredAssetEndUsers;

	public static void UpdateHoveredAsset(string hoveredAssetPath, Rect hoveredAssetRect, bool showingUsedAssets,
		BuildInfo buildReportToDisplay, AssetDependencies assetDependencies)
	{
		var alreadyUsingSameAssetPath =
			hoveredAssetPath.Equals(HoveredAssetEntryPath, StringComparison.OrdinalIgnoreCase);

		if (!alreadyUsingSameAssetPath)
		{
			HoveredAssetEntryPath = hoveredAssetPath;
		}

		// even if the new hovered asset to assign is the same as the current one,
		// its rect might have moved, so we always assign it
		HoveredAssetEntryRect = hoveredAssetRect;

		if (BuildReportTool.Options.ShowAssetPrimaryUsersInTooltipIfAvailable && !alreadyUsingSameAssetPath)
		{
			UpdateHoveredAssetType(hoveredAssetPath, showingUsedAssets);

			if (HoveredAssetIsASceneInBuild)
			{
				UpdateSceneInBuildLabel(SceneIsInBuildLabel,
					buildReportToDisplay.ScenesInBuild, HoveredAssetEntryPath);
			}

			AssignHoveredAssetEndUsers(assetDependencies);
		}
	}

	public static void UpdateSceneInBuildLabel(GUIContent destination, BuildInfo.SceneInBuild[] scenesInBuild,
		string scenePath)
	{
		var foundSceneInBuild = false;
		for (int sceneN = 0, sceneLen = scenesInBuild.Length; sceneN < sceneLen; ++sceneN)
		{
			if (scenesInBuild[sceneN].Path.Equals(scenePath, StringComparison.OrdinalIgnoreCase))
			{
				destination.text = string.Format(SCENE_IN_BUILD_LABEL_WITH_INDEX_FORMAT, sceneN.ToString());
				foundSceneInBuild = true;
				break;
			}
		}

		if (!foundSceneInBuild)
		{
			// This doesn't make sense though. If we're showing used assets,
			// the scene *should* be in the ScenesInBuild array.
			//
			// One possibility is that the user might have had a custom build script
			// that was manipulating the values in UnityEditor.EditorBuildSettings.scenes
			// after Build Report generation recorded it into the ScenesInBuild array.
			//
			destination.text = SCENE_IN_BUILD_LABEL;
		}
	}

	static List<GUIContent> GetEndUserLabelsFor(AssetDependencies assetDependencies, string assetPath)
	{
		if (string.IsNullOrEmpty(assetPath) || assetDependencies == null)
		{
			return null;
		}

		List<GUIContent> endUsersListToUse = null;

		var dependencies = assetDependencies.GetAssetDependencies();
		if (dependencies.ContainsKey(assetPath))
		{
			var entry = dependencies[assetPath];
			if (entry != null)
			{
				endUsersListToUse = entry.GetEndUserLabels();
			}
		}

		return endUsersListToUse;
	}

	static void AssignHoveredAssetEndUsers(AssetDependencies assetDependencies)
	{
		BuildReportTool.AssetDependencies.PopulateAssetEndUsers(HoveredAssetEntryPath, assetDependencies);
		HoveredAssetEndUsers = GetEndUserLabelsFor(assetDependencies, HoveredAssetEntryPath);
	}

	static AssetInfoType _hoveredAssetType = AssetInfoType.None;

	static void UpdateHoveredAssetType(string hoveredAssetPath, bool showingUsedAssets)
	{
		if (hoveredAssetPath.IsInStreamingAssetsFolder())
		{
			_hoveredAssetType = AssetInfoType.InStreamingAssetsFolder;
		}
		else if (hoveredAssetPath.IsInPackagesFolder())
		{
			_hoveredAssetType = AssetInfoType.InAPackage;
		}
		else if (hoveredAssetPath.IsInResourcesFolder())
		{
			_hoveredAssetType = AssetInfoType.InAResourcesFolder;
		}
		else if (hoveredAssetPath.IsSceneFile() && showingUsedAssets)
		{
			_hoveredAssetType = AssetInfoType.ASceneInBuild;
		}
		else
		{
			_hoveredAssetType = AssetInfoType.None;
		}
	}

	public static bool HoveredAssetIsASceneInBuild
	{
		get { return _hoveredAssetType == AssetInfoType.ASceneInBuild; }
	}

	public static bool ShouldHoveredAssetShowEndUserTooltip(AssetDependencies assetDependencies)
	{
		if (_hoveredAssetType != AssetInfoType.None)
		{
			return true;
		}

		List<GUIContent> endUsersListToUse = GetEndUserLabelsFor(assetDependencies, HoveredAssetEntryPath);

		return endUsersListToUse != null && endUsersListToUse.Count > 0;
	}

	public static GUIContent GetAppropriateEndUserLabelForHovered()
	{
		switch (_hoveredAssetType)
		{
			case AssetInfoType.InAPackage:
			{
				if (HoveredAssetEndUsers != null && HoveredAssetEndUsers.Count > 0)
				{
					if (HoveredAssetEndUsers[0].text.IsAnAssembly())
					{
						return InPackagesButAlsoUsedInLabel;
					}
					return InPackagesButAlsoUsedByLabel;
				}
				else
				{
					return InPackagesLabel;
				}
			}

			case AssetInfoType.InStreamingAssetsFolder:
				return InStreamingAssetsLabel;

			case AssetInfoType.InAResourcesFolder:
			{
				if (HoveredAssetEndUsers != null && HoveredAssetEndUsers.Count > 0)
				{
					return AResourcesAssetButAlsoUsedByLabel;
				}
				else
				{
					return AResourcesAssetLabel;
				}
			}

			case AssetInfoType.ASceneInBuild:
				return SceneIsInBuildLabel;

			default:
				if (HoveredAssetEndUsers != null && HoveredAssetEndUsers.Count > 0 && HoveredAssetEndUsers[0].text.IsAnAssembly())
				{
					return IsInLabel;
				}

				return UsedByLabel;
		}
	}

	/// <summary>
	/// "Used by:"
	/// </summary>
	static readonly GUIContent UsedByLabel = new GUIContent("Used by:");

	static readonly GUIContent IsInLabel = new GUIContent("Is compiled into:");

	static readonly GUIContent PlusMore = new GUIContent("...plus x more");

	/// <summary>
	/// "Asset is in a Resources folder"
	/// </summary>
	static readonly GUIContent AResourcesAssetLabel = new GUIContent("Asset is in a Resources folder");

	/// <summary>
	/// "Asset is in the StreamingAssets folder"
	/// </summary>
	static readonly GUIContent InStreamingAssetsLabel = new GUIContent("File is in the StreamingAssets folder");

	/// <summary>
	/// "A Resources asset, but also used by:"
	/// </summary>
	static readonly GUIContent AResourcesAssetButAlsoUsedByLabel =
		new GUIContent("A Resources asset, but also used by:");

	/// <summary>
	/// "Scene is in build"
	/// </summary>
	public static readonly GUIContent SceneIsInBuildLabel = new GUIContent("Scene is in build");

	const string SCENE_IN_BUILD_LABEL_WITH_INDEX_FORMAT = "Scene is in build at index <b>{0}</b>";
	const string SCENE_IN_BUILD_LABEL = "Scene is in build";

	static readonly GUIContent InPackagesLabel = new GUIContent("Asset is from the Packages folder");

	static readonly GUIContent InPackagesButAlsoUsedByLabel = new GUIContent("Asset is from the Packages folder\n<size=7>\n</size>Used by:");
	static readonly GUIContent InPackagesButAlsoUsedInLabel = new GUIContent("Asset is from the Packages folder\n<size=7>\n</size>Is compiled into:");

	Texture2D _toolbarIconLog;
	Texture2D _toolbarIconOpen;
	Texture2D _toolbarIconSave;
	Texture2D _toolbarIconOptions;
	Texture2D _toolbarIconHelp;


	GUIContent _toolbarLabelLog;
	GUIContent _toolbarLabelOpen;
	GUIContent _toolbarLabelSave;
	GUIContent _toolbarLabelOptions;
	GUIContent _toolbarLabelHelp;


	void RecategorizeDisplayedBuildInfo()
	{
		if (BuildReportTool.Util.BuildInfoHasContents(_buildInfo))
		{
			BuildReportTool.ReportGenerator.RecategorizeAssetList(_buildInfo);
		}
	}

	static void GetFromNative(GUIStyle ownStyle, GUIStyle nativeStyle, out GUIStyle styleToAssign, string desiredName = null)
	{
		if (nativeStyle == null)
		{
			styleToAssign = null;
			return;
		}

		if (ownStyle == null)
		{
			// make our own copy of the native skin
			styleToAssign = new GUIStyle(nativeStyle);
			if (!string.IsNullOrEmpty(desiredName))
			{
				styleToAssign.name = desiredName;
			}
		}
		else
		{
			styleToAssign = null;

			if (!string.IsNullOrEmpty(desiredName))
			{
				ownStyle.name = desiredName;
			}

			// ensure our skin uses Unity's builtin look
			ownStyle.normal.background = nativeStyle.normal.background;
			ownStyle.hover.background = nativeStyle.hover.background;
			ownStyle.active.background = nativeStyle.active.background;
			ownStyle.onNormal.background = nativeStyle.onNormal.background;
			ownStyle.onHover.background = nativeStyle.onHover.background;
			ownStyle.onActive.background = nativeStyle.onActive.background;

#if UNITY_5_6_OR_NEWER
			if (nativeStyle.normal.scaledBackgrounds != null && nativeStyle.normal.scaledBackgrounds.Length > 0)
			{
				ownStyle.normal.scaledBackgrounds = new Texture2D[nativeStyle.normal.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.normal.scaledBackgrounds.Length; i++)
				{
					ownStyle.normal.scaledBackgrounds[i] = nativeStyle.normal.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.normal.scaledBackgrounds != null)
				{
					ownStyle.normal.scaledBackgrounds = null;
				}
			}

			if (nativeStyle.hover.scaledBackgrounds != null && nativeStyle.hover.scaledBackgrounds.Length > 0)
			{
				ownStyle.hover.scaledBackgrounds = new Texture2D[nativeStyle.hover.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.hover.scaledBackgrounds.Length; i++)
				{
					ownStyle.hover.scaledBackgrounds[i] = nativeStyle.hover.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.hover.scaledBackgrounds != null)
				{
					ownStyle.hover.scaledBackgrounds = null;
				}
			}

			if (nativeStyle.active.scaledBackgrounds != null && nativeStyle.active.scaledBackgrounds.Length > 0)
			{
				ownStyle.active.scaledBackgrounds = new Texture2D[nativeStyle.active.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.active.scaledBackgrounds.Length; i++)
				{
					ownStyle.active.scaledBackgrounds[i] = nativeStyle.active.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.active.scaledBackgrounds != null)
				{
					ownStyle.active.scaledBackgrounds = null;
				}
			}

			if (nativeStyle.onNormal.scaledBackgrounds != null && nativeStyle.onNormal.scaledBackgrounds.Length > 0)
			{
				ownStyle.onNormal.scaledBackgrounds = new Texture2D[nativeStyle.onNormal.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.onNormal.scaledBackgrounds.Length; i++)
				{
					ownStyle.onNormal.scaledBackgrounds[i] = nativeStyle.onNormal.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.onNormal.scaledBackgrounds != null)
				{
					ownStyle.onNormal.scaledBackgrounds = null;
				}
			}

			if (nativeStyle.onHover.scaledBackgrounds != null && nativeStyle.onHover.scaledBackgrounds.Length > 0)
			{
				ownStyle.onHover.scaledBackgrounds = new Texture2D[nativeStyle.onHover.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.onHover.scaledBackgrounds.Length; i++)
				{
					ownStyle.onHover.scaledBackgrounds[i] = nativeStyle.onHover.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.onHover.scaledBackgrounds != null)
				{
					ownStyle.onHover.scaledBackgrounds = null;
				}
			}

			if (nativeStyle.onActive.scaledBackgrounds != null && nativeStyle.onActive.scaledBackgrounds.Length > 0)
			{
				ownStyle.onActive.scaledBackgrounds = new Texture2D[nativeStyle.onActive.scaledBackgrounds.Length];
				for (int i = 0; i < nativeStyle.onActive.scaledBackgrounds.Length; i++)
				{
					ownStyle.onActive.scaledBackgrounds[i] = nativeStyle.onActive.scaledBackgrounds[i];
				}
			}
			else
			{
				if (ownStyle.onActive.scaledBackgrounds != null)
				{
					ownStyle.onActive.scaledBackgrounds = null;
				}
			}
#endif

			ownStyle.normal.textColor = nativeStyle.normal.textColor;
			ownStyle.hover.textColor = nativeStyle.hover.textColor;
			ownStyle.active.textColor = nativeStyle.active.textColor;
			ownStyle.onNormal.textColor = nativeStyle.onNormal.textColor;
			ownStyle.onHover.textColor = nativeStyle.onHover.textColor;
			ownStyle.onActive.textColor = nativeStyle.onActive.textColor;

			ownStyle.border.top = nativeStyle.border.top;
			ownStyle.border.bottom = nativeStyle.border.bottom;
			ownStyle.border.left = nativeStyle.border.left;
			ownStyle.border.right = nativeStyle.border.right;

			ownStyle.margin.top = nativeStyle.margin.top;
			ownStyle.margin.bottom = nativeStyle.margin.bottom;
			ownStyle.margin.left = nativeStyle.margin.left;
			ownStyle.margin.right = nativeStyle.margin.right;

			ownStyle.padding.top = nativeStyle.padding.top;
			ownStyle.padding.bottom = nativeStyle.padding.bottom;
			ownStyle.padding.left = nativeStyle.padding.left;
			ownStyle.padding.right = nativeStyle.padding.right;

			ownStyle.overflow.top = nativeStyle.overflow.top;
			ownStyle.overflow.bottom = nativeStyle.overflow.bottom;
			ownStyle.overflow.left = nativeStyle.overflow.left;
			ownStyle.overflow.right = nativeStyle.overflow.right;

			ownStyle.font = nativeStyle.font;
			ownStyle.fontStyle = nativeStyle.fontStyle;
			ownStyle.alignment = nativeStyle.alignment;

			ownStyle.richText = nativeStyle.richText;
			ownStyle.wordWrap = nativeStyle.wordWrap;

			ownStyle.contentOffset = nativeStyle.contentOffset;

			ownStyle.fixedWidth = nativeStyle.fixedWidth;
			ownStyle.fixedHeight = nativeStyle.fixedHeight;

			ownStyle.stretchWidth = nativeStyle.stretchWidth;
			ownStyle.stretchHeight = nativeStyle.stretchHeight;
		}
	}


	void InitGUISkin()
	{
		string guiSkinToUse;
		if (EditorGUIUtility.isProSkin || FORCE_USE_DARK_SKIN)
		{
			guiSkinToUse = BuildReportTool.Window.Settings.DARK_GUI_SKIN_FILENAME;
		}
		else
		{
			guiSkinToUse = BuildReportTool.Window.Settings.DEFAULT_GUI_SKIN_FILENAME;
		}

		// try default path
		_usedSkin = AssetDatabase.LoadAssetAtPath(
			            string.Format("{0}/GUI/{1}", BuildReportTool.Options.BUILD_REPORT_TOOL_DEFAULT_PATH, guiSkinToUse),
			            typeof(GUISkin)) as GUISkin;

		if (_usedSkin == null)
		{
#if BRT_SHOW_MINOR_WARNINGS
			Debug.LogWarning(BuildReportTool.Options.BUILD_REPORT_PACKAGE_MOVED_MSG);
#endif

			string folderPath = BuildReportTool.Util.FindAssetFolder(Application.dataPath,
				BuildReportTool.Options.BUILD_REPORT_TOOL_DEFAULT_FOLDER_NAME);
			if (!string.IsNullOrEmpty(folderPath))
			{
				folderPath = folderPath.Replace('\\', '/');
				int assetsIdx = folderPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
				if (assetsIdx != -1)
				{
					folderPath = folderPath.Substring(assetsIdx + 8, folderPath.Length - assetsIdx - 8);
				}
				//Debug.Log(folderPath);

				_usedSkin = AssetDatabase.LoadAssetAtPath(string.Format("Assets/{0}/GUI/{1}", folderPath, guiSkinToUse),
					            typeof(GUISkin)) as GUISkin;
			}
			else
			{
				Debug.LogError(BuildReportTool.Options.BUILD_REPORT_PACKAGE_MISSING_MSG);
			}

			//Debug.Log("_usedSkin " + (_usedSkin != null));
		}

		if (_usedSkin != null)
		{
			// weirdo enum labels used to get either light or dark skin
			// (https://forum.unity.com/threads/editorguiutility-getbuiltinskin-not-working-correctly-in-unity-4-3.211504/#post-1430038)
			GUISkin nativeSkin =
				EditorGUIUtility.GetBuiltinSkin(EditorGUIUtility.isProSkin ? EditorSkin.Scene : EditorSkin.Inspector);

			if (!(EditorGUIUtility.isProSkin || FORCE_USE_DARK_SKIN))
			{
				_usedSkin.verticalScrollbar = nativeSkin.verticalScrollbar;
				_usedSkin.verticalScrollbarThumb = nativeSkin.verticalScrollbarThumb;
				_usedSkin.verticalScrollbarUpButton = nativeSkin.verticalScrollbarUpButton;
				_usedSkin.verticalScrollbarDownButton = nativeSkin.verticalScrollbarDownButton;

				_usedSkin.horizontalScrollbar = nativeSkin.horizontalScrollbar;
				_usedSkin.horizontalScrollbarThumb = nativeSkin.horizontalScrollbarThumb;
				_usedSkin.horizontalScrollbarLeftButton = nativeSkin.horizontalScrollbarLeftButton;
				_usedSkin.horizontalScrollbarRightButton = nativeSkin.horizontalScrollbarRightButton;

				// change the toggle skin to use the Unity builtin look, but keep our settings
				GUIStyle toggleSaved = new GUIStyle(_usedSkin.toggle);

				// make our own copy of the native skin toggle so that editing it won't affect the rest of the editor GUI
				GUIStyle nativeToggleCopy = new GUIStyle(nativeSkin.toggle);

				_usedSkin.toggle = nativeToggleCopy;
				_usedSkin.toggle.font = toggleSaved.font;
				_usedSkin.toggle.fontSize = toggleSaved.fontSize;
				_usedSkin.toggle.border = toggleSaved.border;
				_usedSkin.toggle.margin = toggleSaved.margin;
				_usedSkin.toggle.padding = toggleSaved.padding;
				_usedSkin.toggle.overflow = toggleSaved.overflow;
				_usedSkin.toggle.contentOffset = toggleSaved.contentOffset;

				_usedSkin.box = nativeSkin.box;
				_usedSkin.label = nativeSkin.label;
				_usedSkin.textField = nativeSkin.textField;
				_usedSkin.button = nativeSkin.button;

				_usedSkin.label.wordWrap = true;
			}

			var miniButtonStyle = _usedSkin.FindStyle("MiniButton");
			if (miniButtonStyle != null)
			{
				if (miniButtonStyle.normal.background == null)
				{
					miniButtonStyle.normal.background = nativeSkin.button.normal.background;
					miniButtonStyle.active.background = nativeSkin.button.active.background;
					miniButtonStyle.onNormal.background = nativeSkin.button.onNormal.background;
					miniButtonStyle.onActive.background = nativeSkin.button.onActive.background;
				}
			}

			// ----------------------------------------------------
			// Add styles we need

			var nativeReorderableListDragHandle = nativeSkin.GetStyle("RL DragHandle");
			var nativeReorderableListHeader = nativeSkin.GetStyle("RL Header");
			var nativeReorderableListFooter = nativeSkin.GetStyle("RL Footer");
			var nativeReorderableListBg = nativeSkin.GetStyle("RL Background");
			var nativeReorderableListFooterButton = nativeSkin.GetStyle("RL FooterButton");
			var nativeReorderableListElement = nativeSkin.GetStyle("RL Element");
			var nativeReorderableListEmptyHeader = nativeSkin.FindStyle("RL Empty Header");

			var reorderableListDragHandle = _usedSkin.FindStyle("RL DragHandle");
			var reorderableListHeader = _usedSkin.FindStyle("RL Header");
			var reorderableListFooter = _usedSkin.FindStyle("RL Footer");
			var reorderableListBg = _usedSkin.FindStyle("RL Background");
			var reorderableListFooterButton = _usedSkin.FindStyle("RL FooterButton");
			var reorderableListElement = _usedSkin.FindStyle("RL Element");
			var reorderableListEmptyHeader = _usedSkin.FindStyle("RL Empty Header");

			GUIStyle reorderableListDragHandleToAssign;
			GUIStyle reorderableListHeaderToAssign;
			GUIStyle reorderableListFooterToAssign;
			GUIStyle reorderableListBgToAssign;
			GUIStyle reorderableListFooterButtonToAssign;
			GUIStyle reorderableListElementToAssign;
			GUIStyle reorderableListEmptyHeaderToAssign;
			GetFromNative(reorderableListDragHandle, nativeReorderableListDragHandle, out reorderableListDragHandleToAssign);
			GetFromNative(reorderableListHeader, nativeReorderableListHeader, out reorderableListHeaderToAssign);
			GetFromNative(reorderableListFooter, nativeReorderableListFooter, out reorderableListFooterToAssign);
			GetFromNative(reorderableListBg, nativeReorderableListBg, out reorderableListBgToAssign);
			GetFromNative(reorderableListFooterButton, nativeReorderableListFooterButton, out reorderableListFooterButtonToAssign);
			GetFromNative(reorderableListElement, nativeReorderableListElement, out reorderableListElementToAssign);
			GetFromNative(reorderableListEmptyHeader, nativeReorderableListEmptyHeader, out reorderableListEmptyHeaderToAssign);


			var nativeErrorIcon = nativeSkin.FindStyle("CN EntryErrorIconSmall");
			var nativeWarningIcon = nativeSkin.FindStyle("CN EntryWarnIconSmall");
			var nativeLogIcon = nativeSkin.FindStyle("CN EntryInfoIconSmall");

			var logMessageIcons = _usedSkin.FindStyle("LogMessageIcons");
			bool addLogMessageIcons = logMessageIcons == null;
			if (addLogMessageIcons)
			{
				logMessageIcons = new GUIStyle();
				logMessageIcons.name = "LogMessageIcons";
			}


			if (nativeLogIcon != null && nativeLogIcon.normal.background != null)
			{
				logMessageIcons.normal.background = nativeLogIcon.normal.background;
			}

			if (nativeWarningIcon != null && nativeWarningIcon.normal.background != null)
			{
				logMessageIcons.hover.background = nativeWarningIcon.normal.background;
			}

			if (nativeErrorIcon != null && nativeErrorIcon.normal.background != null)
			{
				logMessageIcons.active.background = nativeErrorIcon.normal.background;
			}

			#region LeftCrumb
			var nativeLeftCrumb = nativeSkin.GetStyle(STYLE_BREADCRUMB_LEFT);
			var leftCrumb = _usedSkin.FindStyle(STYLE_BREADCRUMB_LEFT);
			GUIStyle leftCrumbToAssign = null;
			if (leftCrumb == null)
			{
				// make our own copy of the native skin left crumb so that editing it won't affect the rest of the editor GUI
				leftCrumbToAssign = new GUIStyle(nativeLeftCrumb);

				leftCrumbToAssign.fixedHeight = 19;

#if UNITY_2019_1_OR_NEWER
				// in Unity 2019+, the styles have changed,
				// so ensure we modify it so it fits our GUI
				leftCrumbToAssign.overflow.top = 1;
				leftCrumbToAssign.overflow.bottom = 1;
#else
				leftCrumbToAssign.overflow.top = 0;
				leftCrumbToAssign.overflow.bottom = 0;
#endif
			}
			else
			{
				// ensure our left crumb skin uses Unity's builtin look, but keep our settings
				leftCrumb.normal.background = nativeLeftCrumb.normal.background;
				leftCrumb.hover.background = nativeLeftCrumb.hover.background;
				leftCrumb.active.background = nativeLeftCrumb.active.background;
				leftCrumb.onNormal.background = nativeLeftCrumb.onNormal.background;
				leftCrumb.onHover.background = nativeLeftCrumb.onHover.background;
				leftCrumb.onActive.background = nativeLeftCrumb.onActive.background;

				leftCrumb.fixedHeight = 19;

#if UNITY_2019_1_OR_NEWER
				// in Unity 2019+, the styles have changed,
				// so ensure we modify it so it fits our GUI
				leftCrumb.overflow.top = 1;
				leftCrumb.overflow.bottom = 1;
#else
				leftCrumb.overflow.top = 0;
				leftCrumb.overflow.bottom = 0;
#endif
			}
			#endregion

			// ----------------------------------------------------

			#region MidCrumb
			var nativeMidCrumb = nativeSkin.GetStyle(STYLE_BREADCRUMB_MID);
			var midCrumb = _usedSkin.FindStyle(STYLE_BREADCRUMB_MID);
			GUIStyle midCrumbToAssign = null;

			if (midCrumb == null)
			{
				// make our own copy of the native skin mid crumb so that editing it won't affect the rest of the editor GUI
				midCrumbToAssign = new GUIStyle(nativeMidCrumb);

				midCrumbToAssign.fixedHeight = 19;

#if UNITY_2019_1_OR_NEWER
				// in Unity 2019+, the styles have changed,
				// so ensure we modify it so it fits our GUI
				midCrumbToAssign.overflow.top = 1;
				midCrumbToAssign.overflow.bottom = 1;
#else
				midCrumbToAssign.overflow.top = 0;
				midCrumbToAssign.overflow.bottom = 0;
#endif
			}
			else
			{
				// ensure our mid crumb skin uses Unity's builtin look, but keep our settings
				midCrumb.normal.background = nativeMidCrumb.normal.background;
				midCrumb.hover.background = nativeMidCrumb.hover.background;
				midCrumb.active.background = nativeMidCrumb.active.background;
				midCrumb.onNormal.background = nativeMidCrumb.onNormal.background;
				midCrumb.onHover.background = nativeMidCrumb.onHover.background;
				midCrumb.onActive.background = nativeMidCrumb.onActive.background;

				midCrumb.fixedHeight = 19;

#if UNITY_2019_1_OR_NEWER
				// in Unity 2019+, the styles have changed,
				// so ensure we modify it so it fits our GUI
				midCrumb.overflow.top = 1;
				midCrumb.overflow.bottom = 1;
#else
				midCrumb.overflow.top = 0;
				midCrumb.overflow.bottom = 0;
#endif
			}
			#endregion

			// ----------------------------------------------------

			if (leftCrumbToAssign != null || midCrumbToAssign != null ||
			    reorderableListDragHandleToAssign != null ||
			    reorderableListHeaderToAssign != null ||
			    reorderableListFooterToAssign != null ||
			    reorderableListBgToAssign != null ||
			    reorderableListFooterButtonToAssign != null ||
			    reorderableListElementToAssign != null ||
			    reorderableListEmptyHeaderToAssign != null ||
			    addLogMessageIcons)
			{
				// append these styles to the GUISkin
				// but since it's an array, we have to create a new array and place it there first
				var newStyles = new List<GUIStyle>(_usedSkin.customStyles);
				if (leftCrumbToAssign != null)
				{
					newStyles.Add(leftCrumbToAssign);
				}
				if (midCrumbToAssign != null)
				{
					newStyles.Add(midCrumbToAssign);
				}
				if (reorderableListDragHandleToAssign != null)
				{
					newStyles.Add(reorderableListDragHandleToAssign);
				}
				if (reorderableListHeaderToAssign != null)
				{
					newStyles.Add(reorderableListHeaderToAssign);
				}
				if (reorderableListFooterToAssign != null)
				{
					newStyles.Add(reorderableListFooterToAssign);
				}
				if (reorderableListBgToAssign != null)
				{
					newStyles.Add(reorderableListBgToAssign);
				}
				if (reorderableListFooterButtonToAssign != null)
				{
					newStyles.Add(reorderableListFooterButtonToAssign);
				}
				if (reorderableListElementToAssign != null)
				{
					newStyles.Add(reorderableListElementToAssign);
				}
				if (reorderableListEmptyHeaderToAssign != null)
				{
					newStyles.Add(reorderableListEmptyHeaderToAssign);
				}
				if (addLogMessageIcons)
				{
					newStyles.Add(logMessageIcons);
				}
				_usedSkin.customStyles = newStyles.ToArray();
			}

			// ----------------------------------------------------

			_toolbarIconLog = _usedSkin.GetStyle("Icon-Toolbar-Log").normal.background;
			_toolbarIconOpen = _usedSkin.GetStyle("Icon-Toolbar-Open").normal.background;
			_toolbarIconSave = _usedSkin.GetStyle("Icon-Toolbar-Save").normal.background;
			_toolbarIconOptions = _usedSkin.GetStyle("Icon-Toolbar-Options").normal.background;
			_toolbarIconHelp = _usedSkin.GetStyle("Icon-Toolbar-Help").normal.background;

			_toolbarLabelLog = new GUIContent(Labels.REFRESH_LABEL, _toolbarIconLog);
			_toolbarLabelOpen = new GUIContent(Labels.OPEN_LABEL, _toolbarIconOpen);
			_toolbarLabelSave = new GUIContent(Labels.SAVE_LABEL, _toolbarIconSave);
			_toolbarLabelOptions = new GUIContent(Labels.OPTIONS_CATEGORY_LABEL, _toolbarIconOptions);
			_toolbarLabelHelp = new GUIContent(Labels.HELP_CATEGORY_LABEL, _toolbarIconHelp);
		}
		else
		{
			_toolbarLabelLog = new GUIContent(Labels.REFRESH_LABEL);
			_toolbarLabelOpen = new GUIContent(Labels.OPEN_LABEL);
			_toolbarLabelSave = new GUIContent(Labels.SAVE_LABEL);
			_toolbarLabelOptions = new GUIContent(Labels.OPTIONS_CATEGORY_LABEL);
			_toolbarLabelHelp = new GUIContent(Labels.HELP_CATEGORY_LABEL);
		}
	}


	public void Init(BuildReportTool.BuildInfo buildInfo)
	{
		_buildInfo = buildInfo;

		minSize = new Vector2(903, 440);
	}

	/// <summary>
	/// Creates a Build Report and shows it on-screen.
	/// </summary>
	/// Called either when the "Get Log" button is pressed in this EditorWindow
	/// (called in <see cref="DrawTopRowButtons"/>, which is called in <see cref="OnGUI"/>),
	/// or in <see cref="Update"/>, when it has detected that a build has completed and
	/// a Build Report creation was scheduled.
	void Refresh(bool fromBuild)
	{
		GoToOverviewScreen();
		BuildReportTool.ReportGenerator.RefreshData(fromBuild, ref _buildInfo, ref _assetDependencies, ref _textureData, ref _meshData, ref _prefabData);
	}

	bool IsWaitingForBuildCompletionToGenerateBuildReport
	{
		get { return EditorApplication.isCompiling && BuildReportTool.Util.ShouldGetBuildReportNow; }
	}

	void OnFinishOpeningBuildReportFile()
	{
		_finishedOpeningFromThread = false;

		if (BuildReportTool.Util.BuildInfoHasContents(_buildInfo))
		{
			_buildSettingsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_buildStepsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_usedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_unusedAssetsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_sizeStatsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
			_extraDataScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);

			_buildInfo.OnAfterLoad();
			_buildInfo.SetSavedPath(_lastOpenedBuildInfoFilePath);
		}

		Repaint();
		GoToOverviewScreen();
	}

	void OnFinishGeneratingBuildReport()
	{
		BuildReportTool.ReportGenerator.OnFinishedGetValues(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData);
		_buildInfo.UnescapeAssetNames();

		GoToOverviewScreen();

		_unityBuildReport = ReportGenerator.LastKnownUnityBuildReport;
		if (_unityBuildReport != null)
		{
			Debug.Log(string.Format("UnityBuildReport displayed is now: {0}", _unityBuildReport.SavedPath));
		}

		_buildSettingsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		_buildStepsScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
	}


	void GoToOverviewScreen()
	{
		_selectedCategoryIdx = OVERVIEW_IDX;
	}


	// ==========================================================================

	// ==========================================================================


	int _fileFilterGroupToUseOnOpeningOptionsWindow = 0;
	int _fileFilterGroupToUseOnClosingOptionsWindow = 0;


	int _selectedCategoryIdx = 0;

	bool IsInOverviewCategory
	{
		get { return _selectedCategoryIdx == OVERVIEW_IDX; }
	}

	bool IsInBuildSettingsCategory
	{
		get { return _selectedCategoryIdx == BUILD_SETTINGS_IDX; }
	}

	bool IsInBuildStepsCategory
	{
		get { return _selectedCategoryIdx == BUILD_STEPS_IDX; }
	}

	bool IsInWarningsErrorsCategory
	{
		get { return _selectedCategoryIdx == WARNING_ERRORS_IDX; }
	}

	bool IsInSizeStatsCategory
	{
		get { return _selectedCategoryIdx == SIZE_STATS_IDX; }
	}

	bool IsInUsedAssetsCategory
	{
		get { return _selectedCategoryIdx == USED_ASSETS_IDX; }
	}

	bool IsInUnusedAssetsCategory
	{
		get { return _selectedCategoryIdx == UNUSED_ASSETS_IDX; }
	}

	bool IsInExtraDataCategory
	{
		get { return _selectedCategoryIdx == EXTRA_DATA_IDX; }
	}

	bool IsInOptionsCategory
	{
		get { return _selectedCategoryIdx == OPTIONS_IDX; }
	}

	bool IsInHelpCategory
	{
		get { return _selectedCategoryIdx == HELP_IDX; }
	}


	const int OVERVIEW_IDX = 0;
	const int BUILD_SETTINGS_IDX = 1;
	const int BUILD_STEPS_IDX = 2;
	const int WARNING_ERRORS_IDX = 3;
	const int SIZE_STATS_IDX = 4;
	const int USED_ASSETS_IDX = 5;
	const int UNUSED_ASSETS_IDX = 6;
	const int EXTRA_DATA_IDX = 7;

	const int OPTIONS_IDX = 8;
	const int HELP_IDX = 9;


	bool _finishedOpeningFromThread = false;
	string _lastOpenedBuildInfoFilePath = "";

	void _OpenBuildInfo(string filepath)
	{
		if (string.IsNullOrEmpty(filepath))
		{
			return;
		}

		_finishedOpeningFromThread = false;
		GetValueMessage = "Opening...";
		BuildReportTool.BuildInfo loadedBuild = BuildReportTool.Util.OpenSerializedBuildInfo(filepath, false);

		if (BuildReportTool.Util.BuildInfoHasContents(loadedBuild))
		{
			_buildInfo = loadedBuild;
			_lastOpenedBuildInfoFilePath = filepath;
		}
		else
		{
			Debug.LogError(string.Format("Build Report Tool: Invalid data in build info file: {0}", filepath));
		}

		var assetDependenciesFilePath = BuildReportTool.Util.GetAssetDependenciesFilenameFromBuildInfo(filepath);
		if (System.IO.File.Exists(assetDependenciesFilePath))
		{
			var loadedAssetDependencies = BuildReportTool.Util.OpenSerialized<BuildReportTool.AssetDependencies>(assetDependenciesFilePath);
			if (loadedAssetDependencies != null)
			{
				_assetDependencies = loadedAssetDependencies;
			}
		}
		else
		{
			_assetDependencies = null;
		}

		var textureDataFilePath = BuildReportTool.Util.GetTextureDataFilenameFromBuildInfo(filepath);
		if (System.IO.File.Exists(textureDataFilePath))
		{
			var loadedTextureData = BuildReportTool.Util.OpenSerialized<BuildReportTool.TextureData>(textureDataFilePath);
			if (loadedTextureData != null)
			{
				_textureData = loadedTextureData;
			}
		}
		else
		{
			_textureData = null;
		}

		var meshDataFilePath = BuildReportTool.Util.GetMeshDataFilenameFromBuildInfo(filepath);
		if (System.IO.File.Exists(meshDataFilePath))
		{
			var loadedMeshData = BuildReportTool.Util.OpenSerialized<BuildReportTool.MeshData>(meshDataFilePath);
			if (loadedMeshData != null)
			{
				_meshData = loadedMeshData;
			}
		}
		else
		{
			_meshData = null;
		}

		var prefabDataFilePath = BuildReportTool.Util.GetPrefabDataFilenameFromBuildInfo(filepath);
		if (System.IO.File.Exists(prefabDataFilePath))
		{
			var loadedPrefabData = BuildReportTool.Util.OpenSerialized<BuildReportTool.PrefabData>(prefabDataFilePath);
			if (loadedPrefabData != null)
			{
				_prefabData = loadedPrefabData;
			}
		}
		else
		{
			_prefabData = null;
		}

		var unityBuildReportFilePath = BuildReportTool.Util.GetUnityBuildReportFilenameFromBuildInfo(filepath);
		if (System.IO.File.Exists(unityBuildReportFilePath))
		{
			try
			{
				var loadedUnityBuildReport =
					BuildReportTool.Util.OpenSerialized<BuildReportTool.UnityBuildReport>(unityBuildReportFilePath);
				if (loadedUnityBuildReport != null)
				{
					_unityBuildReport = loadedUnityBuildReport;
					//Debug.Log(string.Format("UnityBuildReport displayed is now: {0}", _unityBuildReport.SavedPath));
				}
				else
				{
					_unityBuildReport = null;
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning(string.Format("Can't open additional build info data due to Unity version incompatibility.\n\n{0}", e));
				_unityBuildReport = null;
			}
		}
		else
		{
			//Debug.LogWarning(string.Format("Not found: {0}", unityBuildReportFilePath));
			_unityBuildReport = null;
		}

		var extraDataFilePath = BuildReportTool.Util.GetExtraDataFilename(filepath).Replace('\\', '/');
		if (System.IO.File.Exists(extraDataFilePath))
		{
			_extraData.Contents = System.IO.File.ReadAllText(extraDataFilePath);
			_extraData.SavedPath = extraDataFilePath;
		}
		else
		{
			_extraData.Contents = null;
			_extraData.SavedPath = null;
		}

		_finishedOpeningFromThread = true;

		GetValueMessage = "";
	}


	Thread _currentBuildReportFileLoadThread = null;

	bool IsCurrentlyOpeningAFile
	{
		get
		{
			return _currentBuildReportFileLoadThread != null &&
			       _currentBuildReportFileLoadThread.ThreadState == ThreadState.Running;
		}
	}

	void ForceStopFileLoadThread()
	{
		if (IsCurrentlyOpeningAFile)
		{
			try
			{
				//Debug.LogFormat(this, "Build Report Tool: Stopping file load background thread...");
				_currentBuildReportFileLoadThread.Abort();
				Debug.LogFormat(this, "Build Report Tool: File load background thread stopped.");
			}
			catch (ThreadStateException)
			{
			}
		}
	}

	void OpenBuildInfoInBackgroundIfNeeded(string filepath)
	{
		if (string.IsNullOrEmpty(filepath))
		{
			return;
		}

		// user selected the asset dependencies file for the build report
		// derive the build report file from it
		if (filepath.DoesFileBeginWith("DEP-"))
		{
			filepath = TakeOutPrefix(filepath, "DEP-");
		}
		else if (filepath.DoesFileBeginWith("TextureData-"))
		{
			filepath = TakeOutPrefix(filepath, "TextureData-");
		}
		else if (filepath.DoesFileBeginWith("MeshData-"))
		{
			filepath = TakeOutPrefix(filepath, "MeshData-");
		}
		else if (filepath.DoesFileBeginWith("PrefabData-"))
		{
			filepath = TakeOutPrefix(filepath, "PrefabData-");
		}
		else if (filepath.DoesFileBeginWith("UBR-"))
		{
			filepath = TakeOutPrefix(filepath, "UBR-");
		}
		else if (filepath.DoesFileBeginWith("ExtraData-"))
		{
			filepath = TakeOutPrefix(filepath, "ExtraData-");
		}

		if (!BuildReportTool.Options.UseThreadedFileLoading)
		{
			_OpenBuildInfo(filepath);
		}
		else
		{
			if (_currentBuildReportFileLoadThread != null &&
			    _currentBuildReportFileLoadThread.ThreadState == ThreadState.Running)
			{
				ForceStopFileLoadThread();
			}

			_currentBuildReportFileLoadThread = new Thread(() => LoadThread(filepath));
			_currentBuildReportFileLoadThread.Start();
			Debug.LogFormat(this, "Build Report Tool: Started new load background thread...");
		}
	}

	static string TakeOutPrefix(string s, string prefixToRemove)
	{
		var path = System.IO.Path.GetDirectoryName(s);
		var filename = s.GetFileNameOnly();
		return string.Format("{0}/{1}", path, filename.Substring(prefixToRemove.Length));
	}

	void LoadThread(string filepath)
	{
		_OpenBuildInfo(filepath);
		Debug.LogFormat(this, "Build Report Tool: Load background thread finished.");
	}


	void DrawCentralMessage(string msg)
	{
		float w = 300;
		float h = 200;
		float x = (position.width - w) * 0.5f;
		float y = (position.height - h) * 0.25f;

		GUI.Label(new Rect(x, y, w, h), msg);
	}


	void DrawWarningMessage(string msg)
	{
		float w = 400;
		float h = 100;
		float x = (position.width - w) * 0.5f;
		float y = ((position.height - h) * 0.25f) + 100 + 40;

		var msgRect = new Rect(x, y, w, h);
		GUI.Label(msgRect, msg);

		var warning = GUI.skin.FindStyle("Icon-Warning");
		if (warning != null)
		{
			var warningIcon = warning.normal.background;

			var iconWidth = warning.fixedWidth;
			var iconHeight = warning.fixedHeight;

			GUI.DrawTexture(new Rect(msgRect.x - iconWidth, msgRect.y, iconWidth, iconHeight), warningIcon);
		}
	}


	void DrawTopRowButtons()
	{
		int toolbarX = 10;

		var leftToolbarStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOOLBAR_LEFT_STYLE_NAME);
		if (leftToolbarStyle == null)
		{
			leftToolbarStyle = GUI.skin.button;
		}

		var midToolbarStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOOLBAR_MIDDLE_STYLE_NAME);
		if (midToolbarStyle == null)
		{
			midToolbarStyle = GUI.skin.button;
		}

		var rightToolbarStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOOLBAR_RIGHT_STYLE_NAME);
		if (rightToolbarStyle == null)
		{
			rightToolbarStyle = GUI.skin.button;
		}

		if (GUI.Button(new Rect(toolbarX, 5, 50, 40), _toolbarLabelLog, leftToolbarStyle) &&
		    !LoadingValuesFromThread)
		{
			Refresh(false);
		}

		toolbarX += 50;
		if (GUI.Button(new Rect(toolbarX, 5, 40, 40), _toolbarLabelOpen, midToolbarStyle) &&
		    !LoadingValuesFromThread)
		{
			string filepath = EditorUtility.OpenFilePanel(
				Labels.OPEN_SERIALIZED_BUILD_INFO_TITLE,
				BuildReportTool.Options.BuildReportSavePath,
				"xml");

			OpenBuildInfoInBackgroundIfNeeded(filepath);
		}

		toolbarX += 40;

		if (GUI.Button(new Rect(toolbarX, 5, 40, 40), _toolbarLabelSave, rightToolbarStyle) &&
		    BuildReportTool.Util.BuildInfoHasContents(_buildInfo))
		{
			string filepath = EditorUtility.SaveFilePanel(
				Labels.SAVE_MSG,
				BuildReportTool.Options.BuildReportSavePath,
				_buildInfo.GetDefaultFilename(),
				"xml");

			if (!string.IsNullOrEmpty(filepath))
			{
				BuildReportTool.Util.Serialize(_buildInfo, filepath);

				if (_assetDependencies != null && _assetDependencies.HasContents)
				{
					var assetDependenciesFilePath = BuildReportTool.Util.GetAssetDependenciesFilenameFromBuildInfo(filepath);
					BuildReportTool.Util.Serialize(_assetDependencies, assetDependenciesFilePath);
				}

				if (_textureData != null && _textureData.HasContents)
				{
					var textureDataFilePath = BuildReportTool.Util.GetTextureDataFilenameFromBuildInfo(filepath);
					BuildReportTool.Util.Serialize(_textureData, textureDataFilePath);
				}

				if (_meshData != null && _meshData.HasContents)
				{
					var meshDataFilePath = BuildReportTool.Util.GetMeshDataFilenameFromBuildInfo(filepath);
					BuildReportTool.Util.Serialize(_meshData, meshDataFilePath);
				}

				if (_prefabData != null && _prefabData.HasContents)
				{
					var prefabDataFilePath = BuildReportTool.Util.GetPrefabDataFilenameFromBuildInfo(filepath);
					BuildReportTool.Util.Serialize(_prefabData, prefabDataFilePath);
				}

				if (_unityBuildReport != null)
				{
					var unityBuildReportFilePath = BuildReportTool.Util.GetUnityBuildReportFilenameFromBuildInfo(filepath);
					BuildReportTool.Util.Serialize(_unityBuildReport, unityBuildReportFilePath);
				}
			}
		}

		toolbarX += 40;


		toolbarX += 20;

		if (GUI.Button(new Rect(toolbarX, 5, 55, 40), _toolbarLabelOptions, leftToolbarStyle))
		{
			_selectedCategoryIdx = OPTIONS_IDX;
			BuildReportTool.Options.UpdatePreviousSearchType();
		}

		toolbarX += 55;
		if (GUI.Button(new Rect(toolbarX, 5, 70, 40), _toolbarLabelHelp, rightToolbarStyle))
		{
			_selectedCategoryIdx = HELP_IDX;
			_helpScreen.RefreshData(_buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport);
		}
	}

	bool _buildInfoHasNoContentsToDisplay = false;

	void OnGUI()
	{
		if (Event.current.type == EventType.Layout)
		{
			_noGuiSkinFound = _usedSkin == null;
			_loadingValuesFromThread = !string.IsNullOrEmpty(GetValueMessage);
			_buildInfoHasNoContentsToDisplay = !BuildReportTool.Util.BuildInfoHasContents(_buildInfo);
		}

		//GUI.Label(new Rect(5, 100, 800, 20), "BuildReportTool.Util.ShouldReload: " + BuildReportTool.Util.ShouldReload + " EditorApplication.isCompiling: " + EditorApplication.isCompiling);
		if (!_noGuiSkinFound)
		{
			GUI.skin = _usedSkin;
			//GUI.Label(new Rect(20, 20, 500, 100), BuildReportTool.Options.BUILD_REPORT_PACKAGE_MISSING_MSG);
			//return;
		}
		else
		{
			GUI.Label(new Rect(300, -25, 500, 100), BuildReportTool.Options.BUILD_REPORT_GUI_SKIN_MISSING_MSG);
		}

		DrawTopRowButtons();

		if (GUI.skin.FindStyle(BuildReportTool.Window.Settings.VERSION_STYLE_NAME) != null)
		{
			GUI.Label(new Rect(0, 0, position.width, 20), BuildReportTool.Info.ReadableVersion,
				BuildReportTool.Window.Settings.VERSION_STYLE_NAME);
		}
		else
		{
			GUI.Label(new Rect(position.width - 160, 0, position.width, 20), BuildReportTool.Info.ReadableVersion);
		}


		// loading message
		if (LoadingValuesFromThread)
		{
			DrawCentralMessage(GetValueMessage);
			return;
		}

		bool requestRepaint = false;

		// content to show when there is no build report on display
		if (_buildInfoHasNoContentsToDisplay)
		{
			if (IsInOptionsCategory)
			{
				GUILayout.Space(40);
				_optionsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaint);
			}
			else if (IsInHelpCategory)
			{
				GUILayout.Space(40);
				_helpScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaint);
			}
			else if (IsWaitingForBuildCompletionToGenerateBuildReport)
			{
				DrawCentralMessage(Labels.WAITING_FOR_BUILD_TO_COMPLETE_MSG);
			}
			else
			{
				DrawCentralMessage(Labels.NO_BUILD_INFO_FOUND_MSG);

				if (ReportGenerator.CheckIfUnityHasNoLogArgument())
				{
					DrawWarningMessage(Labels.FOUND_NO_LOG_ARGUMENT_MSG);
				}
			}

			if (requestRepaint)
			{
				Repaint();
			}

			return;
		}


		GUILayout.Space(50); // top padding (top row buttons are 40 pixels)


		var mouseHasMoved = Mathf.Abs(Event.current.mousePosition.x - _lastMousePos.x) > 0 ||
		                    Mathf.Abs(Event.current.mousePosition.y - _lastMousePos.y) > 0;


		// category buttons

		int oldSelectedCategoryIdx = _selectedCategoryIdx;

		var leftTabStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TAB_LEFT_STYLE_NAME);
		if (leftTabStyle == null)
		{
			leftTabStyle = GUI.skin.button;
		}

		var midTabStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TAB_MIDDLE_STYLE_NAME);
		if (midTabStyle == null)
		{
			midTabStyle = GUI.skin.button;
		}

		var rightTabStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TAB_RIGHT_STYLE_NAME);
		if (rightTabStyle == null)
		{
			rightTabStyle = GUI.skin.button;
		}

		GUILayout.BeginHorizontal();
		if (GUILayout.Toggle(IsInOverviewCategory, "Overview", leftTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = OVERVIEW_IDX;
		}

		if (GUILayout.Toggle(IsInBuildSettingsCategory, "Project Settings", midTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = BUILD_SETTINGS_IDX;
		}

		if (_unityBuildReport != null && GUILayout.Toggle(IsInBuildStepsCategory, "Build Process", midTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = BUILD_STEPS_IDX;
		}

		if (GUILayout.Toggle(IsInSizeStatsCategory, "Size Stats", midTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = SIZE_STATS_IDX;
		}

		if (!string.IsNullOrEmpty(_extraData.Contents) &&
		    GUILayout.Toggle(IsInExtraDataCategory, "Extra Data", midTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = EXTRA_DATA_IDX;
		}

		if (GUILayout.Toggle(IsInUsedAssetsCategory, "Used Assets", midTabStyle, LayoutExpandWidth))
		{
			if (_selectedCategoryIdx != USED_ASSETS_IDX && BuildReportTool.Options.HasSearchTypeChanged)
			{
				_usedAssetsScreen.UpdateSearchNow(_buildInfo);
			}

			_selectedCategoryIdx = USED_ASSETS_IDX;
		}

		if (GUILayout.Toggle(IsInUnusedAssetsCategory, "Unused Assets", rightTabStyle, LayoutExpandWidth))
		{
			if (_selectedCategoryIdx != UNUSED_ASSETS_IDX && BuildReportTool.Options.HasSearchTypeChanged)
			{
				_unusedAssetsScreen.UpdateSearchNow(_buildInfo);
			}

			_selectedCategoryIdx = UNUSED_ASSETS_IDX;
		}

		/*GUILayout.Space(20);

		if (GUILayout.Toggle(IsInOptionsCategory, _toolbarLabelOptions, leftTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = OPTIONS_IDX;
		}
		if (GUILayout.Toggle(IsInHelpCategory, _toolbarLabelHelp, rightTabStyle, LayoutExpandWidth))
		{
			_selectedCategoryIdx = HELP_IDX;
		}*/
		GUILayout.EndHorizontal();


		if (oldSelectedCategoryIdx != OPTIONS_IDX && _selectedCategoryIdx == OPTIONS_IDX)
		{
			// moving into the options screen
			_fileFilterGroupToUseOnOpeningOptionsWindow = BuildReportTool.Options.FilterToUseInt;
		}
		else if (oldSelectedCategoryIdx == OPTIONS_IDX && _selectedCategoryIdx != OPTIONS_IDX)
		{
			// moving away from the options screen
			_fileFilterGroupToUseOnClosingOptionsWindow = BuildReportTool.Options.FilterToUseInt;

			if (_fileFilterGroupToUseOnOpeningOptionsWindow != _fileFilterGroupToUseOnClosingOptionsWindow)
			{
				RecategorizeDisplayedBuildInfo();
			}
		}

		bool requestRepaintOnTabs = false;

		// main content
		GUILayout.BeginHorizontal();
		//GUILayout.Space(3); // left padding
		GUILayout.BeginVertical();

		if (IsInOverviewCategory)
		{
			_overviewScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInBuildSettingsCategory)
		{
			_buildSettingsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInBuildStepsCategory)
		{
			_buildStepsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInSizeStatsCategory)
		{
			_sizeStatsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInUsedAssetsCategory)
		{
			_usedAssetsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInUnusedAssetsCategory)
		{
			_unusedAssetsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInExtraDataCategory)
		{
			_extraDataScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInOptionsCategory)
		{
			_optionsScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}
		else if (IsInHelpCategory)
		{
			_helpScreen.DrawGUI(position, _buildInfo, _assetDependencies, _textureData, _meshData, _prefabData, _unityBuildReport, _extraData, out requestRepaintOnTabs);
		}

		GUILayout.FlexibleSpace();
		GUILayout.EndVertical();
		//GUILayout.Space(5); // right padding
		GUILayout.EndHorizontal();

		//GUILayout.Space(10); // bottom padding

		if (requestRepaintOnTabs)
		{
			_buildInfo.FlagOkToRefresh();
		}

		_lastMousePos = Event.current.mousePosition;
		_lastMouseMoved = mouseHasMoved;
	}

	public static bool LastMouseMoved
	{
		get { return _lastMouseMoved; }
	}

	public static bool MouseMovedNow
	{
		get
		{
			return Mathf.Abs(Event.current.mousePosition.x - _lastMousePos.x) > 0 ||
			       Mathf.Abs(Event.current.mousePosition.y - _lastMousePos.y) > 0;
		}
	}

	// =====================================================================================

	static readonly MethodInfo GetAtlasPreviewTextureMethod = typeof(SpriteAtlasExtensions)
		.GetMethod("GetPreviewTextures", BindingFlags.Static | BindingFlags.NonPublic);

	static readonly object[] GetAtlasPreviewTextureParams = new object[1];

	public static Texture GetAssetPreview(SizePart sizePart)
	{
		if (sizePart == null)
		{
			return null;
		}

		return GetAssetPreview(sizePart.Name);
	}

	public static Texture GetAssetPreview(string assetName)
	{
		if (string.IsNullOrEmpty(assetName))
		{
			return null;
		}

		Texture thumbnailImage = null;
		if (assetName.IsTextureFile())
		{
#if UNITY_5_6_OR_NEWER
			thumbnailImage = AssetDatabase.LoadAssetAtPath<Texture>(assetName);
#else
			thumbnailImage = (Texture)AssetDatabase.LoadAssetAtPath(assetName, typeof(Texture));
#endif
		}
		else //if (_assetListEntryHovered.Name.EndsWith(".prefab") || BuildReportTool.Util.IsFileAUnityMesh(_assetListEntryHovered.Name))
		{
#if UNITY_5_6_OR_NEWER
			var loadedObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetName);
#else
			var loadedObj = (UnityEngine.Object)AssetDatabase.LoadAssetAtPath(assetName, typeof(UnityEngine.Object));
#endif

			if (loadedObj == null)
			{
				return null;
			}

			if (loadedObj is SpriteAtlas atlas)
			{
				GetAtlasPreviewTextureParams[0] = atlas;

				if (GetAtlasPreviewTextureMethod?.Invoke(null, GetAtlasPreviewTextureParams) is Texture2D[] textures &&
				    textures.Length > 0 &&
				    textures[0] != null)
				{
					thumbnailImage = textures[0];
				}
				else
				{
					// If SpriteAtlasExtensions.GetPreviewTextures failed,
					// just try AssetPreview.GetAssetPreview
					thumbnailImage = AssetPreview.GetAssetPreview(atlas);
				}
			}
			else
			{
				thumbnailImage = AssetPreview.GetAssetPreview(loadedObj);
				//thumbnailImage = AssetPreview.GetMiniThumbnail(loadedObj);
			}
		}

		return thumbnailImage;
	}

	public static Vector2 GetThumbnailSize()
	{
		Vector2 thumbnailSize;
		thumbnailSize.x = ZoomedInThumbnails
			                  ? BuildReportTool.Options.TooltipThumbnailZoomedInWidth
			                  : BuildReportTool.Options.TooltipThumbnailWidth;

		thumbnailSize.y = ZoomedInThumbnails
			                  ? BuildReportTool.Options.TooltipThumbnailZoomedInHeight
			                  : BuildReportTool.Options.TooltipThumbnailHeight;
		return thumbnailSize;
	}

	public static Rect DrawTooltip(Rect position, float desiredWidth, float desiredHeight, float additionalPadding = 0)
	{
		var tooltipStyle = GUI.skin.FindStyle("Tooltip");
		if (tooltipStyle == null)
		{
			tooltipStyle = GUI.skin.box;
		}
		var tooltipRect = new Rect();

		// --------------------------------------------------

		var tooltipPos = Event.current.mousePosition;

		// offset for mouse
		tooltipPos.x += 18;
		tooltipPos.y += 15;

		// --------------------------------------------------
		// initially tooltip is to the right and below the mouse

		tooltipRect.width = desiredWidth;
		tooltipRect.height = desiredHeight;

		tooltipRect.width += tooltipStyle.border.horizontal + (additionalPadding * 2);
		tooltipRect.height += tooltipStyle.border.vertical + (additionalPadding * 2);

		tooltipRect.x = tooltipPos.x - tooltipStyle.border.left;
		tooltipRect.y = tooltipPos.y - tooltipStyle.border.top;

		// --------------------------------------------------

		if (tooltipRect.xMax > position.width)
		{
			// move tooltip to the left
			tooltipPos.x = Event.current.mousePosition.x - 5 - tooltipRect.width;
			tooltipRect.x = tooltipPos.x - tooltipStyle.border.left;

			if (tooltipRect.x < 0)
			{
				tooltipPos.x = position.width - tooltipRect.width;
				tooltipRect.x = tooltipPos.x - tooltipStyle.border.left;
			}
		}

		// --------------------------------------------------

		if (tooltipRect.yMax > position.height)
		{
			// move tooltip above mouse
			tooltipPos.y = Event.current.mousePosition.y + 3 - tooltipRect.height;
			tooltipRect.y = tooltipPos.y - tooltipStyle.border.top;

			if (tooltipRect.y < 0)
			{
				tooltipPos.y = position.height - tooltipRect.height;
				tooltipRect.y = tooltipPos.y - tooltipStyle.border.top;
			}
		}

		// --------------------------------------------------

		GUI.Box(tooltipRect, string.Empty, tooltipStyle);

		return new Rect(tooltipPos.x + additionalPadding, tooltipPos.y + additionalPadding, desiredWidth, desiredHeight);
	}

	public static void ProcessThumbnailControls()
	{
		if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.LeftAlt ||
		                                                Event.current.keyCode == KeyCode.RightAlt))
		{
			ShowThumbnailsWithAlphaBlend = !ShowThumbnailsWithAlphaBlend;
		}

		if ((Event.current.keyCode == KeyCode.LeftControl ||
		     Event.current.keyCode == KeyCode.RightControl))
		{
			if (Event.current.type == EventType.KeyDown)
			{
				ZoomedInThumbnails = true;
			}
			else if (Event.current.type == EventType.KeyUp)
			{
				ZoomedInThumbnails = false;
			}
		}
	}

	public static void ResetThumbnailControls()
	{
		// ensure that thumbnails are not zoomed in
		ZoomedInThumbnails = false;
	}

	public static void DrawThumbnail(float posX, float posY, Vector2 thumbnailSize, Texture thumbnailImage)
	{
		var thumbnailRect = new Rect(posX, posY, thumbnailSize.x, thumbnailSize.y);
		GUI.DrawTexture(thumbnailRect, thumbnailImage, ScaleMode.ScaleToFit, ShowThumbnailsWithAlphaBlend);
	}

	public static Vector2 GetEndUsersListSize(GUIContent label, List<GUIContent> endUsers)
	{
		var assetStyle = GUI.skin.FindStyle("Asset");
		if (assetStyle == null)
		{
			assetStyle = GUI.skin.label;
		}
		var labelStyle = GUI.skin.FindStyle("TooltipText");
		if (labelStyle == null)
		{
			labelStyle = GUI.skin.box;
		}

		Vector2 endUsersSize = Vector2.zero;

		var labelSize = labelStyle.CalcSize(label);
		endUsersSize.x = Mathf.Max(endUsersSize.x, labelSize.x);
		endUsersSize.y += labelSize.y;

		if (endUsers != null)
		{
			EditorGUIUtility.SetIconSize(IconSize);

			for (int n = 0, len = Mathf.Min(endUsers.Count, TOOLTIP_END_USERS_MAX_COUNT); n < len; ++n)
			{
				var endUserSize = assetStyle.CalcSize(endUsers[n]);

				endUsersSize.x = Mathf.Max(endUsersSize.x, endUserSize.x);
				endUsersSize.y += endUserSize.y;
			}

			if (endUsers.Count > TOOLTIP_END_USERS_MAX_COUNT)
			{
				PlusMore.text = string.Format("...plus {0} more", endUsers.Count - TOOLTIP_END_USERS_MAX_COUNT);
				var plusMoreSize = labelStyle.CalcSize(PlusMore);

				endUsersSize.x = Mathf.Max(endUsersSize.x, plusMoreSize.x);
				endUsersSize.y += plusMoreSize.y;
			}
		}

		// padding
		endUsersSize.x += TOOLTIP_PADDING_L + TOOLTIP_PADDING_R;
		endUsersSize.y += TOOLTIP_PADDING_T + TOOLTIP_PADDING_B;

		return endUsersSize;
	}

	public static void DrawEndUsersList(Vector2 pos, GUIContent label, List<GUIContent> endUsers)
	{
		var assetStyle = GUI.skin.FindStyle("Asset");
		if (assetStyle == null)
		{
			assetStyle = GUI.skin.label;
		}
		var labelStyle = GUI.skin.FindStyle("TooltipText");
		if (labelStyle == null)
		{
			labelStyle = GUI.skin.box;
		}

		Rect endUserRect = new Rect(pos.x + TOOLTIP_PADDING_T, pos.y + TOOLTIP_PADDING_L, 0, 0);

		endUserRect.size = labelStyle.CalcSize(label);
		GUI.Label(endUserRect, label, labelStyle);

		if (endUsers != null && endUsers.Count > 0)
		{
			endUserRect.y += endUserRect.height;

			EditorGUIUtility.SetIconSize(IconSize);

			for (int n = 0, len = Mathf.Min(endUsers.Count, TOOLTIP_END_USERS_MAX_COUNT); n < len; ++n)
			{
				endUserRect.size = assetStyle.CalcSize(endUsers[n]);

				GUI.Label(endUserRect, endUsers[n], assetStyle);

				endUserRect.y += endUserRect.height;
			}

			if (endUsers.Count > TOOLTIP_END_USERS_MAX_COUNT)
			{
				PlusMore.text = string.Format("...plus {0} more", endUsers.Count - TOOLTIP_END_USERS_MAX_COUNT);
				endUserRect.size = labelStyle.CalcSize(PlusMore);

				GUI.Label(endUserRect, PlusMore, labelStyle);
			}
		}
	}

	// -----------------------------------------------

	public static void DrawThumbnailTooltip(Rect position, BuildReportTool.TextureData textureData)
	{
		DrawThumbnailTooltip(position, HoveredAssetEntryPath, HoveredAssetEntryRect, textureData);
	}

	static readonly GUIContent TextureDataTooltipLabel = new GUIContent();

	static bool GetTextureDataForTooltip(string assetPath, BuildReportTool.TextureData textureData, out Vector2 labelSize)
	{
		if (textureData == null)
		{
			labelSize = Vector2.zero;
			return false;
		}

		var labelStyle = GUI.skin.FindStyle("TooltipText");
		if (labelStyle == null)
		{
			labelStyle = GUI.skin.box;
		}

		if (assetPath.IsSpriteAtlasFile())
		{
			var thumbnailImage = BRT_BuildReportWindow.GetAssetPreview(assetPath);

			TextureDataTooltipLabel.text = string.Format("Sprite Atlas ({0}) {1}x{2}",
				thumbnailImage.graphicsFormat,
				thumbnailImage.width,
				thumbnailImage.height);

			labelSize = labelStyle.CalcSize(TextureDataTooltipLabel);
			return true;
		}

		var data = textureData.GetTextureData();
		if (data.ContainsKey(assetPath))
		{
			if (data[assetPath].IsImportedWidthAndHeightDifferentFromReal)
			{
				if (ZoomedInThumbnails)
				{
					TextureDataTooltipLabel.text = string.Format("{0} ({1}) {2} (source: {3})",
						data[assetPath].TextureType,
						data[assetPath].GetShownTextureFormat(),
						data[assetPath].ToDisplayedValue(TextureData.DataId.ImportedWidthAndHeight),
						data[assetPath].ToDisplayedValue(TextureData.DataId.RealWidthAndHeight));
				}
				else
				{
					TextureDataTooltipLabel.text = string.Format("{0} ({1})\n{2} (source: {3})",
						data[assetPath].TextureType,
						data[assetPath].GetShownTextureFormat(),
						data[assetPath].ToDisplayedValue(TextureData.DataId.ImportedWidthAndHeight),
						data[assetPath].ToDisplayedValue(TextureData.DataId.RealWidthAndHeight));
				}
			}
			else
			{
				TextureDataTooltipLabel.text = string.Format("{0} ({1}) {2}",
					data[assetPath].TextureType,
					data[assetPath].GetShownTextureFormat(),
					data[assetPath].ToDisplayedValue(TextureData.DataId.ImportedWidthAndHeight));
			}

			labelSize = labelStyle.CalcSize(TextureDataTooltipLabel);

			return true;
		}
		else
		{
			labelSize = Vector2.zero;
			return false;
		}
	}

	public static void DrawThumbnailTooltip(Rect position, string assetPath, Rect assetRect, BuildReportTool.TextureData textureData)
	{
		var thumbnailImage = BRT_BuildReportWindow.GetAssetPreview(assetPath);

		if (thumbnailImage != null)
		{
			var desiredSize = Vector2.zero;
			var thumbnailSize = BRT_BuildReportWindow.GetThumbnailSize();
			desiredSize.x = thumbnailSize.x;
			desiredSize.y = thumbnailSize.y;

			Vector2 textureDataLabelSize;
			bool showTextureData = GetTextureDataForTooltip(assetPath, textureData, out textureDataLabelSize);
			if (showTextureData)
			{
				desiredSize.x = Mathf.Max(desiredSize.x, textureDataLabelSize.x);
				desiredSize.y += textureDataLabelSize.y;
			}

			var tooltipRect = BRT_BuildReportWindow.DrawTooltip(position, desiredSize.x, desiredSize.y);

			DrawThumbnail(tooltipRect.x, tooltipRect.y, thumbnailSize, thumbnailImage);

			if (showTextureData)
			{
				var labelStyle = GUI.skin.FindStyle("TooltipText");
				if (labelStyle == null)
				{
					labelStyle = GUI.skin.box;
				}

				GUI.Label(new Rect(
						tooltipRect.x, tooltipRect.y + thumbnailSize.y, textureDataLabelSize.x, textureDataLabelSize.y),
					TextureDataTooltipLabel, labelStyle);
			}
		}
	}

	public static void DrawEndUsersTooltip(Rect position, AssetDependencies assetDependencies)
	{
		List<GUIContent> endUsersListToUse = GetEndUserLabelsFor(assetDependencies, HoveredAssetEntryPath);
		DrawEndUsersTooltip(position, GetAppropriateEndUserLabelForHovered(), endUsersListToUse,
			HoveredAssetEntryRect);
	}

	public static void DrawEndUsersTooltip(Rect position, GUIContent label, List<GUIContent> endUsers, Rect assetRect)
	{
		var endUsersSize = BRT_BuildReportWindow.GetEndUsersListSize(label, endUsers);

		var tooltipRect = BRT_BuildReportWindow.DrawTooltip(position, endUsersSize.x, endUsersSize.y);

		BRT_BuildReportWindow.DrawEndUsersList(tooltipRect.position, label, endUsers);
	}

	public static void DrawThumbnailEndUsersTooltip(Rect position, AssetDependencies assetDependencies, BuildReportTool.TextureData textureData)
	{
		List<GUIContent> endUsersListToUse = GetEndUserLabelsFor(assetDependencies, HoveredAssetEntryPath);
		DrawThumbnailEndUsersTooltip(position, HoveredAssetEntryPath, GetAppropriateEndUserLabelForHovered(),
			endUsersListToUse, HoveredAssetEntryRect, textureData);
	}

	public static void DrawThumbnailEndUsersTooltip(Rect position, string assetPath, GUIContent label,
		List<GUIContent> endUsers, Rect assetRect, BuildReportTool.TextureData textureData)
	{
		var thumbnailImage = BRT_BuildReportWindow.GetAssetPreview(assetPath);

		if (thumbnailImage != null)
		{
			var usedBySpacing = 5;

			var thumbnailSize = BRT_BuildReportWindow.GetThumbnailSize();

			// compute end users height and width
			// then create a tooltip size that encompasses both thumbnail and end users list

			Vector2 endUsersSize = BRT_BuildReportWindow.GetEndUsersListSize(label, endUsers);
			endUsersSize.y += usedBySpacing;

			Vector2 tooltipSize = new Vector2(Mathf.Max(thumbnailSize.x, endUsersSize.x),
				thumbnailSize.y + endUsersSize.y);

			Vector2 textureDataLabelSize;
			bool showTextureData = GetTextureDataForTooltip(assetPath, textureData, out textureDataLabelSize);
			if (showTextureData)
			{
				tooltipSize.x = Mathf.Max(tooltipSize.x, textureDataLabelSize.x);
				tooltipSize.y += textureDataLabelSize.y;
			}

			var tooltipRect = BRT_BuildReportWindow.DrawTooltip(position, tooltipSize.x, tooltipSize.y);

			// --------
			// now draw the contents

			BRT_BuildReportWindow.DrawThumbnail(tooltipRect.x, tooltipRect.y, thumbnailSize, thumbnailImage);

			if (showTextureData)
			{
				var labelStyle = GUI.skin.FindStyle("TooltipText");
				if (labelStyle == null)
				{
					labelStyle = GUI.skin.box;
				}
				GUI.Label(new Rect(
						tooltipRect.x, tooltipRect.y + thumbnailSize.y, textureDataLabelSize.x, textureDataLabelSize.y),
					TextureDataTooltipLabel, labelStyle);
			}

			var endUsersPos = tooltipRect.position;
			endUsersPos.y += thumbnailSize.y + textureDataLabelSize.y + usedBySpacing;
			BRT_BuildReportWindow.DrawEndUsersList(endUsersPos, label, endUsers);
		}
	}

	// =====================================================================================
}

#endif