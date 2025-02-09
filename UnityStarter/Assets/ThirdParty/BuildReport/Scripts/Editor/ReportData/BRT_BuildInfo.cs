using System.Collections.Generic;
using UnityEditor;

namespace BuildReportTool
{
	/// <summary>
	/// Class for holding a Build Report.
	/// This is the class that is serialized when saving a Build Report to a file.
	/// </summary>
	[System.Serializable, System.Xml.Serialization.XmlRoot("BuildInfo")]
	public partial class BuildInfo : BuildReportTool.IDataFile
	{
		// General Info
		// ==================================================================================

		/// <summary>
		/// Name of project folder.
		/// Included as part of the filename when saved.
		/// </summary>
		public string ProjectName;

		/// <summary>
		/// Type of build, as reported by the Unity Editor log, but as a string.
		/// Included as part of the filename when saved.
		/// </summary>
		public string BuildType;

		// -----------------------------------------------

		/// <summary>
		/// When build was created.
		/// Included as part of the filename when saved.
		/// </summary>
		public System.DateTime BuildTimeGot;

		/// <summary>
		/// When report was created.
		/// </summary>
		public System.DateTime TimeGot;

		/// <summary>
		/// When build was created, in readable format.
		/// </summary>
		public string BuildTimeGotReadable;

		/// <summary>
		/// When report was created, in readable format.
		/// </summary>
		public string TimeGotReadable;

		// -----------------------------------------------

		/// <summary>
		/// How long it took to create this Build Report.
		/// </summary>
		System.TimeSpan _reportGenerationTime;

		/// <inheritdoc cref="_reportGenerationTime"/>
		[System.Xml.Serialization.XmlIgnore]
		public System.TimeSpan ReportGenerationTime
		{
			get { return _reportGenerationTime; }
			set { _reportGenerationTime = value; }
		}

		/// <inheritdoc cref="_reportGenerationTime"/>
		[System.Xml.Serialization.XmlElement("ReportGenerationTime")]
		public long ReportGenerationTimeInTicks
		{
			get { return _reportGenerationTime.Ticks; }
			set { _reportGenerationTime = new System.TimeSpan(value); }
		}

		// -----------------------------------------------

		/// <summary>
		/// How long it took to create the build.
		/// </summary>
		System.TimeSpan _buildDurationTime;

		[System.Xml.Serialization.XmlIgnore]
		public System.TimeSpan BuildDurationTime
		{
			get { return _buildDurationTime; }
			set { _buildDurationTime = value; }
		}

		[System.Xml.Serialization.XmlElement("BuildDurationTime")]
		public long BuildTimeDurationTicks
		{
			get { return _buildDurationTime.Ticks; }
			set { _buildDurationTime = new System.TimeSpan(value); }
		}

		// -----------------------------------------------

		/// <summary>
		/// Version of Unity that was used in building project.
		/// </summary>
		public string UnityVersion = "";

		/// <summary>
		/// Value of <see cref="EditorApplication.applicationContentsPath"/> during time of build.
		/// This is the full path where the Unity Editor executable is.
		/// </summary>
		public string EditorAppContentsPath = "";

		/// <summary>
		/// Value of <see cref="UnityEngine.Application.dataPath"/> during time of build.
		/// This is the full path where the project folder is.
		/// </summary>
		public string ProjectAssetsPath = "";

		/// <summary>
		/// Value of <see cref="EditorUserBuildSettings.GetBuildLocation"/> during time of build.
		/// This is the full path where the build output is.
		/// </summary>
		public string BuildFilePath = "";


		// Build Settings at time of Build Report creation
		// ==================================================================================

		/// <summary>
		/// If this Build Report recorded the various Build Settings when project was built.
		/// </summary>
		public bool HasUnityBuildSettings;

		/// <summary>
		/// Various Build Settings when project was built.
		/// </summary>
		public UnityBuildSettings UnityBuildSettings;


		// Unity/OS environment values at time of Build Report creation
		// ==================================================================================

		//public string[] PrefabsUsedInScenes;

		/// <summary>
		/// If the output for an Android build additionally had an .obb file beside the .apk file.
		/// </summary>
		public bool AndroidUseAPKExpansionFiles;

		/// <summary>
		/// If the output for an Android build was a project instead of an .apk file.
		/// </summary>
		public bool AndroidCreateProject;


		// Total sizes
		// ==================================================================================

		/// <summary>
		/// Total size of output, in readable format.
		/// </summary>
		public string TotalBuildSize = "";

		/// <summary>
		/// Not used anymore. This is kept here for compatibility
		/// with opening and displaying old Build Report files.
		/// This was the total size of output, in readable format.
		/// </summary>
		public string CompressedBuildSize = "";

		/// <summary>
		/// Total size of all assets used in the build, in readable format.
		/// </summary>
		public string UsedTotalSize = "";

		/// <summary>
		/// Total size of all assets *not* used in the build, in readable format.
		/// </summary>
		public string UnusedTotalSize = "";

		/// <summary>
		/// Total size of StreamingAssets folder (if present), in readable format.
		/// </summary>
		public string StreamingAssetsSize = "";


		// Per-platform specific sizes
		// -------------------------------------------

		/// <summary>
		/// Not used anymore. This is kept here for compatibility
		/// with opening and displaying old Build Report files.
		/// This was the size of the .unity3d web build file, in readable format.
		/// </summary>
		public string WebFileBuildSize = "";

		/// <summary>
		/// For Android builds, this is the size of the .apk file, in readable format.
		/// </summary>
		public string AndroidApkFileBuildSize = "";

		/// <summary>
		/// For Android builds that generated an additional .obb file,
		/// this is the size of the .obb file, in readable format.
		/// </summary>
		public string AndroidObbFileBuildSize = "";


		// Category Sizes
		// ==================================================================================

		public BuildReportTool.SizePart[] BuildSizes;


		// File entries
		// ==================================================================================

		/// <summary>
		/// All Mono/.NET DLL files used in the build, and their file sizes.
		/// </summary>
		public BuildReportTool.SizePart[] MonoDLLs;

		/// <summary>
		/// All managed DLL files from the project's Assets folder
		/// that were used in the build, and their file sizes.
		/// </summary>
		public BuildReportTool.SizePart[] ScriptDLLs;

		/// <summary>
		/// All the Unity API managed DLL files
		/// that were used in the build, and their file sizes.
		/// </summary>
		public BuildReportTool.SizePart[] UnityEngineDLLs;

		/// <summary>
		/// File filters used at time of Build Report creation
		/// </summary>
		public FileFilterGroup FileFilters;

		/// <summary>
		/// All files from the project's Assets folder
		/// that were included in the build, and their file sizes.
		/// </summary>
		public AssetList UsedAssets;

		/// <summary>
		/// All files from the project's Assets folder
		/// that were *not* included in the build, and their file sizes.
		/// </summary>
		public AssetList UnusedAssets;

		/// <summary>
		/// Scenes included in the build
		/// </summary>
		public SceneInBuild[] ScenesInBuild;


		// Build Report Tool Options used at time of Build Report creation
		// ==================================================================================

		public bool IncludedSvnInUnused;
		public bool IncludedGitInUnused;
		public bool IncludedBuildReportToolAssetsInUnused;

		public List<SavedOptions.IgnorePattern> IgnorePatternsForUnused;

		public bool UsedAssetsIncludedInCreation;
		public bool UnusedAssetsIncludedInCreation;
		public bool UnusedPrefabsIncludedInCreation;

		public bool ProcessUnusedAssetsInBatches = true;
		public int UnusedAssetsEntriesPerBatch;
	}
} // namespace BuildReportTool