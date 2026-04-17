#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIAssetContextProvider))]
    public sealed class UIAssetContextProviderEditor : UnityEditor.Editor
    {
        private static readonly Color directColor = new Color(0.08f, 0.82f, 0.58f);
        private static readonly Color pathColor = new Color(0.98f, 0.62f, 0.08f);
        private static readonly Color assetRefColor = new Color(0.12f, 0.64f, 1.0f);

        private SerializedProperty sourceModeProp;
        private SerializedProperty contextAssetProp;
        private SerializedProperty contextAssetRefProp;
        private SerializedProperty contextAssetLocationProp;

        private GUIStyle _titleStyle;
        private GUIStyle _statusBoxStyle;
        private GUIStyle _sectionLabelStyle;
        private bool _stylesReady;

        private void OnEnable()
        {
            sourceModeProp = serializedObject.FindProperty("sourceMode");
            contextAssetProp = serializedObject.FindProperty("contextAsset");
            contextAssetRefProp = serializedObject.FindProperty("contextAssetRef");
            contextAssetLocationProp = serializedObject.FindProperty("contextAssetLocation");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            UIAssetContextProvider provider = (UIAssetContextProvider)target;

            EditorGUILayout.LabelField("UI Asset Context Provider", _titleStyle);
            EditorGUILayout.Space(4);

            DrawSourceModeSelector(provider);
            EditorGUILayout.Space(6);
            DrawSourceBody(provider);
            EditorGUILayout.Space(6);
            DrawSummary(provider);

            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };

            _statusBoxStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _sectionLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white }
            };
        }

        private void DrawSourceModeSelector(UIAssetContextProvider provider)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawModeStatusBox(provider.SourceMode);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Source Mode", _sectionLabelStyle);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            DrawModeButton("Direct Ref", UIAssetContextProvider.ContextSourceMode.DirectReference, directColor);
            DrawModeButton("Asset Ref", UIAssetContextProvider.ContextSourceMode.AssetReference, assetRefColor);
            DrawModeButton("Path", UIAssetContextProvider.ContextSourceMode.PathLocation, pathColor);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(GetModeDescription(provider.SourceMode), MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawModeStatusBox(UIAssetContextProvider.ContextSourceMode mode)
        {
            Color color = GetModeColor(mode);
            string label = mode switch
            {
                UIAssetContextProvider.ContextSourceMode.DirectReference => "[Direct Ref] Using direct ScriptableObject reference",
                UIAssetContextProvider.ContextSourceMode.AssetReference => "[Asset Ref] Using AssetManagement typed reference",
                UIAssetContextProvider.ContextSourceMode.PathLocation => "[Path] Using custom path location",
                _ => mode.ToString()
            };

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;
            EditorGUILayout.LabelField(label, _statusBoxStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawModeButton(string label, UIAssetContextProvider.ContextSourceMode mode, Color color)
        {
            bool isActive = sourceModeProp.enumValueIndex == (int)mode;
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? color : new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);

            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                sourceModeProp.enumValueIndex = (int)mode;
            }

            GUI.backgroundColor = old;
        }

        private void DrawSourceBody(UIAssetContextProvider provider)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            switch (provider.SourceMode)
            {
                case UIAssetContextProvider.ContextSourceMode.DirectReference:
                    EditorGUILayout.PropertyField(contextAssetProp, new GUIContent("Context Asset"));
                    if (contextAssetProp.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No direct context asset is assigned. UI loading will still work and simply use an empty default context.",
                            MessageType.Info);
                    }
                    break;

                case UIAssetContextProvider.ContextSourceMode.PathLocation:
                    EditorGUILayout.PropertyField(contextAssetLocationProp, new GUIContent("Custom Path"));
                    if (string.IsNullOrEmpty(contextAssetLocationProp.stringValue))
                    {
                        EditorGUILayout.HelpBox(
                            "Custom Path is empty. UI loading will still work and simply use an empty default context.",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "The context asset will be resolved through IAssetPackage.LoadAssetAsync<UIAssetContextAsset>(path).",
                            MessageType.None);
                    }
                    break;

                case UIAssetContextProvider.ContextSourceMode.AssetReference:
                    EditorGUILayout.PropertyField(contextAssetRefProp, new GUIContent("Context Asset Ref"), true);
                    if (!provider.HasAssignedAssetReference)
                    {
                        EditorGUILayout.HelpBox(
                            "Asset Ref has no location yet. UI loading will still work and simply use an empty default context.",
                            MessageType.Info);
                    }
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSummary(UIAssetContextProvider provider)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Resolved Summary", _sectionLabelStyle);
            EditorGUILayout.LabelField($"Effective Location: {GetDisplayValue(provider.EffectiveLocation)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Config Bucket: {GetDisplayValue(provider.ConfigBucket)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Config Tag: {GetDisplayValue(provider.ConfigTag)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Config Owner: {GetDisplayValue(provider.ConfigOwner)}", EditorStyles.miniLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Prefab Bucket: {GetDisplayValue(provider.PrefabBucket)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Prefab Tag: {GetDisplayValue(provider.PrefabTag)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Prefab Owner: {GetDisplayValue(provider.PrefabOwner)}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private static string GetModeDescription(UIAssetContextProvider.ContextSourceMode mode)
        {
            switch (mode)
            {
                case UIAssetContextProvider.ContextSourceMode.DirectReference:
                    return "Direct ScriptableObject reference. Best for tests, samples, or projects that do not use AssetManagement packages.";
                case UIAssetContextProvider.ContextSourceMode.PathLocation:
                    return "Custom address string. Best when your package pipeline resolves assets from a plain path or address.";
                case UIAssetContextProvider.ContextSourceMode.AssetReference:
                    return "Typed AssetRef<UIAssetContextAsset>. Best for Addressables / YooAsset style package-backed projects.";
                default:
                    return string.Empty;
            }
        }

        private static Color GetModeColor(UIAssetContextProvider.ContextSourceMode mode)
        {
            return mode switch
            {
                UIAssetContextProvider.ContextSourceMode.DirectReference => directColor,
                UIAssetContextProvider.ContextSourceMode.AssetReference => assetRefColor,
                UIAssetContextProvider.ContextSourceMode.PathLocation => pathColor,
                _ => Color.gray
            };
        }

        private static string GetDisplayValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "<none>" : value;
        }
    }
}
#endif
