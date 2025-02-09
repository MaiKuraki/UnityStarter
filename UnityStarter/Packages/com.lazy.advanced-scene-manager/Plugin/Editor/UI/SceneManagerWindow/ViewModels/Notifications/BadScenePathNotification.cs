using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class BadScenePathNotification : SceneNotification<Scene, BadPathScenePopup>
    {

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.scenesWithBadPath;

        public override string GetNotificationText(int count) =>
            $"You have {count} imported scenes that have been de-referenced, and are recoverable, click here to fix now...";

    }

}
