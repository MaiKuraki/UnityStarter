using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    public interface ILocalizationService
    {
        LocaleId CurrentLocale { get; }
        IReadOnlyList<Locale> AvailableLocales { get; }
        bool IsInitialized { get; }

        /// <summary>
        /// The active pseudo-localization mode. Set to <see cref="PseudoLocaleMode.None"/> for release.
        /// Can be changed at runtime for live QA toggling.
        /// </summary>
        PseudoLocaleMode PseudoMode { get; set; }

        event Action<LocaleId> OnLocaleChanged;

        UniTask InitializeAsync(LocalizationOptions options);
        UniTask SetLocaleAsync(LocaleId localeId);

        // ── String resolution ───────────────────────────────────
        string GetString(in LocalizedString localizedString);
        string GetString(string tableId, string entryKey);
        string GetFormattedString(in LocalizedString localizedString, params object[] args);
        string GetFormattedString(string tableId, string entryKey, params object[] args);

        // ── Plural string resolution ────────────────────────────
        string GetPluralString(in LocalizedString baseKey, int count);
        string GetPluralString(in LocalizedString baseKey, int count, params object[] extraArgs);
        string GetPluralString(string tableId, string entryKey, int count);
        string GetPluralString(string tableId, string entryKey, int count, params object[] extraArgs);

        // ── Asset resolution ────────────────────────────────────
        AssetRef ResolveAsset(string tableId, string entryKey);
        AssetRef<T> ResolveAsset<T>(LocalizedAsset<T> localizedAsset) where T : UnityEngine.Object;

        // ── Metadata ────────────────────────────────────────────
        /// <summary>
        /// Returns the max character limit for an entry, or 0 if no limit is set.
        /// Useful for runtime input validation (e.g. player name fields).
        /// </summary>
        int GetMaxLength(string tableId, string entryKey);
        void RegisterMetadata(StringTableMetadata metadata);
        void UnregisterMetadata(string tableId);

        // ── Table management ────────────────────────────────────
        void RegisterStringTable(StringTable table);
        void UnregisterStringTable(string tableId, LocaleId localeId);
        void RegisterAssetTable(AssetTable table);
        void UnregisterAssetTable(string tableId, LocaleId localeId);
    }
}
