using System;
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
            if (IsHybridCLRInternalBuild(report))
            {
                UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Detected HybridCLR internal build. Disabling Obfuz build pipeline to prevent interference.");
                if (ObfuzIntegrator.IsBaseObfuzAvailable())
                {
                    ObfuzIntegrator.DisableObfuzBuildPipeline();
                    ObfuzIntegrator.SaveObfuzSettings();
                }
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

            ObfuzIntegrator.ForceInitializeObfuzSettings();

            // Ensure prerequisites are generated (this will configure ObfuzSettings)
            ObfuzIntegrator.EnsureObfuzPrerequisites();

            try
            {
                UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Ensuring Encryption VM is generated and compiled (adaptive timeout)...");
                ObfuzIntegrator.EnsureEncryptionVMGeneratedAndCompiled(); // Uses adaptive timeout
                UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Encryption VM is ready for obfuscation.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ObfuzBuildPreprocessor] Failed to ensure Encryption VM is compiled: {ex.Message}");
                UnityEngine.Debug.LogError("[ObfuzBuildPreprocessor] Build will be cancelled to prevent obfuscation failure.");
                throw new BuildFailedException($"Encryption VM is not compiled. Please generate it manually via Obfuz > GenerateEncryptionVM menu and wait for compilation to complete before building. Error: {ex.Message}");
            }

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