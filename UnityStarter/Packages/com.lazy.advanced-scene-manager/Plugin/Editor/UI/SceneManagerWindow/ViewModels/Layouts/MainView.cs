using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Layouts
{

    class MainView : VerticalLayout<IView>
    {

        public void OnCreateGUI()
        {
            LoadStyles();
            SetupUI();
        }

        public void OnWindowClose()
        {
            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnRemoved);
        }

        void LoadStyles()
        {

            var styles = SceneManagerWindow.FindStyles().ToArray();
            if (!styles.Any())
                Debug.LogError("Could not find any styles for the scene manager window. You may try re-importing or re-installing ASM.");

            foreach (var style in styles)
                rootVisualElement.styleSheets.Add(style);

        }

        void SetupUI()
        {

            viewHandler.mainLayout.SetWrapper(() =>
            {
                var scroll = new ScrollView().Expand();
                scroll.name = "RootScroll";
                scroll.contentContainer.Expand();
                scroll.PersistScrollPosition();
                return scroll;
            },
            scroll => scroll.contentContainer);

            viewHandler.mainLayout.AddAndInvoke<HeaderView>().NoShrink();
            viewHandler.mainLayout.AddAndInvoke<SearchView>().NoShrink();
            viewHandler.mainLayout.AddAndInvoke<CollectionView>().Expand().MinHeight(64);
            viewHandler.mainLayout.AddAndInvoke<SelectionView>().NoShrink();
            viewHandler.mainLayout.AddAndInvoke<UndoView>().NoShrink();
            viewHandler.mainLayout.AddAndInvoke<NotificationView>().NoShrink();
            viewHandler.mainLayout.AddAndInvoke<FooterView>().NoShrink();

            viewHandler.mainLayout.AddAndInvoke<PopupView>(rootVisualElement);
            viewHandler.mainLayout.AddAndInvoke<ProgressSpinnerView>(rootVisualElement);

            foreach (var view in viewHandler.views.Where(v => !v.Value).ToList())
                view.Key.OnAdded();

            SetupScrollViews();

            DependencyInjectionUtility.GetService<NotificationView>().ReloadPersistentNotifications();

        }

        #region Scrollviews

        void SetupScrollViews()
        {

            var rootScroll = rootVisualElement.Q<ScrollView>();
            var collectionView = viewHandler.Get<CollectionView>();
            var collectionScroll = collectionView.view.Q<ScrollView>();

            rootScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            rootScroll.style.paddingLeft = 8;
            rootScroll.style.paddingTop = 4;
            rootScroll.style.paddingRight = 8;
            rootScroll.style.paddingBottom = 4;

            //Only show root scroll if collection scroll is clamped to its min height, 64px.
            UpdateScrollViews();
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(e => UpdateScrollViews(), TrickleDown.TrickleDown);
            collectionView.view.RegisterCallback<GeometryChangedEvent>(e => UpdateScrollViews());
            notifications.view.RegisterCallback<GeometryChangedEvent>(e => UpdateScrollViews());

            void UpdateScrollViews()
            {
                var isClamped = Mathf.Approximately(collectionView.view.resolvedStyle.height, collectionView.view.style.minHeight.value.value);
                rootScroll.verticalScrollerVisibility = isClamped ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            }

        }

        #endregion

    }

}
