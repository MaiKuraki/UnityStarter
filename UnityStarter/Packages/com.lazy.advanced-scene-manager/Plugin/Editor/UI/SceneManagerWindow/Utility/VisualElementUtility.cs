using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEngine.UIElements.PopupWindow;

namespace AdvancedSceneManager.Editor.UI.Utility
{

    static class VisualElementUtility
    {

        #region Setters

        public static T SetChecked<T>(this T button, bool isChecked = true) where T : BaseBoolField
        {
            button.SetValueWithoutNotify(isChecked);
            return button;
        }

        public static void Hide(this VisualElement element) =>
            element.style.display = DisplayStyle.None;

        public static void Show(this VisualElement element) =>
            element.style.display = DisplayStyle.Flex;

        public static void SetVisible(this VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public static T Expand<T>(this T element) where T : VisualElement
        {
            element.style.flexGrow = 1;
            return element;
        }

        public static T NoExpand<T>(this T element) where T : VisualElement
        {
            element.style.flexGrow = 0;
            return element;
        }

        public static T Shrink<T>(this T element) where T : VisualElement
        {
            element.style.flexShrink = 1;
            return element;
        }

        public static T NoShrink<T>(this T element) where T : VisualElement
        {
            element.style.flexShrink = 0;
            return element;
        }

        public static T MinHeight<T>(this T element, float height) where T : VisualElement
        {
            element.style.minHeight = height;
            return element;
        }

        #endregion
        #region EventBase extensions

        public static bool IsShiftKeyHeld(this EventBase e)
        {
            if (e is PointerUpEvent pointer)
                return pointer.shiftKey;
            else if (e is MouseUpEvent mouse)
                return mouse.shiftKey;
            return false;
        }

        public static bool IsCtrlKeyHeld(this EventBase e)
        {
            if (e is PointerUpEvent pointer)
                return pointer.ctrlKey;
            else if (e is MouseUpEvent mouse)
                return mouse.ctrlKey;
            return false;
        }

        public static bool IsCommandKeyHeld(this EventBase e)
        {
            if (e is PointerUpEvent pointer)
                return pointer.commandKey;
            else if (e is MouseUpEvent mouse)
                return mouse.commandKey;
            return false;
        }

        #endregion
        #region RegisterCallbackOnce

#if !UNITY_6000_0_OR_NEWER
        public static void RegisterCallbackOnce<TEventType>(this VisualElement element, EventCallback<TEventType> callback) where TEventType : EventBase<TEventType>, new()
        {

            void Wrapper(TEventType e)
            {
                element.UnregisterCallback<TEventType>(Wrapper);
                callback(e);
            }

            element.RegisterCallback<TEventType>(Wrapper);

        }
#endif

        #endregion
        #region GetAncestor

        public static VisualElement GetAncestor(this VisualElement element, string name = null, string className = null) =>
            GetAncestor<VisualElement>(element, name, className);

        public static T GetAncestor<T>(this VisualElement element, string name = null, string className = null) where T : VisualElement
        {

            if (element == null)
                return null;

            if (element is T && ((string.IsNullOrEmpty(name) || element.name == name) || (string.IsNullOrEmpty(className) || element.ClassListContains(className))))
                return (T)element;

            return element.parent.GetAncestor<T>(className, name);

        }

        #endregion
        #region Animations

        #region Rotate animation

        public static IVisualElementScheduledItem Rotate(this VisualElement element, long tick = 10, int speed = 15) =>
            element.schedule.
            Execute(() => element.style.rotate = new Rotate(new(element.style.rotate.value.angle.value + speed))).
            Every(tick);

        #endregion
        #region Fade animation

        /// <summary>Fades the element.</summary>
        /// <remarks>Uses in-out easing.</remarks>
        public static IVisualElementScheduledItem Fade(this VisualElement view, float to, float duration = 0.25f, Action onComplete = null)
        {

            var initialOpacity = view.style.opacity.value;
            var elapsed = 0f;
            var interval = 0.01f; // Interval for the updates (10ms)

            return view.schedule.Execute(() =>
            {

                elapsed += interval;
                var t = Mathf.Clamp01(elapsed / duration); // Normalized time [0, 1]

                // Ease-in-out function
                var easedT = t * t * (3f - 2f * t);

                view.style.opacity = new StyleFloat(Mathf.Lerp(initialOpacity, to, easedT));

                if (elapsed >= duration)
                {
                    view.style.opacity = new StyleFloat(to); // Ensure final value is set
                    onComplete?.Invoke();
                }

            }).Every((long)(interval * 1000)).Until(() => elapsed >= duration);

        }

        #endregion
        #region Slide animation

        public static IVisualElementScheduledItem AnimateBottom(this VisualElement view, float from, float to, float duration = 0.25f, Action onComplete = null)
        {

            var elapsed = 0f;
            var interval = 0.01f; // Interval for the updates (10ms)

            return view.schedule.Execute(() =>
            {

                elapsed += interval;
                var t = Mathf.Clamp01(elapsed / duration); // Normalized time [0, 1]

                // Ease-in-out function
                var easedT = t * t * (3f - 2f * t);

                view.style.bottom = new StyleLength(Mathf.Lerp(from, to, easedT));

                if (elapsed >= duration)
                {
                    view.style.bottom = new StyleLength(to); // Ensure final value is set
                    onComplete?.Invoke();
                }

            }).Every((long)(interval * 1000)).Until(() => elapsed >= duration);

        }

        public static IVisualElementScheduledItem AnimateLeft(this VisualElement view, float from, float to, float duration = 0.25f, LengthUnit unit = LengthUnit.Pixel, Action onComplete = null)
        {

            var elapsed = 0f;
            var interval = 0.01f; // Interval for the updates (10ms)

            return view.schedule.Execute(() =>
            {

                elapsed += interval;
                var t = Mathf.Clamp01(elapsed / duration); // Normalized time [0, 1]

                // Ease-in-out function
                var easedT = t * t * (3f - 2f * t);

                view.style.left = new StyleLength(new Length(Mathf.Lerp(from, to, easedT), unit));

                if (elapsed >= duration)
                {
                    view.style.left = new StyleLength(new Length(to, unit)); // Ensure final value is set
                    onComplete?.Invoke();
                }

            }).Every((long)(interval * 1000)).Until(() => elapsed >= duration);

        }

        public static IVisualElementScheduledItem AnimateHeight(this VisualElement view, float to, float duration = 0.25f, LengthUnit unit = LengthUnit.Pixel, Action onComplete = null)
        {

            var from = view.localBound.height;
            var elapsed = 0f;
            var interval = 0.01f; // Interval for the updates (10ms)

            return view.schedule.Execute(() =>
            {

                elapsed += interval;
                var t = Mathf.Clamp01(elapsed / duration); // Normalized time [0, 1]

                // Ease-in-out function
                var easedT = t * t * (3f - 2f * t);

                view.style.height = new StyleLength(new Length(Mathf.Lerp(from, to, easedT), unit));

                if (elapsed >= duration)
                {
                    view.style.height = new StyleLength(new Length(to, unit)); // Ensure final value is set
                    onComplete?.Invoke();
                }

            }).Every((long)(interval * 1000)).Until(() => elapsed >= duration);

        }

        public static IVisualElementScheduledItem AnimateWidth(this VisualElement view, float to, float duration = 0.25f, LengthUnit unit = LengthUnit.Pixel, Action onComplete = null)
        {

            var from = view.localBound.width;
            var elapsed = 0f;
            var interval = 0.01f; // Interval for the updates (10ms)

            return view.schedule.Execute(() =>
            {

                elapsed += interval;
                var t = Mathf.Clamp01(elapsed / duration); // Normalized time [0, 1]

                // Ease-in-out function
                var easedT = t * t * (3f - 2f * t);

                view.style.width = new StyleLength(new Length(Mathf.Lerp(from, to, easedT), unit));

                if (elapsed >= duration)
                {
                    view.style.width = new StyleLength(new Length(to, unit)); // Ensure final value is set
                    onComplete?.Invoke();
                }

            }).Every((long)(interval * 1000)).Until(() => elapsed >= duration);

        }

        #endregion
        #region IVisualElementScheduledItem awaiter

        public static Task AsTask(this IVisualElementScheduledItem scheduledItem)
        {
            var tcs = new TaskCompletionSource<bool>();
            scheduledItem.GetAwaiter().OnCompleted(() => tcs.SetResult(true));
            return tcs.Task;
        }

        public static VisualElementScheduledItemAwaiter GetAwaiter(this IVisualElementScheduledItem scheduledItem)
        {
            return new VisualElementScheduledItemAwaiter(scheduledItem);
        }

        public class VisualElementScheduledItemAwaiter : INotifyCompletion
        {
            private readonly IVisualElementScheduledItem _scheduledItem;
            private Action _continuation;

            public VisualElementScheduledItemAwaiter(IVisualElementScheduledItem scheduledItem)
            {
                _scheduledItem = scheduledItem;
                EditorApplication.update += OnEditorUpdate;
            }

            public bool IsCompleted => !_scheduledItem.isActive;

            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                _continuation = continuation;
            }

            private void OnEditorUpdate()
            {
                if (!_scheduledItem.isActive)
                {
                    EditorApplication.update -= OnEditorUpdate;
                    _continuation?.Invoke();
                }
            }
        }

