#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(SpriteRendererSequenceRenderer))]
    public sealed class SpriteRendererSequenceRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LabelSpriteRenderer = new("Sprite Renderer");
        private static readonly GUIContent LabelRenderMode = new("Render Mode");
        private static readonly GUIContent LabelMaterialStrategy = new("Material Strategy");
        private static readonly GUIContent LabelSharedMaterialOverride = new("Shared Material Override");
        private static readonly GUIContent LabelFlipbookMaterial = new("Flipbook Shared Material");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty spriteRenderer = serializedObject.FindProperty("spriteRenderer");
            SerializedProperty renderMode = serializedObject.FindProperty("renderMode");
            SerializedProperty materialStrategy = serializedObject.FindProperty("materialStrategy");
            SerializedProperty sharedMaterialOverride = serializedObject.FindProperty("sharedMaterialOverride");
            SerializedProperty flipbookSharedMaterial = serializedObject.FindProperty("flipbookSharedMaterial");

            EditorGUILayout.PropertyField(spriteRenderer, LabelSpriteRenderer);
            EditorGUILayout.PropertyField(renderMode, LabelRenderMode);

            bool useFlipbookMode = renderMode != null && renderMode.enumValueIndex == (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
            if (useFlipbookMode)
            {
                EditorGUILayout.PropertyField(flipbookSharedMaterial, LabelFlipbookMaterial);
                EditorGUILayout.HelpBox("Flipbook shared-material mode keeps one shared Sprite material and updates UV remap through MaterialPropertyBlock. This is usually only worth it when many SpriteRenderers animate from one atlas and you want tighter material consistency.", MessageType.None);
                DrawCompatibilitySummary(
                    flipbookSharedMaterial.objectReferenceValue as Material,
                    "Sprites/FlipbookRemap",
                    true,
                    flipbookSharedMaterial,
                    ((SpriteRendererSequenceRenderer)target).gameObject.name,
                    renderMode,
                    false);

                DrawMaterialButtons(flipbookSharedMaterial, "Sprites/FlipbookRemap", ((SpriteRendererSequenceRenderer)target).gameObject.name, "Create Sprite Flipbook Material", "_SpriteFlipbookRemap.mat", "Select save path for the created SpriteRenderer flipbook material.");
                DrawFrameTextureWarnings((SpriteRendererSequenceRenderer)target, "SpriteRenderer flipbook shared-material mode requires all frames to come from one texture or atlas.");
            }
            else
            {
                EditorGUILayout.PropertyField(materialStrategy, LabelMaterialStrategy);
                if (materialStrategy != null && materialStrategy.enumValueIndex == (int)SpriteRendererSequenceRenderer.MaterialStrategy.SharedMaterialOverride)
                {
                    EditorGUILayout.PropertyField(sharedMaterialOverride, LabelSharedMaterialOverride);
                    EditorGUILayout.HelpBox("SharedMaterialOverride forces many SpriteRenderers onto the same shared material path. It is simpler than flipbook remap and is often enough when sprites already atlas well.", MessageType.None);

                    SpriteRenderer renderer = ((SpriteRendererSequenceRenderer)target).GetComponent<SpriteRenderer>();
                    string shaderName = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                        ? renderer.sharedMaterial.shader.name
                        : "Sprites/Default";

                    DrawCompatibilitySummary(
                        sharedMaterialOverride.objectReferenceValue as Material,
                        shaderName,
                        true,
                        sharedMaterialOverride,
                        ((SpriteRendererSequenceRenderer)target).gameObject.name,
                        renderMode,
                        true);

                    DrawMaterialButtons(sharedMaterialOverride, shaderName, ((SpriteRendererSequenceRenderer)target).gameObject.name, "Create SpriteRenderer Shared Material", "_SpriteRendererShared.mat", "Select save path for the created SpriteRenderer material.");
                }
                else
                {
                    EditorGUILayout.HelpBox("UseRendererDefault keeps the SpriteRenderer's existing shared material. This is usually the safest option when the scene already has a correct Sprite material setup.", MessageType.None);

                    SpriteRenderer renderer = ((SpriteRendererSequenceRenderer)target).GetComponent<SpriteRenderer>();
                    Material currentMaterial = renderer != null ? renderer.sharedMaterial : null;
                    string shaderName = currentMaterial != null && currentMaterial.shader != null
                        ? currentMaterial.shader.name
                        : "Sprites/Default";
                    DrawCompatibilitySummary(
                        currentMaterial,
                        shaderName,
                        false,
                        null,
                        ((SpriteRendererSequenceRenderer)target).gameObject.name,
                        renderMode,
                        true);
                }

                DrawFrameTextureWarnings((SpriteRendererSequenceRenderer)target, "SpriteRenderer batching benefits are best when frames share a texture/atlas and material state stays consistent.");
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
                        SpriteSequenceMaterialPickerWindow.Open("SpriteRenderer Material Picker", shaderName, candidates, material => AssignMaterial(materialProperty.propertyPath, material));
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
                Material created = SpriteSequenceRendererEditorUtility.CreateMaterialAsset(ownerName, expectedShaderName, "Create Correct Renderer Material", "_RendererAutoFix.mat", "Select save path for a material matching current renderer mode.");
                if (created != null)
                {
                    materialProperty.objectReferenceValue = created;
                }
            }

            if (summary.ShouldSuggestSwitchToFlipbookMode && renderMode != null && GUILayout.Button("Switch To Flipbook Mode", EditorStyles.miniButtonRight))
            {
                renderMode.enumValueIndex = (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
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

        private void DrawFrameTextureWarnings(SpriteRendererSequenceRenderer renderer, string message)
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