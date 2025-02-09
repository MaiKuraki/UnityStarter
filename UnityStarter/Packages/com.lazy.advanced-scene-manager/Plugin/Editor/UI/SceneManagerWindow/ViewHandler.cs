using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    class ViewHandler
    {

        public ViewHandler(ViewLayout<IView> mainLayout, ViewLayout<IPopup> popupLayout, ViewLayout<ISettingsPage> settingsLayout)
        {
            this.mainLayout = mainLayout;
            this.popupLayout = popupLayout;
            this.settingsLayout = settingsLayout;
        }

        public readonly Dictionary<IView, VisualTreeAsset> all = new();

        public readonly Dictionary<IView, VisualTreeAsset> views = new();
        public readonly Dictionary<IPopup, VisualTreeAsset> popups = new();
        public readonly Dictionary<ISettingsPage, VisualTreeAsset> settingsPages = new();

        public readonly Dictionary<IPersistentNotification, VisualTreeAsset> notifications = new(); //VisualTreeAsset unused

        public VisualElement rootVisualElement { get; }

        public ViewLayout<IView> mainLayout { get; }
        public ViewLayout<IPopup> popupLayout { get; }
        public ViewLayout<ISettingsPage> settingsLayout { get; }

        #region Add

        public TViewModel AddView<TViewModel>(VisualTreeAsset view = null) where TViewModel : IView =>
            Add<IView, TViewModel>(views, view);

        public TViewModel AddSettings<TViewModel>(VisualTreeAsset view = null) where TViewModel : ISettingsPage =>
            Add<ISettingsPage, TViewModel>(settingsPages, view);

        public TViewModel AddPopup<TViewModel>(VisualTreeAsset view = null) where TViewModel : IPopup =>
            Add<IPopup, TViewModel>(popups, view);

        public TViewModel AddNotification<TViewModel>() where TViewModel : IPersistentNotification =>
            Add<IPersistentNotification, TViewModel>(notifications);

        public TNotification AddNotification<TNotification, TPopup>(VisualTreeAsset view = null) where TNotification : IPersistentNotification where TPopup : IPopup
        {
            AddPopup<TPopup>(view);
            return AddNotification<TNotification>();
        }

        TImplementation Add<TInterface, TImplementation>(Dictionary<TInterface, VisualTreeAsset> list, VisualTreeAsset view = null) where TInterface : IView where TImplementation : TInterface
        {

            var viewModel = DependencyInjectionUtility.Construct<TImplementation>() ??
                throw new ArgumentException($"Could not construct view model {typeof(TImplementation).Name}.");

            all.Add(viewModel, view);
            list.Add(viewModel, view);
            DependencyInjectionUtility.Add(typeof(TInterface), viewModel);
            return viewModel;

        }

        #endregion
        #region Get view model

        public TViewModel Get<TViewModel>() where TViewModel : DependencyInjectionUtility.IInjectable =>
            (TViewModel)all.Keys.FirstOrDefault(viewModel => typeof(TViewModel).IsAssignableFrom(viewModel.GetType()));

        public IView Get(Type type) =>
            all.Keys.FirstOrDefault(viewModel => type?.IsAssignableFrom(viewModel.GetType()) ?? false);

        #endregion
        #region Get view

        public VisualTreeAsset GetView<TViewModel>() where TViewModel : IView
        {
            var key = Get<TViewModel>();
            return key is not null ? all.GetValueOrDefault(key) : null;
        }

        #endregion

    }

}
