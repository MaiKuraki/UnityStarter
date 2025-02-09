using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER // 5.2 and greater
using System.Linq;
#endif
using UnityEditor;
using UnityEngine;

namespace BuildReportTool
{
	public static class UnityBuildSettingsUtility
	{
		// ================================================================================================

		public static GUIContent[] GetBuildSettingsCategoryListForDropdownBox()
		{
			// WARNING! changing contents here will require changing code in:
			//
			//  GetIdxFromBuildReportValues
			//  GetSettingsCategoryFromIdx
			//
			// as they rely on the array indices
			//
			return new[]
			{
				/*  0 */ new GUIContent("Windows"),
				/*  1 */ new GUIContent("Mac"),
				/*  2 */ new GUIContent("Linux"),

				/*  3 */ new GUIContent("Web"),
				/*  4 */ new GUIContent("Web GL"),

				/*  5 */ new GUIContent("iOS"),
				/*  6 */ new GUIContent("tvOS"),
				/*  7 */ new GUIContent("Android"),
				/*  8 */ new GUIContent("Blackberry"),

				/*  9 */ new GUIContent("Xbox 360"),
				/* 10 */ new GUIContent("Xbox One"),
				/* 11 */ new GUIContent("Xbox Series"),
				/* 12 */ new GUIContent("Playstation 3"),
				/* 13 */ new GUIContent("Playstation 4"),
				/* 14 */ new GUIContent("Playstation 5"),

				/* 15 */ new GUIContent("Playstation Vita (Native)"),

				/* 16 */ new GUIContent("Samsung TV"),
			};
		}


		public static int GetIdxFromBuildReportValues(BuildInfo buildReportToDisplay)
		{
			BuildSettingCategory b = ReportGenerator.GetBuildSettingCategoryFromBuildValues(buildReportToDisplay);

			switch (b)
			{
				case BuildSettingCategory.WindowsDesktopStandalone:
					return 0;
				case BuildSettingCategory.MacStandalone:
					return 1;
				case BuildSettingCategory.LinuxStandalone:
					return 2;

				case BuildSettingCategory.WebPlayer:
					return 3;
				case BuildSettingCategory.WebGL:
					return 4;

				case BuildSettingCategory.iOS:
					return 5;
				case BuildSettingCategory.tvOS:
					return 6;
				case BuildSettingCategory.Android:
					return 7;
				case BuildSettingCategory.Blackberry:
					return 8;

				case BuildSettingCategory.Xbox360:
					return 9;
				case BuildSettingCategory.XboxOne:
					return 10;
				case BuildSettingCategory.XboxSeries:
					return 11;
				case BuildSettingCategory.PS3:
					return 12;
				case BuildSettingCategory.PS4:
					return 13;
				case BuildSettingCategory.PS5:
					return 14;

				case BuildSettingCategory.PSVita:
					return 15;

				case BuildSettingCategory.SamsungTV:
					return 16;
			}

			return -1;
		}

		public static BuildSettingCategory GetSettingsCategoryFromIdx(int idx)
		{
			switch (idx)
			{
				case 0:
					return BuildSettingCategory.WindowsDesktopStandalone;
				case 1:
					return BuildSettingCategory.MacStandalone;
				case 2:
					return BuildSettingCategory.LinuxStandalone;

				case 3:
					return BuildSettingCategory.WebPlayer;
				case 4:
					return BuildSettingCategory.WebGL;

				case 5:
					return BuildSettingCategory.iOS;
				case 6:
					return BuildSettingCategory.tvOS;
				case 7:
					return BuildSettingCategory.Android;
				case 8:
					return BuildSettingCategory.Blackberry;

				case 9:
					return BuildSettingCategory.Xbox360;
				case 10:
					return BuildSettingCategory.XboxOne;
				case 11:
					return BuildSettingCategory.XboxSeries;
				case 12:
					return BuildSettingCategory.PS3;
				case 13:
					return BuildSettingCategory.PS4;
				case 14:
					return BuildSettingCategory.PS5;

				case 15:
					return BuildSettingCategory.PSVita;

				case 16:
					return BuildSettingCategory.SamsungTV;
			}

			return BuildSettingCategory.None;
		}

		public static string GetReadableBuildSettingCategory(BuildSettingCategory b)
		{
			switch (b)
			{
				case BuildSettingCategory.WindowsDesktopStandalone:
					return "Windows";

				case BuildSettingCategory.WindowsStoreApp:
					return "Windows Store App";

				case BuildSettingCategory.WindowsPhone8:
					return "Windows Phone 8";

				case BuildSettingCategory.MacStandalone:
					return "Mac";

				case BuildSettingCategory.LinuxStandalone:
					return "Linux";


				case BuildSettingCategory.WebPlayer:
					return "Web Player";


				case BuildSettingCategory.Xbox360:
					return "Xbox 360";
				case BuildSettingCategory.XboxOne:
					return "Xbox One";
				case BuildSettingCategory.XboxSeries:
					return "Xbox Series";

				case BuildSettingCategory.PS3:
					return "Playstation 3";
				case BuildSettingCategory.PS4:
					return "Playstation 4";
				case BuildSettingCategory.PS5:
					return "Playstation 5";

				case BuildSettingCategory.PSVita:
					return "Playstation Vita (Native)";

				case BuildSettingCategory.PSM:
					return "Playstation Mobile";

				case BuildSettingCategory.WebGL:
					return "Web GL";
			}

			return b.ToString();
		}


		public static string GetReadableWebGLOptimizationLevel(string optimizationLevelCode)
		{
			switch (optimizationLevelCode)
			{
				case "1":
					return "1: Slow (fast builds)";
				case "2":
					return "2: Fast";
				case "3":
					return "3: Fastest (very slow builds)";
			}

			return optimizationLevelCode;
		}

		public static string GetReadableStackTraceType(string stackTraceType)
		{
			switch (stackTraceType)
			{
				case "ScriptOnly":
					return "Show stack trace of scripts only (no native code)";
				case "Full":
					return "Show stack trace of native code + scripts";
				default:
					return stackTraceType;
			}
		}

		// ================================================================================================

		public static void Populate(UnityBuildSettings settings)
		{
			PopulateGeneralSettings(settings);
			PopulateWebSettings(settings);
			PopulateStandaloneSettings(settings);
			PopulateMobileSettings(settings);
			PopulateTvDeviceSettings(settings);
			PopulateBigConsoleGen07Settings(settings);
			PopulateBigConsoleGen08Settings(settings);
			PopulatePackageSettings(settings);
		}


		public static void PopulateGeneralSettings(UnityBuildSettings settings)
		{
			settings.CompanyName = PlayerSettings.companyName;
			settings.ProductName = PlayerSettings.productName;

			settings.UsingAdvancedLicense = PlayerSettings.advancedLicense;

#if UNITY_5_6_OR_NEWER
			settings.ApplicationIdentifier = PlayerSettings.applicationIdentifier;
#endif

			// debug settings
			// ---------------------------------------------------------------
			settings.EnableDevelopmentBuild = EditorUserBuildSettings.development;
			settings.EnableDebugLog = PlayerSettings.usePlayerLog;
			settings.EnableSourceDebugging = EditorUserBuildSettings.allowDebugging;
#if UNITY_2019_3_OR_NEWER
			settings.WaitForManagedDebugger = EditorUserBuildSettings.waitForManagedDebugger;
#endif
			settings.EnableExplicitNullChecks = EditorUserBuildSettings.explicitNullChecks;

#if UNITY_EDITOR_WIN
#if UNITY_5_6_OR_NEWER
			settings.WinIncludeNativePdbFilesInBuild = UnityEditor.WindowsStandalone.UserBuildSettings.copyPDBFiles;
#else
			settings.WinIncludeNativePdbFilesInBuild = false;
#endif
#if UNITY_2017_1_OR_NEWER
			settings.WinCreateVisualStudioSolution = UnityEditor.WindowsStandalone.UserBuildSettings.createSolution;
#else
			settings.WinCreateVisualStudioSolution = false;
#endif
#endif

#if UNITY_5_4_OR_NEWER
			settings.EnableExplicitDivideByZeroChecks = EditorUserBuildSettings.explicitDivideByZeroChecks;
#endif
#if UNITY_2018_1_OR_NEWER
			settings.EnableArrayBoundsChecks = EditorUserBuildSettings.explicitArrayBoundsChecks;
#endif

#if !UNITY_4
			settings.EnableCrashReportApi = PlayerSettings.enableCrashReportAPI;
			settings.EnableInternalProfiler = PlayerSettings.enableInternalProfiler;
			settings.ActionOnDotNetUnhandledException = PlayerSettings.actionOnDotNetUnhandledException.ToString();
#endif

			settings.ConnectProfiler = EditorUserBuildSettings.connectProfiler;

#if UNITY_5_3_OR_NEWER && !UNITY_2017_1_OR_NEWER
			// this setting actually started appearing in Unity 5.2.2 (it is not present in 5.2.1)
			// but our script compilation defines can't detect the patch number in the version,
			// so we have no choice but to restrict this to 5.3
			settings.ForceOptimizeScriptCompilation = EditorUserBuildSettings.forceOptimizeScriptCompilation;
#endif

#if UNITY_5_4_OR_NEWER
			settings.StackTraceForError = PlayerSettings.GetStackTraceLogType(LogType.Error).ToString();
			settings.StackTraceForAssert = PlayerSettings.GetStackTraceLogType(LogType.Assert).ToString();
			settings.StackTraceForWarning = PlayerSettings.GetStackTraceLogType(LogType.Warning).ToString();
			settings.StackTraceForLog = PlayerSettings.GetStackTraceLogType(LogType.Log).ToString();
			settings.StackTraceForException = PlayerSettings.GetStackTraceLogType(LogType.Exception).ToString();
#endif


			// build settings
			// ---------------------------------------------------------------
#if UNITY_2021_2_OR_NEWER
			settings.EnableHeadlessMode = EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
#else
			settings.EnableHeadlessMode = EditorUserBuildSettings.enableHeadlessMode;
#endif
			settings.InstallInBuildFolder = EditorUserBuildSettings.installInBuildFolder;
#if !UNITY_4
			settings.ForceInstallation = EditorUserBuildSettings.forceInstallation;
			settings.BuildScriptsOnly = EditorUserBuildSettings.buildScriptsOnly;
			settings.BakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes;
#endif

#if UNITY_4
			settings.StripPhysicsCode = PlayerSettings.stripPhysics;
#endif
			settings.StripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;

#if UNITY_5_3_OR_NEWER // 5.2 and greater (but Unity 5.2 doesn't have a define so we use 5.3)
			settings.StripEngineCode = PlayerSettings.stripEngineCode;
#endif

#if UNITY_2020_1_OR_NEWER
			settings.StripUnusedMips = PlayerSettings.mipStripping;
#endif


			// code settings
			// ---------------------------------------------------------------

			Dictionary<string, DldUtil.GetRspDefines.Entry> customDefines = DldUtil.GetRspDefines.GetDefines();

			List<string> defines = new List<string>();
			defines.AddRange(EditorUserBuildSettings.activeScriptCompilationDefines);


			foreach (KeyValuePair<string, DldUtil.GetRspDefines.Entry> customDefine in customDefines)
			{
				if (customDefine.Value.TimesDefinedInBuiltIn == 0)
				{
					defines.Add(customDefine.Key);
				}
			}

			settings.CompileDefines = defines.ToArray();

#if UNITY_2019_1_OR_NEWER
			settings.IncrementalGC = PlayerSettings.gcIncremental;
#endif
#if UNITY_2019_4_OR_NEWER
			settings.SuppressCommonWarnings = PlayerSettings.suppressCommonWarnings;
#endif

			BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
#if UNITY_2021_2_OR_NEWER
			var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);
#endif

#if UNITY_2021_2_OR_NEWER
			settings.ScriptingBackend = PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString();
#elif UNITY_2017_1_OR_NEWER
			settings.ScriptingBackend = PlayerSettings.GetScriptingBackend(targetGroup).ToString();
#endif

#if UNITY_2017_1_OR_NEWER
			settings.AdditionalIL2CPPArguments = PlayerSettings.GetAdditionalIl2CppArgs();
#endif

#if UNITY_2021_2_OR_NEWER
			settings.AdditionalCompilerArguments = PlayerSettings.GetAdditionalCompilerArguments(namedBuildTarget);
#endif

#if UNITY_2021_3_OR_NEWER
			settings.StrippingLevelUsed = PlayerSettings
			                              .GetManagedStrippingLevel(namedBuildTarget)
			                              .ToString();
#elif UNITY_2018_3_OR_NEWER
			settings.StrippingLevelUsed = PlayerSettings
			                              .GetManagedStrippingLevel(EditorUserBuildSettings.selectedBuildTargetGroup)
			                              .ToString();
#else
			settings.StrippingLevelUsed = PlayerSettings.strippingLevel.ToString();
#endif

