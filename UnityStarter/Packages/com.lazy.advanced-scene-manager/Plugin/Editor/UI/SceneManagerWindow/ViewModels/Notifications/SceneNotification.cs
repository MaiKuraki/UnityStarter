using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Editor.UI.Notifications
{

    public class CheckableItem<T>
    {
        public T value;
        public bool isChecked;
    }

    abstract class SceneNotification<T, TPopup> : ViewModel, IPersistentNotification where TPopup : IPopup
    {

        public override void OnAdded() => SceneImportUtility.scenesChanged += ReloadNotification;
        public override void OnRemoved() => SceneImportUtility.scenesChanged -= ReloadNotification;

        public virtual NotificationKind Kind { get; protected set; } = NotificationKind.Scene;
        public virtual string Icon { get; protected set; } = null;
        public virtual string IconFont { get; protected set; } = null;
        public virtual string IconInfo { get; protected set; } = "Scene import";

        public abstract string GetNotificationText(int count);

        string notificationID => GetType().FullName;

        public CheckableItem<T>[] items { get; private set; }

        public abstract IEnumerable<T> GetItems();

        public void ReloadNotification()
        {

            notifications.ClearNotification(notificationID);

            if (!SceneManagerWindow.window && !notifications.forceAllNotificationsVisible)
                return;

            var items = GetItems();
            var count = items.Count();
            var hasItems = count > 0;

            Notify((hasItems && Profile.current) || notifications.forceAllNotificationsVisible, GetNotificationText(count));

            void Notify(bool show, string message)
            {

                if (show)
                    notifications.Notify(
                         message: message,
                         id: notificationID,
                         onClick: OpenPopup<TPopup>,
                         canDismiss: false,
                         dismissOnClick: false,
                         kind: Kind,
                         fontAwesomeIcon: Icon,
                         iconFont: IconFont,
                         iconInfo: IconInfo);

            }

        }

    }

}
