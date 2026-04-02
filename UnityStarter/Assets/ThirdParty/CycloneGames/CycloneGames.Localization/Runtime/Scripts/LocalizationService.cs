using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly FallbackChain _fallbackChain = new FallbackChain();

        // tableId → (localeCode → StringTable)
        private readonly Dictionary<string, Dictionary<string, StringTable>> _stringTables
            = new Dictionary<string, Dictionary<string, StringTable>>(8);

        // tableId → (localeCode → AssetTable)
        private readonly Dictionary<string, Dictionary<string, AssetTable>> _assetTables
            = new Dictionary<string, Dictionary<string, AssetTable>>(4);

        // tableId → StringTableMetadata
        private readonly Dictionary<string, StringTableMetadata> _metadata
            = new Dictionary<string, StringTableMetadata>(4);

        private LocaleId _currentLocale;
        private LocaleId[] _currentChain = Array.Empty<LocaleId>();
        private List<Locale> _availableLocales = new List<Locale>();
        private Dictionary<string, Locale> _localeMap = new Dictionary<string, Locale>(8);
        private PseudoLocaleMode _pseudoMode;

        // Default selector chain — lazily created once and reused.
        private static readonly ILocaleSelector[] s_defaultSelectors =
        {
            new CommandLineLocaleSelector(),
            new PlayerPrefsLocaleSelector(),
            new SystemLocaleSelector(),
        };

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly HashSet<string> s_reportedMissing = new HashSet<string>(64);
        private static bool s_logMissingKeys = true;

        /// <summary>
        /// Enable/disable missing key warnings to the console. Editor and Development builds only.
        /// </summary>
        public static bool LogMissingKeys
        {
            get => s_logMissingKeys;
            set => s_logMissingKeys = value;
        }
        #endif

        public LocaleId CurrentLocale => _currentLocale;
        public IReadOnlyList<Locale> AvailableLocales => _availableLocales;
        public bool IsInitialized { get; private set; }

        /// <inheritdoc/>
        public PseudoLocaleMode PseudoMode
        {
            get => _pseudoMode;
            set => _pseudoMode = value;
        }

        public event Action<LocaleId> OnLocaleChanged;

        public UniTask InitializeAsync(LocalizationOptions options)
        {
            if (IsInitialized) return UniTask.CompletedTask;

            _availableLocales.Clear();
            _localeMap.Clear();
            _pseudoMode = options.PseudoMode;

            if (options.AvailableLocales != null)
            {
                for (int i = 0; i < options.AvailableLocales.Count; i++)
                {
                    var locale = options.AvailableLocales[i];
                    if (locale == null) continue;
                    _availableLocales.Add(locale);
                    _localeMap[locale.Id.Code] = locale;
                }
            }

            // ── Locale selection via priority chain ─────────────
            Locale startLocale = null;

            if (options.DetectSystemLanguage)
            {
                var selectors = options.LocaleSelectors ?? (IReadOnlyList<ILocaleSelector>)s_defaultSelectors;
                startLocale = EvaluateSelectorChain(selectors);
            }

            if (startLocale == null && options.DefaultLocale != null)
                startLocale = options.DefaultLocale;

            if (startLocale == null && _availableLocales.Count > 0)
                startLocale = _availableLocales[0];

            if (startLocale != null)
            {
                _currentLocale = startLocale.Id;
                _currentChain = _fallbackChain.Resolve(startLocale);
            }

            IsInitialized = true;
            return UniTask.CompletedTask;
        }

        public UniTask SetLocaleAsync(LocaleId localeId)
        {
            if (_currentLocale == localeId) return UniTask.CompletedTask;

            if (!_localeMap.TryGetValue(localeId.Code, out var locale))
                return UniTask.CompletedTask;

            _currentLocale = localeId;
            _currentChain = _fallbackChain.Resolve(locale);

            OnLocaleChanged?.Invoke(_currentLocale);

            return UniTask.CompletedTask;
        }

        // ── String resolution ───────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetString(in LocalizedString localizedString)
        {
            if (!localizedString.IsValid) return string.Empty;
            return GetStringInternal(localizedString.TableId, localizedString.EntryKey);
        }

        public string GetString(string tableId, string entryKey)
        {
            return GetStringInternal(tableId, entryKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetFormattedString(in LocalizedString localizedString, params object[] args)
        {
            if (!localizedString.IsValid) return string.Empty;
            var template = GetStringInternal(localizedString.TableId, localizedString.EntryKey);
            if (template == null) return null;
            if (args == null || args.Length == 0) return template;
            return string.Format(template, args);
        }

        public string GetFormattedString(string tableId, string entryKey, params object[] args)
        {
            var template = GetStringInternal(tableId, entryKey);
            if (template == null) return null;
            if (args == null || args.Length == 0) return template;
            return string.Format(template, args);
        }

        // ── Plural string resolution ────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetPluralString(in LocalizedString baseKey, int count)
        {
            if (!baseKey.IsValid) return string.Empty;
            return GetPluralStringInternal(baseKey.TableId, baseKey.EntryKey, count, null);
        }

        public string GetPluralString(in LocalizedString baseKey, int count, params object[] extraArgs)
        {
            if (!baseKey.IsValid) return string.Empty;
            return GetPluralStringInternal(baseKey.TableId, baseKey.EntryKey, count, extraArgs);
        }

        public string GetPluralString(string tableId, string entryKey, int count)
        {
            return GetPluralStringInternal(tableId, entryKey, count, null);
        }

        public string GetPluralString(string tableId, string entryKey, int count, params object[] extraArgs)
        {
            return GetPluralStringInternal(tableId, entryKey, count, extraArgs);
        }

        // ── Internal resolution ─────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetStringInternal(string tableId, string entryKey, out string value)
        {
            if (_stringTables.TryGetValue(tableId, out var localeMap))
            {
                for (int i = 0; i < _currentChain.Length; i++)
                {
                    if (localeMap.TryGetValue(_currentChain[i].Code, out var table) &&
                        table.TryGetValue(entryKey, out value))
                    {
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        private string GetStringInternal(string tableId, string entryKey)
        {
            if (TryGetStringInternal(tableId, entryKey, out var value))
                return PseudoLocalizer.Transform(value, _pseudoMode);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            OnMissingKey(tableId, entryKey);
            #endif
            return null;
        }

        private string GetPluralStringInternal(string tableId, string entryKey, int count, object[] extraArgs)
        {
            var category = PluralRules.Resolve(_currentLocale, count);
            string template = ResolvePluralTemplate(tableId, entryKey, category);
            if (template == null) return null;

            // Apply pseudo-localization to the template BEFORE formatting
            // so that format placeholders ({0}, {1}) are not corrupted.
            template = PseudoLocalizer.Transform(template, _pseudoMode);

            // count is always {0}, extra args are {1}, {2}, ...
            if (extraArgs == null || extraArgs.Length == 0)
                return string.Format(template, count);

            var combined = new object[extraArgs.Length + 1];
            combined[0] = count;
            Array.Copy(extraArgs, 0, combined, 1, extraArgs.Length);
            return string.Format(template, combined);
        }

        private string ResolvePluralTemplate(string tableId, string baseEntryKey, PluralCategory category)
        {
            // Try category-specific key: "item_count.one", "item_count.few", etc.
            string suffixedKey = string.Concat(baseEntryKey, PluralRules.GetSuffix(category));
            if (TryGetStringInternal(tableId, suffixedKey, out var value))
                return value;

            // Fallback to .other (always defined)
            if (category != PluralCategory.Other)
            {
                string otherKey = string.Concat(baseEntryKey, PluralRules.GetSuffix(PluralCategory.Other));
                if (TryGetStringInternal(tableId, otherKey, out value))
                    return value;
            }

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            OnMissingKey(tableId, baseEntryKey);
            #endif
            return null;
        }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnMissingKey(string tableId, string entryKey)
        {
            if (!s_logMissingKeys) return;
            string fullKey = string.Concat(tableId, "/", entryKey);
            if (!s_reportedMissing.Add(fullKey)) return;
            Debug.LogWarning($"[Localization] Missing key \"{fullKey}\" (locale: {_currentLocale})");
        }
        #endif

        // ── Asset resolution ────────────────────────────────────────

        public AssetRef ResolveAsset(string tableId, string entryKey)
        {
            if (!_assetTables.TryGetValue(tableId, out var localeMap)) return default;

            for (int i = 0; i < _currentChain.Length; i++)
            {
                if (localeMap.TryGetValue(_currentChain[i].Code, out var table) &&
                    table.TryGetValue(entryKey, out var assetRef))
                {
                    return assetRef;
                }
            }

            return default;
        }

        public AssetRef<T> ResolveAsset<T>(LocalizedAsset<T> localizedAsset) where T : UnityEngine.Object
        {
            if (!localizedAsset.IsValid) return default;
            var untyped = ResolveAsset(localizedAsset.TableId, localizedAsset.EntryKey);
            return untyped.Typed<T>();
        }

        // ── Table management ────────────────────────────────────────

        public void RegisterStringTable(StringTable table)
        {
            if (table == null) return;

            if (!_stringTables.TryGetValue(table.TableId, out var localeMap))
            {
                localeMap = new Dictionary<string, StringTable>(4);
                _stringTables[table.TableId] = localeMap;
            }

            localeMap[table.LocaleId.Code] = table;
        }

        public void UnregisterStringTable(string tableId, LocaleId localeId)
        {
            if (_stringTables.TryGetValue(tableId, out var localeMap))
                localeMap.Remove(localeId.Code);
        }

        public void RegisterAssetTable(AssetTable table)
        {
            if (table == null) return;

            if (!_assetTables.TryGetValue(table.TableId, out var localeMap))
            {
                localeMap = new Dictionary<string, AssetTable>(4);
                _assetTables[table.TableId] = localeMap;
            }

            localeMap[table.LocaleId.Code] = table;
        }

        public void UnregisterAssetTable(string tableId, LocaleId localeId)
        {
            if (_assetTables.TryGetValue(tableId, out var localeMap))
                localeMap.Remove(localeId.Code);
        }

        // ── Metadata management ─────────────────────────────────────

        public void RegisterMetadata(StringTableMetadata metadata)
        {
            if (metadata == null) return;
            _metadata[metadata.TableId] = metadata;
        }

        public void UnregisterMetadata(string tableId)
        {
            _metadata.Remove(tableId);
        }

        public int GetMaxLength(string tableId, string entryKey)
        {
            if (_metadata.TryGetValue(tableId, out var meta))
                return meta.GetMaxLength(entryKey);
            return 0;
        }

        // ── Locale selector chain ───────────────────────────────────

        /// <summary>
        /// Evaluates selectors in priority order. The first selector that returns
        /// a non-null code that matches an available locale wins.
        /// Matching logic: exact → language-only → reverse prefix.
        /// </summary>
        private Locale EvaluateSelectorChain(IReadOnlyList<ILocaleSelector> selectors)
        {
            for (int i = 0; i < selectors.Count; i++)
            {
                string code = selectors[i].GetPreferredLocaleCode();
                if (string.IsNullOrEmpty(code)) continue;

                var locale = MatchLocaleCode(code);
                if (locale != null) return locale;
            }
            return null;
        }

        /// <summary>
        /// Matches a BCP 47 code against available locales:
        /// exact → language-only → reverse prefix.
        /// </summary>
        private Locale MatchLocaleCode(string code)
        {
            // Exact match: "zh-CN"
            if (_localeMap.TryGetValue(code, out var exact))
                return exact;

            // Language-only match: "zh-CN" → "zh"
            int dash = code.IndexOf('-');
            if (dash > 0)
            {
                string lang = code.Substring(0, dash);
                if (_localeMap.TryGetValue(lang, out var langMatch))
                    return langMatch;
            }

            // Reverse match: system "zh" → available "zh-CN"
            foreach (var kvp in _localeMap)
            {
                if (kvp.Key.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return null;
        }
    }
}
