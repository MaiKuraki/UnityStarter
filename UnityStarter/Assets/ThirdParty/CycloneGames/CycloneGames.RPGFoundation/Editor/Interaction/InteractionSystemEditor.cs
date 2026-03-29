using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(InteractionSystem))]
    public class InteractionSystemEditor : UnityEditor.Editor
    {
        private SerializedProperty _is2DMode;
        private SerializedProperty _cellSize;

        private void OnEnable()
        {
            _is2DMode = serializedObject.FindProperty("is2DMode");
            _cellSize = serializedObject.FindProperty("cellSize");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Interaction System", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Central hub for the interaction module.\n" +
                "Manages spatial hash grid and routes interaction commands via VitalRouter.",
                MessageType.None);

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Spatial Grid", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_is2DMode, new GUIContent("2D Mode", "Use X/Y plane for spatial hashing (2D games)"));
                EditorGUILayout.PropertyField(_cellSize, new GUIContent("Cell Size", "Spatial hash cell size in world units. Larger = fewer cells, smaller = finer queries."));

                if (_cellSize.floatValue <= 0f)
                {
                    EditorGUILayout.HelpBox("Cell size must be > 0.", MessageType.Error);
                }
                else if (_cellSize.floatValue < 2f)
                {
                    EditorGUILayout.HelpBox("Small cell sizes increase memory usage. Recommended: 5-20 for most games.", MessageType.Warning);
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                var system = (InteractionSystem)target;
                var grid = system.SpatialGrid;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

                    if (grid != null)
                    {
                        EditorGUILayout.LabelField($"Registered Interactables: {grid.ItemCount}");
                        EditorGUILayout.LabelField($"Occupied Cells: {grid.CellCount}");
                        EditorGUILayout.LabelField($"Slot Capacity: {grid.SlotCapacity}");
                        EditorGUILayout.LabelField($"Mode: {(system.Is2DMode ? "2D (X/Y)" : "3D (X/Z)")}");
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Grid: Not initialized");
                    }
                }

                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
