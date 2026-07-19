#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(SpriteRendererSequenceRenderer))]
    [CanEditMultipleObjects]
    public sealed class SpriteRendererSequenceRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ModuleTitle = new("SpriteRenderer Sequence Renderer");
        private static readonly GUIContent ModuleSubtitle = new("Sprite swapping and shared-material flipbook authoring for SpriteRenderer.");
        private static readonly GUIContent SectionConfiguration = new("Renderer Configuration");
        private static readonly GUIContent SectionDiagnostics = new("Compatibility Diagnostics");
        private static readonly GUIContent BadgeRendering = new("RENDERING");
        private static readonly GUIContent BadgeReady = new("READY");
        private static readonly GUIContent BadgeReview = new("REVIEW");
        private static readonly GUIContent LabelSpriteRenderer = new("Sprite Renderer");
        private static readonly GUIContent LabelRenderMode = new("Render Mode");
        private static readonly GUIContent LabelMaterialStrategy = new("Material Strategy");
        private static readonly GUIContent LabelSharedMaterialOverride = new("Shared Material Override");
        private static readonly GUIContent LabelFlipbookMaterial = new("Flipbook Shared Material");

        private static readonly string[] ExplicitlyDrawnProperties =
        {
            "spriteRenderer",
            "renderMode",
            "materialStrategy",
            "sharedMaterialOverride",
            "flipbookSharedMaterial",
        };

        private SerializedProperty _spriteRenderer;
        private SerializedProperty _renderMode;
        private SerializedProperty _materialStrategy;
        private SerializedProperty _sharedMaterialOverride;
        private SerializedProperty _flipbookSharedMaterial;

        private SpriteRendererSequenceRenderer _rendererTarget;
        private SpriteRenderer _fallbackSpriteRenderer;
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
            _spriteRenderer = serializedObject.FindProperty("spriteRenderer");
            _renderMode = serializedObject.FindProperty("renderMode");
            _materialStrategy = serializedObject.FindProperty("materialStrategy");
            _sharedMaterialOverride = serializedObject.FindProperty("sharedMaterialOverride");
            _flipbookSharedMaterial = serializedObject.FindProperty("flipbookSharedMaterial");
            _serializedPropertiesValid = Foundation2DInspectorUi.ValidateRequiredProperties(
                serializedObject,
                nameof(SpriteRendererSequenceRendererEditor),
                ExplicitlyDrawnProperties,
                out _serializedPropertiesError);
            _rendererTarget = target as SpriteRendererSequenceRenderer;
            _fallbackSpriteRenderer = _rendererTarget != null ? _rendererTarget.GetComponent<SpriteRenderer>() : null;
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
                    EditorGUILayout.PropertyField(_spriteRenderer, LabelSpriteRenderer, false);
                    EditorGUILayout.PropertyField(_renderMode, LabelRenderMode, false);

                    if (!_renderMode.hasMultipleDifferentValues)
                    {
                        bool useFlipbookMode = _renderMode.enumValueIndex == (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
                        if (useFlipbookMode)
                        {
                            EditorGUILayout.PropertyField(_flipbookSharedMaterial, LabelFlipbookMaterial, false);
                            EditorGUILayout.HelpBox("Flipbook mode keeps one shared material and updates UV remap through MaterialPropertyBlock. Frames must satisfy the runtime texture and geometry contract.", MessageType.None);
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(_materialStrategy, LabelMaterialStrategy, false);
                            if (!_materialStrategy.hasMultipleDifferentValues &&
                                _materialStrategy.enumValueIndex == (int)SpriteRendererSequenceRenderer.MaterialStrategy.SharedMaterialOverride)
                            {
                                EditorGUILayout.PropertyField(_sharedMaterialOverride, LabelSharedMaterialOverride, false);
                                EditorGUILayout.HelpBox("SharedMaterialOverride applies one explicit material. Its shader is not constrained by SpriteSwap mode.", MessageType.None);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("UseRendererDefault preserves the SpriteRenderer's current shared material.", MessageType.None);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Selected renderers use different render modes. Set a common mode to edit mode-specific settings.", MessageType.Info);
                    }
                }
            }

            if (!serializedObject.isEditingMultipleObjects && !_renderMode.hasMultipleDifferentValues)
            {
                GetCurrentDiagnosticContext(out Material material, out string shaderName, out bool requiresShader, out SerializedProperty materialProperty, out bool suggestFlipbook);
                EnsureCompatibility(material, shaderName, requiresShader, suggestFlipbook);
                Foundation2DInspectorUi.BadgeTone tone = _compatibilitySummary.MeetsSharedBatchingPrerequisites
                    ? Foundation2DInspectorUi.BadgeTone.Good
                    : Foundation2DInspectorUi.BadgeTone.Warning;
                GUIContent badge = _compatibilitySummary.MeetsSharedBatchingPrerequisites ? BadgeReady : BadgeReview;
                if (Foundation2DInspectorUi.DrawSectionHeader(ref _foldDiagnostics, SectionDiagnostics, badge, tone))
                {
                    using (Foundation2DInspectorUi.BeginCard())
                    {
                        DrawCompatibilitySummary(materialProperty, shaderName, requiresShader, suggestFlipbook);
                        DrawMaterialButtonsForCurrentMode(materialProperty, shaderName);
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
                    _renderMode.enumValueIndex = (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
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
                        "Create Compatible Renderer Material",
                        "_RendererCompatible.mat",
                        "Select a project path for a material matching the current renderer mode.");
                    if (created != null)
                    {
                        materialProperty.objectReferenceValue = created;
                        InvalidateCompatibility();
                    }
                }
            }
        }

        private void DrawMaterialButtonsForCurrentMode(SerializedProperty materialProperty, string shaderName)
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
                            "SpriteRenderer Material Picker",
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
                        "Create SpriteRenderer Material",
                        "_SpriteRenderer.mat",
                        "Select a project path for the created SpriteRenderer material.");
                    if (created != null)
                    {
                        materialProperty.objectReferenceValue = created;
                        InvalidateCompatibility();
                    }
                }
            }
        }

        private void GetCurrentDiagnosticContext(
            out Material material,
            out string shaderName,
            out bool requiresShader,
            out SerializedProperty materialProperty,
            out bool suggestFlipbook)
        {
            bool flipbook = _renderMode.enumValueIndex == (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
            if (flipbook)
            {
                materialProperty = _flipbookSharedMaterial;
                material = _flipbookSharedMaterial.objectReferenceValue as Material;
                shaderName = SpriteRendererSequenceRenderer.FlipbookShaderName;
                requiresShader = true;
                suggestFlipbook = false;
                return;
            }

            suggestFlipbook = true;
            if (!_materialStrategy.hasMultipleDifferentValues &&
                _materialStrategy.enumValueIndex == (int)SpriteRendererSequenceRenderer.MaterialStrategy.SharedMaterialOverride)
            {
                materialProperty = _sharedMaterialOverride;
                material = _sharedMaterialOverride.objectReferenceValue as Material;
                Material boundMaterial = GetBoundMaterial();
                shaderName = material != null && material.shader != null
                    ? material.shader.name
                    : boundMaterial != null && boundMaterial.shader != null
                        ? boundMaterial.shader.name
                        : "Sprites/Default";
                requiresShader = false;
                return;
            }

            materialProperty = null;
            material = GetBoundMaterial();
            shaderName = material != null && material.shader != null ? material.shader.name : "Sprites/Default";
            requiresShader = false;
        }

        private Material GetBoundMaterial()
        {
            SpriteRenderer renderer = _spriteRenderer.objectReferenceValue as SpriteRenderer;
            renderer ??= _fallbackSpriteRenderer;
            return renderer != null ? renderer.sharedMaterial : null;
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
            _fallbackSpriteRenderer = _rendererTarget != null ? _rendererTarget.GetComponent<SpriteRenderer>() : null;
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
                SpriteRenderer renderer = _spriteRenderer.objectReferenceValue as SpriteRenderer;
                renderer ??= _fallbackSpriteRenderer;
                hash = hash * 397 ^ (renderer != null ? renderer.GetInstanceID() : 0);
                hash = hash * 397 ^ (renderer != null ? EditorUtility.GetDirtyCount(renderer) : 0);
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
