using System;
using System.IO;
using CycloneGames.Logging;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Editor utility to open the Application.persistentDataPath in the system file explorer.
    /// Supports Windows, macOS, and Linux.
    /// </summary>
    public static class OpenPersistentDataPath
    {
        private static readonly LogChannel Log = UtilityEditorLog.Channel;

        [MenuItem("Tools/CycloneGames/Open Persistent Data Path")]
        public static void Open()
        {
            OpenDirectory(Application.persistentDataPath);
        }

        private static void OpenDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog(
                    "Open Persistent Data Path",
                    "Application.persistentDataPath is unavailable.",
                    "OK");
                return;
            }

            try
            {
                Directory.CreateDirectory(path);
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "The persistent data directory could not be opened.");
                EditorUtility.DisplayDialog(
                    "Open Persistent Data Path",
                    "The persistent data directory could not be opened. See the Console for details.",
                    "OK");
            }
        }
    }
}
