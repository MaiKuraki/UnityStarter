using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class InvalidScenePopup : ImportPopup<Scene, InvalidScenePopup>
    {

        public override string headerText => "Invalid scenes:";
        public override string button1Text => "Unimport";
        public override bool displayAutoImportField => false;

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.invalidScenes;

        public override void SetupItem(VisualElement element, CheckableItem<Scene> item, int index, out string text)
        {
            text = $"{item.value.name} ({item.value.id})";
            element.ContextMenu((e) => e.menu.AppendAction("View Scene...", e => EditorGUIUtility.PingObject(item.value)));
        }

        public override void OnButton1Click(IEnumerable<Scene> items) =>
            Assets.Remove(items);

    }

}
