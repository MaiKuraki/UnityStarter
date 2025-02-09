using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Layouts;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Utility;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    public class ViewModelBase : IView
    {

        public VisualElement view { get; protected set; }

        public virtual void SetView(VisualElement view)
        {
            this.view = view;
        }

        public bool isAdded => view is not null;

        public virtual void OnAdded()
        { }

        public virtual void OnRemoved()
        { }

        public virtual void ApplyAppearanceSettings()
        { }

        public virtual void OnWindowEnable() { }
        public virtual void OnWindowDisable() { }
        public virtual void OnWindowFocus() { }
        public virtual void OnWindowLostFocus() { }

        public virtual void ApplyAppearanceSettings(VisualElement element) { }

        public virtual void OnGUI() { }

    }

    abstract class ViewModel : ViewModelBase
    {

        public SceneManagerWindow window => SceneManagerWindow.window;
        public VisualElement rootVisualElement => SceneManagerWindow.rootVisualElement;
        public ViewHandler viewHandler => SceneManagerWindow.viewHandler;
        public ViewLocator viewLocator => SceneManagerWindow.viewLocator;

        public void ReloadView<TView>() where TView : IView
        {

            if (view.parent is null)
                return;

            var asset = viewHandler.GetView<TView>();
            var template = asset.CloneTree();
            template.name = typeof(TView).Name;

            var parent = view.parent;

            var index = parent.IndexOf(view);
            view.RemoveFromHierarchy();
            parent.Insert(index, template);
            SetView(template);

        }

        public void DisableTemplateContainer()
        {

            if (view is TemplateContainer container)
            {

                SetView(view.Children().First());

                var parent = container.parent;
                var index = parent.IndexOf(container);
                container.RemoveFromHierarchy();
                view.RemoveFromHierarchy();

                if (string.IsNullOrEmpty(view.name))
                    view.name = GetType().Name;

                parent.Insert(index, view);

            }

        }

        public TemplateContainer Instantiate(VisualTreeAsset view) => ViewUtility.Instantiate(view);

        #region Popup

        /// <summary>Occurs when popup is re-opened.</summary>
        /// <remarks>Not called unless view is hosted in <see cref="PopupLayout{T}"/>.</remarks>
        public virtual void OnReopen()
        { }

        /// <summary>Occurs when parameter is passed to popup.</summary>
        /// <remarks>Not called unless view is hosted in <see cref="PopupLayout{T}"/>. Not called on popup re-open.</remarks>
        public virtual void PassParameter(object parameter)
        { }

        #endregion
        #region Services

        NotificationView _notificationView;
        SelectionView _selectionView;
        SearchView _searchView;
        PickNamePopup _pickNamePopup;
        ConfirmPopup _confirmPopup;
        UndoView _undoView;
        CollectionView _collectionView;
        PopupView _popupView;
        SettingsView _settingsView;
        ProgressSpinnerView _progressSpinnerView;
        ProfileBindingsService _profileBindingService;

        protected NotificationView notifications => _notificationView ??= Get<NotificationView>();
        protected SelectionView selection => _selectionView ??= Get<SelectionView>();
        protected SearchView search => _searchView ??= Get<SearchView>();
        protected PickNamePopup pickNamePopup => _pickNamePopup ??= Get<PickNamePopup>();
        protected ConfirmPopup confirmPopup => _confirmPopup ??= Get<ConfirmPopup>();
        protected UndoView undo => _undoView ??= Get<UndoView>();
        protected CollectionView collectionView => _collectionView ??= Get<CollectionView>();
        protected ProgressSpinnerView progressSpinnerView => _progressSpinnerView ??= Get<ProgressSpinnerView>();
        protected ProfileBindingsService profileBindingService => _profileBindingService ??= Get<ProfileBindingsService>();

        protected PopupView popupView => _popupView ??= Get<PopupView>();
        protected SettingsView settingsView => _settingsView ??= Get<SettingsView>();

        T Get<T>() where T : DependencyInjectionUtility.IInjectable =>
            DependencyInjectionUtility.GetService<T>()
            ?? throw new InvalidOperationException($"Could not retrieve service '{typeof(T).Name}'.");

        protected void OpenPopup<T>() where T : IPopup =>
            popupView.Open<T>();

        protected void OpenPopup<T>(object param) where T : IPopup =>
            popupView.Open<T>(param);

        protected void OpenSettings<T>() where T : ISettingsPage =>
            settingsView.Open<T>();

        protected void ClosePopup() =>
            popupView.ClosePopup();

        protected Task<string> PromptName(string placeholder) =>
            pickNamePopup.PromptName(placeholder);

        #endregion
        #region Persistence

        public void Set<T>(T value, [CallerMemberName] string propertyName = "") =>
            SessionStateUtility.Set(value, $"{GetType().Name}.{propertyName}");

        public T Get<T>(T value, [CallerMemberName] string propertyName = "") =>
            SessionStateUtility.Get(value, $"{GetType().Name}.{propertyName}");

        #endregion

    }

}
