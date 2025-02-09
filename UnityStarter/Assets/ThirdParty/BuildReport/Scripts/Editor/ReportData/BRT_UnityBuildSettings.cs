using System.Collections.Generic;

namespace BuildReportTool
{
	[System.Serializable]
	public partial class UnityBuildSettings
	{
		// Project Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.companyName"/>
		/// </summary>
		public string CompanyName;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.productName"/>
		/// </summary>
		public string ProductName;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.advancedLicense"/>
		/// Can also be obtained using <see cref="UnityEngine.Application.HasProLicense"/>
		/// </summary>
		public bool UsingAdvancedLicense;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.applicationIdentifier"/>
		/// </summary>
		public string ApplicationIdentifier;


		// Debug Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.development"/>
		/// Can also be obtained using <see cref="UnityEngine.Debug.isDebugBuild"/>
		/// </summary>
		public bool EnableDevelopmentBuild;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.usePlayerLog"/>
		/// </summary>
		public bool EnableDebugLog;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.allowDebugging"/>
		/// </summary>
		public bool EnableSourceDebugging;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.waitForManagedDebugger"/>
		/// Added in Unity 2019.3
		/// </summary>
		public bool WaitForManagedDebugger;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.explicitNullChecks"/>
		/// </summary>
		public bool EnableExplicitNullChecks;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.explicitDivideByZeroChecks"/>
		/// Added in Unity 5.4
		/// </summary>
		public bool EnableExplicitDivideByZeroChecks;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.explicitArrayBoundsChecks"/>
		/// Added in Unity 2018.1
		/// </summary>
		public bool EnableArrayBoundsChecks;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.enableCrashReportAPI"/>
		/// </summary>
		public bool EnableCrashReportApi;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.enableInternalProfiler"/>
		/// </summary>
		public bool EnableInternalProfiler;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.connectProfiler"/>
		/// </summary>
		public bool ConnectProfiler;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.actionOnDotNetUnhandledException"/>
		/// Added in Unity 5
		/// </summary>
		public string ActionOnDotNetUnhandledException;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetStackTraceLogType"/> using <see cref="UnityEngine.LogType.Error"/>
		/// </summary>
		public string StackTraceForError;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetStackTraceLogType"/> using <see cref="UnityEngine.LogType.Assert"/>
		/// </summary>
		public string StackTraceForAssert;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetStackTraceLogType"/> using <see cref="UnityEngine.LogType.Warning"/>
		/// </summary>
		public string StackTraceForWarning;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetStackTraceLogType"/> using <see cref="UnityEngine.LogType.Log"/>
		/// </summary>
		public string StackTraceForLog;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetStackTraceLogType"/> using <see cref="UnityEngine.LogType.Exception"/>
		/// </summary>
		public string StackTraceForException;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.enableFrameTimingStats"/>
		/// Added in Unity 2018.3
		/// </summary>
		public bool FrameTimingStats;


		// Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.enableHeadlessMode"/>
		/// </summary>
		public bool EnableHeadlessMode;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.installInBuildFolder"/>
		/// </summary>
		public bool InstallInBuildFolder;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.forceInstallation"/>
		/// </summary>
		public bool ForceInstallation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.bakeCollisionMeshes"/>
		/// Added in Unity 5.
		/// </summary>
		public bool BakeCollisionMeshes;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.stripUnusedMeshComponents"/>
		/// </summary>
		public bool StripUnusedMeshComponents;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.stripEngineCode"/>
		/// </summary>
		public bool StripEngineCode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.mipStripping"/>
		/// Added in Unity 2020.1
		/// </summary>
		public bool StripUnusedMips;


