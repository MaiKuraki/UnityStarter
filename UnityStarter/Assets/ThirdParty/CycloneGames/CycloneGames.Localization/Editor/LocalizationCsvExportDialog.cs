#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    internal enum LocalizationCsvExportProfile : byte
    {
        Spreadsheet,
        Automation,
    }

    /// <summary>
    /// Selects which authoring keys an export includes.
    /// </summary>
    internal enum LocalizationCsvExportKeyScope : byte
    {
        /// <summary>Every key present in the authoring locale table.</summary>
        All = 0,

        /// <summary>Only keys matching the table editor's current search/duplicate filter.</summary>
        CurrentResults = 1,

        /// <summary>
        /// Only keys that still need translation work in at least one exported locale: an
        /// absent or empty value, a <see cref="TranslationStatus.Missing"/> or
        /// <see cref="TranslationStatus.Stale"/> status, or a translated source revision behind
        /// the current source revision.
        /// </summary>
        NeedsTranslation = 2,
    }

    internal readonly struct LocalizationCsvExportLanguageOption
    {
        public readonly string LocaleCode;
        public readonly bool IsRegistered;

        public LocalizationCsvExportLanguageOption(string localeCode, bool isRegistered)
        {
            LocaleCode = localeCode ?? string.Empty;
            IsRegistered = isRegistered;
        }
    }

    internal readonly struct LocalizationCsvExportSelection
    {
        public readonly LocalizationCsvExportProfile Profile;
        public readonly LocalizationCsvExportKeyScope KeyScope;
        public readonly int? TargetColumnIndex;
        public readonly bool RegisteredLocalesOnly;

        public LocalizationCsvEncoding Encoding
        {
            get
            {
                switch (Profile)
                {
                    case LocalizationCsvExportProfile.Spreadsheet:
                        return LocalizationCsvEncoding.Utf8WithBom;
                    case LocalizationCsvExportProfile.Automation:
                        return LocalizationCsvEncoding.Utf8WithoutBom;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Profile));
                }
            }
        }

        public LocalizationCsvExportSelection(
            LocalizationCsvExportProfile profile,
            LocalizationCsvExportKeyScope keyScope,
            int? targetColumnIndex,
            bool registeredLocalesOnly)
        {
            Profile = profile;
            KeyScope = keyScope;
            TargetColumnIndex = targetColumnIndex;
            RegisteredLocalesOnly = registeredLocalesOnly;
        }
    }

    internal sealed class LocalizationCsvExportDialog : EditorWindow
    {
        private static readonly string[] ProfileLabels =
        {
            "Spreadsheet (Recommended)",
            "Automation & CI",
        };

        private const float WindowWidth = 520f;
        private const float WindowHeight = 440f;

        private string _tableId;
        private int _allKeyCount;
        private bool _hasActiveFilter;
        private int _filteredKeyCount;
        private string[] _languageOptions = Array.Empty<string>();
        private bool[] _targetLocaleRegistration = Array.Empty<bool>();
        private int _registeredLanguageCount;
        private bool _hasUnregisteredLocales;
        private LocalizationCsvExportProfile _profile;
        private LocalizationCsvExportKeyScope _keyScope;
        private int _languageSelection;
        private Action<LocalizationCsvExportSelection> _onExport;
        private LocalizationCsvExportSelection _pendingExportSelection;
        private Action<LocalizationCsvExportSelection> _pendingExportCallback;
        private EditorApplication.CallbackFunction _delayedExportInvoker;
        private Func<int?, bool, int> _countKeysNeedingTranslation;
        private int _needsTranslationCount = -1;
        private int _needsTranslationCountSelection = -1;

        public static void Open(
            EditorWindow owner,
            string tableId,
            int allKeyCount,
            bool hasActiveFilter,
            int filteredKeyCount,
            IReadOnlyList<LocalizationCsvExportLanguageOption> targetLocales,
            Func<int?, bool, int> countKeysNeedingTranslation,
            Action<LocalizationCsvExportSelection> onExport)
        {
            if (onExport == null)
                throw new ArgumentNullException(nameof(onExport));

            var window = CreateInstance<LocalizationCsvExportDialog>();
            window.titleContent = new GUIContent("Export Localization");
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = window.minSize;
            window._tableId = tableId ?? string.Empty;
            window._allKeyCount = Math.Max(0, allKeyCount);
            window._hasActiveFilter = hasActiveFilter;
            window._filteredKeyCount = Math.Max(0, filteredKeyCount);
            window._profile = LocalizationCsvExportProfile.Spreadsheet;
            window._keyScope = LocalizationCsvExportKeyScope.All;
            window._languageSelection = 0;
            window._onExport = onExport;
            window._countKeysNeedingTranslation = countKeysNeedingTranslation;

            int targetCount = targetLocales?.Count ?? 0;
            window._languageOptions = new string[targetCount + 1];
            window._targetLocaleRegistration = new bool[targetCount];
            window._registeredLanguageCount = 1;
            for (int index = 0; index < targetCount; index++)
            {
                LocalizationCsvExportLanguageOption option = targetLocales[index];
                window._targetLocaleRegistration[index] = option.IsRegistered;
                if (option.IsRegistered)
                    window._registeredLanguageCount++;
                else
                    window._hasUnregisteredLocales = true;
            }

            window._languageOptions[0] = window._hasUnregisteredLocales
                ? "All Registered Languages (" + window._registeredLanguageCount + ")"
                : "All Languages (" + (targetCount + 1) + ")";
            for (int index = 0; index < targetCount; index++)
            {
                LocalizationCsvExportLanguageOption option = targetLocales[index];
                window._languageOptions[index + 1] = "Source + " + option.LocaleCode +
                                                     (option.IsRegistered ? string.Empty : "  [Not in Settings]");
            }

            if (owner != null)
            {
                Rect ownerPosition = owner.position;
                window.position = new Rect(
                    ownerPosition.center.x - WindowWidth * 0.5f,
                    ownerPosition.center.y - WindowHeight * 0.5f,
                    WindowWidth,
                    WindowHeight);
            }

            window.ShowUtility();
            window.Focus();
        }

        private void OnDisable()
        {
            _onExport = null;
        }

        /// <summary>
        /// Cached delayCall handler that preserves the one-shot deferred export after the
        /// dialog window closes, without allocating a closure per export click.
        /// </summary>
        private void InvokePendingExport()
        {
            EditorApplication.delayCall -= _delayedExportInvoker;
            Action<LocalizationCsvExportSelection> callback = _pendingExportCallback;
            _pendingExportCallback = null;
            LocalizationCsvExportSelection selection = _pendingExportSelection;
            _pendingExportSelection = default;
            callback?.Invoke(selection);
        }

        /// <summary>
        /// Counts keys that still need translation for the currently selected language scope.
        /// The result is cached because the owner computes it from serialized metadata, which is
        /// too expensive to re-evaluate on every repaint.
        /// </summary>
        private int GetNeedsTranslationCount()
        {
            if (_countKeysNeedingTranslation == null)
                return 0;

            if (_needsTranslationCountSelection == _languageSelection && _needsTranslationCount >= 0)
                return _needsTranslationCount;

            _needsTranslationCount = Math.Max(
                0,
                _countKeysNeedingTranslation(
                    _languageSelection == 0 ? (int?)null : _languageSelection,
                    _languageSelection == 0 && _hasUnregisteredLocales));
            _needsTranslationCountSelection = _languageSelection;
            return _needsTranslationCount;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Export Localization", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Table: " + _tableId, EditorStyles.miniLabel);
            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
            _profile = (LocalizationCsvExportProfile)GUILayout.Toolbar((int)_profile, ProfileLabels);
            EditorGUILayout.HelpBox(
                _profile == LocalizationCsvExportProfile.Spreadsheet
                    ? "For planners and translators using Excel or other spreadsheet applications. Encoding: UTF-8 with BOM."
                    : "For scripts, source control, build pipelines, and CI. Encoding: UTF-8 without BOM.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Languages", EditorStyles.boldLabel);
            _languageSelection = EditorGUILayout.Popup(_languageSelection, _languageOptions);

            bool selectedUnregisteredLocale = _languageSelection > 0 &&
                                              _languageSelection <= _targetLocaleRegistration.Length &&
                                              !_targetLocaleRegistration[_languageSelection - 1];
            if (selectedUnregisteredLocale)
            {
                EditorGUILayout.HelpBox(
                    "This locale table is not registered in LocalizationSettings. Export is allowed for backup or migration, but the locale is not active runtime content.",
                    MessageType.Warning);
            }

            // The Needs Translation count depends on the language selection, so the language
            // selector is drawn first and the count is evaluated against the committed value.
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Key Scope", EditorStyles.boldLabel);
            if (GUILayout.Toggle(
                    _keyScope == LocalizationCsvExportKeyScope.All,
                    "All Keys (" + _allKeyCount.ToString(CultureInfo.InvariantCulture) + ")",
                    EditorStyles.radioButton))
            {
                _keyScope = LocalizationCsvExportKeyScope.All;
            }

            bool canUseFiltered = _hasActiveFilter && _filteredKeyCount > 0;
            using (new EditorGUI.DisabledScope(!canUseFiltered))
            {
                string filteredLabel = _hasActiveFilter
                    ? "Current Results (" + _filteredKeyCount.ToString(CultureInfo.InvariantCulture) + ")"
                    : "Current Results (No Filter)";
                if (GUILayout.Toggle(
                        _keyScope == LocalizationCsvExportKeyScope.CurrentResults,
                        filteredLabel,
                        EditorStyles.radioButton))
                {
                    _keyScope = LocalizationCsvExportKeyScope.CurrentResults;
                }
            }
            if (!canUseFiltered && _keyScope == LocalizationCsvExportKeyScope.CurrentResults)
                _keyScope = LocalizationCsvExportKeyScope.All;

            int needsTranslationCount = GetNeedsTranslationCount();
            bool canUseNeedsTranslation = needsTranslationCount > 0;
            using (new EditorGUI.DisabledScope(!canUseNeedsTranslation))
            {
                if (GUILayout.Toggle(
                        _keyScope == LocalizationCsvExportKeyScope.NeedsTranslation,
                        "Needs Translation (" + needsTranslationCount.ToString(CultureInfo.InvariantCulture) + ")",
                        EditorStyles.radioButton))
                {
                    _keyScope = LocalizationCsvExportKeyScope.NeedsTranslation;
                }
            }
            if (!canUseNeedsTranslation && _keyScope == LocalizationCsvExportKeyScope.NeedsTranslation)
                _keyScope = LocalizationCsvExportKeyScope.All;

            int keyCount;
            switch (_keyScope)
            {
                case LocalizationCsvExportKeyScope.CurrentResults:
                    keyCount = _filteredKeyCount;
                    break;
                case LocalizationCsvExportKeyScope.NeedsTranslation:
                    keyCount = needsTranslationCount;
                    break;
                default:
                    keyCount = _allKeyCount;
                    break;
            }
            int languageCount = _languageSelection == 0
                ? (_hasUnregisteredLocales ? _registeredLanguageCount : _languageOptions.Length)
                : Math.Min(2, _languageOptions.Length);
            string profileLabel = _profile == LocalizationCsvExportProfile.Spreadsheet
                ? "Spreadsheet"
                : "Automation & CI";
            string encodingLabel = _profile == LocalizationCsvExportProfile.Spreadsheet
                ? "UTF-8 with BOM"
                : "UTF-8 without BOM";
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Summary: " + keyCount.ToString(CultureInfo.InvariantCulture) + " keys, " +
                languageCount.ToString(CultureInfo.InvariantCulture) + " languages\n" +
                "Profile: " + profileLabel + "    Encoding: " + encodingLabel +
                (_languageSelection == 0 && _hasUnregisteredLocales
                    ? "\nScope: registered locales only; inactive table assets are excluded."
                    : string.Empty),
                MessageType.None);

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
            {
                Close();
                return;
            }

            using (new EditorGUI.DisabledScope(keyCount <= 0 || _languageOptions.Length == 0))
            {
                if (GUILayout.Button("Export...", GUILayout.Width(110f)))
                {
                    var selection = new LocalizationCsvExportSelection(
                        _profile,
                        _keyScope,
                        _languageSelection == 0 ? (int?)null : _languageSelection,
                        _languageSelection == 0 && _hasUnregisteredLocales);
                    _pendingExportCallback = _onExport;
                    _pendingExportSelection = selection;
                    _onExport = null;
                    Close();
                    if (_delayedExportInvoker == null)
                        _delayedExportInvoker = InvokePendingExport;
                    EditorApplication.delayCall += _delayedExportInvoker;
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
        }
    }
}
#endif