			ApiCompatibilityLevel apiCompat;
#if UNITY_2021_3_OR_NEWER
			apiCompat = PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget);
#elif UNITY_5_6_OR_NEWER
			apiCompat = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup);
#else
			apiCompat = PlayerSettings.apiCompatibilityLevel;
#endif

			switch (apiCompat)
			{
				case ApiCompatibilityLevel.NET_Standard_2_0:
					// NET_Standard_2_0 is deprecated, change to NET_Standard instead
					// both NET_Standard_2_0 and NET_Standard have the same int value, so we have to force the change
					settings.NETApiCompatibilityLevel = "NET_Standard";
					break;
				case ApiCompatibilityLevel.NET_4_6:
					// NET_4_6 is deprecated, change to NET_Unity_4_8 instead
					// both NET_4_6 and NET_Unity_4_8 have the same int value, so we have to force the change
					settings.NETApiCompatibilityLevel = "NET_Unity_4_8";
					break;
				default:
					settings.NETApiCompatibilityLevel = apiCompat.ToString();
					break;
			}

#if UNITY_2022_1_OR_NEWER
			settings.IL2CPPCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(namedBuildTarget).ToString();
#elif UNITY_2021_2_OR_NEWER
			// Added in 2021.2, marked as obsolete in 2022.1
			settings.IL2CPPCodeGeneration = EditorUserBuildSettings.il2CppCodeGeneration.ToString();
#endif

#if UNITY_2022_1_OR_NEWER
			settings.InsecureHttpOption = PlayerSettings.insecureHttpOption.ToString();
#endif

#if UNITY_2021_2_OR_NEWER
			settings.IL2CPPCompilerConfig = PlayerSettings.GetIl2CppCompilerConfiguration(namedBuildTarget).ToString();
#elif UNITY_2018_1_OR_NEWER
			settings.IL2CPPCompilerConfig = PlayerSettings.GetIl2CppCompilerConfiguration(targetGroup).ToString();
#endif

#if UNITY_2018_1_OR_NEWER
			settings.AllowUnsafeCode = PlayerSettings.allowUnsafeCode;
#endif

#if UNITY_2022_2_OR_NEWER
#elif UNITY_2019_4_OR_NEWER
			// Added in Unity 2019.4, marked as obsolete in Unity 2022.2
			settings.AssemblyVersionValidation = PlayerSettings.assemblyVersionValidation;
#endif

			settings.AOTOptions = PlayerSettings.aotOptions;

#if UNITY_5_5_OR_NEWER
			settings.LocationUsageDescription = PlayerSettings.iOS.locationUsageDescription;
#else
			settings.LocationUsageDescription = PlayerSettings.locationUsageDescription;
#endif


			// rendering settings
			// ---------------------------------------------------------------

			settings.Use32BitDisplayBuffer = PlayerSettings.use32BitDisplayBuffer;
			settings.UseHDRDisplay = PlayerSettings.useHDRDisplay;
			settings.ColorSpaceUsed = PlayerSettings.colorSpace.ToString();
			settings.UseMultithreadedRendering = PlayerSettings.MTRendering;
			settings.UseGPUSkinning = PlayerSettings.gpuSkinning;
			settings.VisibleInBackground = PlayerSettings.visibleInBackground;

#if UNITY_2022_3_OR_NEWER
			settings.AllowHDRDisplaySupport = PlayerSettings.allowHDRDisplaySupport;
#endif

#if UNITY_2022_2_OR_NEWER
			settings.HdrBitDepth = PlayerSettings.hdrBitDepth.ToString();
#endif

#if UNITY_2018_3_OR_NEWER
			settings.LegacyClampBlendShapeWeights = PlayerSettings.legacyClampBlendShapeWeights;
#endif

#if UNITY_2021_2_OR_NEWER
			settings.OverrideMaxTextureSize = EditorUserBuildSettings.overrideMaxTextureSize;
			settings.OverrideTextureCompression = EditorUserBuildSettings.overrideTextureCompression.ToString();
#endif

#if UNITY_2020_1_OR_NEWER
			settings.VirtualTexturingSupportEnabled = PlayerSettings.GetVirtualTexturingSupportEnabled();
#endif

#if UNITY_2021_2_OR_NEWER
			settings.NormalMapEncoding = PlayerSettings.GetNormalMapEncoding(namedBuildTarget).ToString();
#elif UNITY_2020_2_OR_NEWER
			settings.NormalMapEncoding = PlayerSettings.GetNormalMapEncoding(targetGroup).ToString();
#endif

#if UNITY_2020_2_OR_NEWER
			settings.ShaderPrecisionModel = PlayerSettings.GetShaderPrecisionModel().ToString();
#endif

#if UNITY_2021_3_OR_NEWER && !UNITY_2021_3_0 && !UNITY_2021_3_1 && !UNITY_2021_3_2 && !UNITY_2021_3_3 && !UNITY_2021_3_4 && !UNITY_2021_3_5 && !UNITY_2021_3_6 && !UNITY_2021_3_7 && !UNITY_2021_3_8 && !UNITY_2021_3_9 && !UNITY_2021_3_10 && !UNITY_2021_3_11
			settings.ShaderChunkCountForPlatform = PlayerSettings.GetShaderChunkCountForPlatform(buildTarget);
			settings.ShaderChunkSizeInMBForPlatform = PlayerSettings.GetShaderChunkSizeInMBForPlatform(buildTarget);
#endif

#if UNITY_2022_1_OR_NEWER
			settings.StrictShaderVariantMatching = PlayerSettings.strictShaderVariantMatching;
			settings.SpriteBatchVertexThreshold = PlayerSettings.spriteBatchVertexThreshold;
#endif

#if UNITY_5_4_OR_NEWER
			settings.UseGraphicsJobs = PlayerSettings.graphicsJobs;
#endif
#if UNITY_5_5_OR_NEWER
			settings.GraphicsJobsType = PlayerSettings.graphicsJobMode.ToString();
			settings.StereoRenderingPath = PlayerSettings.stereoRenderingPath.ToString();
#endif

#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX)
#if UNITY_5_5_OR_NEWER
			settings.RenderingPathUsed = UnityEditor.Rendering.EditorGraphicsSettings
			                                        .GetTierSettings(EditorUserBuildSettings.selectedBuildTargetGroup,
				                                        Graphics.activeTier).renderingPath.ToString();
#else
			settings.RenderingPathUsed = PlayerSettings.renderingPath.ToString();
#endif
#endif

#if UNITY_5_3_OR_NEWER && !UNITY_2019_3_OR_NEWER // 5.2 (but Unity 5.2 doesn't have a define so we use 5.3) to 2019.2
			settings.EnableVirtualRealitySupport = PlayerSettings.virtualRealitySupported;
#elif UNITY_2019_3_OR_NEWER
			settings.EnableVirtualRealitySupport = UnityEngine.XR.XRSettings.enabled;
#endif

#if !UNITY_2022_2_OR_NEWER
			// collect all aspect ratios
			UnityEditor.AspectRatio[] aspectRatios =
			{
				UnityEditor.AspectRatio.Aspect4by3,
				UnityEditor.AspectRatio.Aspect5by4,
				UnityEditor.AspectRatio.Aspect16by9,
				UnityEditor.AspectRatio.Aspect16by10,
				UnityEditor.AspectRatio.AspectOthers
			};
			List<string> aspectRatiosList = new List<string>();
			for (int n = 0, len = aspectRatios.Length; n < len; ++n)
			{
				if (PlayerSettings.HasAspectRatio(aspectRatios[n]))
				{
					aspectRatiosList.Add(aspectRatios[n].ToString());
				}
			}

			if (aspectRatiosList.Count == 0)
			{
				aspectRatiosList.Add("none");
			}

			settings.AspectRatiosAllowed = aspectRatiosList.ToArray();
