using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Data;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private void CreateStepList()
        {
            stepList = new ReorderableList(
                serializedObject,
                recipeInvocations,
                draggable: false,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(
                    rect,
                    "Invocations"),
                drawElementCallback = DrawStepElement,
                elementHeightCallback = GetStepElementHeight,
                onAddDropdownCallback = ShowAddStepMenu,
                onRemoveCallback = RemoveStep
            };
        }

        private float GetStepElementHeight(int index)
        {
            if (index < 0 || index >= recipeInvocations.arraySize)
            {
                return EditorGUIUtility.singleLineHeight + 6f;
            }

            SerializedProperty element = recipeInvocations.GetArrayElementAtIndex(index);
            SerializedProperty dependencies = element.FindPropertyRelative("dependencies");
            int dependencyCount = dependencies?.arraySize ?? 0;
            int lineCount = element.isExpanded ? 6 + dependencyCount : 1;
            return EditorGUIUtility.singleLineHeight * lineCount + 14f;
        }

        private void DrawStepElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = recipeInvocations.GetArrayElementAtIndex(index);
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty invocationId = element.FindPropertyRelative("invocationId");
            SerializedProperty stepTypeId = element.FindPropertyRelative("stepTypeId");
            SerializedProperty configuration = element.FindPropertyRelative("configuration");
            SerializedProperty incrementality = element.FindPropertyRelative("incrementality");
            SerializedProperty dependencies = element.FindPropertyRelative("dependencies");
            string currentTypeId = stepTypeId.stringValue?.Trim() ?? string.Empty;

            int selectedIndex = -1;
            for (int descriptorIndex = 0; descriptorIndex < stepDescriptors.Count; descriptorIndex++)
            {
                BuildStepDescriptor candidate = stepDescriptors[descriptorIndex];
                if (string.Equals(currentTypeId, candidate.StepTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = descriptorIndex;
                }
            }

            GUIContent[] choices = stepChoiceLabels;
            string[] ids = stepChoiceIds;
            if (selectedIndex < 0)
            {
                choices = new GUIContent[stepChoiceLabels.Length + 1];
                ids = new string[stepChoiceIds.Length + 1];
                Array.Copy(stepChoiceLabels, choices, stepChoiceLabels.Length);
                Array.Copy(stepChoiceIds, ids, stepChoiceIds.Length);
                choices[choices.Length - 1] = new GUIContent($"Missing Step Type [{currentTypeId}]");
                ids[ids.Length - 1] = currentTypeId;
                selectedIndex = ids.Length - 1;
            }

            BuildStepDescriptor descriptor = FindStepDescriptor(currentTypeId);
            string displayName = descriptor == null
                ? $"Missing Step [{currentTypeId}]"
                : descriptor.DisplayName;
            string detailedSummary =
                $"{displayName}  [{invocationId.stringValue}]  ·  " +
                $"{(BuildIncrementality)incrementality.enumValueIndex}  ·  " +
                $"{dependencies.arraySize} dep{(dependencies.arraySize == 1 ? string.Empty : "s")}  ·  " +
                (descriptor?.ConfigurationType == null
                    ? "No config"
                    : configuration.objectReferenceValue == null
                        ? "Config required"
                        : "Config assigned");
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 2f;
            enabled.boolValue = EditorGUI.Toggle(
                new Rect(rect.x, y, 20f, lineHeight),
                new GUIContent(string.Empty, "Include this invocation in the saved recipe."),
                enabled.boolValue);
            Rect foldoutRect = new Rect(
                rect.x + 22f,
                y,
                Mathf.Max(1f, rect.width - 22f),
                lineHeight);
            string identitySummary = foldoutRect.width < 250f
                ? displayName
                : $"{displayName}  [{invocationId.stringValue}]";
            string collapsedSummary = foldoutRect.width < 500f
                ? identitySummary
                : detailedSummary;
            bool missingConfiguration = descriptor?.ConfigurationRequired == true
                && configuration.objectReferenceValue == null;
            BuildInspectorStatus invocationStatus = descriptor == null
                ? new BuildInspectorStatus(BuildInspectorTone.Error, "MISSING")
                : missingConfiguration
                    ? new BuildInspectorStatus(BuildInspectorTone.Warning, "CONFIG")
                    : enabled.boolValue
                        ? new BuildInspectorStatus(BuildInspectorTone.Ready, "INCLUDED")
                        : new BuildInspectorStatus(BuildInspectorTone.Disabled, "RETAINED");
            element.isExpanded = BuildInspectorUi.DrawInlineFoldout(
                foldoutRect,
                element.isExpanded,
                new GUIContent(
                    element.isExpanded
                        ? identitySummary
                        : collapsedSummary,
                    detailedSummary +
                    "\nExpand to edit routing identity, implementation type, configuration, policy, and dependency edges."),
                invocationStatus);

            if (!element.isExpanded)
            {
                return;
            }

            y += lineHeight + 2f;
            const float expandedContentIndent = 24f;
            const float controlGap = 4f;
            float expandedContentWidth = Mathf.Max(1f, rect.width - expandedContentIndent);
            float accessoryWidth = Mathf.Clamp(
                expandedContentWidth * 0.28f,
                44f,
                BuildInspectorUi.AccessoryButtonWidth);
            float configurationWidth = Mathf.Max(
                1f,
                expandedContentWidth - accessoryWidth - controlGap);
            Rect configurationRect = new Rect(
                rect.x + expandedContentIndent,
                y,
                configurationWidth,
                lineHeight);
            Rect createRect = new Rect(
                configurationRect.xMax + controlGap,
                configurationRect.y,
                accessoryWidth,
                configurationRect.height);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(
                previousLabelWidth,
                Mathf.Max(54f, configurationRect.width * 0.36f));
            try
            {
                if (descriptor?.ConfigurationType != null)
                {
                    configuration.objectReferenceValue = EditorGUI.ObjectField(
                        configurationRect,
                        new GUIContent("Config", descriptor.ConfigurationType.Name),
                        configuration.objectReferenceValue,
                        descriptor.ConfigurationType,
                        allowSceneObjects: false);
                    if (GUI.Button(createRect, "Create"))
                    {
                        ShowCreateStepConfigurationMenu(createRect, index, descriptor);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.ObjectField(
                            configurationRect,
                            new GUIContent("Config"),
                            configuration.objectReferenceValue,
                            typeof(ScriptableObject),
                            allowSceneObjects: false);
                    }

                    EditorGUI.LabelField(createRect, "None", EditorStyles.miniLabel);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }

            y += lineHeight + 2f;
            Rect policyRect = new Rect(
                rect.x + 24f,
                y,
                rect.width - 24f,
                lineHeight);
            EditorGUI.PropertyField(
                policyRect,
                incrementality,
                new GUIContent(
                    "Incrementality",
                    "Execution policy for this invocation only. This is not a release compatibility contract."));

            y += lineHeight + 2f;
            Rect invocationRect = new Rect(
                rect.x + 24f,
                y,
                rect.width - 24f,
                lineHeight);
            string editedInvocationId = EditorGUI.TextField(
                invocationRect,
                new GUIContent(
                    "Invocation ID",
                    "Stable identity used by dependencies, focused builds, CI overrides, logs, and manifests. Renaming updates dependency references atomically."),
                invocationId.stringValue);
            if (!string.Equals(
                    editedInvocationId,
                    invocationId.stringValue,
                    StringComparison.Ordinal))
            {
                TryRenameInvocation(index, invocationId, editedInvocationId);
            }

            y += lineHeight + 2f;
            Rect popupRect = new Rect(rect.x + 24f, y, rect.width - 24f, lineHeight);
            int newIndex = EditorGUI.Popup(
                popupRect,
                new GUIContent(
                    "Step Type",
                    "Registered implementation executed by this invocation."),
                selectedIndex,
                choices);
            if (newIndex >= 0 && newIndex < ids.Length && newIndex != selectedIndex)
            {
                string selectedId = ids[newIndex];
                BuildStepDescriptor selectedDescriptor = FindStepDescriptor(selectedId);
                if (selectedDescriptor?.Multiplicity == BuildStepMultiplicity.Single
                    && IsStepTypeConfiguredAtAnotherIndex(selectedId, index))
                {
                    EditorUtility.DisplayDialog(
                        "Single-Invocation Step",
                        $"The build step type '{selectedId}' allows only one invocation per recipe.",
                        "OK");
                }
                else
                {
                    string previousTypeId = stepTypeId.stringValue?.Trim();
                    stepTypeId.stringValue = selectedId;
                    InvalidateRecipeGraphSnapshot();
                    if (string.IsNullOrWhiteSpace(invocationId.stringValue)
                        || string.Equals(
                            invocationId.stringValue.Trim(),
                            previousTypeId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        RenameInvocation(
                            index,
                            invocationId,
                            CreateUniqueInvocationId(selectedId, index));
                    }

                    if (configuration.objectReferenceValue != null
                        && (selectedDescriptor?.ConfigurationType == null
                            || !selectedDescriptor.ConfigurationType.IsInstanceOfType(
                                configuration.objectReferenceValue)))
                    {
                        configuration.objectReferenceValue = null;
                    }
                }
            }

            y += lineHeight + 2f;
            Rect dependencyHeaderRect = new Rect(
                rect.x + 24f,
                y,
                rect.width - 24f,
                lineHeight);
            EditorGUI.LabelField(
                dependencyHeaderRect,
                "Dependencies",
                EditorStyles.miniBoldLabel);
            Rect addDependencyRect = new Rect(
                dependencyHeaderRect.xMax - 22f,
                dependencyHeaderRect.y,
                22f,
                lineHeight);
            using (new EditorGUI.DisabledScope(!HasAvailableDependency(index)))
            {
                if (GUI.Button(addDependencyRect, "+", EditorStyles.miniButton))
                {
                    AddDependency(index, dependencies);
                }
            }

            for (int dependencyIndex = 0;
                 dependencyIndex < dependencies.arraySize;
                 dependencyIndex++)
            {
                y += lineHeight;
                DrawDependency(
                    new Rect(rect.x + 24f, y, rect.width - 24f, lineHeight),
                    index,
                    dependencies,
                    dependencyIndex);
            }
        }

        private void ShowAddStepMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();
            int availableCount = 0;
            foreach (BuildStepDescriptor descriptor in stepDescriptors)
            {
                GUIContent label = new GUIContent($"{descriptor.Category}/{descriptor.DisplayName}");
                if (descriptor.Multiplicity == BuildStepMultiplicity.Single
                    && IsStepTypeConfiguredAtAnotherIndex(descriptor.StepTypeId, -1))
                {
                    menu.AddDisabledItem(label, on: true);
                    continue;
                }

                string id = descriptor.StepTypeId;
                menu.AddItem(label, on: false, () => AddStep(id));
                availableCount++;
            }

            if (availableCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("All registered steps are already configured"));
            }

            menu.DropDown(buttonRect);
        }

        private void AddStep(string id)
        {
            serializedObject.Update();
            int index = recipeInvocations.arraySize;
            recipeInvocations.InsertArrayElementAtIndex(index);
            SerializedProperty entry = recipeInvocations.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("enabled").boolValue = true;
            entry.FindPropertyRelative("invocationId").stringValue =
                CreateUniqueInvocationId(id, index);
            entry.FindPropertyRelative("stepTypeId").stringValue = id;
            entry.FindPropertyRelative("configuration").objectReferenceValue = null;
            entry.FindPropertyRelative("incrementality").enumValueIndex =
                (int)BuildIncrementality.Clean;
            entry.FindPropertyRelative("dependencies").ClearArray();
            serializedObject.ApplyModifiedProperties();
            InvalidateRecipeGraphSnapshot();
        }

        private void RemoveStep(ReorderableList list)
        {
            int index = list.index;
            if (index < 0 || index >= recipeInvocations.arraySize)
            {
                return;
            }

            string invocationId = recipeInvocations.GetArrayElementAtIndex(index)
                .FindPropertyRelative("invocationId")
                .stringValue?.Trim() ?? string.Empty;
            int referenceCount = CountDependencyReferences(invocationId, index);
            if (referenceCount > 0
                && !EditorUtility.DisplayDialog(
                    "Remove Build Invocation",
                    $"Invocation '{invocationId}' is referenced by {referenceCount} dependency edge(s). " +
                    "Removing it will also remove those edges.",
                    "Remove Invocation and Dependencies",
                    "Cancel"))
            {
                return;
            }

            RemoveDependencyReferences(invocationId, index);
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            InvalidateRecipeGraphSnapshot();
        }

    }
}
