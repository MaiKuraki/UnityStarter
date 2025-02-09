using UnityEngine;
using UnityEditor;

namespace BuildReportTool.Window.Screen
{
	public class BuildSettings : BaseScreen
	{
		public override string Name
		{
			get { return Labels.BUILD_SETTINGS_CATEGORY_LABEL; }
		}

		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies, TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
			_selectedSettingsIdxFromDropdownBox = UnityBuildSettingsUtility.GetIdxFromBuildReportValues(buildReport);
		}

		Vector2 _scrollPos;

		const int SETTING_SPACING = 4;
		const int SETTINGS_GROUP_TITLE_SPACING = 3;
		const int SETTINGS_GROUP_SPACING = 18;
		const int SETTINGS_GROUP_MINOR_SPACING = 12;

		const int DEFAULT_SHORT_COMMIT_HASH_LENGTH_DISPLAYED = 10;

		void DrawSetting(string name, bool val, bool showEvenIfEmpty = true)
		{
			DrawSetting(name, val.ToString(), showEvenIfEmpty);
		}

		void DrawSetting(string name, int val, bool showEvenIfEmpty = true)
		{
			DrawSetting(name, val.ToString(), showEvenIfEmpty);
		}

		void DrawSetting(string name, int val, string textToDisplayIfZero, bool showEvenIfEmpty = true)
		{
			DrawSetting(name, val == 0 ? textToDisplayIfZero: val.ToString(), showEvenIfEmpty);
		}

		void DrawSetting(string name, uint val, bool showEvenIfEmpty = true)
		{
			DrawSetting(name, val.ToString(), showEvenIfEmpty);
		}

