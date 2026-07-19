#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(FlipbookUVMeshEffect))]
    [CanEditMultipleObjects]
    internal sealed class FlipbookUVMeshEffectEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ModuleTitle = new("Flipbook UV Mesh Effect");
        private static readonly GUIContent ModuleSubtitle = new("Carries per-instance flipbook UV rectangles through UGUI vertex channels.");
        private static readonly GUIContent SectionReadiness = new("UGUI Readiness");
        private static readonly GUIContent BadgeReady = new("READY");
        private static readonly GUIContent BadgeReview = new("REVIEW");

        private bool _foldReadiness = true;

        public override void OnInspectorGUI()
        {
            Foundation2DInspectorUi.DrawModuleHeader(ModuleTitle, ModuleSubtitle);
            if (serializedObject.isEditingMultipleObjects)
            {
                Foundation2DInspectorUi.DrawMultiObjectActionNotice();
                EditorGUILayout.HelpBox(
                    "Inspect one FlipbookUVMeshEffect at a time to validate its Graphic and Canvas channels.",
                    MessageType.Info);
                return;
            }

            FlipbookUVMeshEffect effect = target as FlipbookUVMeshEffect;
            Graphic graphic = effect != null ? effect.GetComponent<Graphic>() : null;
            Canvas canvas = graphic != null ? graphic.canvas : null;
            bool ready = effect != null && effect.isActiveAndEnabled && effect.IsReadyFor(graphic);
            Foundation2DInspectorUi.BadgeTone tone = ready
                ? Foundation2DInspectorUi.BadgeTone.Good
                : graphic == null
                    ? Foundation2DInspectorUi.BadgeTone.Error
                    : Foundation2DInspectorUi.BadgeTone.Warning;
            GUIContent badge = ready ? BadgeReady : BadgeReview;

            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldReadiness,
                    SectionReadiness,
                    badge,
                    tone))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Target Graphic", graphic, typeof(Graphic), true);
                    EditorGUILayout.ObjectField("Canvas", canvas, typeof(Canvas), true);
                }

                if (graphic == null)
                {
                    EditorGUILayout.HelpBox("No Graphic is available on this GameObject.", MessageType.Error);
                    return;
                }

                if (!effect.isActiveAndEnabled)
                {
                    if (!effect.enabled)
                    {
                        EditorGUILayout.HelpBox("This effect is disabled. Runtime flipbook rendering cannot use it until it is enabled.", MessageType.Warning);
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                        {
                            if (GUILayout.Button("Enable Flipbook UV Effect"))
                            {
                                SpriteSequenceRendererEditorUtility.EnableFlipbookEffect(effect);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("This GameObject is inactive in the current hierarchy. The effect becomes available when the GameObject is active.", MessageType.Warning);
                    }
                }
                else if (canvas == null)
                {
                    EditorGUILayout.HelpBox("The Graphic is not connected to a Canvas, so flipbook vertex channels cannot be validated.", MessageType.Warning);
                }
                else if (ready)
                {
                    EditorGUILayout.HelpBox("TexCoord1 and TexCoord2 are enabled on the Canvas. This effect is ready for UGUI flipbook remapping.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Enable TexCoord1 and TexCoord2 on the Canvas before using UGUI flipbook remapping.", MessageType.Warning);
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        if (GUILayout.Button("Enable Canvas UV Channels"))
                        {
                            if (!SpriteSequenceRendererEditorUtility.EnableRequiredCanvasChannels(canvas, out string failure))
                            {
                                EditorUtility.DisplayDialog("Canvas Update Failed", failure, "OK");
                            }
                        }
                    }
                }

                UGUISequenceRenderer renderer = effect.GetComponent<UGUISequenceRenderer>();
                if (renderer == null)
                {
                    EditorGUILayout.HelpBox("No UGUISequenceRenderer is present on this GameObject. A renderer on another GameObject can still drive this effect through an explicit reference.", MessageType.None);
                    return;
                }

                using (Foundation2DInspectorUi.BeginActionLayout(2, 110f))
                {
                    if (GUILayout.Button("Ping Renderer"))
                    {
                        EditorGUIUtility.PingObject(renderer);
                    }

                    if (GUILayout.Button("Select Renderer"))
                    {
                        Selection.activeObject = renderer;
                    }
                }
            }
        }
    }
}
#endif
