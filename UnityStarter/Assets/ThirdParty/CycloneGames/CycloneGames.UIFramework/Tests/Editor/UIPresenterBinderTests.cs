using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIPresenterBinderTests
    {
        private GameObject _windowObject;
        private UIWindow _window;
        private UIPresenterBinder _binder;

        [SetUp]
        public void SetUp()
        {
            RecordingPresenter.Reset();
            UIPresenterFactory.CustomFactory = type => type == typeof(RecordingPresenter) ? new RecordingPresenter() : null;

            _windowObject = new GameObject("InventoryWindow");
            _window = _windowObject.AddComponent<UIWindow>();
            _window.SetWindowName("InventoryWindow");

            _binder = new UIPresenterBinder();
            _binder.RegisterMapping<RecordingPresenter>("InventoryWindow");
        }

        [TearDown]
        public void TearDown()
        {
            UIPresenterFactory.CustomFactory = null;
            UIPresenterFactory.ClearRegistrations();
            UIPresenterBinder.ClearGlobalMappings();

            if (_windowObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_windowObject);
            }
        }

        [Test]
        public void OnWindowCreated_CreatesPresenterAndBindsView()
        {
            var service = new TestUIService();
            _binder.SetUIService(service);

            _binder.OnWindowCreated(_window);

            Assert.AreEqual(1, RecordingPresenter.CreatedCount);
            Assert.AreSame(_window, RecordingPresenter.LastView);
            Assert.AreSame(service, RecordingPresenter.LastUIService);
        }

        [Test]
        public void OnWindowStateChanged_ForwardsLifecycleCallbacksInOrder()
        {
            _binder.OnWindowCreated(_window);

            _binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnStartOpen);
            _binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnFinishedOpen);
            _binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnStartClose);
            _binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnFinishedClose);

            Assert.AreEqual("Opening,Opened,Closing,Closed", RecordingPresenter.Lifecycle);
        }

        [Test]
        public void OnWindowDestroying_DisposesAndRemovesPresenter()
        {
            _binder.OnWindowCreated(_window);

            _binder.OnWindowDestroying(_window);
            _binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnStartOpen);

            Assert.AreEqual(1, RecordingPresenter.DisposeCount);
            Assert.AreEqual(string.Empty, RecordingPresenter.Lifecycle);
        }

        [Test]
        public void ExplicitRegistration_CreatesPresenterWithoutCustomFactory()
        {
            UIPresenterFactory.CustomFactory = null;
            UIPresenterFactory.Register(() => new RecordingPresenter());

            var binder = new UIPresenterBinder();
            binder.RegisterMapping<RecordingPresenter>("InventoryWindow");

            binder.OnWindowCreated(_window);

            Assert.AreEqual(1, RecordingPresenter.CreatedCount);
            Assert.AreSame(_window, RecordingPresenter.LastView);
        }

        [Test]
        public void DefaultBinder_ResolvesCreatorGeneratedRegistration()
        {
            UIPresenterFactory.CustomFactory = null;
            CreatorGeneratedPresenter.RegisterGeneratedBinding();

            var windowObject = new GameObject(nameof(CreatorGeneratedWindow));
            var window = windowObject.AddComponent<CreatorGeneratedWindow>();
            window.SetWindowName(nameof(CreatorGeneratedWindow));

            try
            {
                var binder = new UIPresenterBinder();

                binder.OnWindowCreated(window);

                Assert.AreEqual(1, CreatorGeneratedPresenter.CreatedCount);
                Assert.AreSame(window, CreatorGeneratedPresenter.LastView);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(windowObject);
                CreatorGeneratedPresenter.Reset();
            }
        }

        [Test]
        public void OnWindowCreated_WithoutPresenterMapping_LeavesClassicWindowUntouched()
        {
            UIPresenterFactory.CustomFactory = null;
            var binder = new UIPresenterBinder();

            binder.OnWindowCreated(_window);
            binder.OnWindowStateChanged(_window, WindowStateCallbackType.OnStartOpen);
            binder.OnWindowDestroying(_window);

            Assert.AreEqual(0, RecordingPresenter.CreatedCount);
            Assert.IsNull(RecordingPresenter.LastView);
        }

        [Test]
        public void OnWindowCreated_WithMappingButNoFactoryRegistration_DoesNotCreatePresenter()
        {
            UIPresenterFactory.CustomFactory = null;

            _binder.OnWindowCreated(_window);

            Assert.AreEqual(0, RecordingPresenter.CreatedCount);
            Assert.IsNull(RecordingPresenter.LastView);
        }

        private sealed class RecordingPresenter : IUIPresenter
        {
            public static int CreatedCount;
            public static int DisposeCount;
            public static UIWindow LastView;
            public static IUIService LastUIService;
            public static string Lifecycle;

            public RecordingPresenter()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                DisposeCount = 0;
                LastView = null;
                LastUIService = null;
                Lifecycle = string.Empty;
            }

            public void SetView(UIWindow view)
            {
                LastView = view;
            }

            public void SetUIService(IUIService uiService)
            {
                LastUIService = uiService;
            }

            public void OnViewOpening()
            {
                Append("Opening");
            }

            public void OnViewOpened()
            {
                Append("Opened");
            }

            public void OnViewClosing()
            {
                Append("Closing");
            }

            public void OnViewClosed()
            {
                Append("Closed");
            }

            public void Dispose()
            {
                DisposeCount++;
            }

            private static void Append(string value)
            {
                Lifecycle = string.IsNullOrEmpty(Lifecycle) ? value : Lifecycle + "," + value;
            }
        }

        private sealed class CreatorGeneratedWindow : UIWindow
        {
        }

        private sealed class CreatorGeneratedPresenter : IUIPresenter
        {
            public static int CreatedCount;
            public static UIWindow LastView;

            public static void RegisterGeneratedBinding()
            {
                UIPresenterFactory.Register<CreatorGeneratedPresenter>();
                UIPresenterBinder.RegisterGlobalMapping<CreatorGeneratedPresenter>(nameof(CreatorGeneratedWindow));
            }

            public CreatorGeneratedPresenter()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                LastView = null;
            }

            public void SetView(UIWindow view)
            {
                LastView = view;
            }

            public void SetUIService(IUIService uiService)
            {
            }

            public void OnViewOpening()
            {
            }

            public void OnViewOpened()
            {
            }

            public void OnViewClosing()
            {
            }

            public void OnViewClosed()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestUIService : IUIService
        {
            public IUINavigationService NavigationService { get; private set; }
            public IUITransitionCoordinator TransitionCoordinator { get; private set; }

            public void OpenUI(string windowName, Action<UIWindow> onWindowCreated = null, bool? isSceneBoundOverride = null, UIAssetLoadContext assetLoadContext = default) { }
            public UniTask<UIWindow> OpenUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default, bool? isSceneBoundOverride = null, UIAssetLoadContext assetLoadContext = default) => UniTask.FromResult<UIWindow>(null);
            public void CloseUI(string windowName) { }
            public UniTask CloseUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public bool IsUIWindowValid(string windowName) => false;
            public UIWindow GetUIWindow(string windowName) => null;
            public (float, float) GetRootCanvasSize() => default;
            public Camera GetUICamera() => null;
            public void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService) { }
            public void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService, IAssetPackage package) { }
            public void SetNavigationService(IUINavigationService nav) => NavigationService = nav;
            public void SetTransitionCoordinator(IUITransitionCoordinator coordinator) => TransitionCoordinator = coordinator;
            public void RegisterWindowBinder(IUIWindowBinder binder) { }
            public void UnregisterWindowBinder(IUIWindowBinder binder) { }
            public void SetCoordinatedNavStrategy(CoordinatedNavStrategy strategy) { }
            public UniTask CoordinatedNavigateAsync(string fromWindow, string toWindow, NavigationDirection direction = NavigationDirection.Forward, System.Threading.CancellationToken ct = default) => UniTask.CompletedTask;
        }
    }
}
