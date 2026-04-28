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
                Type toolsType = ReflectionCache.GetType("Buildalon.Editor.BuildPipeline.UnityPlayerBuildTools");
                if (toolsType == null)
                {
                    Debug.Log($"{DEBUG_FLAG} Buildalon not detected. Skipping SyncSolution.");
                    return;
                }

                MethodInfo syncMethod = ReflectionCache.GetMethod(toolsType, "SyncSolution", BindingFlags.Public | BindingFlags.Static);
                if (syncMethod == null)
                {
                    Debug.Log($"{DEBUG_FLAG} Buildalon detected but SyncSolution method not found.");
                    return;
                }

                syncMethod.Invoke(null, null);
                Debug.Log($"{DEBUG_FLAG} Buildalon SyncSolution executed.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Buildalon SyncSolution skipped: {ex.Message}");
            }
        }
    }
}