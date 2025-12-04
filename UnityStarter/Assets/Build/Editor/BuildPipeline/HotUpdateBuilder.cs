using System;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// <para>
    /// This script integrates the build pipelines for <see cref="HybridCLRBuilder"/> (code hot-update) and asset management systems
    /// (<see cref="YooAssetBuilder"/> or <see cref="AddressablesBuilder"/>) into a unified workflow. It streamlines the process 
    /// of generating/compiling code and packing assets for hot-update scenarios.
    /// </para>
    /// <para>
    /// The pipeline consists of two main workflows:
    /// 1. <see cref="FullBuild"/>: Performs a complete regeneration of HybridCLR code and metadata, followed by asset bundle build.
    ///    Flow: <c>HybridCLR -> GenerateAllAndCopy</c> + <c>Asset Management -> Build Bundles</c>
    ///    Use this when you have modified C# scripts or need a clean code generation.
    /// </para>
    /// <para>
    /// 2. <see cref="FastBuild"/>: Performs a quick compilation of HybridCLR DLLs (skipping full generation) and then builds asset bundles.
    ///    Flow: <c>HybridCLR -> CompileDLLAndCopy</c> + <c>Asset Management -> Build Bundles</c>
    ///    Use this for rapid iteration when only method bodies have changed, or when you are sure full code generation is not required.
    /// </para>
    /// <para>
    /// The asset management system (YooAsset or Addressables) is automatically selected based on <see cref="BuildData"/> configuration.
    /// </para>
    /// </summary>
    public static class HotUpdateBuilder
    {
        private const string DEBUG_FLAG = "<color=magenta>[HotUpdate]</color>";

        private static BuildData GetBuildData()
        {
            return BuildConfigHelper.GetBuildData();
        }

        [MenuItem("Build/HotUpdate Pipeline/Full Build (Generate Code + Bundles)", priority = 28)]
        public static void FullBuild()
        {
            Debug.Log($"{DEBUG_FLAG} Starting Full HotUpdate Build Pipeline...");

            BuildData buildData = GetBuildData();
            if (buildData == null)
            {
                Debug.LogError($"{DEBUG_FLAG} BuildData not found. Please create a BuildData asset.");
                return;
            }

            try
            {
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
                Debug.Log($"{DEBUG_FLAG} Step 2/2: Asset Management Build Bundles");
                BuildAssetBundles(buildData);
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to asset management error: {e.Message}");
                throw;
            }

            Debug.Log($"{DEBUG_FLAG} Full HotUpdate Build Pipeline Completed Successfully!");
        }

        [MenuItem("Build/HotUpdate Pipeline/Fast Build (Compile Code + Bundles)", priority = 29)]
        public static void FastBuild()
        {
            Debug.Log($"{DEBUG_FLAG} Starting Fast HotUpdate Build Pipeline...");

            BuildData buildData = GetBuildData();
            if (buildData == null)
            {
                Debug.LogError($"{DEBUG_FLAG} BuildData not found. Please create a BuildData asset.");
                return;
            }

            try
            {
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
                Debug.Log($"{DEBUG_FLAG} Step 2/2: Asset Management Build Bundles");
                BuildAssetBundles(buildData);
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Pipeline stopped due to asset management error: {e.Message}");
                throw;
            }

            Debug.Log($"{DEBUG_FLAG} Fast HotUpdate Build Pipeline Completed Successfully!");
        }

        private static void BuildAssetBundles(BuildData buildData)
        {
            if (buildData.UseYooAsset)
            {
                Debug.Log($"{DEBUG_FLAG} Using YooAsset for asset management.");
                YooAssetBuilder.BuildFromConfig();
            }
            else if (buildData.UseAddressables)
            {
                Debug.Log($"{DEBUG_FLAG} Using Addressables for asset management.");
                AddressablesBuilder.BuildFromConfig();
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} No asset management system selected in BuildData. Skipping asset bundle build.");
            }
        }
    }
}