		// Code Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget)"/>
		/// Added in Unity 2017.1
		/// </summary>
		public string ScriptingBackend;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetAdditionalCompilerArguments"/>
		/// Added in Unity 2021.2
		/// </summary>
		public string[] AdditionalCompilerArguments;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetAdditionalIl2CppArgs"/>
		/// Added in Unity 2017.1
		/// </summary>
		public string AdditionalIL2CPPArguments;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetIl2CppCodeGeneration"/>
		/// Added in Unity 2022.1
		/// </summary>
		public string IL2CPPCodeGeneration;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetIl2CppCompilerConfiguration(UnityEditor.Build.NamedBuildTarget)"/>
		/// Added in Unity 2018.1
		/// </summary>
		public string IL2CPPCompilerConfig;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.activeScriptCompilationDefines"/>
		/// </summary>
		public string[] CompileDefines;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.strippingLevel"/>
		/// </summary>
		public string StrippingLevelUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget)"/>
		/// </summary>
		public string NETApiCompatibilityLevel;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowUnsafeCode"/>
		/// Added in Unity 2018.1
		/// </summary>
		public bool AllowUnsafeCode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.suppressCommonWarnings"/>
		/// Added in Unity 2019.4
		/// </summary>
		public bool SuppressCommonWarnings;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.gcIncremental"/>
		/// Added in Unity 2019.1
		/// </summary>
		public bool IncrementalGC;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.aotOptions"/>
		/// </summary>
		public string AOTOptions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.locationUsageDescription"/>
		/// </summary>
		public string LocationUsageDescription;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.insecureHttpOption"/>
		/// Added in Unity 2022.1
		/// </summary>
		public string InsecureHttpOption;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.assemblyVersionValidation"/>
		/// Added in Unity 2019.4
		/// Marked as obsolete in Unity 2022.2
		/// </summary>
		public bool AssemblyVersionValidation;


		// Rendering Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.use32BitDisplayBuffer"/>
		/// </summary>
		public bool Use32BitDisplayBuffer;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.useHDRDisplay"/>
		/// Added in Unity 5.6
		/// </summary>
		public bool UseHDRDisplay;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowHDRDisplaySupport"/>
		/// Added in Unity 2022.3
		/// </summary>
		public bool AllowHDRDisplaySupport;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.hdrBitDepth"/>
		/// Added in Unity 2022.2
		/// </summary>
		public string HdrBitDepth;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.colorSpace"/>
		/// </summary>
		public string ColorSpaceUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.MTRendering"/>
		/// </summary>
		public bool UseMultithreadedRendering;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetVirtualTexturingSupportEnabled"/>
		/// Added in Unity 2020.1
		/// </summary>
		public bool VirtualTexturingSupportEnabled;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetNormalMapEncoding(UnityEditor.Build.NamedBuildTarget)"/>
		/// Added in Unity 2020.2
		/// </summary>
		public string NormalMapEncoding;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetShaderChunkCountForPlatform"/>
		/// Added in Unity 2021.3.12
		/// </summary>
		public int ShaderChunkCountForPlatform;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetShaderChunkSizeInMBForPlatform"/>
		/// Added in Unity 2021.3.12
		/// </summary>
		public int ShaderChunkSizeInMBForPlatform;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetShaderPrecisionModel"/>
		/// Added in Unity 2020.2
		/// </summary>
		public string ShaderPrecisionModel;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.strictShaderVariantMatching"/>
		/// Added in Unity 2022.1
		/// </summary>
		public bool StrictShaderVariantMatching;

