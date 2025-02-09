using System.Collections.Generic;

namespace BuildReportTool
{
	public partial class UnityBuildSettings
	{
		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.forceOptimizeScriptCompilation"/>
		/// Added in Unity 5.2.2
		/// Marked as obsolete in Unity 2017.1.
		/// </summary>
		public bool ForceOptimizeScriptCompilation;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.buildScriptsOnly"/>
		/// No longer needed since we can use <see cref="UnityEditor.BuildOptions.BuildScriptsOnly"/>
		/// </summary>
		public bool BuildScriptsOnly;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.stripPhysics"/>
		/// Added in Unity 4. Removed in Unity 5
		/// </summary>
		public bool StripPhysicsCode;


		// Web Player Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultWebScreenWidth"/>
		/// Removed in Unity 5.6
		/// </summary>
		public int WebPlayerDefaultScreenWidth;
		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultWebScreenHeight"/>
		/// Removed in Unity 5.6
		/// </summary>
		public int WebPlayerDefaultScreenHeight;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.webPlayerStreamed"/>
		/// Removed in Unity 5.6
		/// </summary>
		public bool WebPlayerEnableStreaming;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.webPlayerOfflineDeployment"/>
		/// Removed in Unity 5.6
		/// </summary>
		public bool WebPlayerDeployOffline;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.firstStreamedLevelWithResources"/>
		/// Removed in Unity 5.3
		/// </summary>
		public int WebPlayerFirstStreamedLevelWithResources;


		// WebGL Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.webGLOptimizationLevel"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string WebGLOptimizationLevel;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.webGLUsePreBuiltUnityEngine"/>
		/// Added in Unity 5.4.
		/// Marked as obsolete in 2019.1 onwards.
		/// </summary>
		public bool WebGLUsePreBuiltUnityEngine;


		// Mac OS X-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.macFullscreenMode"/>
		/// Marked as obsolete in Unity 2018.
		/// Use <see cref="UnityEditor.PlayerSettings.fullScreenMode"/> (stored in <see cref="StandaloneFullScreenModeUsed"/>)
		/// </summary>
		public string MacFullscreenModeUsed;


		// Windows Store App-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.metroGenerateReferenceProjects"/>
		/// Added in Unity 5.
		/// Removed in Unity 2019.1.
		/// </summary>
		public bool WSAGenerateReferenceProjects;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.wsaSDK"/>
		/// Marked as obsolete.
		/// </summary>
		public string WSASDK;


		// iOS-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.appendProject"/>
		/// Removed in Unity 5.
		/// </summary>
		public bool iOSAppendedToProject;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.symlinkLibraries"/>
		/// Replaced by <see cref="UnityEditor.BuildOptions.SymlinkSources"/>
		/// </summary>
		public bool iOSSymlinkLibraries;


		// BlackBerry-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.blackberryBuildSubtarget"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryBuildSubtarget;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.blackberryBuildType"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryBuildType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.authorId"/>
		/// Removed in Unity 5
		/// </summary>
		public string BlackBerryAuthorID;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.deviceAddress"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryDeviceAddress;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.saveLogPath"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerrySaveLogPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.tokenPath"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryTokenPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.tokenAuthor"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryTokenAuthor;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.tokenExpires"/>
		/// Removed in Unity 5.4
		/// </summary>
		public string BlackBerryTokenExpiration;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.HasCameraPermissions"/>
		/// Removed in Unity 5.4
		/// </summary>
		public bool BlackBerryHasCamPermissions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.HasMicrophonePermissions"/>
		/// Removed in Unity 5.4
		/// </summary>
		public bool BlackBerryHasMicPermissions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.HasGPSPermissions"/>
		/// Removed in Unity 5.4
		/// </summary>
		public bool BlackBerryHasGpsPermissions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.HasIdentificationPermissions"/>
		/// Removed in Unity 5.4
		/// </summary>
		public bool BlackBerryHasIdPermissions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.BlackBerry.HasSharedPermissions"/>
		/// Removed in Unity 5.4
		/// </summary>
		public bool BlackBerryHasSharedPermissions;


		// XBox 360 Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.xboxBuildSubtarget"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string Xbox360BuildSubtarget;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.xboxRunMethod"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string Xbox360RunMethod;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxTitleId"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string Xbox360TitleId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxImageXexFilePath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string Xbox360ImageXexFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxSpaFilePath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string Xbox360SpaFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxGenerateSpa"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360AutoGenerateSpa;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxEnableKinect"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360EnableKinect;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxEnableKinectAutoTracking"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360EnableKinectAutoTracking;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxEnableSpeech"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360EnableSpeech;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxEnableAvatar"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360EnableAvatar;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxSpeechDB"/>
		/// Removed in Unity 5.5
		/// </summary>
		public uint Xbox360SpeechDB;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxAdditionalTitleMemorySize"/>
		/// Removed in Unity 5.5
		/// </summary>
		public int Xbox360AdditionalTitleMemSize;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxDeployKinectResources"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360DeployKinectResources;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxDeployKinectHeadOrientation"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360DeployKinectHeadOrientation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.xboxDeployKinectHeadPosition"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool Xbox360DeployKinectHeadPosition;


