using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Optional MonoBehaviour host for projects that want Unity lifecycle ownership.
    /// Runtime window authority remains exclusively in UIService.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour
    {
        [SerializeField] private UIRoot uiRoot;
        [SerializeField, Min(1)] private int initialWindowCapacity = 16;
        [SerializeField, Min(1)] private int maxActiveWindows = 64;
        [SerializeField, Min(1)] private int maxInstantiatesPerFrame = 2;

        private UIService _service;

        public IUIService Service => _service;
        public bool IsInitialized => _service != null && !_service.IsDisposed;

        public IUIService Initialize(
            IUIWindowAssetProvider assetProvider = null,
            IUIWindowTransitionDriver transitionDriver = null,
            IUINavigationService navigationService = null,
            IReadOnlyList<IUIWindowBinder> binders = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("UIManager is already initialized.");
            }

            if (uiRoot == null)
            {
                throw new InvalidOperationException("UIManager requires an explicit UIRoot reference.");
            }

            UIServiceOptions options = new UIServiceOptions
            {
                InitialWindowCapacity = initialWindowCapacity,
                MaxActiveWindows = Mathf.Max(initialWindowCapacity, maxActiveWindows),
                MaxInstantiatesPerFrame = maxInstantiatesPerFrame,
                DefaultTransitionDriver = transitionDriver,
                NavigationService = navigationService,
            };

            _service = new UIService(uiRoot, assetProvider, options, binders);
            return _service;
        }

        public UniTask ShutdownAsync(
            UIShutdownMode mode = UIShutdownMode.Immediate,
            CancellationToken cancellationToken = default)
        {
            return _service != null
                ? _service.ShutdownAsync(mode, cancellationToken)
                : UniTask.CompletedTask;
        }

        private void OnDestroy()
        {
            _service?.Dispose();
            _service = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            initialWindowCapacity = Mathf.Max(1, initialWindowCapacity);
            maxActiveWindows = Mathf.Max(initialWindowCapacity, maxActiveWindows);
            maxInstantiatesPerFrame = Mathf.Max(1, maxInstantiatesPerFrame);
        }
#endif
    }
}
