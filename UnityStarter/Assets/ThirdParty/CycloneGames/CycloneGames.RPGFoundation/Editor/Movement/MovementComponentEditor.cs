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
        private SerializedProperty _showGroundDetectionDebug;
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
            _showGroundDetectionDebug = serializedObject.FindProperty("showGroundDetectionDebug");
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

#if UNITY_EDITOR
            EditorGUILayout.Space(5);

            // Debug Visualization Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Debug Visualization", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(_showGroundDetectionDebug, new GUIContent(
                "Show Ground Detection Debug",
                "Show ground detection debug visualization in Scene view.\n" +
                "When enabled, displays the SphereCast used for ground detection:\n" +
                "• Green sphere: Ground detected within range\n" +
                "• Red sphere: No ground detected or out of range\n" +
                "• Yellow line: Ray direction\n" +
                "• Blue sphere: Character bottom position\n" +
                "• Cyan sphere: Ground hit point\n" +
                "• Magenta line: Ground normal\n" +
                "• Cyan line: Grounded check distance range\n" +
                "Editor only - this field is automatically removed in builds."));

            if (_showGroundDetectionDebug.boolValue)
            {
                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Debug visualization is active in Scene view.\n" +
                    "• Select this GameObject to see the visualization\n" +
                    "• Green = Ground detected and within range\n" +
                    "• Red = No ground or out of range\n" +
                    "• Use this to debug ground detection issues",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
#endif

            // Validate groundedCheckDistance against CharacterController's skinWidth
            ValidateGroundedCheckDistance();

            // Draw any additional fields from derived classes
            // This allows inheritance without modifying this editor
            DrawAdditionalProperties();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Validates that groundedCheckDistance is not smaller than CharacterController's skinWidth.
        /// Shows a warning if the configuration might cause ground detection issues.
        /// </summary>
        private void ValidateGroundedCheckDistance()
        {
            var component = target as MovementComponent;
            if (component == null) return;

            var characterController = component.GetComponent<CharacterController>();
            if (characterController == null) return;

            var config = _config.objectReferenceValue as MovementConfig;
            if (config == null) return;

            float skinWidth = characterController.skinWidth;
            float groundedCheckDistance = config.groundedCheckDistance;

            // Check if groundedCheckDistance is smaller than skinWidth
            if (groundedCheckDistance < skinWidth)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var warningStyle = new GUIStyle(EditorStyles.boldLabel);
                warningStyle.normal.textColor = new Color(1f, 0.6f, 0f); // Orange

                EditorGUILayout.LabelField("⚠️ Ground Detection Configuration Warning", warningStyle);
                EditorGUILayout.Space(3);

                EditorGUILayout.HelpBox(
                    $"Grounded Check Distance ({groundedCheckDistance:F3}) is smaller than CharacterController's Skin Width ({skinWidth:F3}).\n\n" +
                    "This may cause ground detection issues:\n" +
                    "• CharacterController maintains at least skinWidth distance from ground\n" +
                    "• If groundedCheckDistance < skinWidth, custom detection may never succeed\n" +
                    "• Character may appear grounded (via CharacterController) but custom detection fails\n\n" +
                    "Recommendation:\n" +
                    $"• Set Grounded Check Distance to at least {skinWidth:F3} (or larger)\n" +
                    "• Or reduce CharacterController's Skin Width to match your needs\n" +
                    "• Typical: skinWidth = 0.01-0.02, groundedCheckDistance = 0.03-0.05",
                    MessageType.Warning);

                EditorGUILayout.Space(3);

                // Show current values for reference
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("CharacterController Skin Width:", GUILayout.Width(200));
                EditorGUILayout.FloatField(skinWidth);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Config Grounded Check Distance:", GUILayout.Width(200));
                EditorGUILayout.FloatField(groundedCheckDistance);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Difference:", GUILayout.Width(200));
                EditorGUILayout.FloatField(skinWidth - groundedCheckDistance);
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>
        /// Draws any additional properties from derived classes.
        /// Excludes already-drawn properties to avoid duplication.
        /// This method allows derived classes to add new fields without modifying this editor.
        /// </summary>
        private void DrawAdditionalProperties()
        {
            // List of properties that are already manually drawn above
            string[] excludedProperties = new string[]
            {
                "config",
                "characterAnimator",
                "animancerComponent",
                "worldUpSource",
                "useRootMotion",
                "ignoreTimeScale",
#if UNITY_EDITOR
                "showGroundDetectionDebug",
#endif
                // Script reference is always excluded by default
                "m_Script"
            };

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
                foreach (string excludedName in excludedProperties)
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
    }
}