#else
			settings.AspectRatiosAllowed = new string[]{"N/A"}; // AspectRatio enum removed in Unity 2022.2
#endif

#if UNITY_5_3_OR_NEWER // 5.2 and greater (but Unity 5.2 doesn't have a define so we use 5.3)
			settings.GraphicsAPIsUsed = PlayerSettings.GetGraphicsAPIs(EditorUserBuildSettings.activeBuildTarget)
			                                          .Select(type => type.ToString()).ToArray();
#endif


#if UNITY_2018_2_OR_NEWER
			settings.VulkanEnableSetSRGBWrite = PlayerSettings.vulkanEnableSetSRGBWrite;
#endif

#if UNITY_2019_3_OR_NEWER
			settings.vulkanNumSwapchainBuffers = PlayerSettings.vulkanNumSwapchainBuffers;
#endif

#if UNITY_2019_4_OR_NEWER
			settings.VulkanEnableLateAcquireNextImage = PlayerSettings.vulkanEnableLateAcquireNextImage;
#endif

#if UNITY_2020_2_OR_NEWER
			settings.VulkanEnablePreTransform = PlayerSettings.vulkanEnablePreTransform;
#endif

			// shared settings
			// ---------------------------------------------------------------

			// shared between web and standalone
			settings.RunInBackground = PlayerSettings.runInBackground;
		}

		public static void PopulateWebSettings(UnityBuildSettings settings)
		{
			// web player settings
			// ---------------------------------------------------------------
#if UNITY_5_6_OR_NEWER
			settings.WebPlayerDefaultScreenWidth = 0;
			settings.WebPlayerDefaultScreenHeight = 0;

			settings.WebPlayerEnableStreaming = false;
			settings.WebPlayerDeployOffline = false;
#else
			settings.WebPlayerDefaultScreenWidth = PlayerSettings.defaultWebScreenWidth;
			settings.WebPlayerDefaultScreenHeight = PlayerSettings.defaultWebScreenHeight;

			settings.WebPlayerEnableStreaming = EditorUserBuildSettings.webPlayerStreamed;
			settings.WebPlayerDeployOffline = EditorUserBuildSettings.webPlayerOfflineDeployment;
#endif

#if UNITY_5_3_OR_NEWER
			settings.WebPlayerFirstStreamedLevelWithResources = 0;
#else
			settings.WebPlayerFirstStreamedLevelWithResources = PlayerSettings.firstStreamedLevelWithResources;
#endif

			// Web GL settings
			// ---------------------------------------------------------------

#if !UNITY_5_4_OR_NEWER
			settings.WebGLOptimizationLevel = EditorUserBuildSettings.webGLOptimizationLevel.ToString();
#endif
#if UNITY_5_4_OR_NEWER && !UNITY_2019_1_OR_NEWER
			settings.WebGLUsePreBuiltUnityEngine = EditorUserBuildSettings.webGLUsePreBuiltUnityEngine;
#endif
#if UNITY_5_5_OR_NEWER
			settings.WebGLCompressionFormat = PlayerSettings.WebGL.compressionFormat.ToString();
			settings.WebGLAutoCacheAssetsData = PlayerSettings.WebGL.dataCaching;
#if UNITY_2021_2_OR_NEWER
			settings.WebGLDebugSymbolMode = PlayerSettings.WebGL.debugSymbolMode.ToString();
			settings.WebGLCreateDebugSymbolsFile = PlayerSettings.WebGL.debugSymbolMode != WebGLDebugSymbolMode.Off;
#else
			settings.WebGLDebugSymbolMode = null;
			settings.WebGLCreateDebugSymbolsFile = PlayerSettings.WebGL.debugSymbols;
#endif
			settings.WebGLExceptionSupportType = PlayerSettings.WebGL.exceptionSupport.ToString();
			settings.WebGLMemorySize = PlayerSettings.WebGL.memorySize;
			settings.WebGLTemplatePath = PlayerSettings.WebGL.template;
#endif
		}

		public static void PopulateStandaloneSettings(UnityBuildSettings settings)
		{
			// standalone (windows/mac/linux) build settings
			// ---------------------------------------------------------------
#if !UNITY_2019_1_OR_NEWER
			settings.StandaloneResolutionDialogSettingUsed = PlayerSettings.displayResolutionDialog.ToString();
#endif
#if UNITY_2018_1_OR_NEWER
			settings.StandaloneFullScreenModeUsed = PlayerSettings.fullScreenMode.ToString();
#endif

			settings.StandaloneDefaultScreenWidth = PlayerSettings.defaultScreenWidth;
			settings.StandaloneDefaultScreenHeight = PlayerSettings.defaultScreenHeight;

#if !UNITY_2019_1_OR_NEWER
			settings.StandaloneFullScreenByDefault = PlayerSettings.defaultIsFullScreen;
#endif
#if UNITY_5_3_OR_NEWER
			settings.StandaloneAllowFullScreenSwitch = PlayerSettings.allowFullscreenSwitch;
#endif

			settings.StandaloneCaptureSingleScreen = PlayerSettings.captureSingleScreen;

			settings.StandaloneForceSingleInstance = PlayerSettings.forceSingleInstance;
			settings.StandaloneEnableResizableWindow = PlayerSettings.resizableWindow;


			// windows only build settings
			// ---------------------------------------------------------------
#if !UNITY_5_3_OR_NEWER
			settings.WinUseDirect3D11IfAvailable = PlayerSettings.useDirect3D11;
#endif

#if !UNITY_2017_3_OR_NEWER
			settings.WinDirect3D9FullscreenModeUsed = PlayerSettings.d3d9FullscreenMode.ToString();
#endif

#if !UNITY_4 && !UNITY_2019_1_OR_NEWER
			settings.WinDirect3D11FullscreenModeUsed = PlayerSettings.d3d11FullscreenMode.ToString();
#endif

#if !UNITY_5_4_OR_NEWER
			settings.StandaloneUseStereoscopic3d = PlayerSettings.stereoscopic3D;
#endif

#if UNITY_2019_1_OR_NEWER
			settings.WinUseFlipModelSwapchain = PlayerSettings.useFlipModelSwapchain;
#endif

#if UNITY_2021_3_OR_NEWER && !UNITY_2021_3_0 && !UNITY_2021_3_1 && !UNITY_2021_3_2 && !UNITY_2021_3_3 && !UNITY_2021_3_4 && !UNITY_2021_3_5 && !UNITY_2021_3_6 && !UNITY_2021_3_7 && !UNITY_2021_3_8 && !UNITY_2021_3_9 && !UNITY_2021_3_10 && !UNITY_2021_3_11 && !UNITY_2021_3_12
			settings.WinGamepadInputHint = PlayerSettings.windowsGamepadBackendHint.ToString();

			// simplify value
			switch (settings.WinGamepadInputHint)
			{
				case "WindowsGamepadBackendHintDefault":
					settings.WinGamepadInputHint = "Default";
					break;
				case "WindowsGamepadBackendHintXInput":
					settings.WinGamepadInputHint = "XInput API";
					break;
				case "WindowsGamepadBackendHintWindowsGamingInput":
					settings.WinGamepadInputHint = "GamingInput API";
					break;
			}
#endif

			// Windows Store App only build settings
			// ---------------------------------------------------------------
#if !UNITY_4 && !UNITY_2019_1_OR_NEWER
			settings.WSAGenerateReferenceProjects = EditorUserBuildSettings.wsaGenerateReferenceProjects;
#endif
#if UNITY_5_3_OR_NEWER && !UNITY_2017_1_OR_NEWER
			settings.WSASDK = EditorUserBuildSettings.wsaSDK.ToString();
#endif


			// mac only build settings
			// ---------------------------------------------------------------

#if UNITY_2017_2_OR_NEWER
			settings.MacRetinaSupport = PlayerSettings.macRetinaSupport;
#endif

#if UNITY_2021_2_OR_NEWER
			settings.MacXcodeBuildConfig = EditorUserBuildSettings.macOSXcodeBuildConfig.ToString();
#endif

			settings.MacUseAppStoreValidation = PlayerSettings.useMacAppStoreValidation;
#if !UNITY_2019_1_OR_NEWER
			settings.MacFullscreenModeUsed = PlayerSettings.macFullscreenMode.ToString();
#endif
		}


		public static void PopulateMobileSettings(UnityBuildSettings settings)
		{
			// Mobile build settings
			// ---------------------------------------------------------------

			// ("Bundle Identifier" in iOS, "Package Identifier" in Android)
#if UNITY_5_6_OR_NEWER
			settings.MobileBundleIdentifier = PlayerSettings.applicationIdentifier;
#else
			settings.MobileBundleIdentifier = PlayerSettings.bundleIdentifier;
#endif
			// ("Bundle Version" in iOS, "Version Name" in Android)
			settings.MobileBundleVersion = PlayerSettings.bundleVersion;
			settings.MobileHideStatusBar = PlayerSettings.statusBarHidden;

			settings.MobileAccelerometerFrequency = PlayerSettings.accelerometerFrequency;

			settings.MobileDefaultOrientationUsed = PlayerSettings.defaultInterfaceOrientation.ToString();
			settings.MobileEnableAutorotateToPortrait = PlayerSettings.allowedAutorotateToPortrait;
			settings.MobileEnableAutorotateToReversePortrait = PlayerSettings.allowedAutorotateToPortraitUpsideDown;
			settings.MobileEnableAutorotateToLandscapeLeft = PlayerSettings.allowedAutorotateToLandscapeLeft;
			settings.MobileEnableAutorotateToLandscapeRight = PlayerSettings.allowedAutorotateToLandscapeRight;
			settings.MobileEnableOSAutorotation = PlayerSettings.useAnimatedAutorotation;
			settings.MuteOtherAudioSources = PlayerSettings.muteOtherAudioSources;


			// iOS only build settings
			// ---------------------------------------------------------------

			// Unity 5: EditorUserBuildSettings.appendProject is removed
#if UNITY_4
			settings.iOSAppendedToProject = EditorUserBuildSettings.appendProject;
#endif

#if UNITY_5_5_OR_NEWER
			settings.iOSTargetOSVersion = PlayerSettings.iOS.targetOSVersionString;
#else
			settings.iOSTargetOSVersion = PlayerSettings.iOS.targetOSVersion.ToString();
#endif
#if UNITY_2021_2_OR_NEWER
			settings.iOSSymlinkLibraries = EditorUserBuildSettings.symlinkSources;
#else
			settings.iOSSymlinkLibraries = EditorUserBuildSettings.symlinkLibraries;
#endif
			settings.iOSAppDisplayName = PlayerSettings.iOS.applicationDisplayName;
			settings.iOSScriptCallOptimizationUsed = PlayerSettings.iOS.scriptCallOptimization.ToString();
			settings.iOSSDKVersionUsed = PlayerSettings.iOS.sdkVersion.ToString();
			settings.iOSTargetDevice = PlayerSettings.iOS.targetDevice.ToString();

#if UNITY_2021_2_OR_NEWER
			settings.iOSXcodeBuildConfig = EditorUserBuildSettings.iOSXcodeBuildConfig.ToString();
#endif

#if !UNITY_5_3_OR_NEWER
			settings.iOSTargetResolution = PlayerSettings.iOS.targetResolution.ToString();
#else
			// not sure what the equivalent is for PlayerSettings.iOS.targetResolution in Unity 5.3
			// Unity 5.3 has a Screen.resolutions but I don't know which of those in the array would be the iOS target resolution
#endif

			settings.iOSIsIconPrerendered = PlayerSettings.iOS.prerenderedIcon;
			settings.iOSRequiresPersistentWiFi = PlayerSettings.iOS.requiresPersistentWiFi.ToString();
			settings.iOSStatusBarStyle = PlayerSettings.iOS.statusBarStyle.ToString();

#if UNITY_4
			settings.iOSExitOnSuspend = PlayerSettings.iOS.exitOnSuspend;
#else
			settings.iOSAppInBackgroundBehavior = PlayerSettings.iOS.appInBackgroundBehavior.ToString();
#endif

			settings.iOSShowProgressBarInLoadingScreen = PlayerSettings.iOS.showActivityIndicatorOnLoading.ToString();

#if !UNITY_4
			settings.iOSLogObjCUncaughtExceptions = PlayerSettings.logObjCUncaughtExceptions;
#endif

#if !UNITY_5_3_OR_NEWER
			settings.iOSTargetGraphics = PlayerSettings.targetIOSGraphics.ToString();
#else
			settings.iOSTargetGraphics = string.Join(",",
				PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS).Select(type => type.ToString()).ToArray());
