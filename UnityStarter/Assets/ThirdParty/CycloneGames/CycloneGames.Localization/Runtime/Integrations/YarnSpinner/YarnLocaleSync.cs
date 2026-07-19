using CycloneGames.Localization.Core;
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
    public sealed class YarnLocaleSync : MonoBehaviour, ILocalizationBindingTarget
    {
        [SerializeField] private DialogueRunner dialogueRunner;

        private ILocalizationService _service;
        private bool _subscribed;

        public void Bind(in LocalizationBindingContext context)
        {
            Unbind();

            _service = context.Localization;
            if (isActiveAndEnabled) Subscribe();
            SyncLocale(_service.CurrentLocale);
        }

        public void Unbind()
        {
            Unsubscribe();
            _service = null;
        }

        private void OnEnable()
        {
            Subscribe();
            if (_service != null) SyncLocale(_service.CurrentLocale);
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unbind();

        private void OnLocalizationChanged(LocalizationChange change)
        {
            if (change.Reason != LocalizationChangeReason.Shutdown)
                SyncLocale(change.CurrentLocale);
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
