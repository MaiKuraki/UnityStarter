using System.IO;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Legacy;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class LegacyPopup : ViewModel, IPopup
    {

        public override void OnAdded()
        {

            var asset = LegacyUtility.FindAssets();

            var field = view.Q<ObjectField>();
            field.value = asset;

            field.Q(className: "unity-object-field__object").SetEnabled(false);
            field.Q(className: "unity-object-field__selector").SetVisible(false);
            field.RegisterCallback<MouseDownEvent>(e => EditorGUIUtility.PingObject(asset), TrickleDown.TrickleDown);

            view.Q<Button>("button-cancel").clicked += ClosePopup;
            view.Q<Button>("button-delete").clicked += () => DeleteAssets(asset);

        }

        void DeleteAssets(LegacyAssetRef asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var folder = AssetDatabaseUtility.MakeRelative(Directory.GetParent(path).FullName);
            if (AssetDatabase.DeleteAsset(folder))
                ClosePopup();
            else
                Debug.LogError($"An error occured when deleting '{folder}'.");
        }

    }

}
