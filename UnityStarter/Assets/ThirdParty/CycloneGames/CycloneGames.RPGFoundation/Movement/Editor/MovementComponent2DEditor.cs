using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;

namespace CycloneGames.RPGFoundation.Movement.Editor
{
    [CustomEditor(typeof(MovementComponent2D))]
    [CanEditMultipleObjects]
    public class MovementComponent2DEditor : UnityEditor.Editor
    {
        private static readonly GUIContent IgnoreTimeScaleLabel = new GUIContent(
            "Ignore Time Scale",
            "Ignore global Time.timeScale for this character.\n" +
            "When enabled, uses Time.unscaledDeltaTime instead of Time.deltaTime.\n" +
            "Can be changed at runtime via IgnoreTimeScale.");

        private static readonly GUIContent TimeScaleIgnored = new GUIContent(
            "Time Scale Ignored:\n" +
            "• Character moves at normal speed even when Time.timeScale changes\n" +
            "• Use for: UI characters, cutscene characters, pause-resistant movement\n" +
            "• Can be changed at runtime: movementComponent.IgnoreTimeScale = true/false\n" +
            "• LocalTimeScale still applies for per-character speed control");

        private static readonly GUIContent TimeScaleAffected = new GUIContent(
            "Time Scale Affected:\n" +
            "• Character movement respects global Time.timeScale\n" +
            "• Use for: Normal gameplay characters\n" +
            "• Slow motion effects will affect this character\n" +
            "• Can be changed at runtime: movementComponent.IgnoreTimeScale = true/false\n" +
            "• LocalTimeScale can still be used for per-character speed control");

        private SerializedProperty _config;
        private SerializedProperty _ignoreTimeScale;
        private bool _showTimeScaleHelp;
        private bool _showQuickSetup = true;

        private readonly Dictionary<string, GUIContent> _statusCache =
            new Dictionary<string, GUIContent>();
        private GUIStyle _statusOkStyle;
        private GUIStyle _statusWarningStyle;

        private void OnEnable()
        {
            _config = serializedObject.FindProperty("config");
            _ignoreTimeScale = serializedObject.FindProperty("ignoreTimeScale");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_config);

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Movement publishes one immutable animation frame per simulation step.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "Bind a backend with BindAnimation(locomotionSink, actionSink) and root motion with BindRootMotion(source).",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "Parameter names belong to the backend, not to this component.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_ignoreTimeScale, IgnoreTimeScaleLabel);
            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showTimeScaleHelp = EditorGUILayout.Foldout(_showTimeScaleHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showTimeScaleHelp)
            {
                EditorGUILayout.HelpBox(
                    (_ignoreTimeScale.boolValue ? TimeScaleIgnored : TimeScaleAffected).text,
                    _ignoreTimeScale.boolValue ? MessageType.Info : MessageType.None);
            }
            EditorGUILayout.EndVertical();

            DrawQuickSetup();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawQuickSetup()
        {
            var component = (MovementComponent2D)target;
            Rigidbody2D body = component.GetComponent<Rigidbody2D>();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);

            if (body == null)
            {
                EditorGUILayout.HelpBox(
                    "Rigidbody2D is required. Unity adds it through RequireComponent when " +
                    "the component is created.", MessageType.Warning);
                return;
            }

            bool continuous = body.collisionDetectionMode == CollisionDetectionMode2D.Continuous;
            bool interpolated = body.interpolation == RigidbodyInterpolation2D.Interpolate;
            bool frozen = body.constraints == RigidbodyConstraints2D.FreezeRotation;
            bool needsSetup = !continuous || !interpolated || !frozen;

            _showQuickSetup = EditorGUILayout.Foldout(_showQuickSetup, "Physics Components", true);
            if (!_showQuickSetup) return;

            EditorGUILayout.LabelField("Rigidbody2D", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawStatus("Collision Detection", continuous,
                continuous ? "Continuous" : CollisionName(body.collisionDetectionMode), "Continuous");
            DrawStatus("Interpolate", interpolated,
                interpolated ? "Interpolate" : InterpolationName(body.interpolation), "Interpolate");
            DrawStatus("Freeze Rotation", frozen,
                frozen ? "FreezeRotation" : ConstraintsName(body.constraints), "FreezeRotation");
            EditorGUI.indentLevel--;

            if (needsSetup && GUILayout.Button("Apply Recommended Rigidbody2D Settings", GUILayout.Height(22)))
            {
                Undo.RecordObject(body, "Apply Recommended Rigidbody2D Settings");
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
            }

            EditorGUILayout.LabelField(
                "Continuous collision detection avoids tunneling; interpolation smooths the render frame.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "The component keeps gravity in its own simulation step.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawStatus(string label, bool isCorrect, string current, string recommended)
        {
            EnsureStatusStyles();
            EditorGUILayout.LabelField(CachedStatus(label, isCorrect),
                isCorrect ? _statusOkStyle : _statusWarningStyle);
            if (isCorrect) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(CachedDetail(current, recommended), EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        private void EnsureStatusStyles()
        {
            if (_statusOkStyle != null) return;
            _statusOkStyle = new GUIStyle(EditorStyles.label);
            _statusOkStyle.normal.textColor = new Color(0.39f, 0.78f, 0.39f);
            _statusWarningStyle = new GUIStyle(EditorStyles.label);
            _statusWarningStyle.normal.textColor = new Color(0.95f, 0.64f, 0.24f);
        }

        private GUIContent CachedStatus(string label, bool isCorrect)
        {
            string key = (isCorrect ? "1" : "0") + label;
            GUIContent cached;
            if (_statusCache.TryGetValue(key, out cached)) return cached;
            cached = new GUIContent((isCorrect ? "✓ " : "✗ ") + label);
            _statusCache.Add(key, cached);
            return cached;
        }

        private GUIContent CachedDetail(string current, string recommended)
        {
            string key = current + "|" + recommended;
            GUIContent cached;
            if (_statusCache.TryGetValue(key, out cached)) return cached;
            cached = new GUIContent("Current: " + current + " -> Recommended: " + recommended);
            _statusCache.Add(key, cached);
            return cached;
        }

        private static string CollisionName(CollisionDetectionMode2D value)
        {
            if (value == CollisionDetectionMode2D.Discrete) return "Discrete";
            if (value == CollisionDetectionMode2D.Continuous) return "Continuous";
            return "Custom";
        }

        private static string InterpolationName(RigidbodyInterpolation2D value)
        {
            if (value == RigidbodyInterpolation2D.None) return "None";
            if (value == RigidbodyInterpolation2D.Interpolate) return "Interpolate";
            if (value == RigidbodyInterpolation2D.Extrapolate) return "Extrapolate";
            return "Custom";
        }

        private static string ConstraintsName(RigidbodyConstraints2D value)
        {
            if (value == RigidbodyConstraints2D.None) return "None";
            if (value == RigidbodyConstraints2D.FreezeRotation) return "FreezeRotation";
            return "Custom";
        }
    }
}
