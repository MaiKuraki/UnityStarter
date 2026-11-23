using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Editor
{
    [CustomEditor(typeof(MovementComponent))]
    [CanEditMultipleObjects]
    public class MovementComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _config;
        private SerializedProperty _worldUpSource;
        private SerializedProperty _useRootMotion;
        private SerializedProperty _ignoreTimeScale;
#if UNITY_EDITOR
        private SerializedProperty _showDebugGizmos;
        private SerializedProperty _showGroundDetection;
        private SerializedProperty _showCollisionSweep;
        private SerializedProperty _showVelocity;
        
        private SerializedProperty _debugConfigRunSpeed;
        private SerializedProperty _debugCurrentSpeed;
        private SerializedProperty _debugDeltaTime;
        private SerializedProperty _debugCurrentState;
        private SerializedProperty _debugConfigAssigned;
        private SerializedProperty _debugInputDirection;
        private SerializedProperty _debugInputMagnitude;
        private SerializedProperty _debugActualVelocity;
        private SerializedProperty _debugActualSpeed;
        
        private SerializedProperty _debugOnPlatform;
        private SerializedProperty _debugPlatformName;
        private SerializedProperty _debugPlatformVelocity;
        private SerializedProperty _debugGroundColliderName;
        private SerializedProperty _debugGroundHasRigidbody;
        private SerializedProperty _debugGroundLayerMatch;
        private SerializedProperty _debugPlatformDeltaPos;
        private SerializedProperty _debugLocalPosition;
        
        private bool _showDebugVisualization = true;
        private bool _showRuntimeDebug = true;
        private bool _showPlatformDebug = true;
#endif

        private readonly Dictionary<(string, bool), GUIContent> _statusLabelCache =
            new Dictionary<(string, bool), GUIContent>();
        private readonly Dictionary<string, GUIContent> _contentCache =
            new Dictionary<string, GUIContent>();
        private readonly Dictionary<(string, string), GUIContent> _detailLineCache =
            new Dictionary<(string, string), GUIContent>();
        private readonly Dictionary<(float, float), GUIContent> _numericDetailLineCache =
            new Dictionary<(float, float), GUIContent>();

        private GUIContent CachedDetailLine(string current, string recommended, string fullRecommended)
        {
            (string, string) key = (current, recommended);
            GUIContent content;
            if (_detailLineCache.TryGetValue(key, out content)) return content;
            content = new GUIContent(
                CurrentTag + ": " + current + "  \u2192  " + RecommendedTag + ": " + recommended,
                "Recommended full value: " + (fullRecommended ?? recommended) +
                ". Use the Apply button below to change this setting.");
            _detailLineCache.Add(key, content);
            return content;
        }

        private GUIContent CachedNumericDetailLine(float current, float recommended)
        {
            (float, float) key = (current, recommended);
            GUIContent content;
            if (_numericDetailLineCache.TryGetValue(key, out content)) return content;
            content = new GUIContent(
                CurrentTag + ": " + current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "  \u2192  " + RecommendedTag + ": " +
                recommended.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                "Use the Apply button below to change this setting.");
            _numericDetailLineCache.Add(key, content);
            return content;
        }

        private GUIContent CachedStatusLabel(string label, bool isCorrect, string fullLabel)
        {
            (string, bool) key = (label, isCorrect);
            GUIContent content;
            if (_statusLabelCache.TryGetValue(key, out content)) return content;
            string display = (isCorrect ? "\u2713 " : "\u2717 ") + label;
            string explanation = isCorrect
                ? "Matches the recommended setting."
                : "Needs change. The line below shows the Current and Recommended values.";
            content = new GUIContent(display, (fullLabel ?? label) + " | " + explanation);
            _statusLabelCache.Add(key, content);
            return content;
        }

        private GUIContent CachedContent(string text, string tooltip)
        {
            GUIContent content;
            if (_contentCache.TryGetValue(text, out content)) return content;
            content = new GUIContent(text, tooltip ?? string.Empty);
            _contentCache.Add(text, content);
            return content;
        }
        private GUIStyle _statusOkStyle;
        private GUIStyle _statusWarningStyle;

        private bool _showTimeScaleHelp = false;
        private bool _showWorldUpHelp = false;
        private bool _showQuickSetup = false;

        private static readonly string[] ExcludedProperties = new string[]
        {
            "config", "worldUpSource",
            "useRootMotion", "ignoreTimeScale", "smoothPlatformFollow",
            "showDebugGizmos", "showGroundDetection", "showCollisionSweep", "showVelocity",
            "_debugConfigRunSpeed", "_debugCurrentSpeed", "_debugDeltaTime", "_debugCurrentState",
            "_debugConfigAssigned", "_debugInputDirection", "_debugInputMagnitude",
            "_debugActualVelocity", "_debugActualSpeed",
            "_debugOnPlatform", "_debugPlatformName", "_debugPlatformVelocity",
            "_debugGroundColliderName", "_debugGroundHasRigidbody", "_debugGroundLayerMatch",
            "_debugPlatformDeltaPos", "_debugLocalPosition",
            "m_Script"
        };

        private Color GetEmphasisColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.85f, 0.85f, 0.85f)
                : new Color(0.25f, 0.25f, 0.25f);
        }

        private void OnEnable()
        {
            _config = serializedObject.FindProperty("config");
            _worldUpSource = serializedObject.FindProperty("worldUpSource");
            _useRootMotion = serializedObject.FindProperty("useRootMotion");
            _ignoreTimeScale = serializedObject.FindProperty("ignoreTimeScale");
#if UNITY_EDITOR
            _showDebugGizmos = serializedObject.FindProperty("showDebugGizmos");
            _showGroundDetection = serializedObject.FindProperty("showGroundDetection");
            _showCollisionSweep = serializedObject.FindProperty("showCollisionSweep");
            _showVelocity = serializedObject.FindProperty("showVelocity");
            
            _debugConfigRunSpeed = serializedObject.FindProperty("_debugConfigRunSpeed");
            _debugCurrentSpeed = serializedObject.FindProperty("_debugCurrentSpeed");
            _debugDeltaTime = serializedObject.FindProperty("_debugDeltaTime");
            _debugCurrentState = serializedObject.FindProperty("_debugCurrentState");
            _debugConfigAssigned = serializedObject.FindProperty("_debugConfigAssigned");
            _debugInputDirection = serializedObject.FindProperty("_debugInputDirection");
            _debugInputMagnitude = serializedObject.FindProperty("_debugInputMagnitude");
            _debugActualVelocity = serializedObject.FindProperty("_debugActualVelocity");
            _debugActualSpeed = serializedObject.FindProperty("_debugActualSpeed");
            
            _debugOnPlatform = serializedObject.FindProperty("_debugOnPlatform");
            _debugPlatformName = serializedObject.FindProperty("_debugPlatformName");
            _debugPlatformVelocity = serializedObject.FindProperty("_debugPlatformVelocity");
            _debugGroundColliderName = serializedObject.FindProperty("_debugGroundColliderName");
            _debugGroundHasRigidbody = serializedObject.FindProperty("_debugGroundHasRigidbody");
            _debugGroundLayerMatch = serializedObject.FindProperty("_debugGroundLayerMatch");
            _debugPlatformDeltaPos = serializedObject.FindProperty("_debugPlatformDeltaPos");
            _debugLocalPosition = serializedObject.FindProperty("_debugLocalPosition");
#endif

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_config);

            EditorGUILayout.Space(10);

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
            EditorGUILayout.PropertyField(_worldUpSource, CachedContent(
                "World Up Source",
                "Optional Transform to use as world up direction reference.\n" +
                "If assigned, character will use this Transform's up direction as the world up.\n" +
                "If null, uses Vector3.up (standard Unity world up)."));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showWorldUpHelp = EditorGUILayout.Foldout(_showWorldUpHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showWorldUpHelp)
            {
                EditorGUI.indentLevel++;
                if (_worldUpSource.objectReferenceValue != null)
                {
                    EditorGUILayout.HelpBox(
                        "World Up Source Assigned:\n" +
                        "• Character will use the assigned Transform's UP direction (Transform.up) as world up\n" +
                        "• WorldUp is updated every frame, supporting dynamic changes\n" +
                        "• Use cases:\n" +
                        "  - Characters on rotating/moving platforms: Assign the platform's Transform\n" +
                        "  - Wall-walking: Transform's UP must point along wall's normal (outward from wall)\n" +
                        "  - Ceiling-walking: Transform's UP must point downward\n" +
                        "  - Space games with rotating space stations\n" +
                        "• How it works:\n" +
                        "  - Character rotation aligns to WorldUp direction\n" +
                        "  - Gravity/vertical movement uses WorldUp direction\n" +
                        "  - Look rotation uses WorldUp as the up vector\n" +
                        "• Important for wall-walking:\n" +
                        "  The Transform's UP direction must align with the wall's normal.\n" +
                        "  You may need to manually rotate the Transform or use a helper script.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "World Up Source Not Assigned:\n" +
                        "• Character will use Vector3.up as the world up direction\n" +
                        "• This is the standard Unity behavior\n" +
                        "• Suitable for most games with standard gravity\n" +
                        "• When to use World Up Source:\n" +
                        "  - Characters on rotating platforms that should stay upright\n" +
                        "  - Games with non-standard gravity directions\n" +
                        "  - Space games where 'up' changes based on location\n" +
                        "  - Any scenario where the character's 'up' should follow a Transform",
                        MessageType.None);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_useRootMotion, CachedContent(
                "Use Root Motion",
                "Consume root motion deltas from the bound IRootMotionSource.\n" +
                "The delta is converted to velocity and resolved by the same physics step."));
            EditorGUILayout.LabelField(
                "Add AnimatorRootMotionSource on the GameObject that owns the Animator, then bind it with BindRootMotion(source).",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "Root motion never bypasses collision, gravity or replay.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_ignoreTimeScale, CachedContent(
                "Ignore Time Scale",
                "Ignore global Time.timeScale for this character.\n" +
                "When enabled, uses Time.unscaledDeltaTime instead of Time.deltaTime.\n" +
                "Can be changed at runtime via IgnoreTimeScale property for dynamic switching."));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showTimeScaleHelp = EditorGUILayout.Foldout(_showTimeScaleHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showTimeScaleHelp)
            {
                EditorGUI.indentLevel++;
                if (_ignoreTimeScale.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Time Scale Ignored:\n" +
                        "• Character will move at normal speed even if Time.timeScale is changed\n" +
                        "• Use for: UI characters, cutscene characters, pause-resistant movement\n" +
                        "• Can be changed at runtime: movementComponent.IgnoreTimeScale = true/false\n" +
                        "• LocalTimeScale still applies for per-character speed control",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Time Scale Affected:\n" +
                        "• Character movement respects global Time.timeScale\n" +
                        "• Use for: Normal gameplay characters\n" +
                        "• Slow motion effects will affect this character\n" +
                        "• Can be changed at runtime: movementComponent.IgnoreTimeScale = true/false\n" +
                        "• LocalTimeScale can still be used for per-character speed control",
                        MessageType.None);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            
            DrawQuickSetupSection();

#if UNITY_EDITOR
            EditorGUILayout.Space(10);
            
            DrawDebugSection();
#endif

            ValidateConfiguration();

            DrawAdditionalProperties();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawQuickSetupSection()
        {
            var component = target as MovementComponent;
            if (component == null) return;

            var rigidbody = component.GetComponent<Rigidbody>();
            var capsuleCollider = component.GetComponent<CapsuleCollider>();

            bool hasRigidbody = rigidbody != null;
            bool hasCapsule = capsuleCollider != null;

            const float recommendedHeight = 1.8f;
            const float recommendedRadius = 0.3f;
            Vector3 recommendedCenter = new Vector3(0f, 0f, 0f);

            bool rigidbodyNeedsSetup = hasRigidbody && (
                !rigidbody.isKinematic ||
                rigidbody.useGravity ||
                rigidbody.interpolation != RigidbodyInterpolation.Interpolate ||
                rigidbody.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative ||
                rigidbody.constraints != RigidbodyConstraints.FreezeRotation
            );

            bool capsuleNeedsSetup = hasCapsule && (
                Mathf.Abs(capsuleCollider.height - recommendedHeight) > 0.01f ||
                Mathf.Abs(capsuleCollider.radius - recommendedRadius) > 0.01f ||
                Vector3.Distance(capsuleCollider.center, recommendedCenter) > 0.01f ||
                capsuleCollider.direction != 1
            );

            Color headerColor = EditorGUIUtility.isProSkin
                ? new Color(0.4f, 0.8f, 0.4f)
                : new Color(0.2f, 0.6f, 0.2f);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            headerStyle.normal.textColor = headerColor;

            EditorGUILayout.LabelField("Quick Setup", headerStyle);

            Color bgColor;
            if (!hasRigidbody || !hasCapsule)
                bgColor = new Color(0.8f, 0.3f, 0.3f, 0.2f);
            else if (rigidbodyNeedsSetup || capsuleNeedsSetup)
                bgColor = new Color(0.8f, 0.6f, 0.2f, 0.2f);
            else
                bgColor = new Color(0.3f, 0.7f, 0.3f, 0.15f);

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            var foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            EditorGUI.indentLevel++;
            _showQuickSetup = EditorGUILayout.Foldout(_showQuickSetup, "Physics Components", true, foldoutStyle);
            EditorGUI.indentLevel--;

            if (_showQuickSetup)
            {
                EditorGUILayout.Space(3);

                if (!hasRigidbody || !hasCapsule)
                {
                    EditorGUILayout.HelpBox(
                        "Missing required components:\n" +
                        (!hasRigidbody ? "• Rigidbody\n" : "") +
                        (!hasCapsule ? "• CapsuleCollider" : ""),
                        MessageType.Error);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Add Missing Components", GUILayout.Width(180)))
                    {
                        Undo.RecordObject(component.gameObject, "Add Physics Components");
                        if (!hasRigidbody)
                        {
                            rigidbody = Undo.AddComponent<Rigidbody>(component.gameObject);
                            ApplyRecommendedRigidbodySettings(rigidbody);
                        }
                        if (!hasCapsule)
                        {
                            capsuleCollider = Undo.AddComponent<CapsuleCollider>(component.gameObject);
                            ApplyRecommendedCapsuleSettings(capsuleCollider);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.LabelField("Rigidbody", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    DrawSettingStatus("Is Kinematic", rigidbody.isKinematic, "true",
                        rigidbody.isKinematic ? "true" : "false");
                    DrawSettingStatus("Use Gravity", !rigidbody.useGravity, "false",
                        rigidbody.useGravity ? "true" : "false");
                    DrawSettingStatus("Interpolation", rigidbody.interpolation == RigidbodyInterpolation.Interpolate,
                        "Interp.", rigidbody.interpolation == RigidbodyInterpolation.Interpolate ? "Interp." : InterpolationName(rigidbody.interpolation),
                        "Interpolation", "RigidbodyInterpolation.Interpolate");
                    DrawSettingStatus("Collision", rigidbody.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative,
                        "Cont.", rigidbody.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative ? "Cont." : CollisionName(rigidbody.collisionDetectionMode),
                        "Collision Detection", "CollisionDetectionMode.ContinuousSpeculative");
                    DrawSettingStatus("Freeze Rot.", rigidbody.constraints == RigidbodyConstraints.FreezeRotation,
                        "Freeze", rigidbody.constraints == RigidbodyConstraints.FreezeRotation ? "Freeze" : ConstraintsName(rigidbody.constraints),
                        "Freeze Rotation", "RigidbodyConstraints.FreezeRotation");

                    EditorGUI.indentLevel--;

                    if (rigidbodyNeedsSetup)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(15);
                        if (GUILayout.Button("Apply Recommended Rigidbody Settings", GUILayout.Height(22)))
                        {
                            Undo.RecordObject(rigidbody, "Apply Recommended Rigidbody Settings");
                            ApplyRecommendedRigidbodySettings(rigidbody);
                        }
                        GUILayout.Space(15);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space(5);

                    EditorGUILayout.LabelField("CapsuleCollider", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    DrawNumericSettingStatus("Height",
                        Mathf.Abs(capsuleCollider.height - recommendedHeight) < 0.01f,
                        capsuleCollider.height, recommendedHeight);
                    DrawNumericSettingStatus("Radius",
                        Mathf.Abs(capsuleCollider.radius - recommendedRadius) < 0.01f,
                        capsuleCollider.radius, recommendedRadius);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(CachedContent("Center", "Recommended center: (0, 0, 0)"),
                        GUILayout.Width(58f));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Vector3Field(GUIContent.none, capsuleCollider.center,
                        GUILayout.ExpandWidth(true));
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                    DrawSettingStatus("Direction", capsuleCollider.direction == 1,
                        "Y-Axis", capsuleCollider.direction == 0 ? "X-Axis" : (capsuleCollider.direction == 1 ? "Y-Axis" : "Z-Axis"));

                    EditorGUI.indentLevel--;

                    if (capsuleNeedsSetup)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(15);
                        if (GUILayout.Button("Apply Recommended Capsule Settings", GUILayout.Height(22)))
                        {
                            Undo.RecordObject(capsuleCollider, "Apply Recommended Capsule Settings");
                            ApplyRecommendedCapsuleSettings(capsuleCollider);
                        }
                        GUILayout.Space(15);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space(3);

                    if (!rigidbodyNeedsSetup && !capsuleNeedsSetup)
                    {
                        var allGoodStyle = new GUIStyle(EditorStyles.label);
                        allGoodStyle.normal.textColor = new Color(0.3f, 0.8f, 0.4f);
                        allGoodStyle.fontStyle = FontStyle.Bold;
                        EditorGUILayout.LabelField("✓ All settings are optimal!", allGoodStyle);
                    }
                    else
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        
                        var applyAllStyle = new GUIStyle(GUI.skin.button);
                        applyAllStyle.fontStyle = FontStyle.Bold;
                        
                        if (GUILayout.Button("Apply All Recommended Settings", applyAllStyle, GUILayout.Width(220), GUILayout.Height(25)))
                        {
                            if (rigidbodyNeedsSetup)
                            {
                                Undo.RecordObject(rigidbody, "Apply Recommended Rigidbody Settings");
                                ApplyRecommendedRigidbodySettings(rigidbody);
                            }
                            if (capsuleNeedsSetup)
                            {
                                Undo.RecordObject(capsuleCollider, "Apply Recommended Capsule Settings");
                                ApplyRecommendedCapsuleSettings(capsuleCollider);
                            }
                        }
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Rigidbody: isKinematic=true, useGravity=false, Interpolate, FreezeRotation.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("The component applies gravity and collision in its own step.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("CapsuleCollider defaults: Height=1.8, Radius=0.3, Center=(0, 0, 0).",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private const string CurrentTag = "Current";
        private const string RecommendedTag = "Recommended";

        private void EnsureStatusStyles()
        {
            if (_statusOkStyle != null) return;
            _statusOkStyle = new GUIStyle(EditorStyles.label);
            _statusOkStyle.normal.textColor = new Color(0.3f, 0.8f, 0.4f);
            _statusWarningStyle = new GUIStyle(EditorStyles.label);
            _statusWarningStyle.normal.textColor = new Color(1f, 0.7f, 0.2f);
        }

        private static string InterpolationName(RigidbodyInterpolation value)
        {
            if (value == RigidbodyInterpolation.None) return "None";
            if (value == RigidbodyInterpolation.Interpolate) return "Interpolate";
            if (value == RigidbodyInterpolation.Extrapolate) return "Extrapolate";
            return "Custom";
        }

        private static string CollisionName(CollisionDetectionMode value)
        {
            if (value == CollisionDetectionMode.Discrete) return "Discrete";
            if (value == CollisionDetectionMode.Continuous) return "Continuous";
            if (value == CollisionDetectionMode.ContinuousSpeculative) return "ContinuousSpeculative";
            if (value == CollisionDetectionMode.ContinuousDynamic) return "ContinuousDynamic";
            return "Custom";
        }

        private static string ConstraintsName(RigidbodyConstraints value)
        {
            if (value == RigidbodyConstraints.None) return "None";
            if (value == RigidbodyConstraints.FreezeRotation) return "FreezeRotation";
            if (value == RigidbodyConstraints.FreezeAll) return "FreezeAll";
            return "Custom";
        }

        private void DrawSettingStatus(string label, bool isCorrect, string recommended, string current,
            string fullLabel = null, string fullRecommended = null)
        {
            EnsureStatusStyles();
            GUIStyle statusStyle = isCorrect ? _statusOkStyle : _statusWarningStyle;
            EditorGUILayout.LabelField(
                CachedStatusLabel(label, isCorrect, fullLabel ?? label), statusStyle);
            if (!isCorrect)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    CachedDetailLine(current, recommended, fullRecommended), EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawNumericSettingStatus(string label, bool isCorrect, float current, float recommended)
        {
            EnsureStatusStyles();
            GUIStyle statusStyle = isCorrect ? _statusOkStyle : _statusWarningStyle;

            EditorGUILayout.LabelField(CachedStatusLabel(label, isCorrect, label), statusStyle);
            if (!isCorrect)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    CachedNumericDetailLine(current, recommended), EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }

        private void ApplyRecommendedRigidbodySettings(Rigidbody rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            EditorUtility.SetDirty(rb);
        }

        private void ApplyRecommendedCapsuleSettings(CapsuleCollider capsule)
        {
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0f, 0f);
            capsule.direction = 1;
            EditorUtility.SetDirty(capsule);
        }

        private void ValidateConfiguration()
        {
            var component = target as MovementComponent;
            if (component == null) return;

            var capsuleCollider = component.GetComponent<CapsuleCollider>();
            if (capsuleCollider == null) return;

            var config = _config.objectReferenceValue as MovementConfig;
            if (config == null) return;

            if (config.StepHeight > capsuleCollider.radius)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var warningStyle = new GUIStyle(EditorStyles.boldLabel);
                warningStyle.normal.textColor = new Color(1f, 0.6f, 0f);

                EditorGUILayout.LabelField("⚠️ Step Height Configuration Warning", warningStyle);
                EditorGUILayout.Space(3);

                EditorGUILayout.HelpBox(
                    "Step Height is larger than the Capsule Radius.\n\n" +
                    "This may cause issues:\n" +
                    "Character may clip through geometry when stepping up.\n" +
                    "Step detection may fail for stairs.\n\n" +
                    "Recommendation: keep Step Height at or below the Capsule Radius.",
                    MessageType.Warning);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField("Step Height", config.StepHeight);
                EditorGUILayout.FloatField("Capsule Radius", capsuleCollider.radius);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
            }

            if (config.GroundLayer == 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Ground Layer is not set!\n" +
                    "The character will not detect any ground. Please assign appropriate layers.",
                    MessageType.Error);
            }
        }

        private void DrawAdditionalProperties()
        {
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            bool hasAdditionalProperties = false;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "m_Script")
                    continue;

                bool isExcluded = false;
                foreach (string excludedName in ExcludedProperties)
                {
                    if (property.propertyPath == excludedName)
                    {
                        isExcluded = true;
                        break;
                    }
                }

                if (!isExcluded)
                {
                    if (!hasAdditionalProperties)
                    {
                        EditorGUILayout.Space(10);
                        EditorGUILayout.LabelField("Additional Properties", EditorStyles.boldLabel);
                        EditorGUILayout.HelpBox(
                            "Properties from derived class. These fields are automatically displayed here.",
                            MessageType.Info);
                        EditorGUILayout.Space(5);
                        hasAdditionalProperties = true;
                    }

                    EditorGUILayout.PropertyField(property, true);
                }
            }
        }

#if UNITY_EDITOR
        private void DrawDebugSection()
        {
            Color headerColor = EditorGUIUtility.isProSkin 
                ? new Color(0.3f, 0.5f, 0.7f)
                : new Color(0.2f, 0.4f, 0.6f);
            
            Color visualizationColor = new Color(0.4f, 0.7f, 1f);
            Color runtimeColor = new Color(0.7f, 0.5f, 1f);
            Color disabledColor = new Color(0.5f, 0.5f, 0.5f);
            
            Color stateGroundedColor = new Color(0.2f, 0.8f, 0.3f);
            Color stateAirColor = new Color(0.9f, 0.4f, 0.3f);
            Color speedOkColor = new Color(0.3f, 0.8f, 0.4f);
            Color speedWarningColor = new Color(1f, 0.7f, 0.2f);
            
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            headerStyle.normal.textColor = headerColor;
            
            EditorGUILayout.LabelField("🔧 Debug", headerStyle);
            
            DrawFoldoutPanel(
                ref _showDebugVisualization,
                "Scene Visualization",
                visualizationColor,
                new Color(0.4f, 0.6f, 0.8f, 0.15f),
                () =>
                {
                    if (_showDebugGizmos != null)
                    {
                        EditorGUILayout.PropertyField(_showDebugGizmos, CachedContent(
                            "Enable Gizmos", "Master toggle for all debug visualization."));

                        if (_showDebugGizmos.boolValue)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(15);
                            EditorGUILayout.BeginVertical();
                            
                            if (_showGroundDetection != null)
                                _showGroundDetection.boolValue = EditorGUILayout.ToggleLeft(
                                    " Ground Detection", _showGroundDetection.boolValue);
                            if (_showCollisionSweep != null)
                                _showCollisionSweep.boolValue = EditorGUILayout.ToggleLeft(
                                    " Collision Sweep", _showCollisionSweep.boolValue);
                            if (_showVelocity != null)
                                _showVelocity.boolValue = EditorGUILayout.ToggleLeft(
                                    " Velocity Vector", _showVelocity.boolValue);
                            
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.EndHorizontal();
                            
                            EditorGUILayout.Space(3);
                            DrawMiniLegend();
                        }
                    }
                });
            
            EditorGUILayout.Space(3);
            
            if (Application.isPlaying)
            {
                DrawFoldoutPanel(
                    ref _showRuntimeDebug,
                    "Runtime Info (Live)",
                    runtimeColor,
                    new Color(0.6f, 0.4f, 0.8f, 0.15f),
                    () =>
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("State");
                        
                        string stateName = _debugCurrentState?.stringValue ?? "Unknown";
                        bool isGrounded = stateName != "Fall" && stateName != "Jump";
                        Color stateColor = isGrounded ? stateGroundedColor : stateAirColor;
                        
                        var stateStyle = new GUIStyle(EditorStyles.boldLabel);
                        stateStyle.normal.textColor = stateColor;
                        EditorGUILayout.LabelField(stateName, stateStyle);
                        EditorGUILayout.EndHorizontal();
                        
                        EditorGUILayout.Space(2);
                        
                        if (_debugConfigRunSpeed != null && _debugActualSpeed != null)
                        {
                            float configSpeed = _debugConfigRunSpeed.floatValue;
                            float actualSpeed = _debugActualSpeed.floatValue;
                            bool speedOk = actualSpeed <= configSpeed + 0.5f;
                            
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Speed");
                            
                            var speedStyle = new GUIStyle(EditorStyles.label);
                            speedStyle.normal.textColor = speedOk ? speedOkColor : speedWarningColor;
                            
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.FloatField("Actual", actualSpeed);
                            EditorGUILayout.FloatField("Config", configSpeed);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugInputDirection != null && _debugInputMagnitude != null)
                        {
                            Vector3 inputDir = _debugInputDirection.vector3Value;
                            float inputMag = _debugInputMagnitude.floatValue;
                            
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Input");
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Vector3Field(GUIContent.none, inputDir);
                            EditorGUILayout.FloatField("Mag", inputMag);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugActualVelocity != null)
                        {
                            Vector3 vel = _debugActualVelocity.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Velocity");
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Vector3Field(GUIContent.none, vel);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugDeltaTime != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("DeltaTime");
                            float dt = _debugDeltaTime.floatValue;
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.FloatField("Seconds", dt);
                            EditorGUILayout.FloatField("FPS", dt > 0 ? 1f / dt : 0f);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugConfigAssigned != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Config");
                            
                            var configStyle = new GUIStyle(EditorStyles.label);
                            configStyle.normal.textColor = _debugConfigAssigned.boolValue ? stateGroundedColor : stateAirColor;
                            EditorGUILayout.LabelField(_debugConfigAssigned.boolValue ? "✓ Assigned" : "✗ Missing", configStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                    });
                
                EditorGUILayout.Space(3);
                
                Color platformColor = new Color(1f, 0.6f, 0.3f);
                Color platformOkColor = new Color(0.3f, 0.8f, 0.4f);
                Color platformWarningColor = new Color(1f, 0.7f, 0.2f);
                Color platformErrorColor = new Color(0.9f, 0.3f, 0.3f);
                
                DrawFoldoutPanel(
                    ref _showPlatformDebug,
                    "Moving Platform",
                    platformColor,
                    new Color(0.8f, 0.5f, 0.2f, 0.15f),
                    () =>
                    {
                        if (_debugGroundColliderName != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Ground");
                            EditorGUILayout.LabelField(_debugGroundColliderName.stringValue);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugGroundHasRigidbody != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Has Rigidbody");
                            
                            var rbStyle = new GUIStyle(EditorStyles.label);
                            rbStyle.normal.textColor = _debugGroundHasRigidbody.boolValue ? platformOkColor : platformErrorColor;
                            EditorGUILayout.LabelField(_debugGroundHasRigidbody.boolValue ? "✓ Yes" : "✗ No (Required!)", rbStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugGroundLayerMatch != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Layer Match");
                            
                            var layerStyle = new GUIStyle(EditorStyles.label);
                            layerStyle.normal.textColor = _debugGroundLayerMatch.boolValue ? platformOkColor : platformWarningColor;
                            EditorGUILayout.LabelField(_debugGroundLayerMatch.boolValue ? "✓ Yes" : "✗ No", layerStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        EditorGUILayout.Space(3);
                        
                        if (_debugOnPlatform != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("On Platform");
                            
                            var onPlatformStyle = new GUIStyle(EditorStyles.boldLabel);
                            onPlatformStyle.normal.textColor = _debugOnPlatform.boolValue ? platformOkColor : disabledColor;
                            EditorGUILayout.LabelField(_debugOnPlatform.boolValue ? "✓ YES" : "No", onPlatformStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugPlatformName != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Platform");
                            EditorGUILayout.LabelField(_debugPlatformName.stringValue);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugPlatformVelocity != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 vel = _debugPlatformVelocity.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Platform Vel");
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Vector3Field(GUIContent.none, vel);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugPlatformDeltaPos != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 delta = _debugPlatformDeltaPos.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Frame Delta");
                            var deltaStyle = new GUIStyle(EditorStyles.label);
                            deltaStyle.normal.textColor = delta.sqrMagnitude > 0.00001f ? platformOkColor : disabledColor;
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Vector3Field(GUIContent.none, delta);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugLocalPosition != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 localPos = _debugLocalPosition.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Local Pos");
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.Vector3Field(GUIContent.none, localPos);
                            EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        if (_debugOnPlatform != null && !_debugOnPlatform.boolValue)
                        {
                            EditorGUILayout.Space(3);
                            EditorGUILayout.HelpBox(
                                "Platform requirements:\n" +
                                "• Must have Rigidbody (isKinematic=true)\n" +
                                "• Must be in Ground Layer or Platform Layer\n" +
                                "• Character must be grounded on platform",
                                MessageType.Info);
                        }
                    });
                
                Repaint();
            }
            else
            {
                DrawFoldoutPanel(
                    ref _showRuntimeDebug,
                    "Runtime Info",
                    disabledColor,
                    new Color(0.5f, 0.5f, 0.5f, 0.1f),
                    () =>
                    {
                        EditorGUILayout.LabelField("Enter Play Mode to see runtime debug info.", EditorStyles.miniLabel);
                    },
                    forceExpanded: false);
            }
        }
        
        private void DrawFoldoutPanel(ref bool foldout, string title, Color titleColor, Color bgColor, 
            System.Action drawContent, bool forceExpanded = false)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;
            
            var foldoutStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };
            foldoutStyle.normal.textColor = titleColor;
            foldoutStyle.onNormal.textColor = titleColor;
            foldoutStyle.focused.textColor = titleColor;
            foldoutStyle.onFocused.textColor = titleColor;
            foldoutStyle.active.textColor = titleColor;
            foldoutStyle.onActive.textColor = titleColor;
            
            EditorGUI.indentLevel++;
            foldout = EditorGUILayout.Foldout(foldout, title, true, foldoutStyle);
            EditorGUI.indentLevel--;
            
            if (foldout || forceExpanded)
            {
                drawContent?.Invoke();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawMiniLegend()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            
            var legendStyle = new GUIStyle(EditorStyles.miniLabel);
            legendStyle.richText = true;
            
            string legend = "<color=#00FF00>●</color> Grounded  " +
                           "<color=#FF4444>●</color> Air  " +
                           "<color=#FFFF00>─</color> Ground  " +
                           "<color=#FF00FF>→</color> Normal  " +
                           "<color=#4444FF>→</color> Velocity";
            
            EditorGUILayout.LabelField(legend, legendStyle);
            EditorGUILayout.EndHorizontal();
        }
#endif
    }
}
