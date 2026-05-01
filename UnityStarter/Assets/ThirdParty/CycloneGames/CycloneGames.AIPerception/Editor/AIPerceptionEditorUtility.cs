using UnityEditor;
using UnityEngine;
using CycloneGames.AIPerception.Runtime;

namespace CycloneGames.AIPerception.Editor
{
    [InitializeOnLoad]
    public static class AIPerceptionEditorUtility
    {
        private const string MENU_OVERLAYS_SHOW = "Tools/CycloneGames/AI Perception/Show All Debug Overlays";
        private const string MENU_OVERLAYS_HIDE = "Tools/CycloneGames/AI Perception/Hide All Debug Overlays";
        private const string MENU_GIZMOS_ALWAYS = "Tools/CycloneGames/AI Perception/Always Show Gizmos";
        private const string PREF_GIZMOS_ALWAYS = "CycloneGames.AIPerception.AlwaysShowGizmos";

        static AIPerceptionEditorUtility()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static bool GlobalShowGizmos
        {
            get => EditorPrefs.GetBool(PREF_GIZMOS_ALWAYS, false);
            set => EditorPrefs.SetBool(PREF_GIZMOS_ALWAYS, value);
        }

        [MenuItem(MENU_OVERLAYS_SHOW, false)]
        public static void ShowAllDebugOverlays()
        {
            SetAllOverlays(true);
        }

        [MenuItem(MENU_OVERLAYS_HIDE, false)]
        public static void HideAllDebugOverlays()
        {
            SetAllOverlays(false);
        }

        [MenuItem(MENU_GIZMOS_ALWAYS, false)]
        public static void ToggleAlwaysShowGizmos()
        {
            GlobalShowGizmos = !GlobalShowGizmos;
        }

        [MenuItem(MENU_GIZMOS_ALWAYS, true)]
        public static bool ToggleAlwaysShowGizmosValidate()
        {
            Menu.SetChecked(MENU_GIZMOS_ALWAYS, GlobalShowGizmos);
            return true;
        }

        private static void SetAllOverlays(bool show)
        {
            var perceptions = Object.FindObjectsByType<AIPerceptionComponent>(FindObjectsSortMode.None);
            foreach (var p in perceptions)
                p.ShowDebugOverlay = show;

            var perceptibles = Object.FindObjectsByType<PerceptibleComponent>(FindObjectsSortMode.None);
            foreach (var p in perceptibles)
                p.ShowDebugOverlay = show;

            Debug.Log($"[AIPerception] {(show ? "Shown" : "Hidden")} {perceptions.Length} AI Perception + {perceptibles.Length} Perceptible overlays.");
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                GlobalShowGizmos = false;
            }
        }
    }

    /// <summary>
    /// Draws sensor gizmos for ALL AIPerceptionComponents in scene, regardless of selection.
    /// Controlled by Tools > CycloneGames > AI Perception > Always Show Gizmos menu item.
    /// </summary>
    public class AIPerceptionGlobalGizmoDrawer
    {
        [DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.InSelectionHierarchy | GizmoType.Active)]
        private static void DrawGlobalGizmos(AIPerceptionComponent target, GizmoType gizmoType)
        {
            if (!Application.isPlaying) return;
            if (!AIPerceptionEditorUtility.GlobalShowGizmos) return;

            var pos = target.transform.position;
            var sight = target.SightSensor;
            var hearing = target.HearingSensor;
            var proximity = target.ProximitySensor;

            // Sight gizmo
            if (sight != null)
            {
                var forward = target.transform.forward;
                var right = target.transform.right;
                var up = target.transform.up;
                float halfAngle = sight.HalfAngle;
                float distance = sight.MaxDistance;
                bool hasDetection = sight.HasDetection;

                Gizmos.color = hasDetection ? new Color(0f, 1f, 0f, 0.15f) : new Color(1f, 0.8f, 0f, 0.08f);

                int segments = 32;
                float angleStep = 360f / segments;
                for (int i = 0; i < segments; i++)
                {
                    float a1 = i * angleStep * Mathf.Deg2Rad;
                    float a2 = (i + 1) * angleStep * Mathf.Deg2Rad;
                    Vector3 d1 = Quaternion.AngleAxis(halfAngle, Mathf.Cos(a1) * right + Mathf.Sin(a1) * up) * forward;
                    Vector3 d2 = Quaternion.AngleAxis(halfAngle, Mathf.Cos(a2) * right + Mathf.Sin(a2) * up) * forward;
                    Gizmos.DrawLine(pos, pos + d1 * distance);
                    Gizmos.DrawLine(pos + d1 * distance, pos + d2 * distance);
                }
            }

            // Hearing gizmo
            if (hearing != null)
            {
                float radius = hearing.Radius;
                bool hasDetection = hearing.HasDetection;

                Gizmos.color = hasDetection ? new Color(0.2f, 0.8f, 1f, 0.12f) : new Color(0.3f, 0.5f, 1f, 0.06f);
                Gizmos.DrawWireSphere(pos, radius);
            }

            // Proximity gizmo
            if (proximity != null)
            {
                float radius = proximity.Radius;
                bool hasDetection = proximity.HasDetection;

                Gizmos.color = hasDetection ? new Color(1f, 0.4f, 0.2f, 0.15f) : new Color(1f, 0.5f, 0.3f, 0.06f);
                Gizmos.DrawWireSphere(pos, radius);
            }
        }
    }
}
