using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Full CycloneGames localization service. Extends the backend-agnostic
    /// <see cref="ILocalizationProvider"/> with the capabilities that only this implementation owns:
    /// lifecycle control, serializable key struct lookups, localized asset resolution, authoring
    /// table registration, catalog installation, and pseudo-localization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumers that only need locale state and text should depend on
    /// <see cref="ILocalizationProvider"/> instead. Doing so keeps them compilable against any
    /// backend and keeps this implementation out of their assembly graph entirely.
    /// </para>
    /// <para>
    /// Thread-aware: initialization and every mutation are owned by the managed thread that calls
    /// <see cref="Initialize"/>. Query methods read immutable snapshots and may run concurrently.
    /// Unity-facing consumers must bind and mutate from the Unity main thread.
    /// </para>
    /// </remarks>
    public interface ILocalizationService : ILocalizationProvider, IDisposable
    {
        PseudoLocaleMode PseudoMode { get; set; }

        void Initialize(LocalizationOptions options);
        bool TrySetLocale(LocaleId localeId);
        void Shutdown();

        string GetString(in LocalizedString localizedString);
        bool TryGetString(in LocalizedString localizedString, out string value);
        string GetFormattedString(in LocalizedString localizedString, params object[] args);

        string GetPluralString(in LocalizedString baseKey, int count);
        string GetPluralString(in LocalizedString baseKey, int count, params object[] extraArgs);
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
