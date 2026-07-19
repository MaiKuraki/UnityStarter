using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Foundation2D.Editor;
using CycloneGames.Foundation2D.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CycloneGames.Foundation2D.Tests.Editor
{
    public sealed class Foundation2DInspectorTests
    {
        private static readonly float[] SupportedInspectorWidths = { 280f, 360f, 560f };

        [Test]
        public void CoreInspectors_BindEveryDeclaredSerializedProperty()
        {
            List<GameObject> owners = new();
            List<UnityEditor.Editor> editors = new();
            try
            {
                CreateCoreTargets(owners, out SpriteSequenceController controller, out SpriteRendererSequenceRenderer spriteRenderer, out UGUISequenceRenderer uguiRenderer);

                AssertInspectorContract(controller, typeof(SpriteSequenceControllerEditor), editors);
                AssertInspectorContract(spriteRenderer, typeof(SpriteRendererSequenceRendererEditor), editors);
                AssertInspectorContract(uguiRenderer, typeof(UGUISequenceRendererEditor), editors);
            }
            finally
            {
                DestroyEditors(editors);
                DestroyOwners(owners);
            }
        }

        [Test]
        public void CoreInspectors_SupportSingleAndMultiObjectTargets()
        {
            List<GameObject> owners = new();
            List<UnityEditor.Editor> editors = new();
            try
            {
                CreateCoreTargets(owners, out SpriteSequenceController firstController, out SpriteRendererSequenceRenderer firstSpriteRenderer, out UGUISequenceRenderer firstUguiRenderer);
                CreateCoreTargets(owners, out SpriteSequenceController secondController, out SpriteRendererSequenceRenderer secondSpriteRenderer, out UGUISequenceRenderer secondUguiRenderer);

                AssertMultiObjectEditor(firstController, secondController, typeof(SpriteSequenceControllerEditor), editors);
                AssertMultiObjectEditor(firstSpriteRenderer, secondSpriteRenderer, typeof(SpriteRendererSequenceRendererEditor), editors);
                AssertMultiObjectEditor(firstUguiRenderer, secondUguiRenderer, typeof(UGUISequenceRendererEditor), editors);
            }
            finally
            {
                DestroyEditors(editors);
                DestroyOwners(owners);
            }
        }

        [Test]
        public void CanvasChannelRepair_AddsRequiredChannelsWithoutDiscardingExistingFlags()
        {
            GameObject owner = new("Foundation2D Canvas Channel Test", typeof(Canvas));
            try
            {
                Canvas canvas = owner.GetComponent<Canvas>();
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord3;

                Type utilityType = typeof(UGUISequenceRendererEditor).Assembly.GetType(
                    "CycloneGames.Foundation2D.Editor.SpriteSequenceRendererEditorUtility",
                    true);
                MethodInfo repairMethod = utilityType.GetMethod(
                    "EnableRequiredCanvasChannels",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.That(repairMethod, Is.Not.Null);

                object[] arguments = { canvas, null };
                bool repaired = (bool)repairMethod.Invoke(null, arguments);

                Assert.That(repaired, Is.True);
                Assert.That(arguments[1], Is.Null);
                Assert.That(
                    canvas.additionalShaderChannels,
                    Is.EqualTo(AdditionalCanvasShaderChannels.TexCoord3 | FlipbookUVMeshEffect.RequiredCanvasChannels));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void UguiInspector_ReadinessMatchesRuntimeWhenFlipbookEffectIsDisabled()
        {
            List<GameObject> owners = new();
            UnityEditor.Editor inspector = null;
            try
            {
                GameObject canvasOwner = new("Foundation2D Readiness Canvas", typeof(Canvas));
                owners.Add(canvasOwner);
                Canvas canvas = canvasOwner.GetComponent<Canvas>();
                canvas.additionalShaderChannels = FlipbookUVMeshEffect.RequiredCanvasChannels;

                GameObject imageOwner = new(
                    "Foundation2D Readiness Image",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                owners.Add(imageOwner);
                imageOwner.transform.SetParent(canvasOwner.transform, false);
                Image image = imageOwner.GetComponent<Image>();
                FlipbookUVMeshEffect effect = imageOwner.AddComponent<FlipbookUVMeshEffect>();
                UGUISequenceRenderer renderer = imageOwner.AddComponent<UGUISequenceRenderer>();

                SerializedObject rendererObject = new(renderer);
                rendererObject.FindProperty("image").objectReferenceValue = image;
                rendererObject.FindProperty("renderMode").enumValueIndex =
                    (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
                rendererObject.FindProperty("flipbookUvMeshEffect").objectReferenceValue = effect;
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                Canvas.ForceUpdateCanvases();

                inspector = UnityEditor.Editor.CreateEditor(renderer);
                MethodInfo readinessMethod = typeof(UGUISequenceRendererEditor).GetMethod(
                    "IsFlipbookEffectReadyForCurrentMode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(readinessMethod, Is.Not.Null);
                Assert.That((bool)readinessMethod.Invoke(inspector, null), Is.True);

                effect.enabled = false;
                Assert.That((bool)readinessMethod.Invoke(inspector, null), Is.False);
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                DestroyOwners(owners);
            }
        }

        [Test]
        public void UguiInspector_AddEffectUsesAssignedImageGameObjectAsOwner()
        {
            List<GameObject> owners = new();
            UnityEditor.Editor inspector = null;
            try
            {
                GameObject rendererOwner = new(
                    "Foundation2D External Image Renderer",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                owners.Add(rendererOwner);
                UGUISequenceRenderer renderer = rendererOwner.AddComponent<UGUISequenceRenderer>();

                GameObject imageOwner = new(
                    "Foundation2D External Image Target",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                owners.Add(imageOwner);
                Image assignedImage = imageOwner.GetComponent<Image>();

                inspector = UnityEditor.Editor.CreateEditor(renderer);
                SerializedObject rendererObject = inspector.serializedObject;
                rendererObject.Update();
                rendererObject.FindProperty("image").objectReferenceValue = assignedImage;
                rendererObject.FindProperty("flipbookUvMeshEffect").objectReferenceValue = null;
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                rendererObject.Update();

                MethodInfo addMethod = typeof(UGUISequenceRendererEditor).GetMethod(
                    "AddFlipbookEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(addMethod, Is.Not.Null);
                addMethod.Invoke(inspector, null);
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                rendererObject.Update();

                FlipbookUVMeshEffect assignedEffect =
                    rendererObject.FindProperty("flipbookUvMeshEffect").objectReferenceValue as FlipbookUVMeshEffect;
                Assert.That(assignedEffect, Is.Not.Null);
                Assert.That(assignedEffect.gameObject, Is.SameAs(imageOwner));
                Assert.That(rendererOwner.GetComponent<FlipbookUVMeshEffect>(), Is.Null);
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                DestroyOwners(owners);
            }
        }

        [TestCase(300f, 150f, 100f, 100f, 75f, 0f, 150f, 150f)]
        [TestCase(300f, 150f, 400f, 100f, 0f, 37.5f, 300f, 75f)]
        [TestCase(100f, 200f, 200f, 100f, 0f, 75f, 100f, 50f)]
        public void ControllerPreview_AspectFitUsesContainerAspect(
            float containerWidth,
            float containerHeight,
            float contentWidth,
            float contentHeight,
            float expectedX,
            float expectedY,
            float expectedWidth,
            float expectedHeight)
        {
            MethodInfo fitMethod = typeof(SpriteSequenceControllerEditor).GetMethod(
                "CalculateAspectFitRect",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(fitMethod, Is.Not.Null);

            Rect fitted = (Rect)fitMethod.Invoke(
                null,
                new object[] { new Rect(0f, 0f, containerWidth, containerHeight), contentWidth, contentHeight });

            Assert.That(fitted.x, Is.EqualTo(expectedX).Within(0.001f));
            Assert.That(fitted.y, Is.EqualTo(expectedY).Within(0.001f));
            Assert.That(fitted.width, Is.EqualTo(expectedWidth).Within(0.001f));
            Assert.That(fitted.height, Is.EqualTo(expectedHeight).Within(0.001f));
        }

        [Test]
        public void SectionHeaderFoldoutControlRect_InvertsMarginAndHierarchyTransforms()
        {
            Type uiType = typeof(UGUISequenceRendererEditor).Assembly.GetType(
                "CycloneGames.Foundation2D.Editor.Foundation2DInspectorUi",
                true);
            MethodInfo calculateRect = uiType.GetMethod(
                "CalculateSectionFoldoutControlRect",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(calculateRect, Is.Not.Null);

            Rect desiredRect = new(5f, 0f, 200f, 23f);
            RectOffset margin = new(3, 4, 2, 1);
            const float hierarchyOffset = 13f;

            foreach (bool hierarchyMode in new[] { false, true })
            {
                Rect controlRect = (Rect)calculateRect.Invoke(
                    null,
                    new object[] { desiredRect, margin, hierarchyMode, hierarchyOffset });
                Rect resolvedRect = margin.Remove(controlRect);
                if (hierarchyMode)
                {
                    resolvedRect.xMin -= hierarchyOffset;
                }

                Assert.That(resolvedRect.x, Is.EqualTo(desiredRect.x).Within(0.001f));
                Assert.That(resolvedRect.y, Is.EqualTo(desiredRect.y).Within(0.001f));
                Assert.That(resolvedRect.width, Is.EqualTo(desiredRect.width).Within(0.001f));
                Assert.That(resolvedRect.height, Is.EqualTo(desiredRect.height).Within(0.001f));
            }
        }

        [Test]
        public void SectionHeaderFoldoutControlRect_KeepsCurrentSkinDisclosureInsideHeader()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("A graphics device is required to initialize Unity's Editor GUI styles.");
            }

            Type uiType = typeof(UGUISequenceRendererEditor).Assembly.GetType(
                "CycloneGames.Foundation2D.Editor.Foundation2DInspectorUi",
                true);
            MethodInfo calculateRect = uiType.GetMethod(
                "CalculateSectionFoldoutControlRect",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo ensureStyles = uiType.GetMethod(
                "EnsureStyles",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo foldoutStyleField = uiType.GetField(
                "_sectionFoldoutStyle",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(calculateRect, Is.Not.Null);
            Assert.That(ensureStyles, Is.Not.Null);
            Assert.That(foldoutStyleField, Is.Not.Null);

            ensureStyles.Invoke(null, null);
            GUIStyle foldoutStyle = (GUIStyle)foldoutStyleField.GetValue(null);
            Assert.That(foldoutStyle, Is.Not.Null);

            float hierarchyOffset = Mathf.Max(
                0f,
                EditorStyles.foldout.padding.left - EditorStyles.label.padding.left);
            Assert.That(hierarchyOffset, Is.GreaterThan(0f));

            foreach (float width in new[] { 80f, 169f, 170f, 280f, 560f })
            {
                foreach (bool hierarchyMode in new[] { false, true })
                {
                    int rightEdgeCount = width >= 170f ? 2 : 1;
                    for (int rightEdgeIndex = 0; rightEdgeIndex < rightEdgeCount; rightEdgeIndex++)
                    {
                        float rightEdge = rightEdgeIndex == 0 ? width - 5f : width - 64f;
                        Rect headerRect = new(0f, 0f, width, 23f);
                        Rect desiredRect = new(5f, 0f, rightEdge - 5f, headerRect.height);
                        Rect controlRect = (Rect)calculateRect.Invoke(
                            null,
                            new object[]
                            {
                                desiredRect,
                                foldoutStyle.margin,
                                hierarchyMode,
                                hierarchyOffset,
                            });

                        // Independently reproduce Unity 2022.3 FoldoutInternal's forward
                        // margin and Inspector hierarchy transforms.
                        Rect resolvedRect = foldoutStyle.margin.Remove(controlRect);
                        if (hierarchyMode)
                        {
                            resolvedRect.xMin -= hierarchyOffset;
                        }

                        Assert.That(resolvedRect.xMin, Is.EqualTo(desiredRect.xMin).Within(0.001f));
                        Assert.That(resolvedRect.xMax, Is.EqualTo(desiredRect.xMax).Within(0.001f));
                        Assert.That(resolvedRect.yMin, Is.EqualTo(desiredRect.yMin).Within(0.001f));
                        Assert.That(resolvedRect.yMax, Is.EqualTo(desiredRect.yMax).Within(0.001f));
                        Assert.That(resolvedRect.width, Is.GreaterThan(0f));
                        Assert.That(resolvedRect.xMax, Is.LessThanOrEqualTo(rightEdge));

                        Rect disclosureRect = foldoutStyle.overflow.Add(new Rect(
                            resolvedRect.xMin,
                            resolvedRect.yMin,
                            foldoutStyle.padding.left,
                            resolvedRect.height));
                        Assert.That(disclosureRect.width, Is.GreaterThan(0f));
                        Assert.That(disclosureRect.height, Is.GreaterThan(0f));
                        Assert.That(disclosureRect.xMin, Is.GreaterThanOrEqualTo(headerRect.xMin));
                        Assert.That(disclosureRect.yMin, Is.GreaterThanOrEqualTo(headerRect.yMin));
                        Assert.That(disclosureRect.xMax, Is.LessThanOrEqualTo(headerRect.xMax));
                        Assert.That(disclosureRect.yMax, Is.LessThanOrEqualTo(headerRect.yMax));
                    }
                }
            }
        }

        [Test]
        public void CoreInspectors_CompleteLayoutAndRepaintAtSupportedWidths()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("A graphics device is required to host an EditorWindow and execute IMGUI Layout/Repaint passes.");
            }

            List<GameObject> owners = new();
            List<UnityEditor.Editor> editors = new();
            InspectorHostWindow host = null;
            try
            {
                CreateCoreTargets(owners, out SpriteSequenceController controller, out SpriteRendererSequenceRenderer spriteRenderer, out UGUISequenceRenderer uguiRenderer);
                CreateCoreTargets(owners, out SpriteSequenceController flipbookController, out SpriteRendererSequenceRenderer flipbookSpriteRenderer, out UGUISequenceRenderer flipbookUguiRenderer);

                GameObject canvasOwner = new("Foundation2D Inspector Canvas", typeof(RectTransform), typeof(Canvas));
                owners.Add(canvasOwner);
                flipbookUguiRenderer.transform.SetParent(canvasOwner.transform, false);
                Image flipbookImage = flipbookUguiRenderer.GetComponent<Image>();
                FlipbookUVMeshEffect flipbookEffect = flipbookUguiRenderer.gameObject.AddComponent<FlipbookUVMeshEffect>();

                SerializedObject flipbookUguiObject = new(flipbookUguiRenderer);
                flipbookUguiObject.FindProperty("image").objectReferenceValue = flipbookImage;
                flipbookUguiObject.FindProperty("renderMode").enumValueIndex =
                    (int)UGUISequenceRenderer.UGUIRenderMode.ShaderFlipbookSharedMaterial;
                flipbookUguiObject.FindProperty("flipbookUvMeshEffect").objectReferenceValue = flipbookEffect;
                flipbookUguiObject.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject flipbookSpriteObject = new(flipbookSpriteRenderer);
                flipbookSpriteObject.FindProperty("renderMode").enumValueIndex =
                    (int)SpriteRendererSequenceRenderer.SpriteRenderMode.ShaderFlipbookSharedMaterial;
                flipbookSpriteObject.ApplyModifiedPropertiesWithoutUndo();

                UnityEditor.Editor controllerEditor = UnityEditor.Editor.CreateEditor(controller);
                SetPrivateBoolean(controllerEditor, "_foldLoop", true);
                SetPrivateBoolean(controllerEditor, "_foldPreview", true);
                editors.Add(controllerEditor);
                editors.Add(UnityEditor.Editor.CreateEditor(spriteRenderer));
                editors.Add(UnityEditor.Editor.CreateEditor(uguiRenderer));
                editors.Add(UnityEditor.Editor.CreateEditor(flipbookSpriteRenderer));
                editors.Add(UnityEditor.Editor.CreateEditor(flipbookUguiRenderer));
                editors.Add(UnityEditor.Editor.CreateEditor(flipbookEffect));
                editors.Add(UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { controller, flipbookController }));
                editors.Add(UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { spriteRenderer, flipbookSpriteRenderer }));
                editors.Add(UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { uguiRenderer, flipbookUguiRenderer }));

                host = ScriptableObject.CreateInstance<InspectorHostWindow>();
                host.titleContent = new GUIContent("Foundation2D Inspector Test Host");
                host.ShowUtility();

                for (int editorIndex = 0; editorIndex < editors.Count; editorIndex++)
                {
                    UnityEditor.Editor inspector = editors[editorIndex];
                    for (int widthIndex = 0; widthIndex < SupportedInspectorWidths.Length; widthIndex++)
                    {
                        host.HostedEditor = inspector;
                        host.ResetResult();
                        host.position = new Rect(100f, 100f, SupportedInspectorWidths[widthIndex], 900f);
                        host.SendEvent(new Event { type = EventType.Layout });
                        host.SendEvent(new Event { type = EventType.Repaint });

                        Assert.That(host.GuiException, Is.Null,
                            $"{inspector.GetType().Name} failed at width {SupportedInspectorWidths[widthIndex]}.");
                        Assert.That(host.PassCount, Is.GreaterThanOrEqualTo(2),
                            $"{inspector.GetType().Name} did not complete Layout and Repaint passes.");
                        Assert.That(host.GlobalGuiStateRestored, Is.True,
                            $"{inspector.GetType().Name} leaked global IMGUI state.");
                    }
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (host != null)
                {
                    host.HostedEditor = null;
                    host.Close();
                    UnityEngine.Object.DestroyImmediate(host);
                }

                DestroyEditors(editors);
                DestroyOwners(owners);
            }
        }

        private static void SetPrivateBoolean(UnityEditor.Editor inspector, string fieldName, bool value)
        {
            FieldInfo field = inspector.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{inspector.GetType().Name}.{fieldName} was not found.");
            field.SetValue(inspector, value);
        }

        private static void AssertInspectorContract(
            Component target,
            Type expectedEditorType,
            List<UnityEditor.Editor> editors)
        {
            UnityEditor.Editor inspector = UnityEditor.Editor.CreateEditor(target);
            editors.Add(inspector);
            Assert.That(inspector, Is.TypeOf(expectedEditorType));

            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo[] instanceFields = expectedEditorType.GetFields(instanceFlags);
            for (int i = 0; i < instanceFields.Length; i++)
            {
                FieldInfo field = instanceFields[i];
                if (field.FieldType == typeof(SerializedProperty))
                {
                    Assert.That(field.GetValue(inspector), Is.Not.Null,
                        $"{expectedEditorType.Name}.{field.Name} did not bind to a serialized property.");
                }
            }

            FieldInfo explicitPathsField = expectedEditorType.GetField(
                "ExplicitlyDrawnProperties",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(explicitPathsField, Is.Not.Null,
                $"{expectedEditorType.Name} must expose its explicit serialized-property closure for tests.");

            string[] paths = (string[])explicitPathsField.GetValue(null);
            HashSet<string> uniquePaths = new(StringComparer.Ordinal);
            SerializedObject serializedTarget = new(target);
            for (int i = 0; i < paths.Length; i++)
            {
                Assert.That(uniquePaths.Add(paths[i]), Is.True,
                    $"{expectedEditorType.Name} declares duplicate path {paths[i]}.");
                Assert.That(serializedTarget.FindProperty(paths[i]), Is.Not.Null,
                    $"{expectedEditorType.Name} references missing path {paths[i]}.");
            }
        }

        private static void AssertMultiObjectEditor(
            Component first,
            Component second,
            Type expectedEditorType,
            List<UnityEditor.Editor> editors)
        {
            UnityEditor.Editor inspector = UnityEditor.Editor.CreateEditor(
                new UnityEngine.Object[] { first, second });
            editors.Add(inspector);
            Assert.That(inspector, Is.TypeOf(expectedEditorType));
            Assert.That(inspector.serializedObject.isEditingMultipleObjects, Is.True);

            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo[] fields = expectedEditorType.GetFields(instanceFlags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == typeof(SerializedProperty))
                {
                    Assert.That(fields[i].GetValue(inspector), Is.Not.Null,
                        $"{expectedEditorType.Name}.{fields[i].Name} did not bind for multi-object editing.");
                }
            }
        }

        private static void CreateCoreTargets(
            List<GameObject> owners,
            out SpriteSequenceController controller,
            out SpriteRendererSequenceRenderer spriteRenderer,
            out UGUISequenceRenderer uguiRenderer)
        {
            GameObject controllerOwner = new("Foundation2D Controller Test");
            owners.Add(controllerOwner);
            controller = controllerOwner.AddComponent<SpriteSequenceController>();

            GameObject spriteOwner = new("Foundation2D SpriteRenderer Test", typeof(SpriteRenderer));
            owners.Add(spriteOwner);
            spriteRenderer = spriteOwner.AddComponent<SpriteRendererSequenceRenderer>();

            GameObject uguiOwner = new(
                "Foundation2D UGUI Test",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            owners.Add(uguiOwner);
            uguiRenderer = uguiOwner.AddComponent<UGUISequenceRenderer>();
        }

        private static void DestroyEditors(List<UnityEditor.Editor> editors)
        {
            for (int i = 0; i < editors.Count; i++)
            {
                if (editors[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(editors[i]);
                }
            }
        }

        private static void DestroyOwners(List<GameObject> owners)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(owners[i]);
                }
            }
        }

        private sealed class InspectorHostWindow : EditorWindow
        {
            internal UnityEditor.Editor HostedEditor;
            internal Exception GuiException;
            internal int PassCount;
            internal bool GlobalGuiStateRestored = true;

            internal void ResetResult()
            {
                GuiException = null;
                PassCount = 0;
                GlobalGuiStateRestored = true;
            }

            private void OnGUI()
            {
                if (HostedEditor == null)
                {
                    return;
                }

                bool previousEnabled = GUI.enabled;
                Color previousColor = GUI.color;
                Color previousBackground = GUI.backgroundColor;
                int previousIndent = EditorGUI.indentLevel;
                bool previousMixedValue = EditorGUI.showMixedValue;
                bool previousHierarchyMode = EditorGUIUtility.hierarchyMode;
                EditorGUIUtility.hierarchyMode = true;
                try
                {
                    HostedEditor.OnInspectorGUI();
                }
                catch (Exception exception)
                {
                    GuiException ??= exception;
                }
                finally
                {
                    GlobalGuiStateRestored &=
                        GUI.enabled == previousEnabled &&
                        GUI.color == previousColor &&
                        GUI.backgroundColor == previousBackground &&
                        EditorGUI.indentLevel == previousIndent &&
                        EditorGUI.showMixedValue == previousMixedValue &&
                        EditorGUIUtility.hierarchyMode;

                    GUI.enabled = previousEnabled;
                    GUI.color = previousColor;
                    GUI.backgroundColor = previousBackground;
                    EditorGUI.indentLevel = previousIndent;
                    EditorGUI.showMixedValue = previousMixedValue;
                    EditorGUIUtility.hierarchyMode = previousHierarchyMode;
                    PassCount++;
                }
            }
        }
    }
}
