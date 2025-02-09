using UnityEngine;
using UnityEditor;

namespace BuildReportTool
{
	public partial class ReportGenerator
	{
		public static void OnPreBuild()
		{
			BuildReportTool.Util.SaveBuildTime();
		}

		/// <summary>
		/// Create and save a Build Report. This is meant to be used for users who have a
		/// custom build script, or is automating a project build command from the command line.
		///
		/// The Editor log needs to have build data for this to work,
		/// so only call this after <see cref="UnityEditor.BuildPipeline.BuildPlayer"/>.
		/// </summary>
		///
		/// <remarks>
		/// <para>This overload will make use of the project's current build settings.</para>
		///
		/// <para>Specifically, that means it will retrieve the list of scenes from
		/// <see cref="UnityEditor.EditorBuildSettings.scenes"/>, the build location using
		/// <see cref="UnityEditor.EditorUserBuildSettings.GetBuildLocation"/>, and the build target
		/// using <see cref="UnityEditor.EditorUserBuildSettings.activeBuildTarget"/>.</para>
		///
		/// <para>If your build scripts are overriding the project's current build settings,
		/// you should use <see cref="CreateReport(string[], string, BuildTarget, string, string)"/>
		/// instead.</para>
		/// </remarks>
		///
		/// <param name="customEditorLogPath">If your Unity Editor log file is not in
		/// the default path, specify it here. Otherwise, leave this null so that
		/// BuildReportTool will just use the default location of the Editor.log file.</param>
		///
		/// <param name="customSavePath">Path to folder where the Build Report XML file will be saved.
		/// Leave this null so that BuildReportTool will just use the path specified in the saved options.</param>
		///
		/// <returns>The full path and filename of the created Build Report XML file,
		/// or null if no Build Report was created.</returns>
		public static string CreateReport(string customEditorLogPath = null, string customSavePath = null)
		{
			return CreateReport(null, null, EditorUserBuildSettings.activeBuildTarget,
				customEditorLogPath, customSavePath);
		}

#if UNITY_5_5_OR_NEWER
		/// <summary>
		/// Create and save a Build Report. This is meant to be used for users who have a
		/// custom build script, or is automating a project build command from the command line.
		///
		/// The Editor log needs to have build data for this to work,
		/// so only call this after <see cref="UnityEditor.BuildPipeline.BuildPlayer"/>.
		/// </summary>
		///
		/// <remarks>
		/// <para>This overload will retrieve the list of scenes, the build location, and the
		/// build target from the <see cref="UnityEditor.BuildPlayerOptions"/> that you pass.
		/// If your custom build script makes a BuildPlayerOptions, you can use this method
		/// to re-use that BuildPlayerOptions for generating the Build Report.</para>
		///
		/// <para><see cref="BuildPlayerOptions.locationPathName"/> can be set to the build folder's absolute path,
		/// but if there are multiple builds in that path, this should be set to the
		/// absolute path to the build file itself instead
		/// (e.g. exe file for Windows Standalone, apk file for Android, etc.).</para>
		/// </remarks>
		///
		/// <param name="buildPlayerOptions">The <see cref="UnityEditor.BuildPlayerOptions"/>
		/// that was used to create the build.</param>
		///
		/// <param name="customEditorLogPath">If your Unity Editor log file is not in
		/// the default path, specify it here. Otherwise, leave this null so that
		/// BuildReportTool will just use the default location of the Editor.log file.</param>
		///
		/// <param name="customSavePath">Path to folder where the Build Report XML file will be saved.
		/// Leave this null so that BuildReportTool will just use the path specified in the saved options.</param>
		///
		/// <returns>The full path and filename of the created Build Report XML file,
		/// or null if no Build Report was created.</returns>
		public static string CreateReport(BuildPlayerOptions buildPlayerOptions,
			string customEditorLogPath = null, string customSavePath = null)
		{
			return CreateReport(buildPlayerOptions.scenes, buildPlayerOptions.locationPathName, buildPlayerOptions.target,
				customEditorLogPath, customSavePath);
		}
#endif

