namespace CycloneGames.Localization.Runtime
{
    /// <summary>Allocation-free retained-content and capacity snapshot for one localization service.</summary>
    public readonly struct LocalizationMemorySnapshot
    {
        public LocalizationMemorySnapshot(
            bool isInitialized,
            long revision,
            int availableLocaleCount,
            int currentFallbackDepth,
            int catalogOwnerCount,
            int manualStringTableCount,
            int manualAssetTableCount,
            int stringTableCount,
            long stringEntryCount,
            long stringCharacterCount,
            int assetTableCount,
            long assetEntryCount,
            long assetReferenceCharacterCount,
            int metadataTableCount,
            long metadataEntryCount,
            int reportedMissingDiagnosticCount,
            int changeHandlerCount,
            LocalizationLimits limits)
            : this(
                isInitialized,
                revision,
                availableLocaleCount,
                currentFallbackDepth,
                catalogOwnerCount,
                manualStringTableCount,
                manualAssetTableCount,
                stringTableCount,
                stringEntryCount,
                stringCharacterCount,
                assetTableCount,
                assetEntryCount,
                assetReferenceCharacterCount,
                metadataTableCount,
                metadataEntryCount,
                reportedMissingDiagnosticCount,
                changeHandlerCount,
                limits,
                LocalizationResidentLimits.Default)
        {
        }

        public LocalizationMemorySnapshot(
            bool isInitialized,
            long revision,
            int availableLocaleCount,
            int currentFallbackDepth,
            int catalogOwnerCount,
            int manualStringTableCount,
            int manualAssetTableCount,
            int stringTableCount,
            long stringEntryCount,
            long stringCharacterCount,
            int assetTableCount,
            long assetEntryCount,
            long assetReferenceCharacterCount,
            int metadataTableCount,
            long metadataEntryCount,
            int reportedMissingDiagnosticCount,
            int changeHandlerCount,
            LocalizationLimits limits,
            LocalizationResidentLimits residentLimits)
        {
            IsInitialized = isInitialized;
            Revision = revision;
            AvailableLocaleCount = availableLocaleCount;
            CurrentFallbackDepth = currentFallbackDepth;
            CatalogOwnerCount = catalogOwnerCount;
            ManualStringTableCount = manualStringTableCount;
            ManualAssetTableCount = manualAssetTableCount;
            StringTableCount = stringTableCount;
            StringEntryCount = stringEntryCount;
            StringCharacterCount = stringCharacterCount;
            AssetTableCount = assetTableCount;
            AssetEntryCount = assetEntryCount;
            AssetReferenceCharacterCount = assetReferenceCharacterCount;
            MetadataTableCount = metadataTableCount;
            MetadataEntryCount = metadataEntryCount;
            ReportedMissingDiagnosticCount = reportedMissingDiagnosticCount;
            ChangeHandlerCount = changeHandlerCount;
            Limits = limits;
            ResidentLimits = residentLimits;
        }

        public bool IsInitialized { get; }
        public long Revision { get; }
        public int AvailableLocaleCount { get; }
        public int CurrentFallbackDepth { get; }
        public int CatalogOwnerCount { get; }
        public int ManualStringTableCount { get; }
        public int ManualAssetTableCount { get; }
        public int StringTableCount { get; }
        public long StringEntryCount { get; }
        public long StringCharacterCount { get; }
        public int AssetTableCount { get; }
        public long AssetEntryCount { get; }
        public long AssetReferenceCharacterCount { get; }
        public int MetadataTableCount { get; }
        public long MetadataEntryCount { get; }
        public int ReportedMissingDiagnosticCount { get; }
        public int ChangeHandlerCount { get; }
        public LocalizationLimits Limits { get; }
        public int ResidentOwnerCount => CatalogOwnerCount + ManualStringTableCount + ManualAssetTableCount;
        public int ResidentTableCount => StringTableCount + AssetTableCount;
        public long ResidentEntryCount => StringEntryCount + AssetEntryCount;
        public long RetainedCharacterCount => StringCharacterCount + AssetReferenceCharacterCount;
        public LocalizationResidentLimits ResidentLimits { get; }
    }

    /// <summary>Result of a bounded clear of reconstructible missing-key diagnostic dedupe entries.</summary>
    public readonly struct LocalizationDiagnosticTrimResult
    {
        public LocalizationDiagnosticTrimResult(int removedCount, bool hasMoreEntries)
        {
            RemovedCount = removedCount;
            HasMoreEntries = hasMoreEntries;
        }

        public int RemovedCount { get; }
        public bool HasMoreEntries { get; }
    }
}
