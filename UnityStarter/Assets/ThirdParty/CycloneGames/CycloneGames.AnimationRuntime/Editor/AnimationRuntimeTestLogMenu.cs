using System.IO;
using CycloneGames.Logging;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Menu entries for the disk-based test log written by <c>LoggedTestBase</c>.
    /// </summary>
    internal static class AnimationRuntimeTestLogMenu
    {
        private const string LogRelativePath = "Temp/AnimationRuntimeTestLog.md";

        private static string LogPath
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", LogRelativePath)); }
        }

        [MenuItem("Tools/CycloneGames/AnimationRuntime/Test Log/Clear", priority = 200)]
        private static void ClearLog()
        {
            string path = LogPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            AnimationRuntimeEditorLog.Channel.Info("[AnimationRuntime] test log cleared: " + path);
        }

        [MenuItem("Tools/CycloneGames/AnimationRuntime/Test Log/Reveal", priority = 201)]
        private static void RevealLog()
        {
            string path = LogPath;
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            AnimationRuntimeEditorLog.Channel.Warning(
                "[AnimationRuntime] no test log yet at " + path + ". Run the EditMode/PlayMode suites first.");
        }
    }
}