		/// <summary>
		/// Create and save a Build Report. This is meant to be used for users who have a
		/// custom build script, or is automating a project build command from the command line.
		///
		/// The Editor log needs to have build data for this to work,
		/// so only call this after <see cref="UnityEditor.BuildPipeline.BuildPlayer"/>.
		/// </summary>
		///
		/// <remarks>
		/// <para>If your custom build scripts override the project's build settings,
		/// this overload allows you to explicitly specify the list of scenes, the build
		/// location, and the build target yourself.</para>
		///
		/// <para>But if your build scripts didn't override the project's build settings,
		/// you can instead use <see cref="CreateReport(string, string)"/>.</para>
		/// </remarks>
		///
		/// <param name="scenes">Which scenes were included in the build. Can be set to null
		/// so that BuildReportTool will just use UnityEditor.EditorBuildSettings.scenes instead.
		/// If you specified your own list of scenes when building, you should pass them here.</param>
		///
		/// <param name="buildLocation">Absolute path where the build was saved to. Can be set to null so that
		/// BuildReportTool will just use Unity's <see cref="UnityEditor.EditorUserBuildSettings.GetBuildLocation"/>
		/// instead. If you specified your own build location, you should set it here.
		///
		/// You can specify only the path to the build folder, but if there are multiple builds
		/// in that folder, it is best to specify the path to the build file itself
		/// (e.g. exe file for Windows Standalone, apk file for Android, etc.).</param>
		///
		/// <param name="buildTarget">Type of build platform being made. You can just pass
		/// UnityEditor.EditorUserBuildSettings.activeBuildTarget if you didn't explicitly
		/// set this. If you specified your own build target, you should pass it here.</param>
		///
		/// <param name="customEditorLogPath">If your Unity Editor log file is not in the
		/// default path, specify it here. Otherwise, leave this null so that BuildReportTool
		/// will just use the default location of the Editor.log file.</param>
		///
		/// <param name="customSavePath">Path to folder where the Build Report XML file will be saved.
		/// Leave this null so that BuildReportTool will just use the path specified in the saved options.</param>
		///
		/// <returns>The full path and filename of the created Build Report XML file,
		/// or null if no Build Report was created.</returns>
		public static string CreateReport(string[] scenes, string buildLocation, BuildTarget buildTarget,
			string customEditorLogPath = null, string customSavePath = null)
		{
			BuildReportTool.Util.BuildTargetOfLastBuild = buildTarget;

			var editorLogPathToUse = !string.IsNullOrEmpty(customEditorLogPath)
				                         ? customEditorLogPath
				                         : BuildReportTool.Util.UsedEditorLogPath;

			if (!DoesEditorLogHaveBuildInfo(editorLogPathToUse))
			{
				if (BuildReportTool.Util.IsDefaultEditorLogPathOverridden)
				{
					Debug.LogWarning(string.Format(NO_BUILD_INFO_OVERRIDDEN_LOG_WARNING, editorLogPathToUse,
						BuildReportTool.Options.FoundPathForSavedOptions));
				}
				else if (CheckIfUnityHasNoLogArgument())
				{
					Debug.LogWarning(NO_BUILD_INFO_NO_LOG_WARNING);
				}
				else
				{
					Debug.LogWarning(NO_BUILD_INFO_WARNING);
				}

				return null;
			}

			var timeNow = System.DateTime.Now;
			_lastPathToBuiltProject = buildLocation;

			Init(true, ref _lastKnownBuildInfo, scenes);
			_lastKnownBuildInfo.TimeGot = timeNow;
			_lastKnownBuildInfo.TimeGotReadable = timeNow.ToString(TIME_OF_BUILD_FORMAT);

			System.DateTime timeBuildStarted;
			if (BuildReportTool.Util.HasBuildTime())
			{
				timeBuildStarted = BuildReportTool.Util.LoadBuildTime();
			}
			else
			{
				timeBuildStarted = timeNow;
			}

			if (BuildReportTool.Options.CalculateAssetDependencies)
			{
				if (_lastKnownAssetDependencies == null)
				{
					_lastKnownAssetDependencies = new BuildReportTool.AssetDependencies();
				}

				_lastKnownAssetDependencies.TimeGot = timeBuildStarted;
			}

			if (BuildReportTool.Options.CollectTextureImportSettings)
			{
				if (_lastKnownTextureData == null)
				{
					_lastKnownTextureData = new BuildReportTool.TextureData();
				}

				_lastKnownTextureData.TimeGot = timeBuildStarted;
			}

			if (BuildReportTool.Options.CollectMeshData)
			{
				if (_lastKnownMeshData == null)
				{
					_lastKnownMeshData = new BuildReportTool.MeshData();
				}

				_lastKnownMeshData.TimeGot = timeBuildStarted;
			}
			else
			{
				if (_lastKnownMeshData != null)
				{
					_lastKnownMeshData.Clear();
				}
			}

			if (BuildReportTool.Options.CollectPrefabData)
			{
				if (_lastKnownPrefabData == null)
				{
					_lastKnownPrefabData = new BuildReportTool.PrefabData();
				}

				_lastKnownPrefabData.TimeGot = timeBuildStarted;
			}
			else
			{
				if (_lastKnownPrefabData != null)
				{
					_lastKnownPrefabData.Clear();
				}
			}

			_lastEditorLogPath = editorLogPathToUse;

			if (BuildReportTool.Options.IncludeUnusedPrefabsInReportCreation)
			{
				RefreshListOfAllPrefabsUsedInAllScenesIncludedInBuild();
			}
			else
			{
				ClearListOfAllPrefabsUsedInAllScenes();
			}

			CommitAdditionalInfoToCache();

			CreateBuildReport(_lastKnownBuildInfo);

			var savedFilePath = OnFinishedGetValues(_lastKnownBuildInfo, _lastKnownAssetDependencies, _lastKnownTextureData, _lastKnownMeshData, _lastKnownPrefabData, customSavePath);

			return savedFilePath;
		}
	}
}