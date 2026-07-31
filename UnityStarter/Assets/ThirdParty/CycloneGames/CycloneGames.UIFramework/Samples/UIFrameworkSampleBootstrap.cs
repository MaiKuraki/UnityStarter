using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    /// <summary>
    /// Minimal composition root that opens one directly referenced window configuration.
    /// </summary>
    public sealed class UIFrameworkSampleBootstrap : MonoBehaviour
    {
        private static readonly LogChannel Log = UIFrameworkSampleLog.Channel;

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

                Log.Info(
                    window.WindowId,
                    static (windowId, builder) => builder
                        .Append("[UIFrameworkSample] Opened window '")
                        .Append(windowId)
                        .Append("'."));
                await UniTask.WaitUntilCanceled(lifetimeToken);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                // The lifetime finally block owns shutdown.
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "[UIFrameworkSample] Startup failed.");
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
                Log.Error(
                    exception,
                    "[UIFrameworkSample] Shutdown failed.");
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
