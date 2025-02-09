using AdvancedSceneManager.Editor.UI.Interfaces;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class GitIgnoreNotification : ViewModel, IPersistentNotification
    {

        bool showFull;

        public void ReloadNotification()
        {
            notifications.ClearNotification(nameof(GitIgnoreNotification));

            if (SceneManager.settings.user.m_hideGitIgnoreNotification && !notifications.forceAllNotificationsVisible)
                return;

            if (!showFull)
            {
                notifications.Notify(
                    message: "<b>Quick reminder for public repositories</b> <i>Click for more info.</i>",
                    id: nameof(GitIgnoreNotification),
                    onClick: Toggle,
                    canDismiss: false,
                    dismissOnClick: false,
                    priority: true,
                    kind: NotificationKind.Info,
                    isCollapsed: false);
            }
            else
            {
                notifications.Notify(
                    message:
                    "<b>Quick reminder for public repositories</b>\n\n" +
                    "Just a heads-up to help you out! Advanced Scene Manager (ASM) is a paid asset, like many others on the Unity Asset Store, and shouldn’t be included in public repositories (such as on GitHub or similar platforms) to comply with Unity’s Asset Store End User License Agreement (EULA).\n\n" +
                    "To make it easy, just add the following to your .gitignore file:\n" +
                    "<b>**/Packages/com.lazy.advanced-scene-manager/</b>\n\n" +
                    "This small step helps keep your project in line with licensing guidelines. Thanks for your attention!\n\n" +
                    "<i>(you may dismiss notification in the upper right corner once notification expanded).</i>",
                    id: nameof(GitIgnoreNotification),
                    onClick: Toggle,
                    canDismiss: true,
                    dismissOnClick: false,
                    priority: true,
                    onDismiss: () =>
                    {
                        SceneManager.settings.user.m_hideGitIgnoreNotification = true;
                        SceneManager.settings.user.Save();
                    },
                    kind: NotificationKind.Info,
                    isCollapsed: true);
            }

        }

        void Toggle()
        {
            showFull = !showFull;
            ReloadNotification();
        }

    }

}
