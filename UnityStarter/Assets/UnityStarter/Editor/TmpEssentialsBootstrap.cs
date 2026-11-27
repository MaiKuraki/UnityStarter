using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace UnityStarter.Editor
{
    /// <summary>
    /// Restores the TMP shaders needed by the project's own font assets, and drops the sample fonts
    /// and materials that ship with the package. Those resources are Unity-licensed and stay out of
    /// version control, so a clone has no shaders until this runs.
    ///
    /// Shader presence is the trigger. Checking settings alone would report a healthy install while
    /// every TMP material renders through the error shader.
    /// </summary>
    [InitializeOnLoad]
    internal static class TmpEssentialsBootstrap
    {
        private const string PackageResourceRelativePath =
            "Package Resources/TMP Essential Resources.unitypackage";
        private const string ImportedRoot = "Assets/TextMesh Pro";
        private const string TargetRoot = "Assets/ThirdParty/TextMesh Pro";
        private const string RuntimeShaderPath = TargetRoot + "/Shaders/TMP_SDF.shader";
        private const string SettingsRelativePath = "/Resources/TMP Settings.asset";
        private const string DefaultFontRelativePath = "/Resources/Fonts & Materials";
        private const double RelocationWindowSeconds = 60.0;

        private static double relocationDeadline;
        private static byte[] pendingSettingsBackup;
        private static string pendingSettingsPath;

        static TmpEssentialsBootstrap()
        {
            EditorApplication.delayCall += EnsureEssentials;

            // ImportPackage is asynchronous. Poll until the importer drops the tree, then stop.
            relocationDeadline = EditorApplication.timeSinceStartup + RelocationWindowSeconds;
            EditorApplication.update += TickRelocation;
        }

        private static void EnsureEssentials()
        {
            if (RelocateImportedResources() && HasRuntimeShader())
            {
                PruneSampleContent();
                return;
            }

            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Settings).Assembly);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                Debug.LogWarning(
                    "TMP package path could not be resolved. Import TMP Essential Resources manually.");
                return;
            }

            string packagePath = Path.Combine(package.resolvedPath, PackageResourceRelativePath);
            if (!File.Exists(packagePath))
            {
                Debug.LogWarning("TMP Essential Resources not found at " + packagePath);
                return;
            }

            pendingSettingsPath = TargetRoot + SettingsRelativePath;
            pendingSettingsBackup = File.Exists(pendingSettingsPath)
                ? File.ReadAllBytes(pendingSettingsPath)
                : null;

            AssetDatabase.ImportPackage(packagePath, false);
            Debug.Log("Imported TMP Essential Resources from " + packagePath);
        }

        // Finish only once the database is idle. Right after Library is cleared the importer is
        // still working, and AssetDatabase queries report nothing exists while that runs.
        private static void TickRelocation()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                return;
            }

            if (RelocateImportedResources() && HasRuntimeShader())
            {
                PruneSampleContent();
                RestorePendingSettings();
                EditorApplication.update -= TickRelocation;
                return;
            }

            if (EditorApplication.timeSinceStartup > relocationDeadline)
            {
                EditorApplication.update -= TickRelocation;
            }
        }

        // File.Exists is deliberate. AssetDatabase.LoadAssetAtPath returns null while the database
        // is still importing, which happens right after Library is cleared, so a healthy install
        // would be reported as missing and trigger a spurious re-import.
        private static bool HasRuntimeShader()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return File.Exists(Path.Combine(projectRoot, RuntimeShaderPath));
        }

        // The project renders Chinese with its own font assets, so the bundled LiberationSans font
        // and its generated material set are dead weight after every import.
        private static void PruneSampleContent()
        {
            DeleteFolder(TargetRoot + "/Fonts");
            DeleteFolder(TargetRoot + DefaultFontRelativePath);
            DeleteFolder(TargetRoot + "/Sprites");
            DeleteFolder(TargetRoot + "/Resources/Sprite Assets");
        }

        private static void DeleteFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        // The import always rewrites TMP Settings with Unity defaults. Put the authored values back
        // so the default font and fallback chain survive a restore.
        private static void RestorePendingSettings()
        {
            if (pendingSettingsBackup == null || !File.Exists(pendingSettingsPath))
            {
                return;
            }

            File.WriteAllBytes(pendingSettingsPath, pendingSettingsBackup);
            AssetDatabase.ImportAsset(pendingSettingsPath);
            pendingSettingsBackup = null;
            Debug.Log("Restored TMP Settings from before the import.");
        }

        // Returns true once the target exists or nothing is left to move.
        private static bool RelocateImportedResources()
        {
            if (AssetDatabase.IsValidFolder(TargetRoot))
            {
                return true;
            }

            if (!AssetDatabase.IsValidFolder(ImportedRoot))
            {
                return false;
            }

            string parent = TargetRoot.Substring(0, TargetRoot.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(parent))
            {
                AssetDatabase.CreateFolder("Assets", "ThirdParty");
            }

            string error = AssetDatabase.MoveAsset(ImportedRoot, TargetRoot);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("Could not move TMP Essential Resources: " + error);
                return true;
            }

            Debug.Log("Moved TMP Essential Resources to " + TargetRoot);
            return true;
        }
    }
}
