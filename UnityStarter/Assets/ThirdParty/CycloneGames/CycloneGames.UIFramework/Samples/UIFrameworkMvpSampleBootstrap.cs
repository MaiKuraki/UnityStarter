using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    /// <summary>
    /// Optional composition root demonstrating an instance-owned MVP binder.
    /// Use this component instead of UIFrameworkSampleBootstrap in a separate scene.
    /// </summary>
    public sealed class UIFrameworkMvpSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private UIRoot uiRoot;
        [SerializeField] private UIWindowConfiguration firstWindowConfiguration;

        private IUIService _uiService;

        private void Start()
        {
            RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask RunAsync(CancellationToken lifetimeToken)
        {
            try
            {
                if (uiRoot == null || firstWindowConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "MVP sample requires explicit UIRoot and UIWindowConfiguration references.");
                }

                var presenterBinder = new UIPresenterBinder(initialCapacity: 1);
                presenterBinder.Register<SampleUIPresenter>(firstWindowConfiguration.WindowId);

                var options = new UIServiceOptions
                {
                    InitialWindowCapacity = 4,
                    MaxActiveWindows = 8,
                    MaxInstantiatesPerFrame = 1,
                };
                IUIWindowBinder[] binders = { presenterBinder };

                _uiService = new UIService(
                    uiRoot,
                    assetProvider: null,
                    options: options,
                    binders: binders);

                await _uiService.OpenAsync(
                    firstWindowConfiguration,
                    cancellationToken: lifetimeToken);
                await UniTask.WaitUntilCanceled(lifetimeToken);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                // The lifetime finally block owns shutdown.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                await ShutdownServiceAsync();
            }
        }

        private async UniTask ShutdownServiceAsync()
        {
            IUIService service = _uiService;
            _uiService = null;
            if (service == null)
            {
                return;
            }

            try
            {
                await service.ShutdownAsync(UIShutdownMode.Immediate, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                if (!service.IsDisposed)
                {
                    service.Dispose();
                }
            }
        }
    }
}