		/// <summary>
		/// In Unity 4 onwards, this is <see cref="UnityEditor.PlayerSettings.gpuSkinning"/>.
		/// In Unity 3, only xbox 360 has this with UnityEditor.PlayerSettings.xboxSkinOnGPU.
		/// </summary>
		public bool UseGPUSkinning;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.graphicsJobs"/>
		/// </summary>
		public bool UseGraphicsJobs;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.graphicsJobMode"/>
		/// </summary>
		public string GraphicsJobsType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.renderingPath"/>
		/// Added in Unity 4
		/// </summary>
		public string RenderingPathUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.visibleInBackground"/>
		/// </summary>
		public bool VisibleInBackground;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.HasAspectRatio"/>
		/// </summary>
		public string[] AspectRatiosAllowed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.GetGraphicsAPIs"/>
		/// Added in Unity 5.3
		/// </summary>
		public string[] GraphicsAPIsUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.virtualRealitySupported"/>
		/// </summary>
		public bool EnableVirtualRealitySupport;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.stereoRenderingPath"/>
		/// Added in Unity 5.5
		/// </summary>
		public string StereoRenderingPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.spriteBatchVertexThreshold"/>
		/// Added in Unity 2022.1
		/// </summary>
		public int SpriteBatchVertexThreshold;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.enable360StereoCapture"/>
		/// Added in Unity 2018.1
		/// </summary>
		public bool Enable360StereoCapture;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.legacyClampBlendShapeWeights"/>
		/// Added in Unity 2018.3
		/// </summary>
		public bool LegacyClampBlendShapeWeights;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.overrideMaxTextureSize"/>
		/// Added in Unity 2021.2
		/// </summary>
		public int OverrideMaxTextureSize;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.overrideTextureCompression"/>
		/// Added in Unity 2021.2
		/// </summary>
		public string OverrideTextureCompression;


		// Vulkan Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.vulkanEnableSetSRGBWrite"/>
		/// Added in Unity 2018.2
		/// </summary>
		public bool VulkanEnableSetSRGBWrite;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.vulkanNumSwapchainBuffers"/>
		/// Added in Unity 2019.3
		/// </summary>
		public uint vulkanNumSwapchainBuffers;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.vulkanEnableLateAcquireNextImage"/>
		/// Added in Unity 2019.4
		/// </summary>
		public bool VulkanEnableLateAcquireNextImage;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.vulkanEnablePreTransform"/>
		/// Only for Android
		/// Added in Unity 2020.2
		/// </summary>
		public bool VulkanEnablePreTransform;


		// WebGL Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.compressionFormat"/>
		/// Added in Unity 5.5
		/// </summary>
		public string WebGLCompressionFormat;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.dataCaching"/>
		/// Added in Unity 5.5
		/// </summary>
		public bool WebGLAutoCacheAssetsData;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.debugSymbols"/>
		/// Added in Unity 2021.2
		/// </summary>
		public bool WebGLCreateDebugSymbolsFile;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.debugSymbolMode"/>
		/// Added in Unity 2021.2
		/// </summary>
		public string WebGLDebugSymbolMode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.exceptionSupport"/>
		/// Added in Unity 5.5
		/// </summary>
		public string WebGLExceptionSupportType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.memorySize"/>
		/// Added in Unity 5.5
		/// </summary>
		public int WebGLMemorySize;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.WebGL.template"/>
		/// Added in Unity 5.5
		/// </summary>
		public string WebGLTemplatePath;


		// Settings shared by Web and Desktop
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.runInBackground"/>
		/// </summary>
		public bool RunInBackground;


		// Desktop (Windows/Mac/Linux) Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.displayResolutionDialog"/>
		/// </summary>
		public string StandaloneResolutionDialogSettingUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.fullScreenMode"/>
		/// Added in Unity 2018.1
		/// </summary>
		public string StandaloneFullScreenModeUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultScreenWidth"/>
		/// </summary>
		public int StandaloneDefaultScreenWidth;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultScreenHeight"/>
		/// </summary>
		public int StandaloneDefaultScreenHeight;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultIsFullScreen"/>
		/// Removed in Unity 2018 in favor of <see cref="UnityEditor.PlayerSettings.fullScreenMode"/>
		/// </summary>
		public bool StandaloneFullScreenByDefault;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowFullscreenSwitch"/>
		/// Added in Unity 5.3
		/// </summary>
		public bool StandaloneAllowFullScreenSwitch;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.captureSingleScreen"/>
		/// </summary>
		public bool StandaloneCaptureSingleScreen;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.forceSingleInstance"/>
		/// Added in Unity 4
		/// </summary>
		public bool StandaloneForceSingleInstance;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.resizableWindow"/>
		/// Added in Unity 4
		/// </summary>
		public bool StandaloneEnableResizableWindow;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.stereoscopic3D"/>
		/// Marked as obsolete in Unity 5.4
		/// </summary>
		public bool StandaloneUseStereoscopic3d;


