#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logger.Editor
{
    /// <summary>
    /// Handles hyperlink clicks in the Unity Console for CLogger messages.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggerHyperLinkHandler
    {
        static LoggerHyperLinkHandler()
        {
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
        }

        private static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (!args.hyperLinkData.TryGetValue("path", out var filePath)) return;

            args.hyperLinkData.TryGetValue("line", out var lineStr);
            int.TryParse(lineStr, out int lineNumber);

            filePath = filePath.Replace('\\', '/');
            if (filePath.StartsWith("/")) filePath = filePath.Substring(1);

            var asset = AssetDatabase.LoadAssetAtPath<Object>(filePath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset, lineNumber);
                return;
            }

            string absolutePath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", filePath));

            if (System.IO.File.Exists(absolutePath))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(absolutePath, lineNumber);
            }
        }
    }
}
#endif
