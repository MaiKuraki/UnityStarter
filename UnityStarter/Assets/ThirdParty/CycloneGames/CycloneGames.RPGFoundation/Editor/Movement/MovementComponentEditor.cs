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
            EditorGUILayout.LabelField("Animation System", EditorStyles.boldLabel);

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
            EditorGUILayout.LabelField("Other Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_worldUpSource);
            EditorGUILayout.PropertyField(_useRootMotion);
            EditorGUILayout.PropertyField(_ignoreTimeScale);

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