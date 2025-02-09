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

    class DuplicateScenePopup : ImportPopup<Scene, DuplicateScenePopup>
    {

        public override string headerText => "Duplicate scenes";
        public override bool displayAutoImportField => false;
        public override string button1Text => "Remove";

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.duplicateScenes.SelectMany(s => s);

        public override void OnButton1Click(IEnumerable<Scene> items)
        {
            foreach (var scene in items)
                Assets.Remove(scene);
        }

        public override void SetupItem(VisualElement element, CheckableItem<Scene> item, int index, out string text)
        {

            text = $"{item.value.name}: {item.value.path}";

            element.ContextMenu((e) =>
            {
                e.menu.AppendAction("View Scene...", e => EditorGUIUtility.PingObject(item.value));
                e.menu.AppendAction("View SceneAsset...", e => EditorGUIUtility.PingObject(item.value.sceneAsset));
            });

        }

    }

}
