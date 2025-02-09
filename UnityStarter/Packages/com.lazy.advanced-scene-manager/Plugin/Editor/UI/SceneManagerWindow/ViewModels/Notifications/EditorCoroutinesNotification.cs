using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEditor;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    class EditorCoroutinesNotification : ViewModel, IPersistentNotification
    {

        bool hasBeenDismissed
        {
            get => SessionState.GetBool(nameof(EditorCoroutinesNotification), false);
            set => SessionState.SetBool(nameof(EditorCoroutinesNotification), value);
        }

        public void Show()
        {
            hasBeenDismissed = false;
            ReloadNotification();
        }

        public void ReloadNotification()
        {

            ClearNotification();

            if (notifications.forceAllNotificationsVisible)
                DisplayNotification();

#if !COROUTINES
            if (!hasBeenDismissed)
                DisplayNotification();
#endif

        }

        const string notificationID = nameof(EditorCoroutinesNotification);
        void DisplayNotification()
        {
            ClearNotification();
            notifications.Notify(
                message: "Editor coroutines is not installed, this may cause some features to behave unexpectedly outside of play mode.",
                id: notificationID,
                onClick: Install,
                onDismiss: Dismiss,
                dismissOnClick: false,
                kind: NotificationKind.Link,
                iconInfo: "PackageManager:com.unity.editorcoroutines");
        }

        void ClearNotification() =>
            notifications.ClearNotification(notificationID);

        void Install()
        {
            try
            {
                UnityEditor.PackageManager.UI.Window.Open("com.unity.editorcoroutines");
                UnityEditor.PackageManager.UI.Window.Open("com.unity.editorcoroutines");
            }
            catch
            {
                //Internal null ref sometimes happen, no clue why
            }
        }

        void Dismiss() =>
            hasBeenDismissed = true;

    }

}
