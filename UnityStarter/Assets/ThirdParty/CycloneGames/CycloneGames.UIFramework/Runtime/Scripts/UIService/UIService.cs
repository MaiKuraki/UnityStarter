using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.UIFramework.Runtime
{
    public enum UIShutdownMode
    {
        Immediate = 0,
        Animated = 1,
    }

    public readonly struct UIOpenOptions
    {
        public UIOpenOptions(
            string openerId = null,
            object context = null,
            bool? sceneBoundOverride = null,
            UIAssetLoadContext assetLoadContext = default,
            IUIWindowTransitionDriver transitionDriver = null,
            bool suppressWindowTransition = false)
        {
            OpenerId = openerId;
            Context = context;
            SceneBoundOverride = sceneBoundOverride;
            AssetLoadContext = assetLoadContext;
            TransitionDriver = transitionDriver;
            SuppressWindowTransition = suppressWindowTransition;
        }

        public string OpenerId { get; }
        public object Context { get; }
        public bool? SceneBoundOverride { get; }
        public UIAssetLoadContext AssetLoadContext { get; }
        public IUIWindowTransitionDriver TransitionDriver { get; }
        public bool SuppressWindowTransition { get; }
    }

    public sealed class UIServiceOptions
    {
        public int InitialWindowCapacity { get; set; } = 16;
        public int MaxActiveWindows { get; set; } = 64;
        public int MaxInstantiatesPerFrame { get; set; } = 2;
        public UIAssetLoadContext DefaultAssetLoadContext { get; set; }
        public IUIWindowTransitionDriver DefaultTransitionDriver { get; set; }
        public IUINavigationService NavigationService { get; set; }

        internal void Validate()
        {
            if (InitialWindowCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(InitialWindowCapacity));
            }

            if (MaxActiveWindows < InitialWindowCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxActiveWindows),
                    "MaxActiveWindows must be greater than or equal to InitialWindowCapacity.");
            }

            if (MaxInstantiatesPerFrame < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxInstantiatesPerFrame));
            }
        }
    }

    public interface IUIService : IDisposable
    {
        bool IsDisposed { get; }
        bool IsShuttingDown { get; }
        int ActiveWindowCount { get; }
        IUINavigationService NavigationService { get; }

        UniTask<UIWindow> OpenAsync(
            string windowId,
            UIOpenOptions options = default,
            CancellationToken cancellationToken = default);

        UniTask<UIWindow> OpenAsync(
            UIWindowConfiguration configuration,
            UIOpenOptions options = default,
            CancellationToken cancellationToken = default);

        UniTask<bool> CloseAsync(
            string windowId,
            ChildClosePolicy childPolicy = ChildClosePolicy.Reparent,
            CancellationToken cancellationToken = default);

        UniTask<UIWindow> NavigateAsync(
            string leavingWindowId,
            string enteringWindowId,
            IUITransitionCoordinator coordinator,
            NavigationDirection direction = NavigationDirection.Forward,
            UIOpenOptions enteringOptions = default,
            CancellationToken cancellationToken = default);

        bool TryGetWindow(string windowId, out UIWindow window);
        int CopyActiveWindows(List<UIWindow> destination);
        void RegisterBinder(IUIWindowBinder binder);
        bool UnregisterBinder(IUIWindowBinder binder);
        Vector2 GetRootCanvasSize();
        Camera GetUICamera();
        UIPerformanceStats GetPerformanceStats();
        int CopyLayerRuntimeStats(List<UILayerRuntimeStats> destination);
        UniTask ShutdownAsync(
            UIShutdownMode mode = UIShutdownMode.Immediate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Main-thread-confined owner of UI window sessions and their resources.
    /// </summary>
    public sealed class UIService : IUIService, IUIWindowLifetimeObserver
    {
        private sealed class WindowSession
        {
            public readonly string WindowId;
            public readonly UIOpenOptions OpenOptions;
            public readonly int RequestedSceneHandle;
            public readonly CancellationTokenSource OpenCancellation;
            public readonly CancellationTokenSource TargetLifetimeCancellation;
            public readonly UniTaskCompletionSource<UIWindow> OpenCompletion =
                new UniTaskCompletionSource<UIWindow>();
            public readonly UniTaskCompletionSource CleanupCompletion =
                new UniTaskCompletionSource();

            public UIWindowConfiguration Configuration;
            public IUIAssetLease<UIWindowConfiguration> ConfigurationLease;
            public IUIAssetLease<GameObject> PrefabLease;
            public UIWindow Window;
            public UILayer Layer;
            public IUIWindowBinding[] Bindings;
            public int BindingCount;
            public IUIWindowTransitionDriver TransitionDriver;
            public UniTaskCompletionSource<bool> CloseCompletion;
            public bool NavigationCommitted;
            public bool IsCommitted;
            public bool HasIsolatedCanvas;
            public bool CloseRequested;
            public bool CloseExecutionDeferred;
            public bool CloseExecutionStarted;
            public bool CleanupStarted;
            public bool CleanupCompleted;
            public int LifecycleDispatchDepth;
            public Exception DeferredCloseRequestException;
            public Exception TerminalException;
            public UniTaskCompletionSource ProviderAcquisitionCompletion;

            public WindowSession(
                string windowId,
                UIOpenOptions openOptions,
                int requestedSceneHandle,
                CancellationToken serviceToken)
            {
                WindowId = windowId;
                OpenOptions = openOptions;
                RequestedSceneHandle = requestedSceneHandle;
                OpenCancellation = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
                TargetLifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            }
        }

        private readonly UIRoot _root;
        private readonly IUIWindowAssetProvider _assetProvider;
        private readonly Dictionary<string, WindowSession> _sessions;
        private readonly List<WindowSession> _sessionOrder;
        private readonly List<IUIWindowBinder> _binders = new List<IUIWindowBinder>(4);
        private readonly List<string> _navigationScratch = new List<string>(8);
        private readonly List<UILayer> _layerScratch = new List<UILayer>(8);
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly int _ownerThreadId;
        private readonly int _maxActiveWindows;
        private readonly int _maxInstantiatesPerFrame;
        private readonly UIAssetLoadContext _defaultAssetLoadContext;
        private readonly IUIWindowTransitionDriver _defaultTransitionDriver;
        private readonly IUINavigationService _navigationService;

        private int _instantiateFrame = -1;
        private int _instantiatesThisFrame;
        private int _activeWindowCount;
        private int _cleanupInProgressCount;
        private UniTaskCompletionSource _shutdownCompletion;
        private UniTaskCompletionSource _disposeCompletion;
        private WindowSession[] _disposeSessions;
        private bool _isDisposed;
        private bool _isShuttingDown;
        private bool _isNavigating;
        private bool _disposePhasesCompleted;

        public UIService(
            UIRoot root,
            IUIWindowAssetProvider assetProvider = null,
            UIServiceOptions options = null,
            IReadOnlyList<IUIWindowBinder> binders = null)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "UIService must be constructed on the Unity main thread.");
            }

            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _assetProvider = assetProvider;

            options ??= new UIServiceOptions();
            options.Validate();
            _maxActiveWindows = options.MaxActiveWindows;
            _maxInstantiatesPerFrame = options.MaxInstantiatesPerFrame;
            _defaultAssetLoadContext = options.DefaultAssetLoadContext;
            _defaultTransitionDriver = options.DefaultTransitionDriver;
            _navigationService = options.NavigationService;
            _sessions = new Dictionary<string, WindowSession>(
                options.InitialWindowCapacity,
                StringComparer.Ordinal);
            _sessionOrder = new List<WindowSession>(options.InitialWindowCapacity);

            _root.EnsureInitialized();
            if (binders != null)
            {
                for (int i = 0; i < binders.Count; i++)
                {
                    IUIWindowBinder binder = binders[i];
                    if (binder != null && !_binders.Contains(binder))
                    {
                        _binders.Add(binder);
                    }
                }
            }

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public bool IsDisposed => _isDisposed;
        public bool IsShuttingDown => _isShuttingDown;
        public int ActiveWindowCount => _activeWindowCount;
        public IUINavigationService NavigationService => _navigationService;

        public UniTask<UIWindow> OpenAsync(
            string windowId,
            UIOpenOptions options = default,
            CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (string.IsNullOrWhiteSpace(windowId))
            {
                throw new ArgumentException("Window id cannot be empty.", nameof(windowId));
            }

            return StartOrJoinOpen(windowId, null, options, cancellationToken);
        }

        public UniTask<UIWindow> OpenAsync(
            UIWindowConfiguration configuration,
            UIOpenOptions options = default,
            CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration.WindowId))
            {
                throw new ArgumentException("Window configuration requires a stable WindowId.", nameof(configuration));
            }

            return StartOrJoinOpen(configuration.WindowId, configuration, options, cancellationToken);
        }

        public UniTask<bool> CloseAsync(
            string windowId,
            ChildClosePolicy childPolicy = ChildClosePolicy.Reparent,
            CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            if (_isDisposed || string.IsNullOrEmpty(windowId))
            {
                return UniTask.FromResult(false);
            }

            UniTask<bool> operation = CloseWithPolicyAsync(windowId, childPolicy);
            return cancellationToken.CanBeCanceled
                ? operation.AttachExternalCancellation(cancellationToken)
                : operation;
        }

        public UniTask<UIWindow> NavigateAsync(
            string leavingWindowId,
            string enteringWindowId,
            IUITransitionCoordinator coordinator,
            NavigationDirection direction = NavigationDirection.Forward,
            UIOpenOptions enteringOptions = default,
            CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (string.IsNullOrWhiteSpace(leavingWindowId))
            {
                throw new ArgumentException("Leaving window id cannot be empty.", nameof(leavingWindowId));
            }

            if (string.IsNullOrWhiteSpace(enteringWindowId))
            {
                throw new ArgumentException("Entering window id cannot be empty.", nameof(enteringWindowId));
            }

            if (string.Equals(leavingWindowId, enteringWindowId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Leaving and entering window ids must be different.",
                    nameof(enteringWindowId));
            }

            if (coordinator == null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }

            if (_isNavigating)
            {
                throw new InvalidOperationException("A coordinated navigation is already in progress.");
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return NavigateCoreAsync(
                    leavingWindowId,
                    enteringWindowId,
                    coordinator,
                    direction,
                    enteringOptions,
                    _lifetimeCancellation.Token);
            }

            return NavigateWithLinkedCancellationAsync(
                leavingWindowId,
                enteringWindowId,
                coordinator,
                direction,
                enteringOptions,
                cancellationToken);
        }

        public bool TryGetWindow(string windowId, out UIWindow window)
        {
            EnsureMainThread();
            if (!_isDisposed && !string.IsNullOrEmpty(windowId) &&
                _sessions.TryGetValue(windowId, out WindowSession session) &&
                session.IsCommitted && session.Window != null)
            {
                window = session.Window;
                return true;
            }

            window = null;
            return false;
        }

        public int CopyActiveWindows(List<UIWindow> destination)
        {
            EnsureMainThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int i = 0; i < _sessionOrder.Count; i++)
            {
                WindowSession session = _sessionOrder[i];
                if (session.IsCommitted && session.Window != null)
                {
                    destination.Add(session.Window);
                }
            }

            return destination.Count;
        }

        public void RegisterBinder(IUIWindowBinder binder)
        {
            EnsureUsable();
            if (binder == null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            EnsureBinderSetCanChange();
            if (!_binders.Contains(binder))
            {
                _binders.Add(binder);
            }
        }

        public bool UnregisterBinder(IUIWindowBinder binder)
        {
            EnsureUsable();
            EnsureBinderSetCanChange();
            return binder != null && _binders.Remove(binder);
        }

        public Vector2 GetRootCanvasSize()
        {
            EnsureUsable();
            return _root.GetRootCanvasSize();
        }

        public Camera GetUICamera()
        {
            EnsureUsable();
            return _root.UICamera;
        }

        public UIPerformanceStats GetPerformanceStats()
        {
            EnsureMainThread();
            int opening = 0;
            int open = 0;
            int closing = 0;
            int sceneBound = 0;
            int isolatedCanvas = 0;

            for (int i = 0; i < _sessionOrder.Count; i++)
            {
                WindowSession session = _sessionOrder[i];
                UIWindow window = session.Window;
                if (!session.IsCommitted)
                {
                    opening++;
                }
                else if (window != null && window.State == UIWindowState.Closing)
                {
                    closing++;
                }
                else
                {
                    open++;
                }

                if (window != null)
                {
                    if (window.IsSceneBound) sceneBound++;
                    if (session.HasIsolatedCanvas) isolatedCanvas++;
                }
            }

            return new UIPerformanceStats(
                _sessions.Count,
                opening,
                open,
                closing,
                sceneBound,
                _binders.Count,
                isolatedCanvas,
                _root.LayerCount,
                _maxActiveWindows);
        }

        public int CopyLayerRuntimeStats(List<UILayerRuntimeStats> destination)
        {
            EnsureMainThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            _root.CopyLayers(_layerScratch);
            for (int i = 0; i < _layerScratch.Count; i++)
            {
                UILayer layer = _layerScratch[i];
                if (layer == null) continue;
                int sortingOrder = layer.UICanvas != null ? layer.UICanvas.sortingOrder : 0;
                destination.Add(new UILayerRuntimeStats(layer.LayerName, sortingOrder, layer.WindowCount));
            }

            return destination.Count;
        }

        public UniTask ShutdownAsync(
            UIShutdownMode mode = UIShutdownMode.Immediate,
            CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            if (_shutdownCompletion == null && !_isDisposed)
            {
                _shutdownCompletion = new UniTaskCompletionSource();
                ShutdownCoreAsync(mode, _shutdownCompletion).Forget();
            }

            UniTask operation = _shutdownCompletion != null
                ? _shutdownCompletion.Task
                : UniTask.CompletedTask;
            return cancellationToken.CanBeCanceled
                ? operation.AttachExternalCancellation(cancellationToken)
                : operation;
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (_isDisposed)
            {
                return;
            }

            _isShuttingDown = true;
            _isDisposed = true;
            _disposeCompletion = new UniTaskCompletionSource();
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            WindowSession[] sessions = _sessionOrder.ToArray();
            _disposeSessions = sessions;
            List<Exception> cleanupErrors = null;

            for (int i = sessions.Length - 1; i >= 0; i--)
            {
                WindowSession session = sessions[i];
                if (session.CleanupStarted)
                {
                    continue;
                }

                AddCleanupError(ref cleanupErrors, CleanupSession(session, false));
            }

            _sessions.Clear();
            _sessionOrder.Clear();
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref cleanupErrors, exception);
            }

            if (_navigationService != null)
            {
                try
                {
                    _navigationService.Clear();
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref cleanupErrors, exception);
                }
            }

            try
            {
                _lifetimeCancellation.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref cleanupErrors, exception);
            }

            _isShuttingDown = false;
            if (cleanupErrors != null)
            {
                Debug.LogException(CreateCleanupException(
                    "UIService disposal completed with cleanup failures.",
                    cleanupErrors));
            }

            _disposePhasesCompleted = true;
            TryCompleteDisposeBarrier();
        }

        void IUIWindowLifetimeObserver.OnWindowDestroyed(UIWindow window)
        {
            EnsureMainThread();
            if (_isDisposed || ReferenceEquals(window, null) || string.IsNullOrEmpty(window.WindowId))
            {
                return;
            }

            if (!_sessions.TryGetValue(window.WindowId, out WindowSession session) ||
                !ReferenceEquals(session.Window, window))
            {
                return;
            }

            InvalidOperationException exception = new InvalidOperationException(
                $"Window '{window.WindowId}' was destroyed outside UIService ownership.");
            session.TerminalException = exception;
            Exception cleanupException = CleanupSession(session, true);
            session.OpenCompletion.TrySetException(exception);
            if (cleanupException != null)
            {
                session.CloseCompletion?.TrySetException(cleanupException);
                Debug.LogException(cleanupException);
            }
            else
            {
                session.CloseCompletion?.TrySetResult(true);
            }
        }

        private UniTask<UIWindow> StartOrJoinOpen(
            string windowId,
            UIWindowConfiguration configuration,
            UIOpenOptions options,
            CancellationToken callerToken)
        {
            if (_sessions.TryGetValue(windowId, out WindowSession existing))
            {
                if (existing.CloseRequested)
                {
                    throw new InvalidOperationException($"Window '{windowId}' is closing.");
                }

                if (configuration != null && existing.Configuration == null)
                {
                    throw new InvalidOperationException(
                        $"Window '{windowId}' is already opening through the asset provider; " +
                        "an explicit configuration cannot join that operation.");
                }

                if (configuration != null &&
                    !ReferenceEquals(configuration, existing.Configuration))
                {
                    throw new InvalidOperationException(
                        $"Window '{windowId}' is already opening from a different configuration.");
                }

                if (!OpenOptionsMatch(existing.OpenOptions, options))
                {
                    throw new InvalidOperationException(
                        $"Window '{windowId}' is already opening with different options.");
                }

                return callerToken.CanBeCanceled
                    ? existing.OpenCompletion.Task.AttachExternalCancellation(callerToken)
                    : existing.OpenCompletion.Task;
            }

            if (_sessions.Count >= _maxActiveWindows)
            {
                throw new InvalidOperationException(
                    $"UI window capacity {_maxActiveWindows} has been reached.");
            }

            WindowSession session = new WindowSession(
                windowId,
                options,
                SceneManager.GetActiveScene().handle,
                _lifetimeCancellation.Token)
            {
                Configuration = configuration,
            };

            _sessions.Add(windowId, session);
            _sessionOrder.Add(session);
            ExecuteOpenAsync(session).Forget();
            return callerToken.CanBeCanceled
                ? session.OpenCompletion.Task.AttachExternalCancellation(callerToken)
                : session.OpenCompletion.Task;
        }

        private async UniTask ExecuteOpenAsync(WindowSession session)
        {
            CancellationToken cancellationToken;
            try
            {
                cancellationToken = session.OpenCancellation.Token;
            }
            catch (ObjectDisposedException)
            {
                session.OpenCompletion.TrySetCanceled();
                return;
            }

            try
            {
                ThrowIfOpenCannotContinue(session, cancellationToken);
                UIAssetLoadContext loadContext = session.OpenOptions.AssetLoadContext.Merge(
                    GetDefaultAssetLoadContext());

                if (session.Configuration == null)
                {
                    if (_assetProvider == null)
                    {
                        throw new InvalidOperationException(
                            "Opening by id requires an IUIWindowAssetProvider.");
                    }

                    IUIAssetLease<UIWindowConfiguration> acquiredLease = null;
                    var acquisitionCompletion = new UniTaskCompletionSource();
                    session.ProviderAcquisitionCompletion = acquisitionCompletion;
                    try
                    {
                        acquiredLease = await _assetProvider.AcquireConfigurationAsync(
                            session.WindowId,
                            loadContext,
                            cancellationToken);
                        await UniTask.SwitchToMainThread();
                        ThrowIfOpenCannotContinue(session, cancellationToken);
                        session.ConfigurationLease = acquiredLease;
                        acquiredLease = null;
                        session.Configuration = session.ConfigurationLease?.Asset;
                    }
                    finally
                    {
                        try
                        {
                            acquiredLease?.Dispose();
                        }
                        finally
                        {
                            acquisitionCompletion.TrySetResult();
                        }
                    }
                }

                ThrowIfOpenCannotContinue(session, cancellationToken);
                ValidateConfiguration(session);
                if (!_root.TryGetLayer(session.Configuration.Layer, out UILayer layer))
                {
                    throw new InvalidOperationException(
                        $"Layer '{session.Configuration.Layer.LayerName}' is not registered in UIRoot.");
                }

                session.Layer = layer;
                UIWindow prefabWindow;
                if (session.Configuration.Source == UIWindowConfiguration.PrefabSource.PrefabReference)
                {
                    prefabWindow = session.Configuration.WindowPrefab;
                }
                else
                {
                    if (_assetProvider == null)
                    {
                        throw new InvalidOperationException(
                            $"Window '{session.WindowId}' uses an address but no asset provider is configured.");
                    }

                    IUIAssetLease<GameObject> acquiredLease = null;
                    var acquisitionCompletion = new UniTaskCompletionSource();
                    session.ProviderAcquisitionCompletion = acquisitionCompletion;
                    try
                    {
                        acquiredLease = await _assetProvider.AcquirePrefabAsync(
                            session.Configuration.EffectiveAssetReference,
                            loadContext,
                            cancellationToken);
                        await UniTask.SwitchToMainThread();
                        ThrowIfOpenCannotContinue(session, cancellationToken);
                        session.PrefabLease = acquiredLease;
                        acquiredLease = null;
                    }
                    finally
                    {
                        try
                        {
                            acquiredLease?.Dispose();
                        }
                        finally
                        {
                            acquisitionCompletion.TrySetResult();
                        }
                    }

                    GameObject prefabObject = session.PrefabLease?.Asset;
                    prefabWindow = prefabObject != null ? prefabObject.GetComponent<UIWindow>() : null;
                }

                if (prefabWindow == null)
                {
                    throw new InvalidOperationException(
                        $"Window '{session.WindowId}' prefab does not contain UIWindow.");
                }

                await WaitForInstantiateBudgetAsync(cancellationToken);
                ThrowIfOpenCannotContinue(session, cancellationToken);
                UIWindow unownedWindow = UnityEngine.Object.Instantiate(
                    prefabWindow,
                    layer.transform,
                    false);
                try
                {
                    ThrowIfOpenCannotContinue(session, cancellationToken);
                    session.Window = unownedWindow;
                    unownedWindow = null;
                }
                finally
                {
                    if (unownedWindow != null)
                    {
                        DestroyWindow(unownedWindow);
                    }
                }

                UIWindow window = session.Window;
                bool sceneBound = session.OpenOptions.SceneBoundOverride ?? session.Configuration.IsSceneBound;
                window.InitializeRuntime(
                    session.WindowId,
                    session.Configuration,
                    sceneBound,
                    session.RequestedSceneHandle,
                    this);
                session.TransitionDriver = session.OpenOptions.SuppressWindowTransition
                    ? null
                    : session.OpenOptions.TransitionDriver ?? _defaultTransitionDriver;
                session.HasIsolatedCanvas = ConfigureCanvas(
                    window,
                    session.Configuration,
                    layer);
                ThrowIfOpenCannotContinue(session, cancellationToken);
                BindWindow(session, cancellationToken);
                ThrowIfOpenCannotContinue(session, cancellationToken);
                layer.Attach(window);
                ThrowIfOpenCannotContinue(session, cancellationToken);

                if (sceneBound && session.RequestedSceneHandle != SceneManager.GetActiveScene().handle)
                {
                    throw new OperationCanceledException(
                        $"Scene-bound window '{session.WindowId}' completed after its owner scene changed.");
                }

                ThrowIfBindingCallbackFailed(
                    await NotifyBindingsAsync(
                        session,
                        WindowStateCallbackType.OnStartOpen,
                        cancellationToken,
                        continueOnFailure: false));
                ThrowIfOpenCannotContinue(session, cancellationToken);
                await window.RunOpenAsync(session.TransitionDriver, cancellationToken);
                await UniTask.SwitchToMainThread();
                ThrowIfOpenCannotContinue(session, cancellationToken);

                if (_navigationService != null)
                {
                    if (!_navigationService.Register(
                            session.WindowId,
                            session.OpenOptions.OpenerId,
                            session.OpenOptions.Context))
                    {
                        throw new InvalidOperationException(
                            $"Navigation rejected registration for '{session.WindowId}'.");
                    }

                    session.NavigationCommitted = true;
                    if (session.CleanupStarted || !IsSessionOwned(session))
                    {
                        Exception releaseException = ReleaseNavigationRegistration(session);
                        if (releaseException != null)
                        {
                            throw releaseException;
                        }
                    }
                }

                ThrowIfOpenCannotContinue(session, cancellationToken);
                session.IsCommitted = true;
                _activeWindowCount++;
                ThrowIfBindingCallbackFailed(
                    await NotifyBindingsAsync(
                        session,
                        WindowStateCallbackType.OnFinishedOpen,
                        cancellationToken,
                        continueOnFailure: false));
                if (!IsSessionOwned(session) ||
                    session.CleanupStarted ||
                    session.CloseRequested ||
                    session.Window == null)
                {
                    session.OpenCompletion.TrySetCanceled();
                    return;
                }

                session.OpenCompletion.TrySetResult(session.Window);
            }
            catch (OperationCanceledException cancellationException)
            {
                await UniTask.SwitchToMainThread();
                if (session.CleanupStarted && !session.CleanupCompleted)
                {
                    return;
                }

                Exception cleanupException = CleanupSession(session, false);
                if (session.TerminalException != null)
                {
                    session.OpenCompletion.TrySetException(session.TerminalException);
                }
                else if (cleanupException == null)
                {
                    session.OpenCompletion.TrySetCanceled();
                }
                else
                {
                    session.OpenCompletion.TrySetException(new AggregateException(
                        "UI window opening was canceled and cleanup failed.",
                        cancellationException,
                        cleanupException));
                }
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                if (session.CleanupStarted && !session.CleanupCompleted)
                {
                    return;
                }

                Exception cleanupException = CleanupSession(session, false);
                if (session.TerminalException != null)
                {
                    session.OpenCompletion.TrySetException(session.TerminalException);
                }
                else
                {
                    session.OpenCompletion.TrySetException(cleanupException == null
                        ? exception
                        : new AggregateException(exception, cleanupException));
                }
            }
        }

        private async UniTask<bool> CloseWithPolicyAsync(string windowId, ChildClosePolicy childPolicy)
        {
            List<string> affected = new List<string>(4);
            List<Exception> errors = null;
            bool navigationRemoved = false;
            if (_navigationService != null)
            {
                try
                {
                    navigationRemoved = _navigationService.Unregister(
                        windowId,
                        childPolicy,
                        affected);
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                    affected.Clear();
                }
            }

            if (!navigationRemoved || affected.Count == 0)
            {
                affected.Add(windowId);
            }
            else
            {
                for (int i = 0; i < affected.Count; i++)
                {
                    if (_sessions.TryGetValue(affected[i], out WindowSession affectedSession))
                    {
                        affectedSession.NavigationCommitted = false;
                    }
                }
            }

            bool closedAny = false;
            for (int i = affected.Count - 1; i >= 0; i--)
            {
                try
                {
                    closedAny |= await CloseSingleAsync(affected[i]);
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }
            }

            if (errors != null)
            {
                throw CreateCleanupException(
                    $"Closing UI window '{windowId}' completed with failures.",
                    errors);
            }

            return closedAny;
        }

        private async UniTask<UIWindow> NavigateWithLinkedCancellationAsync(
            string leavingWindowId,
            string enteringWindowId,
            IUITransitionCoordinator coordinator,
            NavigationDirection direction,
            UIOpenOptions enteringOptions,
            CancellationToken callerToken)
        {
            using (CancellationTokenSource linkedCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       _lifetimeCancellation.Token,
                       callerToken))
            {
                return await NavigateCoreAsync(
                    leavingWindowId,
                    enteringWindowId,
                    coordinator,
                    direction,
                    enteringOptions,
                    linkedCancellation.Token);
            }
        }

        private async UniTask<UIWindow> NavigateCoreAsync(
            string leavingWindowId,
            string enteringWindowId,
            IUITransitionCoordinator coordinator,
            NavigationDirection direction,
            UIOpenOptions enteringOptions,
            CancellationToken cancellationToken)
        {
            _isNavigating = true;
            bool enteringAlreadyActive = TryGetWindow(enteringWindowId, out UIWindow entering);
            bool irreversibleCloseStarted = false;
            WindowSession leavingOwner = null;
            WindowSession enteringOwner = null;
            try
            {
                if (!TryGetWindow(leavingWindowId, out UIWindow leaving))
                {
                    throw new InvalidOperationException(
                        $"Leaving window '{leavingWindowId}' is not active.");
                }

                if (!enteringAlreadyActive)
                {
                    UIOpenOptions coordinatedOptions = new UIOpenOptions(
                        leavingWindowId,
                        enteringOptions.Context,
                        enteringOptions.SceneBoundOverride,
                        enteringOptions.AssetLoadContext,
                        enteringOptions.TransitionDriver,
                        true);
                    entering = await OpenAsync(
                        enteringWindowId,
                        coordinatedOptions,
                        cancellationToken);
                }

                if (!_sessions.TryGetValue(leavingWindowId, out leavingOwner) ||
                    !_sessions.TryGetValue(enteringWindowId, out enteringOwner))
                {
                    throw new InvalidOperationException(
                        "Coordinated navigation lost a window session before transition.");
                }

                using (CancellationTokenSource transitionCancellation =
                       CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken,
                           leavingOwner.TargetLifetimeCancellation.Token,
                           enteringOwner.TargetLifetimeCancellation.Token))
                {
                    await coordinator.TransitionAsync(
                        leaving,
                        entering,
                        direction,
                        transitionCancellation.Token);
                    await UniTask.SwitchToMainThread();
                    transitionCancellation.Token.ThrowIfCancellationRequested();
                }

                ThrowIfNavigationCannotCommit(
                    leavingWindowId,
                    leaving,
                    enteringWindowId,
                    entering,
                    cancellationToken);

                // Closing the leaving window is the irreversible navigation commit point.
                // Caller cancellation after this boundary cannot roll back the entering window.
                irreversibleCloseStarted = true;
                if (_sessions.TryGetValue(leavingWindowId, out WindowSession leavingSession))
                {
                    leavingSession.TransitionDriver = null;
                }

                await CloseWithPolicyAsync(leavingWindowId, ChildClosePolicy.Reparent);
                if (_isDisposed ||
                    !TryGetWindow(enteringWindowId, out UIWindow activeEntering) ||
                    !ReferenceEquals(activeEntering, entering))
                {
                    throw new OperationCanceledException(
                        "Coordinated navigation lost ownership before commit.",
                        cancellationToken);
                }

                return entering;
            }
            catch
            {
                await UniTask.SwitchToMainThread();
                await WaitForDisposePublicationBarrierAsync();
                await WaitForCleanupPublicationBarrierAsync(leavingOwner);
                await WaitForCleanupPublicationBarrierAsync(enteringOwner);
                if (!irreversibleCloseStarted && !enteringAlreadyActive)
                {
                    try
                    {
                        await CloseWithPolicyAsync(enteringWindowId, ChildClosePolicy.Detach);
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogException(cleanupException);
                    }
                }

                throw;
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private UniTask<bool> CloseSingleAsync(string windowId)
        {
            if (!_sessions.TryGetValue(windowId, out WindowSession session))
            {
                return UniTask.FromResult(false);
            }

            if (session.CloseCompletion != null)
            {
                return session.CloseCompletion.Task;
            }

            session.CloseRequested = true;
            session.CloseCompletion = new UniTaskCompletionSource<bool>();
            if (session.LifecycleDispatchDepth > 0)
            {
                session.CloseExecutionDeferred = true;
                try
                {
                    if (!session.OpenCancellation.IsCancellationRequested)
                    {
                        session.OpenCancellation.Cancel();
                    }
                }
                catch (Exception exception)
                {
                    session.DeferredCloseRequestException = exception;
                }
            }
            else
            {
                StartCloseExecution(session);
            }

            return session.CloseCompletion.Task;
        }

        private async UniTask ExecuteCloseAsync(WindowSession session)
        {
            List<Exception> errors = null;
            AddCleanupError(ref errors, session.DeferredCloseRequestException);
            session.DeferredCloseRequestException = null;
            if (!session.IsCommitted)
            {
                try
                {
                    if (!session.OpenCancellation.IsCancellationRequested)
                    {
                        session.OpenCancellation.Cancel();
                    }
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }

                try
                {
                    await session.OpenCompletion.Task.SuppressCancellationThrow();
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }

                await UniTask.SwitchToMainThread();
                if (session.CleanupStarted || !IsSessionOwned(session))
                {
                    if (session.CleanupCompleted)
                    {
                        CompleteClose(session, errors);
                    }

                    return;
                }

                if (!session.IsCommitted)
                {
                    AddCleanupError(ref errors, CleanupSession(session, false));
                    CompleteClose(session, errors);
                    return;
                }
            }

            UIWindow window = session.Window;
            if (window != null)
            {
                CancellationToken targetLifetimeToken;
                try
                {
                    targetLifetimeToken = session.TargetLifetimeCancellation.Token;
                }
                catch (ObjectDisposedException)
                {
                    if (session.CleanupCompleted)
                    {
                        CompleteClose(session, errors);
                    }

                    return;
                }

                AddCleanupError(
                    ref errors,
                    await NotifyBindingsAsync(
                        session,
                        WindowStateCallbackType.OnStartClose,
                        targetLifetimeToken,
                        continueOnFailure: true));
                if (session.CleanupStarted || !IsSessionOwned(session) || window == null)
                {
                    if (session.CleanupCompleted)
                    {
                        CompleteClose(session, errors);
                    }

                    return;
                }

                try
                {
                    await window.RunCloseAsync(
                        session.TransitionDriver,
                        targetLifetimeToken);
                    await UniTask.SwitchToMainThread();
                }
                catch (OperationCanceledException) when (
                    _isShuttingDown ||
                    _isDisposed ||
                    session.CleanupStarted ||
                    session.TargetLifetimeCancellation.IsCancellationRequested)
                {
                    try
                    {
                        window.ForceClosed();
                    }
                    catch (Exception exception)
                    {
                        AddCleanupError(ref errors, exception);
                    }
                }
                catch (Exception exception)
                {
                    await UniTask.SwitchToMainThread();
                    AddCleanupError(ref errors, exception);
                }

                if (!session.CleanupStarted && window != null && window.State != UIWindowState.Closed)
                {
                    try
                    {
                        window.ForceClosed();
                    }
                    catch (Exception exception)
                    {
                        AddCleanupError(ref errors, exception);
                    }
                }

                if (!session.CleanupStarted && window != null && window.State == UIWindowState.Closed)
                {
                    AddCleanupError(
                        ref errors,
                        await NotifyBindingsAsync(
                            session,
                            WindowStateCallbackType.OnFinishedClose,
                            targetLifetimeToken,
                            continueOnFailure: true));
                }
            }

            if (session.CleanupStarted && !session.CleanupCompleted)
            {
                return;
            }

            AddCleanupError(ref errors, CleanupSession(session, false));
            CompleteClose(session, errors);
        }

        private async UniTask ShutdownCoreAsync(
            UIShutdownMode mode,
            UniTaskCompletionSource completion)
        {
            _isShuttingDown = true;
            WindowSession[] sessionsToDrain = null;
            List<Exception> errors = null;
            try
            {
                sessionsToDrain = _sessionOrder.ToArray();
                if (mode == UIShutdownMode.Animated)
                {
                    while (_sessionOrder.Count > 0)
                    {
                        WindowSession session = _sessionOrder[_sessionOrder.Count - 1];
                        try
                        {
                            await CloseSingleAsync(session.WindowId);
                        }
                        catch (Exception exception)
                        {
                            errors ??= new List<Exception>(2);
                            errors.Add(exception);
                        }
                    }
                }

                Dispose();
                await WaitForDisposePublicationBarrierAsync();
                await WaitForProviderAcquisitionDrainAsync(sessionsToDrain);
                if (errors != null)
                {
                    throw new AggregateException("One or more UI windows failed during shutdown.", errors);
                }

                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                if (!_isDisposed)
                {
                    Dispose();
                }

                completion.TrySetException(exception);
            }
            finally
            {
                if (!_isDisposed)
                {
                    _isShuttingDown = false;
                }
            }
        }

        private static async UniTask WaitForProviderAcquisitionDrainAsync(
            WindowSession[] sessions)
        {
            if (sessions == null)
            {
                return;
            }

            for (int i = 0; i < sessions.Length; i++)
            {
                WindowSession session = sessions[i];
                while (true)
                {
                    UniTaskCompletionSource acquisition = session.ProviderAcquisitionCompletion;
                    if (acquisition == null)
                    {
                        break;
                    }

                    await acquisition.Task;
                    await UniTask.SwitchToMainThread();
                    if (ReferenceEquals(acquisition, session.ProviderAcquisitionCompletion))
                    {
                        break;
                    }
                }
            }

        }

        private void ValidateConfiguration(WindowSession session)
        {
            UIWindowConfiguration configuration = session.Configuration;
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    $"Asset provider returned no configuration for '{session.WindowId}'.");
            }

            if (!configuration.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"Configuration for '{session.WindowId}' is incomplete.");
            }

            if (!string.Equals(configuration.WindowId, session.WindowId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Requested id '{session.WindowId}' does not match configuration id '{configuration.WindowId}'.");
            }
        }

        private UIAssetLoadContext GetDefaultAssetLoadContext()
        {
            UIAssetContextProvider provider = _root.AssetContextProvider;
            return provider != null
                ? provider.LoadContext.Merge(_defaultAssetLoadContext)
                : _defaultAssetLoadContext;
        }

        private void BindWindow(WindowSession session, CancellationToken cancellationToken)
        {
            int binderCount = _binders.Count;
            if (binderCount == 0)
            {
                return;
            }

            session.Bindings = new IUIWindowBinding[binderCount];
            UIWindowBindingContext context = new UIWindowBindingContext(
                session.Window,
                this,
                session.OpenOptions.OpenerId,
                session.OpenOptions.Context,
                session.TargetLifetimeCancellation.Token);
            for (int i = 0; i < binderCount; i++)
            {
                ThrowIfOpenCannotContinue(session, cancellationToken);
                IUIWindowBinding unownedBinding = _binders[i].Bind(context);
                try
                {
                    ThrowIfOpenCannotContinue(session, cancellationToken);
                    if (unownedBinding != null)
                    {
                        session.Bindings[session.BindingCount++] = unownedBinding;
                        unownedBinding = null;
                    }
                }
                finally
                {
                    unownedBinding?.Dispose();
                }
            }
        }

        private async UniTask<Exception> NotifyBindingsAsync(
            WindowSession session,
            WindowStateCallbackType callback,
            CancellationToken cancellationToken,
            bool continueOnFailure)
        {
            List<Exception> errors = null;
            session.LifecycleDispatchDepth++;
            try
            {
                for (int i = 0; ; i++)
                {
                    if (session.CleanupStarted ||
                        session.Bindings == null ||
                        i >= session.BindingCount ||
                        IsOpeningCallback(callback) && session.CloseRequested)
                    {
                        break;
                    }

                    try
                    {
                        IUIWindowBinding binding = session.Bindings[i];
                        if (binding == null)
                        {
                            continue;
                        }

                        if (binding is IAsyncUIWindowBinding asyncBinding)
                        {
                            await asyncBinding.OnWindowStateChangedAsync(
                                callback,
                                cancellationToken);
                            await UniTask.SwitchToMainThread();
                        }
                        else
                        {
                            binding.OnWindowStateChanged(callback);
                        }

                        if (session.CleanupStarted ||
                            IsOpeningCallback(callback) && session.CloseRequested)
                        {
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        await UniTask.SwitchToMainThread();
                        if (exception is OperationCanceledException &&
                            IsOpeningCallback(callback) &&
                            session.CloseRequested)
                        {
                            break;
                        }

                        if (!continueOnFailure)
                        {
                            return exception;
                        }

                        AddCleanupError(ref errors, exception);
                        if (session.CleanupStarted ||
                            IsOpeningCallback(callback) && session.CloseRequested)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                session.LifecycleDispatchDepth--;
                if (session.LifecycleDispatchDepth == 0 && session.CloseExecutionDeferred)
                {
                    StartCloseExecution(session);
                }
            }

            return errors == null
                ? null
                : CreateCleanupException(
                    $"One or more UI bindings failed during '{callback}'.",
                    errors);
        }

        private void StartCloseExecution(WindowSession session)
        {
            session.CloseExecutionDeferred = false;
            if (session.CloseExecutionStarted || session.CleanupStarted)
            {
                return;
            }

            session.CloseExecutionStarted = true;
            ExecuteCloseAsync(session).Forget();
        }

        private static bool IsOpeningCallback(WindowStateCallbackType callback)
        {
            return callback == WindowStateCallbackType.OnStartOpen ||
                   callback == WindowStateCallbackType.OnFinishedOpen;
        }

        private Exception CleanupSession(WindowSession session, bool windowAlreadyDestroyed)
        {
            if (session == null || session.CleanupStarted)
            {
                return null;
            }

            session.CleanupStarted = true;
            _cleanupInProgressCount++;
            if (session.IsCommitted)
            {
                session.IsCommitted = false;
                if (_activeWindowCount > 0)
                {
                    _activeWindowCount--;
                }
            }

            _sessions.Remove(session.WindowId);
            _sessionOrder.Remove(session);

            List<Exception> errors = null;
            AddCleanupError(ref errors, ReleaseNavigationRegistration(session));

            try
            {
                if (!session.OpenCancellation.IsCancellationRequested)
                {
                    session.OpenCancellation.Cancel();
                }
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            try
            {
                if (!session.TargetLifetimeCancellation.IsCancellationRequested)
                {
                    session.TargetLifetimeCancellation.Cancel();
                }
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            UILayer layer = session.Layer;
            session.Layer = null;
            if (layer != null && !ReferenceEquals(session.Window, null))
            {
                try
                {
                    layer.Detach(session.Window);
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }
            }

            for (int i = session.BindingCount - 1; i >= 0; i--)
            {
                try
                {
                    session.Bindings[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }
            }

            session.BindingCount = 0;
            session.Bindings = null;

            UIWindow window = session.Window;
            session.Window = null;
            if (!windowAlreadyDestroyed && !ReferenceEquals(window, null))
            {
                try
                {
                    window.ForceClosed();
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }

                try
                {
                    DestroyWindow(window);
                }
                catch (Exception exception)
                {
                    AddCleanupError(ref errors, exception);
                }
            }

            IUIAssetLease<GameObject> prefabLease = session.PrefabLease;
            session.PrefabLease = null;
            try
            {
                prefabLease?.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            IUIAssetLease<UIWindowConfiguration> configurationLease = session.ConfigurationLease;
            session.ConfigurationLease = null;
            try
            {
                configurationLease?.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            try
            {
                session.OpenCancellation.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            try
            {
                session.TargetLifetimeCancellation.Dispose();
            }
            catch (Exception exception)
            {
                AddCleanupError(ref errors, exception);
            }

            session.CleanupCompleted = true;
            _cleanupInProgressCount--;
            session.CleanupCompletion.TrySetResult();
            TryCompleteDisposeBarrier();
            return errors == null
                ? null
                : CreateCleanupException(
                    $"UI window '{session.WindowId}' cleanup completed with failures.",
                    errors);
        }

        private bool IsSessionOwned(WindowSession session)
        {
            return session != null &&
                   _sessions.TryGetValue(session.WindowId, out WindowSession current) &&
                   ReferenceEquals(current, session);
        }

        private void ThrowIfOpenCannotContinue(
            WindowSession session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_isDisposed ||
                _isShuttingDown ||
                session.CloseRequested ||
                session.CleanupStarted ||
                !IsSessionOwned(session))
            {
                throw new OperationCanceledException(
                    $"Opening UI window '{session.WindowId}' lost service ownership.",
                    cancellationToken);
            }
        }

        private Exception ReleaseNavigationRegistration(WindowSession session)
        {
            if (session == null || !session.NavigationCommitted)
            {
                return null;
            }

            session.NavigationCommitted = false;
            if (_navigationService == null)
            {
                return null;
            }

            try
            {
                _navigationService.Unregister(
                    session.WindowId,
                    ChildClosePolicy.Reparent,
                    _navigationScratch);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void ThrowIfNavigationCannotCommit(
            string leavingWindowId,
            UIWindow leaving,
            string enteringWindowId,
            UIWindow entering,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_isDisposed || _isShuttingDown)
            {
                throw new OperationCanceledException(
                    "UIService stopped during coordinated navigation.",
                    cancellationToken);
            }

            if (!_sessions.TryGetValue(leavingWindowId, out WindowSession leavingSession) ||
                !leavingSession.IsCommitted ||
                !ReferenceEquals(leavingSession.Window, leaving) ||
                !_sessions.TryGetValue(enteringWindowId, out WindowSession enteringSession) ||
                !enteringSession.IsCommitted ||
                !ReferenceEquals(enteringSession.Window, entering))
            {
                throw new InvalidOperationException(
                    "A coordinated navigation window changed before commit.");
            }
        }

        private static async UniTask WaitForCleanupPublicationBarrierAsync(
            WindowSession session)
        {
            if (session != null && session.CleanupStarted && !session.CleanupCompleted)
            {
                await session.CleanupCompletion.Task;
            }
        }

        private async UniTask WaitForDisposePublicationBarrierAsync()
        {
            UniTaskCompletionSource completion = _disposeCompletion;
            if (_isDisposed && completion != null)
            {
                await completion.Task;
            }
        }

        private void TryCompleteDisposeBarrier()
        {
            if (!_isDisposed ||
                !_disposePhasesCompleted ||
                _cleanupInProgressCount != 0 ||
                _disposeCompletion == null)
            {
                return;
            }

            WindowSession[] sessions = _disposeSessions;
            _disposeSessions = null;
            if (sessions != null)
            {
                for (int i = sessions.Length - 1; i >= 0; i--)
                {
                    sessions[i].OpenCompletion.TrySetCanceled();
                    sessions[i].CloseCompletion?.TrySetCanceled();
                }
            }

            _disposeCompletion.TrySetResult();
        }

        private static bool OpenOptionsMatch(in UIOpenOptions left, in UIOpenOptions right)
        {
            return string.Equals(left.OpenerId, right.OpenerId, StringComparison.Ordinal) &&
                   ReferenceEquals(left.Context, right.Context) &&
                   left.SceneBoundOverride == right.SceneBoundOverride &&
                   AssetLoadContextsMatch(left.AssetLoadContext, right.AssetLoadContext) &&
                   ReferenceEquals(left.TransitionDriver, right.TransitionDriver) &&
                   left.SuppressWindowTransition == right.SuppressWindowTransition;
        }

        private static bool AssetLoadContextsMatch(
            in UIAssetLoadContext left,
            in UIAssetLoadContext right)
        {
            return string.Equals(left.ConfigBucket, right.ConfigBucket, StringComparison.Ordinal) &&
                   string.Equals(left.ConfigTag, right.ConfigTag, StringComparison.Ordinal) &&
                   string.Equals(left.ConfigOwner, right.ConfigOwner, StringComparison.Ordinal) &&
                   string.Equals(left.PrefabBucket, right.PrefabBucket, StringComparison.Ordinal) &&
                   string.Equals(left.PrefabTag, right.PrefabTag, StringComparison.Ordinal) &&
                   string.Equals(left.PrefabOwner, right.PrefabOwner, StringComparison.Ordinal);
        }

        private static void ThrowIfBindingCallbackFailed(Exception exception)
        {
            if (exception != null)
            {
                throw exception;
            }
        }

        private static void CompleteClose(WindowSession session, List<Exception> errors)
        {
            if (errors == null)
            {
                session.CloseCompletion?.TrySetResult(true);
                return;
            }

            session.CloseCompletion?.TrySetException(CreateCleanupException(
                $"Closing UI window '{session.WindowId}' completed with failures.",
                errors));
        }

        private static void AddCleanupError(
            ref List<Exception> errors,
            Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            errors ??= new List<Exception>(2);
            errors.Add(exception);
        }

        private static Exception CreateCleanupException(
            string message,
            List<Exception> errors)
        {
            return errors.Count == 1
                ? errors[0]
                : new AggregateException(message, errors);
        }

        private async UniTask WaitForInstantiateBudgetAsync(CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
#endif
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int frame = Time.frameCount;
                if (frame != _instantiateFrame)
                {
                    _instantiateFrame = frame;
                    _instantiatesThisFrame = 0;
                }

                if (_instantiatesThisFrame < _maxInstantiatesPerFrame)
                {
                    _instantiatesThisFrame++;
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private static bool ConfigureCanvas(
            UIWindow window,
            UIWindowConfiguration configuration,
            UILayer layer)
        {
            if (configuration.CanvasIsolationPolicy !=
                UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas)
            {
                return false;
            }

            Canvas canvas = window.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = window.gameObject.AddComponent<Canvas>();
            }

            canvas.overrideSorting = false;
            Canvas parent = layer.UICanvas;
            if (parent != null)
            {
                canvas.additionalShaderChannels = parent.additionalShaderChannels;
                canvas.pixelPerfect = parent.pixelPerfect;
            }

            if (window.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                window.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            return true;
        }

        private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            if (_isDisposed || _isShuttingDown)
            {
                return;
            }

            CloseSceneBoundWindowsAsync(nextScene.handle).Forget();
        }

        private async UniTask CloseSceneBoundWindowsAsync(int activeSceneHandle)
        {
            List<string> windowIds = new List<string>(4);
            for (int i = 0; i < _sessionOrder.Count; i++)
            {
                UIWindow window = _sessionOrder[i].Window;
                if (window != null && window.IsSceneBound && window.BoundSceneHandle != activeSceneHandle)
                {
                    windowIds.Add(window.WindowId);
                }
            }

            for (int i = windowIds.Count - 1; i >= 0; i--)
            {
                await CloseSingleAsync(windowIds[i]);
            }
        }

        private void EnsureBinderSetCanChange()
        {
            if (_sessions.Count != 0)
            {
                throw new InvalidOperationException(
                    "Binder registration can only change while no window session is active.");
            }
        }

        private void EnsureUsable()
        {
            EnsureMainThread();
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(UIService));
            }

            if (_isShuttingDown)
            {
                throw new InvalidOperationException("UIService is shutting down.");
            }

            if (_root == null)
            {
                throw new InvalidOperationException("UIRoot has been destroyed.");
            }
        }

        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "UIService may only be used from its owning Unity main thread.");
            }
        }

        private static void DestroyWindow(UIWindow window)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(window.gameObject);
                return;
            }
#endif
            UnityEngine.Object.Destroy(window.gameObject);
        }
    }
}
