using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class BadPathScenePopup : ImportPopup<Scene, BadPathScenePopup>
    {

        public override string headerText => "De-referenced scenes:";
        public override string button1Text => "Fix";
        public override bool displayAutoImportField => false;

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.scenesWithBadPath.ToArray();

        public override void SetupItem(VisualElement element, CheckableItem<Scene> item, int index, out string text)
        {

            text = $"{item.value.name} ({item.value.id})";

            element.ContextMenu((e) =>
            {

                e.menu.AppendAction("View SceneAsset...", e => EditorGUIUtility.PingObject(item.value.sceneAsset));
                e.menu.AppendAction("View Scene...", e => EditorGUIUtility.PingObject(item.value));
                e.menu.AppendSeparator();
                e.menu.AppendAction("Unimport...", e => Assets.Remove(item.value));

            });

        }

        public override void OnButton1Click(IEnumerable<Scene> items)
        {
            foreach (var scene in SceneImportUtility.scenesWithBadPath)
                Assets.SetSceneAssetPath(scene, AssetDatabase.GetAssetPath(scene.sceneAsset));
        }

    }

}
