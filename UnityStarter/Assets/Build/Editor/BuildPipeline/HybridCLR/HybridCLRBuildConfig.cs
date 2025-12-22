using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace Build.Pipeline.Editor
{
    [CreateAssetMenu(menuName = "CycloneGames/Build/HybridCLR Build Config")]
    public class HybridCLRBuildConfig : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Drag Assembly Definition Assets (.asmdef) here that need to be hot updated.")]
        public List<AssemblyDefinitionAsset> hotUpdateAssemblies;

        [Tooltip("Drag Assembly Definition Assets (.asmdef) here for cheat/debug DLLs (optional).")]
        public List<AssemblyDefinitionAsset> cheatAssemblies;

        [Tooltip("The directory within Assets to copy the hot update DLLs to. Drag a folder from your project here.")]
        public DefaultAsset hotUpdateDllOutputDirectory;

        [Tooltip("The directory within Assets to copy cheat DLLs to. Drag a folder from your project here (optional).")]
        public DefaultAsset cheatDllOutputDirectory;

        [Tooltip("Enable Obfuz code obfuscation for hot update assemblies.")]
        public bool enableObfuz = false;

        [Tooltip("The directory within Assets to copy AOT DLLs to. Drag a folder from your project here. Used for AOT metadata generation.")]
        public DefaultAsset aotDllOutputDirectory;

        /// <summary>
        /// Extracts assembly names from the assigned .asmdef files.
        /// </summary>
        public List<string> GetHotUpdateAssemblyNames()
        {
            return GetAssemblyNamesFromList(hotUpdateAssemblies);
        }

        /// <summary>
        /// Extracts assembly names from cheat assemblies list.
        /// </summary>
        public List<string> GetCheatAssemblyNames()
        {
            return GetAssemblyNamesFromList(cheatAssemblies);
        }

        /// <summary>
        /// Gets all hot update assembly names (both HotUpdate and Cheat).
        /// </summary>
        public List<string> GetAllHotUpdateAssemblyNames()
        {
            var allNames = new List<string>();
            allNames.AddRange(GetHotUpdateAssemblyNames());
            allNames.AddRange(GetCheatAssemblyNames());
            return allNames;
        }

        private List<string> GetAssemblyNamesFromList(List<AssemblyDefinitionAsset> assemblies)
        {
            List<string> names = new List<string>();
            if (assemblies == null) return names;

            foreach (var asm in assemblies)
            {
                if (asm == null) continue;

                try
                {
                    var data = JsonUtility.FromJson<AsmDefJson>(asm.text);
                    if (!string.IsNullOrEmpty(data.name))
                    {
                        names.Add(data.name);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HybridCLRBuildConfig] Failed to parse asmdef: {asm.name}. Error: {e.Message}");
                }
            }
            return names;
        }

        /// <summary>
        /// Gets the hot update DLL output directory path as a string.
        /// </summary>
        public string GetHotUpdateDllOutputDirectoryPath()
        {
            if (hotUpdateDllOutputDirectory != null)
            {
                return AssetDatabase.GetAssetPath(hotUpdateDllOutputDirectory);
            }
            return "Assets/HotUpdateDLL"; // Default fallback
        }

        /// <summary>
        /// Gets the Cheat DLL output directory path as a string.
        /// </summary>
        public string GetCheatDllOutputDirectoryPath()
        {
            if (cheatDllOutputDirectory != null)
            {
                return AssetDatabase.GetAssetPath(cheatDllOutputDirectory);
            }
            return null; // Optional, return null if not set
        }

        /// <summary>
        /// Gets the AOT DLL output directory path as a string.
        /// </summary>
        public string GetAOTDllOutputDirectoryPath()
        {
            if (aotDllOutputDirectory != null)
            {
                return AssetDatabase.GetAssetPath(aotDllOutputDirectory);
            }
            return null; // Optional, return null if not set
        }

        [Serializable]
        private class AsmDefJson
        {
            public string name;
        }
#endif
    }
}