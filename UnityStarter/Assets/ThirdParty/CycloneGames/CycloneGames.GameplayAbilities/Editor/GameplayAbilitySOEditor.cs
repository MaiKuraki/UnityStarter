using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom editor for GameplayAbilitySO and derived types.
    /// Provides organized layout with validation, tag summary, and conditional visibility.
    /// </summary>
    [CustomEditor(typeof(Runtime.GameplayAbilitySO), true)]
    [CanEditMultipleObjects]
    public class GameplayAbilitySOEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_BasePropertyNames = new HashSet<string>();
        private static bool s_BasePropertiesInitialized;

        // Basic
        private SerializedProperty abilityNameProp;
        private SerializedProperty instancingPolicyProp;
        private SerializedProperty netExecPolicyProp;
        private SerializedProperty costEffectProp;
        private SerializedProperty cooldownEffectProp;

        // Activation
        private SerializedProperty activateOnGrantedProp;
        private SerializedProperty triggerDataProp;

        // Tags - ability identity
        private SerializedProperty abilityTagsProp;
        private SerializedProperty activationOwnedTagsProp;

        // Tags - activation requirements
        private SerializedProperty activationBlockedTagsProp;
        private SerializedProperty activationRequiredTagsProp;

        // Tags - interaction
        private SerializedProperty cancelWithTagProp;
        private SerializedProperty blockWithTagProp;

        // Tags - source/target
        private SerializedProperty srcRequiredProp;
        private SerializedProperty srcBlockedProp;
        private SerializedProperty tgtRequiredProp;
        private SerializedProperty tgtBlockedProp;

        // Foldout states
        private bool showBasic = true;
        private bool showCostCooldown = true;
        private bool showActivation = true;
        private bool showAbilityTags = true;
        private bool showInteractionTags = false;
        private bool showSourceTargetTags = false;
        private bool showDerivedFields = true;
        private bool showSummary = true;

        private static GUIStyle s_SummaryStyle;
        private static GUIStyle s_SectionHeader;
        private static readonly Color s_SectionLine = new Color(0.3f, 0.3f, 0.3f, 1f);

        private readonly StringBuilder sb = new StringBuilder(256);

        private void OnEnable()
        {
            CacheBasePropertyNames();
            CacheProperties();
        }

        private static void CacheBasePropertyNames()
        {
            if (s_BasePropertiesInitialized) return;
            var baseType = typeof(Runtime.GameplayAbilitySO);
            foreach (var field in baseType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                s_BasePropertyNames.Add(field.Name);
            }
            s_BasePropertiesInitialized = true;
        }

        private void CacheProperties()
        {
            abilityNameProp = serializedObject.FindProperty("AbilityName");
            instancingPolicyProp = serializedObject.FindProperty("InstancingPolicy");
            netExecPolicyProp = serializedObject.FindProperty("NetExecutionPolicy");
            costEffectProp = serializedObject.FindProperty("CostEffect");
            cooldownEffectProp = serializedObject.FindProperty("CooldownEffect");

            activateOnGrantedProp = serializedObject.FindProperty("ActivateAbilityOnGranted");
            triggerDataProp = serializedObject.FindProperty("AbilityTriggerDataList");

            abilityTagsProp = serializedObject.FindProperty("AbilityTags");
            activationOwnedTagsProp = serializedObject.FindProperty("ActivationOwnedTags");
            activationBlockedTagsProp = serializedObject.FindProperty("ActivationBlockedTags");
            activationRequiredTagsProp = serializedObject.FindProperty("ActivationRequiredTags");
            cancelWithTagProp = serializedObject.FindProperty("CancelAbilitiesWithTag");
            blockWithTagProp = serializedObject.FindProperty("BlockAbilitiesWithTag");

            srcRequiredProp = serializedObject.FindProperty("SourceRequiredTags");
            srcBlockedProp = serializedObject.FindProperty("SourceBlockedTags");
            tgtRequiredProp = serializedObject.FindProperty("TargetRequiredTags");
            tgtBlockedProp = serializedObject.FindProperty("TargetBlockedTags");
        }

        private static void EnsureStyles()
        {
            if (s_SummaryStyle != null) return;
            s_SummaryStyle = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = 11,
                padding = new RectOffset(8, 8, 6, 6)
            };
            s_SectionHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            // ═══ Summary ═══
            showSummary = EditorGUILayout.Foldout(showSummary, "Ability Summary", true);
            if (showSummary) DrawSummary();

            EditorGUILayout.Space(4);
            DrawValidationWarnings();

            // ═══ Basic Definition ═══
            DrawSectionLine();
            showBasic = EditorGUILayout.Foldout(showBasic, "Basic Definition", true);
            if (showBasic)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(abilityNameProp, new GUIContent("Name"));
                EditorGUILayout.PropertyField(instancingPolicyProp, new GUIContent("Instancing Policy",
                    "NonInstanced: CDO only (best perf, no state)\nInstancedPerActor: One per ASC (most common)\nInstancedPerExecution: New per activation (clean state)"));
                EditorGUILayout.PropertyField(netExecPolicyProp, new GUIContent("Net Execution",
                    "LocalOnly: Client cosmetic\nLocalPredicted: Client-predicted, server-authoritative\nServerOnly: Server-side only"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Cost & Cooldown ═══
            DrawSectionLine();
            showCostCooldown = EditorGUILayout.Foldout(showCostCooldown, "Cost & Cooldown", true);
            if (showCostCooldown)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(costEffectProp, new GUIContent("Cost Effect",
                    "A GameplayEffect that defines the resource cost (Mana, Stamina, etc.). Applied when the ability commits."));
                EditorGUILayout.PropertyField(cooldownEffectProp, new GUIContent("Cooldown Effect",
                    "A GameplayEffect that applies a cooldown tag. The ability cannot re-activate while on cooldown."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Activation ═══
            DrawSectionLine();
            showActivation = EditorGUILayout.Foldout(showActivation, "Activation", true);
            if (showActivation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(activateOnGrantedProp, new GUIContent("Activate On Granted",
                    "If true, the ability activates automatically when granted. Use for passive abilities."));
                EditorGUILayout.PropertyField(triggerDataProp, new GUIContent("Ability Triggers",
                    "Define automatic trigger conditions (GameplayEvent, OwnedTagAdded, OwnedTagRemoved)."), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Ability Tags ═══
            DrawSectionLine();
            showAbilityTags = EditorGUILayout.Foldout(showAbilityTags, "Ability Tags & Activation Requirements", true);
            if (showAbilityTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(abilityTagsProp, new GUIContent("Ability Tags",
                    "Tags that identify this ability (e.g., 'Ability.Damage.Fire'). Used for cancel/block matching."));
                EditorGUILayout.PropertyField(activationOwnedTagsProp, new GUIContent("Activation Owned Tags",
                    "Tags granted to the owner while this ability is active."));
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(activationRequiredTagsProp, new GUIContent("Activation Required",
                    "Owner must have ALL these tags to activate."));
                EditorGUILayout.PropertyField(activationBlockedTagsProp, new GUIContent("Activation Blocked",
                    "Owner must have NONE of these tags to activate."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Interaction Tags ═══
            DrawSectionLine();
            showInteractionTags = EditorGUILayout.Foldout(showInteractionTags, "Interaction Tags (Cancel/Block)", true);
            if (showInteractionTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(cancelWithTagProp, new GUIContent("Cancel Abilities With Tag",
                    "When this ability activates, cancel any other active abilities with these tags."));
                EditorGUILayout.PropertyField(blockWithTagProp, new GUIContent("Block Abilities With Tag",
                    "While this ability is active, block activation of abilities with these tags."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Source/Target Tags ═══
            DrawSectionLine();
            showSourceTargetTags = EditorGUILayout.Foldout(showSourceTargetTags, "Source/Target Tag Requirements", true);
            if (showSourceTargetTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Source (Caster)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(srcRequiredProp, new GUIContent("Required Tags"));
                EditorGUILayout.PropertyField(srcBlockedProp, new GUIContent("Blocked Tags"));
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(tgtRequiredProp, new GUIContent("Required Tags"));
                EditorGUILayout.PropertyField(tgtBlockedProp, new GUIContent("Blocked Tags"));
                EditorGUI.indentLevel--;
            }

            // ═══ Derived Fields ═══
            DrawDerivedClassFields();

            serializedObject.ApplyModifiedProperties();
        }

        #region Summary

        private void DrawSummary()
        {
            sb.Clear();

            string name = abilityNameProp.stringValue;
            if (string.IsNullOrEmpty(name)) name = target.name;
            sb.Append("<b>").Append(name).Append("</b>");

            // Instancing
            var instancing = (Runtime.EGameplayAbilityInstancingPolicy)instancingPolicyProp.enumValueIndex;
            string instLabel = instancing switch
            {
                Runtime.EGameplayAbilityInstancingPolicy.NonInstanced => "NonInstanced (CDO)",
                Runtime.EGameplayAbilityInstancingPolicy.InstancedPerActor => "InstancedPerActor",
                Runtime.EGameplayAbilityInstancingPolicy.InstancedPerExecution => "InstancedPerExecution",
                _ => "?"
            };
            sb.Append("  \u2022  <color=#6BB8E0>").Append(instLabel).Append("</color>");

            // Net
            var netPolicy = (Runtime.ENetExecutionPolicy)netExecPolicyProp.enumValueIndex;
            sb.Append("  \u2022  ").Append(netPolicy.ToString());

            // Passive
            if (activateOnGrantedProp.boolValue)
                sb.Append("\n<color=#66DD88>Passive (Activates on Grant)</color>");

            // Cost / Cooldown
            bool hasCost = costEffectProp.objectReferenceValue != null;
            bool hasCooldown = cooldownEffectProp.objectReferenceValue != null;
            if (hasCost || hasCooldown)
            {
                sb.Append('\n');
                if (hasCost) sb.Append("Cost: ").Append(costEffectProp.objectReferenceValue.name).Append("  ");
                if (hasCooldown) sb.Append("Cooldown: ").Append(cooldownEffectProp.objectReferenceValue.name);
            }

            // Triggers
            if (triggerDataProp != null && triggerDataProp.arraySize > 0)
            {
                sb.Append('\n').Append(triggerDataProp.arraySize).Append(" trigger(s)");
            }

            EditorGUILayout.LabelField(sb.ToString(), s_SummaryStyle);
        }

        #endregion

        #region Validation

        private void DrawValidationWarnings()
        {
            bool hasWarnings = false;

            if (string.IsNullOrEmpty(abilityNameProp.stringValue))
            {
                EditorGUILayout.HelpBox("Ability Name is empty. This makes debugging difficult.", MessageType.Warning);
                hasWarnings = true;
            }

            // Passive with cooldown is unusual
            if (activateOnGrantedProp.boolValue && cooldownEffectProp.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("'Activate On Granted' is enabled along with a Cooldown. Passive abilities typically don't use cooldowns.", MessageType.Info);
                hasWarnings = true;
            }

            // NonInstanced with state
            if (instancingPolicyProp.enumValueIndex == (int)Runtime.EGameplayAbilityInstancingPolicy.NonInstanced)
            {
                // Check if derived class might store state
                if (target.GetType() != typeof(Runtime.GameplayAbilitySO))
                {
                    EditorGUILayout.HelpBox("Instancing is 'NonInstanced' (CDO). Derived ability code must not store per-activation state in member variables.", MessageType.Info);
                    hasWarnings = true;
                }
            }

            if (hasWarnings) EditorGUILayout.Space(4);
        }

        #endregion

        #region Derived Fields

        private void DrawDerivedClassFields()
        {
            var targetType = target.GetType();
            if (targetType == typeof(Runtime.GameplayAbilitySO)) return;

            var derivedFields = new List<SerializedProperty>();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                if (s_BasePropertyNames.Contains(iterator.name)) continue;
                derivedFields.Add(iterator.Copy());
            }

            if (derivedFields.Count > 0)
            {
                EditorGUILayout.Space(8);
                DrawSectionLine();
                showDerivedFields = EditorGUILayout.Foldout(showDerivedFields,
                    $"Custom Fields ({targetType.Name})", true);

                if (showDerivedFields)
                {
                    EditorGUI.indentLevel++;
                    foreach (var prop in derivedFields)
                    {
                        EditorGUILayout.PropertyField(prop, true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        #endregion

        #region Helpers

        private static void DrawSectionLine()
        {
            EditorGUILayout.Space(2);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, s_SectionLine);
            EditorGUILayout.Space(2);
        }

        #endregion
    }
}