        #endregion

        public static async void Animate(Action onComplete, params IVisualElementScheduledItem[] animations)
        {

            //Make sure animations run smoothly by repainting every frame
            SceneManagerWindow.window.wantsConstantRepaint = true;

            await Task.WhenAll(animations.Select(animation => animation.AsTask()));

            EditorApplication.delayCall += () =>
            {

                if (animations.Any(a => a.isActive))
                    return;

                //Repainting every frame is expensive, lets disable in once animations are done
                SceneManagerWindow.window.wantsConstantRepaint = false;

                onComplete?.Invoke();

            };

        }

        #endregion
        #region Context menu

        static readonly Dictionary<VisualElement, ContextualMenuManipulator> manipulators = new();

        public static void ClearContextMenu(VisualElement element)
        {
            if (manipulators.Remove(element, out var manipulator))
                element.RemoveManipulator(manipulator);
        }

        public static void ContextMenu(this VisualElement element, Action<ContextualMenuPopulateEvent> e)
        {
            ClearContextMenu(element);
            manipulators.Add(element, new ContextualMenuManipulator(e) { target = element });
        }

        #endregion
        #region Popup window context menu

        public static void SetupMenuButton(this Button button, params (string header, Action action, bool enabled)[] actions) =>
            SetupMenuButton(button, button.parent, actions);

