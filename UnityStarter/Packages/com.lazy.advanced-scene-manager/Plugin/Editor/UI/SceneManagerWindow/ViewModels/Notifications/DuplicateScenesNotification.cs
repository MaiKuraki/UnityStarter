using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class DuplicateScenesNotification : SceneNotification<Scene, DuplicateScenePopup>
    {

        public override IEnumerable<Scene> GetItems() =>
            SceneImportUtility.duplicateScenes.SelectMany(s => s);

        public override string GetNotificationText(int count)
        {
            count = SceneImportUtility.duplicateScenes.Select(s => s.Key).Distinct().Count();
            return $"You have {count} duplicated scenes, click here to fix now...";
        }

    }

}
