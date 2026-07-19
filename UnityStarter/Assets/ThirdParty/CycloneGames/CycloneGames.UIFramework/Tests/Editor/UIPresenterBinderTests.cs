using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIPresenterBinderTests
    {
        private readonly List<GameObject> _windowObjects = new List<GameObject>(4);
        private UIRuntimeTestFixture _fixture;
        private UIService _service;

        [SetUp]
        public void SetUp()
        {
            _fixture = new UIRuntimeTestFixture();
            _service = new UIService(_fixture.Root);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            for (int i = _windowObjects.Count - 1; i >= 0; i--)
            {
                if (_windowObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_windowObjects[i]);
                }
            }

            _windowObjects.Clear();
            _fixture?.Dispose();
        }

        [Test]
        public void Bind_WithRegistration_AssignsServiceBeforeView()
        {
            UIWindow window = CreateWindow("Inventory");
            var presenter = new RecordingPresenter();
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>("Inventory", () => presenter);

            IUIWindowBinding binding = binder.Bind(new UIWindowBindingContext(window, _service));

            Assert.IsNotNull(binding);
            CollectionAssert.AreEqual(new[] { "Service", "View" }, presenter.Events);
            Assert.AreSame(_service, presenter.UIService);
            Assert.AreSame(window, presenter.View);
            binding.Dispose();
        }

        [Test]
        public void Bind_WithoutRegistration_ReturnsNullAndDoesNotCreatePresenter()
        {
            UIWindow window = CreateWindow("ClassicWindow");
            int factoryCalls = 0;
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>(
                "OtherWindow",
                () =>
                {
                    factoryCalls++;
                    return new RecordingPresenter();
                });

            IUIWindowBinding binding = binder.Bind(new UIWindowBindingContext(window, _service));

            Assert.IsNull(binding);
            Assert.AreEqual(0, factoryCalls);
        }

        [Test]
        public void Bind_WhenViewBindingFails_ReleasesPresenterExactlyOnce()
        {
            UIWindow window = CreateWindow("Inventory");
            var presenter = new RecordingPresenter { ThrowOnSetView = true };
            int releaseCount = 0;
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>(
                "Inventory",
                () => presenter,
                released =>
                {
                    Assert.AreSame(presenter, released);
                    releaseCount++;
                });

            Assert.Throws<InvalidOperationException>(
                () => binder.Bind(new UIWindowBindingContext(window, _service)));

            Assert.AreEqual(1, releaseCount);
            CollectionAssert.AreEqual(new[] { "Service", "View" }, presenter.Events);
        }

        [Test]
        public void Binding_Dispose_IsIdempotentAndUsesRegisteredReleasePolicy()
        {
            UIWindow window = CreateWindow("Inventory");
            var presenter = new RecordingPresenter();
            int releaseCount = 0;
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>(
                "Inventory",
                () => presenter,
                released =>
                {
                    Assert.AreSame(presenter, released);
                    releaseCount++;
                });

            IUIWindowBinding binding = binder.Bind(new UIWindowBindingContext(window, _service));
            binding.Dispose();
            binding.Dispose();

            Assert.AreEqual(1, releaseCount);
        }

        [Test]
        public void Binding_ForwardsLifecycleInAuthoritativeOrder()
        {
            UIWindow window = CreateWindow("Inventory");
            var presenter = new RecordingPresenter();
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>("Inventory", () => presenter);
            IUIWindowBinding binding = binder.Bind(new UIWindowBindingContext(window, _service));
            presenter.Events.Clear();

            binding.OnWindowStateChanged(WindowStateCallbackType.OnStartOpen);
            binding.OnWindowStateChanged(WindowStateCallbackType.OnFinishedOpen);
            binding.OnWindowStateChanged(WindowStateCallbackType.OnStartClose);
            binding.OnWindowStateChanged(WindowStateCallbackType.OnFinishedClose);

            CollectionAssert.AreEqual(
                new[] { "Opening", "Opened", "Closing", "Closed" },
                presenter.Events);
            binding.Dispose();
        }

        [Test]
        public void ContextualFactory_ReceivesOpenDataAndLifetimeTokenBeforeViewBinding()
        {
            UIWindow window = CreateWindow("Inventory");
            var presenter = new RecordingPresenter();
            var payload = new object();
            using var lifetime = new CancellationTokenSource();
            UIWindowBindingContext observed = default;
            var binder = new UIPresenterBinder();
            binder.RegisterContextual<RecordingPresenter>(
                "Inventory",
                context =>
                {
                    observed = context;
                    return presenter;
                });
            var context = new UIWindowBindingContext(
                window,
                _service,
                "MainMenu",
                payload,
                lifetime.Token);

            IUIWindowBinding binding = binder.Bind(context);

            Assert.AreSame(window, observed.Window);
            Assert.AreSame(_service, observed.UIService);
            Assert.AreEqual("MainMenu", observed.OpenerId);
            Assert.AreSame(payload, observed.OpenContext);
            Assert.AreEqual(lifetime.Token, observed.LifetimeToken);
            Assert.IsTrue(observed.TryGetOpenContext(out object typedPayload));
            Assert.AreSame(payload, typedPayload);
            binding.Dispose();
        }

        [Test]
        public void BinderInstances_IsolateRegistrationsAndMutations()
        {
            UIWindow firstWindow = CreateWindow("SharedId");
            UIWindow secondWindow = CreateWindow("SharedId");
            var firstPresenter = new RecordingPresenter();
            var secondPresenter = new RecordingPresenter();
            var firstBinder = new UIPresenterBinder();
            var secondBinder = new UIPresenterBinder();
            firstBinder.Register<RecordingPresenter>("SharedId", () => firstPresenter);
            secondBinder.Register<RecordingPresenter>("SharedId", () => secondPresenter);

            Assert.IsTrue(firstBinder.Unregister("SharedId"));
            Assert.IsNull(firstBinder.Bind(new UIWindowBindingContext(firstWindow, _service)));
            IUIWindowBinding secondBinding = secondBinder.Bind(
                new UIWindowBindingContext(secondWindow, _service));

            Assert.IsNotNull(secondBinding);
            Assert.AreSame(secondWindow, secondPresenter.View);
            Assert.IsNull(firstPresenter.View);
            secondBinding.Dispose();
        }

        [Test]
        public async Task NavigateBack_WithNonDefaultParentOptions_PreservesActiveParentSession()
        {
            NavigationPresenter childPresenter = null;
            var binder = new UIPresenterBinder();
            binder.Register<NavigationPresenter>(
                "Child",
                () => childPresenter = new NavigationPresenter());
            UINavigationService navigation = RecreateServiceWithNavigation(binder);
            UIWindowConfiguration parentConfiguration =
                _fixture.CreateDirectConfiguration("Parent");
            UIWindowConfiguration childConfiguration =
                _fixture.CreateDirectConfiguration("Child");
            var parentContext = new object();
            var childContext = new object();
            var parentOptions = new UIOpenOptions(
                context: parentContext,
                sceneBoundOverride: false,
                assetLoadContext: new UIAssetLoadContext("parent-assets"),
                suppressWindowTransition: true);

            UIWindow parent = await _service.OpenAsync(parentConfiguration, parentOptions);
            await _service.OpenAsync(
                childConfiguration,
                new UIOpenOptions(openerId: "Parent", context: childContext));

            Assert.IsNotNull(childPresenter);
            Assert.AreSame(parentContext, navigation.GetContext("Parent"));

            await childPresenter.GoBackAsync();

            Assert.IsTrue(_service.TryGetWindow("Parent", out UIWindow activeParent));
            Assert.AreSame(parent, activeParent);
            Assert.IsFalse(_service.TryGetWindow("Child", out _));
            Assert.AreSame(parentContext, navigation.GetContext("Parent"));
            Assert.AreEqual("Parent", navigation.CurrentWindow);
            Assert.AreEqual(1, _service.ActiveWindowCount);
        }

        [Test]
        public async Task NavigateBack_DeepChain_ClosesExactlyOneActiveLevelPerCall()
        {
            NavigationPresenter middlePresenter = null;
            NavigationPresenter leafPresenter = null;
            var binder = new UIPresenterBinder();
            binder.Register<NavigationPresenter>(
                "Middle",
                () => middlePresenter = new NavigationPresenter());
            binder.Register<NavigationPresenter>(
                "Leaf",
                () => leafPresenter = new NavigationPresenter());
            UINavigationService navigation = RecreateServiceWithNavigation(binder);
            UIWindowConfiguration rootConfiguration =
                _fixture.CreateDirectConfiguration("Root");
            UIWindowConfiguration middleConfiguration =
                _fixture.CreateDirectConfiguration("Middle");
            UIWindowConfiguration leafConfiguration =
                _fixture.CreateDirectConfiguration("Leaf");
            var rootContext = new object();
            var middleContext = new object();
            var leafContext = new object();

            UIWindow root = await _service.OpenAsync(
                rootConfiguration,
                new UIOpenOptions(
                    context: rootContext,
                    assetLoadContext: new UIAssetLoadContext("root-assets")));
            UIWindow middle = await _service.OpenAsync(
                middleConfiguration,
                new UIOpenOptions(
                    openerId: "Root",
                    context: middleContext,
                    sceneBoundOverride: false,
                    assetLoadContext: new UIAssetLoadContext("middle-assets"),
                    suppressWindowTransition: true));
            await _service.OpenAsync(
                leafConfiguration,
                new UIOpenOptions(openerId: "Middle", context: leafContext));

            Assert.IsNotNull(middlePresenter);
            Assert.IsNotNull(leafPresenter);

            await leafPresenter.GoBackAsync();

            Assert.IsTrue(_service.TryGetWindow("Middle", out UIWindow activeMiddle));
            Assert.AreSame(middle, activeMiddle);
            Assert.IsTrue(_service.TryGetWindow("Root", out UIWindow activeRoot));
            Assert.AreSame(root, activeRoot);
            Assert.IsFalse(_service.TryGetWindow("Leaf", out _));
            Assert.AreSame(middleContext, navigation.GetContext("Middle"));
            Assert.AreEqual("Middle", navigation.CurrentWindow);

            await middlePresenter.GoBackAsync();

            Assert.IsTrue(_service.TryGetWindow("Root", out activeRoot));
            Assert.AreSame(root, activeRoot);
            Assert.IsFalse(_service.TryGetWindow("Middle", out _));
            Assert.AreSame(rootContext, navigation.GetContext("Root"));
            Assert.AreEqual("Root", navigation.CurrentWindow);
            Assert.AreEqual(1, _service.ActiveWindowCount);
        }

        [Test]
        public async Task NavigateBack_WithoutActiveOpener_ClosesCurrentWindow()
        {
            NavigationPresenter rootPresenter = null;
            var binder = new UIPresenterBinder();
            binder.Register<NavigationPresenter>(
                "Root",
                () => rootPresenter = new NavigationPresenter());
            UINavigationService navigation = RecreateServiceWithNavigation(binder);
            UIWindowConfiguration rootConfiguration =
                _fixture.CreateDirectConfiguration("Root");

            await _service.OpenAsync(
                rootConfiguration,
                new UIOpenOptions(context: new object()));

            Assert.IsNotNull(rootPresenter);
            Assert.IsNull(navigation.ResolveBackTarget("Root"));

            await rootPresenter.GoBackAsync();

            Assert.IsFalse(_service.TryGetWindow("Root", out _));
            Assert.IsNull(navigation.CurrentWindow);
            Assert.AreEqual(0, _service.ActiveWindowCount);
        }

        [Test]
        public void Register_RejectsDuplicateMappingAndInvalidDelegates()
        {
            var binder = new UIPresenterBinder();
            binder.Register<RecordingPresenter>("Inventory", () => new RecordingPresenter());

            Assert.Throws<ArgumentException>(
                () => binder.Register<RecordingPresenter>(string.Empty, () => new RecordingPresenter()));
            Assert.Throws<ArgumentNullException>(
                () => binder.Register<RecordingPresenter>("NullFactory", null));
            Assert.Throws<ArgumentException>(
                () => binder.Register<RecordingPresenter>("Inventory", () => new RecordingPresenter()));
        }

        private UIWindow CreateWindow(string windowId)
        {
            GameObject windowObject = new GameObject(windowId, typeof(RectTransform));
            UIWindow window = windowObject.AddComponent<UIWindow>();
            TestReflection.SetField(window, "_windowId", windowId);
            _windowObjects.Add(windowObject);
            return window;
        }

        private UINavigationService RecreateServiceWithNavigation(UIPresenterBinder binder)
        {
            _service.Dispose();
            var navigation = new UINavigationService();
            _service = new UIService(
                _fixture.Root,
                options: new UIServiceOptions
                {
                    NavigationService = navigation,
                    MaxInstantiatesPerFrame = 8,
                },
                binders: new IUIWindowBinder[] { binder });
            return navigation;
        }

        private sealed class NavigationPresenter : UIPresenter<UIWindow>
        {
            public UniTask GoBackAsync(
                ChildClosePolicy policy = ChildClosePolicy.Reparent,
                CancellationToken cancellationToken = default)
            {
                return NavigateBackAsync(policy, cancellationToken);
            }
        }

        private sealed class RecordingPresenter : IUIPresenter
        {
            public readonly List<string> Events = new List<string>(8);

            public bool ThrowOnSetView { get; set; }
            public UIWindow View { get; private set; }
            public IUIService UIService { get; private set; }

            public void SetView(UIWindow view)
            {
                Events.Add("View");
                if (ThrowOnSetView)
                {
                    throw new InvalidOperationException("View binding failed.");
                }

                View = view;
            }

            public void SetUIService(IUIService uiService)
            {
                Events.Add("Service");
                UIService = uiService;
            }

            public void OnViewOpening() => Events.Add("Opening");
            public void OnViewOpened() => Events.Add("Opened");
            public void OnViewClosing() => Events.Add("Closing");
            public void OnViewClosed() => Events.Add("Closed");
            public void Dispose() => Events.Add("Disposed");
        }
    }
}
