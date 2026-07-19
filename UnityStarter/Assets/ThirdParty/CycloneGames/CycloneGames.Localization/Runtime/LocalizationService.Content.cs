using System;
using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    public sealed partial class LocalizationService
    {
        public bool RegisterMetadata(StringTableMetadata metadata)
        {
            EnsureInitializedOwner();
            if (!TryCompileMetadata(metadata, out string tableId, out Dictionary<string, int> compiled, out string error))
            {
                ReportInvalidContent(error);
                return false;
            }

            return ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized) return;
                _metadata[tableId] = compiled;
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool UnregisterMetadata(string tableId)
        {
            EnsureInitializedOwner();
            if (string.IsNullOrEmpty(tableId) || !_metadata.ContainsKey(tableId)) return false;
            return ScheduleMutation(() =>
            {
                if (_lifecycle == Lifecycle.Initialized && _metadata.Remove(tableId))
                    Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool RegisterStringTable(StringTable table)
        {
            EnsureInitializedOwner();
            if (table == null)
            {
                ReportInvalidContent("String table is null.");
                return false;
            }

            CompiledStringTable compiled;
            try
            {
                if (table.Count > _limits.MaxEntriesPerTable)
                    throw new InvalidOperationException("String table entry count exceeds the configured limit.");
                compiled = table.Compile();
            }
            catch (Exception exception)
            {
                ReportInvalidContent(exception.Message, exception);
                return false;
            }

            if (!ValidateCompiledStringTable(compiled, out string error))
            {
                ReportInvalidContent(error);
                return false;
            }

            var key = new TableKey(compiled.TableId, compiled.LocaleId);
            if (HasCatalogStringKey(key, null))
            {
                ReportInvalidContent("String table ownership conflicts with a registered catalog.");
                return false;
            }

            return ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized || HasCatalogStringKey(key, null)) return;
                _manualStringTables[key] = compiled;
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool UnregisterStringTable(string tableId, LocaleId localeId)
        {
            EnsureInitializedOwner();
            if (string.IsNullOrEmpty(tableId) || !localeId.IsValid) return false;
            var key = new TableKey(tableId, localeId);
            if (!_manualStringTables.ContainsKey(key)) return false;
            return ScheduleMutation(() =>
            {
                if (_lifecycle == Lifecycle.Initialized && _manualStringTables.Remove(key))
                    Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool RegisterAssetTable(AssetTable table)
        {

            EnsureInitializedOwner();
            if (table == null)
            {
                ReportInvalidContent("Asset table is null.");
                return false;
            }

            CompiledAssetTable compiled;
            try
            {
                if (table.Count > _limits.MaxEntriesPerTable)
                    throw new InvalidOperationException("Asset table entry count exceeds the configured limit.");
                compiled = table.Compile();
            }
            catch (Exception exception)
            {
                ReportInvalidContent(exception.Message, exception);
                return false;
            }

            if (!ValidateCompiledAssetTable(compiled, out string error))
            {
                ReportInvalidContent(error);
                return false;
            }

            var key = new TableKey(compiled.TableId, compiled.LocaleId);
            if (HasCatalogAssetKey(key, null))
            {
                ReportInvalidContent("Asset table ownership conflicts with a registered catalog.");
                return false;
            }

            return ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized || HasCatalogAssetKey(key, null)) return;
                _manualAssetTables[key] = compiled;
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool UnregisterAssetTable(string tableId, LocaleId localeId)
        {
            EnsureInitializedOwner();
            if (string.IsNullOrEmpty(tableId) || !localeId.IsValid) return false;
            var key = new TableKey(tableId, localeId);
            if (!_manualAssetTables.ContainsKey(key)) return false;
            return ScheduleMutation(() =>
            {
                if (_lifecycle == Lifecycle.Initialized && _manualAssetTables.Remove(key))
                    Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool TryRegisterCatalog(string ownerId, LocalizationCatalog catalog)
        {
            EnsureInitializedOwner();
            if (!TryCompileCatalog(ownerId, catalog, out CatalogContent content, out string error))
            {
                ReportDiagnostic(new LocalizationDiagnostic(
                    LocalizationDiagnosticCode.InvalidCatalog,
                    LocalizationDiagnosticSeverity.Error,
                    error));
                return false;
            }

            if (HasCatalogConflict(ownerId, content, out error))
            {
                ReportDiagnostic(new LocalizationDiagnostic(
                    LocalizationDiagnosticCode.InvalidCatalog,
                    LocalizationDiagnosticSeverity.Error,
                    error));
                return false;
            }

            if (_catalogs.TryGetValue(ownerId, out CatalogContent existing) &&
                string.Equals(existing.ContentHash, content.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized) return;
                if (HasCatalogConflict(ownerId, content, out string conflict))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        conflict));
                    return;
                }

                _catalogs[ownerId] = content;
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        public bool RemoveCatalog(string ownerId)
        {
            EnsureInitializedOwner();
            if (string.IsNullOrEmpty(ownerId) || !_catalogs.ContainsKey(ownerId)) return false;
            return ScheduleMutation(() =>
            {
                if (_lifecycle == Lifecycle.Initialized && _catalogs.Remove(ownerId))
                    Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
            });
        }

        private bool TryCompileMetadata(
            StringTableMetadata metadata,
            out string tableId,
            out Dictionary<string, int> compiled,
            out string error)
        {
            tableId = metadata != null ? metadata.TableId : null;
            compiled = null;
            error = null;
            if (metadata == null) return Fail("Metadata is null.", out error);
            if (metadata.TableType != TableType.String)
                return Fail("Runtime max-length metadata must target a string table.", out error);
            if (!ValidateIdentifier(tableId, _limits.MaxTableIdLength))
                return Fail("Metadata table ID is invalid or too long.", out error);

            IReadOnlyList<EntryMetadata> entries = metadata.Entries;
            if (entries == null || entries.Count > _limits.MaxEntriesPerTable)
                return Fail("Metadata entry count exceeds the configured limit.", out error);

            compiled = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                EntryMetadata entry = entries[i];
                if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength))
                    return Fail("A metadata key is invalid or too long.", out error);
                if (entry.MaxLength < 0 || entry.SourceRevision < 0)
                    return Fail("Metadata lengths and revisions must not be negative.", out error);
                if (compiled.ContainsKey(entry.Key))
                    return Fail("Duplicate metadata key '" + entry.Key + "'.", out error);

                List<LocaleTranslationState> states = entry.LocaleStatuses;
                if (states != null)
                {
                    if (states.Count > StringTableMetadata.MaxLocaleStatusesPerEntry)
                        return Fail("A metadata locale-status list exceeds its limit.", out error);
                    var localeCodes = new HashSet<string>(StringComparer.Ordinal);
                    for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                    {
                        LocaleTranslationState state = states[stateIndex];
                        if (!LocaleId.TryCreate(state.LocaleCode, out LocaleId stateLocale) ||
                            !localeCodes.Add(stateLocale.Code) ||
                            state.TranslatedSourceRevision < 0 ||
                            state.Status < TranslationStatus.Missing ||
                            state.Status > TranslationStatus.Stale)
                        {
                            return Fail("A metadata locale translation state is invalid.", out error);
                        }
                    }
                }

                compiled.Add(entry.Key, entry.MaxLength);
            }

            return true;
        }

        private bool TryCompileCatalog(
            string ownerId,
            LocalizationCatalog catalog,
            out CatalogContent content,
            out string error)
        {
            content = null;
            error = null;
            if (!ValidateIdentifier(ownerId, _limits.MaxCatalogOwnerIdLength))
                return Fail("Catalog owner ID is invalid or too long.", out error);
            if (catalog == null) return Fail("Catalog is null.", out error);
            if (catalog.SchemaVersion != LocalizationCatalog.CurrentSchemaVersion)
                return Fail("Catalog schema version is unsupported.", out error);
            if (!ValidateIdentifier(catalog.CatalogVersion, _limits.MaxTableIdLength))
                return Fail("Catalog version is invalid or too long.", out error);
            if (!IsSha256(catalog.ContentHash))
                return Fail("Catalog content hash must be a 64-character SHA-256 value.", out error);

            IReadOnlyList<CatalogStringTable> stringTables = catalog.StringTables;
            IReadOnlyList<CatalogAssetTable> assetTables = catalog.AssetTables;
            if (stringTables == null || assetTables == null)
                return Fail("Catalog table collections are missing.", out error);
            if ((long)stringTables.Count + assetTables.Count > _limits.MaxCatalogTables)
                return Fail("Catalog table count exceeds the configured limit.", out error);
            if (!ValidateCatalogAggregateBudgets(stringTables, assetTables, out error))
                return false;

            var strings = new Dictionary<TableKey, CompiledStringTable>();
            var assets = new Dictionary<TableKey, CompiledAssetTable>();
            long totalEntries = 0;

            for (int i = 0; i < stringTables.Count; i++)
            {
                CatalogStringTable table = stringTables[i];
                if (table == null || !ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                    !table.LocaleId.IsValid || !_localeMap.ContainsKey(table.LocaleId.Code))
                {
                    return Fail("A catalog string table identity is invalid.", out error);
                }

                IReadOnlyList<CatalogStringEntry> entries = table.Entries;
                if (entries == null || entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog string table exceeds the entry limit.", out error);
                totalEntries += entries.Count;
                if (totalEntries > _limits.MaxCatalogEntries)
                    return Fail("Catalog entry count exceeds the configured limit.", out error);

                var key = new TableKey(table.TableId, table.LocaleId);
                if (strings.ContainsKey(key))
                    return Fail("Duplicate catalog string table identity.", out error);

                var lookup = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
                var entryKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    CatalogStringEntry entry = entries[entryIndex];
                    if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength) ||
                        entry.Value == null || entry.Value.Length > _limits.MaxStringValueLength ||
                        !IsWellFormedUtf16(entry.Value))
                    {
                        return Fail("A catalog string entry exceeds its configured bounds.", out error);
                    }
                    if (!entryKeys.Add(entry.Key))
                        return Fail("Duplicate catalog string key '" + entry.Key + "'.", out error);
                    if (string.IsNullOrWhiteSpace(entry.Value))
                        continue;
                    lookup.Add(entry.Key, entry.Value);
                }

                strings.Add(key, new CompiledStringTable(table.TableId, table.LocaleId, lookup, true));
            }

            for (int i = 0; i < assetTables.Count; i++)
            {
                CatalogAssetTable table = assetTables[i];
                if (table == null || !ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                    !table.LocaleId.IsValid || !_localeMap.ContainsKey(table.LocaleId.Code))
                {
                    return Fail("A catalog asset table identity is invalid.", out error);
                }

                IReadOnlyList<CatalogAssetEntry> entries = table.Entries;
                if (entries == null || entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog asset table exceeds the entry limit.", out error);
                totalEntries += entries.Count;
                if (totalEntries > _limits.MaxCatalogEntries)
                    return Fail("Catalog entry count exceeds the configured limit.", out error);

                var key = new TableKey(table.TableId, table.LocaleId);
                if (assets.ContainsKey(key))
                    return Fail("Duplicate catalog asset table identity.", out error);

                var lookup = new Dictionary<string, AssetRef>(entries.Count, StringComparer.Ordinal);
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    CatalogAssetEntry entry = entries[entryIndex];
                    if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength) ||
                        !entry.Asset.IsValid ||
                        entry.Asset.Location.Length > _limits.MaxAssetLocationLength ||
                        !IsWellFormedUtf16(entry.Asset.Location) ||
                        (entry.Asset.Guid != null &&
                         (entry.Asset.Guid.Length > _limits.MaxAssetLocationLength ||
                          !IsWellFormedUtf16(entry.Asset.Guid))))
                    {
                        return Fail("A catalog asset entry exceeds its configured bounds.", out error);
                    }
                    if (lookup.ContainsKey(entry.Key))
                        return Fail("Duplicate catalog asset key '" + entry.Key + "'.", out error);
                    lookup.Add(entry.Key, entry.Asset);
                }

                assets.Add(key, new CompiledAssetTable(table.TableId, table.LocaleId, lookup, true));
            }

            string computedHash;
            try
            {
                computedHash = LocalizationCatalog.ComputeContentHash(stringTables, assetTables);
            }
            catch (Exception exception)
            {
                return Fail("Catalog content hash calculation failed: " + exception.Message, out error);
            }
            if (!string.Equals(computedHash, catalog.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Fail("Catalog content hash verification failed.", out error);

            content = new CatalogContent(strings, assets, computedHash);
            return true;
        }

        private bool ValidateCatalogAggregateBudgets(
            IReadOnlyList<CatalogStringTable> stringTables,
            IReadOnlyList<CatalogAssetTable> assetTables,
            out string error)
        {
            long textCharacters = 0L;
            long assetReferenceCharacters = 0L;
            long totalEntries = 0L;

            for (int tableIndex = 0; tableIndex < stringTables.Count; tableIndex++)
            {
                CatalogStringTable table = stringTables[tableIndex];
                if (table == null || table.Entries == null)
                    return Fail("Catalog string table data is missing.", out error);
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog string table exceeds the entry limit.", out error);
                totalEntries += table.Entries.Count;
                if (totalEntries > _limits.MaxCatalogEntries)
                    return Fail("Catalog entry count exceeds the configured limit.", out error);

                if (!TryAddBudget(ref textCharacters, LengthOf(table.TableId), _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(ref textCharacters, LengthOf(table.LocaleId.Code), _limits.MaxCatalogTextCharacters))
                {
                    return Fail("Catalog text exceeds the aggregate character budget.", out error);
                }

                for (int entryIndex = 0; entryIndex < table.Entries.Count; entryIndex++)
                {
                    CatalogStringEntry entry = table.Entries[entryIndex];
                    if (entry.Value == null || !IsWellFormedUtf16(entry.Value))
                        return Fail("A catalog string value contains malformed UTF-16.", out error);
                    if (!TryAddBudget(ref textCharacters, LengthOf(entry.Key), _limits.MaxCatalogTextCharacters) ||
                        !TryAddBudget(ref textCharacters, LengthOf(entry.Value), _limits.MaxCatalogTextCharacters))
                    {
                        return Fail("Catalog text exceeds the aggregate character budget.", out error);
                    }
                }
            }

            for (int tableIndex = 0; tableIndex < assetTables.Count; tableIndex++)
            {
                CatalogAssetTable table = assetTables[tableIndex];
                if (table == null || table.Entries == null)
                    return Fail("Catalog asset table data is missing.", out error);
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog asset table exceeds the entry limit.", out error);
                totalEntries += table.Entries.Count;
                if (totalEntries > _limits.MaxCatalogEntries)
                    return Fail("Catalog entry count exceeds the configured limit.", out error);

                if (!TryAddBudget(ref textCharacters, LengthOf(table.TableId), _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(ref textCharacters, LengthOf(table.LocaleId.Code), _limits.MaxCatalogTextCharacters))
                {
                    return Fail("Catalog text exceeds the aggregate character budget.", out error);
                }

                for (int entryIndex = 0; entryIndex < table.Entries.Count; entryIndex++)
                {
                    CatalogAssetEntry entry = table.Entries[entryIndex];
                    if (!IsWellFormedUtf16(entry.Asset.Location) ||
                        (entry.Asset.Guid != null && !IsWellFormedUtf16(entry.Asset.Guid)))
                    {
                        return Fail("A catalog asset reference contains malformed UTF-16.", out error);
                    }
                    if (!TryAddBudget(ref textCharacters, LengthOf(entry.Key), _limits.MaxCatalogTextCharacters) ||
                        !TryAddBudget(
                            ref assetReferenceCharacters,
                            LengthOf(entry.Asset.Location),
                            _limits.MaxCatalogAssetReferenceCharacters) ||
                        !TryAddBudget(
                            ref assetReferenceCharacters,
                            LengthOf(entry.Asset.Guid),
                            _limits.MaxCatalogAssetReferenceCharacters))
                    {
                        return Fail("Catalog content exceeds an aggregate character budget.", out error);
                    }
                }
            }

            error = null;
            return true;
        }

        private static bool TryAddBudget(ref long total, int amount, long maximum)
        {
            if (amount < 0 || total > maximum - amount) return false;
            total += amount;
            return true;
        }

        private static int LengthOf(string value) => value != null ? value.Length : 0;

        private bool ValidateCompiledStringTable(CompiledStringTable table, out string error)
        {
            error = null;
            if (!ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) || !table.LocaleId.IsValid ||
                !_localeMap.ContainsKey(table.LocaleId.Code) || table.Count > _limits.MaxEntriesPerTable)
            {
                return Fail("Compiled string table exceeds configured bounds.", out error);
            }

            var enumerator = table.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var pair = enumerator.Current;
                if (!ValidateIdentifier(pair.Key, _limits.MaxEntryKeyLength) ||
                    pair.Value == null || pair.Value.Length > _limits.MaxStringValueLength ||
                    !IsWellFormedUtf16(pair.Value))
                {
                    return Fail("Compiled string entry exceeds configured bounds.", out error);
                }
            }
            return true;
        }

        private bool ValidateCompiledAssetTable(CompiledAssetTable table, out string error)
        {
            error = null;
            if (!ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) || !table.LocaleId.IsValid ||
                !_localeMap.ContainsKey(table.LocaleId.Code) || table.Count > _limits.MaxEntriesPerTable)
            {
                return Fail("Compiled asset table exceeds configured bounds.", out error);
            }

            var enumerator = table.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var pair = enumerator.Current;
                if (!ValidateIdentifier(pair.Key, _limits.MaxEntryKeyLength) || !pair.Value.IsValid ||
                    pair.Value.Location.Length > _limits.MaxAssetLocationLength ||
                    !IsWellFormedUtf16(pair.Value.Location) ||
                    (pair.Value.Guid != null &&
                     (pair.Value.Guid.Length > _limits.MaxAssetLocationLength ||
                      !IsWellFormedUtf16(pair.Value.Guid))))
                {
                    return Fail("Compiled asset entry exceeds configured bounds.", out error);
                }
            }
            return true;
        }

        private bool HasCatalogConflict(string ownerId, CatalogContent content, out string error)
        {
            foreach (TableKey key in content.StringTables.Keys)
            {
                if (_manualStringTables.ContainsKey(key) || HasCatalogStringKey(key, ownerId))
                {
                    error = "Catalog string table ownership conflicts with live content.";
                    return true;
                }
            }
            foreach (TableKey key in content.AssetTables.Keys)
            {
                if (_manualAssetTables.ContainsKey(key) || HasCatalogAssetKey(key, ownerId))
                {
                    error = "Catalog asset table ownership conflicts with live content.";
                    return true;
                }
            }
            error = null;
            return false;
        }

        private bool HasCatalogStringKey(TableKey key, string excludedOwner)
        {
            foreach (var pair in _catalogs)
            {
                if (string.Equals(pair.Key, excludedOwner, StringComparison.Ordinal)) continue;
                if (pair.Value.StringTables.ContainsKey(key)) return true;
            }
            return false;
        }

        private bool HasCatalogAssetKey(TableKey key, string excludedOwner)
        {
            foreach (var pair in _catalogs)
            {
                if (string.Equals(pair.Key, excludedOwner, StringComparison.Ordinal)) continue;
                if (pair.Value.AssetTables.ContainsKey(key)) return true;
            }
            return false;
        }

    }
}
