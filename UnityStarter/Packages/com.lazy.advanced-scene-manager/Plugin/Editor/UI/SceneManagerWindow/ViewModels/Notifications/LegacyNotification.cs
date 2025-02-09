using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Legacy;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class LegacyNotification : ViewModel, IPersistentNotification
    {

        public void ReloadNotification()
        {

            notifications.ClearNotification(nameof(LegacyNotification));

            if (!LegacyUtility.FindAssets() && !notifications.forceAllNotificationsVisible)
                return;

            notifications.Notify(
               message: "Welcome to the new and improved Advanced Scene Manager.\n\nUnfortunately, since ASM was rebuilt in 2.0, the ASM assets generated from before are not compatible, and cannot be upgraded. You may remove these.\n\nClick here for more information.",
               id: nameof(LegacyNotification),
               onClick: () => OpenPopup<LegacyPopup>(),
               dismissOnClick: false,
               canDismiss: false,
               priority: true,
               kind: NotificationKind.Info);

        }

    }

}
