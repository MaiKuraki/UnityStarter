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
            if (!TryMeasureManualStringTable(table, out TableKey key, out ContentFootprint footprint, out string error))
            {
                ReportInvalidContent(error);
                return false;
            }

            if (HasCatalogStringKey(key, null))
            {
                ReportInvalidContent("String table ownership conflicts with a registered catalog.");
                return false;
            }
            ContentFootprint replaced = GetManualStringFootprint(key);
            if (!TryValidateResidentAdmission(footprint, replaced, out error))
            {
                ReportInvalidContent(error);
                return false;
            }

            bool accepted = false;
            bool queued = _processingMutations;
            bool scheduled = ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized) return;
                if (!TryMeasureManualStringTable(
                        table,
                        out TableKey liveKey,
                        out ContentFootprint liveFootprint,
                        out string liveMeasureError))
                {
                    ReportInvalidContent(liveMeasureError);
                    return;
                }
                if (!liveKey.Equals(key))
                {
                    ReportInvalidContent("String table authoring identity changed during registration.");
                    return;
                }
                if (HasCatalogStringKey(liveKey, null))
                {
                    ReportInvalidContent("String table ownership conflicts with a registered catalog.");
                    return;
                }

                ContentFootprint liveReplaced = GetManualStringFootprint(liveKey);
                if (!TryValidateResidentAdmission(liveFootprint, liveReplaced, out string admissionError))
                {
                    ReportInvalidContent(admissionError);
                    return;
                }

                CompiledStringTable compiled;
                try
                {
                    compiled = table.CompileForRegistration();
                }
                catch (Exception exception)
                {
                    ReportInvalidContent(exception.Message, exception);
                    return;
                }

                string validationError = null;
                if (!string.Equals(compiled.TableId, liveKey.TableId, StringComparison.Ordinal) ||
                    !string.Equals(compiled.LocaleId.Code, liveKey.LocaleCode, StringComparison.Ordinal) ||
                    !ValidateCompiledStringTable(compiled, out validationError))
                {
                    ReportInvalidContent(validationError ?? "String table authoring data changed during registration.");
                    return;
                }

                ContentFootprint actual = GetStringFootprint(compiled);
                if (!TryValidateResidentAdmission(actual, liveReplaced, out admissionError))
                {
                    ReportInvalidContent(admissionError);
                    return;
                }

                _manualStringTables[liveKey] = compiled;
                table.RetainCompiled(compiled);
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
                accepted = true;
            });
            return queued ? scheduled : accepted;
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
            if (!TryMeasureManualAssetTable(table, out TableKey key, out ContentFootprint footprint, out string error))
            {
                ReportInvalidContent(error);
                return false;
            }

            if (HasCatalogAssetKey(key, null))
            {
                ReportInvalidContent("Asset table ownership conflicts with a registered catalog.");
                return false;
            }
            ContentFootprint replaced = GetManualAssetFootprint(key);
            if (!TryValidateResidentAdmission(footprint, replaced, out error))
            {
                ReportInvalidContent(error);
                return false;
            }

            bool accepted = false;
            bool queued = _processingMutations;
            bool scheduled = ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized) return;
                if (!TryMeasureManualAssetTable(
                        table,
                        out TableKey liveKey,
                        out ContentFootprint liveFootprint,
                        out string liveMeasureError))
                {
                    ReportInvalidContent(liveMeasureError);
                    return;
                }
                if (!liveKey.Equals(key))
                {
                    ReportInvalidContent("Asset table authoring identity changed during registration.");
                    return;
                }
                if (HasCatalogAssetKey(liveKey, null))
                {
                    ReportInvalidContent("Asset table ownership conflicts with a registered catalog.");
                    return;
                }

                ContentFootprint liveReplaced = GetManualAssetFootprint(liveKey);
                if (!TryValidateResidentAdmission(liveFootprint, liveReplaced, out string admissionError))
                {
                    ReportInvalidContent(admissionError);
                    return;
                }

                CompiledAssetTable compiled;
                try
                {
                    compiled = table.CompileForRegistration();
                }
                catch (Exception exception)
                {
                    ReportInvalidContent(exception.Message, exception);
                    return;
                }

                string validationError = null;
                if (!string.Equals(compiled.TableId, liveKey.TableId, StringComparison.Ordinal) ||
                    !string.Equals(compiled.LocaleId.Code, liveKey.LocaleCode, StringComparison.Ordinal) ||
                    !ValidateCompiledAssetTable(compiled, out validationError))
                {
                    ReportInvalidContent(validationError ?? "Asset table authoring data changed during registration.");
                    return;
                }

                ContentFootprint actual = GetAssetFootprint(compiled);
                if (!TryValidateResidentAdmission(actual, liveReplaced, out admissionError))
                {
                    ReportInvalidContent(admissionError);
                    return;
                }

                _manualAssetTables[liveKey] = compiled;
                table.RetainCompiled(compiled);
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
                accepted = true;
            });
            return queued ? scheduled : accepted;
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
            if (!TryMeasureCatalogFootprint(ownerId, catalog, out ContentFootprint footprint, out string error))
            {
                ReportDiagnostic(new LocalizationDiagnostic(
                    LocalizationDiagnosticCode.InvalidCatalog,
                    LocalizationDiagnosticSeverity.Error,
                    error));
                return false;
            }

            ContentFootprint replaced = GetCatalogFootprint(ownerId);
            if (!TryValidateResidentAdmission(footprint, replaced, out error))
            {
                ReportDiagnostic(new LocalizationDiagnostic(
                    LocalizationDiagnosticCode.InvalidCatalog,
                    LocalizationDiagnosticSeverity.Error,
                    error));
                return false;
            }

            bool accepted = false;
            bool queued = _processingMutations;
            bool scheduled = ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized) return;
                if (!TryMeasureCatalogFootprint(
                        ownerId,
                        catalog,
                        out ContentFootprint liveFootprint,
                        out string liveMeasureError))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        liveMeasureError));
                    return;
                }
                ContentFootprint liveReplaced = GetCatalogFootprint(ownerId);
                if (!TryValidateResidentAdmission(liveFootprint, liveReplaced, out string admissionError))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        admissionError));
                    return;
                }

                if (!TryCompileCatalog(ownerId, catalog, out CatalogContent content, out string compileError))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        compileError));
                    return;
                }

                if (HasCatalogConflict(ownerId, content, out string conflict))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        conflict));
                    return;
                }

                if (!TryValidateResidentAdmission(content.Footprint, liveReplaced, out admissionError))
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.InvalidCatalog,
                        LocalizationDiagnosticSeverity.Error,
                        admissionError));
                    return;
                }

                if (_catalogs.TryGetValue(ownerId, out CatalogContent existing) &&
                    string.Equals(existing.ContentHash, content.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _catalogs[ownerId] = content;
                Commit(LocalizationChangeReason.ContentChanged, _currentLocale);
                accepted = true;
            });
            return queued ? scheduled : accepted;
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

        private bool TryMeasureManualStringTable(
            StringTable table,
            out TableKey key,
            out ContentFootprint footprint,
            out string error)
        {
            key = default;
            footprint = default;
            if (table == null) return Fail("String table is null.", out error);

            LocaleId localeId;
            try
            {
                localeId = table.LocaleId;
            }
            catch (Exception)
            {
                return Fail("String table locale code is invalid.", out error);
            }

            if (!ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                !localeId.IsValid || !_localeMap.ContainsKey(localeId.Code))
            {
                return Fail("String table identity exceeds configured bounds.", out error);
            }

            IReadOnlyList<StringEntry> entries = table.AuthoringEntries;
            if (entries == null || entries.Count > _limits.MaxEntriesPerTable)
                return Fail("String table entry count exceeds the configured limit.", out error);
            if ((long)entries.Count + 1L > _residentLimits.MaxCandidateNodes)
                return Fail("String table candidate-node count exceeds the configured work limit.", out error);

            long retainedEntries = 0L;
            long retainedCharacters = table.TableId.Length + localeId.Code.Length;
            if (retainedCharacters > _residentLimits.MaxRetainedCharacters)
                return Fail("String table retained characters exceed the resident limit.", out error);
            for (int index = 0; index < entries.Count; index++)
            {
                StringEntry entry = entries[index];
                if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength))
                    return Fail("A string table key is invalid or too long.", out error);
                if (entry.Value != null &&
                    (entry.Value.Length > _limits.MaxStringValueLength || !IsWellFormedUtf16(entry.Value)))
                    return Fail("A string table value exceeds configured bounds.", out error);
                if (string.IsNullOrWhiteSpace(entry.Value))
                    continue;
                if (!TryAddBudget(
                        ref retainedCharacters,
                        (long)entry.Key.Length + entry.Value.Length,
                        _residentLimits.MaxRetainedCharacters))
                {
                    return Fail("String table retained characters exceed the resident limit.", out error);
                }
                retainedEntries++;
                if (retainedEntries > _residentLimits.MaxEntries)
                    return Fail("String table retained entries exceed the resident limit.", out error);
            }

            key = new TableKey(table.TableId, localeId);
            footprint = new ContentFootprint(1, 1, retainedEntries, retainedCharacters);
            error = null;
            return true;
        }

        private bool TryMeasureManualAssetTable(
            AssetTable table,
            out TableKey key,
            out ContentFootprint footprint,
            out string error)
        {
            key = default;
            footprint = default;
            if (table == null) return Fail("Asset table is null.", out error);

            LocaleId localeId;
            try
            {
                localeId = table.LocaleId;
            }
            catch (Exception)
            {
                return Fail("Asset table locale code is invalid.", out error);
            }

            if (!ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                !localeId.IsValid || !_localeMap.ContainsKey(localeId.Code))
            {
                return Fail("Asset table identity exceeds configured bounds.", out error);
            }

            IReadOnlyList<AssetEntry> entries = table.AuthoringEntries;
            if (entries == null || entries.Count > _limits.MaxEntriesPerTable)
                return Fail("Asset table entry count exceeds the configured limit.", out error);
            if ((long)entries.Count + 1L > _residentLimits.MaxCandidateNodes)
                return Fail("Asset table candidate-node count exceeds the configured work limit.", out error);
            if (entries.Count > _residentLimits.MaxEntries)
                return Fail("Asset table retained entries exceed the resident limit.", out error);

            long retainedCharacters = table.TableId.Length + localeId.Code.Length;
            if (retainedCharacters > _residentLimits.MaxRetainedCharacters)
                return Fail("Asset table retained characters exceed the resident limit.", out error);
            for (int index = 0; index < entries.Count; index++)
            {
                AssetEntry entry = entries[index];
                if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength) || !entry.Asset.IsValid ||
                    entry.Asset.Location.Length > _limits.MaxAssetLocationLength ||
                    !IsWellFormedUtf16(entry.Asset.Location) ||
                    (entry.Asset.Guid != null &&
                     (entry.Asset.Guid.Length > _limits.MaxAssetLocationLength ||
                      !IsWellFormedUtf16(entry.Asset.Guid))))
                {
                    return Fail("An asset table entry exceeds configured bounds.", out error);
                }

                long characterCount = (long)entry.Key.Length + entry.Asset.Location.Length + LengthOf(entry.Asset.Guid);
                if (!TryAddBudget(
                        ref retainedCharacters,
                        characterCount,
                        _residentLimits.MaxRetainedCharacters))
                {
                    return Fail("Asset table retained characters exceed the resident limit.", out error);
                }
            }

            key = new TableKey(table.TableId, localeId);
            footprint = new ContentFootprint(1, 1, entries.Count, retainedCharacters);
            error = null;
            return true;
        }

        private bool TryMeasureCatalogFootprint(
            string ownerId,
            LocalizationCatalog catalog,
            out ContentFootprint footprint,
            out string error)
        {
            footprint = default;
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
            long tableCount = (long)stringTables.Count + assetTables.Count;
            if (tableCount > _limits.MaxCatalogTables || tableCount > LocalizationResidentLimits.AbsoluteMaxTables)
                return Fail("Catalog table count exceeds the configured limit.", out error);
            if (tableCount > _residentLimits.MaxTables)
                return Fail("Catalog table count exceeds the resident limit.", out error);
            if (tableCount > _residentLimits.MaxCandidateNodes)
                return Fail("Catalog candidate-node count exceeds the configured work limit.", out error);

            long totalRawEntries = 0L;
            long candidateNodes = tableCount;
            long minimumRetainedEntries = 0L;
            for (int tableIndex = 0; tableIndex < stringTables.Count; tableIndex++)
            {
                CatalogStringTable table = stringTables[tableIndex];
                if (table == null || table.Entries == null)
                    return Fail("A catalog string table is missing its entry collection.", out error);
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog string table exceeds the entry limit.", out error);
                if (!TryAddBudget(ref totalRawEntries, table.Entries.Count, _limits.MaxCatalogEntries) ||
                    !TryAddBudget(ref candidateNodes, table.Entries.Count, _residentLimits.MaxCandidateNodes))
                {
                    return Fail("Catalog candidate-node count exceeds a configured work limit.", out error);
                }
            }

            for (int tableIndex = 0; tableIndex < assetTables.Count; tableIndex++)
            {
                CatalogAssetTable table = assetTables[tableIndex];
                if (table == null || table.Entries == null)
                    return Fail("A catalog asset table is missing its entry collection.", out error);
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog asset table exceeds the entry limit.", out error);
                if (!TryAddBudget(ref totalRawEntries, table.Entries.Count, _limits.MaxCatalogEntries) ||
                    !TryAddBudget(ref candidateNodes, table.Entries.Count, _residentLimits.MaxCandidateNodes) ||
                    !TryAddBudget(ref minimumRetainedEntries, table.Entries.Count, _residentLimits.MaxEntries))
                {
                    return Fail("Catalog candidate content exceeds a configured entry or work limit.", out error);
                }
            }

            long retainedEntries = 0L;
            long retainedCharacters = 0L;
            long catalogTextCharacters = 0L;
            long catalogAssetReferenceCharacters = 0L;
            var stringIdentities = new HashSet<TableKey>();
            var assetIdentities = new HashSet<TableKey>();

            for (int tableIndex = 0; tableIndex < stringTables.Count; tableIndex++)
            {
                CatalogStringTable table = stringTables[tableIndex];
                LocaleId localeId;
                try
                {
                    localeId = table != null ? table.LocaleId : LocaleId.Invalid;
                }
                catch (Exception)
                {
                    return Fail("A catalog string table locale is invalid.", out error);
                }

                if (table == null || !ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                    !localeId.IsValid || !_localeMap.ContainsKey(localeId.Code) || table.Entries == null)
                {
                    return Fail("A catalog string table identity is invalid.", out error);
                }
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog string table exceeds the entry limit.", out error);
                var tableKey = new TableKey(table.TableId, localeId);
                if (!stringIdentities.Add(tableKey))
                    return Fail("Duplicate catalog string table identity.", out error);
                if (_manualStringTables.ContainsKey(tableKey) || HasCatalogStringKey(tableKey, ownerId))
                    return Fail("Catalog string table ownership conflicts with live content.", out error);
                if (!TryAddBudget(ref catalogTextCharacters, table.TableId.Length, _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(ref catalogTextCharacters, localeId.Code.Length, _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(
                        ref retainedCharacters,
                        (long)table.TableId.Length + localeId.Code.Length,
                        _residentLimits.MaxRetainedCharacters))
                {
                    return Fail("Catalog text exceeds a configured character budget.", out error);
                }

                for (int entryIndex = 0; entryIndex < table.Entries.Count; entryIndex++)
                {
                    CatalogStringEntry entry = table.Entries[entryIndex];
                    if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength) || entry.Value == null ||
                        entry.Value.Length > _limits.MaxStringValueLength || !IsWellFormedUtf16(entry.Value))
                    {
                        return Fail("A catalog string entry exceeds its configured bounds.", out error);
                    }
                    if (!TryAddBudget(ref catalogTextCharacters, entry.Key.Length, _limits.MaxCatalogTextCharacters) ||
                        !TryAddBudget(ref catalogTextCharacters, entry.Value.Length, _limits.MaxCatalogTextCharacters))
                    {
                        return Fail("Catalog text exceeds the aggregate character budget.", out error);
                    }
                    if (string.IsNullOrWhiteSpace(entry.Value))
                        continue;
                    if (!TryAddBudget(
                            ref retainedCharacters,
                            (long)entry.Key.Length + entry.Value.Length,
                            _residentLimits.MaxRetainedCharacters))
                    {
                        return Fail("Catalog retained characters exceed the resident limit.", out error);
                    }
                    retainedEntries++;
                    if (retainedEntries > _residentLimits.MaxEntries)
                        return Fail("Catalog retained entries exceed the resident limit.", out error);
                }
            }

            for (int tableIndex = 0; tableIndex < assetTables.Count; tableIndex++)
            {
                CatalogAssetTable table = assetTables[tableIndex];
                LocaleId localeId;
                try
                {
                    localeId = table != null ? table.LocaleId : LocaleId.Invalid;
                }
                catch (Exception)
                {
                    return Fail("A catalog asset table locale is invalid.", out error);
                }

                if (table == null || !ValidateIdentifier(table.TableId, _limits.MaxTableIdLength) ||
                    !localeId.IsValid || !_localeMap.ContainsKey(localeId.Code) || table.Entries == null)
                {
                    return Fail("A catalog asset table identity is invalid.", out error);
                }
                if (table.Entries.Count > _limits.MaxEntriesPerTable)
                    return Fail("A catalog asset table exceeds the entry limit.", out error);
                var tableKey = new TableKey(table.TableId, localeId);
                if (!assetIdentities.Add(tableKey))
                    return Fail("Duplicate catalog asset table identity.", out error);
                if (_manualAssetTables.ContainsKey(tableKey) || HasCatalogAssetKey(tableKey, ownerId))
                    return Fail("Catalog asset table ownership conflicts with live content.", out error);
                if (!TryAddBudget(ref catalogTextCharacters, table.TableId.Length, _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(ref catalogTextCharacters, localeId.Code.Length, _limits.MaxCatalogTextCharacters) ||
                    !TryAddBudget(
                        ref retainedCharacters,
                        (long)table.TableId.Length + localeId.Code.Length,
                        _residentLimits.MaxRetainedCharacters))
                {
                    return Fail("Catalog text exceeds a configured character budget.", out error);
                }

                for (int entryIndex = 0; entryIndex < table.Entries.Count; entryIndex++)
                {
                    CatalogAssetEntry entry = table.Entries[entryIndex];
                    if (!ValidateIdentifier(entry.Key, _limits.MaxEntryKeyLength) || !entry.Asset.IsValid ||
                        entry.Asset.Location.Length > _limits.MaxAssetLocationLength ||
                        !IsWellFormedUtf16(entry.Asset.Location) ||
                        (entry.Asset.Guid != null &&
                         (entry.Asset.Guid.Length > _limits.MaxAssetLocationLength ||
                          !IsWellFormedUtf16(entry.Asset.Guid))))
                    {
                        return Fail("A catalog asset entry exceeds its configured bounds.", out error);
                    }
                    if (!TryAddBudget(ref catalogTextCharacters, entry.Key.Length, _limits.MaxCatalogTextCharacters) ||
                        !TryAddBudget(
                            ref catalogAssetReferenceCharacters,
                            entry.Asset.Location.Length,
                            _limits.MaxCatalogAssetReferenceCharacters) ||
                        !TryAddBudget(
                            ref catalogAssetReferenceCharacters,
                            LengthOf(entry.Asset.Guid),
                            _limits.MaxCatalogAssetReferenceCharacters) ||
                        !TryAddBudget(
                            ref retainedCharacters,
                            (long)entry.Key.Length + entry.Asset.Location.Length + LengthOf(entry.Asset.Guid),
                            _residentLimits.MaxRetainedCharacters))
                    {
                        return Fail("Catalog content exceeds an aggregate character budget.", out error);
                    }
                    retainedEntries++;
                    if (retainedEntries > _residentLimits.MaxEntries)
                        return Fail("Catalog retained entries exceed the resident limit.", out error);
                }
            }

            footprint = new ContentFootprint(1, (int)tableCount, retainedEntries, retainedCharacters);
            error = null;
            return true;
        }

        private ContentFootprint GetManualStringFootprint(TableKey key)
        {
            return _manualStringTables.TryGetValue(key, out CompiledStringTable table)
                ? GetStringFootprint(table)
                : default;
        }

        private ContentFootprint GetManualAssetFootprint(TableKey key)
        {
            return _manualAssetTables.TryGetValue(key, out CompiledAssetTable table)
                ? GetAssetFootprint(table)
                : default;
        }

        private ContentFootprint GetCatalogFootprint(string ownerId)
        {
            return _catalogs.TryGetValue(ownerId, out CatalogContent content)
                ? content.Footprint
                : default;
        }

        private static ContentFootprint GetStringFootprint(CompiledStringTable table)
        {
            return new ContentFootprint(1, 1, table.Count, table.RetainedCharacterCount);
        }

        private static ContentFootprint GetAssetFootprint(CompiledAssetTable table)
        {
            return new ContentFootprint(1, 1, table.Count, table.RetainedReferenceCharacterCount);
        }

        private bool TryValidateResidentAdmission(
            ContentFootprint candidate,
            ContentFootprint replaced,
            out string error)
        {
            ContentMemoryStats current = ReadSnapshot().ContentMemoryStats;
            if (!Fits(
                    current.ResidentOwnerCount,
                    replaced.OwnerCount,
                    candidate.OwnerCount,
                    _residentLimits.MaxOwners))
            {
                return Fail("Localization resident owner capacity is exhausted.", out error);
            }
            if (!Fits(
                    current.ResidentTableCount,
                    replaced.TableCount,
                    candidate.TableCount,
                    _residentLimits.MaxTables))
            {
                return Fail("Localization resident table capacity is exhausted.", out error);
            }
            if (!Fits(
                    current.ResidentEntryCount,
                    replaced.EntryCount,
                    candidate.EntryCount,
                    _residentLimits.MaxEntries))
            {
                return Fail("Localization resident entry capacity is exhausted.", out error);
            }
            if (!Fits(
                    current.RetainedCharacterCount,
                    replaced.RetainedCharacterCount,
                    candidate.RetainedCharacterCount,
                    _residentLimits.MaxRetainedCharacters))
            {
                return Fail("Localization retained-character capacity is exhausted.", out error);
            }

            error = null;
            return true;
        }

        private static bool Fits(int current, int replaced, int candidate, int maximum)
        {
            return replaced >= 0 && replaced <= current && candidate >= 0 && current - replaced <= maximum - candidate;
        }

        private static bool Fits(long current, long replaced, long candidate, long maximum)
        {
            return replaced >= 0L && replaced <= current && candidate >= 0L && current - replaced <= maximum - candidate;
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

            int compiledTableCount = strings.Count + assets.Count;
            long compiledEntryCount = 0L;
            long compiledCharacterCount = 0L;
            foreach (CompiledStringTable table in strings.Values)
            {
                compiledEntryCount += table.Count;
                compiledCharacterCount += table.RetainedCharacterCount;
            }
            foreach (CompiledAssetTable table in assets.Values)
            {
                compiledEntryCount += table.Count;
                compiledCharacterCount += table.RetainedReferenceCharacterCount;
            }

            content = new CatalogContent(
                strings,
                assets,
                computedHash,
                new ContentFootprint(1, compiledTableCount, compiledEntryCount, compiledCharacterCount));
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

        private static bool TryAddBudget(ref long total, long amount, long maximum)
        {
            if (amount < 0L || total > maximum - amount) return false;
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
