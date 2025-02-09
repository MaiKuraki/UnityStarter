using System.Collections.Generic;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class InvalidSceneNotification : SceneNotification<Scene, InvalidScenePopup>
    {

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.invalidScenes;

        public override string GetNotificationText(int count) =>
            $"You have {count} imported scenes that are invalid, they have no associated SceneAsset, click here to unimport now...";

    }

}
