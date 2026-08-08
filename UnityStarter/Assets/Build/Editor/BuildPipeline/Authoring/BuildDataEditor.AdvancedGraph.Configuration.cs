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
        private BuildStepDescriptor FindStepDescriptor(string stepTypeId)
        {
            if (string.IsNullOrWhiteSpace(stepTypeId))
            {
                return null;
            }

            return stepDescriptors.FirstOrDefault(descriptor => string.Equals(
                descriptor.StepTypeId,
                stepTypeId,
                StringComparison.OrdinalIgnoreCase));
        }

        private void ShowCreateStepConfigurationMenu(
            Rect buttonRect,
            int stepIndex,
            BuildStepDescriptor descriptor)
        {
            if (!descriptor.ConfigurationType.IsAbstract)
            {
                CreateStepConfiguration(
                    stepIndex,
                    descriptor.ConfigurationType,
                    descriptor.DisplayName);
                return;
            }

            var menu = new GenericMenu();
            int count = 0;
            foreach (AssetContentProviderDescriptor provider in providerDescriptors)
            {
                if (!descriptor.ConfigurationType.IsAssignableFrom(
                        provider.ConfigurationType))
                {
                    continue;
                }

                int capturedIndex = stepIndex;
                Type capturedType = provider.ConfigurationType;
                string capturedName = provider.DisplayName;
                menu.AddItem(
                    new GUIContent(provider.DisplayName),
                    on: false,
                    () => CreateStepConfiguration(
                        capturedIndex,
                        capturedType,
                        capturedName));
                count++;
            }

            foreach (HotUpdateProviderDescriptor provider in hotUpdateProviderDescriptors)
            {
                if (!descriptor.ConfigurationType.IsAssignableFrom(
                        provider.ConfigurationType))
                {
                    continue;
                }

                int capturedIndex = stepIndex;
                Type capturedType = provider.ConfigurationType;
                string capturedName = provider.DisplayName;
                menu.AddItem(
                    new GUIContent(provider.DisplayName),
                    on: false,
                    () => CreateStepConfiguration(
                        capturedIndex,
                        capturedType,
                        capturedName));
                count++;
            }

            if (count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No concrete configuration types are installed"));
            }

            menu.DropDown(buttonRect);
        }

        private void CreateStepConfiguration(
            int stepIndex,
            Type configurationType,
            string displayName)
        {
            string defaultName = displayName.Replace(" ", string.Empty) + "BuildConfig";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create " + displayName + " Configuration",
                defaultName,
                "asset",
                "Choose a version-controlled location for this step configuration.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (IsAssetCreationPathOccupied(path))
            {
                EditorUtility.DisplayDialog(
                    "Configuration Already Exists",
                    $"Refusing to replace the existing asset at '{path}'. Choose a new file name.",
                    "OK");
                return;
            }

            var instance = ScriptableObject.CreateInstance(configurationType);
            AssetDatabase.CreateAsset(instance, path);
            Undo.RegisterCreatedObjectUndo(instance, "Create Build Step Configuration");
            serializedObject.Update();
            recipeInvocations.GetArrayElementAtIndex(stepIndex)
                .FindPropertyRelative("configuration")
                .objectReferenceValue = instance;
            serializedObject.ApplyModifiedProperties();
            Selection.activeObject = instance;
            EditorGUIUtility.PingObject(instance);
        }
    }
}

