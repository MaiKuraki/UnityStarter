using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(PlayerStart), true)]
    [CanEditMultipleObjects]
    public sealed class PlayerStartEditor : ActorEditor
    {
        private const float DEFAULT_MARKER_RADIUS = 0.5f;
        private const float MIN_MARKER_SIZE = 0.01f;
        private const float MIN_ARROW_LENGTH = 0.95f;
        private const float MAX_AUTO_ARROW_LENGTH = 3.2f;
        private const float MIN_ARROW_HEAD_SIZE = 0.22f;
        private const float MAX_ARROW_HEAD_SIZE = 0.64f;
        private const float LABEL_OFFSET = 0.48f;

        private const string GIZMO_MODE_PROPERTY = "GizmoMode";
        private const string GIZMO_FACING_AXIS_PROPERTY = "GizmoFacingAxis";
        private const string GIZMO_DRAW_SHAPE_PROPERTY = "GizmoDrawShape";
        private const string GIZMO_DRAW_SPAWN_ANCHOR_PROPERTY = "GizmoDrawSpawnAnchor";
        private const string GIZMO_DRAW_FACING_ARROW_PROPERTY = "GizmoDrawFacingArrow";
        private const string GIZMO_DRAW_LABEL_PROPERTY = "GizmoDrawLabel";
        private const string GIZMO_ARROW_LENGTH_OVERRIDE_PROPERTY = "GizmoArrowLengthOverride";
        private const string GIZMO_ARROW_LENGTH_SCALE_PROPERTY = "GizmoArrowLengthScale";

        private static readonly Color GizmoHeaderColor = new Color(0.24f, 0.42f, 0.52f);
        private static readonly Color GizmoDebugHeaderColor = new Color(0.28f, 0.34f, 0.42f);
        private static readonly Color ShapeFillColor = new Color(0.16f, 0.95f, 0.52f, 0.075f);
        private static readonly Color ShapeFillSelectedColor = new Color(0.18f, 1f, 0.58f, 0.13f);
        private static readonly Color ShapeWireColor = new Color(0.09f, 0.86f, 0.42f, 0.78f);
        private static readonly Color ShapeWireSelectedColor = new Color(0.34f, 1f, 0.60f, 0.95f);
        private static readonly Color ArrowShadowColor = new Color(0f, 0.08f, 0.10f, 0.72f);
        private static readonly Color ArrowShaftColor = new Color(0.06f, 0.65f, 1f, 0.94f);
        private static readonly Color ArrowHeadColor = new Color(0.02f, 0.95f, 1f, 0.96f);
        private static readonly Color SpawnAnchorFillColor = new Color(1f, 0.92f, 0.24f, 0.22f);
        private static readonly Color SpawnAnchorWireColor = new Color(1f, 0.92f, 0.24f, 0.88f);
        private static readonly Color ClearOutlineColor = new Color(0f, 0f, 0f, 0f);

        private static readonly Vector3[] LinePoints = new Vector3[2];
        private static readonly Vector3[] RectanglePoints = new Vector3[4];
        private static readonly Vector3[] PentagonPoints = new Vector3[5];

        private bool showSceneGizmo = true;
        private bool showGizmoDebug;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            EditorGUILayout.Space(6);
            showSceneGizmo = InspectorUiUtility.DrawFoldoutHeader("Scene Gizmo", showSceneGizmo, GizmoHeaderColor);
            if (showSceneGizmo)
            {
                DrawSceneGizmoInspector();
            }

            serializedObject.ApplyModifiedProperties();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawPlayerStartGizmo(PlayerStart playerStart, GizmoType gizmoType)
        {
            if (playerStart == null)
            {
                return;
            }

            bool selected = (gizmoType & GizmoType.Selected) != 0;
            GizmoContext context = CreateGizmoContext(playerStart);
            GizmoMetrics metrics = DrawColliderPreview(playerStart, context, selected, playerStart.ShouldDrawGizmoShape());

            if (playerStart.ShouldDrawGizmoSpawnAnchor())
            {
                DrawSpawnAnchor(playerStart.transform, context, metrics, selected);
            }

            if (playerStart.ShouldDrawGizmoFacingArrow())
            {
                DrawFacingArrow(playerStart, context, metrics, selected);
            }

            if (selected && playerStart.ShouldDrawGizmoLabel())
            {
                DrawSelectedLabel(playerStart, context, metrics);
            }
        }

        private void DrawSceneGizmoInspector()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            InspectorUiUtility.DrawSectionHeader(
                "PlayerStart Preview",
                "Choose how the spawn direction is visualized for 3D, side-scroller 2D, or top-down 2D authoring.",
                new Color(0.42f, 0.86f, 1f, 1f));

            DrawProperty(GIZMO_MODE_PROPERTY, "Gizmo Mode");
            DrawProperty(GIZMO_FACING_AXIS_PROPERTY, "Facing Axis");

            using (new EditorGUI.DisabledScope(true))
            {
                if (targets.Length > 1)
                {
                    EditorGUILayout.LabelField("Resolved Mode", "Multiple PlayerStart objects");
                    EditorGUILayout.LabelField("Resolved Facing", "Multiple PlayerStart objects");
                    EditorGUILayout.LabelField("Gizmo Shape", "Multiple PlayerStart objects");
                }
                else
                {
                    PlayerStart playerStart = (PlayerStart)target;
                    GizmoContext context = CreateGizmoContext(playerStart);
                    EditorGUILayout.LabelField("Resolved Mode", context.Mode.ToString());
                    EditorGUILayout.LabelField("Resolved Facing", context.FacingAxis.ToString());
                    EditorGUILayout.LabelField("Gizmo Shape", GetGizmoShapeDescription(playerStart, context));
                }
            }

            EditorGUILayout.Space(4);
            showGizmoDebug = InspectorUiUtility.DrawFoldoutHeader("Gizmo Debug", showGizmoDebug, GizmoDebugHeaderColor);
            if (showGizmoDebug)
            {
                DrawProperty(GIZMO_DRAW_SHAPE_PROPERTY, "Draw Shape");
                DrawProperty(GIZMO_DRAW_SPAWN_ANCHOR_PROPERTY, "Draw Spawn Anchor");
                DrawProperty(GIZMO_DRAW_FACING_ARROW_PROPERTY, "Draw Facing Arrow");
                DrawProperty(GIZMO_DRAW_LABEL_PROPERTY, "Draw Selected Label");
                DrawProperty(GIZMO_ARROW_LENGTH_OVERRIDE_PROPERTY, "Arrow Length Override");
                DrawProperty(GIZMO_ARROW_LENGTH_SCALE_PROPERTY, "Arrow Length Scale");
                EditorGUILayout.HelpBox("Set Arrow Length Override to 0 to use the automatic clamped length.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawProperty(string propertyName, string label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
            }
        }

        private static string GetGizmoShapeDescription(PlayerStart playerStart, GizmoContext context)
        {
            if (playerStart == null)
            {
                return "None";
            }

            if (context.Is2D)
            {
                if (playerStart.TryGetComponent(out BoxCollider2D _))
                {
                    return "BoxCollider2D";
                }

                if (playerStart.TryGetComponent(out CapsuleCollider2D _))
                {
                    return "CapsuleCollider2D";
                }

                if (playerStart.TryGetComponent(out CircleCollider2D _))
                {
                    return "CircleCollider2D";
                }

                if (playerStart.TryGetComponent(out Collider2D collider2D))
                {
                    return collider2D.GetType().Name + " bounds";
                }
            }

            if (playerStart.TryGetComponent(out CharacterController _))
            {
                return "CharacterController";
            }

            if (playerStart.TryGetComponent(out CapsuleCollider _))
            {
                return "CapsuleCollider";
            }

            if (playerStart.TryGetComponent(out BoxCollider _))
            {
                return "BoxCollider";
            }

            if (playerStart.TryGetComponent(out SphereCollider _))
            {
                return "SphereCollider";
            }

            if (!context.Is2D && playerStart.TryGetComponent(out Collider2D fallback2D))
            {
                return fallback2D.GetType().Name + " bounds";
            }

            return playerStart.TryGetComponent(out Collider collider)
                ? collider.GetType().Name + " bounds"
                : "Default marker";
        }

        private static GizmoMetrics DrawColliderPreview(PlayerStart playerStart, GizmoContext context, bool selected, bool drawShape)
        {
            if (context.Is2D)
            {
                if (playerStart.TryGetComponent(out BoxCollider2D boxCollider2D))
                {
                    return DrawBoxCollider2DPreview(boxCollider2D, context, selected, drawShape);
                }

                if (playerStart.TryGetComponent(out CapsuleCollider2D capsuleCollider2D))
                {
                    return DrawCapsuleCollider2DPreview(capsuleCollider2D, context, selected, drawShape);
                }

                if (playerStart.TryGetComponent(out CircleCollider2D circleCollider2D))
                {
                    return DrawCircleCollider2DPreview(circleCollider2D, context, selected, drawShape);
                }

                if (playerStart.TryGetComponent(out Collider2D collider2D))
                {
                    return DrawCollider2DBoundsPreview(collider2D, context, selected, drawShape);
                }
            }

            if (playerStart.TryGetComponent(out CharacterController characterController))
            {
                return DrawCharacterControllerPreview(characterController, context, selected, drawShape);
            }

            if (playerStart.TryGetComponent(out CapsuleCollider capsuleCollider))
            {
                return DrawCapsuleColliderPreview(capsuleCollider, context, selected, drawShape);
            }

            if (playerStart.TryGetComponent(out BoxCollider boxCollider))
            {
                return DrawBoxColliderPreview(boxCollider, context, selected, drawShape);
            }

            if (playerStart.TryGetComponent(out SphereCollider sphereCollider))
            {
                return DrawSphereColliderPreview(sphereCollider, context, selected, drawShape);
            }

            if (playerStart.TryGetComponent(out Collider collider))
            {
                return DrawBoundsPreview(collider.bounds, context, selected, drawShape);
            }

            if (!context.Is2D && playerStart.TryGetComponent(out Collider2D fallback2D))
            {
                return DrawCollider2DBoundsPreview(fallback2D, context, selected, drawShape);
            }

            return DrawDefaultMarkerPreview(playerStart.transform, context, selected, drawShape);
        }

        private static GizmoMetrics DrawCharacterControllerPreview(
            CharacterController characterController,
            GizmoContext context,
            bool selected,
            bool drawShape)
        {
            Transform transform = characterController.transform;
            Vector3 scale = Abs(transform.lossyScale);
            float radius = Mathf.Max(characterController.radius * Mathf.Max(scale.x, scale.z), MIN_MARKER_SIZE);
            float height = Mathf.Max(characterController.height * scale.y, radius * 2f);
            Vector3 center = transform.TransformPoint(characterController.center);
            Vector3 axis = SafeNormalize(transform.up, Vector3.up);
            Vector3 right = SafeNormalize(transform.right, Vector3.right);
            Vector3 forward = SafeNormalize(transform.forward, Vector3.forward);

            if (drawShape)
            {
                DrawCapsulePreview(center, axis, right, forward, radius, height, selected);
            }

            return CreateCapsuleMetrics(context, center, axis, radius, height);
        }

        private static GizmoMetrics DrawCapsuleColliderPreview(
            CapsuleCollider capsuleCollider,
            GizmoContext context,
            bool selected,
            bool drawShape)
        {
            Transform transform = capsuleCollider.transform;
            GetCapsuleAxes(
                transform,
                capsuleCollider.direction,
                out Vector3 axis,
                out Vector3 firstPerpendicular,
                out Vector3 secondPerpendicular,
                out float axisScale,
                out float radiusScale);

            float radius = Mathf.Max(capsuleCollider.radius * radiusScale, MIN_MARKER_SIZE);
            float height = Mathf.Max(capsuleCollider.height * axisScale, radius * 2f);
            Vector3 center = transform.TransformPoint(capsuleCollider.center);

            if (drawShape)
            {
                DrawCapsulePreview(center, axis, firstPerpendicular, secondPerpendicular, radius, height, selected);
            }

            return CreateCapsuleMetrics(context, center, axis, radius, height);
        }

        private static GizmoMetrics DrawBoxColliderPreview(BoxCollider boxCollider, GizmoContext context, bool selected, bool drawShape)
        {
            Transform transform = boxCollider.transform;
            Vector3 size = Max(Vector3.Scale(boxCollider.size, Abs(transform.lossyScale)), MIN_MARKER_SIZE);
            Vector3 center = transform.TransformPoint(boxCollider.center);
            Vector3 right = SafeNormalize(transform.right, Vector3.right);
            Vector3 up = SafeNormalize(transform.up, Vector3.up);
            Vector3 forward = SafeNormalize(transform.forward, Vector3.forward);
            Vector3 halfRight = right * (size.x * 0.5f);
            Vector3 halfUp = up * (size.y * 0.5f);
            Vector3 halfForward = forward * (size.z * 0.5f);

            if (drawShape)
            {
                Color fillColor = GetShapeFillColor(selected);
                DrawSolidRectangle(center + halfUp, halfRight, halfForward, fillColor);
                DrawSolidRectangle(center - halfUp, halfRight, halfForward, MultiplyAlpha(fillColor, 0.65f));
                DrawSolidRectangle(center, halfRight, halfUp, MultiplyAlpha(fillColor, 0.42f));

                Matrix4x4 matrix = Matrix4x4.TRS(center, transform.rotation, size);
                using (new Handles.DrawingScope(GetShapeWireColor(selected), matrix))
                {
                    Handles.DrawWireCube(Vector3.zero, Vector3.one);
                }
            }

            Vector3 halfSize = size * 0.5f;
            return new GizmoMetrics(
                center,
                ProjectOrientedBoxExtent(right, up, forward, halfSize, context.LabelDirection),
                ProjectOrientedBoxExtent(right, up, forward, halfSize, context.FacingDirection),
                ProjectOrientedBoxExtent(right, up, forward, halfSize, context.SideDirection));
        }

        private static GizmoMetrics DrawSphereColliderPreview(SphereCollider sphereCollider, GizmoContext context, bool selected, bool drawShape)
        {
            Transform transform = sphereCollider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            float radius = Mathf.Max(sphereCollider.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)), MIN_MARKER_SIZE);
            Vector3 center = transform.TransformPoint(sphereCollider.center);

            if (drawShape)
            {
                DrawSpherePreview(center, transform, radius, selected);
            }

            return new GizmoMetrics(center, radius, radius, radius);
        }

        private static GizmoMetrics DrawBoundsPreview(Bounds bounds, GizmoContext context, bool selected, bool drawShape)
        {
            if (bounds.size.sqrMagnitude <= MIN_MARKER_SIZE * MIN_MARKER_SIZE)
            {
                return new GizmoMetrics(bounds.center, DEFAULT_MARKER_RADIUS, DEFAULT_MARKER_RADIUS, DEFAULT_MARKER_RADIUS);
            }

            if (drawShape)
            {
                Vector3 halfRight = Vector3.right * bounds.extents.x;
                Vector3 halfUp = Vector3.up * bounds.extents.y;
                Vector3 halfForward = Vector3.forward * bounds.extents.z;
                Color fillColor = GetShapeFillColor(selected);

                DrawSolidRectangle(bounds.center + halfUp, halfRight, halfForward, fillColor);
                DrawSolidRectangle(bounds.center - halfUp, halfRight, halfForward, MultiplyAlpha(fillColor, 0.65f));

                using (new Handles.DrawingScope(GetShapeWireColor(selected)))
                {
                    Handles.DrawWireCube(bounds.center, bounds.size);
                }
            }

            return new GizmoMetrics(
                bounds.center,
                ProjectBoundsExtent(bounds, context.LabelDirection),
                ProjectBoundsExtent(bounds, context.FacingDirection),
                ProjectBoundsExtent(bounds, context.SideDirection));
        }

        private static GizmoMetrics DrawBoxCollider2DPreview(BoxCollider2D boxCollider, GizmoContext context, bool selected, bool drawShape)
        {
            Transform transform = boxCollider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            Vector2 scaledSize = new Vector2(
                Mathf.Max(boxCollider.size.x * scale.x, MIN_MARKER_SIZE),
                Mathf.Max(boxCollider.size.y * scale.y, MIN_MARKER_SIZE));

            Vector3 center = transform.TransformPoint(boxCollider.offset);
            Vector3 right = SafeNormalize(transform.right, Vector3.right);
            Vector3 up = SafeNormalize(transform.up, Vector3.up);
            Vector3 halfRight = right * (scaledSize.x * 0.5f);
            Vector3 halfUp = up * (scaledSize.y * 0.5f);

            if (drawShape)
            {
                DrawSolidRectangle(center, halfRight, halfUp, GetShapeFillColor(selected));
                DrawRectangleOutline(center, halfRight, halfUp, GetShapeWireColor(selected), selected ? 2.4f : 1.6f);
            }

            return new GizmoMetrics(
                center,
                ProjectOrientedRectExtent(right, up, scaledSize * 0.5f, context.LabelDirection),
                ProjectOrientedRectExtent(right, up, scaledSize * 0.5f, context.FacingDirection),
                ProjectOrientedRectExtent(right, up, scaledSize * 0.5f, context.SideDirection));
        }

        private static GizmoMetrics DrawCircleCollider2DPreview(CircleCollider2D circleCollider, GizmoContext context, bool selected, bool drawShape)
        {
            Transform transform = circleCollider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            float radius = Mathf.Max(circleCollider.radius * Mathf.Max(scale.x, scale.y), MIN_MARKER_SIZE);
            Vector3 center = transform.TransformPoint(circleCollider.offset);

            if (drawShape)
            {
                using (new Handles.DrawingScope(GetShapeFillColor(selected)))
                {
                    Handles.DrawSolidDisc(center, context.PlaneNormal, radius);
                }

                using (new Handles.DrawingScope(GetShapeWireColor(selected)))
                {
                    Handles.DrawWireDisc(center, context.PlaneNormal, radius);
                }
            }

            return new GizmoMetrics(center, radius, radius, radius);
        }

        private static GizmoMetrics DrawCapsuleCollider2DPreview(CapsuleCollider2D capsuleCollider, GizmoContext context, bool selected, bool drawShape)
        {
            Transform transform = capsuleCollider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            Vector2 size = new Vector2(
                Mathf.Max(capsuleCollider.size.x * scale.x, MIN_MARKER_SIZE),
                Mathf.Max(capsuleCollider.size.y * scale.y, MIN_MARKER_SIZE));

            bool vertical = capsuleCollider.direction == CapsuleDirection2D.Vertical;
            Vector3 axis = vertical ? SafeNormalize(transform.up, Vector3.up) : SafeNormalize(transform.right, Vector3.right);
            Vector3 perpendicular = vertical ? SafeNormalize(transform.right, Vector3.right) : SafeNormalize(transform.up, Vector3.up);
            float radius = Mathf.Max((vertical ? size.x : size.y) * 0.5f, MIN_MARKER_SIZE);
            float height = Mathf.Max(vertical ? size.y : size.x, radius * 2f);
            Vector3 center = transform.TransformPoint(capsuleCollider.offset);

            if (drawShape)
            {
                DrawCapsule2DPreview(center, axis, perpendicular, context.PlaneNormal, radius, height, selected);
            }

            return CreateCapsuleMetrics(context, center, axis, radius, height);
        }

        private static GizmoMetrics DrawCollider2DBoundsPreview(Collider2D collider, GizmoContext context, bool selected, bool drawShape)
        {
            return DrawBoundsPreview(collider.bounds, context, selected, drawShape);
        }

        private static GizmoMetrics DrawDefaultMarkerPreview(Transform transform, GizmoContext context, bool selected, bool drawShape)
        {
            Vector3 center = transform.position;

            if (drawShape)
            {
                using (new Handles.DrawingScope(GetShapeFillColor(selected)))
                {
                    Handles.DrawSolidDisc(center, context.PlaneNormal, DEFAULT_MARKER_RADIUS);
                }

                using (new Handles.DrawingScope(GetShapeWireColor(selected)))
                {
                    Handles.DrawWireDisc(center, context.PlaneNormal, DEFAULT_MARKER_RADIUS);
                    Handles.DrawWireDisc(center, context.SideDirection, DEFAULT_MARKER_RADIUS);
                    Handles.DrawWireDisc(center, context.FacingDirection, DEFAULT_MARKER_RADIUS);
                }
            }

            return new GizmoMetrics(center, DEFAULT_MARKER_RADIUS, DEFAULT_MARKER_RADIUS, DEFAULT_MARKER_RADIUS);
        }

        private static void DrawCapsulePreview(
            Vector3 center,
            Vector3 axis,
            Vector3 firstPerpendicular,
            Vector3 secondPerpendicular,
            float radius,
            float height,
            bool selected)
        {
            float straightHalfHeight = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 top = center + axis * straightHalfHeight;
            Vector3 bottom = center - axis * straightHalfHeight;
            Color fillColor = GetShapeFillColor(selected);
            Color wireColor = GetShapeWireColor(selected);

            using (new Handles.DrawingScope(fillColor))
            {
                Handles.DrawSolidDisc(top, axis, radius);
                Handles.DrawSolidDisc(bottom, axis, radius);

                if (straightHalfHeight > MIN_MARKER_SIZE)
                {
                    DrawSolidRectangle((top + bottom) * 0.5f, firstPerpendicular * radius, axis * straightHalfHeight, MultiplyAlpha(fillColor, 0.48f));
                    DrawSolidRectangle((top + bottom) * 0.5f, secondPerpendicular * radius, axis * straightHalfHeight, MultiplyAlpha(fillColor, 0.35f));
                }
            }

            using (new Handles.DrawingScope(wireColor))
            {
                Handles.DrawWireDisc(top, axis, radius);
                Handles.DrawWireDisc(bottom, axis, radius);
                DrawLine(bottom + firstPerpendicular * radius, top + firstPerpendicular * radius, selected ? 2.4f : 1.6f);
                DrawLine(bottom - firstPerpendicular * radius, top - firstPerpendicular * radius, selected ? 2.4f : 1.6f);
                DrawLine(bottom + secondPerpendicular * radius, top + secondPerpendicular * radius, selected ? 2.0f : 1.25f);
                DrawLine(bottom - secondPerpendicular * radius, top - secondPerpendicular * radius, selected ? 2.0f : 1.25f);

                Handles.DrawWireArc(top, secondPerpendicular, firstPerpendicular, 180f, radius);
                Handles.DrawWireArc(bottom, secondPerpendicular, firstPerpendicular, -180f, radius);
                Handles.DrawWireArc(top, firstPerpendicular, secondPerpendicular, -180f, radius);
                Handles.DrawWireArc(bottom, firstPerpendicular, secondPerpendicular, 180f, radius);
            }
        }

        private static void DrawCapsule2DPreview(
            Vector3 center,
            Vector3 axis,
            Vector3 perpendicular,
            Vector3 planeNormal,
            float radius,
            float height,
            bool selected)
        {
            float straightHalfHeight = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 top = center + axis * straightHalfHeight;
            Vector3 bottom = center - axis * straightHalfHeight;
            Color fillColor = GetShapeFillColor(selected);
            Color wireColor = GetShapeWireColor(selected);

            using (new Handles.DrawingScope(fillColor))
            {
                Handles.DrawSolidDisc(top, planeNormal, radius);
                Handles.DrawSolidDisc(bottom, planeNormal, radius);
                if (straightHalfHeight > MIN_MARKER_SIZE)
                {
                    DrawSolidRectangle(center, perpendicular * radius, axis * straightHalfHeight, MultiplyAlpha(fillColor, 0.72f));
                }
            }

            using (new Handles.DrawingScope(wireColor))
            {
                Handles.DrawWireDisc(top, planeNormal, radius);
                Handles.DrawWireDisc(bottom, planeNormal, radius);
                DrawLine(bottom + perpendicular * radius, top + perpendicular * radius, selected ? 2.4f : 1.6f);
                DrawLine(bottom - perpendicular * radius, top - perpendicular * radius, selected ? 2.4f : 1.6f);
            }
        }

        private static void DrawSpherePreview(Vector3 center, Transform transform, float radius, bool selected)
        {
            Vector3 up = SafeNormalize(transform.up, Vector3.up);
            Vector3 right = SafeNormalize(transform.right, Vector3.right);
            Vector3 forward = SafeNormalize(transform.forward, Vector3.forward);
            Color fillColor = GetShapeFillColor(selected);
            Color wireColor = GetShapeWireColor(selected);

            using (new Handles.DrawingScope(fillColor))
            {
                Handles.DrawSolidDisc(center, up, radius);
                Handles.DrawSolidDisc(center, right, radius * 0.82f);
            }

            using (new Handles.DrawingScope(wireColor))
            {
                Handles.DrawWireDisc(center, up, radius);
                Handles.DrawWireDisc(center, right, radius);
                Handles.DrawWireDisc(center, forward, radius);
            }
        }

        private static void DrawSpawnAnchor(Transform transform, GizmoContext context, GizmoMetrics metrics, bool selected)
        {
            float radius = Mathf.Clamp(Mathf.Max(metrics.SideExtent, metrics.FacingExtent) * 0.18f, 0.16f, 0.48f);
            Vector3 position = transform.position;

            using (new Handles.DrawingScope(selected ? SpawnAnchorFillColor : MultiplyAlpha(SpawnAnchorFillColor, 0.62f)))
            {
                Handles.DrawSolidDisc(position, context.PlaneNormal, radius * 0.52f);
            }

            using (new Handles.DrawingScope(selected ? SpawnAnchorWireColor : MultiplyAlpha(SpawnAnchorWireColor, 0.72f)))
            {
                Handles.DrawWireDisc(position, context.PlaneNormal, radius);
            }
        }

        private static void DrawFacingArrow(PlayerStart playerStart, GizmoContext context, GizmoMetrics metrics, bool selected)
        {
            float arrowLength = ResolveArrowLength(playerStart, metrics);
            float headSize = Mathf.Clamp(arrowLength * 0.2f, MIN_ARROW_HEAD_SIZE, MAX_ARROW_HEAD_SIZE);
            Vector3 start = playerStart.transform.position;
            Vector3 tip = start + context.FacingDirection * arrowLength;
            Vector3 shaftEnd = tip - context.FacingDirection * (headSize * 0.46f);

            DrawLine(start, shaftEnd, selected ? 8.2f : 6.2f, ArrowShadowColor);
            DrawLine(start, shaftEnd, selected ? 5.2f : 3.8f, selected ? ArrowHeadColor : ArrowShaftColor);

            using (new Handles.DrawingScope(ArrowHeadColor))
            {
                Handles.ConeHandleCap(0, tip, CreateArrowRotation(context), headSize, EventType.Repaint);
            }

            using (new Handles.DrawingScope(MultiplyAlpha(ArrowShaftColor, selected ? 0.45f : 0.32f)))
            {
                Handles.DrawSolidDisc(start, context.PlaneNormal, headSize * 0.22f);
            }
        }

        private static void DrawSelectedLabel(PlayerStart playerStart, GizmoContext context, GizmoMetrics metrics)
        {
            string label = playerStart.GetName();
            if (string.IsNullOrEmpty(label))
            {
                label = playerStart.gameObject.name;
            }

            Vector3 position = playerStart.transform.position + context.LabelDirection * (metrics.LabelExtent + LABEL_OFFSET);
            Handles.Label(position, label, EditorStyles.boldLabel);
        }

        private static GizmoContext CreateGizmoContext(PlayerStart playerStart)
        {
            PlayerStart.EGizmoMode mode = ResolveGizmoMode(playerStart);
            PlayerStart.EFacingAxis facingAxis = ResolveFacingAxis(playerStart, mode);
            Transform transform = playerStart.transform;

            Vector3 facingDirection = ResolveFacingDirection(transform, facingAxis);
            Vector3 planeNormal = mode == PlayerStart.EGizmoMode.ThreeDimensional
                ? SafeNormalize(transform.up, Vector3.up)
                : SafeNormalize(transform.forward, Vector3.forward);
            Vector3 sideDirection = SafeNormalize(Vector3.Cross(planeNormal, facingDirection), SafeNormalize(transform.right, Vector3.right));
            Vector3 labelDirection = mode == PlayerStart.EGizmoMode.ThreeDimensional
                ? SafeNormalize(transform.up, Vector3.up)
                : SafeNormalize(transform.up, Vector3.up);

            return new GizmoContext(mode, facingAxis, planeNormal, facingDirection, sideDirection, labelDirection);
        }

        private static PlayerStart.EGizmoMode ResolveGizmoMode(PlayerStart playerStart)
        {
            PlayerStart.EGizmoMode mode = playerStart.GetGizmoMode();
            if (mode != PlayerStart.EGizmoMode.Auto)
            {
                return mode;
            }

            bool has2DCollider = playerStart.TryGetComponent(out Collider2D _);
            bool has3DCollider = playerStart.TryGetComponent(out Collider _) || playerStart.TryGetComponent(out CharacterController _);
            if (has2DCollider && !has3DCollider)
            {
                return PlayerStart.EGizmoMode.SideScroller2D;
            }

            return PlayerStart.EGizmoMode.ThreeDimensional;
        }

        private static PlayerStart.EFacingAxis ResolveFacingAxis(PlayerStart playerStart, PlayerStart.EGizmoMode mode)
        {
            PlayerStart.EFacingAxis facingAxis = playerStart.GetGizmoFacingAxis();
            if (facingAxis != PlayerStart.EFacingAxis.Auto)
            {
                return facingAxis;
            }

            switch (mode)
            {
                case PlayerStart.EGizmoMode.SideScroller2D:
                    return PlayerStart.EFacingAxis.TransformRight;
                case PlayerStart.EGizmoMode.TopDown2D:
                    return PlayerStart.EFacingAxis.TransformUp;
                default:
                    return PlayerStart.EFacingAxis.TransformForward;
            }
        }

        private static Vector3 ResolveFacingDirection(Transform transform, PlayerStart.EFacingAxis facingAxis)
        {
            switch (facingAxis)
            {
                case PlayerStart.EFacingAxis.TransformRight:
                    return SafeNormalize(transform.right, Vector3.right);
                case PlayerStart.EFacingAxis.TransformUp:
                    return SafeNormalize(transform.up, Vector3.up);
                case PlayerStart.EFacingAxis.NegativeTransformForward:
                    return SafeNormalize(-transform.forward, Vector3.back);
                case PlayerStart.EFacingAxis.NegativeTransformRight:
                    return SafeNormalize(-transform.right, Vector3.left);
                case PlayerStart.EFacingAxis.NegativeTransformUp:
                    return SafeNormalize(-transform.up, Vector3.down);
                default:
                    return SafeNormalize(transform.forward, Vector3.forward);
            }
        }

        private static float ResolveArrowLength(PlayerStart playerStart, GizmoMetrics metrics)
        {
            float overrideLength = playerStart.GetGizmoArrowLengthOverride();
            if (overrideLength > 0f)
            {
                return Mathf.Max(MIN_ARROW_LENGTH, overrideLength);
            }

            float automaticLength = metrics.FacingExtent + Mathf.Max(metrics.SideExtent * 0.55f, 0.55f);
            automaticLength = Mathf.Clamp(automaticLength, MIN_ARROW_LENGTH, MAX_AUTO_ARROW_LENGTH);
            return Mathf.Max(MIN_ARROW_LENGTH, automaticLength * playerStart.GetGizmoArrowLengthScale());
        }

        private static Quaternion CreateArrowRotation(GizmoContext context)
        {
            Vector3 up = context.PlaneNormal;
            if (Mathf.Abs(Vector3.Dot(context.FacingDirection, up)) > 0.96f)
            {
                up = context.SideDirection;
            }

            return Quaternion.LookRotation(context.FacingDirection, up);
        }

        private static void GetCapsuleAxes(
            Transform transform,
            int direction,
            out Vector3 axis,
            out Vector3 firstPerpendicular,
            out Vector3 secondPerpendicular,
            out float axisScale,
            out float radiusScale)
        {
            Vector3 scale = Abs(transform.lossyScale);
            switch (direction)
            {
                case 0:
                    axis = SafeNormalize(transform.right, Vector3.right);
                    firstPerpendicular = SafeNormalize(transform.up, Vector3.up);
                    secondPerpendicular = SafeNormalize(transform.forward, Vector3.forward);
                    axisScale = scale.x;
                    radiusScale = Mathf.Max(scale.y, scale.z);
                    break;
                case 2:
                    axis = SafeNormalize(transform.forward, Vector3.forward);
                    firstPerpendicular = SafeNormalize(transform.right, Vector3.right);
                    secondPerpendicular = SafeNormalize(transform.up, Vector3.up);
                    axisScale = scale.z;
                    radiusScale = Mathf.Max(scale.x, scale.y);
                    break;
                default:
                    axis = SafeNormalize(transform.up, Vector3.up);
                    firstPerpendicular = SafeNormalize(transform.right, Vector3.right);
                    secondPerpendicular = SafeNormalize(transform.forward, Vector3.forward);
                    axisScale = scale.y;
                    radiusScale = Mathf.Max(scale.x, scale.z);
                    break;
            }
        }

        private static GizmoMetrics CreateCapsuleMetrics(GizmoContext context, Vector3 center, Vector3 axis, float radius, float height)
        {
            return new GizmoMetrics(
                center,
                GetCapsuleProjectedExtent(axis, context.LabelDirection, radius, height),
                GetCapsuleProjectedExtent(axis, context.FacingDirection, radius, height),
                GetCapsuleProjectedExtent(axis, context.SideDirection, radius, height));
        }

        private static float GetCapsuleProjectedExtent(Vector3 capsuleAxis, Vector3 targetAxis, float radius, float height)
        {
            float straightHalfHeight = Mathf.Max(0f, height * 0.5f - radius);
            return straightHalfHeight * Mathf.Abs(Vector3.Dot(capsuleAxis, SafeNormalize(targetAxis, Vector3.up))) + radius;
        }

        private static float ProjectBoundsExtent(Bounds bounds, Vector3 axis)
        {
            axis = SafeNormalize(axis, Vector3.up);
            Vector3 extents = bounds.extents;
            return Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
        }

        private static float ProjectOrientedBoxExtent(Vector3 right, Vector3 up, Vector3 forward, Vector3 halfSize, Vector3 axis)
        {
            axis = SafeNormalize(axis, Vector3.forward);
            return Mathf.Abs(Vector3.Dot(right, axis)) * halfSize.x
                + Mathf.Abs(Vector3.Dot(up, axis)) * halfSize.y
                + Mathf.Abs(Vector3.Dot(forward, axis)) * halfSize.z;
        }

        private static float ProjectOrientedRectExtent(Vector3 right, Vector3 up, Vector2 halfSize, Vector3 axis)
        {
            axis = SafeNormalize(axis, Vector3.right);
            return Mathf.Abs(Vector3.Dot(right, axis)) * halfSize.x
                + Mathf.Abs(Vector3.Dot(up, axis)) * halfSize.y;
        }

        private static void DrawSolidRectangle(Vector3 center, Vector3 halfA, Vector3 halfB, Color fillColor)
        {
            RectanglePoints[0] = center - halfA - halfB;
            RectanglePoints[1] = center - halfA + halfB;
            RectanglePoints[2] = center + halfA + halfB;
            RectanglePoints[3] = center + halfA - halfB;
            Handles.DrawSolidRectangleWithOutline(RectanglePoints, fillColor, ClearOutlineColor);
        }

        private static void DrawRectangleOutline(Vector3 center, Vector3 halfA, Vector3 halfB, Color color, float width)
        {
            PentagonPoints[0] = center - halfA - halfB;
            PentagonPoints[1] = center - halfA + halfB;
            PentagonPoints[2] = center + halfA + halfB;
            PentagonPoints[3] = center + halfA - halfB;
            PentagonPoints[4] = PentagonPoints[0];
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(width, PentagonPoints);
            }
        }

        private static void DrawLine(Vector3 from, Vector3 to, float width)
        {
            LinePoints[0] = from;
            LinePoints[1] = to;
            Handles.DrawAAPolyLine(width, LinePoints);
        }

        private static void DrawLine(Vector3 from, Vector3 to, float width, Color color)
        {
            using (new Handles.DrawingScope(color))
            {
                DrawLine(from, to, width);
            }
        }

        private static Color GetShapeFillColor(bool selected)
        {
            return selected ? ShapeFillSelectedColor : ShapeFillColor;
        }

        private static Color GetShapeWireColor(bool selected)
        {
            return selected ? ShapeWireSelectedColor : ShapeWireColor;
        }

        private static Color MultiplyAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static Vector3 Max(Vector3 value, float minimum)
        {
            return new Vector3(
                Mathf.Max(value.x, minimum),
                Mathf.Max(value.y, minimum),
                Mathf.Max(value.z, minimum));
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : fallback;
        }

        private readonly struct GizmoContext
        {
            public readonly PlayerStart.EGizmoMode Mode;
            public readonly PlayerStart.EFacingAxis FacingAxis;
            public readonly Vector3 PlaneNormal;
            public readonly Vector3 FacingDirection;
            public readonly Vector3 SideDirection;
            public readonly Vector3 LabelDirection;

            public GizmoContext(
                PlayerStart.EGizmoMode mode,
                PlayerStart.EFacingAxis facingAxis,
                Vector3 planeNormal,
                Vector3 facingDirection,
                Vector3 sideDirection,
                Vector3 labelDirection)
            {
                Mode = mode;
                FacingAxis = facingAxis;
                PlaneNormal = planeNormal;
                FacingDirection = facingDirection;
                SideDirection = sideDirection;
                LabelDirection = labelDirection;
            }

            public bool Is2D => Mode == PlayerStart.EGizmoMode.SideScroller2D || Mode == PlayerStart.EGizmoMode.TopDown2D;
        }

        private readonly struct GizmoMetrics
        {
            public readonly Vector3 Center;
            public readonly float LabelExtent;
            public readonly float FacingExtent;
            public readonly float SideExtent;

            public GizmoMetrics(Vector3 center, float labelExtent, float facingExtent, float sideExtent)
            {
                Center = center;
                LabelExtent = Mathf.Max(labelExtent, DEFAULT_MARKER_RADIUS);
                FacingExtent = Mathf.Max(facingExtent, DEFAULT_MARKER_RADIUS);
                SideExtent = Mathf.Max(sideExtent, DEFAULT_MARKER_RADIUS);
            }
        }
    }
}