		// Windows-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.WindowsStandalone.UserBuildSettings.copyPDBFiles"/>
		/// </summary>
		public bool WinIncludeNativePdbFilesInBuild;

		/// <summary>
		/// <see cref="UnityEditor.WindowsStandalone.UserBuildSettings.createSolution"/>
		/// </summary>
		public bool WinCreateVisualStudioSolution;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.useDirect3D11"/>
		/// Added in Unity 4.
		/// Marked as obsolete in Unity 5.2 in favor of <see cref="UnityEditor.PlayerSettings.GetGraphicsAPIs"/>
		/// </summary>
		public bool WinUseDirect3D11IfAvailable;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.d3d9FullscreenMode"/>
		/// Marked as obsolete in Unity 2017.
		/// In 2018 use <see cref="UnityEditor.PlayerSettings.fullScreenMode"/> (stored in <see cref="StandaloneFullScreenModeUsed"/>)
		/// </summary>
		public string WinDirect3D9FullscreenModeUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.d3d11FullscreenMode"/>
		/// Marked as obsolete in Unity 2018.
		/// Use <see cref="UnityEditor.PlayerSettings.fullScreenMode"/> (stored in <see cref="StandaloneFullScreenModeUsed"/>)
		/// </summary>
		public string WinDirect3D11FullscreenModeUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.useFlipModelSwapchain"/>
		/// Added in Unity 2019.1
		/// </summary>
		public bool WinUseFlipModelSwapchain;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.windowsGamepadBackendHint"/>
		/// Added in Unity 2021.3.13
		/// </summary>
		public string WinGamepadInputHint;


		// Mac OS X-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.macRetinaSupport"/>
		/// Added in Unity 2017.2
		/// </summary>
		public bool MacRetinaSupport;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.useMacAppStoreValidation"/>
		/// </summary>
		public bool MacUseAppStoreValidation;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.macOSXcodeBuildConfig"/>
		/// Added in Unity 2021.2
		/// </summary>
		public string MacXcodeBuildConfig;


		// Mobile Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.bundleIdentifier"/>
		/// "Bundle Identifier" in iOS and Mac, "Package Identifier" in Android
		/// </summary>
		public string MobileBundleIdentifier;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.bundleVersion"/>
		/// "Bundle Version" in iOS, "Version Name" in Android
		/// </summary>
		public string MobileBundleVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.statusBarHidden"/>
		/// </summary>
		public bool MobileHideStatusBar;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.accelerometerFrequency"/>
		/// </summary>
		public int MobileAccelerometerFrequency;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.defaultInterfaceOrientation"/>
		/// </summary>
		public string MobileDefaultOrientationUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowedAutorotateToPortrait"/>
		/// </summary>
		public bool MobileEnableAutorotateToPortrait;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowedAutorotateToPortraitUpsideDown"/>
		/// </summary>
		public bool MobileEnableAutorotateToReversePortrait;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowedAutorotateToLandscapeLeft"/>
		/// </summary>
		public bool MobileEnableAutorotateToLandscapeLeft;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.allowedAutorotateToLandscapeRight"/>
		/// </summary>
		public bool MobileEnableAutorotateToLandscapeRight;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.useAnimatedAutorotation"/>
		/// Formerly named UnityEditor.PlayerSettings.useOSAutorotation
		/// </summary>
		public bool MobileEnableOSAutorotation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.muteOtherAudioSources"/>
		/// Added in Unity 5.5
		/// </summary>
		public bool MuteOtherAudioSources;


