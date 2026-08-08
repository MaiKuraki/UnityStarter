using System;
using System.Collections.Generic;
using Build.Data;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private bool HasAvailableDependency(int ownerIndex)
        {
            return FindAvailableDependencyId(ownerIndex) != null;
        }

        private void AddDependency(
            int ownerIndex,
            SerializedProperty dependencies)
        {
            string targetId = FindAvailableDependencyId(ownerIndex);
            if (targetId == null)
            {
                return;
            }

            int index = dependencies.arraySize;
            dependencies.InsertArrayElementAtIndex(index);
            SerializedProperty dependency = dependencies.GetArrayElementAtIndex(index);
            dependency.FindPropertyRelative("invocationId").stringValue = targetId;
            dependency.FindPropertyRelative("mode").enumValueIndex =
                (int)BuildDependencyMode.Required;
            InvalidateRecipeGraphSnapshot();
        }

        private string FindAvailableDependencyId(int ownerIndex)
        {
            return GetRecipeGraphSnapshot().FindFirstAvailableDependencyId(ownerIndex);
        }

        private void DrawDependency(
            Rect rect,
            int ownerIndex,
            SerializedProperty dependencies,
            int dependencyIndex)
        {
            SerializedProperty dependency =
                dependencies.GetArrayElementAtIndex(dependencyIndex);
            SerializedProperty targetId = dependency.FindPropertyRelative("invocationId");
            SerializedProperty mode = dependency.FindPropertyRelative("mode");

            float modeWidth = Math.Min(110f, rect.width * 0.32f);
            Rect modeRect = new Rect(rect.x, rect.y, modeWidth, rect.height);
            Rect removeRect = new Rect(rect.xMax - 22f, rect.y, 22f, rect.height);
            Rect targetRect = new Rect(
                modeRect.xMax + 4f,
                rect.y,
                rect.width - modeWidth - removeRect.width - 8f,
                rect.height);
            EditorGUI.PropertyField(modeRect, mode, GUIContent.none);

            string current = targetId.stringValue?.Trim() ?? string.Empty;
            IReadOnlyList<string> available = GetRecipeGraphSnapshot()
                .GetAvailableDependencyIds(ownerIndex, current);
            var ids = new List<string>(available);
            int selected = ids.FindIndex(id => string.Equals(
                id,
                current,
                StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                ids.Add(string.IsNullOrWhiteSpace(current)
                    ? "<Select invocation>"
                    : "<Missing: " + current + ">");
                selected = ids.Count - 1;
            }

            int next = EditorGUI.Popup(targetRect, selected, ids.ToArray());
            if (next >= 0 && next < ids.Count && next != selected
                && !ids[next].StartsWith("<", StringComparison.Ordinal))
            {
                targetId.stringValue = ids[next];
                InvalidateRecipeGraphSnapshot();
            }

            if (GUI.Button(removeRect, "-", EditorStyles.miniButton))
            {
                dependencies.DeleteArrayElementAtIndex(dependencyIndex);
                InvalidateRecipeGraphSnapshot();
            }
        }

        private bool WouldCreateDependencyCycle(int ownerIndex, string candidateId)
        {
            return GetRecipeGraphSnapshot().WouldCreateDependencyCycle(
                ownerIndex,
                candidateId);
        }

    }
}
