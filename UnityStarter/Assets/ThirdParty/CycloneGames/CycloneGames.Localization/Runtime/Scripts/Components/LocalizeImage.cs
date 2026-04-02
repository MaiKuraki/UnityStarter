using CycloneGames.AssetManagement.Runtime;
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
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;

            ReleaseHandle();
        }

        private void OnLocaleChanged(LocaleId _) => Refresh();

        private async void Refresh()
        {
            if (_image == null || _service == null || _package == null) return;
            if (!localizedAsset.IsValid) return;

            var assetRef = _service.ResolveAsset(localizedAsset);
            if (!assetRef.IsValid) return;

            ReleaseHandle();

            var handle = _package.LoadAsync(assetRef);
            _currentHandle = handle;

            await handle.Task;

            // Guard: component may have been destroyed or a newer Refresh() call may have replaced _currentHandle
            if (_currentHandle != handle || this == null) { handle.Dispose(); return; }
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