#endif

			// Android only build settings
			// ---------------------------------------------------------------

			settings.AndroidBuildSubtarget = EditorUserBuildSettings.androidBuildSubtarget.ToString();

#if UNITY_2018_2_OR_NEWER
			settings.AndroidBuildApkPerCpuArch = PlayerSettings.Android.buildApkPerCpuArchitecture;
#endif

#if UNITY_2021_1_OR_NEWER
			settings.AndroidCreateSymbols = EditorUserBuildSettings.androidCreateSymbols.ToString();
#endif

#if UNITY_2023_1_OR_NEWER
			settings.AndroidUseAPKExpansionFiles = PlayerSettings.Android.splitApplicationBinary;
#else
			settings.AndroidUseAPKExpansionFiles = PlayerSettings.Android.useAPKExpansionFiles;
#endif

#if !UNITY_4
			settings.AndroidAsAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
			settings.AndroidIsGame = PlayerSettings.Android.androidIsGame;
			settings.AndroidTvCompatible = PlayerSettings.Android.androidTVCompatibility;
#endif

#if UNITY_2017_4_OR_NEWER
			settings.AndroidAppBundle = EditorUserBuildSettings.buildAppBundle;
#endif

			settings.AndroidUseLicenseVerification = PlayerSettings.Android.licenseVerification;

#if UNITY_2022_2_OR_NEWER
			settings.AndroidEnableArmV9SecurityFeatures = PlayerSettings.Android.enableArmv9SecurityFeatures;
#endif


#if UNITY_4
			settings.AndroidUse24BitDepthBuffer = PlayerSettings.Android.use24BitDepthBuffer;
#else
			settings.AndroidDisableDepthAndStencilBuffers = PlayerSettings.Android.disableDepthAndStencilBuffers;
#endif

#if UNITY_2017_3_OR_NEWER
			settings.AndroidPreserveFramebufferAlpha = PlayerSettings.preserveFramebufferAlpha;
#endif

			settings.AndroidVersionCode = PlayerSettings.Android.bundleVersionCode;

			settings.AndroidMinSDKVersion = PlayerSettings.Android.minSdkVersion.ToString();

#if UNITY_5_6_OR_NEWER
			settings.AndroidTargetSDKVersion = PlayerSettings.Android.targetSdkVersion.ToString();
#endif

			// Available in 2019.4+ (but was removed in 2020.3.0 to 2020.3.16, returned in 2020.3.17)
#if UNITY_2019_4_OR_NEWER && !UNITY_2020_3_0 && !UNITY_2020_3_1 && !UNITY_2020_3_2 && !UNITY_2020_3_3 && !UNITY_2020_3_4 && !UNITY_2020_3_5 && !UNITY_2020_3_6 && !UNITY_2020_3_7 && !UNITY_2020_3_8 && !UNITY_2020_3_9 && !UNITY_2020_3_10 && !UNITY_2020_3_11 && !UNITY_2020_3_12 && !UNITY_2020_3_13 && !UNITY_2020_3_14 && !UNITY_2020_3_15 && !UNITY_2020_3_16
			settings.AndroidTargetDevice = PlayerSettings.Android.androidTargetDevices.ToString();
#elif UNITY_2020_3_OR_NEWER
			settings.AndroidTargetDevice = PlayerSettings.Android.targetArchitectures.ToString();
#else
			settings.AndroidTargetDevice = PlayerSettings.Android.targetDevice.ToString();
#endif

#if UNITY_2017_4_OR_NEWER
			settings.AndroidTargetArchitectures = PlayerSettings.Android.targetArchitectures.ToString();
#endif

			settings.AndroidSplashScreenScaleMode = PlayerSettings.Android.splashScreenScale.ToString();

			settings.AndroidPreferredInstallLocation = PlayerSettings.Android.preferredInstallLocation.ToString();

			settings.AndroidForceInternetPermission = PlayerSettings.Android.forceInternetPermission;
			settings.AndroidForceSDCardPermission = PlayerSettings.Android.forceSDCardPermission;

			settings.AndroidShowProgressBarInLoadingScreen =
				PlayerSettings.Android.showActivityIndicatorOnLoading.ToString();

			settings.AndroidKeyAliasName = PlayerSettings.Android.keyaliasName;
			settings.AndroidKeystoreName = PlayerSettings.Android.keystoreName;


#if !UNITY_5_4_OR_NEWER // blackberry build option no longer in Unity 5.4
			// BlackBerry only build settings
			// ---------------------------------------------------------------

			settings.BlackBerryBuildSubtarget = EditorUserBuildSettings.blackberryBuildSubtarget.ToString();
			settings.BlackBerryBuildType = EditorUserBuildSettings.blackberryBuildType.ToString();

#if !UNITY_5
			settings.BlackBerryAuthorID = PlayerSettings.BlackBerry.authorId;
#endif
			settings.BlackBerryDeviceAddress = PlayerSettings.BlackBerry.deviceAddress;

			settings.BlackBerrySaveLogPath = PlayerSettings.BlackBerry.saveLogPath;
			settings.BlackBerryTokenPath = PlayerSettings.BlackBerry.tokenPath;

			settings.BlackBerryTokenAuthor = PlayerSettings.BlackBerry.tokenAuthor;
			settings.BlackBerryTokenExpiration = PlayerSettings.BlackBerry.tokenExpires;

			settings.BlackBerryHasCamPermissions = PlayerSettings.BlackBerry.HasCameraPermissions();
			settings.BlackBerryHasMicPermissions = PlayerSettings.BlackBerry.HasMicrophonePermissions();
			settings.BlackBerryHasGpsPermissions = PlayerSettings.BlackBerry.HasGPSPermissions();
			settings.BlackBerryHasIdPermissions = PlayerSettings.BlackBerry.HasIdentificationPermissions();
			settings.BlackBerryHasSharedPermissions = PlayerSettings.BlackBerry.HasSharedPermissions();
