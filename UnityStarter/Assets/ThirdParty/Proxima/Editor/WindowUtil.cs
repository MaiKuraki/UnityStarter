using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Proxima.Editor
{
    internal static class WindowUtil
    {
        private static readonly string _projectMeta = "55a7fa332ffadb5489464312015b3ea8";

        private static string _version;

        private static Texture2D _flexalonIcon;
        private static Texture2D _proximaIcon;

        public static void CenterOnEditor(EditorWindow window)
        {
#if UNITY_2020_1_OR_NEWER
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = window.position;
            float w = (main.width - pos.width) * 0.5f;
            float h = (main.height - pos.height) * 0.5f;
            pos.x = main.x + w;
            pos.y = main.y + h;
            window.position = pos;
#endif
        }

        public static string GetVersion()
        {
            if (_version == null)
            {
                var version = AssetDatabase.GUIDToAssetPath(_projectMeta);
                var lines = File.ReadAllText(version);
                var rx = new Regex("\"version\": \"(.*?)\"");
                _version = rx.Match(lines).Groups[1].Value;
            }

            return _version;
        }
    }
}