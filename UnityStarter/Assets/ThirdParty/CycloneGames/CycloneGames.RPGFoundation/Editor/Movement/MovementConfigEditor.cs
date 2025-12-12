using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Editor.Movement
{
    [CustomEditor(typeof(MovementConfig))]
    [CanEditMultipleObjects]
    public class MovementConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _walkSpeed;
        private SerializedProperty _runSpeed;
        private SerializedProperty _sprintSpeed;
        private SerializedProperty _crouchSpeed;
        private SerializedProperty _jumpForce;
        private SerializedProperty _maxJumpCount;
        private SerializedProperty _rollDistance;
        private SerializedProperty _rollDuration;
        private SerializedProperty _climbSpeed;
        private SerializedProperty _swimSpeed;
        private SerializedProperty _flySpeed;
        private SerializedProperty _gravity;
        private SerializedProperty _airControlMultiplier;
        private SerializedProperty _groundedCheckDistance;
        private SerializedProperty _groundLayer;
        private SerializedProperty _slopeLimit;
        private SerializedProperty _stepHeight;
        private SerializedProperty _rotationSpeed;
        private SerializedProperty _animationSystem;
        private SerializedProperty _animancerParameterMode;
        private SerializedProperty _movementSpeedParameter;
        private SerializedProperty _isGroundedParameter;
        private SerializedProperty _jumpTrigger;
        private SerializedProperty _rollTrigger;

        // Foldout states
        private bool _showSpecialMovementHelp = false;
        private bool _showPhysicsHelp = false;
        private bool _showRotationHelp = false;
        private bool _showAnimationSystemHelp = false;

        private void OnEnable()
        {
            _walkSpeed = serializedObject.FindProperty("walkSpeed");
            _runSpeed = serializedObject.FindProperty("runSpeed");
            _sprintSpeed = serializedObject.FindProperty("sprintSpeed");
            _crouchSpeed = serializedObject.FindProperty("crouchSpeed");
            _jumpForce = serializedObject.FindProperty("jumpForce");
            _maxJumpCount = serializedObject.FindProperty("maxJumpCount");
            _rollDistance = serializedObject.FindProperty("rollDistance");
            _rollDuration = serializedObject.FindProperty("rollDuration");
            _climbSpeed = serializedObject.FindProperty("climbSpeed");
            _swimSpeed = serializedObject.FindProperty("swimSpeed");
            _flySpeed = serializedObject.FindProperty("flySpeed");
            _gravity = serializedObject.FindProperty("gravity");
            _airControlMultiplier = serializedObject.FindProperty("airControlMultiplier");
            _groundedCheckDistance = serializedObject.FindProperty("groundedCheckDistance");
            _groundLayer = serializedObject.FindProperty("groundLayer");
            _slopeLimit = serializedObject.FindProperty("slopeLimit");
            _stepHeight = serializedObject.FindProperty("stepHeight");
            _rotationSpeed = serializedObject.FindProperty("rotationSpeed");
            _animationSystem = serializedObject.FindProperty("animationSystem");
            _animancerParameterMode = serializedObject.FindProperty("animancerParameterMode");
            _movementSpeedParameter = serializedObject.FindProperty("movementSpeedParameter");
            _isGroundedParameter = serializedObject.FindProperty("isGroundedParameter");
            _jumpTrigger = serializedObject.FindProperty("jumpTrigger");
            _rollTrigger = serializedObject.FindProperty("rollTrigger");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3D Movement Configuration", EditorStyles.boldLabel);

            EditorGUILayout.Space(10);

            // Ground Movement
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ground Movement", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_walkSpeed);
            EditorGUILayout.PropertyField(_runSpeed);
            EditorGUILayout.PropertyField(_sprintSpeed);
            EditorGUILayout.PropertyField(_crouchSpeed);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Jump
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Jump", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_jumpForce);
            EditorGUILayout.PropertyField(_maxJumpCount);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Special Movement
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Special Movement", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_rollDistance, new GUIContent(
                "Roll Distance",
                "Distance the character travels during a roll."));
            EditorGUILayout.PropertyField(_rollDuration, new GUIContent(
                "Roll Duration",
                "Time it takes to complete a roll animation."));
            EditorGUILayout.PropertyField(_climbSpeed, new GUIContent(
                "Climb Speed",
                "Speed when climbing walls or ladders."));
            EditorGUILayout.PropertyField(_swimSpeed, new GUIContent(
                "Swim Speed",
                "Speed when swimming in water."));
            EditorGUILayout.PropertyField(_flySpeed, new GUIContent(
                "Fly Speed",
                "Speed when flying (if flight is enabled)."));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showSpecialMovementHelp = EditorGUILayout.Foldout(_showSpecialMovementHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showSpecialMovementHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Special Movement Settings:\n" +
                    "• Roll: Quick dodge/evade movement\n" +
                    "  - Distance: How far the character travels\n" +
                    "  - Duration: How long the roll takes (affects speed)\n" +
                    "• Climb Speed: Speed when climbing vertical surfaces\n" +
                    "  - Used for wall climbing, ladder climbing\n" +
                    "• Swim Speed: Speed when in water\n" +
                    "  - Typically slower than ground movement\n" +
                    "• Fly Speed: Speed when flying\n" +
                    "  - Used for flight abilities or flying characters",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Physics
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Physics", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_gravity, new GUIContent(
                "Gravity",
                "Gravity strength. Negative values pull downward.\n" +
                "Typical values: -20 to -30"));
            EditorGUILayout.PropertyField(_airControlMultiplier, new GUIContent(
                "Air Control Multiplier",
                "How much control the player has while in the air.\n" +
                "1.0 = full control, 0.5 = half control, 0.0 = no control"));
            EditorGUILayout.PropertyField(_groundedCheckDistance, new GUIContent(
                "Grounded Check Distance",
                "Distance for ground detection.\n" +
                "Larger = more forgiving, smaller = more precise"));
            EditorGUILayout.PropertyField(_groundLayer, new GUIContent(
                "Ground Layer",
                "LayerMask for what counts as 'ground'.\n" +
                "Set this to your ground/platform layers to avoid false ground detection."));
            EditorGUILayout.PropertyField(_slopeLimit, new GUIContent(
                "Slope Limit",
                "Maximum angle (in degrees) the character can walk up.\n" +
                "Typical: 30-45 degrees"));
            EditorGUILayout.PropertyField(_stepHeight, new GUIContent(
                "Step Height",
                "Maximum height the character can step up.\n" +
                "Allows walking up small obstacles without jumping"));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showPhysicsHelp = EditorGUILayout.Foldout(_showPhysicsHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showPhysicsHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Physics Settings:\n" +
                    "• Gravity: Higher absolute values = faster falling\n" +
                    "  - Typical: -20 to -30 (realistic)\n" +
                    "  - More negative = stronger gravity\n" +
                    "• Air Control: Lower values (0.3-0.5) make air movement feel more realistic\n" +
                    "• Grounded Check Distance: How far to check for ground\n" +
                    "  - Too small: May miss ground when moving fast\n" +
                    "  - Too large: May detect ground when in air\n" +
                    "  - Typical: 0.1-0.3\n" +
                    "• Ground Layer: LayerMask for ground detection\n" +
                    "  - Set to your ground/platform layers\n" +
                    "  - Prevents false ground detection from walls/obstacles\n" +
                    "  - Important: Configure this to match your scene setup\n" +
                    "• Slope Limit: Maximum walkable slope angle\n" +
                    "  - Prevents walking up walls\n" +
                    "  - Typical: 30-45 degrees\n" +
                    "• Step Height: Maximum step-up height\n" +
                    "  - Allows smooth walking over small obstacles\n" +
                    "  - Typical: 0.2-0.4",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Rotation
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Rotation", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_rotationSpeed, new GUIContent(
                "Rotation Speed",
                "Speed at which the character rotates to face movement direction.\n" +
                "Higher = faster rotation, lower = smoother rotation"));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showRotationHelp = EditorGUILayout.Foldout(_showRotationHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showRotationHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Rotation Settings:\n" +
                    "• Rotation Speed: Controls how quickly character turns\n" +
                    "  - Higher values (20-30): Snappy, responsive turning\n" +
                    "  - Lower values (5-10): Smooth, gradual turning\n" +
                    "  - Typical: 15-25 for most games\n" +
                    "• The character will smoothly rotate to face the movement direction\n" +
                    "• This only affects rotation, not movement speed",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Animation System Configuration
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation System", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_animationSystem, new GUIContent(
                "Animation System",
                "Choose the animation system to use:\n" +
                "• Unity Animator: Standard Unity Animator Controller\n" +
                "• Animancer: Animancer animation system"));

            // Show Animancer parameter mode option only when Animancer is selected
            AnimationSystemType currentSystem = (AnimationSystemType)_animationSystem.enumValueIndex;
            if (currentSystem == AnimationSystemType.Animancer)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(_animancerParameterMode, new GUIContent(
                    "Animancer Parameter Mode",
                    "How to handle animation parameters when using Animancer:\n" +
                    "• Animator Hash: Use Animator hash values (for HybridAnimancerComponent with Animator Controller)\n" +
                    "• String Parameter: Use direct string parameters (for AnimancerComponent Parameters mode)"));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showAnimationSystemHelp = EditorGUILayout.Foldout(_showAnimationSystemHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showAnimationSystemHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Animancer Parameter Mode Guide:\n" +
                        "• Animator Hash Mode:\n" +
                        "  - Use with HybridAnimancerComponent\n" +
                        "  - Requires Animator Controller with parameters defined\n" +
                        "  - Parameters are accessed via Animator hash values\n" +
                        "  - Supports Root Motion (via HybridAnimancerComponent's Animator)\n" +
                        "• String Parameter Mode:\n" +
                        "  - Can use with HybridAnimancerComponent OR AnimancerComponent\n" +
                        "  - With HybridAnimancerComponent: Supports Root Motion (has Animator)\n" +
                        "  - With AnimancerComponent: Does NOT support Root Motion (no Animator)\n" +
                        "  - Parameters are accessed directly by string name\n" +
                        "  - Parameters are created automatically when first used (if using AnimancerComponent)\n" +
                        "  - If Animator Controller has parameters, will use Animator API\n" +
                        "  - If Animator Controller lacks parameters, will fallback to Parameters mode\n" +
                        "• Root Motion Support:\n" +
                        "  - HybridAnimancerComponent: ALWAYS supports Root Motion (has Animator)\n" +
                        "  - AnimancerComponent: NEVER supports Root Motion (no Animator)\n" +
                        "  - Root Motion support depends on component type, NOT parameter mode\n" +
                        "• Recommendation:\n" +
                        "  - Need Root Motion: Use HybridAnimancerComponent (any parameter mode works)\n" +
                        "  - Don't need Root Motion: Use AnimancerComponent + String Parameter",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Animation Parameters
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation Parameters", EditorStyles.miniLabel);

            // Show help text based on selected system
            if (currentSystem == AnimationSystemType.Animancer)
            {
                AnimancerParameterMode paramMode = (AnimancerParameterMode)_animancerParameterMode.enumValueIndex;
                if (paramMode == AnimancerParameterMode.AnimatorHash)
                {
                    EditorGUILayout.HelpBox(
                        "Animator Hash Mode: Ensure these parameter names match your Animator Controller parameters.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "String Parameter Mode: These parameters will be automatically created in Animancer when first used.",
                        MessageType.Info);
                }
                EditorGUILayout.Space(3);
            }

            EditorGUILayout.PropertyField(_movementSpeedParameter);
            EditorGUILayout.PropertyField(_isGroundedParameter);
            EditorGUILayout.PropertyField(_jumpTrigger);
            EditorGUILayout.PropertyField(_rollTrigger);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}