using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    /// <summary>
    /// Validates Interact module setup and helps fix common configuration issues.
    /// </summary>
    public class InteractionValidatorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private List<ValidationIssue> _issues = new();
        private bool _autoFix = true;
        private int _fixedCount;

        private enum Severity { Info, Warning, Error }

        private struct ValidationIssue
        {
            public Severity Severity;
            public string Message;
            public Object Target;
            public System.Action FixAction;
            public bool CanAutoFix;
        }

        [MenuItem("Tools/CycloneGames/Interaction/Interaction Validator", false)]
        public static void ShowWindow()
        {
            var window = GetWindow<InteractionValidatorWindow>();
            window.titleContent = new GUIContent("Interaction Validator", EditorGUIUtility.IconContent("console.infoicon").image);
            window.minSize = new Vector2(450, 300);
            window.Show();
        }

        private void OnEnable()
        {
            RunValidation();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSummary();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawIssues();
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("🔄 Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                RunValidation();
            }

            GUILayout.FlexibleSpace();

            _autoFix = GUILayout.Toggle(_autoFix, "Auto-Fix", EditorStyles.toolbarButton, GUILayout.Width(80));

            if (GUILayout.Button("Fix All", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                FixAllIssues();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            int errors = 0, warnings = 0, infos = 0;
            foreach (var issue in _issues)
            {
                switch (issue.Severity)
                {
                    case Severity.Error: errors++; break;
                    case Severity.Warning: warnings++; break;
                    case Severity.Info: infos++; break;
                }
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (_issues.Count == 0)
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("✅ All checks passed! No issues found.", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }
            else
            {
                if (errors > 0)
                {
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    EditorGUILayout.LabelField($"❌ {errors} Error(s)", EditorStyles.boldLabel, GUILayout.Width(100));
                }
                if (warnings > 0)
                {
                    GUI.color = new Color(1f, 0.8f, 0.3f);
                    EditorGUILayout.LabelField($"⚠️ {warnings} Warning(s)", EditorStyles.boldLabel, GUILayout.Width(120));
                }
                if (infos > 0)
                {
                    GUI.color = new Color(0.6f, 0.8f, 1f);
                    EditorGUILayout.LabelField($"ℹ️ {infos} Info", EditorStyles.boldLabel, GUILayout.Width(80));
                }
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawIssues()
        {
            foreach (var issue in _issues)
            {
                DrawIssue(issue);
            }
        }

        private void DrawIssue(ValidationIssue issue)
        {
            Color bgColor = issue.Severity switch
            {
                Severity.Error => new Color(1f, 0.3f, 0.3f, 0.2f),
                Severity.Warning => new Color(1f, 0.7f, 0.2f, 0.2f),
                _ => new Color(0.5f, 0.7f, 1f, 0.2f)
            };

            GUI.backgroundColor = bgColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();

            string icon = issue.Severity switch
            {
                Severity.Error => "❌",
                Severity.Warning => "⚠️",
                _ => "ℹ️"
            };
            EditorGUILayout.LabelField(icon, GUILayout.Width(20));
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);

            if (issue.Target != null)
            {
                if (GUILayout.Button("Select", GUILayout.Width(50)))
                {
                    Selection.activeObject = issue.Target;
                    EditorGUIUtility.PingObject(issue.Target);
                }
            }

            if (issue.CanAutoFix && issue.FixAction != null)
            {
                if (GUILayout.Button("Fix", GUILayout.Width(40)))
                {
                    issue.FixAction.Invoke();
                    RunValidation();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (issue.Target != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.ObjectField("Object:", issue.Target, typeof(Object), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            if (_fixedCount > 0)
            {
                EditorGUILayout.HelpBox($"✅ Fixed {_fixedCount} issue(s) in this session.", MessageType.Info);
            }
        }

        private void RunValidation()
        {
            _issues.Clear();

            ValidateInteractables();
            ValidateDetectors();
            ValidateEffectPool();
            ValidateLayers();
            ValidateTwoStateInteractions();

            Repaint();
        }

        private void ValidateInteractables()
        {
            var interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);

            foreach (var interactable in interactables)
            {
                // Check for Collider (3D or 2D)
                var collider3D = interactable.GetComponent<Collider>();
                var collider2D = interactable.GetComponent<Collider2D>();
                if (collider3D == null && collider2D == null)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Error,
                        Message = $"Missing Collider on '{interactable.name}'. Interaction detection requires a Collider (3D or 2D).",
                        Target = interactable,
                        CanAutoFix = true,
                        FixAction = () =>
                        {
                            Undo.AddComponent<SphereCollider>(interactable.gameObject);
                            _fixedCount++;
                        }
                    });
                }
                else if (collider3D != null && !collider3D.isTrigger)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Warning,
                        Message = $"Collider on '{interactable.name}' is not a trigger. Consider setting isTrigger=true.",
                        Target = interactable,
                        CanAutoFix = true,
                        FixAction = () =>
                        {
                            Undo.RecordObject(collider3D, "Set Trigger");
                            collider3D.isTrigger = true;
                            _fixedCount++;
                        }
                    });
                }
                else if (collider2D != null && !collider2D.isTrigger)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Warning,
                        Message = $"Collider2D on '{interactable.name}' is not a trigger. Consider setting isTrigger=true.",
                        Target = interactable,
                        CanAutoFix = true,
                        FixAction = () =>
                        {
                            Undo.RecordObject(collider2D, "Set Trigger");
                            collider2D.isTrigger = true;
                            _fixedCount++;
                        }
                    });
                }

                // Check layer
                int layer = interactable.gameObject.layer;
                string layerName = LayerMask.LayerToName(layer);
                if (layerName != "Interactable" && layerName != "Default")
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Info,
                        Message = $"'{interactable.name}' is on layer '{layerName}'. Make sure InteractionDetector includes this layer in its mask.",
                        Target = interactable,
                        CanAutoFix = false
                    });
                }

                // Check for cooldown
                var serializedObject = new SerializedObject(interactable);
                var cooldownProp = serializedObject.FindProperty("interactionCooldown");
                if (cooldownProp != null && cooldownProp.floatValue <= 0)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Info,
                        Message = $"'{interactable.name}' has no cooldown (0s). Multiple rapid interactions are possible.",
                        Target = interactable,
                        CanAutoFix = false
                    });
                }
            }

            if (interactables.Length == 0)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = Severity.Info,
                    Message = "No Interactable components found in scene.",
                    Target = null,
                    CanAutoFix = false
                });
            }
        }

        private void ValidateDetectors()
        {
            var detectors = FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);

            if (detectors.Length == 0)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = Severity.Warning,
                    Message = "No InteractionDetector found in scene. Players won't be able to detect interactables.",
                    Target = null,
                    CanAutoFix = false
                });
                return;
            }

            foreach (var detector in detectors)
            {
                var serializedObject = new SerializedObject(detector);

                var radiusProp = serializedObject.FindProperty("detectionRadius");
                if (radiusProp != null && radiusProp.floatValue <= 0)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Error,
                        Message = $"DetectionRadius on '{detector.name}' is <= 0. No objects will be detected.",
                        Target = detector,
                        CanAutoFix = true,
                        FixAction = () =>
                        {
                            radiusProp.floatValue = 3f;
                            serializedObject.ApplyModifiedProperties();
                            _fixedCount++;
                        }
                    });
                }

                var modeProp = serializedObject.FindProperty("detectionMode");
                if (modeProp != null)
                {
                    var mode = (DetectionMode)modeProp.enumValueIndex;
                    if (mode == DetectionMode.SpatialHash)
                    {
                        var system = FindAnyObjectByType<InteractionSystem>();
                        if (system == null)
                        {
                            _issues.Add(new ValidationIssue
                            {
                                Severity = Severity.Error,
                                Message = $"Detector '{detector.name}' uses SpatialHash mode but no InteractionSystem found in scene.",
                                Target = detector,
                                CanAutoFix = false
                            });
                        }
                    }
                    else
                    {
                        var layerMaskProp = serializedObject.FindProperty("interactableLayer");
                        if (layerMaskProp != null && layerMaskProp.intValue == 0)
                        {
                            _issues.Add(new ValidationIssue
                            {
                                Severity = Severity.Error,
                                Message = $"InteractableLayer on '{detector.name}' is empty (Nothing). No layers will be detected.",
                                Target = detector,
                                CanAutoFix = true,
                                FixAction = () =>
                                {
                                    layerMaskProp.intValue = -1;
                                    serializedObject.ApplyModifiedProperties();
                                    _fixedCount++;
                                }
                            });
                        }
                    }
                }
            }

            if (detectors.Length > 1)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = Severity.Info,
                    Message = $"Multiple InteractionDetectors ({detectors.Length}) found. This is fine for split-screen or multiple player scenarios.",
                    Target = detectors[0],
                    CanAutoFix = false
                });
            }
        }

        private void ValidateEffectPool()
        {
            // EffectPoolSystem is static — only validate PooledEffect prefab references in scene
            var pooledEffects = FindObjectsByType<PooledEffect>(FindObjectsSortMode.None);
            foreach (var effect in pooledEffects)
            {
                if (effect.gameObject.scene.isLoaded && !effect.gameObject.activeInHierarchy)
                    continue; // Inactive is fine for pool

                var ps = effect.GetComponent<ParticleSystem>();
                if (ps == null)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Info,
                        Message = $"PooledEffect '{effect.name}' has no ParticleSystem. This is fine if you use a non-particle effect.",
                        Target = effect,
                        CanAutoFix = false
                    });
                }
            }
        }

        private void ValidateLayers()
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer == -1)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = Severity.Info,
                    Message = "Layer 'Interactable' not found. Consider creating it for better organization and performance.",
                    Target = null,
                    CanAutoFix = false
                });
            }
        }

        private void ValidateTwoStateInteractions()
        {
            var twoStates = FindObjectsByType<TwoStateInteractionBase>(FindObjectsSortMode.None);

            foreach (var ts in twoStates)
            {
                // Check for collider
                var collider3D = ts.GetComponent<Collider>();
                var collider2D = ts.GetComponent<Collider2D>();
                if (collider3D == null && collider2D == null)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Severity = Severity.Warning,
                        Message = $"TwoStateInteraction '{ts.name}' has no Collider. If it's used with InteractionDetector, a collider is required.",
                        Target = ts,
                        CanAutoFix = false
                    });
                }
            }
        }

        private void FixAllIssues()
        {
            foreach (var issue in _issues)
            {
                if (issue.CanAutoFix && issue.FixAction != null)
                {
                    issue.FixAction.Invoke();
                }
            }

            RunValidation();
        }
    }
}