using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.UtilityFunctions.Components
{
    public static class PathComponent
    {
        public static VisualElement PathPicker(string textFieldLabel, string textFieldDefault, string buttonLabel, out TextField address)
        {
            VisualElement addressContainer = new() { style = { flexDirection = FlexDirection.Row } };
            TextField _address = new(textFieldLabel) { style = { flexGrow = 1 }, value = "Assets/" + textFieldDefault };

            Button addressPicker = new(() =>
            {
                // Open folder picker starting from "Assets" folder
                string folderPath = EditorUtility.OpenFolderPanel("Select folder:", "Assets", "");

                if (!string.IsNullOrEmpty(folderPath) && folderPath.Contains(Application.dataPath))
                {
                    Uri fullPathUri = new(folderPath);
                    Uri assetPathUri = new(Application.dataPath);
                    string relativePath = assetPathUri.MakeRelativeUri(fullPathUri).ToString();

                    _address.value = relativePath;
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Selection", "Please select a folder within the Assets folder.", "OK");
                }
            })
            { text = buttonLabel };

            address = _address;

            addressContainer.Add(_address);
            addressContainer.Add(addressPicker);

            return addressContainer;
        }
    }
}