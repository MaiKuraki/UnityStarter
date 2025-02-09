using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class UntrackedSceneNotification : SceneNotification<Scene, UntrackedScenePopup>
    {

        public override string GetNotificationText(int count) =>
            $"You have {count} imported scenes that are not tracked by ASM, click here to fix now...";

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.untrackedScenes;

    }

}
