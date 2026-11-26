using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Draws the fields used by the selected icon source, keeps derived serialized fields visible, and resolves
    /// asset keys from Unity object references.
    /// </summary>
    [CustomEditor(typeof(Runtime.InputDeviceIconSet), true)]
    internal sealed class InputDeviceIconSetEditor : UnityEditor.Editor
    {
        private static readonly string[] OwnedFields =
        {
            "iconSource", "keyboardMouseIcon", "gamepadIcon", "touchIcon",
            "layoutIcons", "controlIcons"
        };

        private static readonly GUIContent LayoutLabel = new GUIContent("Layout",
            "Exact Unity device layout, for example XInputController or DualShockGamepad.");
        private static readonly GUIContent ControlPathLabel = new GUIContent("Control Path",
            "Exact control path, for example <Gamepad>/buttonSouth.");
        private static readonly GUIContent DirectLabel = new GUIContent("Direct Icon", "Sprite stored in this asset.");

        private SerializedProperty _source;
        private SerializedProperty _layoutIcons;
        private SerializedProperty _controlIcons;
        private readonly HashSet<string> _owned = new HashSet<string>(OwnedFields);
        private bool _showLayout = true;
        private bool _showControl = true;

        private void OnEnable()
        {
            _source = serializedObject.FindProperty("iconSource");
            _layoutIcons = serializedObject.FindProperty("layoutIcons");
            _controlIcons = serializedObject.FindProperty("controlIcons");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_source.hasMultipleDifferentValues)
            {
                EditorUiUtility.Warning("Selected icon sets use different sources. Choose one to edit them together.");
                EditorGUILayout.PropertyField(_source);
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            bool byKey = _source.enumValueIndex == (int)Runtime.InputPromptIconSource.ProviderKey;
            EditorGUILayout.PropertyField(_source, new GUIContent("Icon Source",
                "Direct stores sprites in the asset. Provider Key stores keys resolved by the product."));
            DrawDeviceIcons(byKey);
            EditorUiUtility.Info(byKey
                ? "Provider Key: icons are resolved once through IInputPromptIconProvider. Set the provider at the composition root."
                : "Direct: icons are stored in this asset. No provider or loading step is required.");
            DrawEntries(_layoutIcons, "Layout Prompts", "Matched before the coarse device kind.",
                "layout", LayoutLabel, byKey, ref _showLayout);
            DrawEntries(_controlIcons, "Control Prompts", "Matched before both the layout and the coarse device kind.",
                "controlPath", ControlPathLabel, byKey, ref _showControl);

            if (byKey)
            {
                ValidateKeys();
                ValidateAssets();
            }

            DrawDerivedFields();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDeviceIcons(bool byKey)
        {
            EditorUiUtility.Separator();
            EditorUiUtility.BeginCard();
            EditorUiUtility.SectionHeader("Device Icons");
            EditorUiUtility.Hint(byKey
                ? "Fallback keys for the coarse device kind. Plain custom keys."
                : "Fallback sprites for the coarse device kind.");

            DrawDeviceIcon("keyboardMouseIcon", "Keyboard Mouse", byKey);
            DrawDeviceIcon("gamepadIcon", "Gamepad", byKey);
            DrawDeviceIcon("touchIcon", "Touch", byKey);

            EditorUiUtility.EndCard();
        }

        private void DrawDeviceIcon(string field, string label, bool byKey)
        {
            SerializedProperty property = serializedObject.FindProperty(field);
            if (byKey) DrawKeyFields(property, label);
            else EditorGUILayout.PropertyField(property.FindPropertyRelative("direct"), new GUIContent(label + " Icon"));
        }

        private void DrawEntries(SerializedProperty array, string title, string hint, string pathField,
            GUIContent pathLabel, bool byKey, ref bool expanded)
        {
            EditorUiUtility.Separator();
            bool open = EditorUiUtility.FoldoutSection(expanded, title, array.arraySize);
            expanded = open;
            if (open)
            {
                EditorUiUtility.Hint(hint);
                EditorGUI.indentLevel++;

                for (int i = 0; i < array.arraySize; i++)
                {
                    SerializedProperty entry = array.GetArrayElementAtIndex(i);
                    EditorUiUtility.BeginCard();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Entry " + i, EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                    {
                        array.DeleteArrayElementAtIndex(i);
                        EditorGUILayout.EndHorizontal();
                        EditorUiUtility.EndCard();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative(pathField), pathLabel);
                    SerializedProperty icon = entry.FindPropertyRelative("icon");
                    if (byKey) DrawKeyFields(icon, "Prompt");
                    else EditorGUILayout.PropertyField(icon.FindPropertyRelative("direct"), DirectLabel);
                    EditorUiUtility.EndCard();
                }

                if (GUILayout.Button("Add Entry")) array.arraySize++;
                EditorGUI.indentLevel--;
            }

            EditorUiUtility.EndFoldoutSection();
        }

        private void DrawKeyFields(SerializedProperty icon, string label)
        {
            SerializedProperty source = icon.FindPropertyRelative("keySource");
            EditorGUILayout.PropertyField(source, new GUIContent("Key Source",
                "CustomKey: plain string. AssetPath/AssetGuid: resolved from a Unity asset reference."));
            var mode = (Runtime.InputPromptKeySource)source.enumValueIndex;

            if (mode == Runtime.InputPromptKeySource.CustomKey)
            {
                EditorGUILayout.PropertyField(icon.FindPropertyRelative("key"), new GUIContent(label + " Key"));
                return;
            }

            SerializedProperty path = icon.FindPropertyRelative("assetPath");
            SerializedProperty guid = icon.FindPropertyRelative("assetGuid");
            string currentGuid = guid.stringValue;
            string currentPath = string.IsNullOrEmpty(currentGuid)
                ? path.stringValue
                : AssetDatabase.GUIDToAssetPath(currentGuid);
            if (!string.IsNullOrEmpty(currentPath) && currentPath != path.stringValue)
            {
                path.stringValue = currentPath;
            }
            Object current = string.IsNullOrEmpty(currentPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Object>(currentPath);

            EditorGUI.BeginChangeCheck();
            Object selected = EditorGUILayout.ObjectField(
                new GUIContent("Asset", "Drag a project asset; the path and GUID are filled automatically."),
                current, typeof(Object), false);
            if (EditorGUI.EndChangeCheck())
            {
                string selectedPath = selected == null ? string.Empty : AssetDatabase.GetAssetPath(selected);
                string selectedGuid = string.Empty;
                long localId = 0L;
                if (selected != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(selected, out selectedGuid, out localId);
                    if (string.IsNullOrEmpty(selectedPath)) selectedPath = AssetDatabase.GUIDToAssetPath(selectedGuid);
                }

                path.stringValue = selectedPath;
                guid.stringValue = selectedGuid;
                icon.FindPropertyRelative("localFileId").longValue = localId;
                icon.FindPropertyRelative("subAssetName").stringValue = selected == null ? string.Empty : selected.name;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("Path (read-only)",
                    "Project-relative path kept in sync from the asset GUID. Use it to build the loading plan."),
                    path.stringValue);
                EditorGUILayout.TextField(new GUIContent("Sub Asset (read-only)",
                    "Sub-object name when the asset holds several sprites, for example one cell of a multiple-sprite texture."),
                    icon.FindPropertyRelative("subAssetName").stringValue);
                EditorGUILayout.LongField(new GUIContent("Local File Id (read-only)",
                    "Unity sub-object identifier. Together with the GUID it addresses one sprite inside an asset."),
                    icon.FindPropertyRelative("localFileId").longValue);
            }

            if (string.IsNullOrEmpty(path.stringValue)) EditorUiUtility.Hint("No asset assigned.");
            else if (string.IsNullOrEmpty(guid.stringValue)) EditorUiUtility.Warning("Could not resolve a GUID for " + path.stringValue);
            else EditorUiUtility.Hint("GUID: " + guid.stringValue);
        }

        private void ValidateAssets()
        {
            int missing = 0;
            missing += CountMissingInRef(serializedObject.FindProperty("keyboardMouseIcon"));
            missing += CountMissingInRef(serializedObject.FindProperty("gamepadIcon"));
            missing += CountMissingInRef(serializedObject.FindProperty("touchIcon"));
            missing += CountMissingInArray(_layoutIcons);
            missing += CountMissingInArray(_controlIcons);
            if (missing > 0)
            {
                EditorUiUtility.Warning("Missing assets: " + missing + ". Reassign or clear them.");
            }
        }

        private static int CountMissingInRef(SerializedProperty refProperty)
        {
            return refProperty == null ? 0 : IsMissing(refProperty) ? 1 : 0;
        }

        private static int CountMissingInArray(SerializedProperty array)
        {
            int missing = 0;
            for (int i = 0; i < array.arraySize; i++)
            {
                if (IsMissing(array.GetArrayElementAtIndex(i).FindPropertyRelative("icon"))) missing++;
            }

            return missing;
        }

        private static bool IsMissing(SerializedProperty icon)
        {
            if ((Runtime.InputPromptKeySource)icon.FindPropertyRelative("keySource").enumValueIndex !=
                Runtime.InputPromptKeySource.Asset) return false;
            string guid = icon.FindPropertyRelative("assetGuid").stringValue;
            return !string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));
        }

        private void ValidateKeys()
        {
            var seen = new HashSet<string>();
            int empty = 0;
            int duplicate = 0;
            CollectKeys(serializedObject.FindProperty("keyboardMouseIconKey"), seen, ref empty, ref duplicate);
            CollectKeys(serializedObject.FindProperty("gamepadIconKey"), seen, ref empty, ref duplicate);
            CollectKeys(serializedObject.FindProperty("touchIconKey"), seen, ref empty, ref duplicate);
            Collect(_layoutIcons, seen, ref empty, ref duplicate);
            Collect(_controlIcons, seen, ref empty, ref duplicate);
            if (duplicate > 0)
            {
                EditorUiUtility.Warning("Duplicate keys: " + duplicate + ".");
            }
            else if (empty > 0)
            {
                EditorUiUtility.Hint("Unset keys: " + empty + ".");
            }
        }

        private static void CollectKeys(SerializedProperty property, HashSet<string> seen, ref int empty, ref int duplicate)
        {
            if (property == null) return;
            Add(property.stringValue, seen, ref empty, ref duplicate);
        }

        private static void Collect(SerializedProperty array, HashSet<string> seen, ref int empty, ref int duplicate)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty icon = array.GetArrayElementAtIndex(i).FindPropertyRelative("icon");
                var mode = (Runtime.InputPromptKeySource)icon.FindPropertyRelative("keySource").enumValueIndex;
                string key = mode == Runtime.InputPromptKeySource.Asset
                    ? icon.FindPropertyRelative("assetGuid").stringValue + ":" +
                      icon.FindPropertyRelative("localFileId").longValue
                    : "custom:" + icon.FindPropertyRelative("key").stringValue;
                Add(key, seen, ref empty, ref duplicate);
            }
        }

        private static void Add(string key, HashSet<string> seen, ref int empty, ref int duplicate)
        {
            if (string.IsNullOrEmpty(key)) { empty++; return; }
            if (!seen.Add(key)) duplicate++;
        }

        /// <summary>Draws serialized fields declared by derived classes below the base sections.</summary>
        private void DrawDerivedFields()
        {
            SerializedProperty property = serializedObject.GetIterator();
            if (!property.NextVisible(true)) return;

            bool drewHeader = false;
            do
            {
                if (property.depth != 0 || property.name == "m_Script" || _owned.Contains(property.name)) continue;
                if (!drewHeader)
                {
                    EditorUiUtility.Separator();
                    EditorUiUtility.BeginCard();
                    EditorUiUtility.SectionHeader("Additional Fields");
                    EditorUiUtility.Hint("Serialized fields declared by this derived class.");
                    drewHeader = true;
                }

                EditorGUILayout.PropertyField(property, true);
            }
            while (property.NextVisible(true));

            if (drewHeader) EditorUiUtility.EndCard();
        }
    }
}
