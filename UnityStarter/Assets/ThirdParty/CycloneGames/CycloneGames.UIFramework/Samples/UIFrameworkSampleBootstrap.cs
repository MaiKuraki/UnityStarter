using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    /// <summary>
    /// Minimal composition root that opens one directly referenced window configuration.
    /// </summary>
    public sealed class UIFrameworkSampleBootstrap : MonoBehaviour
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
                if (uiRoot == null)
                {
                    throw new InvalidOperationException("UIFramework sample requires a UIRoot reference.");
                }

                if (firstWindowConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "UIFramework sample requires a UIWindowConfiguration reference.");
                }

                var options = new UIServiceOptions
                {
                    InitialWindowCapacity = 4,
                    MaxActiveWindows = 8,
                    MaxInstantiatesPerFrame = 1,
                };

                _uiService = new UIService(uiRoot, options: options);
                UIWindow window = await _uiService.OpenAsync(
                    firstWindowConfiguration,
                    cancellationToken: lifetimeToken);

                Debug.Log($"[UIFrameworkSample] Opened window '{window.WindowId}'.", window);
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
