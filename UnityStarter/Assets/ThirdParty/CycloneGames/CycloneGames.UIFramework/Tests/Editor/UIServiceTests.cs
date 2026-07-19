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
    public sealed class UIServiceTests
    {
        private readonly List<UIService> _services = new List<UIService>(4);
        private UIRuntimeTestFixture _fixture;

        private sealed class CallbackBinder : IUIWindowBinder
        {
            private readonly WindowStateCallbackType _trigger;
            private readonly Action<UIWindowBindingContext> _callback;
            private readonly bool _throwOnCallback;
            private readonly bool _throwOnDispose;

            public CallbackBinder(
                WindowStateCallbackType trigger,
                Action<UIWindowBindingContext> callback = null,
                bool throwOnCallback = false,
                bool throwOnDispose = false)
            {
                _trigger = trigger;
                _callback = callback;
                _throwOnCallback = throwOnCallback;
                _throwOnDispose = throwOnDispose;
            }

            public int DisposeCount { get; private set; }

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(this, context);
            }

            private sealed class Binding : IUIWindowBinding
            {
                private CallbackBinder _owner;
                private readonly UIWindowBindingContext _context;
                private bool _invoked;

                public Binding(CallbackBinder owner, UIWindowBindingContext context)
                {
                    _owner = owner;
                    _context = context;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                    CallbackBinder owner = _owner;
                    if (owner == null || _invoked || state != owner._trigger)
                    {
                        return;
                    }

                    _invoked = true;
                    if (owner._throwOnCallback)
                    {
                        throw new InvalidOperationException("Test binding callback failed.");
                    }

                    owner._callback?.Invoke(_context);
                }

                public void Dispose()
                {
                    CallbackBinder owner = _owner;
                    if (owner == null)
                    {
                        return;
                    }

                    _owner = null;
                    owner.DisposeCount++;
                    if (owner._throwOnDispose)
                    {
                        throw new InvalidOperationException("Test binding disposal failed.");
                    }
                }
            }
        }

        private sealed class ThrowingCloseWindow : UIWindow
        {
            protected override void OnClosing()
            {
                throw new InvalidOperationException("Test OnClosing failed.");
            }

            protected override void OnClosed()
            {
                throw new InvalidOperationException("Test OnClosed failed.");
            }
        }

        private sealed class AsyncGateBinder : IUIWindowBinder
        {
            private readonly UniTaskCompletionSource _gate = new UniTaskCompletionSource();
            private readonly WindowStateCallbackType _gatedCallback;

            public AsyncGateBinder(
                WindowStateCallbackType gatedCallback = WindowStateCallbackType.OnStartOpen)
            {
                _gatedCallback = gatedCallback;
            }

            public int AsyncCallbackCount { get; private set; }
            public int DisposeCount { get; private set; }

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(this);
            }

            public void Release() => _gate.TrySetResult();

            private sealed class Binding : IAsyncUIWindowBinding
            {
                private AsyncGateBinder _owner;

                public Binding(AsyncGateBinder owner)
                {
                    _owner = owner;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                    throw new InvalidOperationException(
                        "The synchronous callback must not run for an async binding.");
                }

                public async UniTask OnWindowStateChangedAsync(
                    WindowStateCallbackType state,
                    CancellationToken cancellationToken)
                {
                    _owner.AsyncCallbackCount++;
                    if (state == _owner._gatedCallback)
                    {
                        await _owner._gate.Task.AttachExternalCancellation(cancellationToken);
                    }
                }

                public void Dispose()
                {
                    AsyncGateBinder owner = _owner;
                    if (owner == null)
                    {
                        return;
                    }

                    _owner = null;
                    owner.DisposeCount++;
                }
            }
        }

        private sealed class GatedCloseTransitionDriver : IUIWindowTransitionDriver
        {
            private readonly UniTaskCompletionSource _closeGate = new UniTaskCompletionSource();

            public UniTask PlayOpenAsync(UIWindow window, CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask PlayCloseAsync(UIWindow window, CancellationToken cancellationToken)
            {
                return _closeGate.Task.AttachExternalCancellation(cancellationToken);
            }

            public void Release() => _closeGate.TrySetResult();
        }

        private sealed class ThrowingCloseTransitionDriver : IUIWindowTransitionDriver
        {
            public UniTask PlayOpenAsync(
                UIWindow window,
                CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask PlayCloseAsync(
                UIWindow window,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Test close transition failed.");
            }
        }

        private sealed class LifecycleTraceWindow : UIWindow
        {
            public static List<string> Events { get; set; }

            protected override void OnOpening()
            {
                Events?.Add("Window:OnOpening:" + State);
            }

            protected override void OnOpened()
            {
                Events?.Add("Window:OnOpened:" + State);
            }

            protected override void OnClosing()
            {
                Events?.Add("Window:OnClosing:" + State);
            }

            protected override void OnClosed()
            {
                Events?.Add("Window:OnClosed:" + State);
            }
        }

        private sealed class LifecycleTraceBinder : IUIWindowBinder
        {
            private readonly List<string> _events;

            public LifecycleTraceBinder(List<string> events)
            {
                _events = events;
            }

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(context.Window, _events);
            }

            private sealed class Binding : IUIWindowBinding
            {
                private readonly UIWindow _window;
                private readonly List<string> _events;

                public Binding(UIWindow window, List<string> events)
                {
                    _window = window;
                    _events = events;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                    _events.Add("Binding:" + state + ":" + _window.State);
                }

                public void Dispose()
                {
                    _events.Add("Binding:Dispose:" + _window.State);
                }
            }
        }

        private sealed class LifecycleTraceTransitionDriver : IUIWindowTransitionDriver
        {
            private readonly List<string> _events;

            public LifecycleTraceTransitionDriver(List<string> events)
            {
                _events = events;
            }

            public UniTask PlayOpenAsync(UIWindow window, CancellationToken cancellationToken)
            {
                _events.Add("Transition:Open:" + window.State);
                return UniTask.CompletedTask;
            }

            public UniTask PlayCloseAsync(UIWindow window, CancellationToken cancellationToken)
            {
                _events.Add("Transition:Close:" + window.State);
                return UniTask.CompletedTask;
            }
        }

        private sealed class BindingContextCaptureBinder : IUIWindowBinder
        {
            public UIWindowBindingContext CapturedContext { get; private set; }
            public bool LifetimeCanceledDuringDispose { get; private set; }

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                CapturedContext = context;
                return new Binding(this, context.LifetimeToken);
            }

            private sealed class Binding : IUIWindowBinding
            {
                private BindingContextCaptureBinder _owner;
                private readonly CancellationToken _lifetimeToken;

                public Binding(
                    BindingContextCaptureBinder owner,
                    CancellationToken lifetimeToken)
                {
                    _owner = owner;
                    _lifetimeToken = lifetimeToken;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                }

                public void Dispose()
                {
                    BindingContextCaptureBinder owner = _owner;
                    if (owner == null)
                    {
                        return;
                    }

                    _owner = null;
                    owner.LifetimeCanceledDuringDispose =
                        _lifetimeToken.IsCancellationRequested;
                }
            }
        }

        private sealed class GatedCoordinator : IUITransitionCoordinator
        {
            private readonly UniTaskCompletionSource _gate = new UniTaskCompletionSource();

            public CancellationToken ObservedToken { get; private set; }

            public UniTask TransitionAsync(
                UIWindow leaving,
                UIWindow entering,
                NavigationDirection direction,
                CancellationToken cancellationToken = default)
            {
                ObservedToken = cancellationToken;
                return _gate.Task.AttachExternalCancellation(cancellationToken);
            }

            public void Release() => _gate.TrySetResult();
        }

        private sealed class ThrowingNavigationService : IUINavigationService
        {
            private readonly UINavigationService _inner = new UINavigationService();

            public bool ThrowOnUnregister { get; set; }
            public string CurrentWindow => _inner.CurrentWindow;
            public bool CanNavigateBack => _inner.CanNavigateBack;

            public bool Register(string windowId, string openerId = null, object context = null)
                => _inner.Register(windowId, openerId, context);

            public bool Unregister(
                string windowId,
                ChildClosePolicy policy,
                List<string> affectedWindowIds)
            {
                if (ThrowOnUnregister)
                {
                    throw new InvalidOperationException("Test navigation unregister failed.");
                }

                return _inner.Unregister(windowId, policy, affectedWindowIds);
            }

            public void Clear() => _inner.Clear();
            public string GetOpener(string windowId) => _inner.GetOpener(windowId);
            public object GetContext(string windowId) => _inner.GetContext(windowId);
            public string ResolveBackTarget(string windowId) => _inner.ResolveBackTarget(windowId);
            public int CopyAncestors(string windowId, List<string> destination)
                => _inner.CopyAncestors(windowId, destination);
            public int CopyChildren(string windowId, List<string> destination)
                => _inner.CopyChildren(windowId, destination);
            public int CopyHistory(List<UINavigationEntry> destination)
                => _inner.CopyHistory(destination);
        }

        [SetUp]
        public void SetUp()
        {
            _fixture = new UIRuntimeTestFixture();
        }

        [TearDown]
        public void TearDown()
        {
            LifecycleTraceWindow.Events = null;
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                _services[i]?.Dispose();
            }

            _services.Clear();
            _fixture?.Dispose();
        }

        [Test]
        public async Task OpenAndClose_DirectConfiguration_CommitsAndReleasesSession()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory", priority: 20);
            var navigation = new UINavigationService();
            UIService service = CreateService(
                options: new UIServiceOptions { NavigationService = navigation });

            UIWindow window = await service.OpenAsync(
                configuration,
                new UIOpenOptions(context: "context"));

            Assert.AreEqual(UIWindowState.Open, window.State);
            Assert.AreEqual("Inventory", window.WindowId);
            Assert.AreEqual(1, service.ActiveWindowCount);
            Assert.AreEqual("Inventory", navigation.CurrentWindow);
            Assert.AreEqual("context", navigation.GetContext("Inventory"));
            Assert.IsTrue(service.TryGetWindow("Inventory", out UIWindow found));
            Assert.AreSame(window, found);

            Assert.IsTrue(await service.CloseAsync("Inventory"));
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.IsFalse(service.TryGetWindow("Inventory", out _));
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.IsFalse(await service.CloseAsync("Inventory"));
        }

        [Test]
        public async Task CanvasIsolation_IsAppliedOnlyWhenExplicitlyConfigured()
        {
            UIWindowConfiguration inherited =
                _fixture.CreateDirectConfiguration("ScrollableInventory");
            inherited.WindowPrefab.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            UIService service = CreateService();

            UIWindow inheritedWindow = await service.OpenAsync(inherited);

            Assert.IsNull(inheritedWindow.GetComponent<Canvas>());
            Assert.AreEqual(0, service.GetPerformanceStats().IsolatedWindowCanvasCount);
            Assert.IsTrue(await service.CloseAsync(inherited.WindowId));

            UIWindowConfiguration isolated =
                _fixture.CreateDirectConfiguration("IsolatedInventory");
            TestReflection.SetField(
                isolated,
                "subCanvasPolicy",
                UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas);

            UIWindow isolatedWindow = await service.OpenAsync(isolated);

            Assert.IsNotNull(isolatedWindow.GetComponent<Canvas>());
            Assert.IsNotNull(isolatedWindow.GetComponent<UnityEngine.UI.GraphicRaycaster>());
            Assert.AreEqual(1, service.GetPerformanceStats().IsolatedWindowCanvasCount);
        }

        [Test]
        public async Task DuplicateOpen_JoinsSingleFlightAndAcquiresConfigurationOnce()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var provider = new RecordingAssetProvider
            {
                ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>(),
            };
            UIService service = CreateService(provider);

            UniTask<UIWindow> firstOpen = service.OpenAsync("Inventory");
            UniTask<UIWindow> secondOpen = service.OpenAsync("Inventory");

            Assert.AreEqual(1, provider.ConfigurationAcquireCount);
            provider.ConfigurationGate.TrySetResult(configurationLease);
            UIWindow first = await firstOpen;
            UIWindow second = await secondOpen;

            Assert.AreSame(first, second);
            Assert.AreEqual(1, service.ActiveWindowCount);
            Assert.IsTrue(await service.CloseAsync("Inventory"));
            Assert.AreEqual(1, configurationLease.DisposeCount);
        }

        [Test]
        public async Task ProviderOpen_RejectsExplicitConfigurationJoinBeforeResolution()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var provider = new RecordingAssetProvider
            {
                ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>(),
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> providerOpen = service.OpenAsync("Inventory");

            Assert.Throws<InvalidOperationException>(() =>
                service.OpenAsync(configuration));

            provider.ConfigurationGate.TrySetResult(configurationLease);
            await providerOpen;
            await service.CloseAsync("Inventory");
            Assert.AreEqual(1, configurationLease.DisposeCount);
        }

        [Test]
        public async Task ExistingWindow_RejectsJoinWithDifferentOpenOptions()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory");
            UIService service = CreateService();
            await service.OpenAsync(configuration);

            Assert.Throws<InvalidOperationException>(() =>
                service.OpenAsync(
                    configuration,
                    new UIOpenOptions(sceneBoundOverride: true)));
        }

        [Test]
        public async Task Bindings_ReceiveLifecycleInCreationOrderAndDisposeInReverse()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory");
            var events = new List<string>(16);
            var first = new RecordingWindowBinder("First", events);
            var second = new RecordingWindowBinder("Second", events);
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { first, second });

            await service.OpenAsync(configuration);
            await service.CloseAsync("Inventory");

            CollectionAssert.AreEqual(
                new[]
                {
                    "First:Bind",
                    "Second:Bind",
                    "First:OnStartOpen",
                    "Second:OnStartOpen",
                    "First:OnFinishedOpen",
                    "Second:OnFinishedOpen",
                    "First:OnStartClose",
                    "Second:OnStartClose",
                    "First:OnFinishedClose",
                    "Second:OnFinishedClose",
                    "Second:Dispose",
                    "First:Dispose",
                },
                events);
        }

        [Test]
        public async Task StateMachine_ExposesDeterministicLifecycleInjectionOrder()
        {
            var events = new List<string>(12);
            LifecycleTraceWindow.Events = events;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<LifecycleTraceWindow>("Lifecycle");
            UIService service = CreateService(
                options: new UIServiceOptions
                {
                    DefaultTransitionDriver = new LifecycleTraceTransitionDriver(events),
                },
                binders: new IUIWindowBinder[] { new LifecycleTraceBinder(events) });

            UIWindow window = await service.OpenAsync(configuration);
            Assert.AreEqual(UIWindowState.Open, window.State);
            Assert.IsTrue(await service.CloseAsync("Lifecycle"));

            CollectionAssert.AreEqual(
                new[]
                {
                    "Binding:OnStartOpen:Created",
                    "Window:OnOpening:Opening",
                    "Transition:Open:Opening",
                    "Window:OnOpened:Open",
                    "Binding:OnFinishedOpen:Open",
                    "Binding:OnStartClose:Open",
                    "Window:OnClosing:Closing",
                    "Transition:Close:Closing",
                    "Window:OnClosed:Closed",
                    "Binding:OnFinishedClose:Closed",
                    "Binding:Dispose:Closed",
                },
                events);
        }

        [Test]
        public async Task CloseHookFailure_ForceClosesBeforeFinishedCloseNotification()
        {
            var events = new List<string>(12);
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<ThrowingCloseWindow>("ThrowingHook");
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { new LifecycleTraceBinder(events) });
            await service.OpenAsync(configuration);

            Assert.CatchAsync<Exception>(async () =>
                await service.CloseAsync("ThrowingHook"));

            CollectionAssert.Contains(events, "Binding:OnFinishedClose:Closed");
            CollectionAssert.DoesNotContain(events, "Binding:OnFinishedClose:Closing");
            CollectionAssert.Contains(events, "Binding:Dispose:Closed");
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task CloseDriverFailure_ForceClosesBeforeFinishedCloseNotification()
        {
            var events = new List<string>(12);
            LifecycleTraceWindow.Events = events;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<LifecycleTraceWindow>("ThrowingDriver");
            UIService service = CreateService(
                options: new UIServiceOptions
                {
                    DefaultTransitionDriver = new ThrowingCloseTransitionDriver(),
                },
                binders: new IUIWindowBinder[] { new LifecycleTraceBinder(events) });
            await service.OpenAsync(configuration);

            Assert.CatchAsync<InvalidOperationException>(async () =>
                await service.CloseAsync("ThrowingDriver"));

            CollectionAssert.Contains(events, "Window:OnClosed:Closed");
            CollectionAssert.Contains(events, "Binding:OnFinishedClose:Closed");
            CollectionAssert.DoesNotContain(events, "Binding:OnFinishedClose:Closing");
            CollectionAssert.Contains(events, "Binding:Dispose:Closed");
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task BindingContext_ExposesOpenDataAndCancelsLifetimeBeforeDisposal()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("ContextWindow");
            var payload = new object();
            var binder = new BindingContextCaptureBinder();
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { binder });

            UIWindow window = await service.OpenAsync(
                configuration,
                new UIOpenOptions(openerId: "MainMenu", context: payload));

            Assert.AreSame(window, binder.CapturedContext.Window);
            Assert.AreSame(service, binder.CapturedContext.UIService);
            Assert.AreEqual("MainMenu", binder.CapturedContext.OpenerId);
            Assert.AreSame(payload, binder.CapturedContext.OpenContext);
            Assert.IsFalse(binder.CapturedContext.LifetimeToken.IsCancellationRequested);
            Assert.IsTrue(
                binder.CapturedContext.TryGetOpenContext(out object typedPayload));
            Assert.AreSame(payload, typedPayload);

            Assert.IsTrue(await service.CloseAsync("ContextWindow"));
            Assert.IsTrue(binder.LifetimeCanceledDuringDispose);
            Assert.IsTrue(binder.CapturedContext.LifetimeToken.IsCancellationRequested);
        }

        [Test]
        public void BinderFailure_RollsBackPreviouslyCreatedBindingsAndSession()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("Inventory");
            var events = new List<string>(8);
            var first = new RecordingWindowBinder("First", events);
            var failing = new RecordingWindowBinder("Failing", events, throwOnBind: true);
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { first, failing });

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.OpenAsync(configuration));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            CollectionAssert.AreEqual(
                new[] { "First:Bind", "Failing:Bind", "First:Dispose" },
                events);
        }

        [Test]
        public async Task Capacity_IsEnforcedUntilOwnedSessionCloses()
        {
            UIWindowConfiguration first = _fixture.CreateDirectConfiguration("First");
            UIWindowConfiguration second = _fixture.CreateDirectConfiguration("Second");
            UIService service = CreateService(
                options: new UIServiceOptions
                {
                    InitialWindowCapacity = 1,
                    MaxActiveWindows = 1,
                    MaxInstantiatesPerFrame = 1,
                });
            await service.OpenAsync(first);

            Assert.Throws<InvalidOperationException>(() => service.OpenAsync(second));

            await service.CloseAsync("First");
            UIWindow secondWindow = await service.OpenAsync(second);
            Assert.AreEqual("Second", secondWindow.WindowId);
            await service.CloseAsync("Second");
        }

        [Test]
        public async Task Dispose_IsIdempotentAndReleasesEveryOwnedSession()
        {
            UIWindowConfiguration first = _fixture.CreateDirectConfiguration("First");
            UIWindowConfiguration second = _fixture.CreateDirectConfiguration("Second");
            var navigation = new UINavigationService();
            UIService service = CreateService(
                options: new UIServiceOptions
                {
                    NavigationService = navigation,
                    MaxInstantiatesPerFrame = 2,
                });
            await service.OpenAsync(first);
            await service.OpenAsync(second);

            service.Dispose();
            service.Dispose();

            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.IsNull(navigation.CurrentWindow);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.Throws<ObjectDisposedException>(() => service.OpenAsync(first));
        }

        [Test]
        public async Task PublicOperations_FromNonOwnerThreadFailBeforeTouchingUnityObjects()
        {
            UIService service = CreateService();

            Exception exception = await Task.Run(() =>
            {
                try
                {
                    service.TryGetWindow("Inventory", out _);
                    return null;
                }
                catch (Exception captured)
                {
                    return captured;
                }
            });

            Assert.IsInstanceOf<InvalidOperationException>(exception);
        }

        [Test]
        public async Task ProviderLeases_AreReleasedExactlyOnceAfterClose()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("Inventory", "ui/inventory");
            GameObject prefab = _fixture.CreateWindowPrefab("Inventory");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var prefabLease = new TestAssetLease<GameObject>(prefab);
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabLease = prefabLease,
            };
            UIService service = CreateService(provider);

            await service.OpenAsync("Inventory");
            await service.CloseAsync("Inventory");
            service.Dispose();

            Assert.AreEqual(1, provider.ConfigurationAcquireCount);
            Assert.AreEqual(1, provider.PrefabAcquireCount);
            Assert.AreEqual("Inventory", provider.LastWindowId);
            Assert.AreEqual("ui/inventory", provider.LastPrefabReference.Location);
            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(1, prefabLease.DisposeCount);
        }

        [Test]
        public void InvalidProviderPrefab_RollsBackBothLeases()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("Inventory");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var prefabLease = new TestAssetLease<GameObject>(
                _fixture.CreateInvalidPrefab("InvalidPrefab"));
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabLease = prefabLease,
            };
            UIService service = CreateService(provider);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.OpenAsync("Inventory"));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(1, prefabLease.DisposeCount);
        }

        [Test]
        public void CanceledOpenTransition_RollsBackWindowAndProviderLeases()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("Inventory");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var prefabLease = new TestAssetLease<GameObject>(
                _fixture.CreateWindowPrefab("Inventory"));
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabLease = prefabLease,
            };
            UIService service = CreateService(provider);

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.OpenAsync(
                    "Inventory",
                    new UIOpenOptions(transitionDriver: new CancelOpenTransitionDriver())));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(1, prefabLease.DisposeCount);
        }

        [Test]
        public async Task CopyActiveWindows_ClearsAndReusesCallerBufferInOpenOrder()
        {
            UIService service = CreateService(
                options: new UIServiceOptions { MaxInstantiatesPerFrame = 2 });
            UIWindow first = await service.OpenAsync(
                _fixture.CreateDirectConfiguration("First"));
            UIWindow second = await service.OpenAsync(
                _fixture.CreateDirectConfiguration("Second"));
            var destination = new List<UIWindow>(4) { null };
            int capacity = destination.Capacity;

            Assert.AreEqual(2, service.CopyActiveWindows(destination));

            Assert.AreEqual(capacity, destination.Capacity);
            CollectionAssert.AreEqual(new[] { first, second }, destination);
        }

        [Test]
        public async Task CoordinatedNavigation_CommitsEnteringBeforeClosingLeaving()
        {
            UIWindowConfiguration leavingConfiguration =
                _fixture.CreateDirectConfiguration("Leaving");
            UIWindowConfiguration enteringConfiguration =
                _fixture.CreateDirectConfiguration("Entering");
            var enteringLease =
                new TestAssetLease<UIWindowConfiguration>(enteringConfiguration);
            var provider = new RecordingAssetProvider { ConfigurationLease = enteringLease };
            var coordinator = new RecordingTransitionCoordinator();
            UIService service = CreateService(provider);
            UIWindow leaving = await service.OpenAsync(leavingConfiguration);

            UIWindow entering = await service.NavigateAsync(
                "Leaving",
                "Entering",
                coordinator,
                NavigationDirection.Replace);

            Assert.AreEqual(1, coordinator.CallCount);
            Assert.AreSame(leaving, coordinator.Leaving);
            Assert.AreSame(entering, coordinator.Entering);
            Assert.AreEqual(NavigationDirection.Replace, coordinator.Direction);
            Assert.IsFalse(service.TryGetWindow("Leaving", out _));
            Assert.IsTrue(service.TryGetWindow("Entering", out UIWindow active));
            Assert.AreSame(entering, active);
            Assert.AreEqual(1, service.ActiveWindowCount);

            await service.CloseAsync("Entering");
            Assert.AreEqual(1, enteringLease.DisposeCount);
        }

        [Test]
        public async Task CanceledCoordinatedNavigation_RollsBackEnteringAndKeepsLeaving()
        {
            UIWindowConfiguration leavingConfiguration =
                _fixture.CreateDirectConfiguration("Leaving");
            UIWindowConfiguration enteringConfiguration =
                _fixture.CreateDirectConfiguration("Entering");
            var enteringLease =
                new TestAssetLease<UIWindowConfiguration>(enteringConfiguration);
            var provider = new RecordingAssetProvider { ConfigurationLease = enteringLease };
            var coordinator = new RecordingTransitionCoordinator(cancel: true);
            UIService service = CreateService(provider);
            UIWindow leaving = await service.OpenAsync(leavingConfiguration);

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.NavigateAsync("Leaving", "Entering", coordinator));

            Assert.IsTrue(service.TryGetWindow("Leaving", out UIWindow activeLeaving));
            Assert.AreSame(leaving, activeLeaving);
            Assert.IsFalse(service.TryGetWindow("Entering", out _));
            Assert.AreEqual(1, service.ActiveWindowCount);
            Assert.AreEqual(1, enteringLease.DisposeCount);
        }

        [Test]
        public async Task CallerCancellationAfterNavigationCommit_KeepsEnteringWindow()
        {
            UIWindowConfiguration leavingConfiguration =
                _fixture.CreateDirectConfiguration("Leaving");
            UIWindowConfiguration enteringConfiguration =
                _fixture.CreateDirectConfiguration("Entering");
            var enteringLease =
                new TestAssetLease<UIWindowConfiguration>(enteringConfiguration);
            var provider = new RecordingAssetProvider { ConfigurationLease = enteringLease };
            using var callerCancellation = new CancellationTokenSource();
            var cancelAfterLeavingClose = new CallbackBinder(
                WindowStateCallbackType.OnFinishedClose,
                context =>
                {
                    if (context.Window.WindowId == "Leaving")
                    {
                        callerCancellation.Cancel();
                    }
                });
            UIService service = CreateService(
                provider,
                binders: new IUIWindowBinder[] { cancelAfterLeavingClose });
            await service.OpenAsync(leavingConfiguration);

            UIWindow entering = await service.NavigateAsync(
                "Leaving",
                "Entering",
                new RecordingTransitionCoordinator(),
                cancellationToken: callerCancellation.Token);

            Assert.IsTrue(callerCancellation.IsCancellationRequested);
            Assert.IsFalse(service.TryGetWindow("Leaving", out _));
            Assert.IsTrue(service.TryGetWindow("Entering", out UIWindow activeEntering));
            Assert.AreSame(entering, activeEntering);
            Assert.AreEqual(1, service.ActiveWindowCount);
            Assert.AreEqual(0, enteringLease.DisposeCount);

            await service.CloseAsync("Entering");
            Assert.AreEqual(1, enteringLease.DisposeCount);
        }

        [Test]
        public async Task ConstructionFromWorkerThread_FailsBeforeAcceptingUnityOwnership()
        {
            Exception exception = await Task.Run(() =>
            {
                try
                {
                    _ = new UIService(_fixture.Root);
                    return null;
                }
                catch (Exception caught)
                {
                    return caught;
                }
            });

            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task ProviderIgnoringCancellation_LateLeaseIsDisposedOnOwnerThread()
        {
            int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("LateConfiguration");
            var lease = new TestAssetLease<UIWindowConfiguration>(configuration);
            var provider = new RecordingAssetProvider
            {
                ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>(),
                IgnoreConfigurationCancellation = true,
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> open = service.OpenAsync("LateConfiguration");

            service.Dispose();
            await Task.Run(() => provider.ConfigurationGate.TrySetResult(lease));
            for (int i = 0; i < 8 && lease.DisposeCount == 0; i++)
            {
                await UniTask.Yield();
            }

            Assert.CatchAsync<OperationCanceledException>(async () => await open);
            Assert.AreEqual(1, lease.DisposeCount);
            Assert.AreEqual(ownerThreadId, lease.DisposeThreadId);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task ProviderFaultFromWorker_CleansPreviouslyOwnedLeaseOnMainThread()
        {
            int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("WorkerFault");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabGate = new UniTaskCompletionSource<IUIAssetLease<GameObject>>(),
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> open = service.OpenAsync("WorkerFault");

            await Task.Run(() => provider.PrefabGate.TrySetException(
                new InvalidOperationException("Worker provider failure.")));
            Assert.CatchAsync<InvalidOperationException>(async () => await open);

            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(ownerThreadId, configurationLease.DisposeThreadId);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public void CloseRequestedFromOpeningCallback_DoesNotLeaveAStuckSession()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("ReentrantClose");
            var binder = new CallbackBinder(
                WindowStateCallbackType.OnStartOpen,
                context => context.UIService.CloseAsync(context.Window.WindowId).Forget());
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.OpenAsync(configuration));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(1, binder.DisposeCount);
        }

        [Test]
        public void DisposeFromFinishedOpenCallback_DoesNotResurrectTheSession()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("ReentrantDispose");
            var binder = new CallbackBinder(
                WindowStateCallbackType.OnFinishedOpen,
                context => context.UIService.Dispose());
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.OpenAsync(configuration));

            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(1, binder.DisposeCount);
        }

        [Test]
        public void CloseFromFirstFinishedOpenBinding_StopsDispatchBeforeDisposedBinding()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("ReentrantCloseDispatch");
            int secondCallbackCount = 0;
            var first = new CallbackBinder(
                WindowStateCallbackType.OnFinishedOpen,
                context => context.UIService.CloseAsync(context.Window.WindowId).Forget());
            var second = new CallbackBinder(
                WindowStateCallbackType.OnFinishedOpen,
                _ => secondCallbackCount++);
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { first, second });

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.OpenAsync(configuration));

            Assert.AreEqual(0, secondCallbackCount);
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreEqual(1, second.DisposeCount);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
        }

        [Test]
        public async Task CloseFromFinishedOpenBinding_IsDeferredUntilTheOpenStageStopsDispatching()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("DeferredLifecycleClose");
            var transition = new GatedCloseTransitionDriver();
            var events = new List<string>(8);
            var first = new CallbackBinder(
                WindowStateCallbackType.OnFinishedOpen,
                context => context.UIService.CloseAsync(context.Window.WindowId).Forget());
            var second = new RecordingWindowBinder("Second", events);
            UIService service = CreateService(
                options: new UIServiceOptions { DefaultTransitionDriver = transition },
                binders: new IUIWindowBinder[] { first, second });

            UniTask<UIWindow> open = service.OpenAsync(configuration);
            Assert.CatchAsync<OperationCanceledException>(async () => await open);
            UniTask<bool> close = service.CloseAsync(configuration.WindowId);

            Assert.AreEqual(UniTaskStatus.Pending, close.Status);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Second:Bind",
                    "Second:OnStartOpen",
                    "Second:OnStartClose",
                },
                events);

            transition.Release();
            Assert.IsTrue(await close);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Second:Bind",
                    "Second:OnStartOpen",
                    "Second:OnStartClose",
                    "Second:OnFinishedClose",
                    "Second:Dispose",
                },
                events);
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
        }

        [Test]
        public async Task DisposeFromFirstStartCloseBinding_StopsDispatchBeforeDisposedBinding()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("ReentrantDisposeDispatch");
            int secondCallbackCount = 0;
            var first = new CallbackBinder(
                WindowStateCallbackType.OnStartClose,
                context => context.UIService.Dispose());
            var second = new CallbackBinder(
                WindowStateCallbackType.OnStartClose,
                _ => secondCallbackCount++);
            UIService service = CreateService(
                binders: new IUIWindowBinder[] { first, second });
            await service.OpenAsync(configuration);

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.CloseAsync(configuration.WindowId));

            Assert.AreEqual(0, secondCallbackCount);
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreEqual(1, second.DisposeCount);
            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
        }

        [Test]
        public void BindingInitializationFailure_RollsBackCommittedOpenTransaction()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("PresenterFailure");
            var binder = new CallbackBinder(
                WindowStateCallbackType.OnFinishedOpen,
                throwOnCallback: true);
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });

            Assert.CatchAsync<InvalidOperationException>(async () =>
                await service.OpenAsync(configuration));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(1, binder.DisposeCount);
        }

        [Test]
        public async Task AsyncLifecycleBinding_BlocksCommitUntilItsStageCompletes()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("AsyncLifecycle");
            var binder = new AsyncGateBinder();
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });

            UniTask<UIWindow> open = service.OpenAsync(configuration);
            Assert.AreEqual(UniTaskStatus.Pending, open.Status);
            Assert.AreEqual(0, service.ActiveWindowCount);

            binder.Release();
            UIWindow window = await open;

            Assert.AreEqual("AsyncLifecycle", window.WindowId);
            Assert.AreEqual(2, binder.AsyncCallbackCount);
            Assert.AreEqual(1, service.ActiveWindowCount);
        }

        [Test]
        public void DisposeDuringAsyncOpening_PublishesCancellationAfterCleanupCompletes()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("AsyncDispose");
            var binder = new AsyncGateBinder();
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });
            UniTask<UIWindow> open = service.OpenAsync(configuration);
            Assert.AreEqual(UniTaskStatus.Pending, open.Status);
            Assert.AreEqual(1, _fixture.Layer.WindowCount);

            service.Dispose();

            Assert.AreEqual(1, binder.DisposeCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.CatchAsync<OperationCanceledException>(async () => await open);
            binder.Release();
        }

        [TestCase(WindowStateCallbackType.OnStartOpen)]
        [TestCase(WindowStateCallbackType.OnFinishedOpen)]
        public async Task CloseDuringAsyncOpening_CancelsTheCurrentStageAndCompletesCleanup(
            WindowStateCallbackType gatedCallback)
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration($"AsyncClose{gatedCallback}");
            var binder = new AsyncGateBinder(gatedCallback);
            var transition = new GatedCloseTransitionDriver();
            UIService service = CreateService(
                options: new UIServiceOptions { DefaultTransitionDriver = transition },
                binders: new IUIWindowBinder[] { binder });
            UniTask<UIWindow> open = service.OpenAsync(configuration);
            Assert.AreEqual(UniTaskStatus.Pending, open.Status);

            UniTask<bool> close = service.CloseAsync(configuration.WindowId);
            for (int i = 0; i < 8 && open.Status == UniTaskStatus.Pending; i++)
            {
                await UniTask.Yield();
            }

            Assert.AreNotEqual(UniTaskStatus.Pending, open.Status);
            Assert.CatchAsync<OperationCanceledException>(async () => await open);
            transition.Release();
            Assert.IsTrue(await close);
            Assert.AreEqual(1, binder.DisposeCount);
            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            binder.Release();
        }

        [Test]
        public async Task AnimatedShutdown_IsSingleFlightForConcurrentCallers()
        {
            var transition = new GatedCloseTransitionDriver();
            UIService service = CreateService(
                options: new UIServiceOptions { DefaultTransitionDriver = transition });
            await service.OpenAsync(_fixture.CreateDirectConfiguration("ShutdownWindow"));

            UniTask first = service.ShutdownAsync(UIShutdownMode.Animated);
            UniTask second = service.ShutdownAsync(UIShutdownMode.Animated);

            Assert.AreEqual(UniTaskStatus.Pending, first.Status);
            Assert.AreEqual(UniTaskStatus.Pending, second.Status);
            transition.Release();
            await first;
            await second;

            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task ImmediateShutdown_WaitsForPendingConfigurationAcquisition()
        {
            int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("PendingConfigurationShutdown");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var provider = new RecordingAssetProvider
            {
                ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>(),
                IgnoreConfigurationCancellation = true,
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> open = service.OpenAsync("PendingConfigurationShutdown");

            UniTask shutdown = service.ShutdownAsync(UIShutdownMode.Immediate);

            Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);
            Assert.AreEqual(0, configurationLease.DisposeCount);
            Assert.CatchAsync<OperationCanceledException>(async () => await open);

            await Task.Run(() => provider.ConfigurationGate.TrySetResult(configurationLease));
            await shutdown;

            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(ownerThreadId, configurationLease.DisposeThreadId);
            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task ImmediateShutdown_ProviderWorkerFaultPublishesOnOwnerThread()
        {
            int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var provider = new RecordingAssetProvider
            {
                ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>(),
                IgnoreConfigurationCancellation = true,
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> open = service.OpenAsync("WorkerFaultShutdown");
            UniTask shutdown = service.ShutdownAsync(UIShutdownMode.Immediate);
            int shutdownContinuationThreadId = -1;

            async UniTask ObserveShutdownAsync()
            {
                await shutdown;
                shutdownContinuationThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            UniTask observer = ObserveShutdownAsync();
            await Task.Run(() => provider.ConfigurationGate.TrySetException(
                new InvalidOperationException("Worker provider failure during shutdown.")));
            await observer;

            Assert.CatchAsync<OperationCanceledException>(async () => await open);
            Assert.AreEqual(ownerThreadId, shutdownContinuationThreadId);
            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task ImmediateShutdown_WaitsForPendingPrefabAcquisitionAndLateLeaseRelease()
        {
            int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("PendingShutdown");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var prefabLease = new TestAssetLease<GameObject>(
                _fixture.CreateWindowPrefab("PendingShutdown"));
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabGate = new UniTaskCompletionSource<IUIAssetLease<GameObject>>(),
                IgnorePrefabCancellation = true,
            };
            UIService service = CreateService(provider);
            UniTask<UIWindow> open = service.OpenAsync("PendingShutdown");
            Assert.AreEqual(1, provider.PrefabAcquireCount);

            UniTask shutdown = service.ShutdownAsync(UIShutdownMode.Immediate);

            Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);
            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(0, prefabLease.DisposeCount);
            Assert.CatchAsync<OperationCanceledException>(async () => await open);

            await Task.Run(() => provider.PrefabGate.TrySetResult(prefabLease));
            await shutdown;

            Assert.AreEqual(1, prefabLease.DisposeCount);
            Assert.AreEqual(ownerThreadId, prefabLease.DisposeThreadId);
            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task DisposeDuringPendingAnimatedShutdown_TerminatesWithoutReentrantLoop()
        {
            var transition = new GatedCloseTransitionDriver();
            UIService service = CreateService(
                options: new UIServiceOptions { DefaultTransitionDriver = transition });
            await service.OpenAsync(_fixture.CreateDirectConfiguration("InterruptedShutdown"));
            UniTask shutdown = service.ShutdownAsync(UIShutdownMode.Animated);
            Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);

            service.Dispose();

            for (int i = 0; i < 8 && shutdown.Status == UniTaskStatus.Pending; i++)
            {
                await UniTask.Yield();
            }

            Assert.AreNotEqual(UniTaskStatus.Pending, shutdown.Status);
            Assert.CatchAsync<Exception>(async () => await shutdown);
            transition.Release();
            Assert.IsTrue(service.IsDisposed);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task DisposeDuringCoordinatedTransition_CancelsNavigationWithoutReturningDestroyedWindow()
        {
            UIService service = CreateService();
            await service.OpenAsync(_fixture.CreateDirectConfiguration("Leaving"));
            await service.OpenAsync(_fixture.CreateDirectConfiguration("Entering"));
            var coordinator = new GatedCoordinator();
            UniTask<UIWindow> navigation = service.NavigateAsync(
                "Leaving",
                "Entering",
                coordinator);

            service.Dispose();
            Assert.CatchAsync<OperationCanceledException>(async () => await navigation);
            coordinator.Release();

            Assert.IsTrue(coordinator.ObservedToken.IsCancellationRequested);
            Assert.AreEqual(0, service.ActiveWindowCount);
        }

        [Test]
        public async Task NavigationWaitingForProviderOpen_PublishesOnlyAfterServiceWideCleanup()
        {
            UIWindowConfiguration enteringConfiguration =
                _fixture.CreateAddressConfiguration("ProviderEntering");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(enteringConfiguration);
            var prefabLease = new TestAssetLease<GameObject>(
                _fixture.CreateWindowPrefab("ProviderEntering"));
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabGate = new UniTaskCompletionSource<IUIAssetLease<GameObject>>(),
                IgnorePrefabCancellation = true,
            };
            var binder = new CallbackBinder(WindowStateCallbackType.OnStartClose);
            UIService service = CreateService(
                provider,
                binders: new IUIWindowBinder[] { binder });
            await service.OpenAsync(_fixture.CreateDirectConfiguration("ProviderLeaving"));

            UniTask<UIWindow> navigation = service.NavigateAsync(
                "ProviderLeaving",
                "ProviderEntering",
                new RecordingTransitionCoordinator());
            Assert.AreEqual(UniTaskStatus.Pending, navigation.Status);

            Exception observedException = null;
            int observedBindingDisposeCount = -1;
            int observedConfigurationDisposeCount = -1;
            int observedActiveCount = -1;
            int observedLayerCount = -1;

            async UniTask ObserveNavigationAsync()
            {
                try
                {
                    await navigation;
                }
                catch (Exception exception)
                {
                    observedException = exception;
                    observedBindingDisposeCount = binder.DisposeCount;
                    observedConfigurationDisposeCount = configurationLease.DisposeCount;
                    observedActiveCount = service.ActiveWindowCount;
                    observedLayerCount = _fixture.Layer.WindowCount;
                }
            }

            UniTask observer = ObserveNavigationAsync();
            service.Dispose();
            await observer;

            Assert.IsInstanceOf<OperationCanceledException>(observedException);
            Assert.AreEqual(1, observedBindingDisposeCount);
            Assert.AreEqual(1, observedConfigurationDisposeCount);
            Assert.AreEqual(0, observedActiveCount);
            Assert.AreEqual(0, observedLayerCount);

            provider.PrefabGate.TrySetResult(prefabLease);
            for (int i = 0; i < 8 && prefabLease.DisposeCount == 0; i++)
            {
                await UniTask.Yield();
            }

            Assert.AreEqual(1, prefabLease.DisposeCount);
        }

        [Test]
        public async Task NavigateRejectsIdenticalLeavingAndEnteringIds()
        {
            UIService service = CreateService();
            await service.OpenAsync(_fixture.CreateDirectConfiguration("Same"));

            Assert.Throws<ArgumentException>(() =>
                service.NavigateAsync("Same", "Same", new RecordingTransitionCoordinator()));
        }

        [Test]
        public async Task CleanupFailures_AreAggregatedAfterEveryOwnedResourceIsReleased()
        {
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration("FailureCleanup");
            var configurationLease =
                new TestAssetLease<UIWindowConfiguration>(configuration);
            var prefabLease = new TestAssetLease<GameObject>(
                _fixture.CreateWindowPrefab<ThrowingCloseWindow>("FailureCleanup"));
            var provider = new RecordingAssetProvider
            {
                ConfigurationLease = configurationLease,
                PrefabLease = prefabLease,
                ThrowOnCancellation = true,
            };
            var navigation = new ThrowingNavigationService();
            var binder = new CallbackBinder(
                WindowStateCallbackType.OnStartClose,
                throwOnDispose: true);
            UIService service = CreateService(
                provider,
                new UIServiceOptions { NavigationService = navigation },
                new IUIWindowBinder[] { binder });
            UIWindow window = await service.OpenAsync("FailureCleanup");
            navigation.ThrowOnUnregister = true;

            Assert.CatchAsync<Exception>(async () =>
                await service.CloseAsync("FailureCleanup"));

            Assert.AreEqual(0, service.ActiveWindowCount);
            Assert.AreEqual(0, _fixture.Layer.WindowCount);
            Assert.AreEqual(1, configurationLease.DisposeCount);
            Assert.AreEqual(1, prefabLease.DisposeCount);
            Assert.AreEqual(1, binder.DisposeCount);
            Assert.IsTrue(window == null);
        }

        private UIService CreateService(
            IUIWindowAssetProvider provider = null,
            UIServiceOptions options = null,
            IReadOnlyList<IUIWindowBinder> binders = null)
        {
            var service = new UIService(_fixture.Root, provider, options, binders);
            _services.Add(service);
            return service;
        }
    }
}
