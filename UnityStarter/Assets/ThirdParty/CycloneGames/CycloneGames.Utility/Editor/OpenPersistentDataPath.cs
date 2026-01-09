using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
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

            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                path = path.Replace('/', '\\');
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
            {
                System.Diagnostics.Process.Start("open", path);
            }
            else // Linux and others
            {
                System.Diagnostics.Process.Start("xdg-open", path);
            }
        }
    }
}