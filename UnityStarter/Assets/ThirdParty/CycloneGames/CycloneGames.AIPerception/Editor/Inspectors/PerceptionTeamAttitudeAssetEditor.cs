using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    /// <summary>
    /// Replaces the raw array drawer. The relation list is derived data that only the matrix window or
    /// <see cref="PerceptionTeamAttitudeAsset.Normalize"/> should write, and hand-editing it is how
    /// duplicate pairs get into an asset.
    /// </summary>
    [CustomEditor(typeof(PerceptionTeamAttitudeAsset))]
    public sealed class PerceptionTeamAttitudeAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _defaultAttitude;
        private SerializedProperty _sameTeamFriendly;
        private SerializedProperty _relations;

        private void OnEnable()
        {
            _defaultAttitude = serializedObject.FindProperty("_defaultAttitude");
            _sameTeamFriendly = serializedObject.FindProperty("_sameTeamFriendly");
            _relations = serializedObject.FindProperty("_relations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var asset = (PerceptionTeamAttitudeAsset)target;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_defaultAttitude, new GUIContent("Default Attitude"));
            EditorGUILayout.PropertyField(_sameTeamFriendly, new GUIContent("Same Team Friendly"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                asset.Normalize();
                EditorUtility.SetDirty(asset);
                serializedObject.Update();
            }

            int relationCount = _relations != null && _relations.isArray ? _relations.arraySize : 0;
            int differingCells = CountDifferingCells(asset);
            EditorGUILayout.HelpBox(
                $"{relationCount} override(s) stored, {differingCells} of {PerceptionTeam.TeamCount * PerceptionTeam.TeamCount} cells differ from the policy. " +
                "Overrides are what get serialized; the 256x256 table is rebuilt at runtime.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Team Matrix"))
                {
                    AIPerceptionTeamMatrixWindow.OpenFromMenu();
                }

                if (GUILayout.Button("Normalize"))
                {
                    asset.Normalize();
                    EditorUtility.SetDirty(asset);
                    serializedObject.Update();
                }

                if (GUILayout.Button("Reset to neutral"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Reset team attitude table",
                            "Clear every override and set the default attitude back to Neutral?",
                            "Reset",
                            "Cancel"))
                    {
                        asset.ResetTo(PerceptionTeamAttitude.Neutral, false);
                        EditorUtility.SetDirty(asset);
                        serializedObject.Update();
                    }
                }
            }

            DrawRelations(asset);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRelations(PerceptionTeamAttitudeAsset asset)
        {
            if (asset.RelationCount == 0)
            {
                EditorGUILayout.HelpBox("No overrides: every pair resolves to the policy.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Overrides ({asset.RelationCount})", EditorStyles.boldLabel);
            const int maximumRows = 64;
            int shown = Mathf.Min(asset.RelationCount, maximumRows);
            for (int i = 0; i < shown; i++)
            {
                PerceptionTeamAttitudeAsset.Relation relation = asset.Relations[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"T{relation.ObserverTeam} -> T{relation.TargetTeam}", GUILayout.Width(120f));
                    GUILayout.Label(relation.Attitude.ToString(), EditorStyles.miniLabel);
                }
            }

            if (asset.RelationCount > shown)
            {
                EditorGUILayout.LabelField($"... {asset.RelationCount - shown} more", EditorStyles.miniLabel);
            }
        }

        private static int CountDifferingCells(PerceptionTeamAttitudeAsset asset)
        {
            int count = 0;
            for (int observer = 0; observer < PerceptionTeam.TeamCount; observer++)
            {
                for (int target = 0; target < PerceptionTeam.TeamCount; target++)
                {
                    if (asset.GetAttitude(observer, target) != PerceptionTeamAttitude.Neutral)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
