using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using Object = UnityEngine.Object;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    /// <summary>
    /// Validates interaction authoring setup and fixes common scene configuration issues.
    /// </summary>
    public sealed class InteractionValidatorWindow : EditorWindow
    {
        private const string DetectorRadiusPropertyName = "detectionRadius";
        private const string InteractableLayerPropertyName = "interactableLayer";
        private const string CooldownPropertyName = "interactionCooldown";
        private const string StableIdPropertyName = "stableId";

        private readonly List<ValidationIssue> _issues = new(32);
        private readonly List<InteractionComponentRuleIssue> _componentRuleIssues = new(16);
        private readonly HashSet<GameObject> _validatedGameObjects = new();
        private Vector2 _scrollPosition;
        private int _fixedCount;
        private bool _pendingRefresh;

        private enum Severity
        {
            Info,
            Warning,
            Error
        }

        private enum FixKind
        {
            None,
            AddBestCollider,
            SetCollider3DTrigger,
            SetCollider2DTrigger,
            SetDetectorRadius,
            SetDetectorLayerAll
        }

        private readonly struct ValidationIssue
        {
            public readonly Severity Severity;
            public readonly string Message;
            public readonly Object Target;
            public readonly FixKind FixKind;

            public ValidationIssue(Severity severity, string message, Object target, FixKind fixKind = FixKind.None)
            {
                Severity = severity;
                Message = message;
                Target = target;
                FixKind = fixKind;
            }
        }

        [MenuItem("Tools/CycloneGames/Interaction/Interaction Validator", false)]
        public static void ShowWindow()
        {
            var window = GetWindow<InteractionValidatorWindow>();
            window.titleContent = new GUIContent("Interaction Validator", EditorGUIUtility.IconContent("console.infoicon").image);
            window.minSize = new Vector2(520f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            RunValidation();
        }

        private void OnGUI()
        {
            _pendingRefresh = false;

            DrawToolbar();
            DrawSummary();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawIssues();
            EditorGUILayout.EndScrollView();

            DrawFooter();

            if (_pendingRefresh)
            {
                RunValidation();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                RunValidation();
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!HasFixableIssues()))
            {
                if (GUILayout.Button("Fix All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    FixAllIssues();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            int errors = 0;
            int warnings = 0;
            int infos = 0;

            for (int i = 0; i < _issues.Count; i++)
            {
                switch (_issues[i].Severity)
                {
                    case Severity.Error:
                        errors++;
                        break;
                    case Severity.Warning:
                        warnings++;
                        break;
                    default:
                        infos++;
                        break;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Validation Summary", EditorStyles.boldLabel);

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("All checks passed. No interaction authoring issues were found.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                DrawSummaryCount("Errors", errors, InteractionInspectorUiUtility.ColorWarning);
                DrawSummaryCount("Warnings", warnings, new Color(1f, 0.75f, 0.25f));
                DrawSummaryCount("Info", infos, InteractionInspectorUiUtility.ColorRuntime);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                InteractionInspectorUiUtility.DrawHelpBox(
                    "Validator fixes only scene component setup. It does not change project layers, prefabs, or assets.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawSummaryCount(string label, int count, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(94f, 20f, GUILayout.Width(94f));
            InteractionInspectorUiUtility.DrawStatusBadge(rect, label + ": " + count, count > 0 ? color : Color.gray);
        }

        private void DrawIssues()
        {
            if (_issues.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _issues.Count; i++)
            {
                DrawIssue(_issues[i]);
            }
        }

        private void DrawIssue(ValidationIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetSeverityLabel(issue.Severity), EditorStyles.boldLabel, GUILayout.Width(64f));
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);

            if (issue.Target != null)
            {
                if (GUILayout.Button("Select", GUILayout.Width(58f)))
                {
                    Selection.activeObject = issue.Target;
                    EditorGUIUtility.PingObject(issue.Target);
                }
            }

            using (new EditorGUI.DisabledScope(issue.FixKind == FixKind.None))
            {
                if (GUILayout.Button("Fix", GUILayout.Width(44f)))
                {
                    ApplyFix(issue);
                    _pendingRefresh = true;
                }
            }

            EditorGUILayout.EndHorizontal();

            if (issue.Target != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.ObjectField("Object", issue.Target, typeof(Object), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            if (_fixedCount > 0)
            {
                EditorGUILayout.HelpBox("Fixed " + _fixedCount + " issue(s) in this editor session.", MessageType.Info);
            }
        }

        private void RunValidation()
        {
            _issues.Clear();

            ValidateComponentRules();
            ValidateInteractables();
            ValidateDetectors();
            ValidateEffectPool();
            ValidateLayers();
            ValidateTwoStateInteractions();

            Repaint();
        }

        private void ValidateComponentRules()
        {
            _componentRuleIssues.Clear();
            _validatedGameObjects.Clear();

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.gameObject == null)
                {
                    continue;
                }

                GameObject gameObject = behaviour.gameObject;
                if (!_validatedGameObjects.Add(gameObject))
                {
                    continue;
                }

                InteractionComponentRules.CollectIssues(gameObject, _componentRuleIssues);
                for (int issueIndex = 0; issueIndex < _componentRuleIssues.Count; issueIndex++)
                {
                    InteractionComponentRuleIssue issue = _componentRuleIssues[issueIndex];
                    _issues.Add(new ValidationIssue(
                        ToValidationSeverity(issue.Severity),
                        issue.Message,
                        issue.Target != null ? issue.Target : gameObject));
                }
            }

            _componentRuleIssues.Clear();
            _validatedGameObjects.Clear();
        }

        private void ValidateInteractables()
        {
            Interactable[] interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            var stableIds = new Dictionary<string, Interactable>(interactables.Length);

            for (int i = 0; i < interactables.Length; i++)
            {
                Interactable interactable = interactables[i];
                if (interactable == null)
                {
                    continue;
                }

                Collider collider3D = interactable.GetComponent<Collider>();
                Collider2D collider2D = interactable.GetComponent<Collider2D>();
                if (collider3D == null && collider2D == null)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Error,
                        "Missing Collider or Collider2D on '" + interactable.name + "'. Interaction detection requires one collider component.",
                        interactable,
                        FixKind.AddBestCollider));
                }
                else if (collider3D != null && !collider3D.isTrigger)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Warning,
                        "Collider on '" + interactable.name + "' is not a trigger. Set isTrigger when this object should not block physics.",
                        collider3D,
                        FixKind.SetCollider3DTrigger));
                }
                else if (collider2D != null && !collider2D.isTrigger)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Warning,
                        "Collider2D on '" + interactable.name + "' is not a trigger. Set isTrigger when this object should not block physics.",
                        collider2D,
                        FixKind.SetCollider2DTrigger));
                }

                int layer = interactable.gameObject.layer;
                string layerName = LayerMask.LayerToName(layer);
                if (layerName != "Interactable" && layerName != "Default")
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Info,
                        "'" + interactable.name + "' is on layer '" + layerName + "'. Confirm detector masks include this layer.",
                        interactable));
                }

                using var serializedObject = new SerializedObject(interactable);
                SerializedProperty stableIdProperty = serializedObject.FindProperty(StableIdPropertyName);
                if (stableIdProperty != null && !string.IsNullOrWhiteSpace(stableIdProperty.stringValue))
                {
                    string stableId = stableIdProperty.stringValue;
                    if (stableIds.TryGetValue(stableId, out Interactable duplicate))
                    {
                        _issues.Add(new ValidationIssue(
                            Severity.Error,
                            "'" + interactable.name + "' and '" + duplicate.name + "' use the same Stable Id. Stable IDs must be unique for multiplayer, save data, replay, and analytics.",
                            interactable));
                    }
                    else
                    {
                        stableIds.Add(stableId, interactable);
                    }
                }

                SerializedProperty cooldownProperty = serializedObject.FindProperty(CooldownPropertyName);
                if (cooldownProperty != null && cooldownProperty.floatValue <= 0f)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Info,
                        "'" + interactable.name + "' has no cooldown. Rapid repeated interactions are allowed.",
                        interactable));
                }
            }

            if (interactables.Length == 0)
            {
                _issues.Add(new ValidationIssue(
                    Severity.Info,
                    "No Interactable components were found in the open scene.",
                    null));
            }
        }

        private void ValidateDetectors()
        {
            InteractionDetector[] detectors = FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);

            if (detectors.Length == 0)
            {
                _issues.Add(new ValidationIssue(
                    Severity.Warning,
                    "No InteractionDetector was found in the open scene. Player-controlled interaction will not detect targets.",
                    null));
                return;
            }

            for (int i = 0; i < detectors.Length; i++)
            {
                InteractionDetector detector = detectors[i];
                if (detector == null)
                {
                    continue;
                }

                using var serializedObject = new SerializedObject(detector);
                SerializedProperty radiusProperty = serializedObject.FindProperty(DetectorRadiusPropertyName);
                if (radiusProperty != null && radiusProperty.floatValue <= 0f)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Error,
                        "DetectionRadius on '" + detector.name + "' is less than or equal to zero.",
                        detector,
                        FixKind.SetDetectorRadius));
                }

                SerializedProperty modeProperty = serializedObject.FindProperty("detectionMode");
                if (modeProperty == null)
                {
                    continue;
                }

                DetectionMode mode = (DetectionMode)modeProperty.enumValueIndex;
                if (mode == DetectionMode.SpatialHash)
                {
                    InteractionSystem system = FindAnyObjectByType<InteractionSystem>();
                    if (system == null)
                    {
                        _issues.Add(new ValidationIssue(
                            Severity.Error,
                            "Detector '" + detector.name + "' uses SpatialHash mode but no InteractionSystem exists in the scene.",
                            detector));
                    }
                }
                else
                {
                    SerializedProperty layerMaskProperty = serializedObject.FindProperty(InteractableLayerPropertyName);
                    if (layerMaskProperty != null && layerMaskProperty.intValue == 0)
                    {
                        _issues.Add(new ValidationIssue(
                            Severity.Error,
                            "InteractableLayer on '" + detector.name + "' is empty. Nothing can be detected.",
                            detector,
                            FixKind.SetDetectorLayerAll));
                    }
                }
            }

            if (detectors.Length > 1)
            {
                _issues.Add(new ValidationIssue(
                    Severity.Info,
                    "Multiple InteractionDetectors were found. This is valid for local multiplayer, split-screen, or multi-agent tools.",
                    detectors[0]));
            }
        }

        private void ValidateEffectPool()
        {
            PooledEffect[] pooledEffects = FindObjectsByType<PooledEffect>(FindObjectsSortMode.None);
            for (int i = 0; i < pooledEffects.Length; i++)
            {
                PooledEffect effect = pooledEffects[i];
                if (effect == null || effect.gameObject.scene.isLoaded && !effect.gameObject.activeInHierarchy)
                {
                    continue;
                }

                ParticleSystem particleSystem = effect.GetComponent<ParticleSystem>();
                if (particleSystem == null)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Info,
                        "PooledEffect '" + effect.name + "' has no ParticleSystem. This is valid for non-particle effects.",
                        effect));
                }
            }
        }

        private void ValidateLayers()
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer == -1)
            {
                _issues.Add(new ValidationIssue(
                    Severity.Info,
                    "Layer 'Interactable' was not found. Creating one can improve authoring clarity and detector mask setup.",
                    null));
            }
        }

        private void ValidateTwoStateInteractions()
        {
            TwoStateInteractionBase[] twoStates = FindObjectsByType<TwoStateInteractionBase>(FindObjectsSortMode.None);
            for (int i = 0; i < twoStates.Length; i++)
            {
                TwoStateInteractionBase twoState = twoStates[i];
                if (twoState == null)
                {
                    continue;
                }

                Collider collider3D = twoState.GetComponent<Collider>();
                Collider2D collider2D = twoState.GetComponent<Collider2D>();
                if (collider3D == null && collider2D == null)
                {
                    _issues.Add(new ValidationIssue(
                        Severity.Warning,
                        "TwoStateInteraction '" + twoState.name + "' has no Collider or Collider2D. Detector-based use requires one collider.",
                        twoState));
                }
            }
        }

        private bool HasFixableIssues()
        {
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].FixKind != FixKind.None)
                {
                    return true;
                }
            }

            return false;
        }

        private void FixAllIssues()
        {
            int fixedBefore = _fixedCount;
            for (int i = 0; i < _issues.Count; i++)
            {
                ApplyFix(_issues[i]);
            }

            if (_fixedCount != fixedBefore)
            {
                RunValidation();
            }
        }

        private void ApplyFix(ValidationIssue issue)
        {
            if (issue.FixKind == FixKind.None || issue.Target == null)
            {
                return;
            }

            switch (issue.FixKind)
            {
                case FixKind.AddBestCollider:
                    if (issue.Target is Component component)
                    {
                        AddBestCollider(component.gameObject);
                    }
                    break;
                case FixKind.SetCollider3DTrigger:
                    if (issue.Target is Collider collider3D)
                    {
                        Undo.RecordObject(collider3D, "Set Interaction Collider Trigger");
                        collider3D.isTrigger = true;
                        EditorUtility.SetDirty(collider3D);
                        _fixedCount++;
                    }
                    break;
                case FixKind.SetCollider2DTrigger:
                    if (issue.Target is Collider2D collider2D)
                    {
                        Undo.RecordObject(collider2D, "Set Interaction Collider2D Trigger");
                        collider2D.isTrigger = true;
                        EditorUtility.SetDirty(collider2D);
                        _fixedCount++;
                    }
                    break;
                case FixKind.SetDetectorRadius:
                    SetDetectorFloat(issue.Target, DetectorRadiusPropertyName, 3f);
                    break;
                case FixKind.SetDetectorLayerAll:
                    SetDetectorInt(issue.Target, InteractableLayerPropertyName, -1);
                    break;
            }
        }

        private void AddBestCollider(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.GetComponent<Collider>() != null || gameObject.GetComponent<Collider2D>() != null)
            {
                return;
            }

            bool looksLike2D = gameObject.GetComponent<SpriteRenderer>() != null || gameObject.GetComponent<Rigidbody2D>() != null;
            if (looksLike2D)
            {
                Undo.AddComponent<CircleCollider2D>(gameObject);
            }
            else
            {
                Undo.AddComponent<SphereCollider>(gameObject);
            }

            _fixedCount++;
        }

        private void SetDetectorFloat(Object target, string propertyName, float value)
        {
            if (target == null)
            {
                return;
            }

            using var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            Undo.RecordObject(target, "Fix Interaction Detector");
            property.floatValue = value;
            serializedObject.ApplyModifiedProperties();
            _fixedCount++;
        }

        private void SetDetectorInt(Object target, string propertyName, int value)
        {
            if (target == null)
            {
                return;
            }

            using var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            Undo.RecordObject(target, "Fix Interaction Detector");
            property.intValue = value;
            serializedObject.ApplyModifiedProperties();
            _fixedCount++;
        }

        private static string GetSeverityLabel(Severity severity)
        {
            return severity switch
            {
                Severity.Error => "Error",
                Severity.Warning => "Warning",
                _ => "Info"
            };
        }

        private static Severity ToValidationSeverity(InteractionComponentRuleSeverity severity)
        {
            return severity switch
            {
                InteractionComponentRuleSeverity.Error => Severity.Error,
                InteractionComponentRuleSeverity.Warning => Severity.Warning,
                _ => Severity.Info
            };
        }
    }
}
