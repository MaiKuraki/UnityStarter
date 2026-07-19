#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// PropertyDrawer for <see cref="SceneRef"/>.
    /// Renders an ObjectField filtered to <see cref="SceneAsset"/> (editor-only type).
    /// Stores the Editor GUID and exposes the provider runtime scene location explicitly.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneRef))]
    public sealed class SceneRefPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_MissingIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "Referenced scene is missing.");
        private static readonly GUIContent s_EmptyLocationIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "A provider runtime scene location is required.");
        private static readonly GUIContent s_RuntimeLocationLabel = new GUIContent(
            "Runtime Location",
            "Exact provider key used to load the scene in Player builds.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight * 2f) + EditorGUIUtility.standardVerticalSpacing;
        }

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
            bool hasMixedValues = guidProp.hasMultipleDifferentValues ||
                                  locationProp.hasMultipleDifferentValues;

            if (!hasMixedValues && !string.IsNullOrEmpty(guid))
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(currentPath))
                {
                    currentObj = AssetDatabase.LoadAssetAtPath<SceneAsset>(currentPath);
                }
                else
                {
                    isBroken = true;
                }
            }

            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            var objectRect = new Rect(position.x, position.y, position.width, lineHeight);
            var locationRect = new Rect(
                position.x,
                position.y + lineHeight + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                lineHeight);
            bool hasEmptyLocation = !hasMixedValues &&
                                    !string.IsNullOrEmpty(guid) &&
                                    string.IsNullOrWhiteSpace(locationProp.stringValue);
            Rect fieldRect = objectRect;
            if (isBroken || hasEmptyLocation)
            {
                var iconRect = new Rect(objectRect.xMax - 18, objectRect.y + 1, 16, 16);
                fieldRect = new Rect(objectRect.x, objectRect.y, objectRect.width - 20, objectRect.height);
                GUI.Label(iconRect, isBroken ? s_MissingIcon : s_EmptyLocationIcon);
            }

            EditorGUI.BeginChangeCheck();
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = hasMixedValues;
            UnityEngine.Object newObj;
            try
            {
                newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, typeof(SceneAsset), false);
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (newObj != null)
                {
                    var path = AssetDatabase.GetAssetPath(newObj);
                    guidProp.stringValue = AssetDatabase.AssetPathToGUID(path);
                    locationProp.stringValue = string.Empty;
                }
                else
                {
                    guidProp.stringValue = string.Empty;
                    locationProp.stringValue = string.Empty;
                }
            }

            EditorGUI.PropertyField(locationRect, locationProp, s_RuntimeLocationLabel);

            EditorGUI.EndProperty();
        }
    }
}
#endif
