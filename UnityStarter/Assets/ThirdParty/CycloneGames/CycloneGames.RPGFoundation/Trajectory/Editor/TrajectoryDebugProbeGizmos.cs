using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.RPGFoundation.Trajectory.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    /// <summary>
    /// Always-visible scene gizmos for <see cref="TrajectoryDebugProbe"/>.
    ///
    /// Unselected probes get a cheap indicator line only — tracing every probe on every repaint would
    /// put a physics query per probe into the editor loop. The full path is drawn only while the
    /// probe is selected.
    ///
    /// Lives in the Editor assembly so it can read <see cref="TrajectoryGizmoSettings"/> without the
    /// runtime assembly referencing editor code.
    /// </summary>
    internal static class TrajectoryDebugProbeGizmos
    {
        private const float IdleIndicatorLength = 1f;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawProbeGizmos(TrajectoryDebugProbe probe, GizmoType gizmoType)
        {
            if (probe == null)
            {
                return;
            }

            // A selected probe is fully drawn by TrajectoryDebugProbeEditor.OnSceneGUI, which owns the
            // trace, the hit markers, the swept-volume wireframe and the interaction handles. Drawing
            // anything here too doubled every line and double-blended the colours.
            if ((gizmoType & GizmoType.Selected) != 0)
            {
                return;
            }

            if (!TrajectoryGizmoSettings.ShowIdleIndicator)
            {
                return;
            }

            Vector3 origin = probe.Origin;
            Vector3 tip = origin + probe.Direction * IdleIndicatorLength;
            Color idle = probe.PreviewSegmentColor;
            idle.a = 0.25f;
            Gizmos.color = idle;
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawWireSphere(tip, 0.04f);
        }

        private static Vector3 ToVector3(TrajectoryVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