		void DrawSetting(string name, string val, bool showEvenIfEmpty = true)
		{
			if (string.IsNullOrEmpty(val) && !showEvenIfEmpty)
			{
				return;
			}

			var nameStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_NAME_STYLE_NAME);
			if (nameStyle == null)
			{
				nameStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_VALUE_NO_WRAP_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginHorizontal(GUIContent.none, groupStyle, NoExpandWidth);
			GUILayout.Label(name, nameStyle, BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(2);
			if (!string.IsNullOrEmpty(val))
			{
				GUILayout.TextField(val, valueStyle, BRT_BuildReportWindow.LayoutNone);
			}

			GUILayout.EndHorizontal();
			GUILayout.Space(SETTING_SPACING);
		}

		void DrawSetting(string name, string[] val, bool showEvenIfEmpty = true)
		{
			if ((val == null || val.Length == 0) && !showEvenIfEmpty)
			{
				return;
			}

			var nameStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_NAME_STYLE_NAME);
			if (nameStyle == null)
			{
				nameStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_VALUE_NO_WRAP_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginHorizontal(GUIContent.none, groupStyle, BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label(name, nameStyle, BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(2);


			if (val != null)
			{
				GUILayout.BeginVertical(GUIContent.none, groupStyle, BRT_BuildReportWindow.LayoutNone);
				for (int n = 0, len = val.Length; n < len; ++n)
				{
					GUILayout.TextField(val[n], valueStyle, BRT_BuildReportWindow.LayoutNone);
				}

				GUILayout.EndVertical();
			}

			GUILayout.EndHorizontal();
			GUILayout.Space(SETTING_SPACING);
		}

		void DrawSetting2Lines(string name, string val, bool showEvenIfEmpty = true)
		{
			if (string.IsNullOrEmpty(val) && !showEvenIfEmpty)
			{
				return;
			}

			var nameStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_NAME_STYLE_NAME);
			if (nameStyle == null)
			{
				nameStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_VALUE_NO_WRAP_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.Label(name, nameStyle, BRT_BuildReportWindow.LayoutNone);
			if (!string.IsNullOrEmpty(val))
			{
				GUILayout.BeginHorizontal(GUIContent.none, groupStyle, NoExpandWidth);
				GUILayout.Space(10);
				GUILayout.TextField(val, valueStyle, BRT_BuildReportWindow.LayoutNone);
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(SETTING_SPACING);
		}

		void DrawSettingsGroupTitle(string name)
		{
			var titleStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.INFO_TITLE_STYLE_NAME);
			if (titleStyle == null)
			{
				titleStyle = GUI.skin.label;
			}

			GUILayout.Label(name, titleStyle, BRT_BuildReportWindow.LayoutNone);
			GUILayout.Space(SETTINGS_GROUP_TITLE_SPACING);
		}

		// =================================================================================

		BuildSettingCategory _settingsShown = BuildSettingCategory.None;

		// ----------------------------------------------

		bool IsShowingWebPlayerSettings
		{
			get { return _settingsShown == BuildSettingCategory.WebPlayer; }
		}

		bool IsShowingWebGlSettings
		{
			get { return _settingsShown == BuildSettingCategory.WebGL; }
		}

		// ----------------------------------------------

		bool IsShowingStandaloneSettings
		{
			get { return IsShowingWindowsDesktopSettings || IsShowingMacSettings || IsShowingLinuxSettings; }
		}

		bool IsShowingWindowsDesktopSettings
		{
			get { return _settingsShown == BuildSettingCategory.WindowsDesktopStandalone; }
		}

		bool IsShowingWindowsStoreAppSettings
		{
			get { return _settingsShown == BuildSettingCategory.WindowsStoreApp; }
		}

		bool IsShowingMacSettings
		{
			get { return _settingsShown == BuildSettingCategory.MacStandalone; }
		}

		bool IsShowingLinuxSettings
		{
			get { return _settingsShown == BuildSettingCategory.LinuxStandalone; }
		}

		// ----------------------------------------------

		bool IsShowingMobileSettings
		{
			get { return IsShowingiOSSettings || IsShowingAndroidSettings || IsShowingBlackberrySettings; }
		}

		bool IsShowingiOSSettings
		{
			get { return _settingsShown == BuildSettingCategory.iOS; }
		}

		bool IsShowingTvOSSettings
		{
			get { return _settingsShown == BuildSettingCategory.tvOS; }
		}

		bool IsShowingAndroidSettings
		{
			get { return _settingsShown == BuildSettingCategory.Android; }
		}

		bool IsShowingBlackberrySettings
		{
			get { return _settingsShown == BuildSettingCategory.Blackberry; }
		}

		// ----------------------------------------------

		bool IsShowingXbox360Settings
		{
			get { return _settingsShown == BuildSettingCategory.Xbox360; }
		}

		bool IsShowingXboxOneSettings
		{
			get { return _settingsShown == BuildSettingCategory.XboxOne; }
		}

		bool IsShowingPS3Settings
		{
			get { return _settingsShown == BuildSettingCategory.PS3; }
		}

		bool IsShowingPS4Settings
		{
			get { return _settingsShown == BuildSettingCategory.PS4; }
		}

		bool IsShowingPSVitaSettings
		{
			get { return _settingsShown == BuildSettingCategory.PSVita; }
		}

		// ----------------------------------------------

		bool IsShowingSamsungTvSettings
		{
			get { return _settingsShown == BuildSettingCategory.SamsungTV; }
		}

		// =================================================================================

		int _selectedSettingsIdxFromDropdownBox;

		GUIContent[] _settingDropdownBoxLabels;
		string _buildTargetOfReport = string.Empty;

		void InitializeDropdownBoxLabelsIfNeeded()
		{
			if (_settingDropdownBoxLabels != null)
			{
				return;
			}

			_settingDropdownBoxLabels = UnityBuildSettingsUtility.GetBuildSettingsCategoryListForDropdownBox();
		}

		static readonly GUILayoutOption[] NoExpandWidth = { GUILayout.ExpandWidth(false) };

		// =================================================================================

		Rect _projectSettingsRect;
		Rect _pathSettingsRect;
		Rect _buildSettingsRect;
		Rect _runtimeSettingsRect;
		Rect _debugSettingsRect;
		Rect _codeSettingsRect;
		Rect _graphicsSettingsRect;

		float _column1Width;

		void DrawProjectSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}
			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Project");

			DrawSetting("Product name:", settings.ProductName);
			if (IsShowingiOSSettings || IsShowingTvOSSettings || IsShowingMacSettings)
			{
				DrawSetting("Bundle identifier:", settings.MobileBundleIdentifier);
			}
			else if (IsShowingAndroidSettings)
			{
				DrawSetting("Package identifier:", settings.MobileBundleIdentifier);
			}
			else
			{
				DrawSetting("Application identifier:", settings.ApplicationIdentifier);
			}
			DrawSetting("Company name:", settings.CompanyName);
			DrawSetting("Build type:", buildReportToDisplay.BuildType);
			DrawSetting("Unity version:", buildReportToDisplay.UnityVersion);
			DrawSetting("Using Pro license:", settings.UsingAdvancedLicense);

			if (IsShowingiOSSettings)
			{
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
				DrawSetting("App display name:", settings.iOSAppDisplayName);
				DrawSetting("Bundle version:", settings.MobileBundleVersion);
			}
			else if (IsShowingAndroidSettings)
			{
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
				DrawSetting("Version name:", settings.MobileBundleVersion);
				DrawSetting("Bundle Version code:", settings.AndroidVersionCode);
			}
			else if (IsShowingXbox360Settings)
			{
				DrawSetting("Title ID:", settings.Xbox360TitleId);
			}
			else if (IsShowingXboxOneSettings)
			{
				DrawSetting("Title ID:", settings.XboxOneTitleId);
				DrawSetting("Content ID:", settings.XboxOneContentId);
				DrawSetting("Product ID:", settings.XboxOneProductId);
				DrawSetting("Sandbox ID:", settings.XboxOneSandboxId);
				DrawSetting("Service Configuration ID:", settings.XboxOneServiceConfigId);
				DrawSetting("Xbox One version:", settings.XboxOneVersion);
				DrawSetting("Description:", settings.XboxOneDescription);
			}
			else if (IsShowingPS4Settings)
			{
				DrawSetting("App type:", settings.PS4AppType);
				DrawSetting("App version:", settings.PS4AppVersion);
				DrawSetting("Category:", settings.PS4Category);
				DrawSetting("Content ID:", settings.PS4ContentId);
				DrawSetting("Master version:", settings.PS4MasterVersion);
			}
			else if (IsShowingPSVitaSettings)
			{
				DrawSetting("Short title:", settings.PSVShortTitle);
				DrawSetting("App version:", settings.PSVAppVersion);
				DrawSetting("App category:", settings.PSVAppCategory);
				DrawSetting("Content ID:", settings.PSVContentId);
				DrawSetting("Master version:", settings.PSVMasterVersion);
			}
			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_projectSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawBuildSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings, UnityBuildReport unityBuildReport)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Build Settings");

			// --------------------------------------------------
			// build settings

			if (unityBuildReport != null)
			{
#if UNITY_2021_2_OR_NEWER
				DrawSetting("Clean Build:",
					unityBuildReport.HasBuildOption(BuildOptions.CleanBuildCache));
#endif
				DrawSetting("Build Asset Bundle for streamed scenes:",
					unityBuildReport.HasBuildOption(BuildOptions.BuildAdditionalStreamedScenes));
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			if (IsShowingStandaloneSettings)
			{
				DrawSetting("Headless (server) build:", settings.EnableHeadlessMode);
			}
			else if (IsShowingWindowsStoreAppSettings)
			{
				DrawSetting("Generate reference projects:", settings.WSAGenerateReferenceProjects);
				DrawSetting("Target Windows Store App SDK:", settings.WSASDK);
			}
			else if (IsShowingMacSettings)
			{
				DrawSetting("Xcode project scheme:", settings.MacXcodeBuildConfig);
			}
			else if (IsShowingWebPlayerSettings)
			{
				DrawSetting("Web player streaming:", settings.WebPlayerEnableStreaming);
				DrawSetting("Web player offline deployment:", settings.WebPlayerDeployOffline);
				DrawSetting("First streamed level with \"Resources\" assets:",
					settings.WebPlayerFirstStreamedLevelWithResources);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingWebGlSettings)
			{
				DrawSetting("WebGL Template used:", settings.WebGLTemplatePath);
				DrawSetting("WebGL optimization level:",
					UnityBuildSettingsUtility.GetReadableWebGLOptimizationLevel(settings.WebGLOptimizationLevel), false);

				DrawSetting("Compression format:", settings.WebGLCompressionFormat);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingiOSSettings)
			{
				DrawSetting("SDK version:", settings.iOSSDKVersionUsed);
				DrawSetting("Target iOS version:", settings.iOSTargetOSVersion);
				DrawSetting("Target device:", settings.iOSTargetDevice);
#if UNITY_2021_2_OR_NEWER
				if (unityBuildReport != null)
				{
					DrawSetting("Symlink libraries:",
						unityBuildReport.HasBuildOption(BuildOptions.SymlinkSources));
				}
#else
				DrawSetting("Symlink libraries:", settings.iOSSymlinkLibraries);
#endif
				DrawSetting("Xcode project scheme:", settings.iOSXcodeBuildConfig);
				if (unityBuildReport != null)
				{
					DrawSetting("Append to existing Xcode project:",
						unityBuildReport.HasBuildOption(BuildOptions.AcceptExternalModificationsToPlayer));
				}

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingAndroidSettings)
			{
				DrawSetting("Min SDK version:", settings.AndroidMinSDKVersion);
				DrawSetting("Target SDK version:", settings.AndroidTargetSDKVersion);
				DrawSetting("Target device:", settings.AndroidTargetDevice);
				DrawSetting("Target architectures:", settings.AndroidTargetArchitectures);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				if (unityBuildReport != null)
				{
					DrawSetting("Patch existing app on device (instead of building):",
#if UNITY_2019_1_OR_NEWER
						// Application patching was added in 2019.1
						unityBuildReport.HasBuildOption(BuildOptions.PatchPackage));
#else
						false);
#endif
					DrawSetting("Symlink Sources:",
#if UNITY_2021_1_OR_NEWER
						unityBuildReport.HasBuildOption(BuildOptions.SymlinkSources));
#else
						unityBuildReport.HasBuildOption(BuildOptions.SymlinkLibraries));
#endif
				}
				DrawSetting("Build a separate APK for each CPU architecture:", settings.AndroidBuildApkPerCpuArch);
				DrawSetting("Build APK Expansion File (.obb file):", settings.AndroidUseAPKExpansionFiles);
				DrawSetting("Build Google Play App Bundle (.aab file):", settings.AndroidAppBundle);
				DrawSetting("Export Android Studio/Gradle project:", settings.AndroidAsAndroidProject);
				if (unityBuildReport != null)
				{
					DrawSetting("Create Android Studio/Eclipse project on each build:",
						unityBuildReport.HasBuildOption(BuildOptions.AcceptExternalModificationsToPlayer));
				}
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);


