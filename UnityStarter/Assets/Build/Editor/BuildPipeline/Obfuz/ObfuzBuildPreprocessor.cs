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
            if (!ObfuzIntegrator.IsAvailable())
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
                // Fallback to HybridCLRBuildConfig
                HybridCLRBuildConfig hybridCLRConfig = BuildConfigHelper.GetHybridCLRConfig();
                if (hybridCLRConfig != null)
                {
                    isObfuzEnabled = hybridCLRConfig.enableObfuz;
                }
            }

            if (!isObfuzEnabled)
            {
                return;
            }

            UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] Configuring ObfuzSettings before build...");

            // Ensure prerequisites are generated (this will configure ObfuzSettings)
            ObfuzIntegrator.EnsureObfuzPrerequisites();

            // Save settings but don't clear instance yet - we want the configuration to be available during build
            // The instance will be naturally cleared/reloaded when needed by Obfuz's build callbacks
            ObfuzIntegrator.SaveObfuzSettings();

            UnityEngine.Debug.Log("[ObfuzBuildPreprocessor] ObfuzSettings configured and saved successfully.");
        }
    }
}