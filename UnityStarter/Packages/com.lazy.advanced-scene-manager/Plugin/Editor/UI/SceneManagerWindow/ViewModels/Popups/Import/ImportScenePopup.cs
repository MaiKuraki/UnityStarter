using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ImportScenePopup : ImportPopup<string, ImportScenePopup>
    {

        public override string headerText => "Unimported scenes:";
        public override string button1Text => "Import";
        public override string subtitleText => "<i>* Right click a scene to access blacklist options</i>";
        public override bool displayAutoImportField => true;

        public override IEnumerable<string> GetItems() =>
            SceneImportUtility.unimportedScenes.Except(SceneImportUtility.dynamicScenes);

        public override void SetupItem(VisualElement element, CheckableItem<string> item, int index, out string text)
        {

            text = item.value;

            element.ContextMenu((e) =>
            {

                e.menu.AppendAction("View SceneAsset...", e => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(item.value)));
                e.menu.AppendSeparator();

                var segments = item.value.Split("/");
                var paths = segments.Select((s, i) => string.Join("\\", segments.Take(i)) + "\\").Skip(1);

                AddBlocklistMenu(e, "Blacklist", BlocklistUtility.blacklist, paths);
                AddBlocklistMenu(e, "Whitelist", BlocklistUtility.whitelist, paths);

            });

            void AddBlocklistMenu(ContextualMenuPopulateEvent e, string menuName, Blocklist blocklist, IEnumerable<string> paths)
            {

                foreach (var path in paths)
                    e.menu.AppendAction($"{menuName}/{path}", (e) => Add(path));

                e.menu.AppendAction($"{menuName}/{item.value.Replace("/", "\\")}", (e) => Add(item.value));

                void Add(string path)
                {
                    blocklist.Add(path);
                    Reload();
                }
            }

        }

        public override void OnButton1Click(IEnumerable<string> items) =>
            SceneImportUtility.Import(items);

    }

}
