using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Movement2D;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Editor.Movement
{
    [CustomEditor(typeof(MovementConfig2D))]
    [CanEditMultipleObjects]
    public class MovementConfig2DEditor : UnityEditor.Editor
    {
        private SerializedProperty _movementType;
        private SerializedProperty _walkSpeed;
        private SerializedProperty _runSpeed;
        private SerializedProperty _sprintSpeed;
        private SerializedProperty _crouchSpeed;
        private SerializedProperty _jumpForce;
        private SerializedProperty _maxJumpCount;
        private SerializedProperty _airControlMultiplier;
        private SerializedProperty _coyoteTime;
        private SerializedProperty _jumpBufferTime;
        private SerializedProperty _gravity;
        private SerializedProperty _maxFallSpeed;
        private SerializedProperty _groundCheckDistance;
        private SerializedProperty _groundLayer;
        private SerializedProperty _groundCheckSize;
        private SerializedProperty _groundCheckOffset;
        private SerializedProperty _lockZAxis;
        private SerializedProperty _slideSpeed;
        private SerializedProperty _wallJumpForceX;
        private SerializedProperty _wallJumpForceY;
        private SerializedProperty _facingRight;
        private SerializedProperty _animationSystem;
        private SerializedProperty _animancerParameterMode;
        private SerializedProperty _movementSpeedParameter;
        private SerializedProperty _isGroundedParameter;
        private SerializedProperty _jumpTrigger;
        private SerializedProperty _verticalSpeedParameter;
        private SerializedProperty _rollTrigger;
        private SerializedProperty _inputXParameter;
        private SerializedProperty _inputYParameter;

        // Moving Platform
        private SerializedProperty _enableMovingPlatform;
        private SerializedProperty _inheritPlatformRotation;
        private SerializedProperty _inheritPlatformMomentum;
        private SerializedProperty _platformLayer;

        // Foldout states
        private bool _showMovementTypeHelp = false;
        private bool _showAirMovementHelp = false;
        private bool _showPhysicsHelp = false;
        private bool _showGroundDetectionHelp = false;
        private bool _showMovingPlatformHelp = false;
        private bool _showGapBridgingHelp = false;
        private bool _showOtherHelp = false;
        private bool _showFacingHelp = false;
        private bool _showAnimationSystemHelp = false;

        // Gap Bridging
        private SerializedProperty _enableGapBridging;
        private SerializedProperty _minSpeedForGapBridge;
        private SerializedProperty _maxGapDistance;

        private void OnEnable()
        {
            _movementType = serializedObject.FindProperty("movementType");
            _walkSpeed = serializedObject.FindProperty("walkSpeed");
            _runSpeed = serializedObject.FindProperty("runSpeed");
            _sprintSpeed = serializedObject.FindProperty("sprintSpeed");
            _crouchSpeed = serializedObject.FindProperty("crouchSpeed");
            _jumpForce = serializedObject.FindProperty("jumpForce");
            _maxJumpCount = serializedObject.FindProperty("maxJumpCount");
            _airControlMultiplier = serializedObject.FindProperty("airControlMultiplier");
            _coyoteTime = serializedObject.FindProperty("coyoteTime");
            _jumpBufferTime = serializedObject.FindProperty("jumpBufferTime");
            _gravity = serializedObject.FindProperty("gravity");
            _maxFallSpeed = serializedObject.FindProperty("maxFallSpeed");
            _groundCheckDistance = serializedObject.FindProperty("groundCheckDistance");
            _groundLayer = serializedObject.FindProperty("groundLayer");
            _groundCheckSize = serializedObject.FindProperty("groundCheckSize");
            _groundCheckOffset = serializedObject.FindProperty("groundCheckOffset");
            _lockZAxis = serializedObject.FindProperty("lockZAxis");
            _slideSpeed = serializedObject.FindProperty("slideSpeed");
            _wallJumpForceX = serializedObject.FindProperty("wallJumpForceX");
            _wallJumpForceY = serializedObject.FindProperty("wallJumpForceY");
            _facingRight = serializedObject.FindProperty("facingRight");
            _animationSystem = serializedObject.FindProperty("animationSystem");
            _animancerParameterMode = serializedObject.FindProperty("animancerParameterMode");
            _movementSpeedParameter = serializedObject.FindProperty("movementSpeedParameter");
            _isGroundedParameter = serializedObject.FindProperty("isGroundedParameter");
            _jumpTrigger = serializedObject.FindProperty("jumpTrigger");
            _verticalSpeedParameter = serializedObject.FindProperty("verticalSpeedParameter");
            _rollTrigger = serializedObject.FindProperty("rollTrigger");
            _inputXParameter = serializedObject.FindProperty("inputXParameter");
            _inputYParameter = serializedObject.FindProperty("inputYParameter");
            _enableMovingPlatform = serializedObject.FindProperty("enableMovingPlatform");
            _inheritPlatformRotation = serializedObject.FindProperty("inheritPlatformRotation");
            _inheritPlatformMomentum = serializedObject.FindProperty("inheritPlatformMomentum");
            _platformLayer = serializedObject.FindProperty("platformLayer");
            _enableGapBridging = serializedObject.FindProperty("enableGapBridging");
            _minSpeedForGapBridge = serializedObject.FindProperty("minSpeedForGapBridge");
            _maxGapDistance = serializedObject.FindProperty("maxGapDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2D Movement Configuration", EditorStyles.boldLabel);

            // Movement Type
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_movementType, new GUIContent(
                "Movement Type",
                "Type of 2D movement system:\n" +
                "• Platformer: Standard side-scroller (X/Y movement, Gravity on Y)\n" +
                "• BeltScroll: DNF Style (X/Z movement, Y is Jump/Height)\n" +
                "• TopDown: Classic RPG Style (X/Y movement, No Gravity, No Jump)"));

            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            _showMovementTypeHelp = EditorGUILayout.Foldout(_showMovementTypeHelp, "Help & Details", EditorStyles.foldout);
            EditorGUI.indentLevel--;
            if (_showMovementTypeHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Movement Type Guide:\n" +
                    "• Platformer: Standard 2D side-scroller like Super Mario\n" +
                    "  - X axis: Left/Right movement\n" +
                    "  - Y axis: Jump/Height (Gravity affects Y)\n" +
                    "  - Supports: Jump, Fall, Ground detection\n" +
                    "• BeltScroll: DNF (Dungeon & Fighter) style\n" +
                    "  - X axis: Left/Right movement\n" +
                    "  - Z axis: Forward/Backward movement (mapped from Input Y)\n" +
                    "  - Y axis: Jump/Height (Gravity affects Y)\n" +
                    "  - Camera follows character on a belt/rail\n" +
                    "• TopDown: Classic RPG style like Final Fantasy\n" +
                    "  - X axis: Left/Right movement\n" +
                    "  - Y axis: Up/Down movement\n" +
                    "  - No gravity, no jump\n" +
                    "  - Uses Animator BlendTree for 4-direction sprites",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            // Get current movement type
            MovementType2D currentType = (MovementType2D)_movementType.enumValueIndex;

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

            // Jump - Only for Platformer and BeltScroll
            if (currentType != MovementType2D.TopDown)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Jump (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_jumpForce);
                EditorGUILayout.PropertyField(_maxJumpCount);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Air Movement - Only for Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Air Movement (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_airControlMultiplier, new GUIContent(
                    "Air Control Multiplier",
                    "How much control the player has while in the air.\n" +
                    "1.0 = full control, 0.5 = half control, 0.0 = no control"));
                EditorGUILayout.PropertyField(_coyoteTime, new GUIContent(
                    "Coyote Time",
                    "Time window after leaving ground where player can still jump.\n" +
                    "Makes jumping feel more forgiving. Recommended: 0.1-0.2 seconds"));
                EditorGUILayout.PropertyField(_jumpBufferTime, new GUIContent(
                    "Jump Buffer Time",
                    "Time window before landing where jump input is remembered.\n" +
                    "Makes jumping feel more responsive. Recommended: 0.1-0.2 seconds"));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showAirMovementHelp = EditorGUILayout.Foldout(_showAirMovementHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showAirMovementHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Air Movement Tips:\n" +
                        "• Air Control: Lower values (0.3-0.5) make air movement feel more realistic\n" +
                        "• Coyote Time: Allows players to jump slightly after leaving a platform\n" +
                        "  - Reduces frustration from missed jumps\n" +
                        "  - Typical values: 0.1-0.2 seconds\n" +
                        "• Jump Buffer: Remembers jump input before landing\n" +
                        "  - Makes platforming feel more responsive\n" +
                        "  - Typical values: 0.1-0.2 seconds\n" +
                        "• These features only apply to Platformer and BeltScroll modes",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Physics - Only for Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Physics (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_gravity, new GUIContent(
                    "Gravity",
                    "Gravity strength. Positive values pull downward.\n" +
                    "Platformer/BeltScroll: Typically 20-30"));
                EditorGUILayout.PropertyField(_maxFallSpeed, new GUIContent(
                    "Max Fall Speed",
                    "Maximum falling speed to prevent infinite acceleration.\n" +
                    "Typical values: 15-25"));
                EditorGUILayout.PropertyField(_groundCheckDistance, new GUIContent(
                    "Ground Check Distance",
                    "Distance for ground detection raycast.\n" +
                    "Larger = more forgiving, smaller = more precise"));
                EditorGUILayout.PropertyField(_groundLayer, new GUIContent(
                    "Ground Layer",
                    "LayerMask for what counts as 'ground'.\n" +
                    "Set this to your ground/platform layers"));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showPhysicsHelp = EditorGUILayout.Foldout(_showPhysicsHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showPhysicsHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Physics Settings:\n" +
                        "• Gravity: Higher values = faster falling\n" +
                        "  - Platformer: 20-30 (realistic)\n" +
                        "  - BeltScroll: 20-30 (similar to Platformer)\n" +
                        "• Max Fall Speed: Prevents characters from falling too fast\n" +
                        "  - Prevents clipping through platforms\n" +
                        "  - Typical: 15-25\n" +
                        "• Ground Check Distance: How far to check for ground\n" +
                        "  - Too small: May miss ground when moving fast\n" +
                        "  - Too large: May detect ground when in air\n" +
                        "  - Typical: 0.1-0.2",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Ground Detection - Only for Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Ground Detection (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_groundCheckSize, new GUIContent(
                    "Ground Check Size",
                    "Size of the ground detection box (width, height).\n" +
                    "Larger = more forgiving, smaller = more precise."));
                EditorGUILayout.PropertyField(_groundCheckOffset, new GUIContent(
                    "Ground Check Offset",
                    "Offset from character position for ground check point.\n" +
                    "Use (0, -0.5) to check at character's feet."));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showGroundDetectionHelp = EditorGUILayout.Foldout(_showGroundDetectionHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showGroundDetectionHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Ground Detection Settings:\n" +
                        "• Only used in Platformer and BeltScroll modes\n" +
                        "• Ground Check Size:\n" +
                        "  - Width: Should match character width (0.8-1.0)\n" +
                        "  - Height: Detection depth (0.1-0.2)\n" +
                        "  - Larger = more forgiving but may detect walls as ground\n" +
                        "  - Smaller = more precise but may miss ground when moving fast\n" +
                        "• Ground Check Offset:\n" +
                        "  - Position relative to character for detection point\n" +
                        "  - (0, -0.5) = at character's feet\n" +
                        "  - Adjust based on character pivot point\n" +
                        "• Detection uses Physics2D.OverlapBox",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Other Settings - Only for Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Other Settings (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_lockZAxis);
                EditorGUILayout.PropertyField(_slideSpeed, new GUIContent(
                    "Slide Speed",
                    "Speed when sliding down slopes or walls."));
                EditorGUILayout.PropertyField(_wallJumpForceX, new GUIContent(
                    "Wall Jump Force X",
                    "Horizontal force when performing a wall jump."));
                EditorGUILayout.PropertyField(_wallJumpForceY, new GUIContent(
                    "Wall Jump Force Y",
                    "Vertical force when performing a wall jump."));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showOtherHelp = EditorGUILayout.Foldout(_showOtherHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showOtherHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Other Settings:\n" +
                        "• Lock Z Axis: Prevents character from moving on Z axis\n" +
                        "  - Essential for 2D games to keep character on correct layer\n" +
                        "  - Should typically be enabled\n" +
                        "• Slide Speed: How fast character slides down slopes\n" +
                        "  - Lower = more control, higher = faster sliding\n" +
                        "• Wall Jump Forces: Control wall jump behavior\n" +
                        "  - Force X: Horizontal push away from wall\n" +
                        "  - Force Y: Vertical jump force",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Facing Direction - For Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Facing Direction (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_facingRight);

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showFacingHelp = EditorGUILayout.Foldout(_showFacingHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showFacingHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Facing Right Usage:\n" +
                        "• Platformer Mode: Automatically flips sprite (Transform.scale.x) based on movement\n" +
                        "  - Set initial facing: true = right, false = left\n" +
                        "  - Character will auto-flip when moving left/right\n" +
                        "• BeltScroll Mode: Automatically flips sprite (Transform.scale.x) based on movement\n" +
                        "  - Similar to Platformer, used for left/right facing in side-scrolling games\n" +
                        "  - Set initial facing: true = right, false = left\n" +
                        "• TopDown Mode: NOT USED - relies on Animator BlendTree for 4-direction sprites\n" +
                        "• Best Practice: Set this based on your character's initial spawn direction",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                // Moving Platform - For Platformer and BeltScroll
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Moving Platform (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_enableMovingPlatform, new GUIContent(
                    "Enable Moving Platform",
                    "Enable moving platform support. Character will move with platforms.\n" +
                    "Requires platform to have a Rigidbody2D component."));

                if (_enableMovingPlatform.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_inheritPlatformRotation, new GUIContent(
                        "Inherit Platform Rotation",
                        "Character will rotate with rotating platforms.\n" +
                        "2D platforms typically only translate, so this is disabled by default."));
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
                        "Moving Platform Support (2D):\n" +
                        "• Platform Requirements:\n" +
                        "  - Must have a Collider2D component\n" +
                        "  - Layer must match Platform Layer (or Ground Layer if empty)\n" +
                        "  - Rigidbody2D is optional (velocity calculated from Transform delta)\n" +
                        "• Features:\n" +
                        "  - Character automatically moves with platform\n" +
                        "  - Supports 2D translation and rotation\n" +
                        "  - Zero allocation design\n" +
                        "• Inherit Rotation: Typically disabled for 2D games\n" +
                        "  - Enable only for rotating platforms",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);

                // Gap Bridging (Mario Style) - For Platformer only
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Gap Bridging (Mario Style)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_enableGapBridging, new GUIContent(
                    "Enable Gap Bridging",
                    "Maintain grounded state across small gaps when running fast.\n" +
                    "Character 'slides' over gaps like Mario running fast."));

                if (_enableGapBridging.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_minSpeedForGapBridge, new GUIContent(
                        "Min Speed",
                        "Minimum speed (m/s) required to bridge gaps."));
                    EditorGUILayout.PropertyField(_maxGapDistance, new GUIContent(
                        "Max Gap Distance",
                        "Maximum gap width (m) that can be bridged."));
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
                        "Gap Bridging (Mario Style):\n" +
                        "• When running fast, character stays grounded over small gaps\n" +
                        "• Checks for ground ahead when current ground is missing\n" +
                        "• Creates fluid running experience like classic Mario games\n" +
                        "• Only active in Platformer mode",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
            else
            {
                // TopDown mode - show different settings
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("TopDown Settings", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_lockZAxis, new GUIContent(
                    "Lock Z Axis",
                    "Prevents character from moving on Z axis.\n" +
                    "Essential for 2D games to keep character on correct layer."));
                EditorGUILayout.HelpBox(
                    "TopDown Mode:\n" +
                    "• No gravity, no jump - these settings are not needed\n" +
                    "• Uses Animator BlendTree for 4-direction sprites\n" +
                    "• InputX/InputY parameters control facing direction",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

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

            // Show different parameters based on movement type
            if (currentType != MovementType2D.TopDown)
            {
                EditorGUILayout.PropertyField(_isGroundedParameter);
                EditorGUILayout.PropertyField(_jumpTrigger);
                EditorGUILayout.PropertyField(_verticalSpeedParameter, new GUIContent(
                    "Vertical Speed Parameter",
                    "Parameter name for vertical speed (Float).\n" +
                    "Used for jump/fall animations (Platformer/BeltScroll)."));
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "TopDown mode uses InputX/InputY parameters for 4-direction facing.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(_inputXParameter, new GUIContent(
                "Input X Parameter",
                "Parameter name for input X axis (Float).\n" +
                "Platformer/BeltScroll: Horizontal movement\n" +
                "TopDown: Used for 4-direction facing (Left/Right)"));
            EditorGUILayout.PropertyField(_inputYParameter, new GUIContent(
                "Input Y Parameter",
                "Parameter name for input Y axis (Float).\n" +
                "Platformer/BeltScroll: Vertical movement (if applicable)\n" +
                "TopDown: Used for 4-direction facing (Up/Down)"));
            EditorGUILayout.PropertyField(_rollTrigger);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}