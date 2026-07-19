#if UNITY_EDITOR
using System;

using UnityEditor;
using UnityEngine;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// PropertyDrawer for <see cref="AssetRef{T}"/> and <see cref="AssetRef"/> (non-generic).
    /// Renders an ObjectField filtered by the generic type constraint and an explicit provider runtime location.
    /// <para>
    /// The GUID is an Editor authoring aid. The runtime location remains explicit because Resources paths,
    /// Addressables addresses, and YooAsset addresses are not interchangeable.
    /// </para>
    /// </summary>
    [CustomPropertyDrawer(typeof(AssetRef<>), true)]
    [CustomPropertyDrawer(typeof(AssetRef))]
    public sealed class AssetRefPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_MissingIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "Referenced asset is missing.");
        private static readonly GUIContent s_EmptyLocationIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "A provider runtime location is required.");
        private static readonly GUIContent s_RuntimeLocationLabel = new GUIContent(
            "Runtime Location",
            "Exact provider key used in Player builds. Enter a Resources-relative path, Addressables address, or YooAsset address as required by the selected provider.");

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
                EditorGUI.LabelField(position, label, new GUIContent("Invalid AssetRef layout"));
                return;
            }

            // --- Determine target asset type from generic argument ---
            var assetType = typeof(UnityEngine.Object);
            if (fieldInfo != null)
            {
                var ft = fieldInfo.FieldType;
                if (ft.IsArray)
                    ft = ft.GetElementType();
                else if (ft.IsGenericType && ft.GetGenericTypeDefinition() != typeof(AssetRef<>))
                    ft = ft.IsGenericType ? ft.GetGenericArguments()[0] : ft; // List<AssetRef<T>> etc.

                if (ft != null && ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(AssetRef<>))
                    assetType = ft.GetGenericArguments()[0];
            }

            // --- Resolve current object from GUID ---
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
                    currentObj = AssetDatabase.LoadAssetAtPath(currentPath, assetType);
                }
                else
                {
                    isBroken = true;
                }
            }

            // --- Draw ---
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
                newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, assetType, false);
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
                    // A newly selected object has no provider-neutral runtime key. Clearing the previous value
                    // prevents a location for another asset from surviving the reassignment silently.
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
