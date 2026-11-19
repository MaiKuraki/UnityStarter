using System;
using System.Threading;
using CycloneGames.Localization.Core;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Event-driven localized Image binding with explicit lease and cancellation ownership.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("CycloneGames/Localization/Localize Image")]
    [DisallowMultipleComponent]
    public sealed class LocalizeImage : MonoBehaviour, ILocalizationBindingTarget
    {
        private static readonly LogChannel Log = LocalizationLog.Channel;

        [SerializeField] private LocalizedAsset<Sprite> localizedAsset;

        private Image _image;
        private ILocalizationService _service;
        private ILocalizedAssetProvider _provider;
        private ILocalizedAssetLease<Sprite> _currentLease;
        private CancellationTokenSource _refreshCancellation;
        private Sprite _designerSprite;
        private Sprite _appliedSprite;
        private int _refreshVersion;
        private bool _subscribed;

        public LocalizedAsset<Sprite> LocalizedAsset
        {
            get => localizedAsset;
            set
            {
                localizedAsset = value;
                Refresh();
            }
        }

        /// <summary>
        /// Provider used to load the resolved sprite. Assigning it is a precondition of
        /// <see cref="Bind"/>, which throws <see cref="InvalidOperationException"/> when it is null.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="LocalizationBindingContext"/> carries no asset types, so the provider is
        /// assigned on the component itself and only components that resolve assets need one.

        /// </para>
        /// <para>
        /// The composition root assigns it before binding, typically right after it collects the
        /// binding targets it is about to hand to the window binder:
        /// <c>GetComponent&lt;LocalizeImage&gt;().AssetProvider = provider;</c>.
        /// A component that reaches <see cref="Bind"/> without a provider fails the whole window
        /// binding.
        /// </para>
        /// </remarks>
        public ILocalizedAssetProvider AssetProvider { get; set; }

        public void Bind(in LocalizationBindingContext context)
        {
            // Localized asset resolution is a CycloneGames capability, not part of the
            // backend-agnostic contract, so this component narrows the provider it was handed.
            if (!(context.Localization is ILocalizationService service))
            {
                throw new ArgumentException(
                    "LocalizeImage resolves localized assets and therefore requires an "
                    + nameof(ILocalizationService) + " provider.",
                    nameof(context));
            }

            if (AssetProvider == null)
            {
                throw new InvalidOperationException(
                    "LocalizeImage requires an asset provider. Assign " + nameof(AssetProvider)
                    + " before binding the component.");
            }

            Unbind();
            _service = service;
            _provider = AssetProvider;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        public void Unbind()
        {
            Unsubscribe();
            CancelPendingRefresh();
            ReleaseCurrentLease(true);
            _service = null;
            _provider = null;
        }

        private void Awake()
        {
            _image = GetComponent<Image>();
            _designerSprite = _image.sprite;
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            CancelPendingRefresh();
            ReleaseCurrentLease(true);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnLocalizationChanged(LocalizationChange change)
        {
            if (change.Reason == LocalizationChangeReason.Shutdown)
            {
                CancelPendingRefresh();
                ReleaseCurrentLease(true);
                return;
            }

            if (change.Reason == LocalizationChangeReason.LocaleChanged ||
                change.Reason == LocalizationChangeReason.ContentChanged ||
                change.Reason == LocalizationChangeReason.Initialized)
            {
                Refresh();
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _service == null) return;
            _service.Changed += OnLocalizationChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _service.Changed -= OnLocalizationChanged;
            _subscribed = false;
        }

        private void Refresh()
        {
            CancelPendingRefresh();
            if (!isActiveAndEnabled || _image == null || _service == null || _provider == null ||
                !localizedAsset.IsValid)
            {
                ReleaseCurrentLease(true);
                return;
            }

            LocalizedAssetRef<Sprite> assetRef = _service.ResolveAsset(localizedAsset);
            if (!assetRef.IsValid)
            {
                ReleaseCurrentLease(true);
                return;
            }

            int version = ++_refreshVersion;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            _refreshCancellation = cancellation;
            ILocalizedAssetProvider provider = _provider;
            RefreshAsync(version, assetRef, provider, cancellation).Forget();
        }

        private async UniTaskVoid RefreshAsync(
            int version,
            LocalizedAssetRef<Sprite> assetRef,
            ILocalizedAssetProvider provider,
            CancellationTokenSource cancellation)
        {
            ILocalizedAssetLease<Sprite> candidate = null;
            try
            {
                candidate = await provider.AcquireAsync<Sprite>(assetRef.Location, cancellation.Token);

                if (this == null || cancellation.IsCancellationRequested || version != _refreshVersion ||
                    !isActiveAndEnabled || candidate.Asset == null)
                {
                    return;
                }

                ILocalizedAssetLease<Sprite> previous = _currentLease;
                _currentLease = candidate;
                candidate = null;
                _appliedSprite = _currentLease.Asset;
                _image.sprite = _appliedSprite;
                previous?.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected when a binding changes, disables, or is destroyed.
            }
            catch (Exception exception)
            {
                if (this != null)
                    Log.Error(exception, "Localized image refresh failed.");
            }
            finally
            {
                candidate?.Dispose();
                if (ReferenceEquals(_refreshCancellation, cancellation))
                    _refreshCancellation = null;
                cancellation.Dispose();
            }
        }

        private void CancelPendingRefresh()
        {
            _refreshVersion++;
            CancellationTokenSource cancellation = _refreshCancellation;
            _refreshCancellation = null;
            if (cancellation == null) return;
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The async completion path won the race and already disposed this source.
            }
        }

        private void ReleaseCurrentLease(bool clearImage)
        {
            ILocalizedAssetLease<Sprite> lease = _currentLease;
            _currentLease = null;
            if (clearImage && _image != null && _image.sprite == _appliedSprite)
                _image.sprite = _designerSprite;
            _appliedSprite = null;
            lease?.Dispose();
        }
    }
}
