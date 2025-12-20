using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Integrator for Obfuz code obfuscation support.
    /// </summary>
    public static class ObfuzIntegrator
    {
        private const string DEBUG_FLAG = "<color=cyan>[Obfuz]</color>";

        /// <summary>
        /// Checks if base Obfuz package is available (works for both HybridCLR and non-HybridCLR projects).
        /// </summary>
        public static bool IsBaseObfuzAvailable()
        {
            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                return obfuzSettingsType != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to check base Obfuz availability: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if Obfuz4HybridCLR packages are available (for HybridCLR-specific features).
        /// </summary>
        public static bool IsHybridCLRObfuzAvailable()
        {
            try
            {
                Type obfuscateUtilType = ReflectionCache.GetType("Obfuz4HybridCLR.ObfuscateUtil");
                Type prebuildCommandExtType = ReflectionCache.GetType("Obfuz4HybridCLR.PrebuildCommandExt");
                return obfuscateUtilType != null && prebuildCommandExtType != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to check Obfuz4HybridCLR availability: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if Obfuz packages are available in the project (checks HybridCLR extension by default for backward compatibility).
        /// </summary>
        public static bool IsAvailable()
        {
            return IsHybridCLRObfuzAvailable();
        }

        /// <summary>
        /// Generates Obfuz encryption VM code file.
        /// This must be called before obfuscation.
        /// </summary>
        public static void GenerateEncryptionVM()
        {
            if (!IsBaseObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Base Obfuz package not found. Skipping encryption VM generation.");
                return;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Generating encryption VM...");
                if (!EditorApplication.ExecuteMenuItem("Obfuz/GenerateEncryptionVM"))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to execute Obfuz/GenerateEncryptionVM menu item.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Encryption VM generated successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate encryption VM: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Generates Obfuz secret key file.
        /// This must be called before obfuscation.
        /// </summary>
        public static void GenerateSecretKeyFile()
        {
            if (!IsBaseObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Base Obfuz package not found. Skipping secret key file generation.");
                return;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Generating secret key file...");
                if (!EditorApplication.ExecuteMenuItem("Obfuz/GenerateSecretKeyFile"))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to execute Obfuz/GenerateSecretKeyFile menu item.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Secret key file generated successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate secret key file: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Configures ObfuzSettings to ensure Assembly-CSharp is added to NonObfuscatedButReferencingObfuscatedAssemblies.
        /// This is required when Assembly-CSharp references obfuscated assemblies like Obfuz.Runtime.
        /// </summary>
        public static void ConfigureObfuzSettings()
        {
            if (!IsBaseObfuzAvailable())
            {
                return;
            }

            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                if (obfuzSettingsType == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings type not found. Cannot configure settings.");
                    return;
                }

                PropertyInfo instanceProperty = ReflectionCache.GetProperty(obfuzSettingsType, "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Instance property not found.");
                    return;
                }

                object obfuzSettingsInstance = instanceProperty.GetValue(null);
                if (obfuzSettingsInstance == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Instance is null.");
                    return;
                }

                FieldInfo assemblySettingsField = ReflectionCache.GetField(obfuzSettingsType, "assemblySettings", BindingFlags.Public | BindingFlags.Instance);
                if (assemblySettingsField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.assemblySettings field not found.");
                    return;
                }

                object assemblySettings = assemblySettingsField.GetValue(obfuzSettingsInstance);
                if (assemblySettings == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.assemblySettings is null.");
                    return;
                }

                Type assemblySettingsType = ReflectionCache.GetType("Obfuz.Settings.AssemblySettings");
                if (assemblySettingsType == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} AssemblySettings type not found.");
                    return;
                }

                FieldInfo nonObfuscatedField = ReflectionCache.GetField(assemblySettingsType, "nonObfuscatedButReferencingObfuscatedAssemblies", BindingFlags.Public | BindingFlags.Instance);
                if (nonObfuscatedField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} AssemblySettings.nonObfuscatedButReferencingObfuscatedAssemblies field not found.");
                    return;
                }

                string[] currentArray = nonObfuscatedField.GetValue(assemblySettings) as string[];
                List<string> currentList = currentArray != null ? new List<string>(currentArray) : new List<string>();

                // Add Assembly-CSharp if not already present
                const string assemblyCSharp = "Assembly-CSharp";
                if (!currentList.Contains(assemblyCSharp))
                {
                    currentList.Add(assemblyCSharp);
                    nonObfuscatedField.SetValue(assemblySettings, currentList.ToArray());
                    Debug.Log($"{DEBUG_FLAG} Added '{assemblyCSharp}' to NonObfuscatedButReferencingObfuscatedAssemblies.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} '{assemblyCSharp}' already in NonObfuscatedButReferencingObfuscatedAssemblies.");
                }

                LogObfuscatedAssemblies(assemblySettings, assemblySettingsType);

                // Always save to ensure settings are persisted (even if Assembly-CSharp was already present)
                // This is important because the settings might have been modified in memory but not saved
                MethodInfo saveMethod = ReflectionCache.GetMethod(obfuzSettingsType, "Save", BindingFlags.Public | BindingFlags.Static);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(null, null);
                    Debug.Log($"{DEBUG_FLAG} ObfuzSettings saved to disk.");

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    // Verify the configuration was saved
                    string[] savedArray = nonObfuscatedField.GetValue(assemblySettings) as string[];
                    if (savedArray != null && savedArray.Contains(assemblyCSharp))
                    {
                        Debug.Log($"{DEBUG_FLAG} Verified '{assemblyCSharp}' is in saved configuration.");
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Warning: '{assemblyCSharp}' may not be in saved configuration. Please check ObfuzSettings manually.");
                    }
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Save method not found. Settings may not be persisted.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to configure ObfuzSettings: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Logs the list of assemblies that will be obfuscated.
        /// </summary>
        private static void LogObfuscatedAssemblies(object assemblySettings, Type assemblySettingsType)
        {
            try
            {
                // Try to get assembliesToObfuscate field
                FieldInfo assembliesToObfuscateField = ReflectionCache.GetField(assemblySettingsType, "assembliesToObfuscate", BindingFlags.Public | BindingFlags.Instance);
                if (assembliesToObfuscateField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} AssemblySettings.assembliesToObfuscate field not found.");
                    return;
                }

                string[] assembliesToObfuscate = assembliesToObfuscateField.GetValue(assemblySettings) as string[];

                // Try to call GetAssembliesToObfuscate() method to get the complete list (including Obfuz.Runtime if enabled)
                MethodInfo getAssembliesMethod = ReflectionCache.GetMethod(assemblySettingsType, "GetAssembliesToObfuscate", BindingFlags.Public | BindingFlags.Instance);
                List<string> completeList = null;

                if (getAssembliesMethod != null)
                {
                    try
                    {
                        object result = getAssembliesMethod.Invoke(assemblySettings, null);
                        completeList = result as List<string>;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Failed to call GetAssembliesToObfuscate(): {ex.Message}");
                    }
                }

                // Log the assemblies that will be obfuscated
                if (completeList != null && completeList.Count > 0)
                {
                    Debug.Log($"{DEBUG_FLAG} === Assemblies to be obfuscated ({completeList.Count}) ===");
                    for (int i = 0; i < completeList.Count; i++)
                    {
                        Debug.Log($"{DEBUG_FLAG}   [{i + 1}] {completeList[i]}");
                    }
                    Debug.Log($"{DEBUG_FLAG} =============================================");
                }
                else if (assembliesToObfuscate != null && assembliesToObfuscate.Length > 0)
                {
                    Debug.Log($"{DEBUG_FLAG} === Assemblies to be obfuscated ({assembliesToObfuscate.Length}) ===");
                    for (int i = 0; i < assembliesToObfuscate.Length; i++)
                    {
                        Debug.Log($"{DEBUG_FLAG}   [{i + 1}] {assembliesToObfuscate[i]}");
                    }
                    Debug.Log($"{DEBUG_FLAG} =============================================");
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} No assemblies configured for obfuscation. Please configure assembliesToObfuscate in ObfuzSettings.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to log obfuscated assemblies: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves ObfuzSettings to disk without clearing the instance.
        /// </summary>
        public static void SaveObfuzSettings()
        {
            if (!IsBaseObfuzAvailable())
            {
                return;
            }

            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                if (obfuzSettingsType == null)
                {
                    return;
                }

                MethodInfo saveMethod = ReflectionCache.GetMethod(obfuzSettingsType, "Save", BindingFlags.Public | BindingFlags.Static);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(null, null);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    Debug.Log($"{DEBUG_FLAG} ObfuzSettings saved to disk.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to save ObfuzSettings: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces ObfuzSettings to reload from disk.
        /// This ensures that any changes made to ObfuzSettings are picked up.
        /// </summary>
        public static void ForceReloadObfuzSettings()
        {
            if (!IsBaseObfuzAvailable())
            {
                return;
            }

            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                if (obfuzSettingsType == null)
                {
                    return;
                }

                SaveObfuzSettings();

                FieldInfo instanceField = ReflectionCache.GetField(obfuzSettingsType, "s_Instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (instanceField != null)
                {
                    instanceField.SetValue(null, null);
                    Debug.Log($"{DEBUG_FLAG} ObfuzSettings instance cleared, will reload from disk on next access.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to force reload ObfuzSettings: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures Obfuz prerequisites are generated before obfuscation.
        /// </summary>
        public static void EnsureObfuzPrerequisites()
        {
            if (!IsBaseObfuzAvailable())
            {
                return;
            }

            Debug.Log($"{DEBUG_FLAG} Ensuring Obfuz prerequisites are generated...");
            GenerateEncryptionVM();
            GenerateSecretKeyFile();
            ConfigureObfuzSettings();
        }

        /// <summary>
        /// Sets Obfuz build pipeline settings enable state.
        /// </summary>
        private static void SetObfuzBuildPipelineEnabled(bool enabled)
        {
            if (!IsBaseObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Base Obfuz package not found. Cannot set build pipeline state.");
                return;
            }

            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                if (obfuzSettingsType == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings type not found.");
                    return;
                }

                PropertyInfo instanceProperty = ReflectionCache.GetProperty(obfuzSettingsType, "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Instance property not found.");
                    return;
                }

                object obfuzSettingsInstance = instanceProperty.GetValue(null);
                if (obfuzSettingsInstance == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Instance is null.");
                    return;
                }

                FieldInfo buildPipelineSettingsField = ReflectionCache.GetField(obfuzSettingsType, "buildPipelineSettings", BindingFlags.Public | BindingFlags.Instance);
                if (buildPipelineSettingsField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.buildPipelineSettings field not found.");
                    return;
                }

                object buildPipelineSettings = buildPipelineSettingsField.GetValue(obfuzSettingsInstance);
                if (buildPipelineSettings == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.buildPipelineSettings is null.");
                    return;
                }

                Type buildPipelineSettingsType = ReflectionCache.GetType("Obfuz.Settings.BuildPipelineSettings");
                if (buildPipelineSettingsType == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} BuildPipelineSettings type not found.");
                    return;
                }

                FieldInfo enableField = ReflectionCache.GetField(buildPipelineSettingsType, "enable", BindingFlags.Public | BindingFlags.Instance);
                if (enableField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} BuildPipelineSettings.enable field not found.");
                    return;
                }

                bool currentEnable = (bool)enableField.GetValue(buildPipelineSettings);
                if (currentEnable != enabled)
                {
                    enableField.SetValue(buildPipelineSettings, enabled);
                    Debug.Log($"{DEBUG_FLAG} Obfuz build pipeline settings {(enabled ? "enabled" : "disabled")}.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Obfuz build pipeline settings already {(enabled ? "enabled" : "disabled")}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to set Obfuz build pipeline state: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Enables Obfuz build pipeline settings for non-HybridCLR projects.
        /// This ensures Obfuz's native ObfuscationProcess will run during build.
        /// </summary>
        public static void EnableObfuzBuildPipeline()
        {
            SetObfuzBuildPipelineEnabled(true);
        }

        /// <summary>
        /// Disables Obfuz build pipeline settings.
        /// This ensures Obfuz's native ObfuscationProcess will be skipped during build.
        /// </summary>
        public static void DisableObfuzBuildPipeline()
        {
            SetObfuzBuildPipelineEnabled(false);
        }

        /// <summary>
        /// Obfuscates hot update assemblies for the given build target.
        /// This method is for HybridCLR-specific obfuscation flow.
        /// </summary>
        public static void ObfuscateHotUpdateAssemblies(BuildTarget target, string outputDir)
        {
            if (!IsHybridCLRObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Obfuz4HybridCLR packages not found. Skipping hot update assembly obfuscation.");
                return;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Starting obfuscation for platform: {target}...");

                Type obfuscateUtilType = ReflectionCache.GetType("Obfuz4HybridCLR.ObfuscateUtil");
                if (obfuscateUtilType == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} ObfuscateUtil type not found.");
                    return;
                }

                MethodInfo obfuscateMethod = ReflectionCache.GetMethod(
                    obfuscateUtilType,
                    "ObfuscateHotUpdateAssemblies",
                    BindingFlags.Public | BindingFlags.Static,
                    new Type[] { typeof(BuildTarget), typeof(string) });

                if (obfuscateMethod == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} ObfuscateHotUpdateAssemblies method not found.");
                    return;
                }

                obfuscateMethod.Invoke(null, new object[] { target, outputDir });
                Debug.Log($"{DEBUG_FLAG} Obfuscation completed successfully for platform: {target}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Obfuscation failed: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Generates method bridge and reverse P/Invoke wrapper using obfuscated assemblies.
        /// This must be called after obfuscation.
        /// </summary>
        public static void GenerateMethodBridgeAndReversePInvokeWrapper(BuildTarget target, string obfuscatedHotUpdateDllPath)
        {
            if (!IsHybridCLRObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Obfuz4HybridCLR packages not found. Skipping method bridge generation.");
                return;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Generating method bridge and reverse P/Invoke wrapper using obfuscated assemblies...");

                Type prebuildCommandExtType = ReflectionCache.GetType("Obfuz4HybridCLR.PrebuildCommandExt");
                if (prebuildCommandExtType == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} PrebuildCommandExt type not found.");
                    return;
                }

                MethodInfo generateMethod = ReflectionCache.GetMethod(
                    prebuildCommandExtType,
                    "GenerateMethodBridgeAndReversePInvokeWrapper",
                    BindingFlags.Public | BindingFlags.Static,
                    new Type[] { typeof(BuildTarget), typeof(string) });

                if (generateMethod == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} GenerateMethodBridgeAndReversePInvokeWrapper method not found.");
                    return;
                }

                generateMethod.Invoke(null, new object[] { target, obfuscatedHotUpdateDllPath });
                Debug.Log($"{DEBUG_FLAG} Method bridge and reverse P/Invoke wrapper generated successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate method bridge: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Generates AOT generic reference using obfuscated assemblies.
        /// This must be called after obfuscation.
        /// </summary>
        public static void GenerateAOTGenericReference(BuildTarget target, string obfuscatedHotUpdateDllPath)
        {
            if (!IsHybridCLRObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Obfuz4HybridCLR packages not found. Skipping AOT generic reference generation.");
                return;
            }

            try
            {
                Debug.Log($"{DEBUG_FLAG} Generating AOT generic reference using obfuscated assemblies...");

                Type prebuildCommandExtType = ReflectionCache.GetType("Obfuz4HybridCLR.PrebuildCommandExt");
                if (prebuildCommandExtType == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} PrebuildCommandExt type not found.");
                    return;
                }

                MethodInfo generateMethod = ReflectionCache.GetMethod(
                    prebuildCommandExtType,
                    "GenerateAOTGenericReference",
                    BindingFlags.Public | BindingFlags.Static,
                    new Type[] { typeof(BuildTarget), typeof(string) });

                if (generateMethod == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} GenerateAOTGenericReference method not found.");
                    return;
                }

                generateMethod.Invoke(null, new object[] { target, obfuscatedHotUpdateDllPath });
                Debug.Log($"{DEBUG_FLAG} AOT generic reference generated successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate AOT generic reference: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Gets the obfuscated hot update assembly output path for the given build target.
        /// </summary>
        public static string GetObfuscatedHotUpdateAssemblyOutputPath(BuildTarget target)
        {
            if (!IsHybridCLRObfuzAvailable())
            {
                Debug.LogWarning($"{DEBUG_FLAG} Obfuz4HybridCLR packages not found. Cannot get obfuscated output path.");
                return null;
            }

            try
            {
                Type prebuildCommandExtType = ReflectionCache.GetType("Obfuz4HybridCLR.PrebuildCommandExt");
                if (prebuildCommandExtType == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} PrebuildCommandExt type not found.");
                    return null;
                }

                MethodInfo getPathMethod = ReflectionCache.GetMethod(
                    prebuildCommandExtType,
                    "GetObfuscatedHotUpdateAssemblyOutputPath",
                    BindingFlags.Public | BindingFlags.Static,
                    new Type[] { typeof(BuildTarget) });

                if (getPathMethod == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} GetObfuscatedHotUpdateAssemblyOutputPath method not found.");
                    return null;
                }

                return (string)getPathMethod.Invoke(null, new object[] { target });
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to get obfuscated output path: {ex.Message}");
                return null;
            }
        }
    }
}