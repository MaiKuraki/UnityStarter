using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Preprocesses build to configure ObfuzSettings before build starts.
    /// This ensures that ObfuzSettings are properly configured before Obfuz's build callbacks are invoked.
    /// </summary>
    public class ObfuzBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100; // Run early, before other preprocessors

        public void OnPreprocessBuild(BuildReport report)
        {
            // Skip HybridCLR internal builds (for generating stripped AOT DLLs)
            if (IsHybridCLRInternalBuild(report))
            {
                UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Skipping Obfuz configuration for HybridCLR internal build. HybridCLR generation steps will continue normally.");
                return;
            }

            if (!ObfuzIntegrator.IsBaseObfuzAvailable())
            {
                return;
            }

            bool isObfuzEnabled = false;

            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData != null && buildData.UseObfuz)
            {
                isObfuzEnabled = true;
            }
            else
            {
                // Fallback to HybridCLRBuildConfig (for HybridCLR projects)
                HybridCLRBuildConfig hybridCLRConfig = BuildConfigHelper.GetHybridCLRConfig();
                if (hybridCLRConfig != null)
                {
                    isObfuzEnabled = hybridCLRConfig.enableObfuz;
                }
            }

            if (!isObfuzEnabled)
            {
                ObfuzIntegrator.DisableObfuzBuildPipeline();
                ObfuzIntegrator.SaveObfuzSettings();
                UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Obfuz is disabled. Build pipeline disabled.");
                return;
            }

            UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Configuring ObfuzSettings before build...");

            // Ensure prerequisites are generated (this will configure ObfuzSettings)
            ObfuzIntegrator.EnsureObfuzPrerequisites();

            // Enable Obfuz build pipeline (for non-HybridCLR projects, this ensures Obfuz's native ObfuscationProcess runs)
            // For HybridCLR projects, this doesn't interfere since HybridCLR uses its own obfuscation flow
            ObfuzIntegrator.EnableObfuzBuildPipeline();

            ObfuzIntegrator.SaveObfuzSettings();

            UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] ObfuzSettings configured and saved successfully.");
        }

        /// <summary>
        /// Checks if this is a HybridCLR internal build for generating stripped AOT DLLs.
        /// HybridCLR uses temporary builds with paths containing "StrippedAOTDllsTempProj" to generate AOT DLLs.
        /// HybridCLR sets buildScriptsOnly = true before starting the internal build.
        /// </summary>
        private static bool IsHybridCLRInternalBuild(BuildReport report)
        {
            if (UnityEditor.EditorUserBuildSettings.buildScriptsOnly)
            {
                BuildData buildData = BuildConfigHelper.GetBuildData();
                if (buildData != null && buildData.UseHybridCLR)
                {
                    return true;
                }
            }

            if (report != null)
            {
                string outputPath = report.summary.outputPath;
                if (!string.IsNullOrEmpty(outputPath) && outputPath.Contains("StrippedAOTDllsTempProj", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}