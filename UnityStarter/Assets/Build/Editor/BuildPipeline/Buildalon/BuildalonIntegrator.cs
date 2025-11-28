using System;
using System.Reflection;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class BuildalonIntegrator
    {
        private const string DEBUG_FLAG = "<color=cyan>[Buildalon]</color>";

        public static void SyncSolution()
        {
            try
            {
                Debug.Log($"{DEBUG_FLAG} Probing Buildalon for SyncSolution...");
                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                bool toolsFound = false;
                bool invoked = false;
                foreach (var asm in assemblies)
                {
                    var toolsType = asm.GetType("Buildalon.Editor.BuildPipeline.UnityPlayerBuildTools");
                    if (toolsType == null) continue;
                    toolsFound = true;
                    var syncMethod = toolsType.GetMethod("SyncSolution", BindingFlags.Public | BindingFlags.Static);
                    if (syncMethod != null)
                    {
                        syncMethod.Invoke(null, null);
                        Debug.Log($"{DEBUG_FLAG} Buildalon SyncSolution executed.");
                        invoked = true;
                    }
                    return;
                }
                if (!toolsFound)
                {
                    Debug.Log($"{DEBUG_FLAG} Buildalon not detected. Skipping SyncSolution.");
                }
                else if (!invoked)
                {
                    Debug.Log($"{DEBUG_FLAG} Buildalon detected but SyncSolution method not found.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Buildalon SyncSolution skipped: {ex.Message}");
            }
        }
    }
}