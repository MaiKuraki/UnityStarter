using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    public enum LocalizationChangeReason : byte
    {
        Initialized = 0,
        LocaleChanged = 1,
        ContentChanged = 2,
        PseudoModeChanged = 3,
        Shutdown = 4,
    }

    public readonly struct LocalizationChange
    {
        public LocalizationChange(
            LocaleId previousLocale,
            LocaleId currentLocale,
            LocalizationChangeReason reason,
            long revision)
        {
            PreviousLocale = previousLocale;
            CurrentLocale = currentLocale;
            Reason = reason;
            Revision = revision;
        }

        public LocaleId PreviousLocale { get; }
        public LocaleId CurrentLocale { get; }
        public LocalizationChangeReason Reason { get; }
        public long Revision { get; }
    }

    /// <summary>
    /// Dependencies supplied to a presentation binding. The binding never owns these services.
    /// </summary>
    public readonly struct LocalizationBindingContext
    {
        public LocalizationBindingContext(
            ILocalizationService localization,
            IAssetPackage assetPackage = null)
        {
            Localization = localization ?? throw new ArgumentNullException(nameof(localization));
            AssetPackage = assetPackage;
        }

        public ILocalizationService Localization { get; }
        public IAssetPackage AssetPackage { get; }
    }

    public interface ILocalizationBindingTarget
    {
        void Bind(in LocalizationBindingContext context);
        void Unbind();
    }

    /// <summary>
    /// Thread-aware localization facade. Initialize and every mutation are owned by the managed
    /// thread that calls <see cref="Initialize"/>. Query methods read immutable snapshots and may
    /// run concurrently. Unity-facing consumers must bind and mutate from the Unity main thread.
    /// </summary>
    public interface ILocalizationService : IDisposable
    {
        LocaleId CurrentLocale { get; }
        IReadOnlyList<LocaleId> AvailableLocales { get; }
        bool IsInitialized { get; }
        long Revision { get; }
        PseudoLocaleMode PseudoMode { get; set; }

        /// <summary>
        /// Raised synchronously on the owner thread after a committed state change. Subscriber
        /// exceptions are isolated and reported through the configured diagnostic sink.
        /// </summary>
        event Action<LocalizationChange> Changed;

        void Initialize(LocalizationOptions options);
        bool TrySetLocale(LocaleId localeId);
        void Shutdown();

        string GetString(in LocalizedString localizedString);
        string GetString(string tableId, string entryKey);
        bool TryGetString(in LocalizedString localizedString, out string value);
        bool TryGetString(string tableId, string entryKey, out string value);
        string GetFormattedString(in LocalizedString localizedString, params object[] args);
        string GetFormattedString(string tableId, string entryKey, params object[] args);

        string GetPluralString(in LocalizedString baseKey, int count);
        string GetPluralString(in LocalizedString baseKey, int count, params object[] extraArgs);
        string GetPluralString(string tableId, string entryKey, int count);
        string GetPluralString(string tableId, string entryKey, int count, params object[] extraArgs);

        AssetRef ResolveAsset(string tableId, string entryKey);
        AssetRef<T> ResolveAsset<T>(LocalizedAsset<T> localizedAsset) where T : UnityEngine.Object;

        int GetMaxLength(string tableId, string entryKey);
        bool RegisterMetadata(StringTableMetadata metadata);
        bool UnregisterMetadata(string tableId);

        bool RegisterStringTable(StringTable table);
        bool UnregisterStringTable(string tableId, LocaleId localeId);
        bool RegisterAssetTable(AssetTable table);
        bool UnregisterAssetTable(string tableId, LocaleId localeId);

        /// <summary>
        /// Validates and atomically installs or replaces all content owned by <paramref name="ownerId"/>.
        /// A failed replacement leaves the previously committed content unchanged.
        /// </summary>
        bool TryRegisterCatalog(string ownerId, LocalizationCatalog catalog);

        /// <summary>
        /// Removes one catalog owner and republishes the remaining content atomically.
        /// </summary>
        bool RemoveCatalog(string ownerId);
    }
}
