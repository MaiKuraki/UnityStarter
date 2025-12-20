using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
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
                ForceReloadObfuzSettings();

                string expectedPath = GetEncryptionVMOutputPath();
                if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
                {
                    Type vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
                    if (vmType != null)
                    {
                        Debug.Log($"{DEBUG_FLAG} Encryption VM file already exists and is compiled at: {expectedPath}");
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Encryption VM file exists but class is not compiled. Forcing reimport and compilation...");
                        AssetDatabase.ImportAsset(expectedPath, ImportAssetOptions.ForceUpdate);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        CompilationPipeline.RequestScriptCompilation();
                    }
                }

                Debug.Log($"{DEBUG_FLAG} Generating encryption VM...");

                // Try to use reflection to call the method directly instead of menu item
                // This is more reliable than ExecuteMenuItem which can fail if ObfuzSettings is not initialized
                Type obfuzMenuType = ReflectionCache.GetType("Obfuz.Unity.ObfuzMenu");
                if (obfuzMenuType != null)
                {
                    MethodInfo generateMethod = ReflectionCache.GetMethod(obfuzMenuType, "GenerateEncryptionVM", BindingFlags.Public | BindingFlags.Static);
                    if (generateMethod != null)
                    {
                        try
                        {
                            generateMethod.Invoke(null, null);
                            if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
                            {
                                Debug.Log($"{DEBUG_FLAG} Encryption VM generated successfully at: {expectedPath}");
                                AssetDatabase.ImportAsset(expectedPath, ImportAssetOptions.ForceUpdate);
                                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                                System.Threading.Thread.Sleep(100);
                                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                                Debug.Log($"{DEBUG_FLAG} Encryption VM file imported and compilation requested.");
                                return;
                            }
                            else
                            {
                                Debug.LogWarning($"{DEBUG_FLAG} GenerateEncryptionVM method executed but file not found at expected path: {expectedPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"{DEBUG_FLAG} Failed to call GenerateEncryptionVM via reflection: {ex.Message}. Trying menu item...");
                        }
                    }
                }

                if (!EditorApplication.ExecuteMenuItem("Obfuz/GenerateEncryptionVM"))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to execute Obfuz/GenerateEncryptionVM menu item.");
                }
                else
                {
                    if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
                    {
                        Debug.Log($"{DEBUG_FLAG} Encryption VM generated successfully at: {expectedPath}");

                        AssetDatabase.ImportAsset(expectedPath, ImportAssetOptions.ForceUpdate);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                        System.Threading.Thread.Sleep(100);

                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();

                        Debug.Log($"{DEBUG_FLAG} Encryption VM file imported and compilation requested.");
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Menu item executed but file not found at expected path: {expectedPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate encryption VM: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"{DEBUG_FLAG} Build will continue without obfuscation. Please generate Encryption VM manually via Obfuz menu.");
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

                Type obfuzMenuType = ReflectionCache.GetType("Obfuz.Unity.ObfuzMenu");
                if (obfuzMenuType != null)
                {
                    MethodInfo saveSecretMethod = ReflectionCache.GetMethod(obfuzMenuType, "SaveSecretFile", BindingFlags.Public | BindingFlags.Static);
                    if (saveSecretMethod != null)
                    {
                        try
                        {
                            saveSecretMethod.Invoke(null, null);
                            Debug.Log($"{DEBUG_FLAG} Secret key file generated successfully via reflection.");
                            AssetDatabase.Refresh();
                            return;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"{DEBUG_FLAG} Failed to call SaveSecretFile via reflection: {ex.Message}. Trying menu item...");
                        }
                    }
                }

                if (!EditorApplication.ExecuteMenuItem("Obfuz/GenerateSecretKeyFile"))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to execute Obfuz/GenerateSecretKeyFile menu item.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Secret key file generated successfully via menu item.");
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate secret key file: {ex.Message}\n{ex.StackTrace}");
                Debug.LogWarning($"{DEBUG_FLAG} Build will continue. Please generate Secret Key file manually via Obfuz menu if needed.");
            }
        }

        /// <summary>
        /// Forces ObfuzSettings to be fully initialized, creating all required sub-objects if they are null.
        /// This must be called before any other Obfuz operations to prevent null reference exceptions.
        /// </summary>
        public static void ForceInitializeObfuzSettings()
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

                PropertyInfo instanceProperty = ReflectionCache.GetProperty(obfuzSettingsType, "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    return;
                }

                object obfuzSettingsInstance = instanceProperty.GetValue(null);
                if (obfuzSettingsInstance == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} ObfuzSettings.Instance is null after loading. This may indicate a corrupted settings file.");
                    return;
                }

                FieldInfo assemblySettingsField = ReflectionCache.GetField(obfuzSettingsType, "assemblySettings", BindingFlags.Public | BindingFlags.Instance);
                if (assemblySettingsField != null)
                {
                    object assemblySettings = assemblySettingsField.GetValue(obfuzSettingsInstance);
                    if (assemblySettings == null)
                    {
                        Debug.Log($"{DEBUG_FLAG} assemblySettings is null, creating new instance...");
                        Type assemblySettingsType = ReflectionCache.GetType("Obfuz.Settings.AssemblySettings");
                        if (assemblySettingsType != null)
                        {
                            try
                            {
                                object newInstance = Activator.CreateInstance(assemblySettingsType);
                                assemblySettingsField.SetValue(obfuzSettingsInstance, newInstance);
                                Debug.Log($"{DEBUG_FLAG} Created new AssemblySettings instance.");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"{DEBUG_FLAG} Failed to create AssemblySettings instance: {ex.Message}");
                            }
                        }
                    }
                }

                // Initialize buildPipelineSettings if null
                FieldInfo buildPipelineSettingsField = ReflectionCache.GetField(obfuzSettingsType, "buildPipelineSettings", BindingFlags.Public | BindingFlags.Instance);
                if (buildPipelineSettingsField != null)
                {
                    object buildPipelineSettings = buildPipelineSettingsField.GetValue(obfuzSettingsInstance);
                    if (buildPipelineSettings == null)
                    {
                        Debug.Log($"{DEBUG_FLAG} buildPipelineSettings is null, creating new instance...");
                        Type buildPipelineSettingsType = ReflectionCache.GetType("Obfuz.Settings.BuildPipelineSettings");
                        if (buildPipelineSettingsType != null)
                        {
                            try
                            {
                                object newInstance = Activator.CreateInstance(buildPipelineSettingsType);
                                buildPipelineSettingsField.SetValue(obfuzSettingsInstance, newInstance);

                                FieldInfo linkXmlProcessCallbackOrderField = ReflectionCache.GetField(buildPipelineSettingsType, "linkXmlProcessCallbackOrder", BindingFlags.Public | BindingFlags.Instance);
                                if (linkXmlProcessCallbackOrderField != null)
                                {
                                    linkXmlProcessCallbackOrderField.SetValue(newInstance, 10000);
                                }

                                FieldInfo obfuscationProcessCallbackOrderField = ReflectionCache.GetField(buildPipelineSettingsType, "obfuscationProcessCallbackOrder", BindingFlags.Public | BindingFlags.Instance);
                                if (obfuscationProcessCallbackOrderField != null)
                                {
                                    obfuscationProcessCallbackOrderField.SetValue(newInstance, 10000);
                                }

                                Debug.Log($"{DEBUG_FLAG} Created new BuildPipelineSettings instance with default callback orders.");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"{DEBUG_FLAG} Failed to create BuildPipelineSettings instance: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        EnsureBuildPipelineSettingsInitialized();
                    }
                }

                SaveObfuzSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to force initialize ObfuzSettings: {ex.Message}\n{ex.StackTrace}");
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
                    Debug.LogError($"{DEBUG_FLAG} ObfuzSettings.assemblySettings is null. This should have been initialized by ForceInitializeObfuzSettings().");
                    ForceInitializeObfuzSettings();
                    assemblySettings = assemblySettingsField.GetValue(obfuzSettingsInstance);
                    if (assemblySettings == null)
                    {
                        Debug.LogError($"{DEBUG_FLAG} Failed to initialize assemblySettings. Cannot configure settings.");
                        return;
                    }
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
        /// Verifies that the Encryption VM class is compiled and available.
        /// </summary>
        public static bool VerifyEncryptionVMCompiled()
        {
            if (!IsBaseObfuzAvailable())
            {
                return false;
            }

            try
            {
                Type vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
                if (vmType != null)
                {
                    Debug.Log($"{DEBUG_FLAG} Encryption VM class is compiled and available.");
                    return true;
                }
                else
                {
                    string expectedPath = GetEncryptionVMOutputPath();
                    if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
                    {
                        Debug.LogError($"{DEBUG_FLAG} Encryption VM file exists at {expectedPath} but class is not compiled. The file may need to be reimported or Unity needs to recompile scripts.");
                    }
                    else
                    {
                        Debug.LogError($"{DEBUG_FLAG} Encryption VM file does not exist. Please generate it via Obfuz > GenerateEncryptionVM menu.");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to verify Encryption VM compilation: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensures Encryption VM is generated and compiled, waiting for compilation if necessary.
        /// This should be called before BuildPlayer to ensure the class is available during obfuscation.
        /// </summary>
        /// <param name="maxWaitSeconds">Maximum safety timeout in seconds (default: 300 seconds / 5 minutes as safety net)</param>
        public static void EnsureEncryptionVMGeneratedAndCompiled(int maxWaitSeconds = 300)
        {
            if (!IsBaseObfuzAvailable())
            {
                return;
            }

            string expectedPath = GetEncryptionVMOutputPath();
            bool fileExistedBefore = !string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath);

            Type vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
            if (vmType != null)
            {
                Debug.Log($"{DEBUG_FLAG} Encryption VM is already compiled.");
                return;
            }

            if (!fileExistedBefore)
            {
                Debug.Log($"{DEBUG_FLAG} Encryption VM file does not exist. Generating...");
                GenerateEncryptionVM();
            }

            expectedPath = GetEncryptionVMOutputPath();
            if (string.IsNullOrEmpty(expectedPath) || !File.Exists(expectedPath))
            {
                Debug.LogError($"{DEBUG_FLAG} Encryption VM file was not generated. Expected path: {expectedPath}");
                throw new InvalidOperationException($"Encryption VM file was not generated. Please generate it manually via Obfuz > GenerateEncryptionVM menu.");
            }

            vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
            if (vmType != null)
            {
                Debug.Log($"{DEBUG_FLAG} Encryption VM is now compiled.");
                return;
            }

            Debug.Log($"{DEBUG_FLAG} Encryption VM file exists but class is not compiled. Forcing reimport and waiting for compilation...");

            AssetDatabase.ImportAsset(expectedPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            System.Threading.Thread.Sleep(300);

            CompilationPipeline.RequestScriptCompilation();

            System.Threading.Thread.Sleep(300);

            bool compilationComplete = WaitForCompilationIntelligent(maxWaitSeconds, expectedPath);

            if (!compilationComplete)
            {
                vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
                if (vmType == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} Encryption VM compilation did not complete within {maxWaitSeconds} seconds.");
                    Debug.LogError($"{DEBUG_FLAG} File exists at: {expectedPath}");
                    Debug.LogError($"{DEBUG_FLAG} Please generate Encryption VM manually via Obfuz > GenerateEncryptionVM menu and wait for compilation to complete before building.");
                    throw new InvalidOperationException($"Encryption VM class is not compiled after {maxWaitSeconds} seconds. Please generate it manually and wait for compilation to complete.");
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Encryption VM compilation completed (verified in final check).");
                }
            }
        }

        private static bool WaitForCompilationIntelligent(int maxWaitSeconds, string expectedPath)
        {
            int maxWaitMs = maxWaitSeconds * 1000;
            int checkIntervalMs = 200;
            int totalChecks = maxWaitMs / checkIntervalMs;

            bool compilationStarted = false;
            bool compilationComplete = false;
            int idleWaitCount = 0;
            const int maxIdleWait = 5;
            int compilationStartWaitCount = 0;
            const int maxCompilationStartWait = 15;

            DateTime startTime = DateTime.Now;
            Debug.Log($"{DEBUG_FLAG} Waiting for Encryption VM compilation (intelligent wait, safety timeout: {maxWaitSeconds}s)...");

            for (int i = 0; i < totalChecks; i++)
            {
                bool isCompiling = EditorApplication.isCompiling;

                if (isCompiling)
                {
                    if (!compilationStarted)
                    {
                        compilationStarted = true;
                        double elapsed = (DateTime.Now - startTime).TotalSeconds;
                        Debug.Log($"{DEBUG_FLAG} Compilation started after {elapsed:F2} seconds. Waiting for completion...");
                    }
                    idleWaitCount = 0;
                    System.Threading.Thread.Sleep(checkIntervalMs);
                    continue;
                }

                if (compilationStarted)
                {
                    Type vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
                    if (vmType != null)
                    {
                        compilationComplete = true;
                        double elapsed = (DateTime.Now - startTime).TotalSeconds;
                        Debug.Log($"{DEBUG_FLAG} Encryption VM compilation completed successfully after {elapsed:F2} seconds.");
                        break;
                    }

                    idleWaitCount++;
                    if (idleWaitCount < maxIdleWait)
                    {
                        System.Threading.Thread.Sleep(checkIntervalMs);
                        continue;
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Compilation finished but class is not yet available. This may indicate a compilation issue.");
                        break;
                    }
                }
                else
                {
                    compilationStartWaitCount++;
                    if (compilationStartWaitCount < maxCompilationStartWait)
                    {
                        System.Threading.Thread.Sleep(checkIntervalMs);
                        continue;
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Compilation hasn't started after {compilationStartWaitCount * checkIntervalMs / 1000.0f} seconds. Requesting compilation again...");
                        CompilationPipeline.RequestScriptCompilation();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        compilationStartWaitCount = 0;
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }
                }
            }

            if (!compilationComplete)
            {
                Type vmType = ReflectionCache.GetType("Obfuz.EncryptionVM.GeneratedEncryptionVirtualMachine");
                if (vmType != null)
                {
                    double elapsed = (DateTime.Now - startTime).TotalSeconds;
                    Debug.Log($"{DEBUG_FLAG} Encryption VM compilation completed (verified in final check after {elapsed:F2} seconds).");
                    return true;
                }
            }

            return compilationComplete;
        }

        /// <summary>
        /// Gets the encryption VM output path from ObfuzSettings.
        /// </summary>
        private static string GetEncryptionVMOutputPath()
        {
            try
            {
                Type obfuzSettingsType = ReflectionCache.GetType("Obfuz.Settings.ObfuzSettings");
                if (obfuzSettingsType == null)
                {
                    return null;
                }

                PropertyInfo instanceProperty = ReflectionCache.GetProperty(obfuzSettingsType, "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    return null;
                }

                object obfuzSettingsInstance = instanceProperty.GetValue(null);
                if (obfuzSettingsInstance == null)
                {
                    return null;
                }

                FieldInfo encryptionVMSettingsField = ReflectionCache.GetField(obfuzSettingsType, "encryptionVMSettings", BindingFlags.Public | BindingFlags.Instance);
                if (encryptionVMSettingsField == null)
                {
                    return null;
                }

                object encryptionVMSettings = encryptionVMSettingsField.GetValue(obfuzSettingsInstance);
                if (encryptionVMSettings == null)
                {
                    return null;
                }

                Type encryptionVMSettingsType = ReflectionCache.GetType("Obfuz.Settings.EncryptionVMSettings");
                if (encryptionVMSettingsType == null)
                {
                    return null;
                }

                FieldInfo codeOutputPathField = ReflectionCache.GetField(encryptionVMSettingsType, "codeOutputPath", BindingFlags.Public | BindingFlags.Instance);
                if (codeOutputPathField == null)
                {
                    return null;
                }

                return codeOutputPathField.GetValue(encryptionVMSettings) as string;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to get encryption VM output path: {ex.Message}");
                return null;
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
            ConfigureObfuzSettings();

            GenerateEncryptionVM();
            GenerateSecretKeyFile();

            string vmPath = GetEncryptionVMOutputPath();
            if (string.IsNullOrEmpty(vmPath) || !File.Exists(vmPath))
            {
                Debug.LogWarning($"{DEBUG_FLAG} Encryption VM file not found at: {vmPath ?? "null"}");
                Debug.LogWarning($"{DEBUG_FLAG} Obfuscation may fail. Please generate Encryption VM manually via Obfuz > GenerateEncryptionVM menu.");
            }
        }

        /// <summary>
        /// Ensures Obfuz build pipeline settings are properly initialized.
        /// This prevents LinkXmlProcess callbackOrder null reference issues.
        /// Note: This is a lightweight check. For full initialization, use ForceInitializeObfuzSettings().
        /// </summary>
        public static void EnsureBuildPipelineSettingsInitialized()
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

                PropertyInfo instanceProperty = ReflectionCache.GetProperty(obfuzSettingsType, "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    return;
                }

                object obfuzSettingsInstance = instanceProperty.GetValue(null);
                if (obfuzSettingsInstance == null)
                {
                    ForceInitializeObfuzSettings();
                    return;
                }

                FieldInfo buildPipelineSettingsField = ReflectionCache.GetField(obfuzSettingsType, "buildPipelineSettings", BindingFlags.Public | BindingFlags.Instance);
                if (buildPipelineSettingsField == null)
                {
                    return;
                }

                object buildPipelineSettings = buildPipelineSettingsField.GetValue(obfuzSettingsInstance);
                if (buildPipelineSettings == null)
                {
                    ForceInitializeObfuzSettings();
                    return;
                }

                Type buildPipelineSettingsType = ReflectionCache.GetType("Obfuz.Settings.BuildPipelineSettings");
                if (buildPipelineSettingsType != null)
                {
                    FieldInfo linkXmlProcessCallbackOrderField = ReflectionCache.GetField(buildPipelineSettingsType, "linkXmlProcessCallbackOrder", BindingFlags.Public | BindingFlags.Instance);
                    if (linkXmlProcessCallbackOrderField != null)
                    {
                        object currentValue = linkXmlProcessCallbackOrderField.GetValue(buildPipelineSettings);
                        if (currentValue == null || (currentValue is int && (int)currentValue == 0))
                        {
                            linkXmlProcessCallbackOrderField.SetValue(buildPipelineSettings, 10000);
                        }
                    }

                    FieldInfo obfuscationProcessCallbackOrderField = ReflectionCache.GetField(buildPipelineSettingsType, "obfuscationProcessCallbackOrder", BindingFlags.Public | BindingFlags.Instance);
                    if (obfuscationProcessCallbackOrderField != null)
                    {
                        object currentValue = obfuscationProcessCallbackOrderField.GetValue(buildPipelineSettings);
                        if (currentValue == null || (currentValue is int && (int)currentValue == 0))
                        {
                            obfuscationProcessCallbackOrderField.SetValue(buildPipelineSettings, 10000);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to ensure build pipeline settings initialization: {ex.Message}");
            }
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