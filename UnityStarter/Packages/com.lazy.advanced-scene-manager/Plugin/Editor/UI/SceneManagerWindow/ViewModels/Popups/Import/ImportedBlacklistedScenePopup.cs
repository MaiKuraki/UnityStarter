using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ImportedBlacklistedScenePopup : ImportPopup<Scene, ImportedBlacklistedScenePopup>
    {

        public override string headerText => "Blacklisted scenes:";
        public override string button1Text => "Unimport";
        public override bool displayAutoImportField => false;

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.importedBlacklistedScenes;

        public override void SetupItem(VisualElement element, CheckableItem<Scene> item, int index, out string text) =>
            text = $"{item.value.name} ({item.value.id})";

        public override void OnButton1Click(IEnumerable<Scene> items) =>
            Assets.Remove(items);

    }

}
