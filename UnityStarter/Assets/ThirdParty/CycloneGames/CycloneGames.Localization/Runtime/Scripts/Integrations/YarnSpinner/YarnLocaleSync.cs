using CycloneGames.Localization.Runtime;
using UnityEngine;
using Yarn.Unity;

namespace CycloneGames.Localization.Runtime.Integrations.YarnSpinner
{
    /// <summary>
    /// Bridges CycloneGames.Localization locale changes to Yarn Spinner's LineProvider.
    /// Attach to the same GameObject as DialogueRunner or any persistent object.
    /// When the global locale changes, this component updates Yarn's LineProvider.LocaleCode
    /// so dialogue text automatically resolves in the correct language.
    /// No modification to Yarn Spinner source required.
    /// </summary>
    [AddComponentMenu("CycloneGames/Localization/Yarn Spinner Locale Bridge")]
    public sealed class YarnLocaleSync : MonoBehaviour
    {
        [SerializeField] private DialogueRunner dialogueRunner;

        private ILocalizationService _service;

        public void Bind(ILocalizationService service)
        {
            Unbind();

            _service = service;
            _service.OnLocaleChanged += OnLocaleChanged;

            SyncLocale(_service.CurrentLocale);
        }

        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            if (_service != null)
            {
                _service.OnLocaleChanged -= OnLocaleChanged;
                _service = null;
            }
        }

        private void OnLocaleChanged(LocaleId newLocale) => SyncLocale(newLocale);

        private void SyncLocale(LocaleId locale)
        {
            if (dialogueRunner == null || !locale.IsValid) return;

            var provider = dialogueRunner.LineProvider;
            if (provider == null) return;

            // ILineProvider exposes LocaleCode as read-only, but Yarn's concrete providers
            // derive from LineProviderBehaviour, which owns the mutable property.
            if (provider is LineProviderBehaviour behaviour)
                behaviour.LocaleCode = locale.Code;
        }
    }
}
