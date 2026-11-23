using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Runtime;

namespace CycloneGames.RPGFoundation.Movement.Editor
{
    internal static class MovementAnimationBackendInspector
    {
        internal static void DrawGuidance(string detail)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Bind this backend with BindAnimation(IMovementLocomotionSink, IMovementActionSink) on the Movement component.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "One Animator is driven by one backend; the guard disables any additional backend.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        internal static void DrawConflict(UnityEditor.Editor editor)
        {
            var owner = editor.target as MonoBehaviour;
            if (owner == null) return;

            MonoBehaviour[] components = owner.GetComponents<MonoBehaviour>();
            int backendCount = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue;
                if (components[i] is IMovementAnimationBackend) backendCount++;
            }
            if (backendCount <= 1) return;

            EditorGUILayout.HelpBox(
                backendCount + " movement animation backends are attached to this GameObject.",
                MessageType.Error);
            EditorGUILayout.LabelField(
                "One Animator can be driven by only one backend. Remove one of them; " +
                "at runtime the first keeps ownership.",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    [CustomEditor(typeof(UnityAnimatorMovementPresenter))]
    [CanEditMultipleObjects]
    public sealed class UnityAnimatorMovementPresenterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MovementAnimationBackendInspector.DrawConflict(this);
            MovementAnimationBackendInspector.DrawGuidance(
                "Parameter names belong to your Animator Controller. Leave a field empty to skip it.");
            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(PlayableMovementPresenter))]
    [CanEditMultipleObjects]
    public sealed class PlayableMovementPresenterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MovementAnimationBackendInspector.DrawConflict(this);
            MovementAnimationBackendInspector.DrawGuidance(
                "Locomotion entries map MovementStateType to a clip; action entries map " +
                "MovementAnimationActionKind. Unassigned states keep the current clip.");
            DrawDefaultInspector();
        }
    }
}
