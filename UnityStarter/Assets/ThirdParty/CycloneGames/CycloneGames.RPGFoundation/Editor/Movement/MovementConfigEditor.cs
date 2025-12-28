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
        private SerializedProperty _climbingParameter;
        private SerializedProperty _wallSlidingParameter;

        // Moving Platform
        private SerializedProperty _enableMovingPlatform;
        private SerializedProperty _inheritPlatformRotation;
        private SerializedProperty _inheritPlatformMomentum;
        private SerializedProperty _platformLayer;

        // Foldout states
        private bool _showSpecialMovementHelp = false;
        private bool _showPhysicsHelp = false;
        private bool _showMovingPlatformHelp = false;
        private bool _showCeilingDetectionHelp = false;
        private bool _showGapBridgingHelp = false;
        private bool _showRotationHelp = false;
        private bool _showAnimationSystemHelp = false;
        private bool _showClimbingHelp = false;

        // Ceiling Detection
        private SerializedProperty _enableCeilingDetection;
        private SerializedProperty _ceilingCheckDistance;

        // Gap Bridging
        private SerializedProperty _enableGapBridging;
        private SerializedProperty _minSpeedForGapBridge;
        private SerializedProperty _maxGapDistance;
        private SerializedProperty _maxGapHeightDiff;

        // Ladder Climbing
        private SerializedProperty _enableLadderClimbing;
        private SerializedProperty _ladderClimbSpeed;
        private SerializedProperty _ladderLayer;

        // Wall Climbing
        private SerializedProperty _enableWallClimbing;
        private SerializedProperty _wallClimbSpeed;
        private SerializedProperty _wallLayer;
        private SerializedProperty _wallCheckDistance;
        private SerializedProperty _wallClingDuration;
        private SerializedProperty _wallSlideSpeed;

        // Wall Jump
        private SerializedProperty _enableWallJump;
        private SerializedProperty _wallJumpForceHorizontal;
        private SerializedProperty _wallJumpForceVertical;
        private SerializedProperty _wallJumpCooldown;
        private SerializedProperty _differentWallAngle;

        // AI Pathfinding
        private SerializedProperty _pathfindingSystem;
        private bool _showPathfindingHelp = false;

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
            _climbingParameter = serializedObject.FindProperty("climbingParameter");
            _wallSlidingParameter = serializedObject.FindProperty("wallSlidingParameter");
            _enableMovingPlatform = serializedObject.FindProperty("enableMovingPlatform");
            _inheritPlatformRotation = serializedObject.FindProperty("inheritPlatformRotation");
            _inheritPlatformMomentum = serializedObject.FindProperty("inheritPlatformMomentum");
            _platformLayer = serializedObject.FindProperty("platformLayer");
            _enableCeilingDetection = serializedObject.FindProperty("enableCeilingDetection");
            _ceilingCheckDistance = serializedObject.FindProperty("ceilingCheckDistance");
            _enableGapBridging = serializedObject.FindProperty("enableGapBridging");
            _minSpeedForGapBridge = serializedObject.FindProperty("minSpeedForGapBridge");
            _maxGapDistance = serializedObject.FindProperty("maxGapDistance");
            _maxGapHeightDiff = serializedObject.FindProperty("maxGapHeightDiff");
            
            // Ladder Climbing
            _enableLadderClimbing = serializedObject.FindProperty("enableLadderClimbing");
            _ladderClimbSpeed = serializedObject.FindProperty("ladderClimbSpeed");
            _ladderLayer = serializedObject.FindProperty("ladderLayer");
            
            // Wall Climbing
            _enableWallClimbing = serializedObject.FindProperty("enableWallClimbing");
            _wallClimbSpeed = serializedObject.FindProperty("wallClimbSpeed");
            _wallLayer = serializedObject.FindProperty("wallLayer");
            _wallCheckDistance = serializedObject.FindProperty("wallCheckDistance");
            _wallClingDuration = serializedObject.FindProperty("wallClingDuration");
            _wallSlideSpeed = serializedObject.FindProperty("wallSlideSpeed");
            
            // Wall Jump
            _enableWallJump = serializedObject.FindProperty("enableWallJump");
            _wallJumpForceHorizontal = serializedObject.FindProperty("wallJumpForceHorizontal");
            _wallJumpForceVertical = serializedObject.FindProperty("wallJumpForceVertical");
            _wallJumpCooldown = serializedObject.FindProperty("wallJumpCooldown");
            _differentWallAngle = serializedObject.FindProperty("differentWallAngle");
            
            _pathfindingSystem = serializedObject.FindProperty("pathfindingSystem");
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
                "Maximum allowed distance from character bottom to ground.\n" +
                "If detected ground is within this distance, character is considered grounded.\n" +
                "NOT the detection ray distance, but a threshold for judging if grounded.\n" +
                "• 0.01-0.03: Precise (recommended, default 0.03)\n" +
                "• 0.05-0.1: More forgiving, for fast movement or uneven terrain\n" +
                "• Larger values may cause visible floating"));
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
                    "• Grounded Check Distance: Maximum distance from character bottom to ground\n" +
                    "  - This is a THRESHOLD, not the detection ray distance\n" +
                    "  - If ground is detected within this distance, character is grounded\n" +
                    "  - Too small (0.01): Very precise, may miss ground when moving fast\n" +
                    "  - Recommended (0.03): Good balance, default value\n" +
                    "  - Too large (0.1+): May cause visible floating above ground\n" +
                    "  - ⚠️ IMPORTANT: Should be >= CharacterController's skinWidth\n" +
                    "    If smaller than skinWidth, ground detection may fail\n" +
                    "    Check MovementComponent Inspector for validation warning\n" +
                    "  - Typical: skinWidth = 0.01-0.02, groundedCheckDistance = 0.03-0.05\n" +
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

            // Moving Platform
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Moving Platform", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_enableMovingPlatform, new GUIContent(
                "Enable Moving Platform",
                "Enable moving platform support. Character will move with platforms.\n" +
                "Requires platform to have a Rigidbody component."));

            if (_enableMovingPlatform.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_inheritPlatformRotation, new GUIContent(
                    "Inherit Platform Rotation",
                    "Character will rotate with rotating platforms.\n" +
                    "Disable for platforms that only translate."));
                EditorGUILayout.PropertyField(_inheritPlatformMomentum, new GUIContent(
                    "Inherit Platform Momentum",
                    "Character keeps platform velocity when jumping off.\n" +
                    "Creates natural feeling when jumping from moving platforms."));
                EditorGUILayout.PropertyField(_platformLayer, new GUIContent(
                    "Platform Layer",
                    "LayerMask for detecting moving platforms.\n" +
                    "If empty, uses Ground Layer instead."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showMovingPlatformHelp = EditorGUILayout.Foldout(_showMovingPlatformHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showMovingPlatformHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Moving Platform Support:\n" +
                    "• Platform Requirements:\n" +
                    "  - Must have a Collider component\n" +
                    "  - Layer must match Platform Layer (or Ground Layer if empty)\n" +
                    "  - Rigidbody is optional (velocity calculated from Transform delta)\n" +
                    "• Features:\n" +
                    "  - Character automatically moves with platform\n" +
                    "  - Supports both translation and rotation\n" +
                    "  - Zero allocation design\n" +
                    "• Inherit Rotation: Enable for rotating platforms\n" +
                    "  - Disable for horizontal/vertical moving platforms only",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Ceiling Detection
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ceiling Detection", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_enableCeilingDetection, new GUIContent(
                "Enable Ceiling Detection",
                "Prevent character from clipping through ceilings during jumps."));

            if (_enableCeilingDetection.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_ceilingCheckDistance, new GUIContent(
                    "Check Distance",
                    "Extra distance above character head to check for ceiling."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showCeilingDetectionHelp = EditorGUILayout.Foldout(_showCeilingDetectionHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showCeilingDetectionHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Ceiling Detection:\n" +
                    "• Prevents head clipping through low ceilings\n" +
                    "• Only active when character is moving upward\n" +
                    "• Uses spherecast for accurate detection\n" +
                    "• Stops vertical velocity when ceiling is detected",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Gap Bridging
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Gap Bridging", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_enableGapBridging, new GUIContent(
                "Enable Gap Bridging",
                "Auto-jump across small gaps when running at sufficient speed."));

            if (_enableGapBridging.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_minSpeedForGapBridge, new GUIContent(
                    "Min Speed",
                    "Minimum speed (m/s) required to trigger gap bridging."));
                EditorGUILayout.PropertyField(_maxGapDistance, new GUIContent(
                    "Max Gap Distance",
                    "Maximum gap width (m) that can be bridged."));
                EditorGUILayout.PropertyField(_maxGapHeightDiff, new GUIContent(
                    "Max Height Diff",
                    "Maximum height difference (m) allowed for gap bridging."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showGapBridgingHelp = EditorGUILayout.Foldout(_showGapBridgingHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showGapBridgingHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Gap Bridging (Auto-Jump):\n" +
                    "• Character auto-jumps when running toward a gap\n" +
                    "• Only triggers at sufficient speed\n" +
                    "• Scans for landing point across the gap\n" +
                    "• Jump height is calculated based on gap height difference\n" +
                    "• Has cooldown to prevent rapid re-triggering",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Climbing
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Climbing", EditorStyles.miniLabel);

            // Ladder Climbing
            EditorGUILayout.PropertyField(_enableLadderClimbing, new GUIContent(
                "Enable Ladder Climbing",
                "Enable climbing on ladders via trigger zones."));

            if (_enableLadderClimbing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_ladderClimbSpeed, new GUIContent(
                    "Ladder Climb Speed",
                    "Speed when climbing ladders."));
                EditorGUILayout.PropertyField(_ladderLayer, new GUIContent(
                    "Ladder Layer",
                    "Layer mask for ladder detection."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // Wall Climbing
            EditorGUILayout.PropertyField(_enableWallClimbing, new GUIContent(
                "Enable Wall Climbing",
                "Enable clinging to and climbing walls."));

            if (_enableWallClimbing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_wallClimbSpeed, new GUIContent(
                    "Wall Climb Speed",
                    "Speed when climbing walls."));
                EditorGUILayout.PropertyField(_wallLayer, new GUIContent(
                    "Wall Layer",
                    "Layer mask for wall detection."));
                EditorGUILayout.PropertyField(_wallCheckDistance, new GUIContent(
                    "Wall Check Distance",
                    "Distance for wall detection raycast."));
                EditorGUILayout.PropertyField(_wallClingDuration, new GUIContent(
                    "Cling Duration",
                    "Time character can cling before sliding."));
                EditorGUILayout.PropertyField(_wallSlideSpeed, new GUIContent(
                    "Slide Speed",
                    "Speed when sliding down wall."));

                EditorGUILayout.Space(3);

                // Wall Jump
                EditorGUILayout.PropertyField(_enableWallJump, new GUIContent(
                    "Enable Wall Jump",
                    "Enable jumping off walls."));

                if (_enableWallJump.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_wallJumpForceHorizontal, new GUIContent(
                        "Wall Jump Force H",
                        "Horizontal force when wall jumping."));
                    EditorGUILayout.PropertyField(_wallJumpForceVertical, new GUIContent(
                        "Wall Jump Force V",
                        "Vertical force when wall jumping."));
                    EditorGUILayout.PropertyField(_wallJumpCooldown, new GUIContent(
                        "Wall Jump Cooldown",
                        "Cooldown before re-clinging to same wall."));
                    EditorGUILayout.PropertyField(_differentWallAngle, new GUIContent(
                        "Different Wall Angle",
                        "Minimum angle difference (degrees) to consider as different wall.\nRecommended: 60° for normal walls, 90° for strict, 45° for lenient."));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showClimbingHelp = EditorGUILayout.Foldout(_showClimbingHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showClimbingHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Climbing System:\n" +
                    "• Ladder: Enter zone + press Up to climb\n" +
                    "  - No gravity, free vertical movement\n" +
                    "  - Jump to detach\n" +
                    "• Wall: In air + near wall + move toward wall\n" +
                    "  - Cling for duration, then slide down\n" +
                    "  - Wall Jump: Push off wall at angle\n" +
                    "  - Supports continuous wall jump in narrow spaces\n" +
                    "  - Different Wall Angle: Controls what counts as 'different wall'\n" +
                    "    for consecutive wall jumps",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // AI Pathfinding
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("AI Pathfinding", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_pathfindingSystem, new GUIContent(
                "Pathfinding System",
                "Select the pathfinding system for AI navigation.\n" +
                "Requires the corresponding package to be installed."));

            // Show availability status
            var system = (CycloneGames.RPGFoundation.Runtime.Movement.PathfindingSystem)_pathfindingSystem.enumValueIndex;
            string statusMessage = GetPathfindingStatus(system);
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Warning);
            }

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showPathfindingHelp = EditorGUILayout.Foldout(_showPathfindingHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showPathfindingHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "AI Pathfinding Systems:\n" +
                    "• None: Manual control only\n" +
                    "• Unity NavMesh: Built-in, requires com.unity.ai.navigation\n" +
                    "• A* Pathfinding: Third-party, requires com.arongranberg.astar\n" +
                    "• Agents Navigation: DOTS-based, requires com.projectdawn.navigation\n\n" +
                    "Add corresponding provider component to AI characters.",
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
            AnimancerParameterMode paramMode = AnimancerParameterMode.StringParameter;

            if (currentSystem == AnimationSystemType.Animancer)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(_animancerParameterMode, new GUIContent(
                    "Animancer Parameter Mode",
                    "How to handle animation parameters when using Animancer:\n" +
                    "• Animator Hash: Use Animator hash values (for HybridAnimancerComponent with Animator Controller)\n" +
                    "• String Parameter: Use direct string parameters (for AnimancerComponent Parameters mode)"));

                paramMode = (AnimancerParameterMode)_animancerParameterMode.enumValueIndex;

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showAnimationSystemHelp = EditorGUILayout.Foldout(_showAnimationSystemHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showAnimationSystemHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Animancer Parameter Mode Guide:\n\n" +
                        "【Animator Hash Mode】\n" +
                        "• Use with: HybridAnimancerComponent + Animator Controller\n" +
                        "• Setup: Animator Controller must have parameters defined\n" +
                        "• Root Motion: ✓ Supported (via HybridAnimancerComponent's Animator)\n" +
                        "• If Animator has no Controller assigned: Parameters are safely ignored\n\n" +
                        "【String Parameter Mode】\n" +
                        "• Use with: HybridAnimancerComponent OR AnimancerComponent\n" +
                        "• Setup: Parameters are created automatically in Animancer when first used\n" +
                        "• With HybridAnimancerComponent: Root Motion ✓ Supported\n" +
                        "• With AnimancerComponent: Root Motion ✗ NOT Supported\n\n" +
                        "【Using Custom Animation Controller?】\n" +
                        "If you handle animations separately (e.g., via Animancer API directly,\n" +
                        "like RPGPlayerCharacterAnimationController using OnJumpStart event):\n" +
                        "→ Leave ALL parameter fields empty below to disable built-in control\n\n" +
                        "【Quick Reference】\n" +
                        "• Need Root Motion? → Use HybridAnimancerComponent\n" +
                        "• Simple setup? → Animator Hash + Animator Controller with parameters\n" +
                        "• Custom control? → Clear all parameters, use events (OnJumpStart, OnLanded)",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Animation Parameters - Show contextual help based on configuration
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation Parameters", EditorStyles.miniLabel);

            // Check if all parameters are empty
            bool allEmpty = string.IsNullOrEmpty(_movementSpeedParameter.stringValue) &&
                           string.IsNullOrEmpty(_isGroundedParameter.stringValue) &&
                           string.IsNullOrEmpty(_jumpTrigger.stringValue) &&
                           string.IsNullOrEmpty(_rollTrigger.stringValue);

            bool hasAnyParameter = !allEmpty;

            // Determine and show appropriate help message
            if (currentSystem == AnimationSystemType.Animancer)
            {
                if (paramMode == AnimancerParameterMode.AnimatorHash)
                {
                    if (allEmpty)
                    {
                        EditorGUILayout.HelpBox(
                            "✓ Direct API Mode: All parameters empty.\n" +
                            "MovementComponent will NOT control animations.\n" +
                            "Use this when you have a custom animation controller that handles animations via events (OnJumpStart, OnLanded).",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Parameter-Based Mode: These parameters will be set on your Animator Controller.\n\n" +
                            "Requirements:\n" +
                            "• Animator component must have a Controller assigned\n" +
                            "• Controller must have parameters with matching names\n" +
                            "• Parameter types: Speed=Float, IsGrounded=Bool, Jump/Roll=Trigger\n\n" +
                            "⚠ If no Animator Controller: Parameters will be sent to Animancer's\n" +
                            "   Parameters system as Bool values (Triggers won't auto-reset!).",
                            MessageType.Warning);
                    }
                }
                else
                {
                    // String Parameter mode
                    if (hasAnyParameter)
                    {
                        EditorGUILayout.HelpBox(
                            "String Parameter Mode: Parameters auto-created in Animancer.\n\n" +
                            "⚠ Triggers (Jump, Roll) will be created as Bool and won't auto-reset!\n" +
                            "   You may need to manually reset them, or use Direct API Mode instead\n" +
                            "   (clear all parameters and handle animations via OnJumpStart event).",
                            MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "✓ Direct API Mode: Parameters will be auto-created when used.\n" +
                            "Leave empty to handle animations via custom controller.",
                            MessageType.Info);
                    }
                }
                EditorGUILayout.Space(3);
            }
            else
            {
                // Unity Animator mode
                EditorGUILayout.HelpBox(
                    "Unity Animator Mode: These parameter names must exist in your Animator Controller.\n" +
                    "Parameter types: Speed=Float, IsGrounded=Bool, Jump/Roll=Trigger",
                    MessageType.Info);
                EditorGUILayout.Space(3);
            }

            EditorGUILayout.PropertyField(_movementSpeedParameter, new GUIContent(
                "Movement Speed Parameter",
                "Float parameter for movement speed (used in Blend Trees).\n" +
                "Leave empty to skip setting this parameter."));
            EditorGUILayout.PropertyField(_isGroundedParameter, new GUIContent(
                "Is Grounded Parameter",
                "Bool parameter for grounded state.\n" +
                "Leave empty to skip setting this parameter."));
            EditorGUILayout.PropertyField(_jumpTrigger, new GUIContent(
                "Jump Trigger",
                "Trigger parameter for jump animation.\n" +
                "Leave empty to skip setting this parameter."));
            EditorGUILayout.PropertyField(_rollTrigger, new GUIContent(
                "Roll Trigger",
                "Trigger parameter for roll/dodge animation.\n" +
                "Leave empty to skip setting this parameter."));
            EditorGUILayout.PropertyField(_climbingParameter, new GUIContent(
                "Climbing Parameter",
                "Bool parameter for climbing state.\n" +
                "Set to true when entering ClimbState, false when exiting.\n" +
                "Leave empty to skip setting this parameter."));
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private string GetPathfindingStatus(CycloneGames.RPGFoundation.Runtime.Movement.PathfindingSystem system)
        {
            switch (system)
            {
                case CycloneGames.RPGFoundation.Runtime.Movement.PathfindingSystem.UnityNavMesh:
#if !UNITY_AI_NAVIGATION
                    return "Unity AI Navigation package (com.unity.ai.navigation) is not installed.";
#else
                    return null;
#endif
                case CycloneGames.RPGFoundation.Runtime.Movement.PathfindingSystem.AStarPathfinding:
#if !ASTAR_PATHFINDING
                    return "A* Pathfinding Project (com.arongranberg.astar) is not installed.";
#else
                    return null;
#endif
                case CycloneGames.RPGFoundation.Runtime.Movement.PathfindingSystem.AgentsNavigation:
#if !AGENTS_NAVIGATION
                    return "Agents Navigation (com.projectdawn.navigation) is not installed.";
#else
                    return null;
#endif
                default:
                    return null;
            }
        }
    }
}