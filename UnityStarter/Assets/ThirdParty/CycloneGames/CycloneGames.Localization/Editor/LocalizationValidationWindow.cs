#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class LocalizationValidationWindow : EditorWindow
    {
        private const string WindowTitle = "Localization Validation";
        private static readonly GUIContent s_scanLabel = new GUIContent("Scan Localization Assets");
        private static readonly GUIContent s_clearLabel = new GUIContent("Clear");

        private readonly List<LocalizationValidationResult> _results = new List<LocalizationValidationResult>(128);
        private Vector2 _scroll;

        [MenuItem("Tools/CycloneGames/Localization/Validation/Window")]
        public static void Open()
        {
            var window = GetWindow<LocalizationValidationWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
        }

        private void OnGUI()
        {
            Rect toolbarRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, EditorStyles.toolbar);
            Rect scanRect = new Rect(toolbarRect.x, toolbarRect.y, 180f, toolbarRect.height);
            Rect clearRect = new Rect(scanRect.xMax + 4f, toolbarRect.y, 64f, toolbarRect.height);

            if (GUI.Button(scanRect, s_scanLabel, EditorStyles.toolbarButton))
                LocalizationValidator.ValidateProject(_results);

            if (GUI.Button(clearRect, s_clearLabel, EditorStyles.toolbarButton))
                _results.Clear();

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_results.Count == 0 ? "No issues found or scan not run." : _results.Count + " issue(s) found.");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _results.Count; i++)
            {
                var result = _results[i];
                float height = Mathf.Max(40f, EditorStyles.helpBox.CalcHeight(new GUIContent(result.Text), position.width - 32f));
                Rect rect = EditorGUILayout.GetControlRect(false, height);
                EditorGUI.HelpBox(rect, result.Text, result.Type);
                if (result.Context != null)
                {
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        Selection.activeObject = result.Context;
                        EditorGUIUtility.PingObject(result.Context);
                        Event.current.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
