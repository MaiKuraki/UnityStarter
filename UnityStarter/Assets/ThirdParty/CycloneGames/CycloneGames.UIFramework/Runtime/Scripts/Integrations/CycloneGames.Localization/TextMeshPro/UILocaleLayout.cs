using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// Applies per-locale RectTransform, text, and layout-group snapshots on locale changes.
    /// </summary>
    /// <remarks>
    /// The component performs no per-frame work. Window instances are normally bound by
    /// <see cref="LocalizationWindowBinder"/>. Non-window UI can bind the component from
    /// its composition root.
    /// </remarks>
    [AddComponentMenu("CycloneGames/UIFramework/UI Locale Layout")]
    [DisallowMultipleComponent]
    public sealed class UILocaleLayout : MonoBehaviour, ILocalizationBindingTarget
    {
        // Field names and types are retained for serialized compatibility.
        [SerializeField] internal string _baseLocale = "en";
        [SerializeField] internal TrackedElement[] _elements = Array.Empty<TrackedElement>();
        [SerializeField] internal LocaleSnapshot[] _snapshots = Array.Empty<LocaleSnapshot>();

        public string BaseLocale => _baseLocale;
        public int TrackedElementCount => _elements != null ? _elements.Length : 0;
        public int LocaleOverrideCount => _snapshots != null ? _snapshots.Length : 0;

        private ElementSnapshot[] _baseSnapshots;
        private ILocalizationService _service;
        private LocaleId _appliedLocale;
        private int _ownerThreadId;
        private bool _isBaked;
        private bool _isSubscribed;

        private void Awake()
        {
            CaptureOwnerThread();
            Bake();
        }

        private void OnEnable()
        {
            CaptureOwnerThread();
            SubscribeIfNeeded();
            if (_service != null)
            {
                Apply(_service.CurrentLocale, true);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            _service = null;
        }

        private void OnValidate()
        {
            _isBaked = false;
            _baseSnapshots = null;
            _appliedLocale = LocaleId.Invalid;
        }

        /// <summary>
        /// Binds the layout to an explicitly owned localization context.
        /// </summary>
        public void Bind(in LocalizationBindingContext context)
        {
            EnsureOwnerThread();
            ILocalizationService service = context.Localization;
            if (service == null)
            {
                throw new ArgumentException(
                    "The localization binding context must contain a service.",
                    nameof(context));
            }

            if (!service.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the localization service before binding a locale layout.");
            }

            if (ReferenceEquals(_service, service))
            {
                SubscribeIfNeeded();
                Apply(service.CurrentLocale, true);
                return;
            }

            Unsubscribe();
            _service = service;
            SubscribeIfNeeded();
            Apply(service.CurrentLocale, true);
        }

        /// <summary>
        /// Releases the current localization binding and event subscription.
        /// </summary>
        public void Unbind()
        {
            EnsureOwnerThread();
            Unsubscribe();
            _service = null;
        }

        private void HandleLocalizationChanged(LocalizationChange change)
        {
            bool force = change.Reason == LocalizationChangeReason.Initialized ||
                         change.Reason == LocalizationChangeReason.Shutdown;
            Apply(change.CurrentLocale, force);
        }

        private void Apply(LocaleId locale, bool force)
        {
            EnsureOwnerThread();
            if (!force && _appliedLocale == locale)
            {
                return;
            }

            Bake();

            int snapshotIndex = FindSnapshot(locale);
            if (snapshotIndex < 0)
            {
                RestoreBase();
                _appliedLocale = locale;
                return;
            }

            LocaleSnapshot localeSnapshot = _snapshots[snapshotIndex];
            if (localeSnapshot.SchemaVersion > LocaleSnapshot.CurrentSchemaVersion)
            {
                RestoreBase();
                _appliedLocale = locale;
                return;
            }

            ElementSnapshot[] elements = localeSnapshot.Elements;
            int count = _elements != null ? _elements.Length : 0;
            bool legacy = localeSnapshot.UsesLegacySchema;

            for (int i = 0; i < count; i++)
            {
                TrackedElement trackedElement = _elements[i];
                _baseSnapshots[i].ApplyTo(trackedElement);

                if (elements == null || i >= elements.Length)
                {
                    continue;
                }

                if (legacy)
                {
                    if (elements[i].IsRuntimeValid(in trackedElement, true))
                    {
                        elements[i].ApplyLegacyTo(trackedElement);
                    }
                }
                else if (elements[i].HasValue &&
                         elements[i].IsRuntimeValid(in trackedElement, false))
                {
                    elements[i].ApplyTo(trackedElement);
                }
            }

            _appliedLocale = locale;
        }

        private void Bake()
        {
            if (_isBaked)
            {
                return;
            }

            _isBaked = true;
            int count = _elements != null ? _elements.Length : 0;
            _baseSnapshots = count == 0
                ? Array.Empty<ElementSnapshot>()
                : new ElementSnapshot[count];

            for (int i = 0; i < count; i++)
            {
                _baseSnapshots[i] = ElementSnapshot.Capture(_elements[i]);
            }
        }

        private void RestoreBase()
        {
            int count = _elements != null ? _elements.Length : 0;
            for (int i = 0; i < count; i++)
            {
                _baseSnapshots[i].ApplyTo(_elements[i]);
            }
        }

        private int FindSnapshot(LocaleId locale)
        {
            string code = locale.Code;
            if (string.IsNullOrEmpty(code) ||
                string.Equals(code, _baseLocale, StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            int exact = FindSnapshotByCode(code);
            if (exact >= 0)
            {
                return exact;
            }

            int separator = code.IndexOf('-');
            if (separator <= 0)
            {
                return -1;
            }

            if (!string.IsNullOrEmpty(_baseLocale) &&
                _baseLocale.Length == separator &&
                string.Compare(
                    _baseLocale,
                    0,
                    code,
                    0,
                    separator,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                return -1;
            }

            return FindSnapshotByCode(code, separator);
        }

        private int FindSnapshotByCode(string code)
        {
            if (_snapshots == null)
            {
                return -1;
            }

            for (int i = 0; i < _snapshots.Length; i++)
            {
                if (string.Equals(
                        _snapshots[i].LocaleCode,
                        code,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindSnapshotByCode(string code, int length)
        {
            if (_snapshots == null)
            {
                return -1;
            }

            for (int i = 0; i < _snapshots.Length; i++)
            {
                string snapshotCode = _snapshots[i].LocaleCode;
                if (snapshotCode != null &&
                    snapshotCode.Length == length &&
                    string.Compare(
                        snapshotCode,
                        0,
                        code,
                        0,
                        length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SubscribeIfNeeded()
        {
            if (_service == null || _isSubscribed || !isActiveAndEnabled)
            {
                return;
            }

            _service.Changed += HandleLocalizationChanged;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed || _service == null)
            {
                _isSubscribed = false;
                return;
            }

            _service.Changed -= HandleLocalizationChanged;
            _isSubscribed = false;
        }

        private void CaptureOwnerThread()
        {
            if (_ownerThreadId == 0)
            {
                if (!PlayerLoopHelper.IsMainThread)
                {
                    throw new InvalidOperationException(
                        "UILocaleLayout must be initialized on the Unity main thread.");
                }

                _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        private void EnsureOwnerThread()
        {
            CaptureOwnerThread();
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "UILocaleLayout can only access Unity UI on its owner main thread.");
            }
        }
    }
}
