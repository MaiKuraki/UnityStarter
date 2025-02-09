using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace BuildReportTool
{
	/// <summary>
	/// Class for holding options.
	/// This is the class that is serialized when saving the options.
	/// </summary>
	[System.Serializable, XmlRoot("BuildReportToolOptions")]
	public class SavedOptions
	{
		public string EditorLogOverridePath;

		public string BuildReportFolderName = BuildReportTool.Options.BUILD_REPORTS_DEFAULT_FOLDER_NAME;

		public string BuildReportCustomOutputPath = BuildReportTool.Util.GetUserHomeFolder();

		/// <summary>
		/// Where build reports are saved to: <br/>
		/// 0: in user's My Documents <br/>
		/// 1: or outside the project folder.
		/// </summary>
		public int SaveType;

		public bool KeepCopyOfLogOfLastSuccessfulBuild = true;

		// ----------------------------------------------------------

		public bool CollectBuildInfo = true;
		public bool CalculateAssetDependencies = true;
		public bool CalculateAssetDependenciesOnUnusedToo = false;
		public bool CollectTextureImportSettings = true;
		public bool CollectTextureImportSettingsOnUnusedToo = false;
		public bool CollectMeshData = true;
		public bool CollectMeshDataOnUnusedToo;
		public bool CollectPrefabData = true;

		public bool GetProjectSettings = true;

		public bool IncludeUsedAssetsInReportCreation = true;
		public bool IncludeUnusedAssetsInReportCreation = true;
		public bool IncludeUnusedPrefabsInReportCreation = true;
		public bool IncludeBuildSizeInReportCreation = true;

		public bool IncludeSvnInUnused = true;
		public bool IncludeGitInUnused = true;
		public bool IncludeBuildReportToolAssetsInUnused = true;

		public bool GetSizeBeforeBuildForUsedAssets = true;
		public bool GetImportedSizesForUnusedAssets = true;

		public struct IgnorePattern
		{
			public int SearchType;
			public string Pattern;
		}
		public List<IgnorePattern> IgnorePatternsForUnused = new List<IgnorePattern>();

		// ----------------------------------------------------------

		public string FileFilterNameForTextureData = "Textures";

		public bool ShowTextureColumnTextureType = true;
		public bool ShowTextureColumnIsSRGB = false;
		public bool ShowTextureColumnAlphaSource = false;
		public bool ShowTextureColumnAlphaIsTransparency = false;
		public bool ShowTextureColumnIgnorePngGamma = false;
		// -----------------------
		public bool ShowTextureColumnNPotScale = false;
		public bool ShowTextureColumnIsReadable = false;
		public bool ShowTextureColumnMipMapGenerated = false;
		public bool ShowTextureColumnMipMapFilter = false;
		public bool ShowTextureColumnStreamingMipMaps = false;
		public bool ShowTextureColumnBorderMipMaps = false;
		public bool ShowTextureColumnPreserveCoverageMipMaps = false;
		public bool ShowTextureColumnFadeOutMipMaps = false;
		// -----------------------
		public bool ShowTextureColumnSpriteImportMode = false;
		public bool ShowTextureColumnSpritePackingTag = false;
		public bool ShowTextureColumnSpritePixelsPerUnit = false;
		public bool ShowTextureColumnQualifiesForSpritePacking = false;
		// -----------------------
		public bool ShowTextureColumnWrapMode = false;
		public bool ShowTextureColumnWrapModeU = false;
		public bool ShowTextureColumnWrapModeV = false;
		public bool ShowTextureColumnWrapModeW = false;
		public bool ShowTextureColumnFilterMode = false;
		public bool ShowTextureColumnAnisoLevel = false;
		// -----------------------
		public bool ShowTextureColumnMaxTextureSize = false;
		public bool ShowTextureColumnResizeAlgorithm = false;
		public bool ShowTextureColumnTextureFormat = true;
		public bool ShowTextureColumnCompressionType = false;
		public bool ShowTextureColumnCompressionIsCrunched = false;
		public bool ShowTextureColumnCompressionQuality = false;
		// -----------------------
		public bool ShowTextureColumnImportedWidthAndHeight = true;
		public bool ShowTextureColumnRealWidthAndHeight = false;

		// ----------------------------------------------------------

		public string FileFilterNameForMeshData = "Models";

		public bool ShowMeshColumnMeshFilterCount;
		public bool ShowMeshColumnSkinnedMeshRendererCount;
		public bool ShowMeshColumnSubMeshCount = true;
		public bool ShowMeshColumnVertexCount;
		public bool ShowMeshColumnTriangleCount = true;
		public bool ShowMeshColumnAnimationType;
		public bool ShowMeshColumnAnimationClipCount = true;

		// ----------------------------------------------------------

		public string FileFilterNameForPrefabData = "Prefabs";

		public bool ShowPrefabColumnContributeGI;
		public bool ShowPrefabColumnBatchingStatic = true;
		public bool ShowPrefabColumnOccluderStatic;
		public bool ShowPrefabColumnOccludeeStatic;
		public bool ShowPrefabColumnReflectionProbeStatic;

		public bool ShowPrefabColumnNavigationStatic;
		public bool ShowPrefabColumnOffMeshLinkGeneration;

		// ----------------------------------------------------------

		public bool ShowColumnAssetPath = true;
		public bool ShowColumnSizeBeforeBuild = true;
		public bool ShowColumnSizeInBuild = true;

		public bool ShowColumnUnusedRawSize = true;
		public bool ShowColumnUnusedImportedSize = true;

		// ----------------------------------------------------------

		public const int SEARCH_METHOD_BASIC = 0;
		public const int SEARCH_METHOD_REGEX = 50;
		public const int SEARCH_METHOD_FUZZY = 100;

		/// <summary>
		/// Method of Search:<br/>
		/// <see cref="SEARCH_METHOD_BASIC"/> <br/>
		/// <see cref="SEARCH_METHOD_REGEX"/> <br/>
		/// <see cref="SEARCH_METHOD_FUZZY"/>
		/// </summary>
		public int SearchType = SEARCH_METHOD_FUZZY;
		public bool SearchFilenameOnly = true;
		public bool SearchCaseSensitive = false;

		// ----------------------------------------------------------

		public bool ShowProjectSettingsInMultipleColumns = true;

		public int LogMessagePaginationLength = 100;

		// ----------------------------------------------------------

		/// <summary>
		/// 0: Use file filters from global config. <br/>
		/// 1: Use file filters embedded in the saved build report file.
		/// </summary>
		public int FilterToUseInt;

		public int AssetListPaginationLength = 300;
		public bool ProcessUnusedAssetsInBatches = true;
		public int UnusedAssetsEntriesPerBatch = 10000;

		public bool DoubleClickOnAssetWillPing = false;

		/// <summary>
		/// Method of displaying labels that explain how one asset uses another. <br/>
		/// 0: verbose (use words only) <br/>
		/// 1: standard (use arrows when possible, use words for extra info) <br/>
		/// 2: minimal (use arrows only, don't show extra info even if available)
		/// </summary>
		public int AssetUsageLabelType;

		public bool ShowAssetPrimaryUsersInTooltipIfAvailable = true;

		public bool ShowTooltipThumbnail = true;

		/// <summary>
		/// 0: Thumbnail should appear when mouse is hovering over asset icon only. <br/>
		/// 1: Thumbnail should appear when mouse is hovering over asset label too, not just on the icon.
		/// </summary>
		public int ShowThumbnailOnHoverType;

		public int TooltipThumbnailWidth = 256;
		public int TooltipThumbnailHeight = 256;

		public int TooltipThumbnailZoomedInWidth = 512;
		public int TooltipThumbnailZoomedInHeight = 512;

		public int NumberOfTopLargestUsedAssetsToShow = 10;
		public int NumberOfTopLargestUnusedAssetsToShow = 10;

		// ----------------------------------------------------------

		public bool AllowDeletingOfUsedAssets;
		public bool ShowImportedSizeForUsedAssets = false;

		public bool AutoShowWindowAfterNormalBuild = true;
		public bool AutoResortAssetsWhenUnityEditorRegainsFocus = false;

		public bool UseThreadedReportGeneration = true;
		public bool UseThreadedFileLoading = false;

		// ----------------------------------------------------------

		public void OnBeforeSave()
		{
			// get rid of invalid characters for folder name
			// but still allow slash/backward slash so user could make relative paths

			BuildReportFolderName = BuildReportFolderName.Replace(":", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace("*", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace("?", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace("\"", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace("<", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace(">", string.Empty);
			BuildReportFolderName = BuildReportFolderName.Replace("|", string.Empty);
		}

		public static void Save(string savePath, SavedOptions optionsToSave)
		{
			optionsToSave.OnBeforeSave();

			XmlSerializer x = new XmlSerializer(typeof(SavedOptions));
			TextWriter writer = new StreamWriter(savePath);
			x.Serialize(writer, optionsToSave);
			writer.Close();

			//Debug.LogFormat("Build Report Tool: Saved options to: {0}", savePath);
		}

		public static SavedOptions Load(string path)
		{
			SavedOptions result = null;

			XmlSerializer x = new XmlSerializer(typeof(SavedOptions));

			try
			{
				using (FileStream fs = new FileStream(path, FileMode.Open))
				{
					if (fs.Length == 0)
					{
						// nothing inside
						return null;
					}

					XmlReader reader = new XmlTextReader(fs);
					result = (SavedOptions) x.Deserialize(reader);
					fs.Close();
				}
			}
			catch (Exception e)
			{
				Debug.LogFormat(
					"Build Report Tool: Error found upon loading options XML file in {0}\nWill create a new options file instead.\n\nError: {1}",
					path, e);
				return new SavedOptions();
			}

			//Debug.LogFormat("Build Report Tool: Loaded options from: {0}", path);
			return result;
		}
	}

	public enum SearchType
	{
		Basic,
		Regex = 50,
		Fuzzy = 100
	}

	public static class Options
	{
		// =======================================================
		// constants
		public const string BUILD_REPORT_PACKAGE_MOVED_MSG = "BuildReport package seems to have been moved. Finding...";

		public const string BUILD_REPORT_PACKAGE_MISSING_MSG =
			"Unable to find BuildReport package folder! Cannot find suitable GUI Skin.\nTry editing the source code and change the value\nof `BUILD_REPORT_TOOL_DEFAULT_PATH` to what path the Build Report Tool is in.\nMake sure the folder is named \"BuildReport\".";

		public const string BUILD_REPORT_GUI_SKIN_MISSING_MSG =
			"Unable to find BuildReport's GUI Skin! The GUI will not render properly.\nTry editing the source code and change the value\nof `BUILD_REPORT_TOOL_DEFAULT_PATH` to what path the Build Report Tool is in.";

		public const string BUILD_REPORT_TOOL_DEFAULT_PATH = "Assets/BuildReport";
		public const string BUILD_REPORT_TOOL_DEFAULT_FOLDER_NAME = "BuildReport";

		public const string BUILD_REPORTS_DEFAULT_FOLDER_NAME = "UnityBuildReports";


		public const int SAVE_TYPE_PERSONAL = 0;
		public const int SAVE_TYPE_PROJECT = 1;
		public const int SAVE_TYPE_CUSTOM = 2;


		public const int ASSET_USAGE_LABEL_TYPE_VERBOSE = 0;
		public const int ASSET_USAGE_LABEL_TYPE_STANDARD = 1;
		public const int ASSET_USAGE_LABEL_TYPE_MINIMAL = 2;

		// =======================================================
		//
		static BuildReportTool.SavedOptions _savedOptions;
		static string _foundPathForSavedOptions;
		const string SAVED_OPTIONS_FILENAME = "BuildReportToolOptions.xml";

		static string DefaultOptionsPath
		{
			get { return string.Format("{0}ProjectSettings/{1}", BuildReportTool.Util.GetProjectPath(Application.dataPath), SAVED_OPTIONS_FILENAME); }
		}

		static bool IsBuildReportInRegularPaths
		{
			get
			{
				return Directory.Exists(string.Format("{0}/BuildReport", Application.dataPath)) ||
				       Directory.Exists(string.Format("{0}/Plugins/BuildReport", Application.dataPath));
			}
		}

		public static string FoundPathForSavedOptions
		{
			get { return _foundPathForSavedOptions; }
		}

		static void InitializeOptionsIfNeeded()
		{
			if (_savedOptions == null)
			{
				_foundPathForSavedOptions = string.Empty;
			}

			if (string.IsNullOrEmpty(_foundPathForSavedOptions))
			{
				// look for the file in this order:
				// 1. inside the BuildReport folder
				// 2. at the very topmost Assets folder
				// 3. outside the Assets folder
				// 4. in the ProjectSettings folder
				// 5. in the User's My Documents folder


				// ---------------------------------------------------
				// look in /Assets/BuildReport/
				var optionsInBuildReportFolder = DefaultOptionsPath;
				if (File.Exists(optionsInBuildReportFolder))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsInBuildReportFolder);
					_foundPathForSavedOptions = optionsInBuildReportFolder;
					return;
				}

				// ---------------------------------------------------
				// look in /Assets/Plugins/BuildReport/
				var optionsInPluginsBuildReport = string.Format("{0}/Plugins/BuildReport/{1}", Application.dataPath,
					SAVED_OPTIONS_FILENAME);
				if (File.Exists(optionsInPluginsBuildReport))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsInPluginsBuildReport);
					_foundPathForSavedOptions = optionsInPluginsBuildReport;
					return;
				}

				// ---------------------------------------------------
				// search for "BuildReport" folder and look in there
				if (!IsBuildReportInRegularPaths)
				{
					string customBuildReportFolder =
						BuildReportTool.Util.FindAssetFolder(Application.dataPath, BUILD_REPORT_TOOL_DEFAULT_FOLDER_NAME);
					if (!string.IsNullOrEmpty(customBuildReportFolder))
					{
						var optionsInCustomBuildReportFolder =
							string.Format("{0}/{1}", customBuildReportFolder, SAVED_OPTIONS_FILENAME);
						if (File.Exists(optionsInCustomBuildReportFolder))
						{
							_savedOptions = BuildReportTool.SavedOptions.Load(optionsInCustomBuildReportFolder);
							_foundPathForSavedOptions = optionsInCustomBuildReportFolder;
							return;
						}
					}
				}

				// ---------------------------------------------------
				// look in /Assets/
				var optionsInTopmostAssets = string.Format("{0}/{1}", Application.dataPath, SAVED_OPTIONS_FILENAME);
				if (File.Exists(optionsInTopmostAssets))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsInTopmostAssets);
					_foundPathForSavedOptions = optionsInTopmostAssets;
					return;
				}

				// ---------------------------------------------------
				// look in Unity project folder (where Assets, Library, and ProjectSettings folder are)
				var outsideAssets = BuildReportTool.Util.GetProjectPath(Application.dataPath);
				var optionsOutsideAssets = string.Format("{0}{1}", outsideAssets, SAVED_OPTIONS_FILENAME);
				if (File.Exists(optionsOutsideAssets))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsOutsideAssets);
					_foundPathForSavedOptions = optionsOutsideAssets;
					return;
				}

				// ---------------------------------------------------
				// look inside ProjectSettings folder
				var optionsInProjectSettings =
					string.Format("{0}ProjectSettings/{1}", outsideAssets, SAVED_OPTIONS_FILENAME);
				//Debug.LogFormat("Looking in {0}", optionsInProjectSettings);
				if (File.Exists(optionsInProjectSettings))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsInProjectSettings);
					_foundPathForSavedOptions = optionsInProjectSettings;
					return;
				}

				// ---------------------------------------------------
				// look in /My Documents/UnityBuildReports/
				var optionsInMyDocs = string.Format("{0}/{1}/{2}", BuildReportTool.Util.GetUserHomeFolder(),
					BUILD_REPORTS_DEFAULT_FOLDER_NAME, SAVED_OPTIONS_FILENAME);
				//Debug.LogFormat("Looking in {0}", optionsInMyDocs);
				if (File.Exists(optionsInMyDocs))
				{
					_savedOptions = BuildReportTool.SavedOptions.Load(optionsInMyDocs);
					_foundPathForSavedOptions = optionsInMyDocs;
					return;
				}

				// ---------------------------------------------------
			}

			// if the options file failed to load
			// one last try
			//
			if (_savedOptions == null)
			{
				if (!string.IsNullOrEmpty(_foundPathForSavedOptions) && File.Exists(_foundPathForSavedOptions))
				{
					// there's a valid options file already
					// just load that one
					_savedOptions = BuildReportTool.SavedOptions.Load(_foundPathForSavedOptions);
				}
			}

			// could not load the file, or there isn't one yet (at least, not in any recognized valid paths).
			// so create a new one at the default path
			if (_savedOptions == null)
			{
				_savedOptions = new BuildReportTool.SavedOptions();
				_foundPathForSavedOptions = DefaultOptionsPath;

				var defaultFolder = Path.GetDirectoryName(_foundPathForSavedOptions);
				if (!string.IsNullOrEmpty(defaultFolder) && !Directory.Exists(defaultFolder))
				{
					Directory.CreateDirectory(defaultFolder);
				}

				SavedOptions.Save(_foundPathForSavedOptions, _savedOptions);
				Debug.LogFormat("Build Report Tool: Created a new options file at: {0}", _foundPathForSavedOptions);
			}
		}

		public static void RefreshOptions()
		{
			_foundPathForSavedOptions = string.Empty;
			_savedOptions = null;
			InitializeOptionsIfNeeded();
		}

		public static void SaveOptions()
		{
			if (string.IsNullOrEmpty(_foundPathForSavedOptions))
			{
				return;
			}

			if (_savedOptions == null || !File.Exists(_foundPathForSavedOptions))
			{
				_foundPathForSavedOptions = string.Empty;
				return;
			}

			SavedOptions.Save(_foundPathForSavedOptions, _savedOptions);
		}

		// =======================================================
		// user options

		public static string EditorLogOverridePath
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.EditorLogOverridePath;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.EditorLogOverridePath != value)
				{
					_savedOptions.EditorLogOverridePath = value;
					SaveOptions();
				}
			}
		}

		public static bool KeepCopyOfLogOfLastSuccessfulBuild
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.KeepCopyOfLogOfLastSuccessfulBuild;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.KeepCopyOfLogOfLastSuccessfulBuild != value)
				{
					_savedOptions.KeepCopyOfLogOfLastSuccessfulBuild = value;
					SaveOptions();
				}
			}
		}

		public static bool IncludeSvnInUnused
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeSvnInUnused;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeSvnInUnused != value)
				{
					_savedOptions.IncludeSvnInUnused = value;
					SaveOptions();
				}
			}
		}

		public static bool IncludeGitInUnused
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeGitInUnused;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeGitInUnused != value)
				{
					_savedOptions.IncludeGitInUnused = value;
					SaveOptions();
				}
			}
		}

		public static bool IncludeBuildReportToolAssetsInUnused
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeBuildReportToolAssetsInUnused;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeBuildReportToolAssetsInUnused != value)
				{
					_savedOptions.IncludeBuildReportToolAssetsInUnused = value;
					SaveOptions();
				}
			}
		}

		public static List<SavedOptions.IgnorePattern> IgnorePatternsForUnused
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IgnorePatternsForUnused;
			}
		}

		public static FileFilterDisplay GetOptionFileFilterDisplay()
		{
			return FileFilterDisplay.DropDown;
		}

		public static bool AllowDeletingOfUsedAssets
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AllowDeletingOfUsedAssets;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.AllowDeletingOfUsedAssets != value)
				{
					_savedOptions.AllowDeletingOfUsedAssets = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectBuildInfo
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectBuildInfo;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectBuildInfo != value)
				{
					_savedOptions.CollectBuildInfo = value;
					SaveOptions();
				}
			}
		}

		public static bool CalculateAssetDependencies
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CalculateAssetDependencies;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CalculateAssetDependencies != value)
				{
					_savedOptions.CalculateAssetDependencies = value;
					SaveOptions();
				}
			}
		}

		public static bool CalculateAssetDependenciesOnUnusedToo
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CalculateAssetDependenciesOnUnusedToo;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CalculateAssetDependenciesOnUnusedToo != value)
				{
					_savedOptions.CalculateAssetDependenciesOnUnusedToo = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectTextureImportSettings
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectTextureImportSettings;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectTextureImportSettings != value)
				{
					_savedOptions.CollectTextureImportSettings = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectTextureImportSettingsOnUnusedToo
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectTextureImportSettingsOnUnusedToo;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectTextureImportSettingsOnUnusedToo != value)
				{
					_savedOptions.CollectTextureImportSettingsOnUnusedToo = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectMeshData
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectMeshData;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectMeshData != value)
				{
					_savedOptions.CollectMeshData = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectMeshDataOnUnusedToo
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectMeshDataOnUnusedToo;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectMeshDataOnUnusedToo != value)
				{
					_savedOptions.CollectMeshDataOnUnusedToo = value;
					SaveOptions();
				}
			}
		}

		public static bool CollectPrefabData
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.CollectPrefabData;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.CollectPrefabData != value)
				{
					_savedOptions.CollectPrefabData = value;
					SaveOptions();
				}
			}
		}

		public static string BuildReportFolderName
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.BuildReportFolderName;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.BuildReportFolderName != value)
				{
					_savedOptions.BuildReportFolderName = value;
					SaveOptions();
				}
			}
		}

		public static string BuildReportCustomOutputPath
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.BuildReportCustomOutputPath;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.BuildReportCustomOutputPath != value)
				{
					_savedOptions.BuildReportCustomOutputPath = value;
					SaveOptions();
				}
			}
		}


		/// <summary>
		/// Full path to folder where Build Reports are saved.
		/// Note: Makes use of Application.dataPath so it has to be called from the main thread.
		/// </summary>
		public static string BuildReportSavePath
		{
			get
			{
				if (BuildReportTool.Options.SaveType == BuildReportTool.Options.SAVE_TYPE_PERSONAL)
				{
					return string.Format("{0}/{1}", BuildReportTool.Util.GetUserHomeFolder(), BuildReportFolderName);
				}
				else if (BuildReportTool.Options.SaveType == BuildReportTool.Options.SAVE_TYPE_PROJECT)
				{
					// makes use of Application.dataPath so it has to be called from the main thread
					return string.Format("{0}/{1}", BuildReportTool.ReportGenerator.GetSavePathToProjectFolder(), BuildReportFolderName);
				}
				else // BuildReportTool.Options.SAVE_TYPE_CUSTOM
				{
					if (Path.IsPathRooted(BuildReportCustomOutputPath))
					{
						return BuildReportCustomOutputPath;
					}
					else
					{
						// relative path? it will be relative to the project folder
						return Path.GetFullPath(Path.Combine(BuildReportTool.Util.GetProjectPath(Application.dataPath),
							BuildReportCustomOutputPath));
					}
				}
			}
		}


		public static int SaveType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.SaveType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.SaveType != value)
				{
					_savedOptions.SaveType = value;
					SaveOptions();
				}
			}
		}

		public enum FileFilterDisplay
		{
			DropDown = 0,
			Buttons = 1
		}


		public enum FilterToUseType
		{
			UseConfiguredFile,
			UseEmbedded
		}

		public static FilterToUseType GetOptionFilterToUse()
		{
			switch (FilterToUseInt)
			{
				case 0:
					return FilterToUseType.UseConfiguredFile;
				case 1:
					return FilterToUseType.UseEmbedded;
			}

			return FilterToUseType.UseConfiguredFile;
		}

		public static bool ShouldUseConfiguredFileFilters()
		{
			//Debug.Log("GetOptionFilterToUse() " + GetOptionFilterToUse());
			return GetOptionFilterToUse() == FilterToUseType.UseConfiguredFile;
		}

		public static bool ShouldUseEmbeddedFileFilters()
		{
			return GetOptionFilterToUse() == FilterToUseType.UseEmbedded;
		}

		public static int FilterToUseInt
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.FilterToUseInt;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.FilterToUseInt != value)
				{
					_savedOptions.FilterToUseInt = value;
					SaveOptions();
				}
			}
		}


		public static int AssetListPaginationLength
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AssetListPaginationLength;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.AssetListPaginationLength != value)
				{
					_savedOptions.AssetListPaginationLength = value;
					SaveOptions();
				}
			}
		}


		public static int AssetUsageLabelType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AssetUsageLabelType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.AssetUsageLabelType != value)
				{
					_savedOptions.AssetUsageLabelType = value;
					SaveOptions();
				}
			}
		}

		public static bool IsAssetUsageLabelTypeOnVerbose
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AssetUsageLabelType == ASSET_USAGE_LABEL_TYPE_VERBOSE;
			}
		}

		public static bool IsAssetUsageLabelTypeOnStandard
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AssetUsageLabelType == ASSET_USAGE_LABEL_TYPE_STANDARD;
			}
		}

		public static bool IsAssetUsageLabelTypeOnMinimal
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AssetUsageLabelType == ASSET_USAGE_LABEL_TYPE_MINIMAL;
			}
		}


		public static bool DoubleClickOnAssetWillPing
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.DoubleClickOnAssetWillPing;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.DoubleClickOnAssetWillPing != value)
				{
					_savedOptions.DoubleClickOnAssetWillPing = value;
					SaveOptions();
				}
			}
		}


		public static bool ShowAssetPrimaryUsersInTooltipIfAvailable
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowAssetPrimaryUsersInTooltipIfAvailable;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowAssetPrimaryUsersInTooltipIfAvailable != value)
				{
					_savedOptions.ShowAssetPrimaryUsersInTooltipIfAvailable = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTooltipThumbnail
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTooltipThumbnail;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTooltipThumbnail != value)
				{
					_savedOptions.ShowTooltipThumbnail = value;
					SaveOptions();
				}
			}
		}

		public static int ShowThumbnailOnHoverType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowThumbnailOnHoverType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowThumbnailOnHoverType != value)
				{
					_savedOptions.ShowThumbnailOnHoverType = value;
					SaveOptions();
				}
			}
		}

		/// <summary>
		/// If thumbnail should appear when mouse is hovering over asset label too, not just on the icon.
		/// </summary>
		public static bool ShowThumbnailOnHoverLabelToo
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowThumbnailOnHoverType == 1;
			}
		}

		public static int TooltipThumbnailWidth
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.TooltipThumbnailWidth;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.TooltipThumbnailWidth != value)
				{
					_savedOptions.TooltipThumbnailWidth = value;
					SaveOptions();
				}
			}
		}

		public static int TooltipThumbnailHeight
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.TooltipThumbnailHeight;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.TooltipThumbnailHeight != value)
				{
					_savedOptions.TooltipThumbnailHeight = value;
					SaveOptions();
				}
			}
		}


		public static int TooltipThumbnailZoomedInWidth
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.TooltipThumbnailZoomedInWidth;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.TooltipThumbnailZoomedInWidth != value)
				{
					_savedOptions.TooltipThumbnailZoomedInWidth = value;
					SaveOptions();
				}
			}
		}

		public static int TooltipThumbnailZoomedInHeight
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.TooltipThumbnailZoomedInHeight;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.TooltipThumbnailZoomedInHeight != value)
				{
					_savedOptions.TooltipThumbnailZoomedInHeight = value;
					SaveOptions();
				}
			}
		}

		public static bool ProcessUnusedAssetsInBatches
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ProcessUnusedAssetsInBatches;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ProcessUnusedAssetsInBatches != value)
				{
					_savedOptions.ProcessUnusedAssetsInBatches = value;
					SaveOptions();
				}
			}
		}

		public static int UnusedAssetsEntriesPerBatch
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.UnusedAssetsEntriesPerBatch;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.UnusedAssetsEntriesPerBatch != value)
				{
					_savedOptions.UnusedAssetsEntriesPerBatch = value;
					SaveOptions();
				}
			}
		}

		public static int NumberOfTopLargestUsedAssetsToShow
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.NumberOfTopLargestUsedAssetsToShow;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.NumberOfTopLargestUsedAssetsToShow != value)
				{
					_savedOptions.NumberOfTopLargestUsedAssetsToShow = value;
					SaveOptions();
				}
			}
		}

		public static int NumberOfTopLargestUnusedAssetsToShow
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.NumberOfTopLargestUnusedAssetsToShow;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.NumberOfTopLargestUnusedAssetsToShow != value)
				{
					_savedOptions.NumberOfTopLargestUnusedAssetsToShow = value;
					SaveOptions();
				}
			}
		}

		// Build Report Calculation
		//  Full report
		//  No prefabs in unused assets calculation
		//  No unused assets calculation, but still has used assets list (won't collect prefabs in scene)
		//  No used assets and unused assets calculation (overview only)

		public static bool IncludeUsedAssetsInReportCreation
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeUsedAssetsInReportCreation;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeUsedAssetsInReportCreation != value)
				{
					_savedOptions.IncludeUsedAssetsInReportCreation = value;
					SaveOptions();
				}
			}
		}

		public static bool IncludeUnusedAssetsInReportCreation
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeUnusedAssetsInReportCreation;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeUnusedAssetsInReportCreation != value)
				{
					_savedOptions.IncludeUnusedAssetsInReportCreation = value;
					SaveOptions();
				}
			}
		}

		public static bool IncludeUnusedPrefabsInReportCreation
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeUnusedPrefabsInReportCreation;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeUnusedPrefabsInReportCreation != value)
				{
					_savedOptions.IncludeUnusedPrefabsInReportCreation = value;
					SaveOptions();
				}
			}
		}


		public static bool IncludeBuildSizeInReportCreation
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.IncludeBuildSizeInReportCreation;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.IncludeBuildSizeInReportCreation != value)
				{
					_savedOptions.IncludeBuildSizeInReportCreation = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowImportedSizeForUsedAssets
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowImportedSizeForUsedAssets;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowImportedSizeForUsedAssets != value)
				{
					_savedOptions.ShowImportedSizeForUsedAssets = value;
					SaveOptions();
				}
			}
		}

		public static bool GetSizeBeforeBuildForUsedAssets
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.GetSizeBeforeBuildForUsedAssets;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.GetSizeBeforeBuildForUsedAssets != value)
				{
					_savedOptions.GetSizeBeforeBuildForUsedAssets = value;
					SaveOptions();
				}
			}
		}

		public static bool GetImportedSizesForUnusedAssets
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.GetImportedSizesForUnusedAssets;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.GetImportedSizesForUnusedAssets != value)
				{
					_savedOptions.GetImportedSizesForUnusedAssets = value;
					SaveOptions();
				}
			}
		}

		public static string FileFilterNameForTextureData
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.FileFilterNameForTextureData;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.FileFilterNameForTextureData != value)
				{
					_savedOptions.FileFilterNameForTextureData = value;
					SaveOptions();
				}
			}
		}

		// -----------------------------------------------------------------

		public static bool ShowTextureColumnTextureType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnTextureType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnTextureType != value)
				{
					_savedOptions.ShowTextureColumnTextureType = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnIsSRGB
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnIsSRGB;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnIsSRGB != value)
				{
					_savedOptions.ShowTextureColumnIsSRGB = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnAlphaSource
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnAlphaSource;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnAlphaSource != value)
				{
					_savedOptions.ShowTextureColumnAlphaSource = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnAlphaIsTransparency
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnAlphaIsTransparency;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnAlphaIsTransparency != value)
				{
					_savedOptions.ShowTextureColumnAlphaIsTransparency = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnIgnorePngGamma
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnIgnorePngGamma;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnIgnorePngGamma != value)
				{
					_savedOptions.ShowTextureColumnIgnorePngGamma = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnNPotScale
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnNPotScale;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnNPotScale != value)
				{
					_savedOptions.ShowTextureColumnNPotScale = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnIsReadable
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnIsReadable;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnIsReadable != value)
				{
					_savedOptions.ShowTextureColumnIsReadable = value;
					SaveOptions();
				}
			}
		}

		// ----------------------------------------------

		public static bool ShowTextureColumnMipMapGenerated
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnMipMapGenerated;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnMipMapGenerated != value)
				{
					_savedOptions.ShowTextureColumnMipMapGenerated = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnMipMapFilter
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnMipMapFilter;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnMipMapFilter != value)
				{
					_savedOptions.ShowTextureColumnMipMapFilter = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnStreamingMipMaps
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnStreamingMipMaps;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnStreamingMipMaps != value)
				{
					_savedOptions.ShowTextureColumnStreamingMipMaps = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnBorderMipMaps
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnBorderMipMaps;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnBorderMipMaps != value)
				{
					_savedOptions.ShowTextureColumnBorderMipMaps = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnPreserveCoverageMipMaps
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnPreserveCoverageMipMaps;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnPreserveCoverageMipMaps != value)
				{
					_savedOptions.ShowTextureColumnPreserveCoverageMipMaps = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnFadeOutMipMaps
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnFadeOutMipMaps;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnFadeOutMipMaps != value)
				{
					_savedOptions.ShowTextureColumnFadeOutMipMaps = value;
					SaveOptions();
				}
			}
		}

		// ----------------------------------------------

		public static bool ShowTextureColumnSpriteImportMode
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnSpriteImportMode;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnSpriteImportMode != value)
				{
					_savedOptions.ShowTextureColumnSpriteImportMode = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnSpritePackingTag
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnSpritePackingTag;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnSpritePackingTag != value)
				{
					_savedOptions.ShowTextureColumnSpritePackingTag = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnSpritePixelsPerUnit
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnSpritePixelsPerUnit;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnSpritePixelsPerUnit != value)
				{
					_savedOptions.ShowTextureColumnSpritePixelsPerUnit = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnQualifiesForSpritePacking
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnQualifiesForSpritePacking;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnQualifiesForSpritePacking != value)
				{
					_savedOptions.ShowTextureColumnQualifiesForSpritePacking = value;
					SaveOptions();
				}
			}
		}

		// ----------------------------------------------

		public static bool ShowTextureColumnWrapMode
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnWrapMode;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnWrapMode != value)
				{
					_savedOptions.ShowTextureColumnWrapMode = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnWrapModeU
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnWrapModeU;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnWrapModeU != value)
				{
					_savedOptions.ShowTextureColumnWrapModeU = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnWrapModeV
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnWrapModeV;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnWrapModeV != value)
				{
					_savedOptions.ShowTextureColumnWrapModeV = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnWrapModeW
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnWrapModeW;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnWrapModeW != value)
				{
					_savedOptions.ShowTextureColumnWrapModeW = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnFilterMode
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnFilterMode;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnFilterMode != value)
				{
					_savedOptions.ShowTextureColumnFilterMode = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnAnisoLevel
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnAnisoLevel;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnAnisoLevel != value)
				{
					_savedOptions.ShowTextureColumnAnisoLevel = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnMaxTextureSize
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnMaxTextureSize;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnMaxTextureSize != value)
				{
					_savedOptions.ShowTextureColumnMaxTextureSize = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnResizeAlgorithm
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnResizeAlgorithm;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnResizeAlgorithm != value)
				{
					_savedOptions.ShowTextureColumnResizeAlgorithm = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnTextureFormat
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnTextureFormat;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnTextureFormat != value)
				{
					_savedOptions.ShowTextureColumnTextureFormat = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnCompressionType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnCompressionType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnCompressionType != value)
				{
					_savedOptions.ShowTextureColumnCompressionType = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnCompressionIsCrunched
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnCompressionIsCrunched;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnCompressionIsCrunched != value)
				{
					_savedOptions.ShowTextureColumnCompressionIsCrunched = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnCompressionQuality
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnCompressionQuality;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnCompressionQuality != value)
				{
					_savedOptions.ShowTextureColumnCompressionQuality = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnImportedWidthAndHeight
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnImportedWidthAndHeight;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnImportedWidthAndHeight != value)
				{
					_savedOptions.ShowTextureColumnImportedWidthAndHeight = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowTextureColumnRealWidthAndHeight
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowTextureColumnRealWidthAndHeight;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowTextureColumnRealWidthAndHeight != value)
				{
					_savedOptions.ShowTextureColumnRealWidthAndHeight = value;
					SaveOptions();
				}
			}
		}

		// -----------------------------------------------------------------

		public static string FileFilterNameForMeshData
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.FileFilterNameForMeshData;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.FileFilterNameForMeshData != value)
				{
					_savedOptions.FileFilterNameForMeshData = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnMeshFilterCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnMeshFilterCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnMeshFilterCount != value)
				{
					_savedOptions.ShowMeshColumnMeshFilterCount = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnSkinnedMeshRendererCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnSkinnedMeshRendererCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnSkinnedMeshRendererCount != value)
				{
					_savedOptions.ShowMeshColumnSkinnedMeshRendererCount = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnSubMeshCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnSubMeshCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnSubMeshCount != value)
				{
					_savedOptions.ShowMeshColumnSubMeshCount = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnVertexCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnVertexCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnVertexCount != value)
				{
					_savedOptions.ShowMeshColumnVertexCount = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnTriangleCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnTriangleCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnTriangleCount != value)
				{
					_savedOptions.ShowMeshColumnTriangleCount = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnAnimationType
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnAnimationType;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnAnimationType != value)
				{
					_savedOptions.ShowMeshColumnAnimationType = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowMeshColumnAnimationClipCount
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowMeshColumnAnimationClipCount;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowMeshColumnAnimationClipCount != value)
				{
					_savedOptions.ShowMeshColumnAnimationClipCount = value;
					SaveOptions();
				}
			}
		}

		// -----------------------------------------------------------------

		public static string FileFilterNameForPrefabData
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.FileFilterNameForPrefabData;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.FileFilterNameForPrefabData != value)
				{
					_savedOptions.FileFilterNameForPrefabData = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnContributeGI
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnContributeGI;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnContributeGI != value)
				{
					_savedOptions.ShowPrefabColumnContributeGI = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnBatchingStatic
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnBatchingStatic;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnBatchingStatic != value)
				{
					_savedOptions.ShowPrefabColumnBatchingStatic = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnOccluderStatic
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnOccluderStatic;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnOccluderStatic != value)
				{
					_savedOptions.ShowPrefabColumnOccluderStatic = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnOccludeeStatic
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnOccludeeStatic;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnOccludeeStatic != value)
				{
					_savedOptions.ShowPrefabColumnOccludeeStatic = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnReflectionProbeStatic
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnReflectionProbeStatic;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnReflectionProbeStatic != value)
				{
					_savedOptions.ShowPrefabColumnReflectionProbeStatic = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnNavigationStatic
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnNavigationStatic;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnNavigationStatic != value)
				{
					_savedOptions.ShowPrefabColumnNavigationStatic = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowPrefabColumnOffMeshLinkGeneration
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowPrefabColumnOffMeshLinkGeneration;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowPrefabColumnOffMeshLinkGeneration != value)
				{
					_savedOptions.ShowPrefabColumnOffMeshLinkGeneration = value;
					SaveOptions();
				}
			}
		}

		// -----------------------------------------------------------------

		public static bool ShowColumnAssetPath
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowColumnAssetPath;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowColumnAssetPath != value)
				{
					_savedOptions.ShowColumnAssetPath = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowColumnSizeBeforeBuild
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowColumnSizeBeforeBuild;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowColumnSizeBeforeBuild != value)
				{
					_savedOptions.ShowColumnSizeBeforeBuild = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowColumnSizeInBuild
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowColumnSizeInBuild;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowColumnSizeInBuild != value)
				{
					_savedOptions.ShowColumnSizeInBuild = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowColumnUnusedRawSize
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowColumnUnusedRawSize;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowColumnUnusedRawSize != value)
				{
					_savedOptions.ShowColumnUnusedRawSize = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowColumnUnusedImportedSize
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowColumnUnusedImportedSize;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowColumnUnusedImportedSize != value)
				{
					_savedOptions.ShowColumnUnusedImportedSize = value;
					SaveOptions();
				}
			}
		}

		// -----------------------------------------------------------------

		static SearchType _previousSearchType = SearchType;

		public static bool HasSearchTypeChanged
		{
			get { return _previousSearchType != SearchType; }
		}

		public static void UpdatePreviousSearchType()
		{
			_previousSearchType = SearchType;
		}

		public static SearchType SearchType
		{
			get
			{
				InitializeOptionsIfNeeded();
				switch (_savedOptions.SearchType)
				{
					case SavedOptions.SEARCH_METHOD_REGEX:
						return SearchType.Regex;
					case SavedOptions.SEARCH_METHOD_FUZZY:
						return SearchType.Fuzzy;
					default:
						return SearchType.Basic;
				}
			}
			set
			{
				InitializeOptionsIfNeeded();

				int newValue;
				switch (value)
				{
					case SearchType.Regex:
						newValue = SavedOptions.SEARCH_METHOD_REGEX;
						break;
					case SearchType.Fuzzy:
						newValue = SavedOptions.SEARCH_METHOD_FUZZY;
						break;
					default:
						newValue = SavedOptions.SEARCH_METHOD_BASIC;
						break;
				}
				if (_savedOptions.SearchType != newValue)
				{
					_savedOptions.SearchType = newValue;
					SaveOptions();
				}
			}
		}

		public const int SEARCH_TYPE_BASIC = 0;
		public const int SEARCH_TYPE_REGEX = 1;
		public const int SEARCH_TYPE_FUZZY = 2;

		public static bool SearchTypeIsBasic
		{
			get
			{
				return SearchTypeInt == SEARCH_TYPE_BASIC;
			}
		}

		public static bool SearchTypeIsRegex
		{
			get
			{
				return SearchTypeInt == SEARCH_TYPE_REGEX;
			}
		}

		public static bool SearchTypeIsFuzzy
		{
			get
			{
				return SearchTypeInt == SEARCH_TYPE_FUZZY;
			}
		}

		public static int SearchTypeInt
		{
			get
			{
				InitializeOptionsIfNeeded();
				switch (_savedOptions.SearchType)
				{
					case SavedOptions.SEARCH_METHOD_REGEX:
						return SEARCH_TYPE_REGEX;
					case SavedOptions.SEARCH_METHOD_FUZZY:
						return SEARCH_TYPE_FUZZY;
					default:
						return SEARCH_TYPE_BASIC;
				}
			}
			set
			{
				InitializeOptionsIfNeeded();

				int newValue;
				switch (value)
				{
					case SEARCH_TYPE_REGEX:
						newValue = SavedOptions.SEARCH_METHOD_REGEX;
						break;
					case SEARCH_TYPE_FUZZY:
						newValue = SavedOptions.SEARCH_METHOD_FUZZY;
						break;
					default:
						newValue = SavedOptions.SEARCH_METHOD_BASIC;
						break;
				}
				if (_savedOptions.SearchType != newValue)
				{
					_savedOptions.SearchType = newValue;
					SaveOptions();
				}
			}
		}

		public static bool SearchFilenameOnly
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.SearchFilenameOnly;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.SearchFilenameOnly != value)
				{
					_savedOptions.SearchFilenameOnly = value;
					SaveOptions();
				}
			}
		}

		public static bool SearchCaseSensitive
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.SearchCaseSensitive;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.SearchCaseSensitive != value)
				{
					_savedOptions.SearchCaseSensitive = value;
					SaveOptions();
				}
			}
		}

		public static bool GetProjectSettings
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.GetProjectSettings;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.GetProjectSettings != value)
				{
					_savedOptions.GetProjectSettings = value;
					SaveOptions();
				}
			}
		}

		public static bool ShowProjectSettingsInMultipleColumns
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.ShowProjectSettingsInMultipleColumns;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.ShowProjectSettingsInMultipleColumns != value)
				{
					_savedOptions.ShowProjectSettingsInMultipleColumns = value;
					SaveOptions();
				}
			}
		}

		public static int LogMessagePaginationLength
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.LogMessagePaginationLength;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.LogMessagePaginationLength != value)
				{
					_savedOptions.LogMessagePaginationLength = value;
					SaveOptions();
				}
			}
		}

		public static bool IsCalculationLevelAtFull(bool includeUsedAssets, bool includeUnusedAssets,
			bool includeUnusedPrefabs)
		{
			return includeUsedAssets && includeUnusedAssets && includeUnusedPrefabs;
		}

		public static bool IsCalculationLevelAtNoUnusedPrefabs(bool includeUsedAssets, bool includeUnusedAssets,
			bool includeUnusedPrefabs)
		{
			return includeUsedAssets && includeUnusedAssets && !includeUnusedPrefabs;
		}

		public static bool IsCalculationLevelAtNoUnusedAssets(bool includeUsedAssets, bool includeUnusedAssets,
			bool includeUnusedPrefabs)
		{
			// unused prefabs are not checked. if unused assets are not calculated, it is understood that unused prefabs are not included
			return includeUsedAssets && !includeUnusedAssets;
		}

		public static bool IsCalculationLevelAtOverviewOnly(bool includeUsedAssets, bool includeUnusedAssets,
			bool includeUnusedPrefabs)
		{
			// if used assets not included, it is understood that unused assets are not included too.
			// if used assets are not included, there is no way to determing if an asset is unused.
			return !includeUsedAssets;
		}


		public static bool IsCurrentCalculationLevelAtFull
		{
			get
			{
				return IsCalculationLevelAtFull(IncludeUsedAssetsInReportCreation, IncludeUnusedAssetsInReportCreation,
					IncludeUnusedPrefabsInReportCreation);
			}
		}

		public static bool IsCurrentCalculationLevelAtNoUnusedPrefabs
		{
			get
			{
				return IsCalculationLevelAtNoUnusedPrefabs(IncludeUsedAssetsInReportCreation,
					IncludeUnusedAssetsInReportCreation, IncludeUnusedPrefabsInReportCreation);
			}
		}

		public static bool IsCurrentCalculationLevelAtNoUnusedAssets
		{
			get
			{
				return IsCalculationLevelAtNoUnusedAssets(IncludeUsedAssetsInReportCreation,
					IncludeUnusedAssetsInReportCreation, IncludeUnusedPrefabsInReportCreation);
			}
		}

		public static bool IsCurrentCalculationLevelAtOverviewOnly
		{
			get
			{
				return IsCalculationLevelAtOverviewOnly(IncludeUsedAssetsInReportCreation,
					IncludeUnusedAssetsInReportCreation, IncludeUnusedPrefabsInReportCreation);
			}
		}


		public static void SetCalculationLevelToFull()
		{
			IncludeUsedAssetsInReportCreation = true;
			IncludeUnusedAssetsInReportCreation = true;
			IncludeUnusedPrefabsInReportCreation = true;
		}

		public static void SetCalculationLevelToNoUnusedPrefabs()
		{
			IncludeUsedAssetsInReportCreation = true;
			IncludeUnusedAssetsInReportCreation = true;
			IncludeUnusedPrefabsInReportCreation = false;
		}

		public static void SetCalculationLevelToNoUnusedAssets()
		{
			IncludeUsedAssetsInReportCreation = true;
			IncludeUnusedAssetsInReportCreation = false;
			IncludeUnusedPrefabsInReportCreation = false;
		}

		public static void SetCalculationLevelToOverviewOnly()
		{
			IncludeUsedAssetsInReportCreation = false;
			IncludeUnusedAssetsInReportCreation = false;
			IncludeUnusedPrefabsInReportCreation = false;
		}


		public static bool AutoShowWindowAfterNormalBuild
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AutoShowWindowAfterNormalBuild;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.AutoShowWindowAfterNormalBuild != value)
				{
					_savedOptions.AutoShowWindowAfterNormalBuild = value;
					SaveOptions();
				}
			}
		}

		public static bool AutoResortAssetsWhenUnityEditorRegainsFocus
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.AutoResortAssetsWhenUnityEditorRegainsFocus;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.AutoResortAssetsWhenUnityEditorRegainsFocus != value)
				{
					_savedOptions.AutoResortAssetsWhenUnityEditorRegainsFocus = value;
					SaveOptions();
				}
			}
		}


		public static bool UseThreadedReportGeneration
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.UseThreadedReportGeneration;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.UseThreadedReportGeneration != value)
				{
					_savedOptions.UseThreadedReportGeneration = value;
					SaveOptions();
				}
			}
		}

		public static bool UseThreadedFileLoading
		{
			get
			{
				InitializeOptionsIfNeeded();
				return _savedOptions.UseThreadedFileLoading;
			}
			set
			{
				InitializeOptionsIfNeeded();
				if (_savedOptions.UseThreadedFileLoading != value)
				{
					_savedOptions.UseThreadedFileLoading = value;
					SaveOptions();
				}
			}
		}


		public static bool ShouldShowWindowAfterBuild
		{
			get { return (!IsInBatchMode && AutoShowWindowAfterNormalBuild); }
		}

		public static bool IsInBatchMode
		{
			get
			{
				return UnityEditorInternal.InternalEditorUtility.inBatchMode;


#if OTHER_BATCH_MODE_DETECTION_CODE
				// different ways to find out actually.
				// included here in case a new version of Unity
				// removes our current way of figuring out batchmode.

				// check the isHumanControllingUs bool
				return UnityEditorInternal.InternalEditorUtility.isHumanControllingUs;

				// check the command line args for "-batchmode"
				string[] arguments = Environment.GetCommandLineArgs();
				for (int n = 0, len = arguments.Length; n < len; ++n)
				{
					if (arguments[n] == "-batchmode")
					{
						return true;
					}
				}
				return false;
#endif
			}
		}
	}
}