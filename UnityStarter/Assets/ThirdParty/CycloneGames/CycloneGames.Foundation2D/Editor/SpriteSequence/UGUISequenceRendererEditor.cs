#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(UGUISequenceRenderer))]
    public sealed class UGUISequenceRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LabelImage = new("Image");
        private static readonly GUIContent LabelCanvasGroup = new("Canvas Group");
        private static readonly GUIContent LabelRenderMode = new("Render Mode");
        private static readonly GUIContent LabelFlipbookMaterial = new("Flipbook Shared Material");
        private static readonly GUIContent LabelFlipbookEffect = new("Flipbook UV Mesh Effect");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty image = serializedObject.FindProperty("image");
            SerializedProperty canvasGroup = serializedObject.FindProperty("canvasGroup");
            SerializedProperty renderMode = serializedObject.FindProperty("renderMode");
            SerializedProperty flipbookSharedMaterial = serializedObject.FindProperty("flipbookSharedMaterial");
            SerializedProperty flipbookUvMeshEffect = serializedObject.FindProperty("flipbookUvMeshEffect");

            EditorGUILayout.PropertyField(image, LabelImage);
            EditorGUILayout.PropertyField(canvasGroup, LabelCanvasGroup);
            EditorGUILayout.PropertyField(renderMode, LabelRenderMode);

            bool useFlipbookMode = renderMode != null && renderMode.enumValueIndex == (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
            if (useFlipbookMode)
            {
                EditorGUILayout.PropertyField(flipbookSharedMaterial, LabelFlipbookMaterial);
                EditorGUILayout.PropertyField(flipbookUvMeshEffect, LabelFlipbookEffect);
                DrawCompatibilitySummary(
                    flipbookSharedMaterial.objectReferenceValue as Material,
                    "UI/FlipbookRemap",
                    true,
                    flipbookSharedMaterial,
                    ((UGUISequenceRenderer)target).gameObject.name,
                    renderMode,
                    false);

                if (flipbookUvMeshEffect != null && flipbookUvMeshEffect.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Missing FlipbookUVMeshEffect. This component writes the UV rect remap data used by the shared-material flipbook path.", MessageType.Info);
                    if (GUILayout.Button("Add FlipbookUVMeshEffect"))
                    {
                        UGUISequenceRenderer renderer = (UGUISequenceRenderer)target;
                        if (renderer.GetComponent<FlipbookUVMeshEffect>() == null)
                        {
                            Undo.AddComponent<FlipbookUVMeshEffect>(renderer.gameObject);
                        }

                        serializedObject.Update();
                        SerializedProperty updatedEffect = serializedObject.FindProperty("flipbookUvMeshEffect");
                        if (updatedEffect != null)
                        {
                            updatedEffect.objectReferenceValue = renderer.GetComponent<FlipbookUVMeshEffect>();
                        }
                    }
                }

                DrawMaterialButtons(flipbookSharedMaterial, "UI/FlipbookRemap", ((UGUISequenceRenderer)target).gameObject.name, "Create UI Flipbook Material", "_UIFlipbookRemap.mat", "Select save path for the created UGUI flipbook material.");
                DrawFrameTextureWarnings((UGUISequenceRenderer)target, "UGUI shared-material flipbook batching requires all frames to come from one texture or atlas.");
            }
            else
            {
                Image currentImage = ((UGUISequenceRenderer)target).GetComponent<Image>();
                Material currentMaterial = currentImage != null ? currentImage.material : null;
                string shaderName = currentMaterial != null && currentMaterial.shader != null
                    ? currentMaterial.shader.name
                    : "UI/Default";
                DrawCompatibilitySummary(
                    currentMaterial,
                    shaderName,
                    false,
                    null,
                    ((UGUISequenceRenderer)target).gameObject.name,
                    renderMode,
                    true);
                EditorGUILayout.HelpBox("SpriteSwap mode updates Image.sprite directly. Use this when flipbook UV remap is unnecessary or when frames are not from one atlas.", MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMaterialButtons(SerializedProperty materialProperty, string shaderName, string ownerName, string dialogTitle, string suffix, string prompt)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Assign Existing Material", EditorStyles.miniButtonLeft))
            {
                Material current = materialProperty.objectReferenceValue as Material;
                Material found = SpriteSequenceRendererEditorUtility.FindBestMaterial((Component)target, shaderName, ownerName, current, out string message);
                if (found != null)
                {
                    materialProperty.objectReferenceValue = found;
                }
                else
                {
                    List<SpriteSequenceRendererEditorUtility.MaterialCandidate> candidates = SpriteSequenceRendererEditorUtility.GetMaterialCandidates((Component)target, shaderName, ownerName);
                    if (candidates.Count > 1)
                    {
                        SpriteSequenceMaterialPickerWindow.Open("UGUI Material Picker", shaderName, candidates, material => AssignMaterial(materialProperty.propertyPath, material));
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Auto Assign Skipped", message ?? $"No compatible material using shader {shaderName} was found.", "OK");
                    }
                }
            }

            if (GUILayout.Button("Create And Assign Material", EditorStyles.miniButtonRight))
            {
                Material created = SpriteSequenceRendererEditorUtility.CreateMaterialAsset(ownerName, shaderName, dialogTitle, suffix, prompt);
                if (created != null)
                {
                    materialProperty.objectReferenceValue = created;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCompatibilitySummary(Material material, string expectedShaderName, bool requiresExpectedShader, SerializedProperty materialProperty, string ownerName, SerializedProperty renderMode, bool suggestSwitchToFlipbook)
        {
            var summary = SpriteSequenceRendererEditorUtility.BuildCompatibilitySummary((Component)target, material, expectedShaderName, suggestSwitchToFlipbook);
            MessageType messageType = summary.MeetsSharedBatchingPrerequisites ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(SpriteSequenceRendererEditorUtility.FormatCompatibilitySummary(summary, requiresExpectedShader), messageType);

            EditorGUILayout.BeginHorizontal();
            if (summary.ShouldSuggestFilterFrames && GUILayout.Button("Filter To First Atlas Frames", EditorStyles.miniButtonLeft))
            {
                SpriteSequenceRendererEditorUtility.KeepOnlyFirstTextureFrames((Component)target);
            }

            if (requiresExpectedShader && materialProperty != null && summary.ShouldSuggestCreateCorrectMaterial && GUILayout.Button("Create Correct Material", EditorStyles.miniButtonMid))
            {
                Material created = SpriteSequenceRendererEditorUtility.CreateMaterialAsset(ownerName, expectedShaderName, "Create UI Flipbook Material", "_UIFlipbookRemap.mat", "Select save path for the created UGUI flipbook material.");
                if (created != null)
                {
                    materialProperty.objectReferenceValue = created;
                }
            }

            if (summary.ShouldSuggestSwitchToFlipbookMode && renderMode != null && GUILayout.Button("Switch To Flipbook Mode", EditorStyles.miniButtonRight))
            {
                renderMode.enumValueIndex = (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void AssignMaterial(string propertyPath, Material material)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null)
            {
                return;
            }

            Undo.RecordObject(target, "Assign Renderer Material");
            so.Update();
            property.objectReferenceValue = material;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void DrawFrameTextureWarnings(UGUISequenceRenderer renderer, string message)
        {
            if (SpriteSequenceRendererEditorUtility.AllFramesShareTexture(renderer))
            {
                return;
            }

            int textureCount = SpriteSequenceRendererEditorUtility.CountDistinctFrameTextures(renderer);
            EditorGUILayout.HelpBox($"Frames currently span {textureCount} textures. {message}", MessageType.Warning);
            if (GUILayout.Button("Keep First Texture Frames Only"))
            {
                SpriteSequenceRendererEditorUtility.KeepOnlyFirstTextureFrames(renderer);
            }
        }
    }
}
#endif