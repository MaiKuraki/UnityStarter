#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// PropertyDrawer for <see cref="SceneRef"/>.
    /// Renders an ObjectField filtered to <see cref="SceneAsset"/> (editor-only type).
    /// Stores the GUID and asset path as location, with auto-heal on asset move/rename.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneRef))]
    public sealed class SceneRefPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_MissingIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "Referenced scene is missing.");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var guidProp = property.FindPropertyRelative("m_GUID");
            var locationProp = property.FindPropertyRelative("m_Location");

            if (guidProp == null || locationProp == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Invalid SceneRef layout"));
                return;
            }

            string guid = guidProp.stringValue;
            UnityEngine.Object currentObj = null;
            bool isBroken = false;

            if (!string.IsNullOrEmpty(guid))
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(currentPath))
                {
                    currentObj = AssetDatabase.LoadAssetAtPath<SceneAsset>(currentPath);
                    if (currentObj != null && locationProp.stringValue != currentPath)
                    {
                        locationProp.stringValue = currentPath;
                        property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                else
                {
                    isBroken = true;
                }
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            if (isBroken)
            {
                var iconRect = new Rect(position.xMax - 18, position.y + 1, 16, 16);
                fieldRect = new Rect(position.x, position.y, position.width - 20, position.height);
                GUI.Label(iconRect, s_MissingIcon);
            }

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (newObj != null)
                {
                    var path = AssetDatabase.GetAssetPath(newObj);
                    guidProp.stringValue = AssetDatabase.AssetPathToGUID(path);
                    locationProp.stringValue = path;
                }
                else
                {
                    guidProp.stringValue = string.Empty;
                    locationProp.stringValue = string.Empty;
                }
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
