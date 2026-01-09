using System.Diagnostics;
using System.IO;
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
        [MenuItem("Tools/CycloneGames/Open Persistent Data Path")]
        public static void Open()
        {
            OpenDirectory(Application.persistentDataPath);
        }

        private static void OpenDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

#if UNITY_EDITOR_WIN
            path = path.Replace('/', '\\');
            Process.Start("explorer.exe", path);
#elif UNITY_EDITOR_OSX
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false
            });
#else // Linux
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false
            });
#endif
        }
    }
}