using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(InteractionSystem))]
    public class InteractionSystemEditor : UnityEditor.Editor
    {
        private SerializedProperty _is2DMode;
        private SerializedProperty _cellSize;

        private static bool s_gridFoldout = true;
        private static bool s_runtimeFoldout = true;

        private void OnEnable()
        {
            _is2DMode = serializedObject.FindProperty("is2DMode");
            _cellSize = serializedObject.FindProperty("cellSize");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Interaction System", EditorStyles.boldLabel);
            InteractionInspectorUiUtility.DrawHelpBox(
                "Central hub for spatial registration, distance monitoring, and VitalRouter command routing.",
                MessageType.None);

            DrawGridSettings();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGridSettings()
        {
            s_gridFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Spatial Grid",
                s_gridFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_gridFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_is2DMode, new GUIContent("2D Mode"));
                EditorGUILayout.PropertyField(_cellSize, new GUIContent("Cell Size"));

                if (_cellSize.floatValue <= 0f)
                    InteractionInspectorUiUtility.DrawHelpBox("Cell size must be greater than zero.", MessageType.Error);
                else if (_cellSize.floatValue < 2f)
                    InteractionInspectorUiUtility.DrawHelpBox("Small cell sizes increase occupied cell count and dictionary pressure.", MessageType.Warning);
            }
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying)
                return;

            s_runtimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Status",
                s_runtimeFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_runtimeFoldout)
                return;

            InteractionSystem system = (InteractionSystem)target;
            SpatialHashGrid grid = system.SpatialGrid;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (grid != null)
                {
                    EditorGUILayout.LabelField("Registered Interactables", grid.ItemCount.ToString());
                    EditorGUILayout.LabelField("Occupied Cells", grid.CellCount.ToString());
                    EditorGUILayout.LabelField("Slot Capacity", grid.SlotCapacity.ToString());
                    EditorGUILayout.LabelField("Mode", system.Is2DMode ? "2D (X/Y)" : "3D (X/Z)");
                }
                else
                {
                    EditorGUILayout.LabelField("Grid", "Not initialized");
                }
            }

            Repaint();
        }
    }
}
