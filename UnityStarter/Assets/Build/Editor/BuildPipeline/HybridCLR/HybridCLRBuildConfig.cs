using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditorInternal;
#endif

namespace Build.Pipeline.Editor
{
    [CreateAssetMenu(menuName = "CycloneGames/Build/HybridCLR Build Config")]
    public class HybridCLRBuildConfig : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("Drag Assembly Definition Assets (.asmdef) here that need to be hot updated.")]
        public List<AssemblyDefinitionAsset> hotUpdateAssemblies;

        [Tooltip("The directory within Assets to copy the hot update DLLs to. (e.g., 'Assets/HotUpdateDLL')")]
        public string hotUpdateDllOutputDirectory = "Assets/HotUpdateDLL";

        /// <summary>
        /// Extracts assembly names from the assigned .asmdef files.
        /// </summary>
        public List<string> GetHotUpdateAssemblyNames()
        {
            List<string> names = new List<string>();
            if (hotUpdateAssemblies == null) return names;

            foreach (var asm in hotUpdateAssemblies)
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

        [Serializable]
        private class AsmDefJson
        {
            public string name;
        }
#endif
    }
}