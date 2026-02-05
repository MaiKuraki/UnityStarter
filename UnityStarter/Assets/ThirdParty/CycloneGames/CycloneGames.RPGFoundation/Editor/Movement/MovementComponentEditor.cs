using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Editor.Movement
{
    [CustomEditor(typeof(MovementComponent))]
    [CanEditMultipleObjects]
    public class MovementComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _config;
        private SerializedProperty _characterAnimator;
        private SerializedProperty _animancerComponent;
        private SerializedProperty _worldUpSource;
        private SerializedProperty _useRootMotion;
        private SerializedProperty _ignoreTimeScale;
#if UNITY_EDITOR
        private SerializedProperty _showDebugGizmos;
        private SerializedProperty _showGroundDetection;
        private SerializedProperty _showCollisionSweep;
        private SerializedProperty _showVelocity;
        
        // Runtime debug properties
        private SerializedProperty _debugConfigRunSpeed;
        private SerializedProperty _debugCurrentSpeed;
        private SerializedProperty _debugDeltaTime;
        private SerializedProperty _debugCurrentState;
        private SerializedProperty _debugConfigAssigned;
        private SerializedProperty _debugInputDirection;
        private SerializedProperty _debugInputMagnitude;
        private SerializedProperty _debugActualVelocity;
        private SerializedProperty _debugActualSpeed;
        
        // Moving platform debug properties
        private SerializedProperty _debugOnPlatform;
        private SerializedProperty _debugPlatformName;
        private SerializedProperty _debugPlatformVelocity;
        private SerializedProperty _debugGroundColliderName;
        private SerializedProperty _debugGroundHasRigidbody;
        private SerializedProperty _debugGroundLayerMatch;
        private SerializedProperty _debugPlatformDeltaPos;
        private SerializedProperty _debugLocalPosition;
        
        // Foldout states
        private bool _showDebugVisualization = true;
        private bool _showRuntimeDebug = true;
        private bool _showPlatformDebug = true;
#endif

        private enum AnimationSystemType
        {
            UnityAnimator,
            Animancer
        }

        private AnimationSystemType _selectedSystem = AnimationSystemType.UnityAnimator;

        // Foldout states for collapsible help boxes
        private bool _showRootMotionHelp = false;
        private bool _showTimeScaleHelp = false;
        private bool _showWorldUpHelp = false;
        private bool _showQuickSetup = false;

        // Static excluded properties list to avoid GC allocation every inspector draw
        private static readonly string[] ExcludedProperties = new string[]
        {
            "config", "characterAnimator", "animancerComponent", "worldUpSource",
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

        /// <summary>
        /// Gets a color for emphasis text that is visible in both light and dark Unity editor themes.
        /// </summary>
        private Color GetEmphasisColor()
        {
            // For dark theme (Pro): use light gray/white for visibility
            // For light theme: use dark gray for emphasis
            return EditorGUIUtility.isProSkin
                ? new Color(0.85f, 0.85f, 0.85f)  // Light gray for dark theme
                : new Color(0.25f, 0.25f, 0.25f); // Dark gray for light theme
        }

        private void OnEnable()
        {
            _config = serializedObject.FindProperty("config");
            _characterAnimator = serializedObject.FindProperty("characterAnimator");
            _animancerComponent = serializedObject.FindProperty("animancerComponent");
            _worldUpSource = serializedObject.FindProperty("worldUpSource");
            _useRootMotion = serializedObject.FindProperty("useRootMotion");
            _ignoreTimeScale = serializedObject.FindProperty("ignoreTimeScale");
#if UNITY_EDITOR
            _showDebugGizmos = serializedObject.FindProperty("showDebugGizmos");
            _showGroundDetection = serializedObject.FindProperty("showGroundDetection");
            _showCollisionSweep = serializedObject.FindProperty("showCollisionSweep");
            _showVelocity = serializedObject.FindProperty("showVelocity");
            
            // Runtime debug properties
            _debugConfigRunSpeed = serializedObject.FindProperty("_debugConfigRunSpeed");
            _debugCurrentSpeed = serializedObject.FindProperty("_debugCurrentSpeed");
            _debugDeltaTime = serializedObject.FindProperty("_debugDeltaTime");
            _debugCurrentState = serializedObject.FindProperty("_debugCurrentState");
            _debugConfigAssigned = serializedObject.FindProperty("_debugConfigAssigned");
            _debugInputDirection = serializedObject.FindProperty("_debugInputDirection");
            _debugInputMagnitude = serializedObject.FindProperty("_debugInputMagnitude");
            _debugActualVelocity = serializedObject.FindProperty("_debugActualVelocity");
            _debugActualSpeed = serializedObject.FindProperty("_debugActualSpeed");
            
            // Moving platform debug properties
            _debugOnPlatform = serializedObject.FindProperty("_debugOnPlatform");
            _debugPlatformName = serializedObject.FindProperty("_debugPlatformName");
            _debugPlatformVelocity = serializedObject.FindProperty("_debugPlatformVelocity");
            _debugGroundColliderName = serializedObject.FindProperty("_debugGroundColliderName");
            _debugGroundHasRigidbody = serializedObject.FindProperty("_debugGroundHasRigidbody");
            _debugGroundLayerMatch = serializedObject.FindProperty("_debugGroundLayerMatch");
            _debugPlatformDeltaPos = serializedObject.FindProperty("_debugPlatformDeltaPos");
            _debugLocalPosition = serializedObject.FindProperty("_debugLocalPosition");
#endif

            // Determine current system based on assigned references
            if (_animancerComponent.objectReferenceValue != null)
            {
                _selectedSystem = AnimationSystemType.Animancer;
            }
            else if (_characterAnimator.objectReferenceValue != null)
            {
                _selectedSystem = AnimationSystemType.UnityAnimator;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_config);

            EditorGUILayout.Space(10);

            // Animation system selection
            EditorGUI.BeginChangeCheck();
            _selectedSystem = (AnimationSystemType)EditorGUILayout.EnumPopup(
                new GUIContent("Animation System", "Choose between Unity Animator or Animancer"),
                _selectedSystem);

            if (EditorGUI.EndChangeCheck())
            {
                // Clear references when switching systems
                if (_selectedSystem == AnimationSystemType.UnityAnimator)
                {
                    _animancerComponent.objectReferenceValue = null;
                }
                else
                {
                    _characterAnimator.objectReferenceValue = null;
                }
            }

            EditorGUILayout.Space(5);

            // Display fields based on selected system
            if (_selectedSystem == AnimationSystemType.UnityAnimator)
            {
                DrawUnityAnimatorFields();
            }
            else
            {
                DrawAnimancerFields();
            }

            EditorGUILayout.Space(10);

            // World Up Source Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_worldUpSource, new GUIContent(
                "World Up Source",
                "Optional Transform to use as world up direction reference.\n" +
                "If assigned, character will use this Transform's up direction as the world up.\n" +
                "If null, uses Vector3.up (standard Unity world up)."));

            // Collapsible help section - inside helpBox with proper indentation
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

            // Root Motion Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_useRootMotion, new GUIContent(
                "Use Root Motion",
                "Enable root motion support for animations.\n" +
                "When enabled, animations with root motion will drive character movement."));

            // Check Animancer type for Root Motion compatibility
            bool isUsingAnimancer = _animancerComponent.objectReferenceValue != null;
            bool isHybridAnimancer = false;
            bool isRegularAnimancer = false;

            if (isUsingAnimancer)
            {
                var animancerObj = _animancerComponent.objectReferenceValue;
                var animancerType = animancerObj.GetType();
                isHybridAnimancer = animancerType.Name == "HybridAnimancerComponent" ||
                                   animancerType.FullName == "Animancer.HybridAnimancerComponent";
                isRegularAnimancer = animancerType.Name == "AnimancerComponent" ||
                                    animancerType.FullName == "Animancer.AnimancerComponent";
            }

            // Show Root Motion compatibility warning if using regular AnimancerComponent
            if (_useRootMotion.boolValue && isUsingAnimancer && isRegularAnimancer)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Compact visual emphasis with theme-aware color and larger font
                var statusStyle = new GUIStyle(EditorStyles.boldLabel);
                statusStyle.normal.textColor = GetEmphasisColor();
                statusStyle.fontSize = 13;

                EditorGUILayout.LabelField("ROOT MOTION NOT SUPPORTED", statusStyle);
                EditorGUILayout.Space(2);

                EditorGUILayout.HelpBox(
                    "AnimancerComponent does NOT support Root Motion.\n" +
                    "Root Motion will be automatically disabled at runtime.\n" +
                    "To use Root Motion, switch to HybridAnimancerComponent (requires Pro license).",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
            }

            // Collapsible help section - inside helpBox with proper indentation
            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showRootMotionHelp = EditorGUILayout.Foldout(_showRootMotionHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showRootMotionHelp)
            {
                EditorGUI.indentLevel++;
                if (_useRootMotion.boolValue)
                {
                    if (isHybridAnimancer)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // Compact visual emphasis for Root Motion supported
                        var statusStyle = new GUIStyle(EditorStyles.boldLabel);
                        statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f); // Green for supported
                        statusStyle.fontSize = 13;

                        EditorGUILayout.LabelField("Root Motion: FULLY SUPPORTED (HybridAnimancerComponent)", statusStyle);
                        EditorGUILayout.Space(2);

                        EditorGUILayout.HelpBox(
                            "Fully supported via Animator API\n" +
                            "• Animations with root motion will control character position\n" +
                            "• Use for: Attack lunges, dodge rolls, special movement animations\n" +
                            "• States can override this setting via MovementContext.UseRootMotion\n" +
                            "• Requires Animator component with RuntimeAnimatorController\n" +
                            "• Root motion is applied in OnAnimatorMove callback",
                            MessageType.Info);
                        EditorGUILayout.EndVertical();
                    }
                    else if (isRegularAnimancer)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // Compact visual emphasis with theme-aware color
                        var statusStyle = new GUIStyle(EditorStyles.boldLabel);
                        statusStyle.normal.textColor = GetEmphasisColor();
                        statusStyle.fontSize = 13;

                        EditorGUILayout.LabelField("Root Motion: NOT SUPPORTED (AnimancerComponent)", statusStyle);
                        EditorGUILayout.Space(2);

                        EditorGUILayout.HelpBox(
                            "Will be disabled at runtime\n" +
                            "• AnimancerComponent uses Parameters mode (no Animator API)\n" +
                            "• Root Motion requires Animator.applyRootMotion and OnAnimatorMove\n" +
                            "• Switch to HybridAnimancerComponent to use Root Motion",
                            MessageType.Info);
                        EditorGUILayout.EndVertical();
                    }
                    else if (isUsingAnimancer)
                    {
                        EditorGUILayout.HelpBox(
                            "Root Motion Enabled (Unknown Animancer Type):\n" +
                            "• Root Motion support depends on component type\n" +
                            "• HybridAnimancerComponent: SUPPORTED\n" +
                            "• AnimancerComponent: NOT SUPPORTED",
                            MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Root Motion Enabled:\n" +
                            "• Animations with root motion will control character position\n" +
                            "• Use for: Attack lunges, dodge rolls, special movement animations\n" +
                            "• States can override this setting via MovementContext.UseRootMotion\n" +
                            "• Requires Animator component and animations with root motion enabled\n" +
                            "• Root motion is applied in OnAnimatorMove callback",
                            MessageType.Info);
                    }

                    // Check for Animator
                    Animator targetAnimator = null;
                    if (isHybridAnimancer && _animancerComponent.objectReferenceValue != null)
                    {
                        var animancerObj = _animancerComponent.objectReferenceValue;
                        var animancerType = animancerObj.GetType();
                        var animatorProperty = animancerType.GetProperty("Animator",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (animatorProperty != null)
                        {
                            targetAnimator = animatorProperty.GetValue(animancerObj) as Animator;
                        }
                    }
                    else
                    {
                        targetAnimator = _characterAnimator.objectReferenceValue as Animator;
                    }

                    if (targetAnimator == null)
                    {
                        if (isHybridAnimancer)
                        {
                            EditorGUILayout.HelpBox(
                                "[WARNING] HybridAnimancerComponent has no Animator assigned.\n" +
                                "Root Motion requires an Animator component.",
                                MessageType.Warning);
                        }
                        else if (!isUsingAnimancer)
                        {
                            EditorGUILayout.HelpBox(
                                "[WARNING] No Animator assigned. Root motion requires an Animator component.",
                                MessageType.Warning);
                        }
                    }
                    else if (!targetAnimator.applyRootMotion)
                    {
                        EditorGUILayout.HelpBox(
                            "[INFO] Animator's 'Apply Root Motion' is currently disabled.\n" +
                            "It will be automatically enabled at runtime when root motion is used.",
                            MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Root Motion Disabled:\n" +
                        "• Character movement is controlled by script (state machine)\n" +
                        "• Use for: Standard walk/run/jump movements\n" +
                        "• You can enable root motion per-state by setting MovementContext.UseRootMotion",
                        MessageType.None);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Time Scale Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_ignoreTimeScale, new GUIContent(
                "Ignore Time Scale",
                "Ignore global Time.timeScale for this character.\n" +
                "When enabled, uses Time.unscaledDeltaTime instead of Time.deltaTime.\n" +
                "Can be changed at runtime via IgnoreTimeScale property for dynamic switching."));

            // Collapsible help section - inside helpBox with proper indentation
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
            
            // ===== QUICK SETUP SECTION =====
            DrawQuickSetupSection();

#if UNITY_EDITOR
            EditorGUILayout.Space(10);
            
            // ===== DEBUG SECTION =====
            DrawDebugSection();
#endif

            // Validate configuration settings
            ValidateConfiguration();

            // Draw any additional fields from derived classes
            // This allows inheritance without modifying this editor
            DrawAdditionalProperties();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws the Quick Setup section with recommended Rigidbody and CapsuleCollider settings.
        /// </summary>
        private void DrawQuickSetupSection()
        {
            var component = target as MovementComponent;
            if (component == null) return;

            var rigidbody = component.GetComponent<Rigidbody>();
            var capsuleCollider = component.GetComponent<CapsuleCollider>();

            // Check if components exist
            bool hasRigidbody = rigidbody != null;
            bool hasCapsule = capsuleCollider != null;

            // Recommended settings
            const float recommendedHeight = 1.8f;
            const float recommendedRadius = 0.3f;
            Vector3 recommendedCenter = new Vector3(0f, 0f, 0f);

            // Check current settings against recommended
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
                capsuleCollider.direction != 1 // Y-Axis
            );

            // Header style
            Color headerColor = EditorGUIUtility.isProSkin
                ? new Color(0.4f, 0.8f, 0.4f)
                : new Color(0.2f, 0.6f, 0.2f);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            headerStyle.normal.textColor = headerColor;

            EditorGUILayout.LabelField("⚙️ Quick Setup", headerStyle);

            // Determine background color based on status
            Color bgColor;
            if (!hasRigidbody || !hasCapsule)
                bgColor = new Color(0.8f, 0.3f, 0.3f, 0.2f); // Red - missing components
            else if (rigidbodyNeedsSetup || capsuleNeedsSetup)
                bgColor = new Color(0.8f, 0.6f, 0.2f, 0.2f); // Orange - needs setup
            else
                bgColor = new Color(0.3f, 0.7f, 0.3f, 0.15f); // Green - all good

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            // Foldout
            var foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            EditorGUI.indentLevel++;
            _showQuickSetup = EditorGUILayout.Foldout(_showQuickSetup, "Physics Components", true, foldoutStyle);
            EditorGUI.indentLevel--;

            if (_showQuickSetup)
            {
                EditorGUILayout.Space(3);

                // Missing components warning
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
                    // Status colors
                    Color okColor = new Color(0.3f, 0.8f, 0.4f);
                    Color warningColor = new Color(1f, 0.7f, 0.2f);

                    // ===== RIGIDBODY STATUS =====
                    EditorGUILayout.LabelField("Rigidbody", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    DrawSettingStatus("Is Kinematic", rigidbody.isKinematic, "true", rigidbody.isKinematic.ToString());
                    DrawSettingStatus("Use Gravity", !rigidbody.useGravity, "false", rigidbody.useGravity.ToString());
                    DrawSettingStatus("Interpolation", rigidbody.interpolation == RigidbodyInterpolation.Interpolate, 
                        RigidbodyInterpolation.Interpolate.ToString(), rigidbody.interpolation.ToString());
                    DrawSettingStatus("Collision Detection", rigidbody.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative,
                        "ContinuousSpeculative", rigidbody.collisionDetectionMode.ToString());
                    DrawSettingStatus("Freeze Rotation", rigidbody.constraints == RigidbodyConstraints.FreezeRotation,
                        "FreezeRotation", rigidbody.constraints.ToString());

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

                    // ===== CAPSULE COLLIDER STATUS =====
                    EditorGUILayout.LabelField("CapsuleCollider", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    DrawSettingStatus("Height", Mathf.Abs(capsuleCollider.height - recommendedHeight) < 0.01f,
                        recommendedHeight.ToString("F2"), capsuleCollider.height.ToString("F2"));
                    DrawSettingStatus("Radius", Mathf.Abs(capsuleCollider.radius - recommendedRadius) < 0.01f,
                        recommendedRadius.ToString("F2"), capsuleCollider.radius.ToString("F2"));
                    DrawSettingStatus("Center", Vector3.Distance(capsuleCollider.center, recommendedCenter) < 0.01f,
                        $"(0, 0, 0)", $"({capsuleCollider.center.x:F2}, {capsuleCollider.center.y:F2}, {capsuleCollider.center.z:F2})");
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

                    // All good message
                    if (!rigidbodyNeedsSetup && !capsuleNeedsSetup)
                    {
                        var allGoodStyle = new GUIStyle(EditorStyles.label);
                        allGoodStyle.normal.textColor = okColor;
                        allGoodStyle.fontStyle = FontStyle.Bold;
                        EditorGUILayout.LabelField("✓ All settings are optimal!", allGoodStyle);
                    }
                    else
                    {
                        // Apply all button
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

                // Help info
                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Recommended settings for kinematic character movement:\n\n" +
                    "Rigidbody:\n" +
                    "• isKinematic=true - Character handles own physics\n" +
                    "• useGravity=false - Gravity applied via MovementConfig\n" +
                    "• Interpolate - Smooth visual movement\n" +
                    "• FreezeRotation - Prevent physics rotation\n\n" +
                    "CapsuleCollider (1.8m humanoid):\n" +
                    "• Height=1.8 - Standard human height\n" +
                    "• Radius=0.3 - Shoulder width\n" +
                    "• Center=(0, 0, 0) - Default center\n",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a setting status line with checkmark or warning.
        /// </summary>
        private void DrawSettingStatus(string label, bool isCorrect, string recommended, string current)
        {
            Color okColor = new Color(0.3f, 0.8f, 0.4f);
            Color warningColor = new Color(1f, 0.7f, 0.2f);

            EditorGUILayout.BeginHorizontal();
            
            var statusStyle = new GUIStyle(EditorStyles.label);
            statusStyle.normal.textColor = isCorrect ? okColor : warningColor;
            
            string statusIcon = isCorrect ? "✓" : "⚠";
            EditorGUILayout.LabelField($"{statusIcon} {label}:", GUILayout.Width(140));
            
            if (isCorrect)
            {
                EditorGUILayout.LabelField(current, statusStyle);
            }
            else
            {
                EditorGUILayout.LabelField($"{current} → {recommended}", statusStyle);
            }
            
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Applies recommended Rigidbody settings for kinematic character movement.
        /// </summary>
        private void ApplyRecommendedRigidbodySettings(Rigidbody rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            EditorUtility.SetDirty(rb);
        }

        /// <summary>
        /// Applies recommended CapsuleCollider settings for a 1.8m humanoid character.
        /// </summary>
        private void ApplyRecommendedCapsuleSettings(CapsuleCollider capsule)
        {
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0f, 0f);
            capsule.direction = 1; // Y-Axis
            EditorUtility.SetDirty(capsule);
        }

        /// <summary>
        /// Validates MovementConfig settings for the Rigidbody + CapsuleCollider setup.
        /// Shows warnings for potentially problematic configurations.
        /// </summary>
        private void ValidateConfiguration()
        {
            var component = target as MovementComponent;
            if (component == null) return;

            var capsuleCollider = component.GetComponent<CapsuleCollider>();
            if (capsuleCollider == null) return;

            var config = _config.objectReferenceValue as MovementConfig;
            if (config == null) return;

            // Check if stepHeight is appropriate
            if (config.stepHeight > capsuleCollider.radius)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var warningStyle = new GUIStyle(EditorStyles.boldLabel);
                warningStyle.normal.textColor = new Color(1f, 0.6f, 0f); // Orange

                EditorGUILayout.LabelField("⚠️ Step Height Configuration Warning", warningStyle);
                EditorGUILayout.Space(3);

                EditorGUILayout.HelpBox(
                    $"Step Height ({config.stepHeight:F3}) is larger than Capsule Radius ({capsuleCollider.radius:F3}).\n\n" +
                    "This may cause issues:\n" +
                    "• Character may clip through geometry when stepping up\n" +
                    "• Step detection may fail for stairs\n\n" +
                    "Recommendation:\n" +
                    $"• Set Step Height to at most {capsuleCollider.radius:F3}\n" +
                    "• Typical: stepHeight = 0.3 for a radius of 0.3-0.5",
                    MessageType.Warning);

                EditorGUILayout.EndVertical();
            }

            // Check for missing layer masks
            if (config.groundLayer == 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Ground Layer is not set!\n" +
                    "The character will not detect any ground. Please assign appropriate layers.",
                    MessageType.Error);
            }
        }

        /// <summary>
        /// Draws any additional properties from derived classes.
        /// Excludes already-drawn properties to avoid duplication.
        /// This method allows derived classes to add new fields without modifying this editor.
        /// </summary>
        private void DrawAdditionalProperties()
        {
            // Get all serialized properties
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            bool hasAdditionalProperties = false;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip script field (always drawn by default)
                if (property.propertyPath == "m_Script")
                    continue;

                // Check if this property is not in the excluded list
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
                        // Only add space and label if there are additional properties
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

        private void DrawUnityAnimatorFields()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Unity Animator Setup", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(_characterAnimator, new GUIContent(
                "Animator",
                "The Animator component that controls character animations.\n" +
                "If not assigned, will auto-find on the same GameObject."));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Find Animator", GUILayout.Width(150)))
            {
                var component = (MovementComponent)target;
                var animator = component.GetComponent<Animator>();
                if (animator != null)
                {
                    _characterAnimator.objectReferenceValue = animator;
                    EditorUtility.SetDirty(target);
                }
                else
                {
                    EditorUtility.DisplayDialog("Animator Not Found",
                        "No Animator component found on this GameObject.", "OK");
                }
            }

            if (_characterAnimator.objectReferenceValue != null)
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    _characterAnimator.objectReferenceValue = null;
                    EditorUtility.SetDirty(target);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_characterAnimator.objectReferenceValue == null)
            {
                var component = (MovementComponent)target;
                var autoFoundAnimator = component.GetComponent<Animator>();
                if (autoFoundAnimator != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Auto-detected Animator: {autoFoundAnimator.name}\n" +
                        "Click 'Auto-Find Animator' to assign it.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No Animator found. Add an Animator component to this GameObject.",
                        MessageType.Warning);
                }
            }
            else
            {
                var animator = _characterAnimator.objectReferenceValue as Animator;
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Animator Controller: {animator.runtimeAnimatorController.name}",
                        MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimancerFields()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animancer Setup", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(_animancerComponent, new GUIContent(
                "Animancer Component",
                "The AnimancerComponent that controls character animations.\n" +
                "Animancer uses Unity's Playables API and requires an Animator component."));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Find Animancer", GUILayout.Width(150)))
            {
                var component = (MovementComponent)target;
                var animancer = component.GetComponent<MonoBehaviour>();
                if (animancer != null)
                {
                    var animancerType = animancer.GetType();
                    if (animancerType.Name.Contains("AnimancerComponent"))
                    {
                        _animancerComponent.objectReferenceValue = animancer;
                        EditorUtility.SetDirty(target);
                    }
                }

                if (_animancerComponent.objectReferenceValue == null)
                {
                    EditorUtility.DisplayDialog("Animancer Not Found",
                        "No AnimancerComponent found on this GameObject.", "OK");
                }
            }

            if (_animancerComponent.objectReferenceValue != null)
            {
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    _animancerComponent.objectReferenceValue = null;
                    EditorUtility.SetDirty(target);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_animancerComponent.objectReferenceValue != null)
            {
                // Detect Animancer component type
                var animancerObj = _animancerComponent.objectReferenceValue;
                var animancerType = animancerObj.GetType();
                bool isHybridAnimancer = animancerType.Name == "HybridAnimancerComponent" ||
                                        animancerType.FullName == "Animancer.HybridAnimancerComponent";
                bool isRegularAnimancer = animancerType.Name == "AnimancerComponent" ||
                                         animancerType.FullName == "Animancer.AnimancerComponent";

                EditorGUILayout.Space(3);

                // Display component type and Root Motion support info
                if (isHybridAnimancer)
                {
                    // Create a custom style for the status label
                    var statusStyle = new GUIStyle(EditorStyles.boldLabel);
                    statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f); // Green

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Component Type: HybridAnimancerComponent", statusStyle);

                    // Compact Root Motion notice with visual emphasis (theme-aware)
                    var rootMotionStyle = new GUIStyle(EditorStyles.boldLabel);
                    rootMotionStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f); // Green for supported
                    rootMotionStyle.fontSize = 12;

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("ROOT MOTION: FULLY SUPPORTED", rootMotionStyle);
                    EditorGUILayout.Space(2);

                    EditorGUILayout.HelpBox(
                        "• Supports Root Motion via Animator API\n" +
                        "• Can use RuntimeAnimatorController\n" +
                        "• Can also play individual AnimationClips\n" +
                        "• Requires Animancer Pro license",
                        MessageType.Info);
                    EditorGUILayout.EndVertical();
                }
                else if (isRegularAnimancer)
                {
                    // Create a custom style for the status label
                    var statusStyle = new GUIStyle(EditorStyles.boldLabel);
                    statusStyle.normal.textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.7f, 0.7f, 0.7f)  // Light gray for dark theme
                        : new Color(0.5f, 0.5f, 0.5f); // Medium gray for light theme

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Component Type: AnimancerComponent", statusStyle);

                    // Compact Root Motion notice with visual emphasis (theme-aware)
                    var rootMotionStyle = new GUIStyle(EditorStyles.boldLabel);
                    rootMotionStyle.normal.textColor = GetEmphasisColor();
                    rootMotionStyle.fontSize = 12;

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("ROOT MOTION: NOT SUPPORTED", rootMotionStyle);
                    EditorGUILayout.Space(2);

                    EditorGUILayout.HelpBox(
                        "• Uses Parameters mode (string-based parameters)\n" +
                        "• Pure code-driven animation control\n" +
                        "• No RuntimeAnimatorController needed",
                        MessageType.Info);
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "WARNING: Unknown Animancer Component Type\n" +
                        $"Type: {animancerType.Name}\n" +
                        "Root Motion support depends on component type.",
                        MessageType.Warning);
                }

                // Check if Animancer has an Animator
                var animatorProperty = animancerType.GetProperty("Animator",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (animatorProperty != null)
                {
                    var animancerAnimator = animatorProperty.GetValue(animancerObj) as Animator;
                    if (animancerAnimator == null)
                    {
                        if (isHybridAnimancer)
                        {
                            EditorGUILayout.HelpBox(
                                "[WARNING] HybridAnimancerComponent does not have an Animator assigned.\n" +
                                "Root Motion will not work without an Animator.\n" +
                                "Assign an Animator to the HybridAnimancerComponent to enable Root Motion support.",
                                MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "[INFO] AnimancerComponent does not have an Animator assigned.\n" +
                                "It will use Parameters mode instead of Animator mode.\n" +
                                "This is expected for AnimancerComponent (not HybridAnimancerComponent).",
                                MessageType.Info);
                        }
                    }
                    else
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Animancer's Animator:", GUILayout.Width(140));
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(animancerAnimator, typeof(Animator), true);
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.EndHorizontal();

                        if (isHybridAnimancer)
                        {
                            if (animancerAnimator.runtimeAnimatorController != null)
                            {
                                EditorGUILayout.HelpBox(
                                    $"[OK] Animator Controller: {animancerAnimator.runtimeAnimatorController.name}\n" +
                                    "This is correct for HybridAnimancerComponent.\n" +
                                    "Root Motion is supported when enabled.",
                                    MessageType.Info);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox(
                                    "[INFO] Animator has no Controller assigned.\n" +
                                    "For HybridAnimancerComponent, you can assign a simple Controller\n" +
                                    "(even just a default state) to enable Root Motion support.",
                                    MessageType.Info);
                            }
                        }
                        else
                        {
                            if (animancerAnimator.runtimeAnimatorController != null)
                            {
                                EditorGUILayout.HelpBox(
                                    $"[NOTE] Animator has Controller '{animancerAnimator.runtimeAnimatorController.name}' assigned.\n" +
                                    "For AnimancerComponent, the Controller should typically be empty.\n" +
                                    "Consider using HybridAnimancerComponent if you need Controller support.",
                                    MessageType.Warning);
                            }
                        }
                    }
                }

                // Root Motion compatibility warning
                if (_useRootMotion.boolValue)
                {
                    EditorGUILayout.Space(3);
                    if (isHybridAnimancer)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // Compact visual emphasis for Root Motion supported (theme-aware)
                        var rootMotionStyle = new GUIStyle(EditorStyles.boldLabel);
                        rootMotionStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f); // Green for supported
                        rootMotionStyle.fontSize = 13;

                        EditorGUILayout.LabelField("ROOT MOTION: ENABLED AND SUPPORTED", rootMotionStyle);
                        EditorGUILayout.Space(2);

                        EditorGUILayout.HelpBox(
                            "HybridAnimancerComponent fully supports Root Motion.\n" +
                            "States can control Root Motion via MovementContext.UseRootMotion.",
                            MessageType.Info);
                        EditorGUILayout.EndVertical();
                    }
                    else if (isRegularAnimancer)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // Compact visual emphasis for Root Motion not supported (theme-aware)
                        var rootMotionStyle = new GUIStyle(EditorStyles.boldLabel);
                        rootMotionStyle.normal.textColor = GetEmphasisColor();
                        rootMotionStyle.fontSize = 13;

                        EditorGUILayout.LabelField("ROOT MOTION: ENABLED BUT NOT SUPPORTED", rootMotionStyle);
                        EditorGUILayout.Space(2);

                        EditorGUILayout.HelpBox(
                            "AnimancerComponent does NOT support Root Motion.\n" +
                            "Root Motion will be automatically disabled at runtime.\n" +
                            "To use Root Motion, switch to HybridAnimancerComponent.",
                            MessageType.Info);
                        EditorGUILayout.EndVertical();
                    }
                }
            }
            else
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Assign an AnimancerComponent to use Animancer for animation control.\n" +
                    "• AnimancerComponent: Pure code control, no Root Motion\n" +
                    "• HybridAnimancerComponent: Supports Root Motion, requires Pro license",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draws the debug section with colored modules and foldouts.
        /// </summary>
        private void DrawDebugSection()
        {
            // Cache colors for performance
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
            
            // ===== MAIN DEBUG HEADER =====
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            headerStyle.normal.textColor = headerColor;
            
            EditorGUILayout.LabelField("🔧 Debug", headerStyle);
            
            // ===== SCENE VISUALIZATION =====
            DrawFoldoutPanel(
                ref _showDebugVisualization,
                "Scene Visualization",
                visualizationColor,
                new Color(0.4f, 0.6f, 0.8f, 0.15f),
                () =>
                {
                    if (_showDebugGizmos != null)
                    {
                        EditorGUILayout.PropertyField(_showDebugGizmos, new GUIContent(
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
            
            // ===== RUNTIME DEBUG INFO =====
            if (Application.isPlaying)
            {
                DrawFoldoutPanel(
                    ref _showRuntimeDebug,
                    "Runtime Info (Live)",
                    runtimeColor,
                    new Color(0.6f, 0.4f, 0.8f, 0.15f),
                    () =>
                    {
                        // State indicator with color
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
                        
                        // Speed info with comparison
                        if (_debugConfigRunSpeed != null && _debugActualSpeed != null)
                        {
                            float configSpeed = _debugConfigRunSpeed.floatValue;
                            float actualSpeed = _debugActualSpeed.floatValue;
                            bool speedOk = actualSpeed <= configSpeed + 0.5f;
                            
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Speed");
                            
                            var speedStyle = new GUIStyle(EditorStyles.label);
                            speedStyle.normal.textColor = speedOk ? speedOkColor : speedWarningColor;
                            
                            string speedText = $"{actualSpeed:F2} / {configSpeed:F1} m/s";
                            EditorGUILayout.LabelField(speedText, speedStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Input info
                        if (_debugInputDirection != null && _debugInputMagnitude != null)
                        {
                            Vector3 inputDir = _debugInputDirection.vector3Value;
                            float inputMag = _debugInputMagnitude.floatValue;
                            
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Input");
                            EditorGUILayout.LabelField($"({inputDir.x:F2}, {inputDir.z:F2}) mag: {inputMag:F2}");
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Velocity
                        if (_debugActualVelocity != null)
                        {
                            Vector3 vel = _debugActualVelocity.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Velocity");
                            EditorGUILayout.LabelField($"({vel.x:F2}, {vel.y:F2}, {vel.z:F2})");
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // DeltaTime
                        if (_debugDeltaTime != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("DeltaTime");
                            float dt = _debugDeltaTime.floatValue;
                            EditorGUILayout.LabelField($"{dt:F4}s ({(dt > 0 ? 1f/dt : 0):F0} FPS)");
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Config status
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
                
                // ===== MOVING PLATFORM DEBUG =====
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
                        // Ground collider info
                        if (_debugGroundColliderName != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Ground");
                            EditorGUILayout.LabelField(_debugGroundColliderName.stringValue);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Has Rigidbody check
                        if (_debugGroundHasRigidbody != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Has Rigidbody");
                            
                            var rbStyle = new GUIStyle(EditorStyles.label);
                            rbStyle.normal.textColor = _debugGroundHasRigidbody.boolValue ? platformOkColor : platformErrorColor;
                            EditorGUILayout.LabelField(_debugGroundHasRigidbody.boolValue ? "✓ Yes" : "✗ No (Required!)", rbStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Layer match check
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
                        
                        // On platform status
                        if (_debugOnPlatform != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("On Platform");
                            
                            var onPlatformStyle = new GUIStyle(EditorStyles.boldLabel);
                            onPlatformStyle.normal.textColor = _debugOnPlatform.boolValue ? platformOkColor : disabledColor;
                            EditorGUILayout.LabelField(_debugOnPlatform.boolValue ? "✓ YES" : "No", onPlatformStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Platform name
                        if (_debugPlatformName != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Platform");
                            EditorGUILayout.LabelField(_debugPlatformName.stringValue);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Platform velocity
                        if (_debugPlatformVelocity != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 vel = _debugPlatformVelocity.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Platform Vel");
                            EditorGUILayout.LabelField($"({vel.x:F2}, {vel.y:F2}, {vel.z:F2})");
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Platform delta position (actual movement applied this frame)
                        if (_debugPlatformDeltaPos != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 delta = _debugPlatformDeltaPos.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Frame Delta");
                            var deltaStyle = new GUIStyle(EditorStyles.label);
                            deltaStyle.normal.textColor = delta.sqrMagnitude > 0.00001f ? platformOkColor : disabledColor;
                            EditorGUILayout.LabelField($"({delta.x:F4}, {delta.y:F4}, {delta.z:F4})", deltaStyle);
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Local position on platform
                        if (_debugLocalPosition != null && _debugOnPlatform != null && _debugOnPlatform.boolValue)
                        {
                            Vector3 localPos = _debugLocalPosition.vector3Value;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Local Pos");
                            EditorGUILayout.LabelField($"({localPos.x:F2}, {localPos.y:F2}, {localPos.z:F2})");
                            EditorGUILayout.EndHorizontal();
                        }
                        
                        // Help text if not on platform
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
                
                // Force repaint when playing for live updates
                Repaint();
            }
            else
            {
                // Not playing - show placeholder
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
        
        /// <summary>
        /// Draws a foldout panel with colored title inside the helpBox.
        /// Uses the same pattern as "Help & Details" foldouts.
        /// </summary>
        private void DrawFoldoutPanel(ref bool foldout, string title, Color titleColor, Color bgColor, 
            System.Action drawContent, bool forceExpanded = false)
        {
            // Begin outer box with background color
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;
            
            // Create foldout style with custom color
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
            
            // Use indentLevel to keep foldout arrow inside the box (same as "Help & Details")
            EditorGUI.indentLevel++;
            foldout = EditorGUILayout.Foldout(foldout, title, true, foldoutStyle);
            EditorGUI.indentLevel--;
            
            // Draw content if expanded
            if (foldout || forceExpanded)
            {
                drawContent?.Invoke();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draws a compact legend for gizmo colors.
        /// </summary>
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