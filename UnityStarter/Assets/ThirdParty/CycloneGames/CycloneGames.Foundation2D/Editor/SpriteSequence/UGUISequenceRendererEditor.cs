#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(UGUISequenceRenderer))]
    [CanEditMultipleObjects]
    public sealed class UGUISequenceRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ModuleTitle = new("UGUI Sequence Renderer");
        private static readonly GUIContent ModuleSubtitle = new("Sprite swapping and shared-material UV remap authoring for Unity UI.");
        private static readonly GUIContent SectionConfiguration = new("Renderer Configuration");
        private static readonly GUIContent SectionDiagnostics = new("Compatibility Diagnostics");
        private static readonly GUIContent BadgeRendering = new("UGUI");
        private static readonly GUIContent BadgeReady = new("READY");
        private static readonly GUIContent BadgeReview = new("REVIEW");
        private static readonly GUIContent LabelImage = new("Image");
        private static readonly GUIContent LabelRenderMode = new("Render Mode");
        private static readonly GUIContent LabelFlipbookMaterial = new("Flipbook Shared Material");
        private static readonly GUIContent LabelFlipbookEffect = new("Flipbook UV Mesh Effect");

        private static readonly string[] ExplicitlyDrawnProperties =
        {
            "image",
            "renderMode",
            "flipbookSharedMaterial",
            "flipbookUvMeshEffect",
        };

        private SerializedProperty _image;
        private SerializedProperty _renderMode;
        private SerializedProperty _flipbookSharedMaterial;
        private SerializedProperty _flipbookUvMeshEffect;

        private UGUISequenceRenderer _rendererTarget;
        private Image _fallbackImage;
        private SpriteSequenceController _controller;
        private SpriteSequenceRendererEditorUtility.CompatibilitySummary _compatibilitySummary;
        private string _compatibilityText;
        private int _compatibilityFingerprint;
        private bool _compatibilityValid;
        private bool _serializedPropertiesValid;
        private string _serializedPropertiesError;
        private bool _foldConfiguration = true;
        private bool _foldDiagnostics = true;

        private void OnEnable()
        {
            _image = serializedObject.FindProperty("image");
            _renderMode = serializedObject.FindProperty("renderMode");
            _flipbookSharedMaterial = serializedObject.FindProperty("flipbookSharedMaterial");
            _flipbookUvMeshEffect = serializedObject.FindProperty("flipbookUvMeshEffect");
            _serializedPropertiesValid = Foundation2DInspectorUi.ValidateRequiredProperties(
                serializedObject,
                nameof(UGUISequenceRendererEditor),
                ExplicitlyDrawnProperties,
                out _serializedPropertiesError);
            _rendererTarget = target as UGUISequenceRenderer;
            _fallbackImage = _rendererTarget != null ? _rendererTarget.GetComponent<Image>() : null;
            _controller = _rendererTarget != null ? _rendererTarget.GetComponent<SpriteSequenceController>() : null;
            Undo.undoRedoPerformed += OnUndoRedo;
            InvalidateCompatibility();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (!_serializedPropertiesValid)
            {
                Foundation2DInspectorUi.DrawInvalidSerializedPropertyState(
                    serializedObject,
                    ModuleTitle,
                    ModuleSubtitle,
                    _serializedPropertiesError);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            Foundation2DInspectorUi.DrawModuleHeader(ModuleTitle, ModuleSubtitle);
            if (serializedObject.isEditingMultipleObjects)
            {
                Foundation2DInspectorUi.DrawMultiObjectActionNotice();
            }

            if (Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldConfiguration,
                    SectionConfiguration,
                    BadgeRendering,
                    Foundation2DInspectorUi.BadgeTone.Neutral))
            {
                using (Foundation2DInspectorUi.BeginCard())
                {
                    EditorGUILayout.PropertyField(_image, LabelImage, false);
                    EditorGUILayout.PropertyField(_renderMode, LabelRenderMode, false);

                    if (_renderMode.hasMultipleDifferentValues)
                    {
                        EditorGUILayout.HelpBox("Selected renderers use different render modes. Set a common mode to edit mode-specific settings.", MessageType.Info);
                    }
                    else if (_renderMode.enumValueIndex == (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial)
                    {
                        EditorGUILayout.PropertyField(_flipbookSharedMaterial, LabelFlipbookMaterial, false);
                        EditorGUILayout.PropertyField(_flipbookUvMeshEffect, LabelFlipbookEffect, false);
                        EditorGUILayout.HelpBox("Flipbook mode reuses one Image geometry and remaps UVs. Frames must satisfy the runtime texture and geometry contract.", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("SpriteSwap mode updates Image.sprite directly and does not require shared UV geometry.", MessageType.None);
                    }
                }
            }

            if (!serializedObject.isEditingMultipleObjects && !_renderMode.hasMultipleDifferentValues)
            {
                GetCurrentDiagnosticContext(out Material material, out string shaderName, out bool requiresShader, out SerializedProperty materialProperty, out bool suggestFlipbook);
                EnsureCompatibility(material, shaderName, requiresShader, suggestFlipbook);
                bool authoringReady = _compatibilitySummary.MeetsSharedBatchingPrerequisites && IsFlipbookEffectReadyForCurrentMode();
                Foundation2DInspectorUi.BadgeTone tone = authoringReady
                    ? Foundation2DInspectorUi.BadgeTone.Good
                    : Foundation2DInspectorUi.BadgeTone.Warning;
                GUIContent badge = authoringReady ? BadgeReady : BadgeReview;
                if (Foundation2DInspectorUi.DrawSectionHeader(ref _foldDiagnostics, SectionDiagnostics, badge, tone))
                {
                    using (Foundation2DInspectorUi.BeginCard())
                    {
                        DrawCompatibilitySummary(materialProperty, shaderName, requiresShader, suggestFlipbook);
                        DrawFlipbookEffectDiagnostics();
                        DrawMaterialButtons(materialProperty, shaderName);
                    }
                }
            }

            Foundation2DInspectorUi.DrawRemainingProperties(serializedObject, ExplicitlyDrawnProperties);
            if (serializedObject.ApplyModifiedProperties())
            {
                InvalidateCompatibility();
                Repaint();
            }
        }

        private void DrawCompatibilitySummary(
            SerializedProperty materialProperty,
            string expectedShaderName,
            bool requiresExpectedShader,
            bool suggestSwitchToFlipbook)
        {
            MessageType messageType = _compatibilitySummary.MeetsSharedBatchingPrerequisites
                ? MessageType.Info
                : MessageType.Warning;
            EditorGUILayout.HelpBox(_compatibilityText, messageType);

            int actionCount = 1;
            if (_compatibilitySummary.ShouldSuggestFilterFrames)
            {
                actionCount++;
            }

            if (_compatibilitySummary.ShouldSuggestSwitchToFlipbookMode)
            {
                actionCount++;
            }

            using (Foundation2DInspectorUi.BeginActionLayout(actionCount, 150f))
            {
                if (GUILayout.Button("Refresh Diagnostics"))
                {
                    RefreshCompatibility(materialProperty != null ? materialProperty.objectReferenceValue as Material : GetBoundMaterial(), expectedShaderName, requiresExpectedShader, suggestSwitchToFlipbook);
                }

                if (_compatibilitySummary.ShouldSuggestFilterFrames &&
                    GUILayout.Button("Keep First-Texture Frames"))
                {
                    if (SpriteSequenceRendererEditorUtility.KeepOnlyFirstTextureFrames(_rendererTarget))
                    {
                        InvalidateCompatibility();
                    }
                }

                if (_compatibilitySummary.ShouldSuggestSwitchToFlipbookMode &&
                    GUILayout.Button("Switch To Flipbook"))
                {
                    _renderMode.enumValueIndex = (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
                    InvalidateCompatibility();
                }
            }

            if (requiresExpectedShader && materialProperty != null && _compatibilitySummary.ShouldSuggestCreateCorrectMaterial)
            {
                if (GUILayout.Button("Create Compatible Material"))
                {
                    Material created = SpriteSequenceRendererEditorUtility.CreateMaterialAsset(
                        _rendererTarget.gameObject.name,
                        expectedShaderName,
                        "Create Compatible UI Material",
                        "_UICompatible.mat",
                        "Select a project path for a material matching the current UGUI renderer mode.");
                    if (created != null)
                    {
                        materialProperty.objectReferenceValue = created;
                        InvalidateCompatibility();
                    }
                }
            }
        }

        private void DrawMaterialButtons(SerializedProperty materialProperty, string shaderName)
        {
            if (materialProperty == null)
            {
                return;
            }

            EditorGUILayout.Space(2f);
            using (Foundation2DInspectorUi.BeginActionLayout(2, 135f))
            {
                if (GUILayout.Button("Find Existing Material"))
                {
                    List<SpriteSequenceRendererEditorUtility.MaterialCandidate> candidates =
                        SpriteSequenceRendererEditorUtility.GetMaterialCandidates(_rendererTarget, shaderName, _rendererTarget.gameObject.name);
                    Material current = materialProperty.objectReferenceValue as Material;
                    Material found = SpriteSequenceRendererEditorUtility.SelectBestMaterial(candidates, current, shaderName, out string message);
                    if (found != null)
                    {
                        materialProperty.objectReferenceValue = found;
                        InvalidateCompatibility();
                    }
                    else if (candidates.Count > 1)
                    {
                        string propertyPath = materialProperty.propertyPath;
                        SpriteSequenceMaterialPickerWindow.Open(
                            "UGUI Material Picker",
                            shaderName,
                            candidates,
                            material => AssignMaterial(propertyPath, material));
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Material Search", message, "OK");
                    }
                }

                if (GUILayout.Button("Create Material"))
                {
                    Material created = SpriteSequenceRendererEditorUtility.CreateMaterialAsset(
                        _rendererTarget.gameObject.name,
                        shaderName,
                        "Create UGUI Material",
                        "_UGUI.mat",
                        "Select a project path for the created UGUI material.");
                    if (created != null)
                    {
                        materialProperty.objectReferenceValue = created;
                        InvalidateCompatibility();
                    }
                }
            }
        }

        private void AddFlipbookEffect()
        {
            if (_rendererTarget == null || serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            Image targetImage = _image.objectReferenceValue as Image;
            targetImage ??= _fallbackImage;
            if (targetImage == null)
            {
                EditorUtility.DisplayDialog(
                    "Image Required",
                    "Assign an Image before adding FlipbookUVMeshEffect. The effect must be owned by the same GameObject as the Image it modifies.",
                    "OK");
                return;
            }

            FlipbookUVMeshEffect effect = targetImage.GetComponent<FlipbookUVMeshEffect>();
            if (effect == null)
            {
                effect = Undo.AddComponent<FlipbookUVMeshEffect>(targetImage.gameObject);
            }

            _flipbookUvMeshEffect.objectReferenceValue = effect;
            InvalidateCompatibility();
        }

        private void DrawFlipbookEffectDiagnostics()
        {
            if (_renderMode.enumValueIndex != (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial)
            {
                return;
            }

            FlipbookUVMeshEffect effect = _flipbookUvMeshEffect.objectReferenceValue as FlipbookUVMeshEffect;
            Image image = _image.objectReferenceValue as Image;
            image ??= _fallbackImage;
            if (image == null)
            {
                EditorGUILayout.HelpBox("No Image is assigned or available on this GameObject.", MessageType.Error);
                return;
            }

            if (effect == null)
            {
                EditorGUILayout.HelpBox("FlipbookUVMeshEffect is missing. It must write the base and target UV rectangles into the Image geometry.", MessageType.Warning);
                if (GUILayout.Button("Add Flipbook UV Effect"))
                {
                    AddFlipbookEffect();
                }
                return;
            }

            if (effect.gameObject != image.gameObject)
            {
                EditorGUILayout.HelpBox("The FlipbookUVMeshEffect is bound to a different Graphic. Assign an effect on the same GameObject as this Image.", MessageType.Error);
                if (GUILayout.Button("Use Effect On Image"))
                {
                    AddFlipbookEffect();
                }
                return;
            }

            if (!effect.isActiveAndEnabled)
            {
                if (!effect.enabled)
                {
                    EditorGUILayout.HelpBox("FlipbookUVMeshEffect is disabled. Runtime flipbook rendering will fall back until the effect is enabled.", MessageType.Warning);
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        if (GUILayout.Button("Enable Flipbook UV Effect"))
                        {
                            SpriteSequenceRendererEditorUtility.EnableFlipbookEffect(effect);
                            InvalidateCompatibility();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("The Image GameObject is inactive in the current hierarchy. FlipbookUVMeshEffect cannot run until the GameObject becomes active.", MessageType.Warning);
                }
                return;
            }

            Canvas canvas = image.canvas;
            if (canvas == null)
            {
                EditorGUILayout.HelpBox("The Image is not connected to a Canvas. Canvas shader channels cannot be validated.", MessageType.Warning);
                return;
            }

            if (effect.IsReadyFor(image))
            {
                EditorGUILayout.HelpBox("FlipbookUVMeshEffect is bound to this Image and the Canvas provides TexCoord1 and TexCoord2.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox("The Canvas must provide TexCoord1 and TexCoord2 for flipbook UV remapping.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Enable Canvas UV Channels"))
                {
                    if (!SpriteSequenceRendererEditorUtility.EnableRequiredCanvasChannels(canvas, out string failure))
                    {
                        EditorUtility.DisplayDialog("Canvas Update Failed", failure, "OK");
                    }
                    InvalidateCompatibility();
                }
            }
        }

        private bool IsFlipbookEffectReadyForCurrentMode()
        {
            if (_renderMode.enumValueIndex != (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial)
            {
                return true;
            }

            FlipbookUVMeshEffect effect = _flipbookUvMeshEffect.objectReferenceValue as FlipbookUVMeshEffect;
            Image image = _image.objectReferenceValue as Image;
            image ??= _fallbackImage;
            return effect != null && effect.isActiveAndEnabled && effect.IsReadyFor(image);
        }

        private void GetCurrentDiagnosticContext(
            out Material material,
            out string shaderName,
            out bool requiresShader,
            out SerializedProperty materialProperty,
            out bool suggestFlipbook)
        {
            bool flipbook = _renderMode.enumValueIndex == (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
            if (flipbook)
            {
                materialProperty = _flipbookSharedMaterial;
                material = _flipbookSharedMaterial.objectReferenceValue as Material;
                shaderName = UGUISequenceRenderer.FlipbookShaderName;
                requiresShader = true;
                suggestFlipbook = false;
                return;
            }

            materialProperty = null;
            material = GetBoundMaterial();
            shaderName = material != null && material.shader != null ? material.shader.name : "UI/Default";
            requiresShader = false;
            suggestFlipbook = true;
        }

        private Material GetBoundMaterial()
        {
            Image image = _image.objectReferenceValue as Image;
            image ??= _fallbackImage;
            return image != null ? image.material : null;
        }

        private void EnsureCompatibility(Material material, string shaderName, bool requiresShader, bool suggestFlipbook)
        {
            int fingerprint = BuildCompatibilityFingerprint(material, shaderName, requiresShader, suggestFlipbook);
            if (!_compatibilityValid || fingerprint != _compatibilityFingerprint)
            {
                RefreshCompatibility(material, shaderName, requiresShader, suggestFlipbook);
            }
        }

        private void RefreshCompatibility(Material material, string shaderName, bool requiresShader, bool suggestFlipbook)
        {
            _controller = _rendererTarget != null ? _rendererTarget.GetComponent<SpriteSequenceController>() : null;
            _fallbackImage = _rendererTarget != null ? _rendererTarget.GetComponent<Image>() : null;
            _compatibilitySummary = SpriteSequenceRendererEditorUtility.BuildCompatibilitySummary(
                _rendererTarget,
                material,
                shaderName,
                suggestFlipbook);
            _compatibilityText = SpriteSequenceRendererEditorUtility.FormatCompatibilitySummary(_compatibilitySummary, requiresShader);
            _compatibilityFingerprint = BuildCompatibilityFingerprint(material, shaderName, requiresShader, suggestFlipbook);
            _compatibilityValid = true;
        }

        private int BuildCompatibilityFingerprint(Material material, string shaderName, bool requiresShader, bool suggestFlipbook)
        {
            unchecked
            {
                int hash = _rendererTarget != null ? EditorUtility.GetDirtyCount(_rendererTarget) : 0;
                hash = hash * 397 ^ (_controller != null ? EditorUtility.GetDirtyCount(_controller) : 0);
                hash = hash * 397 ^ (material != null ? material.GetInstanceID() : 0);
                hash = hash * 397 ^ (material != null ? EditorUtility.GetDirtyCount(material) : 0);
                Image image = _image.objectReferenceValue as Image;
                image ??= _fallbackImage;
                hash = hash * 397 ^ (image != null ? image.GetInstanceID() : 0);
                hash = hash * 397 ^ (image != null ? EditorUtility.GetDirtyCount(image) : 0);
                Canvas canvas = image != null ? image.canvas : null;
                hash = hash * 397 ^ (canvas != null ? EditorUtility.GetDirtyCount(canvas) : 0);
                UnityEngine.Object effect = _flipbookUvMeshEffect.objectReferenceValue;
                hash = hash * 397 ^ (effect != null ? effect.GetInstanceID() : 0);
                hash = hash * 397 ^ (effect != null ? EditorUtility.GetDirtyCount(effect) : 0);
                hash = hash * 397 ^ (shaderName != null ? shaderName.GetHashCode() : 0);
                hash = hash * 397 ^ (requiresShader ? 1 : 0);
                hash = hash * 397 ^ (suggestFlipbook ? 1 : 0);
                return hash;
            }
        }

        private void AssignMaterial(string propertyPath, Material material)
        {
            if (target == null || serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            SerializedObject targetObject = new(target);
            targetObject.Update();
            SerializedProperty property = targetObject.FindProperty(propertyPath);
            if (property == null)
            {
                return;
            }

            property.objectReferenceValue = material;
            targetObject.ApplyModifiedProperties();
            InvalidateCompatibility();
            Repaint();
        }

        private void OnUndoRedo()
        {
            InvalidateCompatibility();
            Repaint();
        }

        private void InvalidateCompatibility()
        {
            _compatibilityValid = false;
        }
    }
}
#endif
