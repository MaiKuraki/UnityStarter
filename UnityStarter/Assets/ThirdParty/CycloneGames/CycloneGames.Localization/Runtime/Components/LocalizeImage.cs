using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Event-driven localized Image binding with explicit handle and cancellation ownership.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("CycloneGames/Localization/Localize Image")]
    [DisallowMultipleComponent]
    public sealed class LocalizeImage : MonoBehaviour, ILocalizationBindingTarget
    {
        [SerializeField] private LocalizedAsset<Sprite> localizedAsset;

        private Image _image;
        private ILocalizationService _service;
        private IAssetPackage _package;
        private IAssetHandle<Sprite> _currentHandle;
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

        public void Bind(in LocalizationBindingContext context)
        {
            if (context.AssetPackage == null)
                throw new ArgumentException("LocalizeImage requires an asset package.", nameof(context));

            Unbind();
            _service = context.Localization;
            _package = context.AssetPackage;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        public void Unbind()
        {
            Unsubscribe();
            CancelPendingRefresh();
            ReleaseCurrentHandle(true);
            _service = null;
            _package = null;
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
            ReleaseCurrentHandle(true);
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
                ReleaseCurrentHandle(true);
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
            if (!isActiveAndEnabled || _image == null || _service == null || _package == null ||
                !localizedAsset.IsValid)
            {
                ReleaseCurrentHandle(true);
                return;
            }

            AssetRef<Sprite> assetRef = _service.ResolveAsset(localizedAsset);
            if (!assetRef.IsValid)
            {
                ReleaseCurrentHandle(true);
                return;
            }

            int version = ++_refreshVersion;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            _refreshCancellation = cancellation;
            IAssetPackage package = _package;
            RefreshAsync(version, assetRef, package, cancellation).Forget();
        }

        private async UniTaskVoid RefreshAsync(
            int version,
            AssetRef<Sprite> assetRef,
            IAssetPackage package,
            CancellationTokenSource cancellation)
        {
            IAssetHandle<Sprite> candidate = null;
            try
            {
                candidate = package.LoadAsync(assetRef, cancellationToken: cancellation.Token);
                await candidate.Task;

                if (this == null || cancellation.IsCancellationRequested || version != _refreshVersion ||
                    !isActiveAndEnabled || candidate.Asset == null)
                {
                    return;
                }

                IAssetHandle<Sprite> previous = _currentHandle;
                _currentHandle = candidate;
                candidate = null;
                _appliedSprite = _currentHandle.Asset;
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
                    Debug.LogException(exception, this);
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

        private void ReleaseCurrentHandle(bool clearImage)
        {
            IAssetHandle<Sprite> handle = _currentHandle;
            _currentHandle = null;
            if (clearImage && _image != null && _image.sprite == _appliedSprite)
                _image.sprite = _designerSprite;
            _appliedSprite = null;
            handle?.Dispose();
        }
    }
}