#endif
		}


		public static void PopulateTvDeviceSettings(UnityBuildSettings settings)
		{
			// no more Samsung TV in Unity 2017.3 or greater

#if UNITY_4 || UNITY_5 || (UNITY_2017 && !UNITY_2017_3_OR_NEWER)
			settings.SamsungTVDeviceAddress = PlayerSettings.SamsungTV.deviceAddress;
#if !UNITY_4
			settings.SamsungTVAuthor = PlayerSettings.SamsungTV.productAuthor;
			settings.SamsungTVAuthorEmail = PlayerSettings.SamsungTV.productAuthorEmail;
			settings.SamsungTVAuthorWebsiteUrl = PlayerSettings.SamsungTV.productLink;
			settings.SamsungTVCategory = PlayerSettings.SamsungTV.productCategory.ToString();
			settings.SamsungTVDescription = PlayerSettings.SamsungTV.productDescription;
#endif
#endif
		}


		public static void PopulateBigConsoleGen07Settings(UnityBuildSettings settings)
		{
			// XBox 360 build settings
			// ---------------------------------------------------------------

#if UNITY_5_5_OR_NEWER
			// In Unity 5.5, API for Xbox 360 is still there but build options
			// do not allow Xbox 360 anymore, so we don't bother with it
#else
			settings.Xbox360BuildSubtarget = EditorUserBuildSettings.xboxBuildSubtarget.ToString();
			settings.Xbox360RunMethod = EditorUserBuildSettings.xboxRunMethod.ToString();

			settings.Xbox360TitleId = PlayerSettings.xboxTitleId;
			settings.Xbox360ImageXexFilePath = PlayerSettings.xboxImageXexFilePath;
			settings.Xbox360SpaFilePath = PlayerSettings.xboxSpaFilePath;

			settings.Xbox360AutoGenerateSpa = PlayerSettings.xboxGenerateSpa;
			settings.Xbox360EnableKinect = PlayerSettings.xboxEnableKinect;
			settings.Xbox360EnableKinectAutoTracking = PlayerSettings.xboxEnableKinectAutoTracking;
			settings.Xbox360EnableSpeech = PlayerSettings.xboxEnableSpeech;
			settings.Xbox360EnableAvatar = PlayerSettings.xboxEnableAvatar;

			settings.Xbox360SpeechDB = PlayerSettings.xboxSpeechDB;

			settings.Xbox360AdditionalTitleMemSize = PlayerSettings.xboxAdditionalTitleMemorySize;

			settings.Xbox360DeployKinectResources = PlayerSettings.xboxDeployKinectResources;
			settings.Xbox360DeployKinectHeadOrientation = PlayerSettings.xboxDeployKinectHeadOrientation;
			settings.Xbox360DeployKinectHeadPosition = PlayerSettings.xboxDeployKinectHeadPosition;
#endif


			// Playstation devices build settings
			// ---------------------------------------------------------------

#if UNITY_5_5_OR_NEWER
			// In Unity 5.5, EditorUserBuildSettings.sceBuildSubtarget is removed
#else
			settings.SCEBuildSubtarget = EditorUserBuildSettings.sceBuildSubtarget.ToString();
#endif

#if !UNITY_4
#if UNITY_2021_2_OR_NEWER
			settings.CompressBuildWithPsArc = false;
#else
			settings.CompressBuildWithPsArc = EditorUserBuildSettings.compressWithPsArc;
#endif
			settings.NeedSubmissionMaterials = EditorUserBuildSettings.needSubmissionMaterials;
#endif

			// PS3 build settings
			// ---------------------------------------------------------------

			// paths
#if UNITY_5_5_OR_NEWER
			// no more PS3 support in Unity 5.5 and greater

#elif !UNITY_5
			settings.PS3TitleConfigFilePath = PlayerSettings.ps3TitleConfigPath;
			settings.PS3DLCConfigFilePath = PlayerSettings.ps3DLCConfigPath;
			settings.PS3ThumbnailFilePath = PlayerSettings.ps3ThumbnailPath;
			settings.PS3BackgroundImageFilePath = PlayerSettings.ps3BackgroundPath;
			settings.PS3BackgroundSoundFilePath = PlayerSettings.ps3SoundPath;
			settings.PS3TrophyPackagePath = PlayerSettings.ps3TrophyPackagePath;

			settings.PS3InTrialMode = PlayerSettings.ps3TrialMode;

			settings.PS3BootCheckMaxSaveGameSizeKB = PlayerSettings.ps3BootCheckMaxSaveGameSizeKB;

			settings.PS3SaveGameSlots = PlayerSettings.ps3SaveGameSlots;

			settings.PS3NpCommsId = PlayerSettings.ps3TrophyCommId;
			settings.PS3NpCommsSig = PlayerSettings.ps3TrophyCommSig;
			settings.PS3VideoMemoryForVertexBuffers = PlayerSettings.PS3.videoMemoryForVertexBuffers;
#else
			settings.PS3TitleConfigFilePath = PlayerSettings.PS3.titleConfigPath;
			settings.PS3DLCConfigFilePath = PlayerSettings.PS3.dlcConfigPath;
			settings.PS3ThumbnailFilePath = PlayerSettings.PS3.thumbnailPath;
			settings.PS3BackgroundImageFilePath = PlayerSettings.PS3.backgroundPath;
			settings.PS3BackgroundSoundFilePath = PlayerSettings.PS3.soundPath;
			settings.PS3TrophyPackagePath = PlayerSettings.PS3.npTrophyPackagePath;

			settings.PS3InTrialMode = PlayerSettings.PS3.trialMode;

			settings.PS3NpCommsId = PlayerSettings.PS3.npTrophyCommId;
			settings.PS3NpCommsSig = PlayerSettings.PS3.npTrophyCommSig;

			settings.PS3DisableDolbyEncoding = PlayerSettings.PS3.DisableDolbyEncoding;
			settings.PS3EnableMoveSupport = PlayerSettings.PS3.EnableMoveSupport;
			settings.PS3UseSPUForUmbra = PlayerSettings.PS3.UseSPUForUmbra;
			settings.PS3EnableVerboseMemoryStats = PlayerSettings.PS3.EnableVerboseMemoryStats;
			settings.PS3VideoMemoryForAudio = PlayerSettings.PS3.videoMemoryForAudio;
			settings.PS3BootCheckMaxSaveGameSizeKB = PlayerSettings.PS3.bootCheckMaxSaveGameSizeKB;

			settings.PS3SaveGameSlots = PlayerSettings.PS3.saveGameSlots;
			settings.PS3NpAgeRating = PlayerSettings.PS3.npAgeRating;
			settings.PS3VideoMemoryForVertexBuffers = PlayerSettings.PS3.videoMemoryForVertexBuffers;
#endif


			// PS Vita build settings
			// ---------------------------------------------------------------

#if !UNITY_2018_3_OR_NEWER // PS Vita removed in 2018.3
#if UNITY_4
			settings.PSVTrophyPackagePath = PlayerSettings.psp2NPTrophyPackPath;
			settings.PSVParamSfxPath = PlayerSettings.psp2ParamSfxPath;

			settings.PSVNpCommsId = PlayerSettings.psp2NPCommsID;
			settings.PSVNpCommsSig = PlayerSettings.psp2NPCommsSig;
#else
			settings.PSVTrophyPackagePath = PlayerSettings.PSVita.npTrophyPackPath;
			settings.PSVParamSfxPath = PlayerSettings.PSVita.paramSfxPath;

			settings.PSVNpCommsId = PlayerSettings.PSVita.npCommunicationsID;
			settings.PSVNpCommsSig = PlayerSettings.PSVita.npCommsSig;


			settings.PSVBuildSubtarget = EditorUserBuildSettings.psp2BuildSubtarget.ToString();

			settings.PSVShortTitle = PlayerSettings.PSVita.shortTitle;
			settings.PSVAppVersion = PlayerSettings.PSVita.appVersion;
			settings.PSVMasterVersion = PlayerSettings.PSVita.masterVersion;
			settings.PSVAppCategory = PlayerSettings.PSVita.category.ToString();
			settings.PSVContentId = PlayerSettings.PSVita.contentID;

			settings.PSVNpAgeRating = PlayerSettings.PSVita.npAgeRating.ToString();
			settings.PSVParentalLevel = PlayerSettings.PSVita.parentalLevel.ToString();

			settings.PSVDrmType = PlayerSettings.PSVita.drmType.ToString();
			settings.PSVUpgradable = PlayerSettings.PSVita.upgradable;
			settings.PSVTvBootMode = PlayerSettings.PSVita.tvBootMode.ToString();
			settings.PSVAcquireBgm = PlayerSettings.PSVita.acquireBGM;
#if !UNITY_5_3_OR_NEWER
			settings.PSVAllowTwitterDialog = PlayerSettings.PSVita.AllowTwitterDialog;
#endif

			settings.PSVMediaCapacity = PlayerSettings.PSVita.mediaCapacity.ToString();
			settings.PSVStorageType = PlayerSettings.PSVita.storageType.ToString();
			settings.PSVTvDisableEmu = PlayerSettings.PSVita.tvDisableEmu;
			settings.PSVNpSupportGbmOrGjp = PlayerSettings.PSVita.npSupportGBMorGJP;
			settings.PSVPowerMode = PlayerSettings.PSVita.powerMode.ToString();
#if !UNITY_5_3_OR_NEWER
			settings.PSVUseLibLocation = PlayerSettings.PSVita.useLibLocation;
#endif

			settings.PSVHealthWarning = PlayerSettings.PSVita.healthWarning;
			settings.PSVEnterButtonAssignment = PlayerSettings.PSVita.enterButtonAssignment.ToString();

			settings.PSVInfoBarColor = PlayerSettings.PSVita.infoBarColor;
			settings.PSVShowInfoBarOnStartup = PlayerSettings.PSVita.infoBarOnStartup;
			settings.PSVSaveDataQuota = PlayerSettings.PSVita.saveDataQuota;

			// paths
			settings.PSVPatchChangeInfoPath = PlayerSettings.PSVita.patchChangeInfoPath;
			settings.PSVPatchOriginalPackPath = PlayerSettings.PSVita.patchOriginalPackage;
			settings.PSVKeystoneFilePath = PlayerSettings.PSVita.keystoneFile;
			settings.PSVLiveAreaBgImagePath = PlayerSettings.PSVita.liveAreaBackroundPath;
			settings.PSVLiveAreaGateImagePath = PlayerSettings.PSVita.liveAreaGatePath;
			settings.PSVCustomLiveAreaPath = PlayerSettings.PSVita.liveAreaPath;
			settings.PSVLiveAreaTrialPath = PlayerSettings.PSVita.liveAreaTrialPath;

			settings.PSVManualPath = PlayerSettings.PSVita.manualPath;
#endif
#endif
		}

		public static void PopulateBigConsoleGen08Settings(UnityBuildSettings settings)
		{
#if !UNITY_4
#if !UNITY_2021_1_OR_NEWER
			// Xbox One build settings
			// ---------------------------------------------------------------
			settings.XboxOneDeployMethod = EditorUserBuildSettings.xboxOneDeployMethod.ToString();
			settings.XboxOneTitleId = PlayerSettings.XboxOne.TitleId;
			settings.XboxOneContentId = PlayerSettings.XboxOne.ContentId;
			settings.XboxOneProductId = PlayerSettings.XboxOne.ProductId;

#if !UNITY_5_6_OR_NEWER
			settings.XboxOneSandboxId = PlayerSettings.XboxOne.SandboxId;
#endif

			settings.XboxOneServiceConfigId = PlayerSettings.XboxOne.SCID;
			settings.XboxOneVersion = PlayerSettings.XboxOne.Version;
			settings.XboxOneIsContentPackage = PlayerSettings.XboxOne.IsContentPackage;

			settings.XboxOneDescription = PlayerSettings.XboxOne.Description;

			settings.XboxOnePackagingEncryptionLevel = PlayerSettings.XboxOne.PackagingEncryption.ToString();

			settings.XboxOneAllowedProductIds = PlayerSettings.XboxOne.AllowedProductIds;

			settings.XboxOneDisableKinectGpuReservation = PlayerSettings.XboxOne.DisableKinectGpuReservation;
			settings.XboxOneEnableVariableGPU = PlayerSettings.XboxOne.EnableVariableGPU;
			settings.XboxOneStreamingInstallLaunchRange = EditorUserBuildSettings.streamingInstallLaunchRange;
			settings.XboxOnePersistentLocalStorageSize = PlayerSettings.XboxOne.PersistentLocalStorageSize;

			settings.XboxOneSocketNames = PlayerSettings.XboxOne.SocketNames;

			settings.XboxOneGameOsOverridePath = PlayerSettings.XboxOne.GameOsOverridePath;
			settings.XboxOneAppManifestOverridePath = PlayerSettings.XboxOne.AppManifestOverridePath;
			settings.XboxOnePackagingOverridePath = PlayerSettings.XboxOne.PackagingOverridePath;
#endif


			// PS4 build settings
			// ---------------------------------------------------------------
			settings.PS4BuildSubtarget = EditorUserBuildSettings.ps4BuildSubtarget.ToString();

			settings.PS4AppParameter1 = PlayerSettings.PS4.applicationParameter1;
			settings.PS4AppParameter2 = PlayerSettings.PS4.applicationParameter2;
			settings.PS4AppParameter3 = PlayerSettings.PS4.applicationParameter3;
			settings.PS4AppParameter4 = PlayerSettings.PS4.applicationParameter4;

			settings.PS4AppType = PlayerSettings.PS4.appType;
			settings.PS4AppVersion = PlayerSettings.PS4.appVersion;
			settings.PS4Category = PlayerSettings.PS4.category.ToString();
			settings.PS4ContentId = PlayerSettings.PS4.contentID;
			settings.PS4MasterVersion = PlayerSettings.PS4.masterVersion;

			settings.PS4EnterButtonAssignment = PlayerSettings.PS4.enterButtonAssignment.ToString();
			settings.PS4RemotePlayKeyAssignment = PlayerSettings.PS4.remotePlayKeyAssignment.ToString();

			settings.PS4VideoOutPixelFormat = PlayerSettings.PS4.videoOutPixelFormat.ToString();
#if UNITY_5_5_OR_NEWER
			settings.PS4VideoOutResolution = string.Format("Width: {0} ReprojectionRate: {1}",
				PlayerSettings.PS4.videoOutInitialWidth, PlayerSettings.PS4.videoOutReprojectionRate);
#else
			settings.PS4VideoOutResolution = PlayerSettings.PS4.videoOutResolution.ToString();
#endif

			settings.PS4MonoEnvVars = PlayerSettings.PS4.monoEnv;

			settings.PS4NpAgeRating = PlayerSettings.PS4.npAgeRating.ToString();
			settings.PS4ParentalLevel = PlayerSettings.PS4.parentalLevel.ToString();

			settings.PS4EnablePlayerPrefsSupport = PlayerSettings.PS4.playerPrefsSupport;

			settings.PS4EnableFriendPushNotifications = PlayerSettings.PS4.pnFriends;
			settings.PS4EnablePresencePushNotifications = PlayerSettings.PS4.pnPresence;
			settings.PS4EnableSessionPushNotifications = PlayerSettings.PS4.pnSessions;
			settings.PS4EnableGameCustomDataPushNotifications = PlayerSettings.PS4.pnGameCustomData;

			// paths
			settings.PS4BgImagePath = PlayerSettings.PS4.BackgroundImagePath;
			settings.PS4BgMusicPath = PlayerSettings.PS4.BGMPath;
			settings.PS4StartupImagePath = PlayerSettings.PS4.StartupImagePath;
			settings.PS4ParamSfxPath = PlayerSettings.PS4.paramSfxPath;
			settings.PS4NpTitleDatPath = PlayerSettings.PS4.NPtitleDatPath;
			settings.PS4NpTrophyPackagePath = PlayerSettings.PS4.npTrophyPackPath;
			settings.PS4PronunciationSigPath = PlayerSettings.PS4.PronunciationSIGPath;
			settings.PS4PronunciationXmlPath = PlayerSettings.PS4.PronunciationXMLPath;
			settings.PS4SaveDataImagePath = PlayerSettings.PS4.SaveDataImagePath;
			settings.PS4ShareFilePath = PlayerSettings.PS4.ShareFilePath;
#endif
		}

		static void PopulatePackageSettings(UnityBuildSettings settings)
		{
			var packageList = settings.PackageEntries;
			packageList.Clear();

			var builtInPackageList = settings.BuiltInPackageEntries;
			builtInPackageList.Clear();

			string projectPath = Application.dataPath;

			// remove the "Assets" so that we go to the parent folder
			projectPath = projectPath.Substring(0, projectPath.Length - 6);

			string manifestJsonPath = string.Format("{0}Packages/manifest.json", projectPath);
			if (!System.IO.File.Exists(manifestJsonPath))
			{
				// no manifest.json in project
				return;
			}
			string manifestJsonText = System.IO.File.ReadAllText(manifestJsonPath);

			string packagesLockJsonText;
			string packagesLockJsonPath = string.Format("{0}Packages/packages-lock.json", projectPath);
			if (System.IO.File.Exists(packagesLockJsonPath))
			{
				packagesLockJsonText = System.IO.File.ReadAllText(packagesLockJsonPath);
			}
			else
			{
				packagesLockJsonText = null;
			}

			PopulatePackageList(manifestJsonText, packagesLockJsonText, packageList, builtInPackageList);
		}

		public const string DEFAULT_REGISTRY_URL = "https://packages.unity.com";

		static void PopulatePackageList(string manifestJsonText, string packagesLockJsonText,
			List<BuildReportTool.UnityBuildSettings.PackageEntry> packageList, List<BuildReportTool.UnityBuildSettings.BuiltInPackageEntry> builtInPackageList)
		{
			//Debug.Log($"Exists: {manifestJsonPath}");
			var manifest = MiniJSON.Json.Deserialize(manifestJsonText) as Dictionary<string, object>;
			if (manifest == null)
			{
				return;
			}

			string mainRegistry;
			if (manifest.ContainsKey("registry"))
			{
				mainRegistry = manifest["registry"] as string;
			}
			else
			{
				// this is the default value when no registry is specified
				mainRegistry = DEFAULT_REGISTRY_URL;
			}

			List<object> scopedRegistries;
			if (manifest.ContainsKey("scopedRegistries"))
			{
				scopedRegistries = manifest["scopedRegistries"] as List<object>;
			}
			else
			{
				scopedRegistries = null;
			}

			Dictionary<string, object> externalLock;
			if (!string.IsNullOrEmpty(packagesLockJsonText))
			{
				var locks = MiniJSON.Json.Deserialize(packagesLockJsonText) as Dictionary<string, object>;
				if (locks != null && locks.ContainsKey("dependencies"))
				{
					externalLock = locks["dependencies"] as Dictionary<string, object>;
				}
				else
				{
					externalLock = null;
				}
			}
			else
			{
				externalLock = null;
			}

			if (manifest.ContainsKey("dependencies"))
			{
				Dictionary<string, object> embeddedLockUsed;
				if (manifest.ContainsKey("lock"))
				{
					embeddedLockUsed = manifest["lock"] as Dictionary<string, object>;
				}
				else
				{
					embeddedLockUsed = null;
				}

				var dependencies = manifest["dependencies"] as Dictionary<string, object>;
				if (dependencies != null)
				{

					var projectPackagesCachePath = Application.dataPath;
					projectPackagesCachePath = projectPackagesCachePath.Substring(0, projectPackagesCachePath.Length - 6);
					projectPackagesCachePath = string.Format("{0}Library/PackageCache/", projectPackagesCachePath);

					foreach (var pair in dependencies)
					{
						//Debug.Log($"package name: {pair.Key} version: {pair.Value}");
						if (string.IsNullOrEmpty(pair.Key))
						{
							continue;
						}

						if (pair.Key.StartsWith("com.unity.modules."))
						{
							BuildReportTool.UnityBuildSettings.BuiltInPackageEntry newBuiltInEntry;
							newBuiltInEntry.PackageName = pair.Key;
							newBuiltInEntry.DisplayName = null;
							builtInPackageList.Add(newBuiltInEntry);
							continue;
						}

						BuildReportTool.UnityBuildSettings.PackageEntry newEntry;
						newEntry.PackageName = pair.Key;
						newEntry.DisplayName = null;
						newEntry.VersionUsed = null;
						newEntry.LocalPath = null;

						var gotValue = pair.Value as string;
						if (embeddedLockUsed != null && embeddedLockUsed.ContainsKey(newEntry.PackageName))
						{
							// if this is a git package, it should have an entry in the manifest's lock

							newEntry.Location = gotValue;
							var lockEntry = embeddedLockUsed[newEntry.PackageName] as Dictionary<string, object>;
							if (lockEntry != null && lockEntry.ContainsKey("hash"))
							{
								string rev = lockEntry["hash"] as string;
								if (rev != null)
								{
									newEntry.VersionUsed = rev;
								}
							}
						}
						else if (externalLock != null && externalLock.ContainsKey(newEntry.PackageName))
						{
							var lockEntry = externalLock[newEntry.PackageName] as Dictionary<string, object>;
							if (lockEntry != null && lockEntry.ContainsKey("source"))
							{
								string source = lockEntry["source"] as string;
								if (source == "git" && lockEntry.ContainsKey("hash"))
								{
									string rev = lockEntry["hash"] as string;
									if (rev != null)
									{
										newEntry.VersionUsed = rev;
									}

									// for git packages, the git url is the value in the manifest
									newEntry.Location = gotValue;
								}
								else if (source == "registry" && lockEntry.ContainsKey("url"))
								{
									string packageUrl = lockEntry["url"] as string;
									if (packageUrl != null)
									{
										newEntry.Location = packageUrl;
									}
									else
									{
										newEntry.Location = null;
									}
								}
								else
								{
									newEntry.Location = null;
								}
							}
							else
							{
								newEntry.Location = null;
							}
						}
						else
						{
							newEntry.Location = null;
						}

						if (string.IsNullOrEmpty(newEntry.VersionUsed))
						{
							if (gotValue != null &&
							    (gotValue.StartsWith("file://") ||
							     gotValue.StartsWith("https://") ||
							     gotValue.StartsWith("git://") ||
							     gotValue.StartsWith("ssh://") ||
							     gotValue.StartsWith("git+https://") ||
							     gotValue.StartsWith("git+ssh://") ||
							     gotValue.StartsWith("git+file://")))
							{
								// git package, but no entry in the manifest's lock

								// check if commit hash is specified in the url
								var lastHash = gotValue.LastIndexOf('#');
								if (lastHash > -1)
								{
									var afterHash = gotValue.Substring(lastHash);
									newEntry.VersionUsed = afterHash;
									newEntry.Location = gotValue.Substring(0, lastHash);
								}
								else
								{
									// no commit hash specified
									newEntry.VersionUsed = null;
									newEntry.Location = gotValue;
								}
							}
							else if (gotValue != null && gotValue.StartsWith("file:") && !gotValue.StartsWith("file://"))
							{
								// local package
								newEntry.VersionUsed = null;
								newEntry.Location = gotValue;
							}
							else
							{
								// regular package
								newEntry.VersionUsed = gotValue;
								if (scopedRegistries != null)
								{
									newEntry.Location = GetMatchingRegistry(newEntry.PackageName, scopedRegistries);
								}

								if (string.IsNullOrEmpty(newEntry.Location))
								{
									newEntry.Location = mainRegistry;
								}
							}
						}

						// attempt to get the package's display name by loading its package.json file

						// we need the VersionUsed since that's used as part of the folder name
						if (!string.IsNullOrEmpty(newEntry.VersionUsed))
						{
							newEntry.LocalPath = GetPackageCachePath(newEntry, projectPackagesCachePath);

							if (!string.IsNullOrEmpty(newEntry.LocalPath))
							{
								string packageManifestPath = string.Format("{0}package.json", newEntry.LocalPath);
								if (System.IO.File.Exists(packageManifestPath))
								{
									var packageManifest = MiniJSON.Json.Deserialize(System.IO.File.ReadAllText(packageManifestPath)) as Dictionary<string, object>;
									if (packageManifest != null && packageManifest.ContainsKey("displayName"))
									{
										newEntry.DisplayName = packageManifest["displayName"] as string;
									}
									else
									{
										// no package.json, or package.json has no displayName
										// we can hardcode some detections here
										if (newEntry.PackageName == "com.unity.ads")
										{
											newEntry.DisplayName = "Advertisement";
										}
									}
								}
							}
						}

						packageList.Add(newEntry);
					}
				}
			}

#if BRT_PACKAGE_PARSE_DEBUG
			for (int n = 0, len = packageList.Count; n < len; ++n)
			{
				Debug.Log($"{packageList[n].PackageName} {packageList[n].VersionUsed}\n{packageList[n].DisplayName}\n{packageList[n].Location}");
			}
#endif
		}

		const int DEFAULT_SHORT_HASH_LENGTH = 10;

		static string GetPackageCachePath(BuildReportTool.UnityBuildSettings.PackageEntry entry, string projectPackagesCachePath)
		{
			string packageCachePath = string.Format("{0}{1}@{2}/", projectPackagesCachePath, entry.PackageName, entry.VersionUsed);
			if (System.IO.Directory.Exists(packageCachePath))
			{
				return packageCachePath;
			}

			if (entry.VersionUsed.Length > DEFAULT_SHORT_HASH_LENGTH)
			{
				// in Unity 2019+, git packages now only use the first 10 characters of the commit hash, so try that
				// in case this is a git package
				packageCachePath = string.Format("{0}{1}@{2}/",
					projectPackagesCachePath, entry.PackageName,
					entry.VersionUsed.Substring(0, DEFAULT_SHORT_HASH_LENGTH));

				if (System.IO.Directory.Exists(packageCachePath))
				{
					return packageCachePath;
				}
			}

			// Not found in project's packageCache. Now Try finding from user's AppData

			if (string.IsNullOrEmpty(entry.Location))
			{
				// we need the url found in Location since that's used as the folder name
				// if we don't have it, we can't determine the package cache path
				return null;
			}

			// get the registry url and remove the url, that will be the folder name
			string registryName;
			int registrySlashIdx = entry.Location.LastIndexOf("//", StringComparison.Ordinal);
			if (registrySlashIdx > -1)
			{
				registryName = entry.Location.Substring(registrySlashIdx+2);
			}
			else
			{
				return null;
			}

			if (string.IsNullOrEmpty(registryName))
			{
				return null;
			}

#if UNITY_EDITOR_WIN
			string localAppDataVar = System.Environment.GetEnvironmentVariable("LOCALAPPDATA");
			if (string.IsNullOrEmpty(localAppDataVar))
			{
				return null;
			}

			localAppDataVar = localAppDataVar.Replace("\\", "/");
#else
			string localAppDataVar = "~/Users/Library";
#endif

			packageCachePath = string.Format("{0}/Unity/cache/packages/{1}/{2}@{3}/",
				localAppDataVar, registryName, entry.PackageName, entry.VersionUsed);

			if (System.IO.Directory.Exists(packageCachePath))
			{
				return packageCachePath;
			}

			if (entry.VersionUsed.Length > DEFAULT_SHORT_HASH_LENGTH)
			{
				// in Unity 2019+, git packages now only use the first 10 characters of the commit hash, so try that
				// in case this is a git package
				packageCachePath = string.Format("{0}/Unity/cache/packages/{1}/{2}@{3}/",
					localAppDataVar, registryName, entry.PackageName,
					entry.VersionUsed.Substring(0, DEFAULT_SHORT_HASH_LENGTH));

				if (System.IO.Directory.Exists(packageCachePath))
				{
					return packageCachePath;
				}
			}

			return null;
		}

		static string GetMatchingRegistry(string packageName, List<object> scopedRegistries)
		{
			if (string.IsNullOrEmpty(packageName))
			{
				return null;
			}

			if (scopedRegistries == null)
			{
				return null;
			}

			int closestMatchScore = 0;
			string closestMatch = null;

			for (int r = 0, rLen = scopedRegistries.Count; r < rLen; ++r)
			{
				if (scopedRegistries[r] == null)
				{
					continue;
				}

				var scopedRegistry = scopedRegistries[r] as Dictionary<string, object>;
				if (scopedRegistry == null)
				{
					continue;
				}

				if (!scopedRegistry.ContainsKey("scopes"))
				{
					continue;
				}

				if (!scopedRegistry.ContainsKey("url"))
				{
					continue;
				}

				var registryUrl = scopedRegistry["url"] as string;

				var scopes = scopedRegistry["scopes"] as List<object>;
				if (scopes == null)
				{
					continue;
				}

				for (int s = 0, sLen = scopes.Count; s < sLen; ++s)
				{
					var scope = scopes[s] as string;
					if (string.IsNullOrEmpty(scope))
					{
						continue;
					}

					if (packageName.Equals(scope, StringComparison.OrdinalIgnoreCase))
					{
						// exact match, use it right away
						return registryUrl;
					}

					if (packageName.StartsWith(scope))
					{
						var currentScopeMatchScore = scope.Length;
						if (currentScopeMatchScore > closestMatchScore)
						{
							closestMatch = registryUrl;
							closestMatchScore = currentScopeMatchScore;
						}
					}
				}
			}

			return closestMatch;
		}

