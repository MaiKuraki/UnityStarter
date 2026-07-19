using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    public sealed partial class LocalizationService
    {
        private enum Lifecycle : byte
        {
            Created = 0,
            Initialized = 1,
            Shutdown = 2,
            Disposed = 3,
        }

        private readonly struct TableKey : IEquatable<TableKey>
        {
            public TableKey(string tableId, LocaleId localeId)
            {
                TableId = tableId;
                LocaleCode = localeId.Code;
            }

            public string TableId { get; }
            public string LocaleCode { get; }

            public bool Equals(TableKey other)
            {
                return string.Equals(TableId, other.TableId, StringComparison.Ordinal) &&
                       string.Equals(LocaleCode, other.LocaleCode, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is TableKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((TableId != null ? StringComparer.Ordinal.GetHashCode(TableId) : 0) * 397) ^
                           (LocaleCode != null ? StringComparer.Ordinal.GetHashCode(LocaleCode) : 0);
                }
            }
        }

        private sealed class CatalogContent
        {
            public CatalogContent(
                Dictionary<TableKey, CompiledStringTable> stringTables,
                Dictionary<TableKey, CompiledAssetTable> assetTables,
                string contentHash)
            {
                StringTables = stringTables;
                AssetTables = assetTables;
                ContentHash = contentHash;
            }

            public Dictionary<TableKey, CompiledStringTable> StringTables { get; }
            public Dictionary<TableKey, CompiledAssetTable> AssetTables { get; }
            public string ContentHash { get; }
        }

        private sealed class Snapshot
        {
            private static readonly ReadOnlyCollection<LocaleId> s_EmptyLocales =
                Array.AsReadOnly(Array.Empty<LocaleId>());

            public static readonly Snapshot Empty = CreateStopped(
                0,
                null,
                CultureInfo.InvariantCulture,
                LocalizationLimits.Default);

            public Snapshot(
                bool isInitialized,
                LocaleId currentLocale,
                LocaleId[] currentChain,
                ReadOnlyCollection<LocaleId> availableLocales,
                PseudoLocaleMode pseudoMode,
                long revision,
                Dictionary<string, Dictionary<string, CompiledStringTable>> stringTables,
                Dictionary<string, Dictionary<string, CompiledAssetTable>> assetTables,
                Dictionary<string, Dictionary<string, int>> metadata,
                Action<LocalizationDiagnostic> diagnosticSink,
                IFormatProvider formatProvider,
                LocalizationLimits limits)
            {
                IsInitialized = isInitialized;
                CurrentLocale = currentLocale;
                CurrentChain = currentChain;
                AvailableLocales = availableLocales;
                PseudoMode = pseudoMode;
                Revision = revision;
                StringTables = stringTables;
                AssetTables = assetTables;
                Metadata = metadata;
                DiagnosticSink = diagnosticSink;
                FormatProvider = formatProvider ?? CultureInfo.InvariantCulture;
                Limits = limits.Normalized();
            }

            public bool IsInitialized { get; }
            public LocaleId CurrentLocale { get; }
            public LocaleId[] CurrentChain { get; }
            public ReadOnlyCollection<LocaleId> AvailableLocales { get; }
            public PseudoLocaleMode PseudoMode { get; }
            public long Revision { get; }
            public Dictionary<string, Dictionary<string, CompiledStringTable>> StringTables { get; }
            public Dictionary<string, Dictionary<string, CompiledAssetTable>> AssetTables { get; }
            public Dictionary<string, Dictionary<string, int>> Metadata { get; }
            public Action<LocalizationDiagnostic> DiagnosticSink { get; }
            public IFormatProvider FormatProvider { get; }
            public LocalizationLimits Limits { get; }

            public static Snapshot CreateStopped(
                long revision,
                Action<LocalizationDiagnostic> diagnosticSink,
                IFormatProvider formatProvider,
                LocalizationLimits limits)
            {
                return new Snapshot(
                    false,
                    LocaleId.Invalid,
                    Array.Empty<LocaleId>(),
                    s_EmptyLocales,
                    PseudoLocaleMode.None,
                    revision,
                    new Dictionary<string, Dictionary<string, CompiledStringTable>>(StringComparer.Ordinal),
                    new Dictionary<string, Dictionary<string, CompiledAssetTable>>(StringComparer.Ordinal),
                    new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal),
                    diagnosticSink,
                    formatProvider,
                    limits);
            }
        }
    }
}

