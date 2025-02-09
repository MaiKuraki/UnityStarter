using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.Utility;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class UpdateNotification : ViewModel, IPersistentNotification
    {

        public UpdateNotification()
        {
            UpdateUtility.updateChecked += UpdateUtility_updateChecked;
        }

        private void UpdateUtility_updateChecked()
        {
            ReloadNotification();
        }

        public void ReloadNotification()
        {

            notifications.ClearNotification(nameof(UpdateNotification));

            if (!notifications.forceAllNotificationsVisible)
                if (!UpdateUtility.isUpdateAvailable || UpdateUtility.hasNotifiedAboutVersion)
                    return;

            notifications.Notify(
               message: $"ASM {UpdateUtility.availableVersionStr} is available for download",
               id: nameof(UpdateNotification),
               onClick: () => OpenPopup<UpdatePopup>(),
               dismissOnClick: false,
               onDismiss: () => UpdateUtility.lastNotifyVersion = UpdateUtility.availableVersionStr,
               kind: NotificationKind.Info);
        }
    }

}
