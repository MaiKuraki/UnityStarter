using CycloneGames.RPGFoundation.Editor;
using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Editor
{
    [CustomEditor(typeof(ProjectileSystemBehaviour), true)]
    [CanEditMultipleObjects]
    public class ProjectileSystemBehaviourEditor : UnityEditor.Editor
    {
        private static readonly string[] ExcludedProperties =
        {
            "m_Script",
            "SimulationPlane",
            "CollisionMode",
            "ProjectileCapacity",
            "EventCapacity",
            "CollisionHitCapacity",
            "LockedAxisValue",
            "Gravity",
            "AutoTick"
        };

        private static readonly GUIContent SimulationPlaneLabel = new GUIContent("Simulation Plane", "Full 3D or locked-axis 2D plane used by core projectile motion.");
        private static readonly GUIContent CollisionModeLabel = new GUIContent("Collision Mode", "Unity collision adapter used by the default system.");
        private static readonly GUIContent LockedAxisLabel = new GUIContent("Locked Axis Value", "Plane coordinate written to the locked axis for 2D style simulation.");
        private static readonly GUIContent GravityLabel = new GUIContent("Gravity", "Base gravity vector multiplied by projectile definition gravity scale.");
        private static readonly GUIContent AutoTickLabel = new GUIContent("Auto Tick", "When enabled, Update drives the projectile world with Time.deltaTime.");
        private static readonly GUIContent ProjectileCapacityLabel = new GUIContent("Projectile Capacity", "Maximum active projectile count.");
        private static readonly GUIContent EventCapacityLabel = new GUIContent("Event Capacity", "Maximum hit events buffered per step.");
        private static readonly GUIContent CollisionHitCapacityLabel = new GUIContent("Collision Hit Capacity", "Scratch result count for each collision sweep.");

        private SerializedProperty _simulationPlane;
        private SerializedProperty _collisionMode;
        private SerializedProperty _projectileCapacity;
        private SerializedProperty _eventCapacity;
        private SerializedProperty _collisionHitCapacity;
        private SerializedProperty _lockedAxisValue;
        private SerializedProperty _gravity;
        private SerializedProperty _autoTick;

        private static bool s_validationFoldout = true;
        private static bool s_simulationFoldout = true;
        private static bool s_capacityFoldout = true;
        private static bool s_runtimeFoldout = true;

        private void OnEnable()
        {
            _simulationPlane = serializedObject.FindProperty("SimulationPlane");
            _collisionMode = serializedObject.FindProperty("CollisionMode");
            _projectileCapacity = serializedObject.FindProperty("ProjectileCapacity");
            _eventCapacity = serializedObject.FindProperty("EventCapacity");
            _collisionHitCapacity = serializedObject.FindProperty("CollisionHitCapacity");
            _lockedAxisValue = serializedObject.FindProperty("LockedAxisValue");
            _gravity = serializedObject.FindProperty("Gravity");
            _autoTick = serializedObject.FindProperty("AutoTick");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();
            DrawValidation();
            DrawSimulation();
            DrawCapacity();
            DrawRuntimeStatus();

            RPGFoundationEditorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Extension Fields",
                ExcludedProperties);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawInspectorHeader()
        {
            EditorGUILayout.LabelField("Projectile System", EditorStyles.boldLabel);
            RPGFoundationEditorUiUtility.DrawHelpBox(
                "Unity lifecycle bridge for ProjectileWorld. Derived behaviours can override world and collision-world creation without replacing the Inspector.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            RPGFoundationEditorUiUtility.DrawSection("Validation", RPGFoundationEditorUiUtility.ColorWarning, ref s_validationFoldout, () =>
            {
                bool hasIssue = false;

                if (!HasMixedValues(_projectileCapacity) && _projectileCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Projectile Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_eventCapacity) && _eventCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Event Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_collisionHitCapacity) && _collisionHitCapacity.intValue <= 0)
                {
                    hasIssue = true;
                    RPGFoundationEditorUiUtility.DrawHelpBox("Collision Hit Capacity must be greater than zero.", MessageType.Error);
                }

                if (!HasMixedValues(_collisionMode) && (ProjectileCollisionMode)_collisionMode.enumValueIndex == ProjectileCollisionMode.None)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Collision Mode is None. This is valid for custom collision worlds or pure visual projectiles, but default Unity collisions will not run.", MessageType.Info);
                }

                if (!HasMixedValues(_autoTick) && !_autoTick.boolValue)
                {
                    RPGFoundationEditorUiUtility.DrawHelpBox("Auto Tick is disabled. Another owner must call Step with the authoritative tick and target provider.", MessageType.Info);
                }

                if (!hasIssue)
                {
                    RPGFoundationEditorUiUtility.DrawStatusRow("Status", "Ready", RPGFoundationEditorUiUtility.ColorBehavior);
                }
            });
        }

        private void DrawSimulation()
        {
            RPGFoundationEditorUiUtility.DrawSection("Simulation", RPGFoundationEditorUiUtility.ColorCore, ref s_simulationFoldout, () =>
            {
                EditorGUILayout.PropertyField(_simulationPlane, SimulationPlaneLabel);
                EditorGUILayout.PropertyField(_collisionMode, CollisionModeLabel);
                EditorGUILayout.PropertyField(_lockedAxisValue, LockedAxisLabel);
                EditorGUILayout.PropertyField(_gravity, GravityLabel);
                EditorGUILayout.PropertyField(_autoTick, AutoTickLabel);
            });
        }

        private void DrawCapacity()
        {
            RPGFoundationEditorUiUtility.DrawSection("Capacity", RPGFoundationEditorUiUtility.ColorRuntime, ref s_capacityFoldout, () =>
            {
                EditorGUILayout.PropertyField(_projectileCapacity, ProjectileCapacityLabel);
                EditorGUILayout.PropertyField(_eventCapacity, EventCapacityLabel);
                EditorGUILayout.PropertyField(_collisionHitCapacity, CollisionHitCapacityLabel);

                if (!HasMixedValues(_projectileCapacity) && !HasMixedValues(_collisionHitCapacity))
                {
                    int scratchSlots = Mathf.Max(1, _collisionHitCapacity.intValue);
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Cast Scratch Hits", scratchSlots.ToString());
                }
            });
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying || targets.Length != 1)
            {
                return;
            }

            ProjectileSystemBehaviour system = (ProjectileSystemBehaviour)target;
            RPGFoundationEditorUiUtility.DrawSection("Runtime Status", RPGFoundationEditorUiUtility.ColorDebug, ref s_runtimeFoldout, () =>
            {
                RPGFoundationEditorUiUtility.DrawStatusRow("Initialized", system.IsInitialized ? "Yes" : "No", system.IsInitialized ? RPGFoundationEditorUiUtility.ColorBehavior : RPGFoundationEditorUiUtility.ColorWarning);
                RPGFoundationEditorUiUtility.DrawReadOnlyText("Tick", system.CurrentTick.ToString());
                if (system.IsInitialized)
                {
                    ProjectileWorldStats stats = system.World.Stats;
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Active Projectiles", stats.ActiveCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("World Capacity", stats.Capacity.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Capacity Used", (stats.ActiveCapacityRatio * 100f).ToString("F1") + "%");
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Peak Active", stats.PeakActiveCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Steps", stats.StepCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Last Hit Events", stats.LastHitEventCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Collision Queries", stats.LastCollisionQueryCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Collision Hits", stats.LastCollisionHitCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Spawn Accepted", stats.TotalSpawnAcceptedCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Spawn Rejected", (stats.TotalSpawnRejectedInvalidCount + stats.TotalSpawnRejectedCapacityCount).ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Hit Overflow", stats.TotalHitEventOverflowCount.ToString());
                    RPGFoundationEditorUiUtility.DrawReadOnlyText("Iteration Limits", stats.TotalCollisionIterationLimitCount.ToString());

                    if (GUILayout.Button("Reset Runtime Stats"))
                    {
                        system.World.ResetStats();
                    }
                }
            });
        }

        private static bool HasMixedValues(SerializedProperty property)
        {
            return property == null || property.hasMultipleDifferentValues;
        }
    }
}
