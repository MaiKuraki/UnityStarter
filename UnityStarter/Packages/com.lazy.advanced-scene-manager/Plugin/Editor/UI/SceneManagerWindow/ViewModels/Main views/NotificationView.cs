using System;
using System.Collections.Generic;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Layouts;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Legacy;
using AdvancedSceneManager.Models;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{
    public enum NotificationKind
    {
        Default,
        Info,
        Warning,
        Scene,
        Link,
    }
}

namespace AdvancedSceneManager.Editor.UI.Views
{

    class NotificationView : VerticalLayout<INotification>
    {
        public readonly Dictionary<NotificationKind, (string fontAwesomeIcon, string font)> Icons = new Dictionary<NotificationKind, (string fontAwesomeIcon, string font)>
        {
            { NotificationKind.Default, ("", "") },
            { NotificationKind.Info, ("", "") },
            { NotificationKind.Warning, ("🔧","") },
            { NotificationKind.Scene, ("", "fontAwesomeBrands") },
            { NotificationKind.Link, ("", "") },
        };

        public bool forceAllNotificationsVisible
        {
            get => Get(false);
            set { Set(value); ReloadPersistentNotifications(); }
        }

        public override void OnAdded()
        {
            SceneImportUtility.scenesChanged += ReloadPersistentNotifications;
            Profile.onProfileChanged += ReloadPersistentNotifications;
        }

        public override void OnRemoved()
        {
            SceneImportUtility.scenesChanged -= ReloadPersistentNotifications;
            Profile.onProfileChanged -= ReloadPersistentNotifications;
        }

        public override void OnWindowFocus() =>
            ReloadPersistentNotifications();

        #region Persistent notifications

        IEnumerable<IPersistentNotification> persistentNotifications => DependencyInjectionUtility.GetServices<IPersistentNotification>();

        public void ReloadPersistentNotifications()
        {
            foreach (var notification in persistentNotifications)
                notification.ReloadNotification();
        }

        #endregion

        readonly new Dictionary<string, VisualElement> notifications = new();

        public void Notify(string message, string id, Action onClick, Action onDismiss = null, bool canDismiss = true, bool dismissOnClick = true, bool priority = false, NotificationKind kind = NotificationKind.Default, bool? isCollapsed = null, string iconInfo = "", string fontAwesomeIcon = null, string iconFont = null)
        {

            ClearNotification(id);

            if (!priority && LegacyUtility.FindAssets())
                return;

            Button element = null;
            element = new Button(() => Dismiss(onClick, dismissOnClick));

            element.Add(new Label(message) { name = "text" });
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            spacer.pickingMode = PickingMode.Ignore;
            element.Add(spacer);

            var buttonContainer = new VisualElement() { name = "button-container" };

            element.AddToClassList("notification");
            element.AddToClassList(kind.ToString().ToLower());

            if (isCollapsed.HasValue)
            {
                var expander = new Label(isCollapsed.Value ? "▲" : "▼");
                expander.tooltip = isCollapsed.Value ? "Collapse" : "Expand";
                buttonContainer.Add(expander);
            }

            if (string.IsNullOrEmpty(fontAwesomeIcon))
                fontAwesomeIcon = Icons[kind].fontAwesomeIcon;

            if (string.IsNullOrEmpty(iconFont))
                iconFont = Icons[kind].font;

            var iconLabel = new Label(fontAwesomeIcon);
            iconLabel.AddToClassList("fontAwesome");
            if (!string.IsNullOrEmpty(iconFont))
                iconLabel.AddToClassList(iconFont);

            if (string.IsNullOrEmpty(iconInfo))
            {
                if (kind == NotificationKind.Scene)
                    iconInfo = "Scene import";
                else
                    iconInfo = kind.ToString();
            }

            if (!string.IsNullOrEmpty(iconInfo))
                iconLabel.tooltip = iconInfo;

            buttonContainer.Add(iconLabel);

            if (canDismiss)
            {
                var button = new Button(() => Dismiss(onDismiss, true)) { text = "", tooltip = "Dismiss" };
                button.AddToClassList("fontAwesome");
                buttonContainer.Add(button);
            }

            element.Add(buttonContainer);

            Add(element);

            notifications.Add(id, element);

            void Dismiss(Action callback, bool canDismiss)
            {
                if (canDismiss)
                    element.RemoveFromHierarchy();
                callback?.Invoke();
            }

        }

        public void ClearNotification(string id)
        {
            if (notifications.Remove(id, out var element))
                element.RemoveFromHierarchy();
        }

    }

}
