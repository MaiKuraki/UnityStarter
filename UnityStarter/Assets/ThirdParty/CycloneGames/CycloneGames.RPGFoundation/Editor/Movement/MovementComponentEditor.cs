using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime;

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

        private void OnEnable()
        {
            _config = serializedObject.FindProperty("config");
            _characterAnimator = serializedObject.FindProperty("characterAnimator");
            _animancerComponent = serializedObject.FindProperty("animancerComponent");
            _worldUpSource = serializedObject.FindProperty("worldUpSource");
            _useRootMotion = serializedObject.FindProperty("useRootMotion");
            _ignoreTimeScale = serializedObject.FindProperty("ignoreTimeScale");

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
                    EditorGUILayout.HelpBox(
                        "Root Motion Enabled:\n" +
                        "• Animations with root motion will control character position\n" +
                        "• Use for: Attack lunges, dodge rolls, special movement animations\n" +
                        "• States can override this setting via MovementContext.UseRootMotion\n" +
                        "• Requires Animator component and animations with root motion enabled\n" +
                        "• Root motion is applied in OnAnimatorMove callback",
                        MessageType.Info);

                    var animator = _characterAnimator.objectReferenceValue as Animator;
                    if (animator == null)
                    {
                        EditorGUILayout.HelpBox(
                            "⚠️ Warning: No Animator assigned. Root motion requires an Animator component.",
                            MessageType.Warning);
                    }
                    else if (!animator.applyRootMotion)
                    {
                        EditorGUILayout.HelpBox(
                            "ℹ️ Info: Animator's 'Apply Root Motion' is currently disabled.\n" +
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

            serializedObject.ApplyModifiedProperties();
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
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Note: Animancer requires an Animator component on the same GameObject.\n" +
                    "The Animator's Controller field should typically be empty.",
                    MessageType.Info);

                // Check if Animancer has an Animator
                var animancerObj = _animancerComponent.objectReferenceValue;
                var animancerType = animancerObj.GetType();
                var animatorProperty = animancerType.GetProperty("Animator",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (animatorProperty != null)
                {
                    var animancerAnimator = animatorProperty.GetValue(animancerObj) as Animator;
                    if (animancerAnimator == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Warning: AnimancerComponent does not have an Animator assigned.\n" +
                            "It will use Parameters mode instead of Animator mode.\n" +
                            "Assign an Animator to the AnimancerComponent for better performance.",
                            MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Animancer's Animator:", GUILayout.Width(140));
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(animancerAnimator, typeof(Animator), true);
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.EndHorizontal();

                        if (animancerAnimator.runtimeAnimatorController != null)
                        {
                            EditorGUILayout.HelpBox(
                                $"Note: Animator has Controller '{animancerAnimator.runtimeAnimatorController.name}' assigned.\n" +
                                "For Animancer, the Controller should typically be empty.",
                                MessageType.Warning);
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(
                    "Assign an AnimancerComponent to use Animancer for animation control.\n" +
                    "Animancer provides better performance and code-driven animation management.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }
    }
}