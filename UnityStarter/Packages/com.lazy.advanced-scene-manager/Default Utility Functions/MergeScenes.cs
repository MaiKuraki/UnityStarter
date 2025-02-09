#if UNITY_EDITOR

using System;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.UtilityFunctions.Components;

namespace AdvancedSceneManager.UtilityFunctions
{
    public class MergeScenes : ASMUtilityFunction
    {
        public override string Name => "Merge Scenes";
        public override string Description => "Merges 2 or more scenes together into one";
        public override string Group => "Generation";

        public override void OnInvoke(ref VisualElement optionsGUI)
        {
            VisualElement visualElement = new() { style = { flexGrow = 1, } };

            Label infoLabel = new("Make sure the scenes are imported, or they will not show up.");

            List<Scene> scenes = new();

            ListView listView = new()
            {
                itemsSource = scenes,
                fixedItemHeight = 20,
                selectionType = SelectionType.Multiple,
                showAddRemoveFooter = true,
                makeItem = () => new ObjectField { objectType = typeof(Scene) },
                showBorder = true,
                bindItem = (element, i) =>
                {
                    var objectField = (ObjectField)element;
                    scenes[i] = (Scene)objectField.value;
                },
                style =
            {
                flexGrow = 0,
                minHeight = 50
            },
                reorderable = true,
            };

            VisualElement addressContainer = PathComponent.PathPicker("Target folder:", "Scenes", "Pick Folder.", out var address);
            TextField targetName = new("New Name:");

            Toggle deleteScenes = new("Delete merged scenes");

            Label successLable = new()
            {
                style =
            {
                flexGrow = 0,
                alignSelf = Align.Center,
            }
            };

            Button mergeButton = new(() =>
            {
                listView.RefreshItems();
                MergeSelectedScenes(scenes);
            })
            { text = "Merge Selected Scenes" };



            void MergeSelectedScenes(List<Scene> scenesToMerge)
            {
                Scene[] scenes = scenesToMerge.Where(o => o).Distinct().ToArray();

                if (scenes.Length < 2)
                {
                    successLable.text = "Please select at least two unique scenes to merge.";
                    successLable.style.color = Color.red;
                    return;
                }

                Scene _base = SceneUtility.CreateAndImport($"{address.text}/{targetName.value}");

                if (deleteScenes.value)
                    _base.MergeScenes(scenes);
                else
                    _base.MergeScenesPreserveOriginal(scenes);

                successLable.text = $"Merged {scenesToMerge.Count} scenes to {targetName.value}.";
                successLable.style.color = Color.gray;

                listView.itemsSource.Clear();
                listView.RefreshItems();
            }

            // Bind elements
            visualElement.Add(infoLabel);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing
            visualElement.Add(listView);
            visualElement.Add(SpaceComponent.Create(30));  // Add spacing
            visualElement.Add(addressContainer);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing
            visualElement.Add(targetName);  // Add spacing
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing
            visualElement.Add(deleteScenes);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing
            visualElement.Add(mergeButton);
            visualElement.Add(SpaceComponent.Create(10));  // Add spacing
            visualElement.Add(successLable);

            optionsGUI = visualElement;

        }
    }
}
#endif