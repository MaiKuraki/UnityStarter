using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Editor
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

        private SerializedProperty _enableMovingPlatform;
        private SerializedProperty _inheritPlatformRotation;
        private SerializedProperty _inheritPlatformMomentum;
        private SerializedProperty _platformLayer;

        private SerializedProperty _enableLadderClimbing;
        private SerializedProperty _ladderClimbSpeed;
        private SerializedProperty _ladderLayer;
        private SerializedProperty _enableWallClimbing;
        private SerializedProperty _wallClimbSpeed;
        private SerializedProperty _wallLayer;
        private SerializedProperty _wallCheckDistance;
        private SerializedProperty _wallClingDuration;
        private SerializedProperty _wallSlideSpeed;
        private SerializedProperty _enableWallJump;
        private SerializedProperty _wallJumpCooldown;
        private SerializedProperty _differentWallAngle;

        private bool _showMovementTypeHelp = false;
        private bool _showAirMovementHelp = false;
        private bool _showPhysicsHelp = false;
        private bool _showGroundDetectionHelp = false;
        private bool _showMovingPlatformHelp = false;
        private bool _showGapBridgingHelp = false;
        private bool _showOtherHelp = false;
        private bool _showFacingHelp = false;

        private bool _showClimbingHelp = false;

        private SerializedProperty _enableGapBridging;
        private SerializedProperty _minSpeedForGapBridge;
        private SerializedProperty _maxGapDistance;

        private string _validationReport;

        private readonly MovementConfigPresetLibrary _presetLibrary = new MovementConfigPresetLibrary();
        private string[] _presetNames = new string[0];
        private int[] _presetIndices = new int[0];
        private int _presetSelection;

        private void RefreshExternalPresets()
        {
            _presetLibrary.ClearExternalPresets();
            string[] guids = AssetDatabase.FindAssets("t:MovementConfigPresetAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<MovementConfigPresetAsset>(path);
                if (asset == null) continue;
                _presetLibrary.Register(asset.ToPreset());
            }
        }

        private void BuildPresetNames(bool is3D)
        {
            RefreshExternalPresets();
            int count = _presetLibrary.CountFor(is3D);
            _presetNames = new string[count];
            _presetIndices = new int[count];
            int k = 0;
            for (int i = 0; i < _presetLibrary.Count; i++)
            {
                MovementConfigPreset preset = _presetLibrary.Get(i);
                if (preset == null || preset.Is3D != is3D) continue;
                _presetNames[k] = preset.Name;
                _presetIndices[k] = i;
                k++;
            }
            if (_presetSelection >= count) _presetSelection = 0;
        }

        private void ApplySelectedPreset()
        {
            if (_presetIndices.Length == 0) return;
            int libraryIndex = _presetIndices[_presetSelection];
            MovementConfigPreset preset = _presetLibrary.Get(libraryIndex);
            if (preset == null) return;
            var rules = new MovementConfigRules();
            var issues = new MovementConfigIssue[MovementConfigRules.FieldCount + 4];
            int invalid = _presetLibrary.ValidatePreset(rules, libraryIndex, issues);
            if (invalid != 0)
            {
                _validationReport = "Preset contains out-of-range values; apply aborted.";
                return;
            }
            Undo.RecordObject(target, "Apply Movement Preset");
            serializedObject.Update();
            for (int i = 0; i < preset.Count; i++)
            {
                string name = _presetLibrary.GetSerializedFieldName(preset.Entries[i].Field);
                if (name == null) continue;
                SerializedProperty prop = serializedObject.FindProperty(name);
                if (prop == null) continue;
                if (prop.propertyType == SerializedPropertyType.Float)
                    prop.floatValue = preset.Entries[i].Value;
                else if (prop.propertyType == SerializedPropertyType.Integer)
                    prop.intValue = (int)preset.Entries[i].Value;
            }
            for (int i = 0; i < preset.RawCount; i++)
            {
                MovementConfigRawOverride raw = preset.RawOverrides[i];
                SerializedProperty prop = serializedObject.FindProperty(raw.SerializedField);
                if (prop == null) continue;
                if (prop.propertyType == SerializedPropertyType.Float)
                    prop.floatValue = raw.Value;
                else if (prop.propertyType == SerializedPropertyType.Integer)
                    prop.intValue = (int)raw.Value;
                else if (prop.propertyType == SerializedPropertyType.Boolean)
                    prop.boolValue = raw.Value != 0f;
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void RestoreDefaults(bool is3D)
        {
            var rules = new MovementConfigRules();
            float[] defaults = new float[MovementConfigRules.FieldCount];
            rules.WriteDefaults(defaults);
            Undo.RecordObject(target, "Restore Movement Defaults");
            serializedObject.Update();
            for (int i = 0; i < MovementConfigRules.FieldCount; i++)
            {
                if (is3D && i > 31) continue;
                if (!is3D && i >= 6 && i <= 31) continue;
                string name = _presetLibrary.GetSerializedFieldName((MovementConfigField)(byte)i);
                if (name == null) continue;
                SerializedProperty prop = serializedObject.FindProperty(name);
                if (prop == null) continue;
                if (prop.propertyType == SerializedPropertyType.Float)
                    prop.floatValue = defaults[i];
                else if (prop.propertyType == SerializedPropertyType.Integer)
                    prop.intValue = (int)defaults[i];
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void OnEnable()
        {
            BuildPresetNames(false);
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
            _enableMovingPlatform = serializedObject.FindProperty("enableMovingPlatform");
            _inheritPlatformRotation = serializedObject.FindProperty("inheritPlatformRotation");
            _inheritPlatformMomentum = serializedObject.FindProperty("inheritPlatformMomentum");
            _platformLayer = serializedObject.FindProperty("platformLayer");
            _enableGapBridging = serializedObject.FindProperty("enableGapBridging");
            _minSpeedForGapBridge = serializedObject.FindProperty("minSpeedForGapBridge");
            _maxGapDistance = serializedObject.FindProperty("maxGapDistance");
            _minSpeedForGapBridge = serializedObject.FindProperty("minSpeedForGapBridge");
            _maxGapDistance = serializedObject.FindProperty("maxGapDistance");

            _enableLadderClimbing = serializedObject.FindProperty("enableLadderClimbing");
            _ladderClimbSpeed = serializedObject.FindProperty("ladderClimbSpeed");
            _ladderLayer = serializedObject.FindProperty("ladderLayer");
            _enableWallClimbing = serializedObject.FindProperty("enableWallClimbing");
            _wallClimbSpeed = serializedObject.FindProperty("wallClimbSpeed");
            _wallLayer = serializedObject.FindProperty("wallLayer");
            _wallCheckDistance = serializedObject.FindProperty("wallCheckDistance");
            _wallClingDuration = serializedObject.FindProperty("wallClingDuration");
            _wallSlideSpeed = serializedObject.FindProperty("wallSlideSpeed");
            _enableWallJump = serializedObject.FindProperty("enableWallJump");
            _wallJumpCooldown = serializedObject.FindProperty("wallJumpCooldown");
            _differentWallAngle = serializedObject.FindProperty("differentWallAngle");

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Units: speeds m/s, distances m, times s, angles deg, gravity m/s^2.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("2D gravity is a positive downward magnitude. OnValidate clamps out-of-range values.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preset", GUILayout.Width(46f));
            _presetSelection = EditorGUILayout.Popup(_presetSelection, _presetNames);
            if (GUILayout.Button("Apply", GUILayout.Width(56f))) ApplySelectedPreset();
            if (GUILayout.Button("Restore Defaults", GUILayout.Width(120f))) RestoreDefaults(false);
            EditorGUILayout.EndHorizontal();

            MovementConfigPreset selected = _presetLibrary.Get(
                _presetIndices.Length > 0 ? _presetIndices[_presetSelection] : -1);
            if (selected != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(selected.Description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(3);
            if (GUILayout.Button("Validate Configuration"))
            {
                var config = target as MovementConfigBase;
                _validationReport = config != null
                    ? config.ValidateConfiguration()
                    : "No configuration selected.";
            }
            if (!string.IsNullOrEmpty(_validationReport))
            {
                EditorGUILayout.HelpBox(_validationReport,
                    _validationReport.StartsWith("OK", System.StringComparison.Ordinal)
                        ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2D Movement Configuration", EditorStyles.boldLabel);

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

            MovementType2D currentType = (MovementType2D)_movementType.enumValueIndex;

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ground Movement", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_walkSpeed);
            EditorGUILayout.PropertyField(_runSpeed);
            EditorGUILayout.PropertyField(_sprintSpeed);
            EditorGUILayout.PropertyField(_crouchSpeed);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            if (currentType != MovementType2D.TopDown)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Jump (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_jumpForce);
                EditorGUILayout.PropertyField(_maxJumpCount);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

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

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Movement Options (Platformer/BeltScroll)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(_lockZAxis);
                EditorGUILayout.PropertyField(_slideSpeed, new GUIContent(
                    "Slide Speed",
                    "Speed when sliding down slopes or ground."));

                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                _showOtherHelp = EditorGUILayout.Foldout(_showOtherHelp, "Help & Details", EditorStyles.foldout);
                EditorGUI.indentLevel--;
                if (_showOtherHelp)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox(
                        "Movement Options:\n" +
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

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Facing (Platformer/BeltScroll)", EditorStyles.miniLabel);
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

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Gap Bridging (Platformer/BeltScroll)", EditorStyles.miniLabel);
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
                        "Gap Bridging (Platformer/BeltScroll):\n" +
                        "• When running fast, character stays grounded over small gaps\n" +
                        "• Checks for ground ahead when current ground is missing\n" +
                        "• Creates fluid running experience like classic Mario games\n" +
                        "• Only active in Platformer mode",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Climbing (Platformer/BeltScroll)", EditorStyles.miniLabel);
                
                EditorGUILayout.PropertyField(_enableLadderClimbing);
                if (_enableLadderClimbing.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_ladderClimbSpeed);
                    EditorGUILayout.PropertyField(_ladderLayer);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);

                EditorGUILayout.PropertyField(_enableWallClimbing);
                if (_enableWallClimbing.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_wallClimbSpeed);
                    EditorGUILayout.PropertyField(_wallLayer);
                    EditorGUILayout.PropertyField(_wallCheckDistance);
                    EditorGUILayout.PropertyField(_wallClingDuration);
                    EditorGUILayout.PropertyField(_wallSlideSpeed);
                    
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Wall Jump", EditorStyles.miniLabel);
                    EditorGUILayout.PropertyField(_enableWallJump);
                    if (_enableWallJump.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(_wallJumpForceX);
                        EditorGUILayout.PropertyField(_wallJumpForceY);
                        EditorGUILayout.PropertyField(_wallJumpCooldown);
                        EditorGUILayout.PropertyField(_differentWallAngle);
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
                        "• Ladder Climbing:\n" +
                        "  - Triggered by entering Ladder Layer zone + Up input\n" +
                        "  - Moves freely (Up/Down/Left/Right)\n" +
                        "• Wall Climbing:\n" +
                        "  - Triggered by being in air + near wall + input towards wall\n" +
                        "  - Cling Duration: How long before sliding down\n" +
                        "  - Omni-directional: Supports vertical and horizontal movement (Vines)\n" +
                        "• Wall Jump:\n" +
                        "  - Force X/Y: Jump velocity vector\n" +
                        "  - Different Angle: For continuous wall jumping",
                        MessageType.Info);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
            else
            {
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

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("This asset configures simulation only.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "Animation backends own their parameter names and are bound with BindAnimation.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}