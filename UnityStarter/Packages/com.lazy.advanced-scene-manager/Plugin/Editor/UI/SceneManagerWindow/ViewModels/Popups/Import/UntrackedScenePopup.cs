using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class UntrackedScenePopup : ImportPopup<Scene, UntrackedScenePopup>
    {

        public override string headerText => "Untracked imported scenes:";
        public override string button1Text => "Remove";
        public override string button2Text => "Track";
        public override bool displayAutoImportField => false;

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.untrackedScenes;

        public override void SetupItem(VisualElement element, CheckableItem<Scene> item, int index, out string text)
        {
            text = $"{item.value.name} ({item.value.id})";
        }

        public override void OnButton1Click(IEnumerable<Scene> items)
        {

            //Remove
            var l = new List<string>();
            AssetDatabase.DeleteAssets(items.Select(s => s.asmPath).ToArray(), l);
            if (l.Count > 0)
                Debug.LogError("Could not delete the following assets:\n" + string.Join("\n", l));

        }

        public override void OnButton2Click(IEnumerable<Scene> items)
        {

            //Track
            Assets.Add(items);

        }

    }

}
