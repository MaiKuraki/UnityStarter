using System;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using TMPro;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// Per-prefab locale layout manager. The prefab's native state IS the base locale.
    /// no snapshot is stored for it, only for override locales.
    /// <para>
    /// <b>Editor workflow</b>: design base-language layout as normal, add override locales,
    /// adjust, then Capture. Preview mode is for safe read-only comparison.
    /// <b>Runtime</b>: if the current locale has a snapshot, apply it; otherwise keep base (0 GC).
    /// </para>
    /// </summary>
    [AddComponentMenu("CycloneGames/UIFramework/UI Locale Layout")]
    [DisallowMultipleComponent]
    public sealed class UILocaleLayout : MonoBehaviour, ILocaleResponder
    {
        [SerializeField] internal string _baseLocale = "en";
        [SerializeField] internal TrackedElement[] _elements = Array.Empty<TrackedElement>();
        [SerializeField] internal LocaleSnapshot[] _snapshots = Array.Empty<LocaleSnapshot>();

        // Baked parallel arrays for 0-GC runtime access
        private RectTransform[] _rects;
        private TMP_Text[] _texts;
        // Base layout captured at Awake from the prefab's natural state.
        private ElementSnapshot[] _baseSnapshots;
        private ILocalizationService _service;
        private LocaleId _appliedLocale;
        private bool _baked;
        private bool _selfSubscribed;

        private void Awake() => Bake();

        private void OnEnable()
        {
            if (GetComponentInParent<UIWindow>(true) != null)
                return;

            _service ??= UIServiceLocator.Get<ILocalizationService>();
            if (_service == null) return;
            _service.OnLocaleChanged += HandleLocaleChanged;
            _selfSubscribed = true;
            Apply(_service.CurrentLocale);
        }

        private void OnDisable()
        {
            if (_selfSubscribed && _service != null)
                _service.OnLocaleChanged -= HandleLocaleChanged;
            _selfSubscribed = false;
        }

        // ILocaleResponder
        public void OnLocaleChanged(LocaleId newLocale) => Apply(newLocale);

        private void HandleLocaleChanged(LocaleId locale) => Apply(locale);

        private void Bake()
        {
            if (_baked) return;
            _baked = true;
            int count = _elements.Length;
            _rects = new RectTransform[count];
            _texts = new TMP_Text[count];
            _baseSnapshots = new ElementSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                _rects[i] = _elements[i].Target;
                _texts[i] = _elements[i].Text;
                _baseSnapshots[i] = ElementSnapshot.Capture(_rects[i], _texts[i]);
            }
        }

        private void Apply(LocaleId locale)
        {
            if (_appliedLocale == locale) return;
            _appliedLocale = locale;
            if (!_baked) Bake();

            int snapIdx = FindSnapshot(locale);
            int count = _rects.Length;

            if (snapIdx < 0)
            {
                // No override: restore base layout.
                for (int i = 0; i < count; i++)
                    _baseSnapshots[i].ApplyTo(_rects[i], _texts[i]);
                return;
            }

            ref var snap = ref _snapshots[snapIdx];
            if (snap.Elements == null) return;

            int applyCount = Math.Min(count, snap.Elements.Length);
            for (int i = 0; i < applyCount; i++)
                snap.Elements[i].ApplyTo(_rects[i], _texts[i]);
        }

        private int FindSnapshot(LocaleId locale)
        {
            string code = locale.Code;
            if (string.IsNullOrEmpty(code)) return -1;

            for (int i = 0; i < _snapshots.Length; i++)
            {
                if (string.Equals(_snapshots[i].LocaleCode, code, StringComparison.Ordinal))
                    return i;
            }

            int dash = code.IndexOf('-');
            if (dash > 0)
            {
                for (int i = 0; i < _snapshots.Length; i++)
                {
                    string snapshotCode = _snapshots[i].LocaleCode;
                    if (snapshotCode != null &&
                        snapshotCode.Length == dash &&
                        string.Compare(snapshotCode, 0, code, 0, dash, StringComparison.Ordinal) == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }
    }
}
