using CycloneGames.RPGFoundation.Editor;
using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Editor
{
    [CustomEditor(typeof(ProjectileDefinitionAsset), true)]
    [CanEditMultipleObjects]
    public class ProjectileDefinitionAssetEditor : UnityEditor.Editor
    {
        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "DefinitionId",
            "GuidanceMode",
            "LifecycleFlags",
            "InitialSpeed",
            "MaxSpeed",
            "Acceleration",
            "GravityScale",
            "Radius",
            "MaxLifetime",
            "TurnRateDegreesPerSecond",
            "LeadPredictionTime",
            "PierceCount",
            "BounceCount",
            "CollisionLayerMask",
            "EffectPayloadId"
        };

        private static readonly GUIContent DefinitionIdLabel = new GUIContent("Definition Id", "Stable projectile definition id used by gameplay, networking, replay, and data tables.");
        private static readonly GUIContent GuidanceModeLabel = new GUIContent("Guidance Mode", "Direction, homing, or lead-homing steering behavior.");
        private static readonly GUIContent LifecycleFlagsLabel = new GUIContent("Lifecycle Flags", "Ownership, prediction, authority, and hit lifecycle behavior.");
        private static readonly GUIContent InitialSpeedLabel = new GUIContent("Initial Speed", "Initial projectile speed in world units per second.");
        private static readonly GUIContent MaxSpeedLabel = new GUIContent("Max Speed", "Maximum speed after acceleration. Set to zero only for custom derived behavior.");
        private static readonly GUIContent AccelerationLabel = new GUIContent("Acceleration", "Speed delta per second. Negative values slow the projectile.");
        private static readonly GUIContent GravityScaleLabel = new GUIContent("Gravity Scale", "Multiplier applied to the projectile system gravity.");
        private static readonly GUIContent RadiusLabel = new GUIContent("Radius", "Sweep radius for collision queries.");
        private static readonly GUIContent MaxLifetimeLabel = new GUIContent("Max Lifetime", "Maximum lifetime in seconds.");
        private static readonly GUIContent TurnRateLabel = new GUIContent("Turn Rate", "Maximum homing turn rate in degrees per second.");
        private static readonly GUIContent LeadTimeLabel = new GUIContent("Lead Prediction Time", "Target velocity prediction time used by lead homing.");
        private static readonly GUIContent PierceCountLabel = new GUIContent("Pierce Count", "Number of non-terminal pierce continuations before normal hit behavior.");
        private static readonly GUIContent BounceCountLabel = new GUIContent("Bounce Count", "Number of non-terminal bounce continuations before normal hit behavior.");
        private static readonly GUIContent CollisionLayerMaskLabel = new GUIContent("Collision Layer Mask", "Layers included by the projectile collision adapter.");
        private static readonly GUIContent EffectPayloadIdLabel = new GUIContent("Effect Payload Id", "Stable gameplay payload id emitted with hit events.");
        private static readonly GUIContent PredictedFlagLabel = new GUIContent("Predicted", "This definition may be spawned by client prediction. Runtime authority still decides whether prediction is accepted.");
        private static readonly GUIContent AuthoritativeFlagLabel = new GUIContent("Authoritative", "This definition may be simulated by the server or another authoritative owner.");
        private static readonly GUIContent DespawnOnHitFlagLabel = new GUIContent("Despawn On Hit", "Terminal hits despawn the projectile when no bounce or pierce continuation remains.");
        private static readonly GUIContent IgnoreOwnerFlagLabel = new GUIContent("Ignore Owner", "Collision adapters should ignore the owner where supported.");
        private static readonly GUIContent HasVisualArcFlagLabel = new GUIContent("Has Visual Arc", "Presentation systems may render an arced visual trail for this definition.");

        private SerializedProperty _definitionId;
        private SerializedProperty _guidanceMode;
        private SerializedProperty _lifecycleFlags;
        private SerializedProperty _initialSpeed;
        private SerializedProperty _maxSpeed;
        private SerializedProperty _acceleration;
        private SerializedProperty _gravityScale;
        private SerializedProperty _radius;
        private SerializedProperty _maxLifetime;
        private SerializedProperty _turnRateDegreesPerSecond;
        private SerializedProperty _leadPredictionTime;
        private SerializedProperty _pierceCount;
        private SerializedProperty _bounceCount;
        private SerializedProperty _collisionLayerMask;
        private SerializedProperty _effectPayloadId;

        private readonly ProjectileDefinitionValidationIssue[] _validationIssues =
            new ProjectileDefinitionValidationIssue[ProjectileDefinitionValidator.RECOMMENDED_ISSUE_CAPACITY];

        private static bool s_validationFoldout = true;
        private static bool s_identityFoldout = true;
        private static bool s_guidanceFoldout = true;
        private static bool s_kinematicsFoldout = true;
        private static bool s_collisionFoldout = true;
        private static bool s_effectFoldout;

        private void OnEnable()
        {
            _definitionId = serializedObject.FindProperty("DefinitionId");
            _guidanceMode = serializedObject.FindProperty("GuidanceMode");
            _lifecycleFlags = serializedObject.FindProperty("LifecycleFlags");
            _initialSpeed = serializedObject.FindProperty("InitialSpeed");
            _maxSpeed = serializedObject.FindProperty("MaxSpeed");
            _acceleration = serializedObject.FindProperty("Acceleration");
            _gravityScale = serializedObject.FindProperty("GravityScale");
            _radius = serializedObject.FindProperty("Radius");
            _maxLifetime = serializedObject.FindProperty("MaxLifetime");
            _turnRateDegreesPerSecond = serializedObject.FindProperty("TurnRateDegreesPerSecond");
            _leadPredictionTime = serializedObject.FindProperty("LeadPredictionTime");
            _pierceCount = serializedObject.FindProperty("PierceCount");
            _bounceCount = serializedObject.FindProperty("BounceCount");
            _collisionLayerMask = serializedObject.FindProperty("CollisionLayerMask");
            _effectPayloadId = serializedObject.FindProperty("EffectPayloadId");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();
            DrawValidation();
            DrawIdentity();
            DrawGuidance();
            DrawKinematics();
            DrawCollision();
            DrawEffects();

            RPGFoundationEditorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Extension Fields",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.LabelField("Projectile Definition", EditorStyles.boldLabel);
            RPGFoundationEditorUiUtility.DrawHelpBox(
                "Authoring asset for spawned projectile entities. Core simulation uses a copied runtime definition, so this asset should remain immutable during gameplay.",
                MessageType.None);
            DrawPresetButtons();
        }

        private void DrawValidation()
        {
            RPGFoundationEditorUiUtility.DrawSection("Validation", RPGFoundationEditorUiUtility.ColorWarning, ref s_validationFoldout, () =>
            {
                DrawDefinitionValidation();
            });
        }

        private void DrawIdentity()
        {
            RPGFoundationEditorUiUtility.DrawSection("Identity", RPGFoundationEditorUiUtility.ColorCore, ref s_identityFoldout, () =>
            {
                EditorGUILayout.PropertyField(_definitionId, DefinitionIdLabel);
                DrawLifecycleFlags();
            });
        }

        private void DrawGuidance()
        {
            RPGFoundationEditorUiUtility.DrawSection("Guidance", RPGFoundationEditorUiUtility.ColorBehavior, ref s_guidanceFoldout, () =>
            {
                EditorGUILayout.PropertyField(_guidanceMode, GuidanceModeLabel);

                ProjectileGuidanceMode mode = HasMixedValues(_guidanceMode)
                    ? ProjectileGuidanceMode.Direction
                    : (ProjectileGuidanceMode)_guidanceMode.enumValueIndex;

                using (new EditorGUI.DisabledScope(mode == ProjectileGuidanceMode.Direction && !HasMixedValues(_guidanceMode)))
                {
                    EditorGUILayout.PropertyField(_turnRateDegreesPerSecond, TurnRateLabel);
                    EditorGUILayout.PropertyField(_leadPredictionTime, LeadTimeLabel);
                }

                if (!HasMixedValues(_guidanceMode) && mode == ProjectileGuidanceMode.Direction)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Direction mode ignores turn rate and lead prediction. Use Homing or Lead Homing for target-seeking projectiles.", MessageType.None);
                }
            });
        }

        private void DrawKinematics()
        {
            RPGFoundationEditorUiUtility.DrawSection("Kinematics", RPGFoundationEditorUiUtility.ColorRuntime, ref s_kinematicsFoldout, () =>
            {
                EditorGUILayout.PropertyField(_initialSpeed, InitialSpeedLabel);
                EditorGUILayout.PropertyField(_maxSpeed, MaxSpeedLabel);
                EditorGUILayout.PropertyField(_acceleration, AccelerationLabel);
                EditorGUILayout.PropertyField(_gravityScale, GravityScaleLabel);
                EditorGUILayout.PropertyField(_maxLifetime, MaxLifetimeLabel);
            });
        }

        private void DrawCollision()
        {
            RPGFoundationEditorUiUtility.DrawSection("Collision", RPGFoundationEditorUiUtility.ColorWarning, ref s_collisionFoldout, () =>
            {
                EditorGUILayout.PropertyField(_radius, RadiusLabel);
                EditorGUILayout.PropertyField(_pierceCount, PierceCountLabel);
                EditorGUILayout.PropertyField(_bounceCount, BounceCountLabel);
                EditorGUILayout.PropertyField(_collisionLayerMask, CollisionLayerMaskLabel);
            });
        }

        private void DrawEffects()
        {
            RPGFoundationEditorUiUtility.DrawSection("Effects And Payload", RPGFoundationEditorUiUtility.ColorDebug, ref s_effectFoldout, () =>
            {
                EditorGUILayout.PropertyField(_effectPayloadId, EffectPayloadIdLabel);
                RPGFoundationEditorUiUtility.DrawHelpBox(
                    "Effect Payload Id is intentionally an integer so GameplayAbilities, GameplayTags, DataTable, or product-specific systems can map it without adding dependencies to Projectile.Core.",
                MessageType.None);
            });
        }

        private void DrawPresetButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fireball"))
            {
                ApplyPreset(
                    ProjectileGuidanceMode.Direction,
                    initialSpeed: 16f,
                    maxSpeed: 16f,
                    acceleration: 0f,
                    gravityScale: 0f,
                    radius: 0.18f,
                    maxLifetime: 4f,
                    turnRate: 0f,
                    leadTime: 0f,
                    pierceCount: 0,
                    bounceCount: 0);
            }

            if (GUILayout.Button("Arcane Missile"))
            {
                ApplyPreset(
                    ProjectileGuidanceMode.LeadHoming,
                    initialSpeed: 12f,
                    maxSpeed: 18f,
                    acceleration: 3f,
                    gravityScale: 0f,
                    radius: 0.08f,
                    maxLifetime: 5f,
                    turnRate: 360f,
                    leadTime: 0.15f,
                    pierceCount: 0,
                    bounceCount: 0);
            }

            if (GUILayout.Button("Homing Missile"))
            {
                ApplyPreset(
                    ProjectileGuidanceMode.Homing,
                    initialSpeed: 8f,
                    maxSpeed: 14f,
                    acceleration: 4f,
                    gravityScale: 0f,
                    radius: 0.15f,
                    maxLifetime: 6f,
                    turnRate: 180f,
                    leadTime: 0f,
                    pierceCount: 0,
                    bounceCount: 0);
            }

            if (GUILayout.Button("Ricochet"))
            {
                ApplyPreset(
                    ProjectileGuidanceMode.Direction,
                    initialSpeed: 35f,
                    maxSpeed: 35f,
                    acceleration: 0f,
                    gravityScale: 0f,
                    radius: 0.06f,
                    maxLifetime: 2.5f,
                    turnRate: 0f,
                    leadTime: 0f,
                    pierceCount: 0,
                    bounceCount: 3);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDefinitionValidation()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                DrawMultiObjectValidation();
                return;
            }

            var asset = (ProjectileDefinitionAsset)target;
            ProjectileDefinition definition = asset.BuildAuthoringDefinition();
            int issueCount = ProjectileDefinitionValidator.Validate(in definition, _validationIssues);
            if (issueCount == 0)
            {
                RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                return;
            }

            DrawValidationStatus(_validationIssues, issueCount);
        }

        private void DrawMultiObjectValidation()
        {
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                var asset = (ProjectileDefinitionAsset)targets[i];
                ProjectileDefinition definition = asset.BuildAuthoringDefinition();
                int issueCount = ProjectileDefinitionValidator.Validate(in definition, _validationIssues);
                int max = Mathf.Min(issueCount, _validationIssues.Length);
                for (int issueIndex = 0; issueIndex < max; issueIndex++)
                {
                    switch (_validationIssues[issueIndex].Severity)
                    {
                        case ProjectileValidationSeverity.Error:
                            errorCount++;
                            break;
                        case ProjectileValidationSeverity.Warning:
                            warningCount++;
                            break;
                        default:
                            infoCount++;
                            break;
                    }
                }
            }

            if (errorCount == 0 && warningCount == 0 && infoCount == 0)
            {
                RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                return;
            }

            RPGFoundationEditorUiUtility.DrawStatusRow("Errors", errorCount.ToString(), errorCount > 0 ? RPGFoundationEditorUiUtility.ColorError : RPGFoundationEditorUiUtility.ColorBehavior);
            RPGFoundationEditorUiUtility.DrawStatusRow("Warnings", warningCount.ToString(), warningCount > 0 ? RPGFoundationEditorUiUtility.ColorWarning : RPGFoundationEditorUiUtility.ColorBehavior);
            RPGFoundationEditorUiUtility.DrawStatusRow("Info", infoCount.ToString(), RPGFoundationEditorUiUtility.ColorDebug);
        }

        private static void DrawValidationStatus(
            ProjectileDefinitionValidationIssue[] issues,
            int issueCount)
        {
            int max = Mathf.Min(issueCount, issues.Length);
            for (int i = 0; i < max; i++)
            {
                RPGFoundationEditorUiUtility.DrawHelpBox(
                    issues[i].Message,
                    ToMessageType(issues[i].Severity));
            }

            if (issueCount > issues.Length)
            {
                RPGFoundationEditorUiUtility.DrawHelpBox("Validation issue buffer is full. Increase the editor issue buffer to see every issue.", MessageType.Warning);
            }
        }

        private void DrawLifecycleFlags()
        {
            EditorGUILayout.LabelField(LifecycleFlagsLabel, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawLifecycleFlag(PredictedFlagLabel, ProjectileLifecycleFlags.Predicted);
                DrawLifecycleFlag(AuthoritativeFlagLabel, ProjectileLifecycleFlags.Authoritative);
                DrawLifecycleFlag(DespawnOnHitFlagLabel, ProjectileLifecycleFlags.DespawnOnHit);
                DrawLifecycleFlag(IgnoreOwnerFlagLabel, ProjectileLifecycleFlags.IgnoreOwner);
                DrawLifecycleFlag(HasVisualArcFlagLabel, ProjectileLifecycleFlags.HasVisualArc);
            }
        }

        private void DrawLifecycleFlag(
            GUIContent label,
            ProjectileLifecycleFlags flag)
        {
            uint currentValue = unchecked((uint)_lifecycleFlags.intValue);
            bool enabled = (currentValue & (uint)flag) == (uint)flag;
            EditorGUI.showMixedValue = _lifecycleFlags.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool nextValue = EditorGUILayout.ToggleLeft(label, enabled);
            if (EditorGUI.EndChangeCheck())
            {
                if (nextValue)
                {
                    currentValue |= (uint)flag;
                }
                else
                {
                    currentValue &= ~(uint)flag;
                }

                _lifecycleFlags.intValue = unchecked((int)currentValue);
            }

            EditorGUI.showMixedValue = false;
        }

        private void ApplyPreset(
            ProjectileGuidanceMode guidanceMode,
            float initialSpeed,
            float maxSpeed,
            float acceleration,
            float gravityScale,
            float radius,
            float maxLifetime,
            float turnRate,
            float leadTime,
            int pierceCount,
            int bounceCount)
        {
            Undo.RecordObjects(targets, "Apply Projectile Definition Preset");
            _guidanceMode.enumValueIndex = (int)guidanceMode;
            _initialSpeed.floatValue = initialSpeed;
            _maxSpeed.floatValue = maxSpeed;
            _acceleration.floatValue = acceleration;
            _gravityScale.floatValue = gravityScale;
            _radius.floatValue = radius;
            _maxLifetime.floatValue = maxLifetime;
            _turnRateDegreesPerSecond.floatValue = turnRate;
            _leadPredictionTime.floatValue = leadTime;
            _pierceCount.intValue = pierceCount;
            _bounceCount.intValue = bounceCount;
            uint lifecycle = unchecked((uint)_lifecycleFlags.intValue);
            lifecycle |= (uint)(ProjectileLifecycleFlags.DespawnOnHit | ProjectileLifecycleFlags.IgnoreOwner);
            _lifecycleFlags.intValue = unchecked((int)lifecycle);
        }

        private static MessageType ToMessageType(ProjectileValidationSeverity severity)
        {
            switch (severity)
            {
                case ProjectileValidationSeverity.Error:
                    return MessageType.Error;
                case ProjectileValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private static bool HasMixedValues(SerializedProperty property)
        {
            return property == null || property.hasMultipleDifferentValues;
        }
    }
}
