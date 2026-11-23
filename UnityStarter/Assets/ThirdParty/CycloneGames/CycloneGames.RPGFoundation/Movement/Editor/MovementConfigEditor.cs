using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Editor
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
        private SerializedProperty _collisionLayer;
        private SerializedProperty _slopeLimit;
        private SerializedProperty _stepHeight;
        private SerializedProperty _minAirborneTimeForFall;
        private SerializedProperty _rotationSpeed;

        private SerializedProperty _enableMovingPlatform;
        private SerializedProperty _inheritPlatformRotation;
        private SerializedProperty _inheritPlatformMomentum;
        private SerializedProperty _platformLayer;

        private bool _showSpecialMovementHelp = false;
        private bool _showPhysicsHelp = false;
        private bool _showMovingPlatformHelp = false;
        private bool _showCeilingDetectionHelp = false;
        private bool _showGapBridgingHelp = false;
        private bool _showRotationHelp = false;
        private bool _showClimbingHelp = false;

        private SerializedProperty _enableCeilingDetection;
        private SerializedProperty _ceilingCheckDistance;

        private SerializedProperty _enableGapBridging;
        private SerializedProperty _minSpeedForGapBridge;
        private SerializedProperty _maxGapDistance;
        private SerializedProperty _maxGapHeightDiff;

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
        private SerializedProperty _wallJumpForceHorizontal;
        private SerializedProperty _wallJumpForceVertical;
        private SerializedProperty _wallJumpCooldown;
        private SerializedProperty _differentWallAngle;


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
            BuildPresetNames(true);
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
            _collisionLayer = serializedObject.FindProperty("collisionLayer");
            _slopeLimit = serializedObject.FindProperty("slopeLimit");
            _stepHeight = serializedObject.FindProperty("stepHeight");
            _minAirborneTimeForFall = serializedObject.FindProperty("minAirborneTimeForFall");
            _rotationSpeed = serializedObject.FindProperty("rotationSpeed");
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
            _wallJumpForceHorizontal = serializedObject.FindProperty("wallJumpForceHorizontal");
            _wallJumpForceVertical = serializedObject.FindProperty("wallJumpForceVertical");
            _wallJumpCooldown = serializedObject.FindProperty("wallJumpCooldown");
            _differentWallAngle = serializedObject.FindProperty("differentWallAngle");
            
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Units: speeds m/s, distances m, times s, angles deg, gravity m/s^2.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("3D gravity is negative on Y. OnValidate clamps out-of-range values.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preset", GUILayout.Width(46f));
            _presetSelection = EditorGUILayout.Popup(_presetSelection, _presetNames);
            if (GUILayout.Button("Apply", GUILayout.Width(56f))) ApplySelectedPreset();
            if (GUILayout.Button("Restore Defaults", GUILayout.Width(120f))) RestoreDefaults(true);
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
            EditorGUILayout.LabelField("3D Movement Configuration", EditorStyles.boldLabel);

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ground Movement", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_walkSpeed);
            EditorGUILayout.PropertyField(_runSpeed);
            EditorGUILayout.PropertyField(_sprintSpeed);
            EditorGUILayout.PropertyField(_crouchSpeed);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Jump", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(_jumpForce);
            EditorGUILayout.PropertyField(_maxJumpCount);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

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
            EditorGUILayout.PropertyField(_collisionLayer, new GUIContent(
                "Collision Layer",
                "LayerMask for collision detection during movement.\n" +
                "If empty (0), uses Ground Layer instead.\n" +
                "Use this to specify separate layers for movement collision (e.g., include walls but exclude triggers)."));
            EditorGUILayout.PropertyField(_slopeLimit, new GUIContent(
                "Slope Limit",
                "Maximum angle (in degrees) the character can walk up.\n" +
                "Typical: 30-45 degrees"));
            EditorGUILayout.PropertyField(_stepHeight, new GUIContent(
                "Step Height",
                "Maximum height the character can step up.\n" +
                "Allows walking up small obstacles without jumping"));
            EditorGUILayout.PropertyField(_minAirborneTimeForFall, new GUIContent(
                "Min Airborne Time for Fall",
                "Minimum time (seconds) character must be airborne before triggering fall animation.\n" +
                "Prevents false fall animation triggers when climbing stairs or stepping up.\n" +
                "• 0.05-0.08: Responsive, but may trigger on stairs\n" +
                "• 0.1: Balanced (recommended)\n" +
                "• 0.15-0.2: Conservative, avoids false triggers"));

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
                    "  - Typical: 0.2-0.4\n" +
                    "• Min Airborne Time for Fall: Delay before fall animation\n" +
                    "  - Prevents false triggers on stairs\n" +
                    "  - Higher values = more conservative",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Climbing", EditorStyles.miniLabel);

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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Navigation Integrations", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Integration assemblies compile only when their package is installed.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(NavigationAvailabilityLine,
                EditorStyles.wordWrappedMiniLabel);
            if (NavigationAvailability == "none")
            {
                EditorGUILayout.LabelField(
                    "Add the package to Packages/manifest.json as a file: dependency, or place it under Packages/.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "Its version define then enables the matching integration assembly.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.LabelField(
                "Implement IPathfindingProvider in your own assembly and bind it in your composition root.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

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

        private static readonly string NavigationAvailability = BuildNavigationAvailability();
        private static readonly string NavigationAvailabilityLine = "Available here: " + NavigationAvailability;

        private static string BuildNavigationAvailability()
        {
            string names = "";
#if CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION
            names += "Unity AI Navigation, ";
#endif
#if CYCLONE_RPGFOUNDATION_HAS_ASTAR_PATHFINDING
            names += "A* Pathfinding Project, ";
#endif
#if CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION
            names += "Agents Navigation, ";
#endif
#if CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION_CROWDS
            names += "Agents Navigation Crowds, ";
#endif
            return names.Length == 0 ? "none" : names.Substring(0, names.Length - 2);
        }

    }
}