		// iOS-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.applicationDisplayName"/>
		/// </summary>
		public string iOSAppDisplayName;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.scriptCallOptimization"/>
		/// </summary>
		public string iOSScriptCallOptimizationUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.sdkVersion"/>
		/// </summary>
		public string iOSSDKVersionUsed;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.targetOSVersion"/>
		/// </summary>
		public string iOSTargetOSVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.targetDevice"/>
		/// </summary>
		public string iOSTargetDevice;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.targetResolution"/>
		/// Removed in Unity 5.3
		/// </summary>
		public string iOSTargetResolution;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.iOSXcodeBuildConfig"/>
		/// Added in Unity 2021.2
		/// </summary>
		public string iOSXcodeBuildConfig;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.prerenderedIcon"/>
		/// </summary>
		public bool iOSIsIconPrerendered;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.requiresPersistentWiFi"/>
		/// </summary>
		public string iOSRequiresPersistentWiFi;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.statusBarStyle"/>
		/// </summary>
		public string iOSStatusBarStyle;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.appInBackgroundBehavior"/>
		/// Formerly <see cref="UnityEditor.PlayerSettings.iOS.exitOnSuspend"/> in Unity 4.
		/// </summary>
		public bool iOSExitOnSuspend;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.appInBackgroundBehavior"/>
		/// undocumented as of Unity 5.0.0f4
		/// </summary>
		public string iOSAppInBackgroundBehavior;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.logObjCUncaughtExceptions"/>
		/// </summary>
		public bool iOSLogObjCUncaughtExceptions;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.iOS.showActivityIndicatorOnLoading"/>
		/// </summary>
		public string iOSShowProgressBarInLoadingScreen;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.targetIOSGraphics"/>
		/// Removed in Unity 5.3
		/// </summary>
		public string iOSTargetGraphics;


		// Android-only Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.androidBuildSubtarget"/>
		/// </summary>
		public string AndroidBuildSubtarget;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.androidCreateSymbols"/>
		/// Added in Unity 2021.1
		/// </summary>
		public string AndroidCreateSymbols;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.buildApkPerCpuArchitecture"/>
		/// Added in Unity 2018.2
		/// </summary>
		public bool AndroidBuildApkPerCpuArch;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.useAPKExpansionFiles"/>
		/// </summary>
		public bool AndroidUseAPKExpansionFiles;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.exportAsGoogleAndroidProject"/>
		/// </summary>
		public bool AndroidAsAndroidProject;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.buildAppBundle"/>
		/// Added in Unity 2017.4
		/// </summary>
		public bool AndroidAppBundle;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.licenseVerification"/>
		/// </summary>
		public bool AndroidUseLicenseVerification;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.enableArmv9SecurityFeatures"/>
		/// Added in Unity 2022.2
		/// </summary>
		public bool AndroidEnableArmV9SecurityFeatures;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.androidIsGame"/>
		/// Added in Unity 5
		/// </summary>
		public bool AndroidIsGame;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.androidTVCompatibility"/>
		/// Added in Unity 5
		/// </summary>
		public bool AndroidTvCompatible;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.use24BitDepthBuffer"/>
		/// Replaced in Unity 5 with <see cref="UnityEditor.PlayerSettings.Android.disableDepthAndStencilBuffers"/>
		/// </summary>
		public bool AndroidUse24BitDepthBuffer;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.disableDepthAndStencilBuffers"/>
		/// </summary>
		public bool AndroidDisableDepthAndStencilBuffers;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.preserveFramebufferAlpha"/>
		/// Added in Unity 2017.3
		/// </summary>
		public bool AndroidPreserveFramebufferAlpha;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.bundleVersionCode"/>
		/// </summary>
		public int AndroidVersionCode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.minSdkVersion"/>
		/// </summary>
		public string AndroidMinSDKVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.targetSdkVersion"/>
		/// Added in Unity 5.6
		/// </summary>
		public string AndroidTargetSDKVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.androidTargetDevices"/>
		/// Formerly <see cref="UnityEditor.PlayerSettings.Android.targetDevice"/>
		/// </summary>
		public string AndroidTargetDevice;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.targetArchitectures"/>
		/// Added in Unity 2017.4
		/// </summary>
		public string AndroidTargetArchitectures;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.splashScreenScale"/>
		/// </summary>
		public string AndroidSplashScreenScaleMode;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.preferredInstallLocation"/>
		/// </summary>
		public string AndroidPreferredInstallLocation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.forceInternetPermission"/>
		/// </summary>
		public bool AndroidForceInternetPermission;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.forceSDCardPermission"/>
		/// </summary>
		public bool AndroidForceSDCardPermission;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.showActivityIndicatorOnLoading"/>
		/// </summary>
		public string AndroidShowProgressBarInLoadingScreen;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.keyaliasName"/>
		/// </summary>
		public string AndroidKeyAliasName;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.Android.keystoreName"/>
		/// </summary>
		public string AndroidKeystoreName;