				DrawSetting("Is game:", settings.AndroidIsGame);
				DrawSetting("TV-compatible:", settings.AndroidTvCompatible);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);


				DrawSetting("Force Internet permission:", settings.AndroidForceInternetPermission);
				DrawSetting("Force SD card permission:", settings.AndroidForceSDCardPermission);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);


				DrawSetting("Key alias name:", settings.AndroidKeyAliasName);
				DrawSetting("Keystore name:", settings.AndroidKeystoreName);
			}
			else if (IsShowingBlackberrySettings)
			{
				DrawSetting("Build subtarget:", settings.BlackBerryBuildSubtarget);
				DrawSetting("Build type:", settings.BlackBerryBuildType);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				if (buildReportToDisplay.IsUnityVersionAtMost(4, 0, 0))
				{
					DrawSetting("Author ID:", settings.BlackBerryAuthorID);
				}

				DrawSetting("Device address:", settings.BlackBerryDeviceAddress);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Save log path:", settings.BlackBerrySaveLogPath);
				DrawSetting("Token path:", settings.BlackBerryTokenPath);

				DrawSetting("Token author:", settings.BlackBerryTokenAuthor);
				DrawSetting("Token expiration:", settings.BlackBerryTokenExpiration);
			}
			else if (IsShowingXbox360Settings)
			{
				DrawSetting("Build subtarget:", settings.Xbox360BuildSubtarget);
				DrawSetting("Run method:", settings.Xbox360RunMethod);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Image .xex filepath:", settings.Xbox360ImageXexFilePath);
				DrawSetting(".spa filepath:", settings.Xbox360SpaFilePath);
				DrawSetting("Auto-generate .spa:", settings.Xbox360AutoGenerateSpa);
				DrawSetting("Additional title memory size:", settings.Xbox360AdditionalTitleMemSize);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingXboxOneSettings)
			{
				DrawSetting("Deploy method:", settings.XboxOneDeployMethod);
				DrawSetting("Is content package:", settings.XboxOneIsContentPackage);
				DrawSetting("Packaging encryption level:", settings.XboxOnePackagingEncryptionLevel);
				DrawSetting("Allowed product IDs:", settings.XboxOneAllowedProductIds);
				DrawSetting("Disable Kinect GPU reservation:", settings.XboxOneDisableKinectGpuReservation);
				DrawSetting("Enable variable GPU:", settings.XboxOneEnableVariableGPU);
				DrawSetting("Streaming install launch range:", settings.XboxOneStreamingInstallLaunchRange);
				DrawSetting("Persistent local storage size:", settings.XboxOnePersistentLocalStorageSize);
				DrawSetting("Socket names:", settings.XboxOneSocketNames);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Game OS override path:", settings.XboxOneGameOsOverridePath);
				DrawSetting("App manifest override path:", settings.XboxOneAppManifestOverridePath);
				DrawSetting("Packaging override path:", settings.XboxOnePackagingOverridePath);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingPS3Settings)
			{
				DrawSetting("Build subtarget:", settings.SCEBuildSubtarget);

				DrawSetting("NP Communications ID:", settings.PS3NpCommsId);
				DrawSetting("NP Communications Signature:", settings.PS3NpCommsSig);
				DrawSetting("NP Age Rating:", settings.PS3NpAgeRating);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Title config filepath:", settings.PS3TitleConfigFilePath);
				DrawSetting("DLC config filepath:", settings.PS3DLCConfigFilePath);
				DrawSetting("Thumbnail filepath:", settings.PS3ThumbnailFilePath);
				DrawSetting("Background image filepath:", settings.PS3BackgroundImageFilePath);
				DrawSetting("Background sound filepath:", settings.PS3BackgroundSoundFilePath);
				DrawSetting("Trophy package path:", settings.PS3TrophyPackagePath);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Compress build with PS Arc:", settings.CompressBuildWithPsArc);
				DrawSetting("Need submission materials:", settings.NeedSubmissionMaterials);

				DrawSetting("In trial mode:", settings.PS3InTrialMode);
				DrawSetting("Disable Dolby encoding:", settings.PS3DisableDolbyEncoding);
				DrawSetting("Enable Move support:", settings.PS3EnableMoveSupport);
				DrawSetting("Use SPU for Umbra:", settings.PS3UseSPUForUmbra);

				DrawSetting("Video memory for vertex buffers:", settings.PS3VideoMemoryForVertexBuffers);
				DrawSetting("Video memory for audio:", settings.PS3VideoMemoryForAudio);
				DrawSetting("Boot check max save game size (KB):", settings.PS3BootCheckMaxSaveGameSizeKB);
				DrawSetting("Save game slots:", settings.PS3SaveGameSlots);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingPS4Settings)
			{
				DrawSetting("Build subtarget:", settings.PS4BuildSubtarget);

				DrawSetting("App parameter 1:", settings.PS4AppParameter1);
				DrawSetting("App parameter 2:", settings.PS4AppParameter2);
				DrawSetting("App parameter 3:", settings.PS4AppParameter3);
				DrawSetting("App parameter 4:", settings.PS4AppParameter4);


				DrawSetting("Enter button assignment:", settings.PS4EnterButtonAssignment);
				DrawSetting("Remote play key assignment:", settings.PS4RemotePlayKeyAssignment);

				DrawSetting("NP Age rating:", settings.PS4NpAgeRating);
				DrawSetting("Parental level:", settings.PS4ParentalLevel);

				DrawSetting("Enable friend push notifications:", settings.PS4EnableFriendPushNotifications);
				DrawSetting("Enable presence push notifications:", settings.PS4EnablePresencePushNotifications);
				DrawSetting("Enable session push notifications:", settings.PS4EnableSessionPushNotifications);
				DrawSetting("Enable game custom data push notifications:",
					settings.PS4EnableGameCustomDataPushNotifications);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Background image path:", settings.PS4BgImagePath);
				DrawSetting("Background music path:", settings.PS4BgMusicPath);
				DrawSetting("Startup image path:", settings.PS4StartupImagePath);
				DrawSetting("Save data image path:", settings.PS4SaveDataImagePath);

				DrawSetting("Params sfx path:", settings.PS4ParamSfxPath);
				DrawSetting("NP Title dat path:", settings.PS4NpTitleDatPath);
				DrawSetting("NP Trophy Package path:", settings.PS4NpTrophyPackagePath);
				DrawSetting("Pronunciations SIG path:", settings.PS4PronunciationSigPath);
				DrawSetting("Pronunciations XML path:", settings.PS4PronunciationXmlPath);

				DrawSetting("Share file path:", settings.PS4ShareFilePath);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingPSVitaSettings)
			{
				DrawSetting("Build subtarget:", settings.PSVBuildSubtarget);

				DrawSetting("DRM type:", settings.PSVDrmType);
				DrawSetting("Upgradable:", settings.PSVUpgradable);
				DrawSetting("TV boot mode:", settings.PSVTvBootMode);
				DrawSetting("Parental Level:", settings.PSVParentalLevel);
				DrawSetting("Health warning:", settings.PSVHealthWarning);
				DrawSetting("Enter button assignment:", settings.PSVEnterButtonAssignment);

				DrawSetting("Acquire BGM:", settings.PSVAcquireBgm);
				DrawSetting("Allow Twitter Dialog:", settings.PSVAllowTwitterDialog);

				DrawSetting("NP Communications ID:", settings.PSVNpCommsId);
				DrawSetting("NP Communications Signature:", settings.PSVNpCommsSig);
				DrawSetting("Age Rating:", settings.PSVNpAgeRating);

				DrawSetting("Power mode:", settings.PSVPowerMode);
				DrawSetting("Media capacity:", settings.PSVMediaCapacity);
				DrawSetting("Storage type:", settings.PSVStorageType);
				DrawSetting("TV disable emu:", settings.PSVTvDisableEmu);
				DrawSetting("Support Game Boot Message or Game Joining Presence:", settings.PSVNpSupportGbmOrGjp);
				DrawSetting("Use lib location:", settings.PSVUseLibLocation);

				DrawSetting("Info bar color:", settings.PSVInfoBarColor);
				DrawSetting("Show info bar on startup:", settings.PSVShowInfoBarOnStartup);
				DrawSetting("Save data quota:", settings.PSVSaveDataQuota);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting("Manual filepath:", settings.PSVManualPath);
				DrawSetting("Trophy package filepath:", settings.PSVTrophyPackagePath);
				DrawSetting("Params Sfx filepath:", settings.PSVParamSfxPath);
				DrawSetting("Patch change info filepath:", settings.PSVPatchChangeInfoPath);
				DrawSetting("Patch original filepath:", settings.PSVPatchOriginalPackPath);
				DrawSetting("Keystone filepath:", settings.PSVKeystoneFilePath);
				DrawSetting("Live Area BG image filepath:", settings.PSVLiveAreaBgImagePath);
				DrawSetting("Live Area Gate image filepath:", settings.PSVLiveAreaGateImagePath);
				DrawSetting("Custom Live Area path:", settings.PSVCustomLiveAreaPath);
				DrawSetting("Live Area trial path:", settings.PSVLiveAreaTrialPath);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingSamsungTvSettings)
			{
				DrawSetting("Device address:", settings.SamsungTVDeviceAddress);
				DrawSetting("Author:", settings.SamsungTVAuthor);
				DrawSetting("Author email:", settings.SamsungTVAuthorEmail);
				DrawSetting("Website:", settings.SamsungTVAuthorWebsiteUrl);
				DrawSetting("Category:", settings.SamsungTVCategory);
				DrawSetting("Description:", settings.SamsungTVDescription);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			if (unityBuildReport != null)
			{
				DrawSetting("Scripts only build:", unityBuildReport.HasBuildOption(BuildOptions.BuildScriptsOnly));
			}

			DrawSetting("Install in build folder:", settings.InstallInBuildFolder);

			if (buildReportToDisplay.IsUnityVersionAtMost(4, 0, 0))
			{
				DrawSetting("Physics code stripped:", settings.StripPhysicsCode);
			}

			DrawSetting("Prebake collision meshes:", settings.BakeCollisionMeshes);
			DrawSetting("Optimize mesh data:", settings.StripUnusedMeshComponents);

			if (unityBuildReport != null)
			{
#if UNITY_5_6_OR_NEWER
				if (unityBuildReport.HasBuildOption(BuildOptions.CompressWithLz4))
				{
					DrawSetting("Compression Method:", "LZ4");
				}
#endif
#if UNITY_2017_2_OR_NEWER
				else if (unityBuildReport.HasBuildOption(BuildOptions.CompressWithLz4HC))
				{
					DrawSetting("Compression Method:", "LZ4HC");
				}
#endif
#if UNITY_5_6_OR_NEWER
				else
#endif
				{
					DrawSetting("Compression Method:", "Default");
				}
#if UNITY_2018_1_OR_NEWER
				DrawSetting("Test Assemblies included in build:", unityBuildReport.HasBuildOption(BuildOptions.IncludeTestAssemblies));
#endif
#if UNITY_5_6_OR_NEWER
				DrawSetting("No Unique Identifier (force build GUID to all zeros):", unityBuildReport.HasBuildOption(BuildOptions.NoUniqueIdentifier));
#endif
#if UNITY_2020_1_OR_NEWER
				DrawSetting("Detailed Build Report:", unityBuildReport.HasBuildOption(BuildOptions.DetailedBuildReport));
#endif
			}

#if UNITY_2018_3_OR_NEWER
			DrawSetting("Managed stripping level:", settings.StrippingLevelUsed);
#else
			if (IsShowingMobileSettings)
			{
				DrawSetting("Stripping level:", settings.StrippingLevelUsed);
			}
#endif

			DrawSetting("Strip unused engine code (IL2CPP-only):", settings.StripEngineCode);
			DrawSetting("Strip unused mips from textures:", settings.StripUnusedMips);

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_buildSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawRuntimeSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Runtime Settings");

			if (IsShowingMobileSettings)
			{
				DrawSetting("Mute other audio sources:", settings.MuteOtherAudioSources);
			}

			if (IsShowingiOSSettings)
			{
				DrawSetting("Hide status bar:", settings.MobileHideStatusBar);
				DrawSetting("Status bar style:", settings.iOSStatusBarStyle);
				DrawSetting("Accelerometer frequency:", settings.MobileAccelerometerFrequency);
				DrawSetting("Requires persistent Wi-Fi:", settings.iOSRequiresPersistentWiFi);

				if (buildReportToDisplay.IsUnityVersionAtMost(4, 0, 0))
				{
					DrawSetting("Exit on suspend:", settings.iOSExitOnSuspend);
				}

				if (buildReportToDisplay.IsUnityVersionAtLeast(5, 0, 0))
				{
					DrawSetting("App-in-background behavior:", settings.iOSAppInBackgroundBehavior);
				}


				DrawSetting("Activity indicator on loading:", settings.iOSShowProgressBarInLoadingScreen);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingAndroidSettings)
			{
				DrawSetting("Hide status bar:", settings.MobileHideStatusBar);
				DrawSetting("Accelerometer frequency:", settings.MobileAccelerometerFrequency);
				DrawSetting("Activity indicator on loading:", settings.AndroidShowProgressBarInLoadingScreen);
				DrawSetting("Splash screen scale:", settings.AndroidSplashScreenScaleMode);

				DrawSetting("Preferred install location:", settings.AndroidPreferredInstallLocation);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingWebGlSettings)
			{
				DrawSetting("Automatically cache WebGL assets data:", settings.WebGLAutoCacheAssetsData);
				DrawSetting("WebGL Memory Size:", settings.WebGLMemorySize);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}


			if (!IsShowingiOSSettings && !IsShowingAndroidSettings && IsShowingMobileSettings) // any mobile except iOS, Android
			{
				DrawSetting("Hide status bar:", settings.MobileHideStatusBar);
				DrawSetting("Accelerometer frequency:", settings.MobileAccelerometerFrequency);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			if (IsShowingXbox360Settings)
			{
				DrawSetting("Enable avatar:", settings.Xbox360EnableAvatar);
				DrawSetting("Enable Kinect:", settings.Xbox360EnableKinect);
				DrawSetting("Enable Kinect auto-tracking:", settings.Xbox360EnableKinectAutoTracking);

				DrawSetting("Deploy Kinect resources:", settings.Xbox360DeployKinectResources);
				DrawSetting("Deploy Kinect head orientation:", settings.Xbox360DeployKinectHeadOrientation);
				DrawSetting("Deploy Kinect head position:", settings.Xbox360DeployKinectHeadPosition);

				DrawSetting("Enable speech:", settings.Xbox360EnableSpeech);
				DrawSetting("Speech DB:", settings.Xbox360SpeechDB);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingBlackberrySettings)
			{
				DrawSetting("Has camera permissions:", settings.BlackBerryHasCamPermissions);
				DrawSetting("Has microphone permissions:", settings.BlackBerryHasMicPermissions);
				DrawSetting("Has GPS permissions:", settings.BlackBerryHasGpsPermissions);
				DrawSetting("Has ID permissions:", settings.BlackBerryHasIdPermissions);
				DrawSetting("Has shared permissions:", settings.BlackBerryHasSharedPermissions);
			}

			if (IsShowingStandaloneSettings || IsShowingWebPlayerSettings || IsShowingBlackberrySettings)
			{
				DrawSetting("Run in background:", settings.RunInBackground);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			if (IsShowingWindowsDesktopSettings)
			{
				DrawSetting("Desired API for gamepad input:", settings.WinGamepadInputHint);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			// --------------------------------------------------
			// security settings
			if (IsShowingMacSettings)
			{
				DrawSetting("Use App Store validation:", settings.MacUseAppStoreValidation);
			}
			else if (IsShowingAndroidSettings)
			{
				DrawSetting("Use license verification:", settings.AndroidUseLicenseVerification);
				DrawSetting("Enable ARMv9 security features:", settings.AndroidEnableArmV9SecurityFeatures);
			}
			DrawSetting("Allow plain-text (insecure) HTTP connections:", settings.InsecureHttpOption);

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_runtimeSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawDebugSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings, UnityBuildReport unityBuildReport)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Debug Settings");

			DrawSetting("Is development build:", settings.EnableDevelopmentBuild);
			if (IsShowingWindowsDesktopSettings)
			{
				DrawSetting("PDB files for native DLLs included in build:", settings.WinIncludeNativePdbFilesInBuild);
				DrawSetting("Create Visual Studio Solution:", settings.WinCreateVisualStudioSolution);
			}
			DrawSetting("Debug Log enabled:", settings.EnableDebugLog);

			if (buildReportToDisplay.IsUnityVersionAtLeast(5, 4, 0))
			{
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

				DrawSetting2Lines("Stack trace for regular logs:",
					UnityBuildSettingsUtility.GetReadableStackTraceType(settings.StackTraceForLog), false);
				DrawSetting2Lines("Stack trace for warning logs:",
					UnityBuildSettingsUtility.GetReadableStackTraceType(settings.StackTraceForWarning), false);
				DrawSetting2Lines("Stack trace for error logs:",
					UnityBuildSettingsUtility.GetReadableStackTraceType(settings.StackTraceForError), false);
				DrawSetting2Lines("Stack trace for assert logs:",
					UnityBuildSettingsUtility.GetReadableStackTraceType(settings.StackTraceForAssert), false);
				DrawSetting2Lines("Stack trace for exception logs:",
					UnityBuildSettingsUtility.GetReadableStackTraceType(settings.StackTraceForException), false);
			}

			GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

			if (IsShowingPS3Settings)
			{
				DrawSetting("Enable verbose memory stats:", settings.PS3EnableVerboseMemoryStats);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingiOSSettings)
			{
				if (buildReportToDisplay.IsUnityVersionAtLeast(5, 0, 0))
				{
					DrawSetting("Log Objective-C uncaught exceptions:", settings.iOSLogObjCUncaughtExceptions);
				}
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingAndroidSettings)
			{
				DrawSetting("Create symbol package:", settings.AndroidCreateSymbols);
			}
			else if (IsShowingWebGlSettings)
			{
				DrawSetting("Use pre-built WebGL Unity engine:", settings.WebGLUsePreBuiltUnityEngine);
				DrawSetting("Create WebGL debug symbols file:", settings.WebGLCreateDebugSymbolsFile);
				DrawSetting("WebGL debug symbols mode:", settings.WebGLDebugSymbolMode);
				DrawSetting("WebGL exception support:", settings.WebGLExceptionSupportType);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			DrawSetting("Enable explicit null checks:", settings.EnableExplicitNullChecks);

			if (buildReportToDisplay.IsUnityVersionAtLeast(5, 4, 0))
			{
				DrawSetting("Enable explicit divide-by-zero checks:", settings.EnableExplicitDivideByZeroChecks);
			}
			DrawSetting("Enable explicit array bounds checks:", settings.EnableArrayBoundsChecks);

			if (buildReportToDisplay.IsUnityVersionAtLeast(5, 0, 0))
			{
				DrawSetting("Action on unhandled .NET exception:", settings.ActionOnDotNetUnhandledException);

				DrawSetting("Enable internal profiler:", settings.EnableInternalProfiler);

				DrawSetting("Enable CrashReport API:", settings.EnableCrashReportApi);
			}

			GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

			DrawSetting("Auto-connect to Unity Editor Profiler:", settings.ConnectProfiler);
			if (unityBuildReport != null)
			{
#if UNITY_2019_3_OR_NEWER
				DrawSetting("Deep profiling support:", unityBuildReport.HasBuildOption(BuildOptions.EnableDeepProfilingSupport));
#endif
#if UNITY_5_2 || UNITY_5_3_OR_NEWER
				DrawSetting("Force enable assertions in release build:", unityBuildReport.HasBuildOption(BuildOptions.ForceEnableAssertions));
#endif
			}
			DrawSetting("Allow remote script debuggers:", settings.EnableSourceDebugging);
			DrawSetting("Wait for Managed Debugger before executing scripts:", settings.WaitForManagedDebugger);

			DrawSetting("Collect CPU/GPU frame timing statistics:", settings.FrameTimingStats);

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_debugSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawCodeSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings, UnityBuildReport unityBuildReport)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Code Settings");

			DrawSetting("Scripting Backend:", settings.ScriptingBackend);
			DrawSetting(".NET API compatibility level:", settings.NETApiCompatibilityLevel);
			DrawSetting("Incremental garbage collection:", settings.IncrementalGC);
			DrawSetting("Suppress common C# warnings:", settings.SuppressCommonWarnings);
			DrawSetting("Allow 'unsafe' code:", settings.AllowUnsafeCode);
			DrawSetting("Exact version matching for Strong-named assemblies:", settings.AssemblyVersionValidation);
			DrawSetting("IL2CPP code generation:", settings.IL2CPPCodeGeneration);
			DrawSetting("IL2CPP compiler configuration:", settings.IL2CPPCompilerConfig);

			DrawSetting("AOT options:", settings.AOTOptions);
			DrawSetting("Location usage description:", settings.LocationUsageDescription);

			if (unityBuildReport != null)
			{
#if UNITY_2019_2_OR_NEWER
				DrawSetting("Enable Code Coverage:",
					unityBuildReport.HasBuildOption(BuildOptions.EnableCodeCoverage));
#endif
			}
			if (IsShowingiOSSettings)
			{
				DrawSetting("Script call optimized:", settings.iOSScriptCallOptimizationUsed);
				//GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingPS4Settings)
			{
				DrawSetting("Mono environment variables:", settings.PS4MonoEnvVars);
				DrawSetting("Enable Player Prefs support:", settings.PS4EnablePlayerPrefsSupport);
			}

			DrawSetting("Additional Compiler Arguments:", settings.AdditionalCompilerArguments);
			DrawSetting("Additional IL2CPP Arguments:", settings.AdditionalIL2CPPArguments);
			DrawSetting("Script Compilation Defines:", settings.CompileDefines);

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_codeSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawGraphicsSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings, UnityBuildReport unityBuildReport)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Graphics Settings");

			if (IsShowingAndroidSettings)
			{
				DrawSetting("Texture Compression:", settings.AndroidBuildSubtarget);
			}

			DrawSetting("Max Texture Size Override:", settings.OverrideMaxTextureSize, "No Override");
			DrawSetting("Texture Compression Override:", settings.OverrideTextureCompression);

			DrawSetting("Use HDR display if available:", settings.UseHDRDisplay);
			DrawSetting("Allow HDR display support:", settings.AllowHDRDisplaySupport);
			DrawSetting("HDR bit depth:", settings.HdrBitDepth);
			DrawSetting("Use 32-bit display buffer:", settings.Use32BitDisplayBuffer);
			if (IsShowingMacSettings)
			{
				DrawSetting("Enable Retina support:", settings.MacRetinaSupport);
			}
			DrawSetting("Rendering path:", settings.RenderingPathUsed);
			DrawSetting("Color space:", settings.ColorSpaceUsed);
			DrawSetting("Use multi-threaded rendering:", settings.UseMultithreadedRendering);
			DrawSetting("Virtual texturing support enabled:", settings.VirtualTexturingSupportEnabled);
			DrawSetting("Use graphics jobs:", settings.UseGraphicsJobs);
			DrawSetting("Graphics jobs mode:", settings.GraphicsJobsType);
			DrawSetting("Use GPU skinning:", settings.UseGPUSkinning);
			DrawSetting("Clamp BlendShape weights (deprecated):", settings.LegacyClampBlendShapeWeights);
			DrawSetting("Enable Virtual Reality Support:", settings.EnableVirtualRealitySupport);
			DrawSetting("Stereo rendering path:", settings.StereoRenderingPath);

			if (IsShowingWindowsDesktopSettings || IsShowingMacSettings)
			{
				DrawSetting("Enable 360 stereo capture:", settings.Enable360StereoCapture);
			}

			if (IsShowingiOSSettings || IsShowingTvOSSettings || IsShowingAndroidSettings)
			{
				DrawSetting("Normal map encoding:", settings.NormalMapEncoding);
			}

			DrawSetting("Shader chunk count for platform:", settings.ShaderChunkCountForPlatform);
			DrawSetting("Shader chunk size in MB for platform:", settings.ShaderChunkSizeInMBForPlatform);
			DrawSetting("Shader precision model:", settings.ShaderPrecisionModel);
			DrawSetting("Strict shader variant matching:", settings.StrictShaderVariantMatching);
			DrawSetting("Max vertex limit for Sprite batching:", settings.SpriteBatchVertexThreshold);

#if UNITY_2020_2_OR_NEWER && !UNITY_2023_1_OR_NEWER
			// Shader Live Link added in 2020.2, removed in 2023.1
			if (unityBuildReport != null)
			{
				DrawSetting("Enable Shader Livelink Support:",
					unityBuildReport.HasBuildOption(BuildOptions.ShaderLivelinkSupport));
			}
#endif

			if (buildReportToDisplay.IsUnityVersionAtLeast(5, 2, 0))
			{
				DrawSetting("Graphics APIs Used:", settings.GraphicsAPIsUsed);
			}

			GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);

			if (IsShowingMobileSettings)
			{
				DrawSetting("Default interface orientation:", settings.MobileDefaultOrientationUsed);

				DrawSetting("Use OS screen auto-rotate:", settings.MobileEnableOSAutorotation);
				DrawSetting("Auto-rotate to portrait:", settings.MobileEnableAutorotateToPortrait);
				DrawSetting("Auto-rotate to reverse portrait:", settings.MobileEnableAutorotateToReversePortrait);
				DrawSetting("Auto-rotate to landscape left:", settings.MobileEnableAutorotateToLandscapeLeft);
				DrawSetting("Auto-rotate to landscape right:", settings.MobileEnableAutorotateToLandscapeRight);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingStandaloneSettings)
			{
				string standaloneScreenSize =
					string.Format("{0} x {1}", settings.StandaloneDefaultScreenWidth.ToString(), settings.StandaloneDefaultScreenHeight.ToString());
				DrawSetting("Default screen size:", standaloneScreenSize);
				DrawSetting("Resolution dialog:", settings.StandaloneResolutionDialogSettingUsed);

				// removed in Unity 2018
				if (buildReportToDisplay.IsUnityVersionAtLeast(2017, 0, 0))
				{
					DrawSetting("Full-screen by default:", settings.StandaloneFullScreenByDefault);
				}

				DrawSetting("Resizable window:", settings.StandaloneEnableResizableWindow);

				// added in Unity 2018
				if (buildReportToDisplay.IsUnityVersionAtLeast(2018, 0, 0))
				{
					DrawSetting("Fullscreen Mode:", settings.StandaloneFullScreenModeUsed);
				}

				if (IsShowingWindowsDesktopSettings)
				{
					// not needed in Unity 5.3 since settings.GraphicsAPIsUsed shows better information
					if (buildReportToDisplay.IsUnityVersionAtMost(5, 2, 0))
					{
						DrawSetting("Use Direct3D11 if available:", settings.WinUseDirect3D11IfAvailable);
					}

					// removed in 2017
					if (buildReportToDisplay.IsUnityVersionAtLeast(5, 0, 0))
					{
						DrawSetting("Direct3D9 Fullscreen Mode:", settings.WinDirect3D9FullscreenModeUsed);
					}

					// removed in 2018
					if (buildReportToDisplay.IsUnityVersionAtLeast(2017, 0, 0))
					{
						DrawSetting("Direct3D11 Fullscreen Mode:", settings.WinDirect3D11FullscreenModeUsed);
					}

					DrawSetting("Use DXGI flip model swap chain if possible (for Direct3D11):", settings.WinUseFlipModelSwapchain);

					DrawSetting("Visible in background (for Fullscreen Windowed mode):", settings.VisibleInBackground);
				}
				else if (IsShowingMacSettings)
				{
					// removed in 2018
					if (buildReportToDisplay.IsUnityVersionAtLeast(2017, 0, 0) &&
					    buildReportToDisplay.IsUnityVersionAtMost(2017, 4, 0))
					{
						DrawSetting("Fullscreen mode:", settings.MacFullscreenModeUsed);
						GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
					}
				}

				DrawSetting("Allow OS switching between full-screen and window mode:",
					settings.StandaloneAllowFullScreenSwitch);
				DrawSetting("Darken secondary monitors on full-screen:", settings.StandaloneCaptureSingleScreen);
				DrawSetting("Force single instance:", settings.StandaloneForceSingleInstance);

				DrawSetting("Stereoscopic Rendering:", settings.StandaloneUseStereoscopic3d);
				DrawSetting("Supported aspect ratios:", settings.AspectRatiosAllowed);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			if (IsShowingWebPlayerSettings)
			{
				string webScreenSize = string.Format("{0} x {1}", settings.WebPlayerDefaultScreenWidth.ToString(), settings.WebPlayerDefaultScreenHeight.ToString());
				DrawSetting("Screen size:", webScreenSize);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingiOSSettings)
			{
				if (buildReportToDisplay.IsUnityVersionAtMost(5, 2, 0))
				{
					// Unity 5.3 has a Screen.resolutions but I don't know
					// which of those in the array would be the iOS target resolution
					DrawSetting("Target resolution:", settings.iOSTargetResolution);
				}

				if (buildReportToDisplay.IsUnityVersionAtMost(5, 1, 0))
				{
					// not used in Unity 5.2 since settings.GraphicsAPIsUsed shows better information
					DrawSetting("Target graphics:", settings.iOSTargetGraphics);
				}


				DrawSetting("App icon pre-rendered:", settings.iOSIsIconPrerendered);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingAndroidSettings)
			{
				if (buildReportToDisplay.IsUnityVersionAtMost(4, 0, 0))
				{
					DrawSetting("Use 24-bit depth buffer:", settings.AndroidUse24BitDepthBuffer);
				}

				if (buildReportToDisplay.IsUnityVersionAtLeast(5, 0, 0))
				{
					DrawSetting("Disable depth and stencil buffers:", settings.AndroidDisableDepthAndStencilBuffers);
				}

				DrawSetting("Preserve alpha in framebuffer:", settings.AndroidPreserveFramebufferAlpha);

				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}
			else if (IsShowingPS4Settings)
			{
				DrawSetting("Video out pixel format:", settings.PS4VideoOutPixelFormat);
				DrawSetting("Video out resolution:", settings.PS4VideoOutResolution);
				GUILayout.Space(SETTINGS_GROUP_MINOR_SPACING);
			}

			DrawSetting("Enable SetSRGBWrite on Vulkan:", settings.VulkanEnableSetSRGBWrite);
			DrawSetting("Number of swapchain buffers on Vulkan:", settings.vulkanNumSwapchainBuffers);
			DrawSetting("Acquire swapchain image as late as possible on Vulkan:", settings.VulkanEnableLateAcquireNextImage);
			if (IsShowingAndroidSettings)
			{
				DrawSetting("Apply display rotation during rendering:", settings.VulkanEnablePreTransform);
			}

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_graphicsSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawPackageSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings)
		{
			var packageList = settings.PackageEntries;
			var builtInPackageList = settings.BuiltInPackageEntries;

			bool packageListIsEmpty = packageList == null || packageList.Count == 0;
			bool builtInPackageListIsEmpty = builtInPackageList == null || builtInPackageList.Count == 0;

			if (packageListIsEmpty && builtInPackageListIsEmpty)
			{
				return;
			}

			var nameStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_NAME_STYLE_NAME);
			if (nameStyle == null)
			{
				nameStyle = GUI.skin.label;
			}

			var valueStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.SETTING_VALUE_STYLE_NAME);
			if (valueStyle == null)
			{
				valueStyle = GUI.skin.label;
			}

			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);

			if (!packageListIsEmpty)
			{
				DrawSettingsGroupTitle("Packages");
				for (int n = 0, len = packageList.Count; n < len; ++n)
				{
					if (!string.IsNullOrEmpty(packageList[n].DisplayName))
					{
						if (!string.IsNullOrEmpty(packageList[n].Location) && packageList[n].Location.EndsWith(".git") && packageList[n].VersionUsed.Length > 7)
						{
							// show commit hash as short
							GUILayout.BeginHorizontal(GUIContent.none, groupStyle, NoExpandWidth);
							GUILayout.Label(packageList[n].DisplayName, nameStyle);
							GUILayout.Space(4);
							GUILayout.TextField(packageList[n].VersionUsed.Substring(0, DEFAULT_SHORT_COMMIT_HASH_LENGTH_DISPLAYED), valueStyle);
							GUILayout.Space(4);
							DrawPackagePingButton(packageList[n]);
							GUILayout.EndHorizontal();
							GUILayout.TextField(packageList[n].PackageName, valueStyle);
						}
						else if (packageList[n].VersionUsed.Length <= 10)
						{
							// version is short enough, put it in the same line as the Package Name
							GUILayout.BeginHorizontal(GUIContent.none, groupStyle, NoExpandWidth);
							GUILayout.Label(packageList[n].DisplayName, nameStyle);
							GUILayout.Space(4);
							GUILayout.TextField(packageList[n].VersionUsed, valueStyle);
							GUILayout.Space(4);
							DrawPackagePingButton(packageList[n]);
							GUILayout.EndHorizontal();
							GUILayout.TextField(packageList[n].PackageName, valueStyle);
						}
						else
						{
							// version is too long, put it as a 2nd line after the Display Name
							GUILayout.Label(packageList[n].DisplayName, nameStyle);
							GUILayout.TextField(packageList[n].VersionUsed, valueStyle);
							GUILayout.TextField(packageList[n].PackageName, valueStyle);
							DrawPackagePingButton(packageList[n]);
						}
					}
					else
					{
						// no display name
						if (packageList[n].VersionUsed.Length <= 10)
						{
							// version is short enough, put it in the same line as the Package Name
							GUILayout.BeginHorizontal(GUIContent.none, groupStyle, NoExpandWidth);
							GUILayout.TextField(packageList[n].PackageName, nameStyle);
							GUILayout.Space(4);
							GUILayout.TextField(packageList[n].VersionUsed, valueStyle);
							GUILayout.Space(4);
							DrawPackagePingButton(packageList[n]);
							GUILayout.EndHorizontal();
						}
						else
						{
							// version is too long, put it as a 2nd line after the Package Name
							GUILayout.TextField(packageList[n].PackageName, nameStyle);
							GUILayout.TextField(packageList[n].VersionUsed, valueStyle);
							DrawPackagePingButton(packageList[n]);
						}
					}

					if (!string.IsNullOrEmpty(packageList[n].Location) && packageList[n].Location != BuildReportTool.UnityBuildSettingsUtility.DEFAULT_REGISTRY_URL)
					{
						GUILayout.TextField(packageList[n].Location, valueStyle);
					}

					GUILayout.Space(10);
				}

				if (!builtInPackageListIsEmpty)
				{
					GUILayout.Space(14);
				}
			}

			if (!builtInPackageListIsEmpty)
			{
				DrawSettingsGroupTitle("Built-In Packages");
				for (int n = 0, len = builtInPackageList.Count; n < len; ++n)
				{
					if (!string.IsNullOrEmpty(builtInPackageList[n].DisplayName))
					{
						GUILayout.Label(builtInPackageList[n].DisplayName, nameStyle);
						GUILayout.TextField(builtInPackageList[n].PackageName, valueStyle);
					}
					else
					{
						// no display name
						GUILayout.Label(builtInPackageList[n].PackageName, nameStyle);
					}

					GUILayout.Space(5);
				}
			}

			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_pathSettingsRect = GUILayoutUtility.GetLastRect();
			}
		}

		void DrawPackagePingButton(BuildReportTool.UnityBuildSettings.PackageEntry packageEntry)
		{
			if (!string.IsNullOrEmpty(packageEntry.LocalPath))
			{
				if (GUILayout.Button("Ping", "MiniButton"))
				{
					Utility.PingAssetInProject(string.Format("Packages/{0}/package.json", packageEntry.PackageName));
				}
				if (GUILayout.Button("Explore", "MiniButton"))
				{
					BuildReportTool.Util.OpenInFileBrowser(packageEntry.LocalPath);
				}
			}
		}

		void DrawPathSettings(BuildInfo buildReportToDisplay, UnityBuildSettings settings)
		{
			var groupStyle = GUI.skin.FindStyle("ProjectSettingsGroup");
			if (groupStyle == null)
			{
				groupStyle = GUI.skin.label;
			}

			GUILayout.BeginVertical(GUIContent.none, groupStyle, NoExpandWidth);
			DrawSettingsGroupTitle("Paths");

			DrawSetting2Lines("Unity path:", buildReportToDisplay.EditorAppContentsPath);
			DrawSetting2Lines("Project path:", buildReportToDisplay.ProjectAssetsPath);
			DrawSetting2Lines("Build path:", buildReportToDisplay.BuildFilePath);
			GUILayout.EndVertical();
			if (Event.current.type == EventType.Repaint)
			{
				_pathSettingsRect = GUILayoutUtility.GetLastRect();
			}
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

			BuildSettingCategory b = ReportGenerator.GetBuildSettingCategoryFromBuildValues(buildReportToDisplay);
			_buildTargetOfReport = UnityBuildSettingsUtility.GetReadableBuildSettingCategory(b);

			UnityBuildSettings settings = buildReportToDisplay.UnityBuildSettings;

			if (settings == null)
			{
				Utility.DrawCentralMessage(position, "No \"Project Settings\" recorded in this build report.");
				requestRepaint = false;
				return;
			}

			var topBarBgStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOP_BAR_BG_STYLE_NAME);
			if (topBarBgStyle == null)
			{
				topBarBgStyle = GUI.skin.box;
			}

			var topBarLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOP_BAR_LABEL_STYLE_NAME);
			if (topBarLabelStyle == null)
			{
				topBarLabelStyle = GUI.skin.label;
			}

			var fileFilterPopupStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.FILE_FILTER_POPUP_STYLE_NAME);
			if (fileFilterPopupStyle == null)
			{
				fileFilterPopupStyle = GUI.skin.label;
			}

			// ----------------------------------------------------------
			// top bar

			GUILayout.Space(1);
			GUILayout.BeginHorizontal();

			GUILayout.Label(" ", topBarBgStyle);

			GUILayout.Space(8);
			GUILayout.Label("Build Target: ", topBarLabelStyle);

			InitializeDropdownBoxLabelsIfNeeded();
			_selectedSettingsIdxFromDropdownBox = EditorGUILayout.Popup(_selectedSettingsIdxFromDropdownBox,
				_settingDropdownBoxLabels, fileFilterPopupStyle);
			GUILayout.Space(15);

			GUILayout.Label(string.Format("Note: Project was built in {0} target", _buildTargetOfReport), topBarLabelStyle);
			GUILayout.FlexibleSpace();

			BuildReportTool.Options.ShowProjectSettingsInMultipleColumns = GUILayout.Toggle(BuildReportTool.Options.ShowProjectSettingsInMultipleColumns, "Multiple Columns");
			GUILayout.Space(30);

			GUILayout.EndHorizontal();

			_settingsShown = UnityBuildSettingsUtility.GetSettingsCategoryFromIdx(_selectedSettingsIdxFromDropdownBox);

			// ----------------------------------------------------------

			var showMultiColumn = BuildReportTool.Options.ShowProjectSettingsInMultipleColumns;

			_scrollPos = GUILayout.BeginScrollView(_scrollPos);

			GUILayout.BeginHorizontal(NoExpandWidth);

			// left padding
			GUILayout.Space(10);
			GUILayout.BeginVertical(NoExpandWidth);

			// top padding
			GUILayout.Space(10);

			// columns
			GUILayout.BeginHorizontal(NoExpandWidth);

			// column 1
			GUILayout.BeginVertical(NoExpandWidth);

			bool putCodeSettingsInColumn2 = showMultiColumn && _codeSettingsRect.width > 0 && _column1Width + _codeSettingsRect.width < position.width;
			bool putGraphicsSettingsInColumn3 = showMultiColumn && _graphicsSettingsRect.width > 0 && _column1Width + _codeSettingsRect.width + _graphicsSettingsRect.width < position.width;


			// =================================================================
			DrawProjectSettings(buildReportToDisplay, settings);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			// =================================================================
			DrawPathSettings(buildReportToDisplay, settings);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			// =================================================================
			DrawBuildSettings(buildReportToDisplay, settings, unityBuildReport);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			// =================================================================
			DrawRuntimeSettings(buildReportToDisplay, settings);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			// =================================================================
			DrawDebugSettings(buildReportToDisplay, settings, unityBuildReport);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			if (putGraphicsSettingsInColumn3)
			{
				GUILayout.EndVertical(); // end of column 1

				// column 2
				GUILayout.BeginVertical(NoExpandWidth);
			}
			// =================================================================
			DrawGraphicsSettings(buildReportToDisplay, settings, unityBuildReport);
			GUILayout.Space(SETTINGS_GROUP_SPACING);

			// =================================================================
			DrawPackageSettings(buildReportToDisplay, settings);
			GUILayout.Space(SETTINGS_GROUP_SPACING);

			if (putCodeSettingsInColumn2)
			{
				GUILayout.EndVertical(); // end of column 1 or 2

				// column 2 or 2
				GUILayout.BeginVertical(NoExpandWidth);
			}
			// =================================================================
			DrawCodeSettings(buildReportToDisplay, settings, unityBuildReport);
			GUILayout.Space(SETTINGS_GROUP_SPACING);


			GUILayout.EndVertical(); // end of last column
			GUILayout.EndHorizontal(); // end columns

			// bottom padding
			GUILayout.Space(10);
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			GUILayout.EndScrollView();
			requestRepaint = false;

			// =================================================================

			if (Event.current.type == EventType.Repaint)
			{
				_column1Width = 0;
				_column1Width = Mathf.Max(_projectSettingsRect.width, _column1Width);
				_column1Width = Mathf.Max(_pathSettingsRect.width, _column1Width);
				_column1Width = Mathf.Max(_buildSettingsRect.width, _column1Width);
				_column1Width = Mathf.Max(_runtimeSettingsRect.width, _column1Width);
				_column1Width = Mathf.Max(_debugSettingsRect.width, _column1Width);
				//_column1Width = Mathf.Max(_graphicsSettingsRect.width, _column1Width);
			}
			//_column1Width = Mathf.Max(_codeSettingsRect.width, _column1Width);
		}
	}
}


public static class ScriptReference
{
	//
	//   Example usage:
	//
	//   if (GUILayout.Button("doc"))
	//   {
	//      ScriptReference.GoTo("EditorUserBuildSettings.development");
	//   }
	//
	public static void GoTo(string pageName)
	{
		string pageUrl = "file:///";

		pageUrl += EditorApplication.applicationContentsPath;

		// unity 3
		pageUrl += "/Documentation/Documentation/ScriptReference/";

		pageUrl += pageName.Replace(".", "-");
		pageUrl += ".html";

		Debug.Log("going to: " + pageUrl);

		Application.OpenURL(pageUrl);
	}
}