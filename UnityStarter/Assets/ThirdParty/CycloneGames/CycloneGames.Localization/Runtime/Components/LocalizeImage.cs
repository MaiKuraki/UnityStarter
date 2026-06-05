using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Auto-updates an Image sprite when the active locale changes.
    /// Manages asset handle lifetime: disposes old handle before loading new locale variant.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("CycloneGames/Localization/Localize Image")]
    [DisallowMultipleComponent]
    public sealed class LocalizeImage : MonoBehaviour
    {
        [SerializeField] private LocalizedAsset<Sprite> localizedAsset;

        private Image _image;
        private ILocalizationService _service;
        private IAssetPackage _package;
        private IAssetHandle<Sprite> _currentHandle;
        private int _refreshVersion;

        public void Bind(ILocalizationService service, IAssetPackage package)
        {
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;

            _service = service;
            _package = package;

            if (_service != null)
            {
                _service.OnLocaleChanged += OnLocaleChanged;
                Refresh();
            }
        }

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void OnDestroy()
        {
            _refreshVersion++;

            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;

            ReleaseHandle();
        }

        private void OnLocaleChanged(LocaleId _) => Refresh();

        private void Refresh()
        {
            RefreshAsync(++_refreshVersion, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid RefreshAsync(int version, CancellationToken cancellationToken)
        {
            if (_image == null || _service == null || _package == null) return;
            if (!localizedAsset.IsValid) return;

            var assetRef = _service.ResolveAsset(localizedAsset);
            if (!assetRef.IsValid) return;

            ReleaseHandle();

            var handle = _package.LoadAsync(assetRef, cancellationToken: cancellationToken);
            _currentHandle = handle;

            try
            {
                await handle.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (_currentHandle == handle)
                    _currentHandle = null;
                handle.Dispose();
                return;
            }

            if (version != _refreshVersion || _currentHandle != handle || this == null) { handle.Dispose(); return; }
            if (handle.Asset != null) _image.sprite = handle.Asset;
        }

        private void ReleaseHandle()
        {
            if (_currentHandle != null)
            {
                _currentHandle.Dispose();
                _currentHandle = null;
            }
        }
    }
}
