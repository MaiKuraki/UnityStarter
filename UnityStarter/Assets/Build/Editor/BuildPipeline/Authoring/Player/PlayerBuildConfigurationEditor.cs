using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(PlayerBuildConfiguration))]
    public sealed class PlayerBuildConfigurationEditor : UnityEditor.Editor
    {
        private SerializedProperty extensions;
        private ReorderableList extensionList;
        private IReadOnlyList<PlayerBuildExtensionDescriptor> descriptors =
            Array.Empty<PlayerBuildExtensionDescriptor>();
        private string catalogError;

        private void OnEnable()
        {
            extensions = serializedObject.FindProperty("extensions");
            extensionList = new ReorderableList(
                serializedObject,
                extensions,
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Ordered Player Extensions"),
                drawElementCallback = DrawElement,
                onAddDropdownCallback = ShowAddMenu
            };

            try
            {
                descriptors = PlayerBuildExtensionRegistry.GetDescriptors();
                catalogError = null;
            }
            catch (Exception exception)
            {
                descriptors = Array.Empty<PlayerBuildExtensionDescriptor>();
                catalogError = exception.Message;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField(
                "Player Build Configuration",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Extensions run in this order around BuildPlayer. Keep the Player invocation configuration empty when no extension is required.",
                MessageType.None);

            if (!string.IsNullOrWhiteSpace(catalogError))
            {
                EditorGUILayout.HelpBox(catalogError, MessageType.Error);
            }

            extensionList.DoLayoutList();
            DrawValidation();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            SerializedProperty entry = extensions.GetArrayElementAtIndex(index);
            entry.objectReferenceValue = EditorGUI.ObjectField(
                rect,
                entry.objectReferenceValue,
                typeof(PlayerBuildExtensionConfiguration),
                allowSceneObjects: false);
        }

        private void ShowAddMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();
            bool hasEntries = false;
            for (int index = 0; index < descriptors.Count; index++)
            {
                PlayerBuildExtensionDescriptor descriptor = descriptors[index];
                GUIContent label = new GUIContent(
                    descriptor.DisplayName,
                    descriptor.Description);
                if (!descriptor.IsAvailable)
                {
                    menu.AddDisabledItem(new GUIContent(
                        descriptor.DisplayName + " (Dependency Unavailable)"));
                }
                else
                {
                    PlayerBuildExtensionDescriptor captured = descriptor;
                    menu.AddItem(label, false, () => CreateAndAssign(captured));
                }

                hasEntries = true;
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(
                new GUIContent("Reference Existing Asset"),
                false,
                AddEmptySlot);
            if (!hasEntries && !string.IsNullOrWhiteSpace(catalogError))
            {
                menu.AddDisabledItem(new GUIContent("Provider Catalog Invalid"));
            }

            menu.DropDown(buttonRect);
        }

        private void AddEmptySlot()
        {
            serializedObject.Update();
            int index = extensions.arraySize;
            extensions.InsertArrayElementAtIndex(index);
            extensions.GetArrayElementAtIndex(index).objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
        }

        private void CreateAndAssign(PlayerBuildExtensionDescriptor descriptor)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Player Extension Configuration",
                descriptor.DisplayName + " Player Extension",
                "asset",
                "Choose a version-controlled location for the Player extension configuration.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var asset = (PlayerBuildExtensionConfiguration)
                CreateInstance(descriptor.ConfigurationType);
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            Undo.RegisterCreatedObjectUndo(asset, "Create Player Extension Configuration");
            AssetDatabase.SaveAssetIfDirty(asset);

            serializedObject.Update();
            int index = extensions.arraySize;
            extensions.InsertArrayElementAtIndex(index);
            extensions.GetArrayElementAtIndex(index).objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(asset);
        }

        private void DrawValidation()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < extensions.arraySize; index++)
            {
                var configuration = extensions.GetArrayElementAtIndex(index)
                    .objectReferenceValue as PlayerBuildExtensionConfiguration;
                if (configuration == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Extension entry {index} is empty.",
                        MessageType.Error);
                    continue;
                }

                string providerId = configuration.ProviderId?.Trim();
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    EditorGUILayout.HelpBox(
                        $"Extension entry {index} returned an empty provider id.",
                        MessageType.Error);
                }
                else if (!seen.Add(providerId))
                {
                    EditorGUILayout.HelpBox(
                        $"Player extension provider '{providerId}' is configured more than once.",
                        MessageType.Error);
                }

                PlayerBuildExtensionDescriptor descriptor = FindDescriptor(providerId);
                if (descriptor == null)
                {
                    EditorGUILayout.HelpBox(
                        $"No authoring registration is available for Player extension provider '{providerId}'.",
                        MessageType.Error);
                }
                else if (!descriptor.ConfigurationType.IsInstanceOfType(configuration))
                {
                    EditorGUILayout.HelpBox(
                        $"Player extension provider '{providerId}' requires '{descriptor.ConfigurationType.Name}', " +
                        $"but entry {index} uses '{configuration.GetType().Name}'.",
                        MessageType.Error);
                }
                else if (!descriptor.IsAvailable)
                {
                    EditorGUILayout.HelpBox(
                        $"Player extension '{descriptor.DisplayName}' is unavailable because its adapter or package dependency is missing.",
                        MessageType.Error);
                }
            }
        }

        private PlayerBuildExtensionDescriptor FindDescriptor(string providerId)
        {
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (string.Equals(
                        descriptors[index].ProviderId,
                        providerId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return descriptors[index];
                }
            }

            return null;
        }
    }
}