		// XBox One Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.xboxOneDeployMethod"/>
		/// </summary>
		public string XboxOneDeployMethod;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.TitleId"/>
		/// </summary>
		public string XboxOneTitleId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.ContentId"/>
		/// </summary>
		public string XboxOneContentId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.ProductId"/>
		/// </summary>
		public string XboxOneProductId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.SandboxId"/>
		/// </summary>
		public string XboxOneSandboxId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.SCID"/>
		/// </summary>
		public string XboxOneServiceConfigId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.Version"/>
		/// </summary>
		public string XboxOneVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.IsContentPackage"/>
		/// </summary>
		public bool XboxOneIsContentPackage;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.Description"/>
		/// </summary>
		public string XboxOneDescription;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.PackagingEncryption"/>
		/// </summary>
		public string XboxOnePackagingEncryptionLevel;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.AllowedProductIds"/>
		/// </summary>
		public string[] XboxOneAllowedProductIds;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.DisableKinectGpuReservation"/>
		/// </summary>
		public bool XboxOneDisableKinectGpuReservation;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.EnableVariableGPU"/>
		/// </summary>
		public bool XboxOneEnableVariableGPU;

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.streamingInstallLaunchRange"/>
		/// </summary>
		public int XboxOneStreamingInstallLaunchRange;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.PersistentLocalStorageSize"/>
		/// </summary>
		public uint XboxOnePersistentLocalStorageSize;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.SocketNames"/>
		/// </summary>
		public string[] XboxOneSocketNames;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.GameOsOverridePath"/>
		/// </summary>
		public string XboxOneGameOsOverridePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.AppManifestOverridePath"/>
		/// </summary>
		public string XboxOneAppManifestOverridePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.XboxOne.PackagingOverridePath"/>
		/// </summary>
		public string XboxOnePackagingOverridePath;


		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.needSubmissionMaterials"/>
		/// Used in PS3, but don't know which other Playstation platforms it also applies to.
		/// </summary>
		public bool NeedSubmissionMaterials;


		// PS4 Build Settings
		// ---------------------------------------------------------------

