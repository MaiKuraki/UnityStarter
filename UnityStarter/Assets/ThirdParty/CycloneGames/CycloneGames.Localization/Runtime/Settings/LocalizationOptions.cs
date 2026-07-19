using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    public enum LocalizationDiagnosticSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public enum LocalizationDiagnosticCode : byte
    {
        MissingKey = 0,
        InvalidCatalog = 1,
        MutationQueueFull = 2,
        SubscriberException = 3,
        DiagnosticSinkException = 4,
        InvalidContent = 5,
        FormatError = 6,
    }

    public readonly struct LocalizationDiagnostic
    {
        public LocalizationDiagnostic(
            LocalizationDiagnosticCode code,
            LocalizationDiagnosticSeverity severity,
            string message,
            Exception exception = null)
        {
            Code = code;
            Severity = severity;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public LocalizationDiagnosticCode Code { get; }
        public LocalizationDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Hard limits applied before localization content is copied into live memory.
    /// </summary>
    public readonly struct LocalizationLimits
    {
        public const int DefaultMaxAvailableLocales = 128;
        public const int DefaultMaxFallbackLocales = 32;
        public const int DefaultMaxCatalogTables = 2048;
        public const int DefaultMaxEntriesPerTable = 100000;
        public const int DefaultMaxCatalogEntries = 1000000;
        public const int DefaultMaxTableIdLength = 128;
        public const int DefaultMaxEntryKeyLength = 256;
        public const int DefaultMaxStringValueLength = 65536;
        public const int DefaultMaxAssetLocationLength = 1024;
        public const int DefaultMaxCatalogOwnerIdLength = 128;
        public const int DefaultMaxMissingDiagnostics = 1024;
        public const int DefaultMaxQueuedMutations = 64;
        public const long DefaultMaxCatalogTextCharacters = 16000000L;
        public const long DefaultMaxCatalogAssetReferenceCharacters = 4000000L;

        private readonly byte _configured;

        public LocalizationLimits(
            int maxAvailableLocales = DefaultMaxAvailableLocales,
            int maxFallbackLocales = DefaultMaxFallbackLocales,
            int maxCatalogTables = DefaultMaxCatalogTables,
            int maxEntriesPerTable = DefaultMaxEntriesPerTable,
            int maxCatalogEntries = DefaultMaxCatalogEntries,
            int maxTableIdLength = DefaultMaxTableIdLength,
            int maxEntryKeyLength = DefaultMaxEntryKeyLength,
            int maxStringValueLength = DefaultMaxStringValueLength,
            int maxAssetLocationLength = DefaultMaxAssetLocationLength,
            int maxCatalogOwnerIdLength = DefaultMaxCatalogOwnerIdLength,
            int maxMissingDiagnostics = DefaultMaxMissingDiagnostics,
            int maxQueuedMutations = DefaultMaxQueuedMutations,
            long maxCatalogTextCharacters = DefaultMaxCatalogTextCharacters,
            long maxCatalogAssetReferenceCharacters = DefaultMaxCatalogAssetReferenceCharacters)
        {
            MaxAvailableLocales = RequirePositive(maxAvailableLocales, nameof(maxAvailableLocales));
            MaxFallbackLocales = RequirePositive(maxFallbackLocales, nameof(maxFallbackLocales));
            MaxCatalogTables = RequirePositive(maxCatalogTables, nameof(maxCatalogTables));
            MaxEntriesPerTable = RequirePositive(maxEntriesPerTable, nameof(maxEntriesPerTable));
            MaxCatalogEntries = RequirePositive(maxCatalogEntries, nameof(maxCatalogEntries));
            MaxTableIdLength = RequirePositive(maxTableIdLength, nameof(maxTableIdLength));
            MaxEntryKeyLength = RequirePositive(maxEntryKeyLength, nameof(maxEntryKeyLength));
            MaxStringValueLength = RequirePositive(maxStringValueLength, nameof(maxStringValueLength));
            MaxAssetLocationLength = RequirePositive(maxAssetLocationLength, nameof(maxAssetLocationLength));
            MaxCatalogOwnerIdLength = RequirePositive(maxCatalogOwnerIdLength, nameof(maxCatalogOwnerIdLength));
            MaxMissingDiagnostics = RequirePositive(maxMissingDiagnostics, nameof(maxMissingDiagnostics));
            MaxQueuedMutations = RequirePositive(maxQueuedMutations, nameof(maxQueuedMutations));
            MaxCatalogTextCharacters = RequirePositive(
                maxCatalogTextCharacters,
                nameof(maxCatalogTextCharacters));
            MaxCatalogAssetReferenceCharacters = RequirePositive(
                maxCatalogAssetReferenceCharacters,
                nameof(maxCatalogAssetReferenceCharacters));
            _configured = 1;
        }

        public int MaxAvailableLocales { get; }
        public int MaxFallbackLocales { get; }
        public int MaxCatalogTables { get; }
        public int MaxEntriesPerTable { get; }
        public int MaxCatalogEntries { get; }
        public int MaxTableIdLength { get; }
        public int MaxEntryKeyLength { get; }
        public int MaxStringValueLength { get; }
        public int MaxAssetLocationLength { get; }
        public int MaxCatalogOwnerIdLength { get; }
        public int MaxMissingDiagnostics { get; }
        public int MaxQueuedMutations { get; }
        public long MaxCatalogTextCharacters { get; }
        public long MaxCatalogAssetReferenceCharacters { get; }

        public static LocalizationLimits Default => new LocalizationLimits(
            DefaultMaxAvailableLocales,
            DefaultMaxFallbackLocales,
            DefaultMaxCatalogTables,
            DefaultMaxEntriesPerTable,
            DefaultMaxCatalogEntries,
            DefaultMaxTableIdLength,
            DefaultMaxEntryKeyLength,
            DefaultMaxStringValueLength,
            DefaultMaxAssetLocationLength,
            DefaultMaxCatalogOwnerIdLength,
            DefaultMaxMissingDiagnostics,
            DefaultMaxQueuedMutations,
            DefaultMaxCatalogTextCharacters,
            DefaultMaxCatalogAssetReferenceCharacters);

        internal LocalizationLimits Normalized()
        {
            return _configured == 0 ? Default : this;
        }

        private static int RequirePositive(int value, string parameterName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "Localization limits must be positive.");
            return value;
        }

        private static long RequirePositive(long value, string parameterName)
        {
            if (value <= 0L)
                throw new ArgumentOutOfRangeException(parameterName, "Localization limits must be positive.");
            return value;
        }
    }

    /// <summary>
    /// Immutable initialization configuration. Input collections are copied by the constructor.
    /// </summary>
    public readonly struct LocalizationOptions
    {
        private readonly ReadOnlyCollection<Locale> _availableLocales;
        private readonly ReadOnlyCollection<ILocaleSelector> _localeSelectors;

        public LocalizationOptions(
            Locale defaultLocale,
            IReadOnlyList<Locale> availableLocales,
            bool detectSystemLanguage = true,
            IReadOnlyList<ILocaleSelector> localeSelectors = null,
            PseudoLocaleMode pseudoMode = PseudoLocaleMode.None,
            Action<LocalizationDiagnostic> diagnosticSink = null,
            IFormatProvider formatProvider = null,
            LocalizationLimits limits = default)
        {
            DefaultLocale = defaultLocale;
            _availableLocales = Copy(availableLocales);
            DetectSystemLanguage = detectSystemLanguage;
            _localeSelectors = localeSelectors == null ? null : Copy(localeSelectors);
            PseudoMode = pseudoMode;
            DiagnosticSink = diagnosticSink;
            FormatProvider = formatProvider ?? CultureInfo.InvariantCulture;
            Limits = limits.Normalized();
        }

        public Locale DefaultLocale { get; }
        public IReadOnlyList<Locale> AvailableLocales =>
            _availableLocales != null ? (IReadOnlyList<Locale>)_availableLocales : Array.Empty<Locale>();
        public bool DetectSystemLanguage { get; }
        public IReadOnlyList<ILocaleSelector> LocaleSelectors => _localeSelectors;
        public PseudoLocaleMode PseudoMode { get; }
        public Action<LocalizationDiagnostic> DiagnosticSink { get; }
        public IFormatProvider FormatProvider { get; }
        public LocalizationLimits Limits { get; }

        private static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.AsReadOnly(Array.Empty<T>());

            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = source[i];
            return Array.AsReadOnly(copy);
        }
    }
}
