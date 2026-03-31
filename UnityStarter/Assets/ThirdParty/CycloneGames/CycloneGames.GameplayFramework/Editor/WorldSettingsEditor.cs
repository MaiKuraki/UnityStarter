using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(WorldSettings), true)]
    public class WorldSettingsEditor : UnityEditor.Editor
    {
        private static readonly Color ValidColor = new Color(0.7f, 1f, 0.7f);
        private static readonly Color WarningColor = new Color(1f, 0.9f, 0.5f);
        private static readonly Color ErrorColor = new Color(1f, 0.6f, 0.6f);

        public override void OnInspectorGUI()
        {
            var ws = (WorldSettings)target;

            EditorGUILayout.LabelField("World Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);

            DrawValidationStatus("GameMode", ws.GameModeClass, true);
            DrawValidationStatus("PlayerController", ws.PlayerControllerClass, true);
            DrawValidationStatus("Pawn", ws.PawnClass, true);
            DrawValidationStatus("PlayerState", ws.PlayerStateClass, false);
            DrawValidationStatus("CameraManager", ws.CameraManagerClass, false);
            DrawValidationStatus("SpectatorPawn", ws.SpectatorPawnClass, false);

            GUI.color = Color.white;
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Validate Configuration"))
            {
                bool valid = ws.Validate();
                if (valid)
                {
                    Debug.Log($"[WorldSettings] '{ws.name}': All required references are assigned.");
                }
            }

            EditorGUILayout.Space(8);

            DrawDefaultInspector();
        }

        private void DrawValidationStatus(string label, Object reference, bool required)
        {
            if (reference != null)
            {
                GUI.color = ValidColor;
                EditorGUILayout.LabelField($"  \u2713 {label}", EditorStyles.miniLabel);
            }
            else if (required)
            {
                GUI.color = ErrorColor;
                EditorGUILayout.LabelField($"  \u2717 {label} (Required)", EditorStyles.miniBoldLabel);
            }
            else
            {
                GUI.color = WarningColor;
                EditorGUILayout.LabelField($"  \u25CB {label} (Optional)", EditorStyles.miniLabel);
            }
        }
    }
}
