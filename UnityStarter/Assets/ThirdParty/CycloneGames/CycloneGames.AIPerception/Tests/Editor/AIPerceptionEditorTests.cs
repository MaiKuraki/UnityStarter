using System;
using System.Reflection;
using CycloneGames.AIPerception.Editor;
using CycloneGames.AIPerception.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.AIPerception.Tests.Editor
{
    public sealed class AIPerceptionEditorTests
    {
        [TearDown]
        public void TearDown()
        {
            AIPerceptionEditorUtility.GlobalShowGizmos = false;
        }

        [Test]
        public void CustomInspectors_AreRegisteredForMultiObjectEditing()
        {
            var firstObject = new GameObject("AIPerceptionEditorTests_First");
            var secondObject = new GameObject("AIPerceptionEditorTests_Second");
            UnityEditor.Editor perceptionEditor = null;
            UnityEditor.Editor perceptibleEditor = null;
            UnityEditor.Editor managerEditor = null;

            try
            {
                AIPerceptionComponent firstPerception = firstObject.AddComponent<AIPerceptionComponent>();
                AIPerceptionComponent secondPerception = secondObject.AddComponent<AIPerceptionComponent>();
                PerceptibleComponent firstPerceptible = firstObject.AddComponent<PerceptibleComponent>();
                PerceptibleComponent secondPerceptible = secondObject.AddComponent<PerceptibleComponent>();
                PerceptionManagerComponent firstManager = firstObject.AddComponent<PerceptionManagerComponent>();
                PerceptionManagerComponent secondManager = secondObject.AddComponent<PerceptionManagerComponent>();

                perceptionEditor = UnityEditor.Editor.CreateEditor(
                    new Object[] { firstPerception, secondPerception });
                perceptibleEditor = UnityEditor.Editor.CreateEditor(
                    new Object[] { firstPerceptible, secondPerceptible });
                managerEditor = UnityEditor.Editor.CreateEditor(
                    new Object[] { firstManager, secondManager });

                Assert.That(perceptionEditor, Is.TypeOf<AIPerceptionComponentEditor>());
                Assert.That(perceptibleEditor, Is.TypeOf<PerceptibleComponentEditor>());
                Assert.That(managerEditor, Is.TypeOf<PerceptionManagerComponentEditor>());
            }
            finally
            {
                Object.DestroyImmediate(perceptionEditor);
                Object.DestroyImmediate(perceptibleEditor);
                Object.DestroyImmediate(managerEditor);
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void SceneGizmoVisibility_UsesSelectionPinAndSessionScope()
        {
            var gameObject = new GameObject("AIPerceptionEditorTests_GizmoVisibility");
            try
            {
                AIPerceptionComponent component = gameObject.AddComponent<AIPerceptionComponent>();
                MethodInfo shouldDraw = ResolveShouldDrawMethod(typeof(AIPerceptionComponent));

                Assert.That(InvokeShouldDraw(shouldDraw, component, GizmoType.Selected), Is.True);
                Assert.That(InvokeShouldDraw(shouldDraw, component, GizmoType.NonSelected), Is.False);

                component.ShowDebugOverlay = true;
                Assert.That(InvokeShouldDraw(shouldDraw, component, GizmoType.NonSelected), Is.True);

                component.ShowDebugOverlay = false;
                AIPerceptionEditorUtility.GlobalShowGizmos = true;
                Assert.That(InvokeShouldDraw(shouldDraw, component, GizmoType.NonSelected), Is.True);

                component.enabled = false;
                Assert.That(InvokeShouldDraw(shouldDraw, component, GizmoType.Selected), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SceneGizmoMenu_PinsAndUnpinsMixedSelectedObjects()
        {
            Object[] previousSelection = Selection.objects;
            var perceptionObject = new GameObject("AIPerceptionEditorTests_MenuPerception");
            var perceptibleObject = new GameObject("AIPerceptionEditorTests_MenuPerceptible");
            AIPerceptionComponent perception = null;
            PerceptibleComponent perceptible = null;

            try
            {
                perception = perceptionObject.AddComponent<AIPerceptionComponent>();
                perceptible = perceptibleObject.AddComponent<PerceptibleComponent>();
                Selection.objects = new Object[] { perceptionObject, perceptibleObject };

                Assert.That(
                    EditorApplication.ExecuteMenuItem(
                        "Tools/CycloneGames/AI Perception/Scene Gizmos/Pin Selected Objects"),
                    Is.True);
                Assert.That(perception.ShowDebugOverlay, Is.True);
                Assert.That(perceptible.ShowDebugOverlay, Is.True);

                Undo.PerformUndo();
                Assert.That(perception.ShowDebugOverlay, Is.False);
                Assert.That(perceptible.ShowDebugOverlay, Is.False);

                Selection.objects = new Object[] { perceptionObject, perceptibleObject };
                Assert.That(
                    EditorApplication.ExecuteMenuItem(
                        "Tools/CycloneGames/AI Perception/Scene Gizmos/Pin Selected Objects"),
                    Is.True);
                Assert.That(
                    EditorApplication.ExecuteMenuItem(
                        "Tools/CycloneGames/AI Perception/Scene Gizmos/Unpin Selected Objects"),
                    Is.True);
                Assert.That(perception.ShowDebugOverlay, Is.False);
                Assert.That(perceptible.ShowDebugOverlay, Is.False);
            }
            finally
            {
                Selection.objects = previousSelection;
                if (perception != null)
                {
                    Undo.ClearUndo(perception);
                }

                if (perceptible != null)
                {
                    Undo.ClearUndo(perceptible);
                }

                Object.DestroyImmediate(perceptionObject);
                Object.DestroyImmediate(perceptibleObject);
            }
        }

        [Test]
        public void SceneGizmoPin_SupportsSerializedMultiObjectUndo()
        {
            var firstObject = new GameObject("AIPerceptionEditorTests_UndoFirst");
            var secondObject = new GameObject("AIPerceptionEditorTests_UndoSecond");
            AIPerceptionComponent first = null;
            AIPerceptionComponent second = null;
            try
            {
                first = firstObject.AddComponent<AIPerceptionComponent>();
                second = secondObject.AddComponent<AIPerceptionComponent>();
                var serialized = new SerializedObject(new Object[] { first, second });
                SerializedProperty pinProperty = serialized.FindProperty("_showDebugOverlay");

                Assert.That(pinProperty, Is.Not.Null);
                Undo.RecordObjects(new Object[] { first, second }, "Pin AI Perception Scene Gizmos");
                pinProperty.boolValue = true;
                Assert.That(serialized.ApplyModifiedProperties(), Is.True);
                Assert.That(first.ShowDebugOverlay, Is.True);
                Assert.That(second.ShowDebugOverlay, Is.True);

                Undo.PerformUndo();
                Assert.That(first.ShowDebugOverlay, Is.False);
                Assert.That(second.ShowDebugOverlay, Is.False);
            }
            finally
            {
                if (first != null)
                {
                    Undo.ClearUndo(first);
                }

                if (second != null)
                {
                    Undo.ClearUndo(second);
                }

                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        private static MethodInfo ResolveShouldDrawMethod(Type targetType)
        {
            Type drawerType = typeof(AIPerceptionEditorUtility).Assembly.GetType(
                "CycloneGames.AIPerception.Editor.AIPerceptionSceneGizmoDrawer",
                true);
            MethodInfo method = drawerType.GetMethod(
                "ShouldDraw",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { targetType, typeof(GizmoType) },
                null);
            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static bool InvokeShouldDraw(
            MethodInfo method,
            AIPerceptionComponent component,
            GizmoType gizmoType)
        {
            return (bool)method.Invoke(null, new object[] { component, gizmoType });
        }
    }
}
