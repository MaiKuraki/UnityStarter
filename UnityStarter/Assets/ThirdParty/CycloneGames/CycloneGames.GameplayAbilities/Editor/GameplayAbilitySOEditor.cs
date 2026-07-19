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
        private SerializedProperty executionPolicyProp;
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
        private static readonly GUIContent s_NameContent = new GUIContent("Name");
        private static readonly GUIContent s_InstancingContent = new GUIContent(
            "Instancing Policy",
            "InstancedPerActor: one instance per ASC. InstancedPerExecution: one instance per activation. NonInstanced is not supported by the Unity Runtime path.");
        private static readonly GUIContent s_ExecutionPolicyContent = new GUIContent(
            "Execution Policy",
            "LocalOnly executes in the current runtime. AuthorityOnly requires a runtime context that owns simulation authority.");
        private static readonly GUIContent s_CostContent = new GUIContent(
            "Cost Effect",
            "A GameplayEffect that defines the resource cost and is applied when the ability commits.");
        private static readonly GUIContent s_CooldownContent = new GUIContent(
            "Cooldown Effect",
            "A GameplayEffect that grants the cooldown state checked before activation.");
        private static readonly GUIContent s_ActivateOnGrantedContent = new GUIContent(
            "Activate On Granted",
            "Activates the ability immediately after a successful grant. Use this for passive abilities with an explicit lifetime.");
        private static readonly GUIContent s_TriggersContent = new GUIContent(
            "Ability Triggers",
            "Bounded automatic activation conditions: GameplayEvent, OwnedTagAdded, or OwnedTagRemoved.");
        private static readonly GUIContent s_AbilityTagsContent = new GUIContent(
            "Ability Tags",
            "Tags that identify this ability and participate in cancel or block matching.");
        private static readonly GUIContent s_ActivationOwnedTagsContent = new GUIContent(
            "Activation Owned Tags",
            "Tags granted to the owner while this ability is active.");
        private static readonly GUIContent s_ActivationRequiredContent = new GUIContent(
            "Activation Required",
            "The owner must have all of these tags before activation.");
        private static readonly GUIContent s_ActivationBlockedContent = new GUIContent(
            "Activation Blocked",
            "The owner must have none of these tags before activation.");
        private static readonly GUIContent s_CancelAbilitiesContent = new GUIContent(
            "Cancel Abilities With Tag",
            "Activation cancels active abilities whose definition tags match any of these tags.");
        private static readonly GUIContent s_BlockAbilitiesContent = new GUIContent(
            "Block Abilities With Tag",
            "While active, this ability blocks definitions whose tags match any of these tags.");
        private static readonly GUIContent s_RequiredTagsContent = new GUIContent("Required Tags");
        private static readonly GUIContent s_BlockedTagsContent = new GUIContent("Blocked Tags");

        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<SerializedProperty> derivedProperties = new List<SerializedProperty>(8);
        private string derivedFieldsLabel;

        private void OnEnable()
        {
            CacheBasePropertyNames();
            CacheProperties();
            CacheDerivedProperties();
        }

        private static void CacheBasePropertyNames()
        {
            if (s_BasePropertiesInitialized) return;
            var baseType = typeof(Runtime.GameplayAbilitySO);
            foreach (var field in baseType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsUnitySerializedField(field))
                {
                    s_BasePropertyNames.Add(field.Name);
                }
            }
            s_BasePropertiesInitialized = true;
        }

        private static bool IsUnitySerializedField(FieldInfo field)
        {
            return !field.IsStatic
                && !field.IsNotSerialized
                && (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null);
        }

        private void CacheProperties()
        {
            abilityNameProp = serializedObject.FindProperty("AbilityName");
            instancingPolicyProp = serializedObject.FindProperty("InstancingPolicy");
            executionPolicyProp = serializedObject.FindProperty("ExecutionPolicy");
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

        private void CacheDerivedProperties()
        {
            derivedProperties.Clear();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script" || s_BasePropertyNames.Contains(iterator.name))
                {
                    continue;
                }

                derivedProperties.Add(iterator.Copy());
            }

            derivedFieldsLabel = target != null
                ? $"Custom Fields ({target.GetType().Name})"
                : "Custom Fields";
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
                EditorGUILayout.PropertyField(abilityNameProp, s_NameContent);
                EditorGUILayout.PropertyField(instancingPolicyProp, s_InstancingContent);
                EditorGUILayout.PropertyField(executionPolicyProp, s_ExecutionPolicyContent);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Cost & Cooldown ═══
            DrawSectionLine();
            showCostCooldown = EditorGUILayout.Foldout(showCostCooldown, "Cost & Cooldown", true);
            if (showCostCooldown)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(costEffectProp, s_CostContent);
                EditorGUILayout.PropertyField(cooldownEffectProp, s_CooldownContent);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Activation ═══
            DrawSectionLine();
            showActivation = EditorGUILayout.Foldout(showActivation, "Activation", true);
            if (showActivation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(activateOnGrantedProp, s_ActivateOnGrantedContent);
                EditorGUILayout.PropertyField(triggerDataProp, s_TriggersContent, true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Ability Tags ═══
            DrawSectionLine();
            showAbilityTags = EditorGUILayout.Foldout(showAbilityTags, "Ability Tags & Activation Requirements", true);
            if (showAbilityTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(abilityTagsProp, s_AbilityTagsContent);
                EditorGUILayout.PropertyField(activationOwnedTagsProp, s_ActivationOwnedTagsContent);
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(activationRequiredTagsProp, s_ActivationRequiredContent);
                EditorGUILayout.PropertyField(activationBlockedTagsProp, s_ActivationBlockedContent);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══ Interaction Tags ═══
            DrawSectionLine();
            showInteractionTags = EditorGUILayout.Foldout(showInteractionTags, "Interaction Tags (Cancel/Block)", true);
            if (showInteractionTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(cancelWithTagProp, s_CancelAbilitiesContent);
                EditorGUILayout.PropertyField(blockWithTagProp, s_BlockAbilitiesContent);
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
                EditorGUILayout.PropertyField(srcRequiredProp, s_RequiredTagsContent);
                EditorGUILayout.PropertyField(srcBlockedProp, s_BlockedTagsContent);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(tgtRequiredProp, s_RequiredTagsContent);
                EditorGUILayout.PropertyField(tgtBlockedProp, s_BlockedTagsContent);
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

            if (serializedObject.isEditingMultipleObjects)
            {
                sb.Append(targets.Length).Append(" abilities selected");
                EditorGUILayout.LabelField(sb.ToString(), s_SummaryStyle);
                return;
            }

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

            var executionPolicy = (Runtime.EAbilityExecutionPolicy)executionPolicyProp.enumValueIndex;
            string executionLabel = executionPolicy switch
            {
                Runtime.EAbilityExecutionPolicy.Invalid => "Invalid",
                Runtime.EAbilityExecutionPolicy.LocalOnly => "LocalOnly",
                Runtime.EAbilityExecutionPolicy.AuthorityOnly => "AuthorityOnly",
                Runtime.EAbilityExecutionPolicy.LocalPredicted => "LocalPredicted",
                _ => "?"
            };
            sb.Append("  \u2022  ").Append(executionLabel);

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

            if (!abilityNameProp.hasMultipleDifferentValues && string.IsNullOrEmpty(abilityNameProp.stringValue))
            {
                EditorGUILayout.HelpBox("Ability Name is empty. This makes debugging difficult.", MessageType.Warning);
                hasWarnings = true;
            }

            // Passive with cooldown is unusual
            if (!activateOnGrantedProp.hasMultipleDifferentValues &&
                !cooldownEffectProp.hasMultipleDifferentValues &&
                activateOnGrantedProp.boolValue &&
                cooldownEffectProp.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("'Activate On Granted' is enabled along with a Cooldown. Passive abilities typically don't use cooldowns.", MessageType.Info);
                hasWarnings = true;
            }

            // Runtime definitions require an explicit instance owner.
            if (!instancingPolicyProp.hasMultipleDifferentValues &&
                instancingPolicyProp.enumValueIndex == (int)Runtime.EGameplayAbilityInstancingPolicy.NonInstanced)
            {
                EditorGUILayout.HelpBox(
                    "Unity Runtime does not grant NonInstanced abilities because shared definitions cannot safely own ASC, task, or activation state. " +
                    "Choose InstancedPerActor or InstancedPerExecution. Pure Core simulation may still use stateless NonInstanced definitions.",
                    MessageType.Error);
                hasWarnings = true;
            }

            if (!executionPolicyProp.hasMultipleDifferentValues &&
                executionPolicyProp.enumValueIndex == (int)Runtime.EAbilityExecutionPolicy.Invalid)
            {
                EditorGUILayout.HelpBox(
                    "Execution Policy is Invalid. Choose LocalOnly or AuthorityOnly before creating a runtime ability.",
                    MessageType.Error);
                hasWarnings = true;
            }

            if (hasWarnings) EditorGUILayout.Space(4);
        }

        #endregion

        #region Derived Fields

        private void DrawDerivedClassFields()
        {
            if (derivedProperties.Count > 0)
            {
                EditorGUILayout.Space(8);
                DrawSectionLine();
                showDerivedFields = EditorGUILayout.Foldout(showDerivedFields, derivedFieldsLabel, true);

                if (showDerivedFields)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < derivedProperties.Count; i++)
                    {
                        EditorGUILayout.PropertyField(derivedProperties[i], true);
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
