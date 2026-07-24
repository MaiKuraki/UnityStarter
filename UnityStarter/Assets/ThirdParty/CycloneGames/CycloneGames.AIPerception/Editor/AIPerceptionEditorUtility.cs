using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    /// <summary>
    /// Session-scoped Scene view diagnostics for AI Perception.
    /// The global setting is intentionally not persisted because drawing every sensor has an Editor cost.
    /// </summary>
    [InitializeOnLoad]
    public static class AIPerceptionEditorUtility
    {
        private const string SceneGizmosMenuRoot =
            "Tools/CycloneGames/AI Perception/Scene Gizmos/";
        private const string ShowAllGizmosMenu =
            SceneGizmosMenuRoot + "Show All (Session)";
        private const string FilledVolumesMenu =
            SceneGizmosMenuRoot + "Filled Volumes (Session)";
        private const string PinSelectedObjectsMenu =
            SceneGizmosMenuRoot + "Pin Selected Objects";
        private const string UnpinSelectedObjectsMenu =
            SceneGizmosMenuRoot + "Unpin Selected Objects";
        private const string DebugOverlayPropertyName = "_showDebugOverlay";

        private static bool _globalShowGizmos;
        private static bool _filledVolumes = true;

        static AIPerceptionEditorUtility()
        {
            Menu.SetChecked(ShowAllGizmosMenu, false);
            Menu.SetChecked(FilledVolumesMenu, true);
        }

        public static bool GlobalShowGizmos
        {
            get => _globalShowGizmos;
            set
            {
                if (_globalShowGizmos == value)
                {
                    return;
                }

                _globalShowGizmos = value;
                Menu.SetChecked(ShowAllGizmosMenu, value);
                SceneView.RepaintAll();
            }
        }

        internal static bool FilledVolumes
        {
            get => _filledVolumes;
            set
            {
                if (_filledVolumes == value)
                {
                    return;
                }

                _filledVolumes = value;
                Menu.SetChecked(FilledVolumesMenu, value);
                SceneView.RepaintAll();
            }
        }

        [MenuItem(ShowAllGizmosMenu, false, 100)]
        private static void ToggleShowAllGizmos()
        {
            GlobalShowGizmos = !GlobalShowGizmos;
        }

        [MenuItem(ShowAllGizmosMenu, true)]
        private static bool ValidateShowAllGizmos()
        {
            Menu.SetChecked(ShowAllGizmosMenu, GlobalShowGizmos);
            return true;
        }

        [MenuItem(FilledVolumesMenu, false, 101)]
        private static void ToggleFilledVolumes()
        {
            FilledVolumes = !FilledVolumes;
        }

        [MenuItem(FilledVolumesMenu, true)]
        private static bool ValidateFilledVolumes()
        {
            Menu.SetChecked(FilledVolumesMenu, FilledVolumes);
            return true;
        }

        [MenuItem(PinSelectedObjectsMenu, false, 120)]
        private static void PinSelectedObjects()
        {
            SetSelectedObjectsPinned(Selection.gameObjects, true);
        }

        [MenuItem(PinSelectedObjectsMenu, true)]
        private static bool ValidatePinSelectedObjects()
        {
            return HasSelectedPinTargets(Selection.gameObjects);
        }

        [MenuItem(UnpinSelectedObjectsMenu, false, 121)]
        private static void UnpinSelectedObjects()
        {
            SetSelectedObjectsPinned(Selection.gameObjects, false);
        }

        [MenuItem(UnpinSelectedObjectsMenu, true)]
        private static bool ValidateUnpinSelectedObjects()
        {
            return HasSelectedPinTargets(Selection.gameObjects);
        }

        internal static int SetSelectedObjectsPinned(GameObject[] selectedObjects, bool pinned)
        {
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                return 0;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(pinned
                ? "Pin AI Perception Scene Gizmos"
                : "Unpin AI Perception Scene Gizmos");

            int changedCount = 0;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject selectedObject = selectedObjects[i];
                if (selectedObject == null)
                {
                    continue;
                }

                if (selectedObject.TryGetComponent(out AIPerceptionComponent perception))
                {
                    changedCount += ApplyPinnedState(perception, pinned);
                }

                if (selectedObject.TryGetComponent(out PerceptibleComponent perceptible))
                {
                    changedCount += ApplyPinnedState(perceptible, pinned);
                }
            }

            if (changedCount > 0)
            {
                Undo.CollapseUndoOperations(undoGroup);
                SceneView.RepaintAll();
            }

            return changedCount;
        }

        private static bool HasSelectedPinTargets(GameObject[] selectedObjects)
        {
            if (selectedObjects == null)
            {
                return false;
            }

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject selectedObject = selectedObjects[i];
                if (selectedObject != null &&
                    (selectedObject.TryGetComponent<AIPerceptionComponent>(out _) ||
                     selectedObject.TryGetComponent<PerceptibleComponent>(out _)))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ApplyPinnedState(Component target, bool pinned)
        {
            var serializedTarget = new SerializedObject(target);
            serializedTarget.Update();
            SerializedProperty property = serializedTarget.FindProperty(DebugOverlayPropertyName);
            if (property == null || property.boolValue == pinned)
            {
                return 0;
            }

            property.boolValue = pinned;
            if (!serializedTarget.ApplyModifiedProperties())
            {
                return 0;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            return 1;
        }
    }

    internal static class AIPerceptionSceneGizmoDrawer
    {
        internal const int MaximumDetectionLinks = 64;

        private const int SightSegments = 24;
        private const float DetectionPointSize = 0.055f;
        private const float IdleVolumeAlpha = 0.045f;
        private const float ActiveVolumeAlpha = 0.075f;
        private const float IdleSphereAlpha = 0.065f;
        private const float ActiveSphereAlpha = 0.105f;

        private static readonly Vector3[] SightRingPoints = new Vector3[SightSegments + 1];
        private static readonly Vector3[] SightCapPoints = new Vector3[SightSegments];
        private static readonly Vector3[] SightFacePoints = new Vector3[3];

        private static readonly Color SightDetectedColor = new Color(0.34f, 1f, 0.28f, 0.9f);
        private static readonly Color SightIdleColor = new Color(1f, 0.76f, 0.12f, 0.72f);
        private static readonly Color HearingDetectedColor = new Color(0.22f, 0.86f, 1f, 0.9f);
        private static readonly Color HearingIdleColor = new Color(0.28f, 0.62f, 1f, 0.7f);
        private static readonly Color ProximityDetectedColor = new Color(1f, 0.42f, 0.2f, 0.92f);
        private static readonly Color ProximityIdleColor = new Color(1f, 0.5f, 0.28f, 0.7f);
        private static readonly Color MemoryLinkColor = new Color(0.58f, 0.78f, 0.62f, 0.62f);
        private static readonly Color PerceptibleColor = new Color(0.18f, 0.85f, 0.72f, 0.8f);
        private static readonly Color DisabledPerceptibleColor = new Color(0.48f, 0.5f, 0.52f, 0.52f);
        private static readonly Color SoundSourceColor = new Color(1f, 0.58f, 0.16f, 0.9f);
        private static readonly Color LosPointColor = new Color(1f, 0.86f, 0.22f, 0.9f);

        [DrawGizmo(
            GizmoType.Selected |
            GizmoType.NonSelected |
            GizmoType.InSelectionHierarchy |
            GizmoType.NotInSelectionHierarchy)]
        private static void DrawPerceptionGizmos(AIPerceptionComponent target, GizmoType gizmoType)
        {
            if (!ShouldDraw(target, gizmoType))
            {
                return;
            }

            Color previousGizmoColor = Gizmos.color;
            Color previousHandleColor = Handles.color;
            try
            {
                DrawSight(target);
                DrawHearing(target);
                DrawProximity(target);
            }
            finally
            {
                Gizmos.color = previousGizmoColor;
                Handles.color = previousHandleColor;
            }
        }

        [DrawGizmo(
            GizmoType.Selected |
            GizmoType.NonSelected |
            GizmoType.InSelectionHierarchy |
            GizmoType.NotInSelectionHierarchy)]
        private static void DrawPerceptibleGizmos(PerceptibleComponent target, GizmoType gizmoType)
        {
            if (!ShouldDraw(target, gizmoType))
            {
                return;
            }

            Color previousGizmoColor = Gizmos.color;
            Color previousHandleColor = Handles.color;
            try
            {
                Vector3 position = target.transform.position;
                Color volumeColor = target.IsDetectable ? PerceptibleColor : DisabledPerceptibleColor;
                float radius = Mathf.Max(0f, target.DetectionRadius);
                DrawSphereVolume(position, radius, volumeColor, target.IsDetectable);

                if (target.IsSoundSource)
                {
                    Handles.color = SoundSourceColor;
                    float markerSize = HandleUtility.GetHandleSize(position) * 0.12f;
                    Handles.DrawWireDisc(position, Vector3.up, markerSize);
                    Handles.DrawWireDisc(position, Vector3.right, markerSize);
                }

                Vector3 losPoint = ToVector3(target.GetLOSPoint());
                if ((losPoint - position).sqrMagnitude > 0.000001f)
                {
                    Handles.color = LosPointColor;
                    Handles.DrawDottedLine(position, losPoint, 3f);
                    float markerSize = HandleUtility.GetHandleSize(losPoint) * 0.07f;
                    Handles.SphereHandleCap(0, losPoint, Quaternion.identity, markerSize, EventType.Repaint);
                }
            }
            finally
            {
                Gizmos.color = previousGizmoColor;
                Handles.color = previousHandleColor;
            }
        }

        internal static bool ShouldDraw(AIPerceptionComponent target, GizmoType gizmoType)
        {
            return target != null &&
                   target.isActiveAndEnabled &&
                   (IsSelected(gizmoType) || target.ShowDebugOverlay || AIPerceptionEditorUtility.GlobalShowGizmos);
        }

        internal static bool ShouldDraw(PerceptibleComponent target, GizmoType gizmoType)
        {
            return target != null &&
                   target.isActiveAndEnabled &&
                   (IsSelected(gizmoType) || target.ShowDebugOverlay || AIPerceptionEditorUtility.GlobalShowGizmos);
        }

        private static bool IsSelected(GizmoType gizmoType)
        {
            return (gizmoType & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0;
        }

        private static void DrawSight(AIPerceptionComponent target)
        {
            SightSensor sensor = target.SightSensor;
            if (!target.EnableSight || (Application.isPlaying && target.IsInitialized && sensor == null))
            {
                return;
            }

            SightSensorConfig config = sensor != null ? sensor.Config : target.SightConfig;
            Transform sensorTransform = target.transform;
            Vector3 position = sensorTransform.position;
            Vector3 forward = sensorTransform.forward;
            Vector3 right = sensorTransform.right;
            Vector3 up = sensorTransform.up;
            float halfAngle = Mathf.Clamp(config.HalfAngle, 0f, 180f);
            float distance = Mathf.Max(0f, config.MaxDistance);
            bool hasDetection = sensor != null && sensor.HasDetection;
            Color sightColor = hasDetection ? SightDetectedColor : SightIdleColor;

            if (halfAngle <= 0.01f)
            {
                Handles.color = sightColor;
                Handles.DrawLine(position, position + forward * distance);
                DrawDetectionLinks(position, sensor, SightDetectedColor);
                return;
            }

            if (halfAngle >= 179.5f)
            {
                DrawSphereVolume(position, distance, sightColor, hasDetection);
                DrawDetectionLinks(position, sensor, SightDetectedColor);
                return;
            }

            float angleStep = 360f / SightSegments;
            for (int i = 0; i < SightSegments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 axis = Mathf.Cos(angle) * right + Mathf.Sin(angle) * up;
                Vector3 direction = Quaternion.AngleAxis(halfAngle, axis) * forward;
                SightRingPoints[i] = position + direction * distance;
                SightCapPoints[i] = SightRingPoints[i];
            }

            SightRingPoints[SightSegments] = SightRingPoints[0];
            if (AIPerceptionEditorUtility.FilledVolumes && distance > 0f)
            {
                Handles.color = WithAlpha(
                    sightColor,
                    hasDetection ? ActiveVolumeAlpha : IdleVolumeAlpha);
                for (int i = 0; i < SightSegments; i++)
                {
                    SightFacePoints[0] = position;
                    SightFacePoints[1] = SightRingPoints[i];
                    SightFacePoints[2] = SightRingPoints[i + 1];
                    Handles.DrawAAConvexPolygon(SightFacePoints);
                }

                Handles.DrawAAConvexPolygon(SightCapPoints);
            }

            Handles.color = sightColor;
            Handles.DrawAAPolyLine(2f, SightRingPoints);
            for (int i = 0; i < SightSegments; i += SightSegments / 4)
            {
                Handles.DrawLine(position, SightRingPoints[i]);
            }

            DrawDetectionLinks(position, sensor, SightDetectedColor);
        }

        private static void DrawHearing(AIPerceptionComponent target)
        {
            HearingSensor sensor = target.HearingSensor;
            if (!target.EnableHearing || (Application.isPlaying && target.IsInitialized && sensor == null))
            {
                return;
            }

            HearingSensorConfig config = sensor != null ? sensor.Config : target.HearingConfig;
            Vector3 position = target.transform.position;
            bool hasDetection = sensor != null && sensor.HasDetection;
            Color volumeColor = hasDetection ? HearingDetectedColor : HearingIdleColor;
            DrawSphereVolume(position, Mathf.Max(0f, config.Radius), volumeColor, hasDetection);
            DrawDetectionLinks(position, sensor, HearingDetectedColor);
        }

        private static void DrawProximity(AIPerceptionComponent target)
        {
            ProximitySensor sensor = target.ProximitySensor;
            if (!target.EnableProximity || (Application.isPlaying && target.IsInitialized && sensor == null))
            {
                return;
            }

            ProximitySensorConfig config = sensor != null ? sensor.Config : target.ProximityConfig;
            Vector3 position = target.transform.position;
            bool hasDetection = sensor != null && sensor.HasDetection;
            Color volumeColor = hasDetection ? ProximityDetectedColor : ProximityIdleColor;
            DrawSphereVolume(position, Mathf.Max(0f, config.Radius), volumeColor, hasDetection);
            DrawDetectionLinks(position, sensor, ProximityDetectedColor);
        }

        private static void DrawSphereVolume(
            Vector3 position,
            float radius,
            Color wireColor,
            bool active)
        {
            if (AIPerceptionEditorUtility.FilledVolumes && radius > 0f)
            {
                Gizmos.color = WithAlpha(
                    wireColor,
                    active ? ActiveSphereAlpha : IdleSphereAlpha);
                Gizmos.DrawSphere(position, radius);
            }

            Gizmos.color = wireColor;
            Gizmos.DrawWireSphere(position, radius);
        }

        private static void DrawDetectionLinks(Vector3 origin, ISensor sensor, Color liveColor)
        {
            if (!Application.isPlaying || sensor == null)
            {
                return;
            }

            int count = Mathf.Min(sensor.DetectedCount, MaximumDetectionLinks);
            for (int i = 0; i < count; i++)
            {
                if (!sensor.TryGetResult(i, out DetectionResult result))
                {
                    continue;
                }

                Vector3 targetPosition = ToVector3(result.LastKnownPosition);
                Handles.color = result.IsFromMemory ? MemoryLinkColor : liveColor;
                Handles.DrawDottedLine(origin, targetPosition, result.IsFromMemory ? 5f : 2.5f);
                float markerSize = HandleUtility.GetHandleSize(targetPosition) * DetectionPointSize;
                Handles.SphereHandleCap(0, targetPosition, Quaternion.identity, markerSize, EventType.Repaint);
            }
        }

        private static Vector3 ToVector3(Unity.Mathematics.float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