#if BRT_PACKAGE_PARSE_DEBUG
		[MenuItem("Window/TestPopulatePackageList1")]
		public static void TestPopulatePackageList1()
		{
			const string TEST_SCOPED_REGISTRY_TEXT = @"{
    ""scopedRegistries"": [
		{
			""name"": ""General"",
			""url"": ""https://example.com/registry"",
			""scopes"": [
			""com.example"", ""com.example.tools.physics""
				]
		},
		{
			""name"": ""Tools"",
			""url"": ""https://mycompany.example.com/tools-registry"",
			""scopes"": [
			""com.example.mycompany.tools""
				]
		}
		],
	""dependencies"": {
	""com.unity.animation"": ""1.0.0"",
	""com.example.mycompany.tools.animation"": ""1.0.0"",
	""com.example.tools.physics"": ""1.0.0"",
	""com.example.animation"": ""1.0.0""
}
}";
			var packageList = new List<BuildReportTool.UnityBuildSettings.PackageEntry>();
			var builtInPackageList = new List<BuildReportTool.UnityBuildSettings.BuiltInPackageEntry>();
			PopulatePackageList(TEST_SCOPED_REGISTRY_TEXT, null, packageList, builtInPackageList);
		}

		[MenuItem("Window/TestPopulatePackageList2")]
		public static void TestPopulatePackageList2()
		{
			const string TEST_MANIFEST_TEXT = @"{
  ""dependencies"": {
    ""com.marijnzwemmer.unity-toolbar-extender"": ""https://github.com/marijnz/unity-toolbar-extender.git"",
    ""com.unity.editorcoroutines"": ""1.0.0"",
    ""com.unity.package-manager-ui"": ""2.0.13"",
    ""com.unity.postprocessing"": ""https://github.com/AnomalousUnderdog/UnityPostProcessingStack.git"",
    ""com.unity.textmeshpro"": ""1.4.1"",
    ""com.vladfaust.unitywakatime"": ""https://github.com/AnomalousUnderdog/unity-wakatime.git#package"",
    ""com.unity.modules.ai"": ""1.0.0"",
    ""com.unity.modules.animation"": ""1.0.0"",
    ""com.unity.modules.assetbundle"": ""1.0.0"",
    ""com.unity.modules.audio"": ""1.0.0"",
    ""com.unity.modules.cloth"": ""1.0.0"",
    ""com.unity.modules.director"": ""1.0.0"",
    ""com.unity.modules.imageconversion"": ""1.0.0"",
    ""com.unity.modules.imgui"": ""1.0.0"",
    ""com.unity.modules.jsonserialize"": ""1.0.0"",
    ""com.unity.modules.particlesystem"": ""1.0.0"",
    ""com.unity.modules.physics"": ""1.0.0"",
    ""com.unity.modules.screencapture"": ""1.0.0"",
    ""com.unity.modules.terrain"": ""1.0.0"",
    ""com.unity.modules.terrainphysics"": ""1.0.0"",
    ""com.unity.modules.tilemap"": ""1.0.0"",
    ""com.unity.modules.ui"": ""1.0.0"",
    ""com.unity.modules.uielements"": ""1.0.0"",
    ""com.unity.modules.umbra"": ""1.0.0"",
    ""com.unity.modules.unitywebrequest"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestassetbundle"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestaudio"": ""1.0.0"",
    ""com.unity.modules.unitywebrequesttexture"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestwww"": ""1.0.0"",
    ""com.unity.modules.vehicles"": ""1.0.0"",
    ""com.unity.modules.video"": ""1.0.0"",
    ""com.unity.modules.wind"": ""1.0.0""
  },
  ""lock"": {
    ""com.vladfaust.unitywakatime"": {
      ""revision"": ""package"",
      ""hash"": ""34ad206dfa61e282cc3f31f2a32a6b07e0a8cbf5""
    },
    ""com.marijnzwemmer.unity-toolbar-extender"": {
      ""revision"": ""HEAD"",
      ""hash"": ""c106680bbd730a66ec8745d606f7a7ccbcbae18c""
    },
    ""com.unity.postprocessing"": {
      ""revision"": ""HEAD"",
      ""hash"": ""bb15f6b6ec3b93cb25981a7018ba0a61d78f3be6""
    }
  }
}
";
			var packageList = new List<BuildReportTool.UnityBuildSettings.PackageEntry>();
			var builtInPackageList = new List<BuildReportTool.UnityBuildSettings.BuiltInPackageEntry>();
			PopulatePackageList(TEST_MANIFEST_TEXT, null, packageList, builtInPackageList);
		}

		[MenuItem("Window/TestPopulatePackageList3")]
		public static void TestPopulatePackageList3()
		{
			const string TEST_MANIFEST_TEXT = @"{
  ""dependencies"": {
    ""com.marijnzwemmer.unity-toolbar-extender"": ""https://github.com/marijnz/unity-toolbar-extender.git"",
    ""com.unity.collab-proxy"": ""1.6.0"",
    ""com.unity.ide.rider"": ""1.2.1"",
    ""com.unity.ide.visualstudio"": ""2.0.9"",
    ""com.unity.ide.vscode"": ""1.2.3"",
    ""com.unity.test-framework"": ""1.1.27"",
    ""com.unity.textmeshpro"": ""2.1.4"",
    ""com.unity.timeline"": ""1.2.18"",
    ""com.unity.ugui"": ""1.0.0"",
    ""com.unity.modules.ai"": ""1.0.0"",
    ""com.unity.modules.androidjni"": ""1.0.0"",
    ""com.unity.modules.animation"": ""1.0.0"",
    ""com.unity.modules.assetbundle"": ""1.0.0"",
    ""com.unity.modules.audio"": ""1.0.0"",
    ""com.unity.modules.cloth"": ""1.0.0"",
    ""com.unity.modules.director"": ""1.0.0"",
    ""com.unity.modules.imageconversion"": ""1.0.0"",
    ""com.unity.modules.imgui"": ""1.0.0"",
    ""com.unity.modules.jsonserialize"": ""1.0.0"",
    ""com.unity.modules.particlesystem"": ""1.0.0"",
    ""com.unity.modules.physics"": ""1.0.0"",
    ""com.unity.modules.physics2d"": ""1.0.0"",
    ""com.unity.modules.screencapture"": ""1.0.0"",
    ""com.unity.modules.terrain"": ""1.0.0"",
    ""com.unity.modules.terrainphysics"": ""1.0.0"",
    ""com.unity.modules.tilemap"": ""1.0.0"",
    ""com.unity.modules.ui"": ""1.0.0"",
    ""com.unity.modules.uielements"": ""1.0.0"",
    ""com.unity.modules.umbra"": ""1.0.0"",
    ""com.unity.modules.unityanalytics"": ""1.0.0"",
    ""com.unity.modules.unitywebrequest"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestassetbundle"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestaudio"": ""1.0.0"",
    ""com.unity.modules.unitywebrequesttexture"": ""1.0.0"",
    ""com.unity.modules.unitywebrequestwww"": ""1.0.0"",
    ""com.unity.modules.vehicles"": ""1.0.0"",
    ""com.unity.modules.video"": ""1.0.0"",
    ""com.unity.modules.vr"": ""1.0.0"",
    ""com.unity.modules.wind"": ""1.0.0"",
    ""com.unity.modules.xr"": ""1.0.0""
  }
}
";
			const string TEST_PACKAGES_LOCK_TEXT = @"{
  ""dependencies"": {
    ""com.marijnzwemmer.unity-toolbar-extender"": {
      ""version"": ""https://github.com/marijnz/unity-toolbar-extender.git"",
      ""depth"": 0,
      ""source"": ""git"",
      ""dependencies"": {},
      ""hash"": ""df8031d46275ab1e0efc1225c33f58cda2f74872""
    },
    ""com.unity.collab-proxy"": {
      ""version"": ""1.6.0"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {},
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.ext.nunit"": {
      ""version"": ""1.0.6"",
      ""depth"": 1,
      ""source"": ""registry"",
      ""dependencies"": {},
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.ide.rider"": {
      ""version"": ""1.2.1"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {
        ""com.unity.test-framework"": ""1.1.1""
      },
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.ide.visualstudio"": {
      ""version"": ""2.0.9"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {
        ""com.unity.test-framework"": ""1.1.9""
      },
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.ide.vscode"": {
      ""version"": ""1.2.3"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {},
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.test-framework"": {
      ""version"": ""1.1.27"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {
        ""com.unity.ext.nunit"": ""1.0.6"",
        ""com.unity.modules.imgui"": ""1.0.0"",
        ""com.unity.modules.jsonserialize"": ""1.0.0""
      },
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.textmeshpro"": {
      ""version"": ""2.1.4"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {
        ""com.unity.ugui"": ""1.0.0""
      },
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.timeline"": {
      ""version"": ""1.2.18"",
      ""depth"": 0,
      ""source"": ""registry"",
      ""dependencies"": {
        ""com.unity.modules.director"": ""1.0.0"",
        ""com.unity.modules.animation"": ""1.0.0"",
        ""com.unity.modules.audio"": ""1.0.0"",
        ""com.unity.modules.particlesystem"": ""1.0.0""
      },
      ""url"": ""https://packages.unity.com""
    },
    ""com.unity.ugui"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.ui"": ""1.0.0"",
        ""com.unity.modules.imgui"": ""1.0.0""
      }
    },
    ""com.unity.modules.ai"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.androidjni"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.animation"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.assetbundle"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.audio"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.cloth"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.physics"": ""1.0.0""
      }
    },
    ""com.unity.modules.director"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.audio"": ""1.0.0"",
        ""com.unity.modules.animation"": ""1.0.0""
      }
    },
    ""com.unity.modules.imageconversion"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.imgui"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.jsonserialize"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.particlesystem"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.physics"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.physics2d"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.screencapture"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.imageconversion"": ""1.0.0""
      }
    },
    ""com.unity.modules.subsystems"": {
      ""version"": ""1.0.0"",
      ""depth"": 1,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.jsonserialize"": ""1.0.0""
      }
    },
    ""com.unity.modules.terrain"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.terrainphysics"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.physics"": ""1.0.0"",
        ""com.unity.modules.terrain"": ""1.0.0""
      }
    },
    ""com.unity.modules.tilemap"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.physics2d"": ""1.0.0""
      }
    },
    ""com.unity.modules.ui"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.uielements"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.imgui"": ""1.0.0"",
        ""com.unity.modules.jsonserialize"": ""1.0.0""
      }
    },
    ""com.unity.modules.umbra"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.unityanalytics"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.unitywebrequest"": ""1.0.0"",
        ""com.unity.modules.jsonserialize"": ""1.0.0""
      }
    },
    ""com.unity.modules.unitywebrequest"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.unitywebrequestassetbundle"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.assetbundle"": ""1.0.0"",
        ""com.unity.modules.unitywebrequest"": ""1.0.0""
      }
    },
    ""com.unity.modules.unitywebrequestaudio"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.unitywebrequest"": ""1.0.0"",
        ""com.unity.modules.audio"": ""1.0.0""
      }
    },
    ""com.unity.modules.unitywebrequesttexture"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.unitywebrequest"": ""1.0.0"",
        ""com.unity.modules.imageconversion"": ""1.0.0""
      }
    },
    ""com.unity.modules.unitywebrequestwww"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.unitywebrequest"": ""1.0.0"",
        ""com.unity.modules.unitywebrequestassetbundle"": ""1.0.0"",
        ""com.unity.modules.unitywebrequestaudio"": ""1.0.0"",
        ""com.unity.modules.audio"": ""1.0.0"",
        ""com.unity.modules.assetbundle"": ""1.0.0"",
        ""com.unity.modules.imageconversion"": ""1.0.0""
      }
    },
    ""com.unity.modules.vehicles"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.physics"": ""1.0.0""
      }
    },
    ""com.unity.modules.video"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.audio"": ""1.0.0"",
        ""com.unity.modules.ui"": ""1.0.0"",
        ""com.unity.modules.unitywebrequest"": ""1.0.0""
      }
    },
    ""com.unity.modules.vr"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.jsonserialize"": ""1.0.0"",
        ""com.unity.modules.physics"": ""1.0.0"",
        ""com.unity.modules.xr"": ""1.0.0""
      }
    },
    ""com.unity.modules.wind"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {}
    },
    ""com.unity.modules.xr"": {
      ""version"": ""1.0.0"",
      ""depth"": 0,
      ""source"": ""builtin"",
      ""dependencies"": {
        ""com.unity.modules.physics"": ""1.0.0"",
        ""com.unity.modules.jsonserialize"": ""1.0.0"",
        ""com.unity.modules.subsystems"": ""1.0.0""
      }
    }
  }
}
";

			var packageList = new List<BuildReportTool.UnityBuildSettings.PackageEntry>();
			var builtInPackageList = new List<BuildReportTool.UnityBuildSettings.BuiltInPackageEntry>();
			PopulatePackageList(TEST_MANIFEST_TEXT, TEST_PACKAGES_LOCK_TEXT, packageList, builtInPackageList);
		}
#endif
	}
}