		/// <summary>
		/// <see cref="UnityEditor.EditorUserBuildSettings.ps4BuildSubtarget"/>
		/// </summary>
		public string PS4BuildSubtarget;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.applicationParameter1"/>
		/// </summary>
		public int PS4AppParameter1;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.applicationParameter2"/>
		/// </summary>
		public int PS4AppParameter2;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.applicationParameter3"/>
		/// </summary>
		public int PS4AppParameter3;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.applicationParameter4"/>
		/// </summary>
		public int PS4AppParameter4;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.appType"/>
		/// </summary>
		public int PS4AppType;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.appVersion"/>
		/// </summary>
		public string PS4AppVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.category"/>
		/// </summary>
		public string PS4Category;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.contentID"/>
		/// </summary>
		public string PS4ContentId;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.masterVersion"/>
		/// </summary>
		public string PS4MasterVersion;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.enterButtonAssignment"/>
		/// </summary>
		public string PS4EnterButtonAssignment;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.remotePlayKeyAssignment"/>
		/// </summary>
		public string PS4RemotePlayKeyAssignment;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.videoOutPixelFormat"/>
		/// </summary>
		public string PS4VideoOutPixelFormat;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.videoOutResolution"/>
		/// </summary>
		public string PS4VideoOutResolution;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.monoEnv"/>
		/// </summary>
		public string PS4MonoEnvVars;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.npAgeRating"/>
		/// </summary>
		public string PS4NpAgeRating;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.parentalLevel"/>
		/// </summary>
		public string PS4ParentalLevel;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.playerPrefsSupport"/>
		/// </summary>
		public bool PS4EnablePlayerPrefsSupport;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.pnFriends"/>
		/// </summary>
		public bool PS4EnableFriendPushNotifications;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.pnPresence"/>
		/// </summary>
		public bool PS4EnablePresencePushNotifications;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.pnSessions"/>
		/// </summary>
		public bool PS4EnableSessionPushNotifications;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.pnGameCustomData"/>
		/// </summary>
		public bool PS4EnableGameCustomDataPushNotifications;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.BackgroundImagePath"/>
		/// </summary>
		public string PS4BgImagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.BGMPath"/>
		/// </summary>
		public string PS4BgMusicPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.StartupImagePath"/>
		/// </summary>
		public string PS4StartupImagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.paramSfxPath"/>
		/// </summary>
		public string PS4ParamSfxPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.NPtitleDatPath"/>
		/// </summary>
		public string PS4NpTitleDatPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.npTrophyPackPath"/>
		/// </summary>
		public string PS4NpTrophyPackagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.PronunciationSIGPath"/>
		/// </summary>
		public string PS4PronunciationSigPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.PronunciationXMLPath"/>
		/// </summary>
		public string PS4PronunciationXmlPath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.SaveDataImagePath"/>
		/// </summary>
		public string PS4SaveDataImagePath;

		/// <summary>
		/// <see cref="UnityEditor.PlayerSettings.PS4.ShareFilePath"/>
		/// </summary>
		public string PS4ShareFilePath;

		// ---------------------------------------------------------------

		[System.Serializable]
		public struct PackageEntry
		{
			/// <summary>
			/// Name of package using reverse domain name notation. Serves as the unique identifier.
			/// </summary>
			public string PackageName;

			/// <summary>
			/// User-friendly readable name of the package.
			/// </summary>
			public string DisplayName;

			/// <summary>
			/// For normal packages, this is the version used.<br/>
			/// For git packages, this is the commit id.<br/>
			/// For local folder packages, this will be null.
			/// </summary>
			public string VersionUsed;

			/// <summary>
			/// For normal packages, this will be the registry url that matches this package.<br/>
			/// For git packages, this is the repo url.<br/>
			/// For local folder packages, this is the path.
			/// </summary>
			public string Location;

			/// <summary>
			/// Absolute path in local PC where package was found.
			/// This will normally be in the project's "Library/PackageCache/" subfolder.
			/// </summary>
			public string LocalPath;
		}

		public List<PackageEntry> PackageEntries = new List<PackageEntry>();

		[System.Serializable]
		public struct BuiltInPackageEntry
		{
			public string PackageName;
			public string DisplayName;
		}

		public List<BuiltInPackageEntry> BuiltInPackageEntries = new List<BuiltInPackageEntry>();

		// Derived Values
		// ---------------------------------------------------------------

		public bool HasValues
		{
			get { return !string.IsNullOrEmpty(CompanyName) && !string.IsNullOrEmpty(NETApiCompatibilityLevel); }
		}
	}
}