        static void SetupMenuButton(Button button, VisualElement parent, params (string header, Action action, bool enabled)[] actions)
        {

            button.userData = actions;

            if (button.style.opacity == 1)
                button.style.opacity = 0;

            button.RegisterCallback<ClickEvent>(OnMenu);

            var isHover = false;

            parent.RegisterCallback<PointerEnterEvent>(e =>
            {
                isHover = true;

                if (disableAutoHide.Contains(button))
                    return;

                if (currentMenu is null || currentMenu.userData != button)
                    button.style.opacity = 1;

            });

            parent.RegisterCallback<PointerLeaveEvent>(e =>
            {

                if (currentMenu is null || currentMenu?.userData != button)
                {
                    if (!disableAutoHide.Contains(button))
                        button.style.opacity = 0;
                }
                else
                {
                    currentMenu.RegisterCallbackOnce<DetachFromPanelEvent>(e =>
                    {
                        if (!isHover && !disableAutoHide.Contains(button))
                            button.style.opacity = 0;
                    });
                }

                isHover = false;

            });

        }

        public static VisualElement MenuButton(VisualElement parent, params (string header, Action action, bool enabled)[] actions)
        {

            var button = new Button() { text = "" };
            button.AddToClassList("fontAwesome");
            button.style.width = 22;
            button.style.height = 22;
            button.style.marginTop = 4;

            SetupMenuButton(button, parent, actions);

            return button;

        }

        static void OnMenu(ClickEvent e)
        {
            if (e.currentTarget is Button button && button.userData is (string header, Action action, bool enabled)[] actions)
                OpenMenu(button, e.pointerId, actions);
        }

        static PopupWindow currentMenu;
        static bool isPopupHover;
        static VisualElement rootVisualElement;
        static readonly EventCallback<PointerDownEvent> pointerDownCallback = new(e => CloseMenu());
        static EventCallback<GeometryChangedEvent> geometryChangedCallback;

