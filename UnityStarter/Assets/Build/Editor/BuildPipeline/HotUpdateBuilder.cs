using System;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// <para>
    /// This script integrates the build pipelines for <see cref="HybridCLRBuilder"/> (code hot-update) and <see cref="YooAssetBuilder"/> (resource hot-update) 
    /// into a unified workflow. It streamlines the process of generating/compiling code and packing assets 
    /// for hot-update scenarios.
    /// </para>
    /// <para>
    /// The pipeline consists of two main workflows:
    /// 1. <see cref="FullBuild"/>: Performs a complete regeneration of HybridCLR code and metadata, followed by a YooAsset bundle build.
    ///    Flow: <c>HybridCLR -> GenerateAllAndCopy</c> + <c>YooAsset -> Build Bundles</c>
    ///    Use this when you have modified C# scripts or need a clean code generation.
    /// </para>
    /// <para>
    /// 2. <see cref="FastBuild"/>: Performs a quick compilation of HybridCLR DLLs (skipping full generation) and then builds YooAsset bundles.
    ///    Flow: <c>HybridCLR -> CompileDLLAndCopy</c> + <c>YooAsset -> Build Bundles</c>
    ///    Use this for rapid iteration when only method bodies have changed, or when you are sure full code generation is not required.
    /// </para>
    /// </summary>
    public static class HotUpdateBuilder
    {
        private const string DEBUG_FLAG = "<color=magenta>[HotUpdate]</color>";

        // Priority 2000 ensures these items appear at the bottom of the Build menu, 
        // below standard Game builds (priority ~400-500) and individual tool builds (priority ~100-200).
        [MenuItem("Build/HotUpdate Pipeline/Full Build (Generate Code + Bundles)", priority = 2000)]
        public static void FullBuild()
        {
            Debug.Log($"{DEBUG_FLAG} Starting Full HotUpdate Build Pipeline...");

            try
            {
                // This executes the complete HybridCLR generation process:
                // - Scans for hot-update assemblies
                // - Generates bridge functions and metadata
                // - Compiles the DLLs
                // - Copies the output .dll files to the StreamingAssets/HotUpdateDlls folder (as .bytes)
                Debug.Log($"{DEBUG_FLAG} Step 1/2: HybridCLR Generate All + Copy");
                HybridCLRBuilder.GenerateAllAndCopy();
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to HybridCLR error: {e.Message}");
                throw;
            }

            try
            {
                // This executes the YooAsset bundle build process using the configuration asset:
                // - Collects assets based on the collector rules
                // - Packs them into bundles
                // - Copies the bundles to StreamingAssets (if configured)
                Debug.Log($"{DEBUG_FLAG} Step 2/2: YooAsset Build Bundles");
                YooAssetBuilder.BuildFromConfig();
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to YooAsset error: {e.Message}");
                throw;
            }

            Debug.Log($"{DEBUG_FLAG} Full HotUpdate Build Pipeline Completed Successfully!");
        }

        [MenuItem("Build/HotUpdate Pipeline/Fast Build (Compile Code + Bundles)", priority = 2001)]
        public static void FastBuild()
        {
            Debug.Log($"{DEBUG_FLAG} Starting Fast HotUpdate Build Pipeline...");

            try
            {
                // This executes a faster HybridCLR process:
                // - Skips full code generation (assumes bridge functions are up to date)
                // - Only recompiles the C# DLLs
                // - Copies the output .dll files to the StreamingAssets/HotUpdateDlls folder (as .bytes)
                Debug.Log($"{DEBUG_FLAG} Step 1/2: HybridCLR Compile DLL + Copy");
                HybridCLRBuilder.CompileDllAndCopy();
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to HybridCLR error: {e.Message}");
                throw;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Step 2/2: YooAsset Build Bundles");
                YooAssetBuilder.BuildFromConfig();
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to YooAsset error: {e.Message}");
                throw;
            }

            Debug.Log($"{DEBUG_FLAG} Fast HotUpdate Build Pipeline Completed Successfully!");
        }
    }
}
