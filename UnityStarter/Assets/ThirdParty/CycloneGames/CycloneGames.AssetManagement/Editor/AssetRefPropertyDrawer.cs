#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Editor
{
    /// <summary>
    /// PropertyDrawer for <see cref="AssetRef{T}"/> and <see cref="AssetRef"/> (non-generic).
    /// Renders a standard ObjectField filtered by the generic type constraint.
    /// <para>
    /// On asset assignment: stores the GUID (stable across renames) and the asset path as location.
    /// On display: resolves the GUID to the current asset. If the asset was moved/renamed,
    /// the location is auto-healed to the new path.
    /// </para>
    /// </summary>
    [CustomPropertyDrawer(typeof(AssetRef<>), true)]
    [CustomPropertyDrawer(typeof(AssetRef))]
    public sealed class AssetRefPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_MissingIcon = EditorGUIUtility.TrIconContent(
            "console.warnicon.sml", "Referenced asset is missing.");

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

            if (!string.IsNullOrEmpty(guid))
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(currentPath))
                {
                    currentObj = AssetDatabase.LoadAssetAtPath(currentPath, assetType);
                    // Auto-heal: if asset was moved/renamed, update the stored location.
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

            // --- Draw ---
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            if (isBroken)
            {
                var iconRect = new Rect(position.xMax - 18, position.y + 1, 16, 16);
                fieldRect = new Rect(position.x, position.y, position.width - 20, position.height);
                GUI.Label(iconRect, s_MissingIcon);
            }

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, assetType, false);
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
