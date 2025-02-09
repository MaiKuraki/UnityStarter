using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class ImportSceneNotification : SceneNotification<string, ImportScenePopup>
    {

        public override NotificationKind Kind => NotificationKind.Info;
        public override string Icon => notifications.Icons[NotificationKind.Scene].fontAwesomeIcon;
        public override string IconFont => notifications.Icons[NotificationKind.Scene].font;
        public override string IconInfo => "Scene import";

        public override IEnumerable<string> GetItems() =>
            SceneImportUtility.unimportedScenes.Except(SceneImportUtility.dynamicScenes);

        public override string GetNotificationText(int count) =>
            $"You have {count} scenes ready to be imported, click here to import them now...";

    }

}
