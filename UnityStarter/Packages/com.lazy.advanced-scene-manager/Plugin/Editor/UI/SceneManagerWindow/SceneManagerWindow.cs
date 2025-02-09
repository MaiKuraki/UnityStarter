using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.DependencyInjection.Editor;
using AdvancedSceneManager.Editor.UI.Layouts;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Editor.UI.Views.Settings;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    [InitializeInEditor]
    /// <summary>The scene manager window provides the front-end for Advanced Scene Manager.</summary>
    public class SceneManagerWindow : EditorWindow
    {

        //Font awesome assets keep getting warnings that the assets aren't in use, even if they are.
        //This makes the warnings go away
        public FontAsset fontAsset1;
        public FontAsset fontAsset2;
        public FontAsset fontAsset3;

        internal static new VisualElement rootVisualElement { get; private set; }
        internal static SceneManagerWindow window { get; private set; }

        public static event Action onOpen;
        public static event Action onClose;
        public static event Action onFocus;

        internal static ViewHandler viewHandler { get; } = new(
            mainLayout: new MainView(),
            popupLayout: new PopupView(),
            settingsLayout: new SettingsView());

        void CreateGUI()
        {

            titleContent = new GUIContent("Scene Manager");

            minSize = new(466, 230);

            window = this;
            rootVisualElement = base.rootVisualElement;

            SetupDependencyInjection();
            ((MainView)viewHandler.mainLayout).OnCreateGUI();

            ApplyAppearanceSettings();

            onOpen?.Invoke();

            SceneManager.settings.user.PropertyChanged += (s, e) => ApplyAppearanceSettings();

        }

        private void OnDestroy() =>
           viewHandler.mainLayout.InvokeView(((MainView)viewHandler.mainLayout).OnWindowClose);

        /// <summary>Closes the window.</summary>
        public static new void Close()
        {
            if (window)
                ((EditorWindow)window).Close();
        }

        [MenuItem("File/Scene Manager %#m", priority = 205)]
        [MenuItem("Window/Advanced Scene Manager/Scene Manager", priority = 3030)]
        public static void Open() => GetWindow<SceneManagerWindow>();

        public bool wantsConstantRepaint { get; set; }

        public static new void Focus()
        {
            if (window)
                ((EditorWindow)window).Focus();
        }

        public static IEnumerable<StyleSheet> FindStyles() =>
            AssetDatabaseUtility.FindAssets<StyleSheet>($"{SceneManager.package.folder}/Plugin/Editor/UI/SceneManagerWindow/Styles");

        #region ISceneManagerWindow

        sealed class SceneManagerWindowService : ISceneManagerWindow
        {

            private readonly CollectionView collectionView;

            public SceneManagerWindowService(CollectionView collectionView) =>
                this.collectionView = collectionView;

            public void OpenWindow() =>
                Open();

            public void CloseWindow() =>
                Close();

            public void Reload()
            {
                if (window)
                {
                    collectionView.Reload();
                    DependencyInjectionUtility.GetService<NotificationView>().ReloadPersistentNotifications();
                }
            }
        }

        #endregion
        #region Dependency injection

        internal static readonly Dictionary<Type, ASMSettingsPageAttribute> additionalSettingsPages = new();

        /// <summary>Adds a page to the settings.</summary>
        /// <remarks>Will register <typeparamref name="TPage"/> with <see cref="DependencyInjection.DependencyInjectionUtility"/>.</remarks>
        static void AddSettingsPage(Type type, ASMSettingsPageAttribute attribute)
        {
            var view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.UxmlPageTemplatePath);
            if (view)
            {
                var m = typeof(ViewHandler).GetMethod(nameof(ViewHandler.AddView)).MakeGenericMethod(type);
                m.Invoke(viewHandler, new[] { view });
                additionalSettingsPages.Set(type, attribute);
            }
            else
                Debug.LogError($"The settings page {type.FullName} cannot be inserted. The view could not be found.");
        }

        static ViewLocator m_viewLocator;
        internal static ViewLocator viewLocator
        {
            get
            {
                if (m_viewLocator)
                    return m_viewLocator;

                m_viewLocator = AssetDatabaseUtility.FindAssets<ViewLocator>().FirstOrDefault();
                if (!m_viewLocator)
                    throw new InvalidOperationException("Could not find ViewLocator, you may have to re-install ASM.");

                return m_viewLocator;

            }
        }

        public const int popupFadeAnimationDuration = 250;

        static bool hasInjected;
        static void SetupDependencyInjection()
        {

            if (hasInjected)
                return;
            hasInjected = true;

            //Main
            viewHandler.AddView<HeaderView>(viewLocator.main.header);
            viewHandler.AddView<SearchView>(viewLocator.main.search);
            viewHandler.AddView<CollectionView>(viewLocator.main.collection);
            viewHandler.AddView<UndoView>(viewLocator.main.undo);
            viewHandler.AddView<FooterView>(viewLocator.main.footer);
            viewHandler.AddView<ProgressSpinnerView>(viewLocator.main.progressSpinner);
            viewHandler.AddView<NotificationView>(viewLocator.main.notification);
            viewHandler.AddView<SelectionView>(viewLocator.main.selection);
            viewHandler.AddView<DevMenuView>();
            viewHandler.AddView<ProfileBindingsService>();

            //Layouts
            viewHandler.AddView<PopupView>(viewLocator.layouts.popups);
            viewHandler.AddPopup<SettingsView>(viewLocator.layouts.settings);

            //Popups
            viewHandler.AddPopup<CollectionPopup>(viewLocator.popups.collection);
            viewHandler.AddPopup<DynamicCollectionPopup>(viewLocator.popups.dynamicCollection);
            viewHandler.AddPopup<MenuPopup>(viewLocator.popups.menu);
            viewHandler.AddPopup<OverviewPopup>(viewLocator.popups.overview);
            viewHandler.AddPopup<ScenePopup>(viewLocator.popups.scene);
            viewHandler.AddPopup<ExtraCollectionPopup>(viewLocator.popups.list);
            viewHandler.AddPopup<ProfilePopup>(viewLocator.popups.list);
            viewHandler.AddPopup<PickNamePopup>(viewLocator.popups.pickName);
            viewHandler.AddPopup<ConfirmPopup>(viewLocator.popups.confirm);

            //Settings
            viewHandler.AddSettings<AppearancePage>(viewLocator.settings.appearance);
            viewHandler.AddSettings<AssetsPage>(viewLocator.settings.assets);
            viewHandler.AddSettings<EditorPage>(viewLocator.settings.editor);
            viewHandler.AddSettings<NetworkPage>(viewLocator.settings.network);
            viewHandler.AddSettings<SceneLoadingPage>(viewLocator.settings.sceneLoading);
            viewHandler.AddSettings<StartupPage>(viewLocator.settings.startup);
            viewHandler.AddSettings<ExperimentalPage>(viewLocator.settings.experimental);

            var dynamicSettingsPages = TypeUtility.FindClassesDecoratedWithAttribute<ASMSettingsPageAttribute>();
            foreach (var (type, attribute) in dynamicSettingsPages)
                AddSettingsPage(type, attribute);

            //Notifications
            viewHandler.AddNotification<WelcomeNotification>();
            viewHandler.AddNotification<EditorCoroutinesNotification>();
            viewHandler.AddNotification<LegacyNotification, LegacyPopup>(viewLocator.popups.legacy);
            viewHandler.AddNotification<BadScenePathNotification, BadPathScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<DuplicateScenesNotification, DuplicateScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<ImportedBlacklistedSceneNotification, ImportedBlacklistedScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<ImportSceneNotification, ImportScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<InvalidSceneNotification, InvalidScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<UntrackedSceneNotification, UntrackedScenePopup>(viewLocator.popups.importScene);
            viewHandler.AddNotification<GitIgnoreNotification>();

            viewHandler.AddNotification<UpdateNotification, UpdatePopup>(viewLocator.popups.update);
            viewHandler.AddSettings<UpdatesPage>(viewLocator.settings.updates);

            DependencyInjectionUtility.Add<ISceneManagerWindow, SceneManagerWindowService>();

        }

        #endregion
        #region Callbacks

        void OnEnable()
        {
            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnWindowEnable);
        }

        void OnDisable()
        {
            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnWindowDisable);
            onClose?.Invoke();
        }

        void OnFocus()
        {

            if (!SceneManager.isInitialized)
                return;

            if (Assets.Cleanup())
                Assets.Save();

            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnWindowFocus);

            onFocus?.Invoke();

        }

        void OnLostFocus()
        {
            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnWindowLostFocus);
        }

        void OnGUI()
        {

            foreach (var view in viewHandler.views)
                view.Key.InvokeView(view.Key.OnGUI);

            if (wantsConstantRepaint)
                Repaint();

        }

        #endregion
        #region Appearance settings

        void ApplyAppearanceSettings()
        {
            foreach (var view in viewHandler.all)
                view.Key.InvokeView(view.Key.ApplyAppearanceSettings);
        }

        #endregion
        #region Close on uninstall

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            Events.registeringPackages += (e) =>
            {
                var id = SceneManager.package.id;
                if (e.removed.Any(p => p.packageId == id))
                    Close();
            };
        }

        #endregion

    }

}
