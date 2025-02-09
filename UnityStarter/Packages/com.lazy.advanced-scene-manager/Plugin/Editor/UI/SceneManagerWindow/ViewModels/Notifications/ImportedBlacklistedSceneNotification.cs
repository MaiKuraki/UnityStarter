using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class ImportedBlacklistedSceneNotification : SceneNotification<Scene, ImportedBlacklistedScenePopup>
    {

        public override string GetNotificationText(int count) =>
            $"You have {count} imported scenes that are blacklisted, click here to fix now...";

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.importedBlacklistedScenes;

    }

}