		// PS3 Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.compressWithPsArc"/>
		/// Marked as obsolete in Unity 2021.2
		/// </summary>
		public bool CompressBuildWithPsArc;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.sceBuildSubtarget"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string SCEBuildSubtarget;

		// paths

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3TitleConfigPath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3TitleConfigFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3DLCConfigPath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3DLCConfigFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3ThumbnailPath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3ThumbnailFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3BackgroundPath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3BackgroundImageFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3SoundPath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3BackgroundSoundFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3TrophyPackagePath"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3TrophyPackagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3TrialMode"/>
		/// Removed in Unity 5.5
		/// </summary>
		public bool PS3InTrialMode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.DisableDolbyEncoding"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public bool PS3DisableDolbyEncoding;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.EnableMoveSupport"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public bool PS3EnableMoveSupport;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.UseSPUForUmbra"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public bool PS3UseSPUForUmbra;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.EnableVerboseMemoryStats"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public bool PS3EnableVerboseMemoryStats;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.videoMemoryForAudio"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public int PS3VideoMemoryForAudio;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.videoMemoryForVertexBuffers"/>
		/// Removed in Unity 5.5
		/// </summary>
		public int PS3VideoMemoryForVertexBuffers;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3BootCheckMaxSaveGameSizeKB"/>
		/// Removed in Unity 5.5
		/// </summary>
		public int PS3BootCheckMaxSaveGameSizeKB;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3SaveGameSlots"/>
		/// Removed in Unity 5.5
		/// </summary>
		public int PS3SaveGameSlots;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3TrophyCommId"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3NpCommsId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.ps3TrophyCommSig"/>
		/// Removed in Unity 5.5
		/// </summary>
		public string PS3NpCommsSig;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS3.npAgeRating"/>
		/// Added in Unity 5
		/// Removed in Unity 5.5
		/// </summary>
		public int PS3NpAgeRating;


		// PS Vita Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.psp2NPTrophyPackPath"/>
		/// Replaced in Unity 5 with <see cref="UnityEditor.PlayerSettings.PSVita.npTrophyPackPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVTrophyPackagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.psp2ParamSfxPath"/>
		/// Replaced in Unity 5 with <see cref="UnityEditor.PlayerSettings.PSVita.paramSfxPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVParamSfxPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.psp2NPCommsID"/>
		/// Replaced in Unity 5 with <see cref="UnityEditor.PlayerSettings.PSVita.npCommunicationsID"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVNpCommsId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.psp2NPCommsSig"/>
		/// Replaced in Unity 5 with <see cref="UnityEditor.PlayerSettings.PSVita.npCommsSig"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVNpCommsSig;


		// new values in Unity 5:

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.psp2BuildSubtarget"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVBuildSubtarget;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.shortTitle"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVShortTitle;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.appVersion"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVAppVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.masterVersion"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVMasterVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.category"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVAppCategory;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.contentID"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVContentId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.npAgeRating"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVNpAgeRating;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.parentalLevel"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVParentalLevel;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.drmType"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVDrmType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.upgradable"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVUpgradable;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.tvBootMode"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVTvBootMode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.acquireBGM"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVAcquireBgm;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.AllowTwitterDialog"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVAllowTwitterDialog;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.mediaCapacity"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVMediaCapacity;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.storageType"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVStorageType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.tvDisableEmu"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVTvDisableEmu;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.npSupportGBMorGJP"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVNpSupportGbmOrGjp;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.powerMode"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVPowerMode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.useLibLocation"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVUseLibLocation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.healthWarning"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVHealthWarning;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.enterButtonAssignment"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVEnterButtonAssignment;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.infoBarColor"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVInfoBarColor;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.infoBarOnStartup"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public bool PSVShowInfoBarOnStartup;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.saveDataQuota"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public int PSVSaveDataQuota;

		// paths
		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.patchChangeInfoPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVPatchChangeInfoPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.patchOriginalPackage"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVPatchOriginalPackPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.keystoneFile"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVKeystoneFilePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.liveAreaBackroundPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVLiveAreaBgImagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.liveAreaGatePath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVLiveAreaGateImagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.liveAreaPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVCustomLiveAreaPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.liveAreaTrialPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVLiveAreaTrialPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PSVita.manualPath"/>
		/// Removed in Unity 2018.3
		/// </summary>
		public string PSVManualPath;


		// Samsung TV Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.deviceAddress"/>
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVDeviceAddress; //

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.productAuthor"/>
		/// Added in Unity 5
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVAuthor;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.productAuthorEmail"/>
		/// Added in Unity 5
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVAuthorEmail;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.productLink"/>
		/// Added in Unity 5
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVAuthorWebsiteUrl;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.productCategory"/>
		/// Added in Unity 5
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVCategory;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.SamsungTV.productDescription"/>
		/// Added in Unity 5
		/// Removed in Unity 2017.3
		/// </summary>
		public string SamsungTVDescription;
	}
}