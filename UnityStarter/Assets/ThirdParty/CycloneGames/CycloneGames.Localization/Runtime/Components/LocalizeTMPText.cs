using System;
using CycloneGames.Localization.Core;
using TMPro;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Auto-updates a TMP_Text component when the active locale changes.
    /// Zero per-frame cost: event-driven refresh only.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("CycloneGames/Localization/Localize TMP Text")]
    [DisallowMultipleComponent]
    public sealed class LocalizeTMPText : MonoBehaviour
    {
        [SerializeField] private LocalizedString localizedString;

        private TMP_Text _text;
        private ILocalizationService _service;
        private object[] _arguments;
        private float _numericArg0;
        private float _numericArg1;
        private float _numericArg2;
        private byte _numericArgumentCount;
        private int _pluralCount;
        private bool _isPluralMode;

        public LocalizedString LocalizedString
        {
            get => localizedString;
            set
            {
                localizedString = value;
                Refresh();
            }
        }

        /// <summary>
        /// Sets format arguments for non-plural text. Template: "Deals {0} {1} damage".
        /// Mutually exclusive with <see cref="SetPluralArguments"/>.
        /// </summary>
        public void SetArguments(params object[] args)
        {
            _isPluralMode = false;
            _numericArgumentCount = 0;
            _arguments = args;
            Refresh();
        }

        /// <summary>
        /// Sets one numeric argument through TMP_Text.SetText to avoid params array allocation.
        /// </summary>
        public void SetNumericArguments(float arg0)
        {
            _isPluralMode = false;
            _arguments = null;
            _numericArgumentCount = 1;
            _numericArg0 = arg0;
            Refresh();
        }

        /// <summary>
        /// Sets two numeric arguments through TMP_Text.SetText to avoid params array allocation.
        /// </summary>
        public void SetNumericArguments(float arg0, float arg1)
        {
            _isPluralMode = false;
            _arguments = null;
            _numericArgumentCount = 2;
            _numericArg0 = arg0;
            _numericArg1 = arg1;
            Refresh();
        }

        /// <summary>
        /// Sets three numeric arguments through TMP_Text.SetText to avoid params array allocation.
        /// </summary>
        public void SetNumericArguments(float arg0, float arg1, float arg2)
        {
            _isPluralMode = false;
            _arguments = null;
            _numericArgumentCount = 3;
            _numericArg0 = arg0;
            _numericArg1 = arg1;
            _numericArg2 = arg2;
            Refresh();
        }

        /// <summary>
        /// Sets plural count and optional extra arguments. The count determines which plural form
        /// to use (via CLDR rules) and is auto-injected as {0}. Extra args become {1}, {2}, etc.
        /// <para>
        /// StringTable keys use suffixes: "item_count.one", "item_count.other", etc.
        /// The base key in <see cref="localizedString"/> should be "item_count" (without suffix).
        /// </para>
        /// </summary>
        public void SetPluralArguments(int count, params object[] extraArgs)
        {
            _isPluralMode = true;
            _numericArgumentCount = 0;
            _pluralCount = count;
            _arguments = extraArgs;
            Refresh();
        }

        public void Bind(ILocalizationService service)
        {
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;

            _service = service;

            if (_service != null)
            {
                _service.OnLocaleChanged += OnLocaleChanged;
                Refresh();
            }
        }

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        private void OnDestroy()
        {
            if (_service != null)
                _service.OnLocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged(LocaleId _) => Refresh();

        private void Refresh()
        {
            if (_text == null || _service == null || !localizedString.IsValid) return;

            string resolved;
            if (_isPluralMode)
                resolved = _service.GetPluralString(localizedString, _pluralCount, _arguments);
            else if (_numericArgumentCount > 0)
            {
                if (!_service.TryGetString(localizedString, out resolved) || resolved == null)
                    return;

                ApplyNumericTemplate(resolved);
                return;
            }
            else if (_arguments != null && _arguments.Length > 0)
                resolved = _service.GetFormattedString(localizedString, _arguments);
            else
                resolved = _service.GetString(localizedString);

            // null means missing key, so keep the designer placeholder text.
            if (resolved != null)
                _text.text = resolved;
        }

        private void ApplyNumericTemplate(string template)
        {
            switch (_numericArgumentCount)
            {
                case 1:
                    _text.SetText(template, _numericArg0);
                    break;
                case 2:
                    _text.SetText(template, _numericArg0, _numericArg1);
                    break;
                default:
                    _text.SetText(template, _numericArg0, _numericArg1, _numericArg2);
                    break;
            }
        }
    }
}
