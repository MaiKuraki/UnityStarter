using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    /// <summary>
    /// Scene-view display toggles for trajectory probes.
    ///
    /// These are view preferences, not module configuration: they never affect runtime behaviour and
    /// are safe to reset. Stored in <see cref="EditorPrefs"/> for the same reason the interaction
    /// detector gizmo settings are — a per-machine display toggle should not dirty scenes or prefabs.
    /// </summary>
    public static class TrajectoryGizmoSettings
    {
        private const string Prefix = "TrajectoryProbe_";

        public static bool ShowSegments
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowSegments", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowSegments", value); }
        }

        public static bool ShowHits
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowHits", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowHits", value); }
        }

        public static bool ShowNormals
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowNormals", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowNormals", value); }
        }

        public static bool ShowLabels
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowLabels", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowLabels", value); }
        }

        public static bool ShowHandles
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowHandles", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowHandles", value); }
        }

        /// <summary>Wireframe of the sweep volume at the trace origin.</summary>
        public static bool ShowShapeAtOrigin
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowShapeAtOrigin", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowShapeAtOrigin", value); }
        }

        /// <summary>Wireframe of the sweep volume at the final position. Off by default: a multi-bounce
        /// trace would otherwise fill the view with boxes.</summary>
        public static bool ShowShapeAtEnd
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowShapeAtEnd", false); }
            set { EditorPrefs.SetBool(Prefix + "ShowShapeAtEnd", value); }
        }

        /// <summary>
        /// Lines connecting the origin shape to the end shape, forming the swept tube. Off by default:
        /// the end shape sits up to MaxDistance away, so these read as four long diagonals crossing
        /// the whole scene rather than as a tube.
        /// </summary>
        public static bool ShowSweepWalls
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowSweepWalls", false); }
            set { EditorPrefs.SetBool(Prefix + "ShowSweepWalls", value); }
        }

        /// <summary>
        /// Draws hemispherical meridian arcs on capsule caps. Without them the wireframe reads as a
        /// cylinder, which is the wrong volume for a CapsuleCast, so this is on by default. The cap
        /// poles are drawn with the arcs and never on their own.
        /// </summary>
        public static bool ShowCapsuleCaps
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowCapsuleCaps", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowCapsuleCaps", value); }
        }

        /// <summary>Dimmed indicator line drawn for probes that are not selected.</summary>
        public static bool ShowIdleIndicator
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowIdleIndicator", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowIdleIndicator", value); }
        }

        public static void DrawMenuItems()
        {
            ShowSegments = EditorGUILayout.ToggleLeft("Segments", ShowSegments);
            ShowHits = EditorGUILayout.ToggleLeft("Hit points", ShowHits);
            ShowNormals = EditorGUILayout.ToggleLeft("Hit normals", ShowNormals);
            ShowLabels = EditorGUILayout.ToggleLeft("Labels", ShowLabels);
            ShowHandles = EditorGUILayout.ToggleLeft("Scene handles", ShowHandles);
            ShowShapeAtOrigin = EditorGUILayout.ToggleLeft("Sweep shape at origin", ShowShapeAtOrigin);
            ShowShapeAtEnd = EditorGUILayout.ToggleLeft("Sweep shape at end", ShowShapeAtEnd);
            ShowSweepWalls = EditorGUILayout.ToggleLeft("Sweep walls (connect shapes)", ShowSweepWalls);
            ShowCapsuleCaps = EditorGUILayout.ToggleLeft("Capsule cap arcs", ShowCapsuleCaps);
            ShowIdleIndicator = EditorGUILayout.ToggleLeft("Idle indicator (unselected)", ShowIdleIndicator);
        }
    }
}
