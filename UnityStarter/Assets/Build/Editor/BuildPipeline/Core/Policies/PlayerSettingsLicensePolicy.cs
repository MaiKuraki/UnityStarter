using System;
using UnityEditor;
using UnityEditorInternal;

namespace Build.Pipeline.Editor
{
    internal readonly struct EditorBuildSceneState
    {
        internal EditorBuildSceneState(string path, bool enabled)
        {
            Path = path ?? string.Empty;
            Enabled = enabled;
        }

        internal string Path { get; }
        internal bool Enabled { get; }
    }

    internal readonly struct PlayerSettingsOwnedState
    {
        internal PlayerSettingsOwnedState(
            int scriptingBackend,
            string companyName,
            string productName,
            string bundleVersion,
            string applicationIdentifier,
            int androidBundleVersionCode,
            string iosBuildNumber,
            bool exportAndroidProject,
            bool developmentBuild,
            EditorBuildSceneState[] editorBuildScenes,
            PlayerSettingsSplashState splash,
            string[] preloadedAssetIds)
        {
            ScriptingBackend = scriptingBackend;
            CompanyName = companyName ?? string.Empty;
            ProductName = productName ?? string.Empty;
            BundleVersion = bundleVersion ?? string.Empty;
            ApplicationIdentifier = applicationIdentifier ?? string.Empty;
            AndroidBundleVersionCode = androidBundleVersionCode;
            IosBuildNumber = iosBuildNumber ?? string.Empty;
            ExportAndroidProject = exportAndroidProject;
            DevelopmentBuild = developmentBuild;
            EditorBuildScenes = editorBuildScenes == null
                ? Array.Empty<EditorBuildSceneState>()
                : (EditorBuildSceneState[])editorBuildScenes.Clone();
            Splash = splash;
            PreloadedAssetIds = preloadedAssetIds == null
                ? Array.Empty<string>()
                : (string[])preloadedAssetIds.Clone();
        }

        internal int ScriptingBackend { get; }
        internal string CompanyName { get; }
        internal string ProductName { get; }
        internal string BundleVersion { get; }
        internal string ApplicationIdentifier { get; }
        internal int AndroidBundleVersionCode { get; }
        internal string IosBuildNumber { get; }
        internal bool ExportAndroidProject { get; }
        internal bool DevelopmentBuild { get; }
        internal EditorBuildSceneState[] EditorBuildScenes { get; }
        internal PlayerSettingsSplashState Splash { get; }
        internal string[] PreloadedAssetIds { get; }
    }

    internal readonly struct PlayerSettingsSplashState
    {
        internal PlayerSettingsSplashState(bool showSplashScreen, bool showUnityLogo)
        {
            ShowSplashScreen = showSplashScreen;
            ShowUnityLogo = showUnityLogo;
        }

        internal bool ShowSplashScreen { get; }
        internal bool ShowUnityLogo { get; }
    }

    /// <summary>
    /// Normalizes the serialized PlayerSettings post-image to the constraints of
    /// the active Unity license before it becomes transaction-owned.
    /// </summary>
    internal static class PlayerSettingsLicensePolicy
    {
        private const string ShowSplashScreenProperty = "m_ShowUnitySplashScreen";
        private const string ShowUnityLogoProperty = "m_ShowUnitySplashLogo";

        internal static PlayerSettingsSplashState Capture(PlayerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var serialized = new SerializedObject(settings);
            serialized.UpdateIfRequiredOrScript();
            return new PlayerSettingsSplashState(
                RequireBoolean(serialized, ShowSplashScreenProperty).boolValue,
                RequireBoolean(serialized, ShowUnityLogoProperty).boolValue);
        }

        internal static void Apply(PlayerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (InternalEditorUtility.HasPro())
            {
                return;
            }

            ApplyExact(settings, new PlayerSettingsSplashState(true, true));
        }

        internal static void ApplyExact(
            PlayerSettings settings,
            PlayerSettingsSplashState state)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var serialized = new SerializedObject(settings);
            serialized.UpdateIfRequiredOrScript();
            RequireBoolean(serialized, ShowSplashScreenProperty).boolValue =
                state.ShowSplashScreen;
            RequireBoolean(serialized, ShowUnityLogoProperty).boolValue =
                state.ShowUnityLogo;
            if (!serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                PlayerSettingsSplashState persisted = Capture(settings);
                if (persisted.ShowSplashScreen != state.ShowSplashScreen
                    || persisted.ShowUnityLogo != state.ShowUnityLogo)
                {
                    throw new InvalidOperationException(
                        "The serialized PlayerSettings splash state could not be applied exactly.");
                }
            }
        }

        internal static void Validate(PlayerSettings settings)
        {
            if (InternalEditorUtility.HasPro())
            {
                return;
            }

            PlayerSettingsSplashState state = Capture(settings);
            if (!state.ShowSplashScreen || !state.ShowUnityLogo)
            {
                throw new InvalidOperationException(
                    "Unity Personal requires the serialized PlayerSettings splash screen and Unity logo to be enabled before BuildPlayer starts.");
            }
        }

        internal static bool RequiresMutation(PlayerSettingsSplashState original)
        {
            return !InternalEditorUtility.HasPro()
                && (!original.ShowSplashScreen || !original.ShowUnityLogo);
        }

        private static SerializedProperty RequireBoolean(
            SerializedObject serialized,
            string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            {
                throw new MissingMemberException(
                    typeof(PlayerSettings).FullName,
                    propertyName);
            }

            return property;
        }
    }
}
