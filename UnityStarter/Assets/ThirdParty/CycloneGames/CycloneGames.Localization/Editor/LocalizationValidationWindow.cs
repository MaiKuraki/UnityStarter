#if UNITY_EDITOR
using System.Collections.Generic;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class LocalizationValidationWindow : EditorWindow
    {
        private const string WindowTitle = "Localization Validation";
        private static readonly GUIContent s_scanLabel = new GUIContent("Scan Localization Assets");
        private static readonly GUIContent s_clearLabel = new GUIContent("Clear");

        private readonly List<ValidationMessage> _messages = new List<ValidationMessage>(64);
        private readonly HashSet<string> _keySet = new HashSet<string>(128, System.StringComparer.Ordinal);
        private Vector2 _scroll;

        [MenuItem("Tools/CycloneGames/Localization/Validation")]
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
                Scan();

            if (GUI.Button(clearRect, s_clearLabel, EditorStyles.toolbarButton))
                _messages.Clear();

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_messages.Count == 0 ? "No issues found or scan not run." : _messages.Count + " issue(s) found.");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _messages.Count; i++)
            {
                var message = _messages[i];
                float height = Mathf.Max(40f, EditorStyles.helpBox.CalcHeight(new GUIContent(message.Text), position.width - 32f));
                Rect rect = EditorGUILayout.GetControlRect(false, height);
                EditorGUI.HelpBox(rect, message.Text, message.Type);
                if (message.Context != null)
                {
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        Selection.activeObject = message.Context;
                        EditorGUIUtility.PingObject(message.Context);
                        Event.current.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            _messages.Clear();
            ScanStringTables();
            ScanAssetTables();
            ScanLocales();
        }

        private void ScanStringTables()
        {
            string[] guids = AssetDatabase.FindAssets("t:StringTable");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null) continue;

                ValidateTableHeader(table.TableId, table.LocaleId.IsValid, "StringTable", table);
                ValidateEntryKeys(table, "entries", "StringTable");
            }
        }

        private void ScanAssetTables()
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetTable");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                if (table == null) continue;

                ValidateTableHeader(table.TableId, table.LocaleId.IsValid, "AssetTable", table);
                ValidateEntryKeys(table, "entries", "AssetTable");
            }
        }

        private void ScanLocales()
        {
            string[] guids = AssetDatabase.FindAssets("t:Locale");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
                if (locale == null) continue;

                if (!locale.Id.IsValid)
                    Add("Locale has empty localeCode: " + path, MessageType.Error, locale);

                ValidateLocaleFallbacks(locale, path);
            }
        }

        private void ValidateTableHeader(string tableId, bool localeValid, string typeName, Object context)
        {
            string path = AssetDatabase.GetAssetPath(context);
            if (string.IsNullOrEmpty(tableId))
                Add(typeName + " has empty tableId: " + path, MessageType.Error, context);
            if (!localeValid)
                Add(typeName + " has empty localeCode: " + path, MessageType.Error, context);
        }

        private void ValidateEntryKeys(Object table, string propertyName, string typeName)
        {
            _keySet.Clear();
            var serialized = new SerializedObject(table);
            var entries = serialized.FindProperty(propertyName);
            if (entries == null || !entries.isArray) return;

            string path = AssetDatabase.GetAssetPath(table);
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var keyProperty = entry.FindPropertyRelative("Key");
                string key = keyProperty != null ? keyProperty.stringValue : null;

                if (string.IsNullOrEmpty(key))
                {
                    Add(typeName + " has empty key at index " + i + ": " + path, MessageType.Error, table);
                    continue;
                }

                if (!_keySet.Add(key))
                    Add(typeName + " has duplicate key '" + key + "': " + path, MessageType.Error, table);
            }
        }

        private void ValidateLocaleFallbacks(Locale locale, string path)
        {
            for (int i = 0; i < locale.FallbackCount; i++)
            {
                var fallback = locale.GetFallback(i);
                if (fallback == null) continue;
                if (ReferenceEquals(locale, fallback))
                    Add("Locale fallback references itself: " + path, MessageType.Error, locale);
            }

            var visited = new HashSet<string>(8, System.StringComparer.Ordinal);
            if (HasFallbackCycle(locale, visited, new HashSet<string>(8, System.StringComparer.Ordinal)))
                Add("Locale fallback chain has a cycle: " + path, MessageType.Error, locale);
        }

        private static bool HasFallbackCycle(Locale locale, HashSet<string> visited, HashSet<string> stack)
        {
            if (locale == null || !locale.Id.IsValid) return false;

            string code = locale.Id.Code;
            if (stack.Contains(code)) return true;
            if (!visited.Add(code)) return false;

            stack.Add(code);
            for (int i = 0; i < locale.FallbackCount; i++)
            {
                if (HasFallbackCycle(locale.GetFallback(i), visited, stack))
                    return true;
            }

            stack.Remove(code);
            return false;
        }

        private void Add(string text, MessageType type, Object context)
        {
            _messages.Add(new ValidationMessage(text, type, context));
        }

        private readonly struct ValidationMessage
        {
            public readonly string Text;
            public readonly MessageType Type;
            public readonly Object Context;

            public ValidationMessage(string text, MessageType type, Object context)
            {
                Text = text;
                Type = type;
                Context = context;
            }
        }
    }
}
#endif
