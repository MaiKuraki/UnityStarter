using System;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Layouts
{

    class PopupView : PopupLayout<IPopup>
    {

        internal static event Action onPopupClose;

        EventCallback<GeometryChangedEvent> contentContainerGeometryChangedCallback;
        EventCallback<GeometryChangedEvent> contentContainerGeometryChangedCallback2;

        public override VisualElement contentContainer => view?.Q("contentContainer");

        public override void OnAdded()
        {

            DisableTemplateContainer();

            contentContainerGeometryChangedCallback = new(OnGeometryChanged);
            contentContainerGeometryChangedCallback2 = new(OnGeometryChanged2);
            view.RegisterCallback(contentContainerGeometryChangedCallback2);

            view.SetVisible(false);

            view.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == view)
                    ClosePopup();
            });

            base.OnAdded();

            var scroll = view.Q<ScrollView>();
            if (scroll is not null)
                scroll.verticalScroller.value = 0;

        }

        public override void Reopen<TPopup>()
        {
            OpenInternal<TPopup>(isReopen: true);
        }

        public override void Open<TPopup>(object parameter = null)
        {

            contentContainer.Clear();

            OpenInternal<TPopup>(parameter);
            AnimateOpen();

        }

        void OpenInternal<TPopup>(object parameter = null, bool isReopen = false) where TPopup : IPopup
        {

            var popupView = Add<TPopup>();
            currentPopup = viewHandler.Get<TPopup>();
            currentPopup.SetView(popupView);

            if (parameter is not null)
                currentPopup.PassParameter(parameter);

            if (isReopen)
                currentPopup.OnReopen();

            //IPopup.OnReopen can call ClosePopup
            if (currentPopup is not null)
            {
                currentPopup.InvokeView(currentPopup.OnAdded);
                view.SetVisible(true);
            }

        }

        public override void ClosePopup(Action onComplete = null)
        {

            var popup = currentPopup;
            currentPopup = null;
            AnimateClose(OnCompleted);

            void OnCompleted()
            {

                if (popup is not null)
                {

                    try
                    {
                        popup.InvokeView(popup.OnRemoved);
                        popup.view?.RemoveFromHierarchy();
                        popup.SetView(null);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }

                }

                view.SetVisible(false);
                contentContainer.Clear();

                onPopupClose?.Invoke();
                onComplete?.Invoke();

            }

        }

        void OnGeometryChanged2(GeometryChangedEvent e)
        {
            contentContainer.style.maxHeight = view.resolvedStyle.height - 60;
        }

        #region Animations

        void AnimateOpen() =>
            contentContainer.RegisterCallback(contentContainerGeometryChangedCallback);

        void OnGeometryChanged(GeometryChangedEvent e)
        {

            view.style.opacity = 0;

            contentContainer.UnregisterCallback(contentContainerGeometryChangedCallback);
            VisualElementUtility.Animate(
                onComplete: null,
                view.Fade(1, 0.2f),
                contentContainer.AnimateBottom(-view.resolvedStyle.height + view.resolvedStyle.marginBottom, 0, 0.15f));

        }

        void AnimateClose(Action onComplete = null) =>
            VisualElementUtility.Animate(onComplete, view.Fade(0));

        #endregion

    }

}
