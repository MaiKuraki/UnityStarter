using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Views.Popups;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class WelcomeNotification : ViewModel, IPersistentNotification
    {

        public void ReloadNotification()
        {

            notifications.ClearNotification(nameof(WelcomeNotification));

            if (SceneManager.settings.user.m_hideDocsNotification && !notifications.forceAllNotificationsVisible)
                return;

            notifications.Notify(
               message: "Welcome to Advanced Scene Manager, it is strongly recommended to check out quick start guides before getting started. Click here to do so.",
               id: nameof(WelcomeNotification),
               onClick: () => OpenPopup<MenuPopup>(MenuPopup.flashDocsSection),
               onDismiss: () =>
               {
                   SceneManager.settings.user.m_hideDocsNotification = true;
                   SceneManager.settings.user.Save();
               },
               dismissOnClick: false,
               kind: NotificationKind.Link,
               iconInfo: "ASMMenu:Docs");

        }

    }

}
