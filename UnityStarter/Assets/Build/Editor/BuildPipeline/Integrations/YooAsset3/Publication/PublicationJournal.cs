using System;
using System.Globalization;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    [Serializable]
    internal sealed class PublicationJournalOperation
    {
        public string kind;
        public string packageName;
        public string packageVersion;
        public string cryptographyAdapterId;
        public string runtimeDecryptContractId;
        public string approvedRoot;
        public string target;
        public string stage;
        public string backup;
        public bool targetInitiallyExisted;
        public bool originalWasOwned;
        public string originalTransactionId;
        public string originalPackageVersion;
        public string originalCryptographyAdapterId;
        public string originalRuntimeDecryptContractId;
        public string originalContentIdentity;
        public int originalEntryCount;
        public string installedContentIdentity;
        public int installedEntryCount;
        public bool managesSiblingMeta;
        public string targetMeta;
        public string protectedMeta;
        public bool originalMetaExisted;
        public long originalMetaLength;
        public string originalMetaSha256;
        public bool installedMetaExisted;
        public long installedMetaLength;
        public string installedMetaSha256;
        public string state;
    }

    internal sealed class PackagePublication
    {
        public PackagePublication(
            PublicationJournalOperation outputOperation,
            PublicationJournalOperation bundledOperation,
            string bundledWorkDirectory)
        {
            OutputOperation = outputOperation;
            BundledOperation = bundledOperation;
            BundledWorkDirectory = bundledWorkDirectory ?? string.Empty;
        }

        public PublicationJournalOperation OutputOperation { get; }
        public PublicationJournalOperation BundledOperation { get; }
        public string BundledWorkDirectory { get; }
    }

    [Serializable]
    internal sealed class PublicationJournal
    {
        public string documentType;
        public long sequence;
        public string invocationId;
        public string transactionId;
        public string phase;
        public string projectRoot;
        public string buildOutputRoot;
        public string bundledFileRoot;
        public string workRoot;
        public PublicationJournalOperation[] operations;
        public string checksum;
    }

    internal readonly struct MetaFileSnapshot
    {
        public static readonly MetaFileSnapshot Missing = new MetaFileSnapshot(false, 0, string.Empty);

        public MetaFileSnapshot(bool exists, long length, string sha256)
        {
            Exists = exists;
            Length = length;
            Sha256 = sha256 ?? string.Empty;
        }

        public bool Exists { get; }
        public long Length { get; }
        public string Sha256 { get; }
    }

    internal readonly struct CopyDirectoryEntry
    {
        public CopyDirectoryEntry(string source, string destination, int depth)
        {
            Source = source;
            Destination = destination;
            Depth = depth;
        }

        public string Source { get; }
        public string Destination { get; }
        public int Depth { get; }
    }

    internal readonly struct SourceQualificationPaths
    {
        internal SourceQualificationPaths(
            string operationRoot,
            string installedDirectory,
            string installedMeta,
            string originalMeta)
        {
            OperationRoot = operationRoot;
            InstalledDirectory = installedDirectory;
            InstalledMeta = installedMeta;
            OriginalMeta = originalMeta;
        }

        internal string OperationRoot { get; }
        internal string InstalledDirectory { get; }
        internal string InstalledMeta { get; }
        internal string OriginalMeta { get; }
    }
}