        static void OpenMenu(Button placementTarget, int pointerID, params (string header, Action action, bool enabled)[] actions)
        {

            CloseMenu();

            rootVisualElement = placementTarget.panel.visualTree.Query().AtIndex(2);
            if (rootVisualElement is null)
                return;

            var popup = new PopupWindow();
            popup.RegisterCallback<PointerOverEvent>(e => isPopupHover = true);
            popup.RegisterCallback<PointerLeaveEvent>(e => isPopupHover = false);
            popup.RegisterCallbackOnce<DetachFromPanelEvent>(e => isPopupHover = false);

            currentMenu = popup;
            currentMenu.userData = placementTarget;

            SetupButtons();

            popup.Hide();
            rootVisualElement.Add(popup);

            SetupPosition();
            SetStyle();

            popup.Show();
            SetupClose();

            void SetupButtons()
            {

                foreach (var (header, action, enabled) in actions)
                {
                    var button = new Button(() => CloseMenu(action)) { text = header };
                    button.SetEnabled(enabled);
                    popup.Add(button);
                }

                void CloseMenu(Action action)
                {
                    action.Invoke();
                    VisualElementUtility.CloseMenu(ignoreHover: true);
                }

            }

            void SetStyle()
            {
                popup.style.borderTopLeftRadius = 3;
                popup.style.borderTopRightRadius = 3;
                popup.style.borderBottomRightRadius = 3;
                popup.style.borderBottomLeftRadius = 3;
                popup.Q("unity-content-container").style.paddingTop = 4;
            }

            void SetupPosition()
            {

                if (geometryChangedCallback is not null)
                {
                    rootVisualElement.UnregisterCallback(geometryChangedCallback);
                    popup.UnregisterCallback(geometryChangedCallback);
                }

                geometryChangedCallback = new(e => UpdatePosition());

                rootVisualElement.RegisterCallback(geometryChangedCallback);
                popup.RegisterCallback(geometryChangedCallback);

                UpdatePosition();
                void UpdatePosition()
                {

                    var buttonWorldBound = placementTarget.worldBound;
                    var rootWorldBound = rootVisualElement.worldBound;
                    var popupWidth = popup.worldBound.width;
                    var popupHeight = popup.worldBound.height;

                    // Calculate the initial popup position
                    var popupX = buttonWorldBound.xMin + (buttonWorldBound.width / 2) - (popupWidth / 2);
                    var popupY = buttonWorldBound.yMin - rootWorldBound.yMin - popupHeight - 3;

                    // Adjust popupX to ensure it is within the rootWorldBound
                    popupX = Mathf.Clamp(popupX, rootWorldBound.xMin + 14, rootWorldBound.xMax - popupWidth - 14);
                    // Adjust popupY to ensure it is within the rootWorldBound
                    popupY = Mathf.Clamp(popupY, rootWorldBound.yMin + 14, rootWorldBound.yMax - popupHeight - 14);

                    var (left, top) = offsets.GetValueOrDefault(placementTarget);

                    popup.style.position = Position.Absolute;
                    popup.style.left = popupX - left;
                    popup.style.top = popupY - top;

                }

            }

            void SetupClose()
            {

                // Capture pointer events on the root to detect clicks outside the popup
                rootVisualElement.UnregisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
                rootVisualElement.RegisterCallback(pointerDownCallback, TrickleDown.TrickleDown);

            }

        }

        static void CloseMenu(bool ignoreHover = false)
        {

            if (!ignoreHover && isPopupHover)
                return;

            rootVisualElement?.UnregisterCallback(pointerDownCallback, TrickleDown.TrickleDown);
            rootVisualElement?.UnregisterCallback(geometryChangedCallback);
            rootVisualElement = null;

            currentMenu?.RemoveFromHierarchy();
            currentMenu = null;

        }

        static readonly Dictionary<Button, (int left, int top)> offsets = new();
        static readonly List<Button> disableAutoHide = new();
        public static void OffsetPopupMenu(this Button button, int? left = null, int? top = null)
        {
            offsets.Set(button, (left ?? 0, top ?? 0));
            currentMenu?.SendEvent(GeometryChangedEvent.GetPooled());
        }

        public static void DisableMenuButtonAutoHide(this Button button) =>
            disableAutoHide.Add(button);

        #endregion
        #region ScrollView

        static SerializableDictionary<string, float> scrollPositions => SceneManager.settings.user.scrollPositions;
        static readonly List<ScrollView> restoring = new();
        static readonly List<ScrollView> scrollViews = new();

        public static void PersistScrollPosition(this ScrollView scrollView)
        {
            if (scrollViews.Contains(scrollView))
                return;
            scrollViews.Add(scrollView);

            SceneManager.OnInitialized(() =>
            {

                scrollView.RegisterCallbackOnce<DetachFromPanelEvent>(e =>
                {
                    scrollViews.Remove(scrollView);
                    restoring.Remove(scrollView);
                });

                //Register restore for ScrollView GeometryChangedEvent
                scrollView.RegisterCallback<GeometryChangedEvent>(e => scrollView.RestoreScrollPosition());

                //Register restore for nested ListView GeometryChangedEvent
                scrollView.Query<ListView>().ForEach(list => list.RegisterCallback<GeometryChangedEvent>(e => scrollView.RestoreScrollPosition()));

                //Register save when scroll changes
                scrollView.verticalScroller.slider.RegisterValueChangedCallback(e =>
                {

                    if (restoring.Contains(scrollView))
                        return;

                    scrollPositions.Set(scrollView.name, e.newValue);
                    //Debug.Log($"Save '{scrollView.name}': " + e.newValue);

                });

            });
        }


        public static void RestoreScrollPosition(this ScrollView scrollView)
        {
            SceneManager.OnInitialized(() =>
            {

                restoring.Add(scrollView);

                var value = scrollPositions.GetValueOrDefault(scrollView.name);
                scrollView.scrollOffset = new Vector2(0, value);

                //Debug.Log($"Restore '{scrollView.name}': " + value);
                restoring.Remove(scrollView);

            });
        }

        public static void ClearScrollPosition(this ScrollView scrollView)
        {
            //Debug.Log($"Clear '{scrollView.name}'");
            SceneManager.OnInitialized(() => scrollPositions.Set(scrollView.name, 0));
        }

        #endregion

    }

}
